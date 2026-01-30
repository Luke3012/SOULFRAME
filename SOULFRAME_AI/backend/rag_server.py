"""
SOULFRAME - RAG Server (FastAPI)

Cosa fa:
- Memoria per avatar con ChromaDB (persistente, per-avatar DB)
- Embedding via Ollama (/api/embed)
- Chat via Ollama (/api/chat) con RAG retrieval e deduplicazione
- Ingest di file: PDF (con OCR sempre attivo), immagini (OCR), testo
- Descrizione immagini con Gemini Vision (opzionale)
- Pulizia testo intelligente e rimozione garbage
- Gestione lock/handle Windows con stop system Chroma
- Endpoint di salute e clear avatar (soft/hard)
- Ricerca ibrida: BM25 keyword matching + vector similarity (60/40)
- Linearizzazione testo tabelle per semantic similarity migliore
- Debug endpoint per test OCR su singole pagine PDF

Endpoints principali:
- GET /health: stato servizio
- POST /remember: salva un testo con embedding
- POST /recall: ritrova documenti (ricerca ibrida BM25+semantic)
- POST /chat: chat con context RAG e hybrid search
- POST /ingest_file: importa PDF/immagini/testo con deduplicazione
- POST /describe_image: descrizione con Gemini Vision
- POST /clear_avatar: cancella memoria di un avatar (soft/hard)
- POST /debug_pdf_ocr: DEBUG - test OCR su pagina specifica

Note pratiche:
- PDF: usa sempre OCR (600 DPI) - testo embedded spesso corrotto
- Tabelle OCR: linearizzate per miglior embedding semantico
- Deduplicazione: rimuove chunk duplicati (similarity > 92%) durante ingest e recall
- Garbage filtering: scarta solo testo REALMENTE vuoto/inutile, lascia decision all'LLM
- Ricerca ibrida: 60% BM25 (keyword), 40% vector (semantic) - più match su parole chiave
- Windows: gestisce lock file Chroma tramite stop system e rmtree robusto
- Se memoria piena di "spazzatura": svuota con /clear_avatar e re-ingest
- OCR: italiano+inglese configurabile (RAG_OCR_LANG)
"""

from __future__ import annotations

import os
import re
import io
import difflib
import uuid
import time
import gc
import stat
import shutil
import threading
import traceback
from typing import Any, Optional, List, Tuple

import requests
import chromadb
from chromadb.config import Settings
from fastapi import FastAPI, UploadFile, File, Form, HTTPException, Request
from rank_bm25 import BM25Okapi
from fastapi.responses import JSONResponse
from pydantic import BaseModel

# Patch hnswlib compatibility (chroma expects file_handle_count on Index)
try:
    import hnswlib  # type: ignore
    if not hasattr(hnswlib.Index, "file_handle_count"):
        hnswlib.Index.file_handle_count = 0  # type: ignore[attr-defined]
except Exception:
    pass

# Optional Gemini Vision (nuovo SDK)
try:
    from google import genai
    from google.genai import types as genai_types
    print("[OK] google.genai importato")
except Exception as e:
    print(f"[ERR] Errore import google.genai: {e}")
    genai = None  # type: ignore
    genai_types = None  # type: ignore
try:
    from PIL import Image
except Exception:
    Image = None  # type: ignore
try:
    import pytesseract
except Exception:
    pytesseract = None  # type: ignore
try:
    import fitz  # PyMuPDF
except Exception:
    fitz = None  # type: ignore


# -----------------------------
# Config (env)
# -----------------------------
OLLAMA_HOST = os.getenv("OLLAMA_HOST", "http://127.0.0.1:11434").rstrip("/")
EMBED_MODEL = os.getenv("EMBED_MODEL", "nomic-embed-text")
CHAT_MODEL = os.getenv("CHAT_MODEL", "llama3:8b-instruct-q4_K_M")

PERSIST_ROOT = os.getenv("RAG_DIR", os.path.join(os.path.dirname(__file__), "rag_store"))
PERSIST_ROOT = PERSIST_ROOT.strip().strip('"')
PERSIST_ROOT = os.path.abspath(os.path.normpath(PERSIST_ROOT))
os.makedirs(PERSIST_ROOT, exist_ok=True)

CHUNK_CHARS = int(os.getenv("RAG_CHUNK_CHARS", "3500"))
CHUNK_OVERLAP = int(os.getenv("RAG_CHUNK_OVERLAP", "500"))
MIN_CHUNK_CHARS = int(os.getenv("RAG_MIN_CHUNK_CHARS", "20"))
MAX_CONTEXT_CHARS = int(os.getenv("RAG_MAX_CONTEXT_CHARS", "6000"))

# OCR / Tesseract
RAG_OCR_LANG = os.getenv("RAG_OCR_LANG", "ita+eng").strip()          # es: "ita" oppure "ita+eng"
TESSERACT_CMD = os.getenv("TESSERACT_CMD", "").strip().strip('"')
DEFAULT_TESSERACT_CMD = r"C:\Program Files\Tesseract-OCR\tesseract.exe"
OCR_DPI = int(os.getenv("RAG_OCR_DPI", "400"))  # Aumentato da 300 a 400 per tabelle

# Gemini Vision (opzionale)
GEMINI_API_KEY = os.getenv("GEMINI_API_KEY", "").strip()
# Se non trovata in env, prova a leggerla da file
if not GEMINI_API_KEY:
    # Prova percorsi multiple
    potential_paths = [
        os.path.join(os.path.dirname(__file__), "gemini_key.txt"),
        os.path.join(os.path.dirname(__file__), "..", "gemini_key.txt"),
        os.path.join(os.getcwd(), "gemini_key.txt"),
    ]
    for gemini_key_file in potential_paths:
        if os.path.exists(gemini_key_file):
            try:
                with open(gemini_key_file, "r") as f:
                    GEMINI_API_KEY = f.read().strip()
                print(f"[OK] Caricato GEMINI_API_KEY da: {gemini_key_file}")
                break
            except Exception as e:
                print(f"[ERR] Errore lettura {gemini_key_file}: {e}")

_gemini_client = None
if GEMINI_API_KEY and genai is not None:
    try:
        _gemini_client = genai.Client(api_key=GEMINI_API_KEY)
    except Exception as e:
        print(f"[ERR] Errore inizializzazione google.genai: {e}")
        _gemini_client = None
elif not GEMINI_API_KEY:
    print("[WARN] GEMINI_API_KEY non trovata in env né in file gemini_key.txt")


# -----------------------------
# Helpers: cleaning + quality
# -----------------------------
# rimuove caratteri di controllo (ma mantiene tab/newline)
_CTRL_RE = re.compile(r"[\x00-\x08\x0B\x0C\x0E-\x1F\x7F]")
_WS_RE = re.compile(r"[ \t]+")


def clean_text(s: str, ocr: bool = False) -> str:
    """Normalizza, opzionalmente filtra OCR (pagine, matricole, righe rumorose)."""
    if not s:
        return ""
    s = s.replace("\r\n", "\n").replace("\r", "\n")
    s = _CTRL_RE.sub("", s)
    s = _WS_RE.sub(" ", s)
    s = re.sub(r"\n{3,}", "\n\n", s)
    # Nota: il filtro OCR è stato rimosso perché troppo aggressivo per documenti reali
    return s.strip()


def looks_like_garbage(text: str) -> bool:
    """Scarta solo testo REALMENTE garbage: quasi vuoto o zero parole.
    Permette TUTTO il resto (tabelle, numeri, etc) così che l'LLM possa decidere.
    """
    t = (text or "").strip()
    # Minimo assoluto: testo deve avere almeno 20 caratteri
    if len(t) < 20:
        return True
    
    # Deve avere almeno 2 parole alfabetiche (altrimenti è lista di numeri o simboli)
    sample = t[:2000]
    words = re.findall(r"[A-Za-z]{2,}", sample)
    if len(words) < 2:
        return True
    
    # Tutto il resto è accettato (tabelle, numeri, testo denso, etc)
    return False


# -----------------------------
# Ollama helpers
# -----------------------------

def _post_json(url: str, payload: dict, timeout: int):
    try:
        r = requests.post(url, json=payload, timeout=timeout)
        r.raise_for_status()
        return r.json()
    except requests.exceptions.RequestException as e:
        raise HTTPException(status_code=502, detail=f"Ollama non raggiungibile o errore HTTP: {e}")


def ollama_embed_many(texts: List[str]) -> List[List[float]]:
    """Embeddings di 1 o N testi usando Ollama /api/embed."""
    if not texts:
        return []

    payload: dict[str, Any] = {
        "model": EMBED_MODEL,
        "input": texts if len(texts) > 1 else texts[0],
    }
    data = _post_json(f"{OLLAMA_HOST}/api/embed", payload, timeout=180)

    if "embeddings" in data and isinstance(data["embeddings"], list):
        if data["embeddings"] and isinstance(data["embeddings"][0], list):
            embs = data["embeddings"]
            _validate_embeddings(embs)
            return embs
        if all(isinstance(x, (int, float)) for x in data["embeddings"]):
            embs = [data["embeddings"]]  # type: ignore
            _validate_embeddings(embs)
            return embs

    if "embedding" in data and isinstance(data["embedding"], list):
        embs = [data["embedding"]]  # type: ignore
        _validate_embeddings(embs)
        return embs

    raise RuntimeError(f"Unexpected embed response: {data}")


def _validate_embeddings(embs: List[List[float]]) -> None:
    if not embs:
        raise HTTPException(status_code=500, detail="Embedding vuoti.")
    dim = len(embs[0])
    for e in embs:
        if len(e) != dim or any(v != v or v in (float('inf'), float('-inf')) for v in e):
            raise HTTPException(status_code=500, detail="Embedding invalido (dim/NaN/Inf).")


def ollama_chat(messages: list[dict[str, str]]) -> str:
    data = _post_json(
        f"{OLLAMA_HOST}/api/chat",
        {"model": CHAT_MODEL, "messages": messages, "stream": False},
        timeout=600,
    )
    return (data.get("message") or {}).get("content", "") or ""


# -----------------------------
# Chroma init (per-avatar persist dir)
# -----------------------------

_AVATAR_CLIENTS: dict[str, chromadb.PersistentClient] = {}
_AVATAR_LOCK = threading.Lock()

def _safe_avatar_key(avatar_id: str) -> str:
    s = (avatar_id or "default").strip()
    # solo caratteri sicuri per nomi cartelle su Windows
    s = re.sub(r"[^a-zA-Z0-9_-]+", "_", s)
    if not s:
        s = "default"
    return s[:64]

def _avatar_persist_dir(avatar_id: str) -> str:
    return os.path.join(PERSIST_ROOT, _safe_avatar_key(avatar_id))

def _get_client_for_avatar(avatar_id: str) -> chromadb.PersistentClient:
    key = _safe_avatar_key(avatar_id)
    with _AVATAR_LOCK:
        c = _AVATAR_CLIENTS.get(key)
        if c is not None:
            return c
        d = os.path.join(PERSIST_ROOT, key)
        os.makedirs(d, exist_ok=True)
        try:
            # Usa impostazioni minimaliste senza forcing API per evitare conflitti hnswlib
            c = chromadb.PersistentClient(path=d)
        except Exception as e:
            raise RuntimeError(
                f"Impossibile aprire il database Chroma in '{d}'. "
                f"Controlla permessi/percorso. Errore: {e}"
            )
        _AVATAR_CLIENTS[key] = c
        return c

def get_collection(avatar_id: str):
    # Unica collezione dentro al DB dell'avatar
    client = _get_client_for_avatar(avatar_id)
    return client.get_or_create_collection(name="memory")


# -----------------------------
# Chroma Windows lock helpers
# -----------------------------
def _stop_chroma_system(client) -> None:
    """Prova a stoppare il 'system' condiviso di Chroma per liberare lock su Windows."""
    if client is None:
        return

    # 1) stop diretto se esiste
    try:
        sys = getattr(client, "_system", None)
        if sys and hasattr(sys, "stop"):
            sys.stop()
    except Exception:
        pass

    # 2) rimuovi dal cache SharedSystemClient (se presente)
    try:
        from chromadb.api.shared_system_client import SharedSystemClient
        ident = getattr(client, "_identifier", None)
        mapping = getattr(SharedSystemClient, "_identifier_to_system", None)
        if ident and isinstance(mapping, dict):
            sys2 = mapping.pop(ident, None)
            if sys2 and hasattr(sys2, "stop"):
                sys2.stop()
    except Exception:
        pass


def _rmtree_force(path: str):
    """rmtree robusto (toglie read-only)"""
    if not os.path.isdir(path):
        return True, None

    def _onerror(func, p, exc_info):
        try:
            os.chmod(p, stat.S_IWRITE)
            func(p)
        except Exception:
            pass

    try:
        shutil.rmtree(path, onerror=_onerror)
        return True, None
    except Exception as e:
        return False, str(e)


def _sanitize_metadata(meta: dict | None) -> dict:
    """Solo scalar (str, int, float, bool, None)."""
    if not meta:
        return {}
    return {k: v if isinstance(v, (str, int, float, bool, type(None))) else str(v) for k, v in meta.items()}


# -----------------------------
# File text extraction
# -----------------------------

def describe_image_with_gemini(image_bytes: bytes, prompt: str = "Descrivi dettagliatamente questa immagine in italiano.") -> str:
    """Usa Gemini Vision per descrivere un'immagine."""
    if _gemini_client is None or not GEMINI_API_KEY:
        raise HTTPException(status_code=500, detail="Gemini non configurato. Imposta GEMINI_API_KEY.")

    try:
        if genai_types is None:
            raise HTTPException(status_code=500, detail="google.genai non disponibile.")

        parts = [
            prompt,
            genai_types.Part.from_bytes(data=image_bytes, mime_type="image/png"),
        ]

        response = _gemini_client.models.generate_content(
            model="gemini-2.0-flash",
            contents=parts,
        )

        text = getattr(response, "text", None)
        if text:
            return text

        # fallback: prova a leggere da candidates
        try:
            return response.candidates[0].content.parts[0].text or ""
        except Exception:
            return ""
    except Exception as e:
        raise HTTPException(status_code=500, detail=f"Errore Gemini Vision: {e}")


def _ensure_ocr_ready() -> bool:
    """Verifica e configura OCR. Ritorna True se pronto, False altrimenti."""
    if pytesseract is None or Image is None:
        return False
    
    # Configura tesseract_cmd se specificato (o usa default Windows)
    tcmd = TESSERACT_CMD or DEFAULT_TESSERACT_CMD
    if tcmd and os.path.exists(tcmd):
        pytesseract.pytesseract.tesseract_cmd = tcmd
    
    # Verifica che tesseract sia effettivamente disponibile
    try:
        pytesseract.get_tesseract_version()
        return True
    except Exception:
        return False


def ocr_image_bytes(image_bytes: bytes) -> str:
    if not _ensure_ocr_ready():
        raise HTTPException(status_code=500, detail="OCR non disponibile: installa pytesseract/pillow e tesseract")
    img = Image.open(io.BytesIO(image_bytes))
    try:
        txt = pytesseract.image_to_string(img, lang=RAG_OCR_LANG)
    except Exception:
        try:
            txt = pytesseract.image_to_string(img, lang="eng")
        except Exception as e:
            raise HTTPException(status_code=500, detail=f"OCR fallito: {e}")
    return clean_text(txt)


def linearize_table_text(text: str) -> str:
    """Trasforma testo di tabella OCR in formato lineare per miglior semantic similarity.
    
    Esempio: 
    Input:  "Esame\nVoto\nMATEMATICA I\n24/30"
    Output: "Esame MATEMATICA I Voto 24/30"
    
    Questo riduce il rumore dovuto alla fragmentazione della tabella e aiuta gli embeddings
    a capire meglio le relazioni tra colonne.
    """
    # Normalizza spazi multipli e newline
    lines = text.split('\n')
    # Rimuovi righe vuote e unite consecutive in una sola
    cleaned_lines = [line.strip() for line in lines if line.strip()]
    return ' '.join(cleaned_lines)


def extract_text_from_pdf(pdf_bytes: bytes, force_ocr: bool = False) -> List[Tuple[str, dict]]:
    """Estrae testo da PDF usando SEMPRE OCR (più affidabile del testo embedded).
    
    Il testo embedded nei PDF è spesso corrotto, non selezionabile o mal formattato.
    OCR garantisce testo pulito e leggibile per il RAG.
    """
    if fitz is None:
        raise HTTPException(status_code=500, detail="PDF parsing non disponibile: installa 'pymupdf'.")

    if not _ensure_ocr_ready():
        raise HTTPException(
            status_code=500, 
            detail="OCR non disponibile. Installa tesseract-ocr e pytesseract/pillow."
        )

    sections: List[Tuple[str, dict]] = []
    skipped_pages = []
    total_pages = 0
    
    with fitz.open(stream=pdf_bytes, filetype="pdf") as doc:
        total_pages = doc.page_count
        for i in range(total_pages):
            page = doc.load_page(i)
            
            # OCR diretto - ignora testo embedded
            try:
                pix = page.get_pixmap(dpi=OCR_DPI)
                img_bytes = pix.tobytes("png")
                ocr_text = clean_text(ocr_image_bytes(img_bytes), ocr=True)
                # Linearizza tabelle per miglior semantic similarity
                ocr_text = linearize_table_text(ocr_text)
            except Exception:
                skipped_pages.append(i + 1)
                continue

            # Accetta solo se non è vuoto
            if ocr_text and len(ocr_text.strip()) >= 5:
                meta = {
                    "page": i + 1, 
                    "method": "ocr", 
                    "ocr": True,
                    "text_length": len(ocr_text)
                }
                sections.append((ocr_text, meta))
            else:
                skipped_pages.append(i + 1)

    if not sections:
        raise HTTPException(
            status_code=400, 
            detail=f"Nessun testo estratto dal PDF. Pagine totali: {total_pages}, saltate: {len(skipped_pages)}. "
                   f"OCR non ha prodotto testo o pagine vuote."
        )

    return sections


def extract_text_from_plain(bytes_data: bytes) -> str:
    for enc in ("utf-8", "utf-8-sig", "cp1252", "latin-1"):
        try:
            return clean_text(bytes_data.decode(enc))
        except Exception:
            continue
    return clean_text(bytes_data.decode("utf-8", errors="ignore"))


def chunk_text(text: str, chunk_chars: int = CHUNK_CHARS, overlap: int = CHUNK_OVERLAP) -> List[str]:
    text = clean_text(text)
    if not text:
        return []

    chunks: List[str] = []
    start = 0
    n = len(text)
    while start < n:
        end = min(n, start + chunk_chars)

        window = text[start:end]
        cut = max(window.rfind("\n\n"), window.rfind(". "), window.rfind("\n"))
        if cut > 200:
            end = start + cut + (2 if window[cut : cut + 2] == ". " else 0)

        chunk = clean_text(text[start:end])
        if chunk and len(chunk) >= MIN_CHUNK_CHARS and not looks_like_garbage(chunk):
            chunks.append(chunk)

        start = max(end - overlap, start + 1)

    return chunks


def _dedupe_chunks(docs: List[str], metas: List[dict], similarity: float = 0.92) -> Tuple[List[str], List[dict]]:
    """Rimuove duplicati (similarity > 0.92)."""
    if not docs:
        return docs, metas
    kept = []
    for d, m in zip(docs, metas):
        norm = clean_text(d).lower() if d else ""
        if norm and not any(difflib.SequenceMatcher(None, norm, kn).ratio() >= similarity for kn in [k[2] for k in kept]):
            kept.append((d, m or {}, norm))
    return (list(map(lambda x: x[0], kept)), list(map(lambda x: x[1], kept))) if kept else ([], [])


def _hybrid_search(
    query: str,
    query_embedding: List[float],
    col,
    top_k: int = 20,
    bm25_weight: float = 0.6,
) -> Tuple[List[str], List[dict]]:
    """
    Ricerca ibrida ottimizzata: combina BM25 keyword matching + vector similarity.
    
    Strategia:
    1. Vector search recupera top_k*3 candidati (veloce, usa index)
    2. BM25 re-rank solo su quei candidati (veloce, pochi documenti)
    3. Combina score e ritorna top_k finali
    
    - BM25 (60%): keyword matching esatto
    - Vector (40%): similarità semantica
    """
    
    # 1) Vector similarity search per recuperare candidati
    try:
        candidate_k = min(top_k * 3, 100)  # Limita a max 100 candidati
        res = col.query(
            query_embeddings=[query_embedding],
            n_results=candidate_k,
            include=["documents", "metadatas", "distances"],
        )
        vec_docs = (res.get("documents") or [[]])[0]
        vec_metas = (res.get("metadatas") or [[]])[0]
        vec_distances = (res.get("distances") or [[]])[0]
        
        if not vec_docs:
            return [], []
        
        # 2) BM25 search solo sui candidati
        tokenized_docs = [doc.lower().split() for doc in vec_docs]
        bm25 = BM25Okapi(tokenized_docs)
        query_tokens = query.lower().split()
        bm25_scores = bm25.get_scores(query_tokens)
        
        # Normalizza scores
        max_bm25 = max(bm25_scores) if max(bm25_scores) > 0 else 1
        bm25_norm = [s / max_bm25 for s in bm25_scores]
        
        vec_scores_raw = [1 - d if d is not None else 0 for d in vec_distances]
        max_vec = max(vec_scores_raw) if max(vec_scores_raw) > 0 else 1
        vec_norm = [s / max_vec for s in vec_scores_raw]
        
        # 3) Combina score
        combined = []
        for i in range(len(vec_docs)):
            score = (bm25_weight * bm25_norm[i]) + ((1 - bm25_weight) * vec_norm[i])
            combined.append((score, vec_docs[i], vec_metas[i]))
        
        # 4) Ordina e ritorna top_k
        combined.sort(key=lambda x: x[0], reverse=True)
        result_docs = [doc for _, doc, _ in combined[:top_k]]
        result_metas = [meta for _, _, meta in combined[:top_k]]
        
        return result_docs, result_metas
        
    except Exception as e:
        # Fallback: solo vector search
        try:
            res = col.query(
                query_embeddings=[query_embedding],
                n_results=top_k,
                include=["documents", "metadatas"],
            )
            return (res.get("documents") or [[]])[0], (res.get("metadatas") or [[]])[0]
        except:
            return [], []


# -----------------------------
# API models
# -----------------------------
app = FastAPI(title="SOULFRAME RAG Server", version="3.0")


# Exception handler globale
@app.exception_handler(Exception)
async def global_exception_handler(request, exc):
    print(f"[EXCEPTION] {type(exc).__name__}: {exc}")
    import traceback
    traceback.print_exc()
    from fastapi import HTTPException
    raise HTTPException(status_code=500, detail=str(exc))


class RememberReq(BaseModel):
    avatar_id: str
    text: str
    meta: Optional[dict] = None


class RecallReq(BaseModel):
    avatar_id: str
    query: str
    top_k: int = 20


class ChatReq(BaseModel):
    avatar_id: str
    user_text: str
    top_k: int = 20
    system: Optional[str] = None


# -----------------------------
# Endpoints
# -----------------------------
@app.get("/health")
def health():
    return {
        "ok": True,
        "ollama": OLLAMA_HOST,
        "embed_model": EMBED_MODEL,
        "chat_model": CHAT_MODEL,
        "rag_root": PERSIST_ROOT,
        "per_avatar_db": True,
        "cached_avatars": len(_AVATAR_CLIENTS),
        "ocr": bool(pytesseract and Image),
        "pdf": bool(fitz),
        "ocr_lang": RAG_OCR_LANG,
        "gemini_vision": bool(_gemini_client and GEMINI_API_KEY),
    }


@app.post("/remember")
def remember(req: RememberReq):
    col = get_collection(req.avatar_id)

    txt = clean_text(req.text)
    if len(txt) < MIN_CHUNK_CHARS:
        raise HTTPException(status_code=400, detail="Testo troppo corto o vuoto.")
    if looks_like_garbage(txt):
        raise HTTPException(status_code=400, detail="Testo sembra corrotto/non testuale (molti caratteri anomali).")

    emb = ollama_embed_many([txt])[0]
    _id = str(uuid.uuid4())

    meta = _sanitize_metadata(req.meta or {})
    meta.setdefault("source_type", "manual")
    meta.setdefault("avatar_id", req.avatar_id)
    meta.setdefault("ts", int(time.time()))

    col.add(ids=[_id], embeddings=[emb], documents=[txt], metadatas=[meta])
    return {"ok": True, "id": _id}


@app.post("/recall")
def recall(req: RecallReq):
    col = get_collection(req.avatar_id)

    q = clean_text(req.query)
    if not q:
        raise HTTPException(status_code=400, detail="Query vuota.")

    try:
        count = col.count()
    except Exception:
        count = 0

    if count == 0:
        return {"documents": [[]], "metadatas": [[]], "distances": [[]], "ids": [[]]}

    try:
        qemb = ollama_embed_many([q])[0]
    except Exception as e:
        raise HTTPException(status_code=500, detail=f"Errore embedding: {e}")

    # Hybrid search ottimizzata
    try:
        docs_hybrid, metas_hybrid = _hybrid_search(
            query=q,
            query_embedding=qemb,
            col=col,
            top_k=req.top_k,
            bm25_weight=0.6,
        )
        
        # Ritorna nel formato standard di ChromaDB
        return {
            "documents": [docs_hybrid],
            "metadatas": [metas_hybrid],
            "distances": [[0] * len(docs_hybrid)],
            "ids": [[f"doc_{i}" for i in range(len(docs_hybrid))]],
        }
    except Exception as e:
        # Fallback a ricerca standard se hybrid fallisce
        res = col.query(
            query_embeddings=[qemb],
            n_results=req.top_k,
            include=["documents", "metadatas", "distances", "ids"],
        )
        return res


@app.post("/chat")
def chat(req: ChatReq):
    col = get_collection(req.avatar_id)

    q = clean_text(req.user_text)
    if not q:
        raise HTTPException(status_code=400, detail="Messaggio vuoto.")

    try:
        count = col.count()
    except Exception:
        count = 0

    if count > 0:
        try:
            qemb = ollama_embed_many([q])[0]
        except Exception as e:
            raise HTTPException(status_code=500, detail=f"Errore embedding: {e}")

        try:
            # Hybrid search ottimizzata
            docs, metas = _hybrid_search(
                query=q,
                query_embedding=qemb,
                col=col,
                top_k=req.top_k,
                bm25_weight=0.6,
            )
        except Exception as e:
            raise HTTPException(status_code=500, detail=f"Errore query memoria: {e}")
    else:
        docs, metas = [], []

    # deduplica per ridurre overlap/duplicati
    docs, metas = _dedupe_chunks(docs, metas)

    # contesto compatto con sorgente
    ctx_parts: List[str] = []
    total = 0
    for d, m in zip(docs, metas):
        if not d:
            continue

        src = (m or {}).get("source_filename") or (m or {}).get("source_type") or "memoria"
        page = (m or {}).get("page")
        tag = f"[{src}{' p.' + str(page) if page else ''}]"
        piece = f"{tag} {d}"

        if total + len(piece) > MAX_CONTEXT_CHARS:
            break

        ctx_parts.append(piece)
        total += len(piece)

    context = "\n\n".join(ctx_parts)

    system = req.system or (
        "Sei l'avatar stesso, non un assistente. Parla SEMPRE in PRIMA PERSONA come se tu fossi la persona. "
        "Usa il CONTESTO come la tua memoria personale: sono ricordi, esperienze, informazioni su di te. "
        "Rispondi nella TUA LINGUA (indicata nel contesto se presente), altrimenti in italiano. "
        "Sii naturale e diretto, come farebbe una persona vera. "
        "NON dire 'secondo il contesto' o 'l'utente': rispondi come se fossi TU quella persona. "
        "Esempi: 'Odio i maccheroni' (NON 'l'utente odia'), 'Mi chiamo Luca' (NON 'si chiama Luca'). "
        "Se non sai qualcosa, dillo semplicemente: 'Non ricordo' o 'Non lo so'."
    )

    user = f"CONTESTO (memoria RAG):\n{context}\n\nUTENTE:\n{q}"

    answer = ollama_chat(
        [
            {"role": "system", "content": system},
            {"role": "user", "content": user},
        ]
    ).strip()

    rag_used = []
    for d, m in zip(docs, metas):
        if not d:
            continue
        rag_used.append({"text": d, "meta": m or {}})

    return {"text": answer, "rag_used": rag_used}


@app.post("/clear_avatar")
def clear_avatar(avatar_id: str = Form(...), hard: bool = Form(True)):
    """
    Cancella la memoria RAG di un avatar.

    - soft  (hard=false): elimina la collezione dal DB dell'avatar
    - hard  (hard=true): come sopra, ma rimuove anche la cartella su disco (rag_store/<avatar_id>)

    Nota: su Windows la cancellazione può fallire se ci sono handle aperti; in quel caso riprova.
    """
    avatar_id = (avatar_id or "").strip()
    if not avatar_id:
        raise HTTPException(status_code=400, detail="avatar_id is required")

    avatar_key = _safe_avatar_key(avatar_id)
    avatar_dir = os.path.join(PERSIST_ROOT, avatar_key)

    # 1) chiudi/stoppa Chroma e rimuovi client dalla cache
    client = _AVATAR_CLIENTS.pop(avatar_key, None)
    try:
        if client is None:
            client = chromadb.PersistentClient(path=avatar_dir)
    except Exception:
        client = None

    # prova a cancellare la collection (soft wipe)
    collection_deleted = False
    try:
        if client is not None:
            try:
                client.delete_collection(name="memory")
                collection_deleted = True
            except Exception:
                # se non esiste o fallisce, ok
                pass
    except Exception:
        pass

    # stop system per liberare lock su Windows
    _stop_chroma_system(client)
    del client
    gc.collect()

    deleted_dir = False
    delete_error = None

    if hard:
        # 2) elimina fisicamente la directory avatar
        ok, err = _rmtree_force(avatar_dir)
        deleted_dir = ok
        delete_error = err

    return {
        "ok": True,
        "hard": hard,
        "collection_deleted": collection_deleted,
        "deleted_dir": deleted_dir,
        "delete_error": delete_error,
        "avatar_dir": avatar_dir,
    }

@app.post("/debug_pdf_ocr")
async def debug_pdf_ocr(
    file: UploadFile = File(...),
    page: int = Form(0),  # 0-indexed
):
    """DEBUG: Estrai OCR grezzo da una pagina specifica del PDF (no filtering)."""
    if not file.filename.lower().endswith('.pdf'):
        raise HTTPException(status_code=400, detail="Solo PDF")
    
    raw = await file.read()
    if not raw:
        raise HTTPException(status_code=400, detail="File vuoto")
    
    if fitz is None or not _ensure_ocr_ready():
        raise HTTPException(status_code=500, detail="OCR non disponibile")
    
    try:
        with fitz.open(stream=raw, filetype="pdf") as doc:
            if page < 0 or page >= doc.page_count:
                raise HTTPException(status_code=400, detail=f"Pagina {page} non esiste (totale: {doc.page_count})")
            
            p = doc.load_page(page)
            pix = p.get_pixmap(dpi=OCR_DPI)
            img_bytes = pix.tobytes("png")
            ocr_raw = pytesseract.image_to_string(Image.open(io.BytesIO(img_bytes)), lang="eng")
            
            return {
                "filename": file.filename,
                "page": page,
                "dpi": OCR_DPI,
                "ocr_length": len(ocr_raw),
                "ocr_text": ocr_raw[:3000] if len(ocr_raw) > 3000 else ocr_raw  # primi 3000 char
            }
    except Exception as e:
        raise HTTPException(status_code=500, detail=f"OCR error: {str(e)[:100]}")

@app.post("/describe_image")
async def describe_image(
    file: UploadFile = File(...),
    prompt: str = Form("Descrivi dettagliatamente questa immagine in italiano."),
):
    """Descrivi un'immagine usando Gemini Vision.
    
    Args:
        file: Immagine (PNG, JPG, WEBP, ecc.)
        prompt: Prompt personalizzato per la descrizione
    """
    if _gemini_client is None or not GEMINI_API_KEY:
        raise HTTPException(status_code=500, detail="Gemini non configurato. Imposta GEMINI_API_KEY.")

    filename = file.filename or "image"
    raw = await file.read()
    if not raw:
        raise HTTPException(status_code=400, detail="File vuoto.")

    # Converti in PNG se necessario
    ext = filename.lower().split(".")[-1]
    if ext not in ["png", "jpg", "jpeg", "webp"]:
        if Image is None:
            raise HTTPException(status_code=400, detail="Formato non supportato. Installa pillow.")
        try:
            img = Image.open(io.BytesIO(raw))
            png_buf = io.BytesIO()
            img.save(png_buf, format="PNG")
            raw = png_buf.getvalue()
        except Exception as e:
            raise HTTPException(status_code=400, detail=f"Errore conversione immagine: {e}")

    description = describe_image_with_gemini(raw, prompt)

    return {
        "ok": True,
        "filename": filename,
        "description": description,
        "prompt_used": prompt,
    }


@app.post("/ingest_file")
async def ingest_file(
    avatar_id: str = Form(...),
    file: UploadFile = File(...),
):
    """Ingest PDF / immagini / testo. Usa sempre OCR per PDF e immagini.
    
    Args:
        avatar_id: ID dell'avatar
        file: File da processare (PDF, immagini, testo)
    """

    filename = file.filename or "upload"
    ext = os.path.splitext(filename)[1].lower()

    raw = await file.read()
    if not raw:
        raise HTTPException(status_code=400, detail="File vuoto.")

    sections: List[Tuple[str, dict]] = []

    # PDF - sempre OCR
    if ext == ".pdf":
        sections = extract_text_from_pdf(raw)
        if not sections:
            raise HTTPException(status_code=400, detail="Nessun testo estratto dal PDF (OCR fallito o testo illeggibile).")

    # Images
    elif ext in (".png", ".jpg", ".jpeg", ".webp", ".bmp", ".tif", ".tiff"):
        txt = ocr_image_bytes(raw)
        if not txt or looks_like_garbage(txt):
            raise HTTPException(status_code=400, detail="OCR non ha prodotto testo utile dall'immagine.")
        sections = [(txt, {"page": 1, "ocr": True})]

    # Plain-ish text
    else:
        txt = extract_text_from_plain(raw)
        if not txt or looks_like_garbage(txt):
            raise HTTPException(status_code=400, detail="File non testuale o contenuto non leggibile.")
        sections = [(txt, {"page": 1})]

    col = get_collection(avatar_id)

    ids: List[str] = []
    docs: List[str] = []
    metas: List[dict] = []
    seen_chunks: set = set()  # Track unique chunks

    for text, meta in sections:
        chunks = chunk_text(text)
        for idx, ch in enumerate(chunks):
            # Deduplicate: skip if we've already seen this chunk
            if ch in seen_chunks:
                continue
            seen_chunks.add(ch)
            
            _id = str(uuid.uuid4())
            ids.append(_id)
            docs.append(ch)
            m = dict(meta or {})
            m.update(
                {
                    "source_type": "file",
                    "source_filename": filename,
                    "chunk": idx,
                    "ts": int(time.time()),
                }
            )
            m["avatar_id"] = avatar_id
            metas.append(_sanitize_metadata(m))

    if not docs:
        raise HTTPException(status_code=400, detail="Nessun chunk valido generato dal file.")

    # embed in batch piccoli
    embeddings: List[List[float]] = []
    batch = 16
    for i in range(0, len(docs), batch):
        embeddings.extend(ollama_embed_many(docs[i : i + batch]))

    if len(embeddings) != len(docs):
        raise HTTPException(status_code=500, detail="Errore embeddings: conteggio non combacia.")

    col.add(ids=ids, embeddings=embeddings, documents=docs, metadatas=metas)

    return {
        "ok": True,
        "filename": filename,
        "sections": len(sections),
        "chunks_added": len(docs),
    }


if __name__ == "__main__":
    import uvicorn
    import sys
    try:
        print("[INFO] Avvio server su 127.0.0.1:8002", file=sys.stderr, flush=True)
        # Usa reload=False per evitare problemi di multiprocessing
        uvicorn.run(app, host="127.0.0.1", port=8002, reload=False)
    except Exception as e:
        print(f"[FATAL] Errore server: {e}", file=sys.stderr, flush=True)
        import traceback
        traceback.print_exc(file=sys.stderr)
        sys.exit(1)
