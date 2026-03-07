"""
SOULFRAME - RAG Server (FastAPI)

Cosa fa:
- Memoria per avatar con ChromaDB (persistente, per-avatar DB)
- Embedding via Ollama (/api/embed)
- Chat via Ollama (/api/chat) con RAG retrieval e deduplicazione
- Log conversazioni per avatar/sessione MainMode su file .log persistenti
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
- POST /chat_session/start: apre una sessione conversazione e crea il file log
- POST /ingest_file: importa PDF/immagini/testo con deduplicazione
- POST /describe_image: descrizione con Gemini Vision
- POST /clear_avatar: cancella memoria di un avatar (soft/hard)
- POST /clear_avatar_logs: cancella solo la cartella log di un avatar
- POST /debug_pdf_ocr: DEBUG - test OCR su pagina specifica

Note pratiche:
- PDF: usa sempre OCR (600 DPI) - testo embedded spesso corrotto
- Tabelle OCR: linearizzate per miglior embedding semantico
- Deduplicazione: rimuove chunk duplicati (similarity > 92%) durante ingest e recall
- Garbage filtering: scarta solo testo REALMENTE vuoto/inutile, lascia decision all'LLM
- Ricerca ibrida: 60% BM25 (keyword), 40% vector (semantic) - piu' match su parole chiave
- Windows: gestisce lock file Chroma tramite stop system e rmtree robusto
- Se memoria piena di "spazzatura": svuota con /clear_avatar e re-ingest
- OCR: italiano+inglese configurabile (RAG_OCR_LANG)
"""

from __future__ import annotations

import os
import re
import io
import json
import difflib
import uuid
import time
from functools import lru_cache
from collections import deque
from datetime import datetime
import gc
import stat
import shutil
import threading
import traceback
from typing import Any, Optional, List, Tuple, TYPE_CHECKING, cast

import requests
import chromadb
from fastapi import FastAPI, UploadFile, File, Form, HTTPException, Request
from fastapi.middleware.cors import CORSMiddleware
from rank_bm25 import BM25Okapi
from pydantic import BaseModel

# Compatibilita' hnswlib: aggiungiamo file_handle_count atteso da chroma su Index
try:
    import hnswlib  # type: ignore
    if not hasattr(hnswlib.Index, "file_handle_count"):
        hnswlib.Index.file_handle_count = 0  # type: ignore[attr-defined]
except Exception:
    pass

# Gemini Vision opzionale (SDK nuovo)
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


if TYPE_CHECKING:
    from chromadb.api import ClientAPI as ChromaClientAPI
    from chromadb.api.types import Embedding as ChromaEmbedding
    from chromadb.api.types import Metadata as ChromaMetadata
else:
    ChromaClientAPI = Any  # type: ignore[misc,assignment]
    ChromaEmbedding = Any  # type: ignore[misc,assignment]
    ChromaMetadata = Any  # type: ignore[misc,assignment]


# -----------------------------
# Configurazione (env)
# -----------------------------
def _env_bool(name: str, default: bool) -> bool:
    raw = os.getenv(name)
    if raw is None:
        return default
    return raw.strip().lower() in {"1", "true", "yes", "y", "on"}


OLLAMA_HOST = os.getenv("OLLAMA_HOST", "http://127.0.0.1:11434").rstrip("/")
EMBED_MODEL = os.getenv("EMBED_MODEL", "nomic-embed-text")
CHAT_MODEL = os.getenv("CHAT_MODEL", "llama3:8b-instruct-q4_K_M")
CHAT_TEMPERATURE = float(os.getenv("CHAT_TEMPERATURE", "0.45"))
CHAT_TOP_P = float(os.getenv("CHAT_TOP_P", "0.9"))
CHAT_REPEAT_PENALTY = float(os.getenv("CHAT_REPEAT_PENALTY", "1.08"))
CHAT_NUM_PREDICT = int(os.getenv("CHAT_NUM_PREDICT", "280"))

PERSIST_ROOT = os.getenv("RAG_DIR", os.path.join(os.path.dirname(__file__), "rag_store"))
PERSIST_ROOT = PERSIST_ROOT.strip().strip('"')
PERSIST_ROOT = os.path.abspath(os.path.normpath(PERSIST_ROOT))
os.makedirs(PERSIST_ROOT, exist_ok=True)

RAG_LOG_DIR = os.getenv("RAG_LOG_DIR", os.path.join(os.path.dirname(__file__), "log"))
RAG_LOG_DIR = RAG_LOG_DIR.strip().strip('"')
RAG_LOG_DIR = os.path.abspath(os.path.normpath(RAG_LOG_DIR))
os.makedirs(RAG_LOG_DIR, exist_ok=True)

CHUNK_CHARS = int(os.getenv("RAG_CHUNK_CHARS", "3500"))
CHUNK_OVERLAP = int(os.getenv("RAG_CHUNK_OVERLAP", "500"))
MIN_CHUNK_CHARS = int(os.getenv("RAG_MIN_CHUNK_CHARS", "20"))
REMEMBER_MIN_CHARS = int(os.getenv("RAG_REMEMBER_MIN_CHARS", "10"))
MAX_CHROMA_ADD_BATCH = int(os.getenv("RAG_CHROMA_ADD_BATCH", "5000"))
MAX_CONTEXT_CHARS = int(os.getenv("RAG_MAX_CONTEXT_CHARS", "6000"))
FACTUAL_MAX_CONTEXT_CHARS = int(os.getenv("RAG_FACTUAL_MAX_CONTEXT_CHARS", "3600"))
RAG_FACTUAL_SCORE_MIN = float(os.getenv("RAG_FACTUAL_SCORE_MIN", "0.33"))
RAG_FACTUAL_SCORE_GAP_MIN = float(os.getenv("RAG_FACTUAL_SCORE_GAP_MIN", "0.08"))
RAG_SESSION_TURNS = int(os.getenv("RAG_SESSION_TURNS", "8"))
RAG_CHAT_TOP_K_CAP = int(os.getenv("RAG_CHAT_TOP_K_CAP", "8"))
RAG_INTENT_ROUTER_NUM_PREDICT = int(os.getenv("RAG_INTENT_ROUTER_NUM_PREDICT", "32"))
RAG_ENFORCE_GROUNDED = _env_bool("RAG_ENFORCE_GROUNDED", True)

# OCR / Tesseract
RAG_OCR_LANG = os.getenv("RAG_OCR_LANG", "ita+eng").strip()          # es: "ita" oppure "ita+eng"
TESSERACT_CMD = os.getenv("TESSERACT_CMD", "").strip().strip('"')
DEFAULT_TESSERACT_CMD = r"C:\Program Files\Tesseract-OCR\tesseract.exe"
OCR_DPI = int(os.getenv("RAG_OCR_DPI", "400"))  # Aumentato da 300 a 400 per tabelle

# Gemini Vision (opzionale)
GEMINI_API_KEY = os.getenv("GEMINI_API_KEY", "").strip()
# Se non trovata in env, prova a leggerla da file
if not GEMINI_API_KEY:
    # Prova percorsi multipli
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
    print("[WARN] GEMINI_API_KEY non trovata in env ne' in file gemini_key.txt")


# -----------------------------
# Supporto: pulizia + qualita'
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
    # Abbiamo rimosso il filtro OCR perche' era troppo aggressivo sui documenti reali
    return s.strip()


def looks_like_garbage(text: str, min_len: int = 20, min_words: int = 2) -> bool:
    """Scarta solo testo quasi vuoto o senza parole utili."""
    t = (text or "").strip()
    if len(t) < max(1, int(min_len)):
        return True

    sample = t[:2000]
    words = re.findall(r"[A-Za-z]{2,}", sample)
    if len(words) < max(1, int(min_words)):
        return True

    return False


def _remember_validation_error_detail(text: str) -> Optional[dict]:
    txt = clean_text(text or "")
    text_len = len(txt)
    min_chars = max(2, int(REMEMBER_MIN_CHARS))
    if text_len == 0:
        return {
            "code": "remember_empty_text",
            "message": "Testo vuoto dopo pulizia.",
            "received_length": 0,
            "min_length_required": min_chars,
        }

    if text_len < min_chars:
        return {
            "code": "remember_text_too_short",
            "message": "Testo troppo corto per essere salvato in memoria.",
            "received_length": text_len,
            "min_length_required": min_chars,
        }

    alpha_words = re.findall(r"[^\W\d_]{2,}", txt, flags=re.UNICODE)
    if len(alpha_words) < 2:
        return {
            "code": "remember_not_enough_words",
            "message": "Servono almeno 2 parole alfabetiche per salvare un ricordo.",
            "alpha_words_found": len(alpha_words),
            "alpha_words_min_required": 2,
        }

    if looks_like_garbage(txt, min_len=min_chars, min_words=2):
        return {
            "code": "remember_text_not_supported",
            "message": "Testo non valido per il salvataggio (rumoroso/corrotto).",
        }
    return None


_PERSONA_STYLE_RE = re.compile(
    r"\b("
    r"caratter\w*|personalit\w*|temperament\w*|modo di parlare|"
    r"tono di voce|tono|stile lingu\w*|stile comunicativ\w*|"
    r"registro|formale|informale|iron\w*|sarcast\w*|gentil\w*|"
    r"timid\w*|estrovers\w*|pacat\w*|dirett\w*|schiett\w*|"
    r"slang|dialetto|espression\w* ricorrent\w*|lessico"
    r")\b",
    re.IGNORECASE,
)

_SPEAKER_PREFIX_RE = re.compile(
    r"^\s*(?:ta|utente|user|assistant|avatar|narratore|narrator)\b\s*[:>\-\]]?\s*",
    re.IGNORECASE,
)
_GENERIC_SPEAKER_PREFIX_RE = re.compile(
    r"^\s*[A-Za-zÀ-ÿ]{2,20}\s*:\s*",
    re.IGNORECASE,
)
_STAGE_DIRECTION_RE = re.compile(
    r"(\(([^()]{1,60})\)|\[([^\[\]]{1,60})\]|\*([^*]{1,60})\*)",
    re.IGNORECASE,
)
_STAGE_KEYWORDS = (
    "ride", "ridendo", "risata", "risate", "sorride", "sorridendo",
    "sospira", "sospirando", "pausa", "silenzio", "annuisce", "annuendo",
    "sbuffa", "sbuffando", "schiarisce la voce", "tossisce",
    "laugh", "laughs", "laughing", "chuckle", "chuckles", "chuckling",
    "giggle", "giggles", "giggling", "sigh", "sighs", "sighing",
    "whisper", "whispers", "whispering", "shout", "shouts", "shouting",
    "applause", "music", "cough", "coughs",
)
_LEADING_FILLER_RE = re.compile(r"^\s*(?:ahem+|ehm+|mmm+|uhm+|hmm+)\b[\s,.;:!?-]*", re.IGNORECASE)
_WORD_TOKEN_RE = re.compile(r"\b[\w']+\b", re.UNICODE)
_IDENTITY_META_RE = re.compile(
    r"\b("
    r"sono un avatar|sono un assistente|sono un'ia|sono una ia|sono un ai|"
    r"modello linguistico|language model|sono un bot|sono un sistema"
    r")\b",
    re.IGNORECASE,
)
_PROFILE_MEMORY_SOURCE_TYPES = ("manual", "manual_note", "auto_remember_voice")
_DOC_QUERY_HINTS = {
    "pdf", "documento", "documenti", "file", "messaggi", "certificato",
    "modulo", "privacy", "dati", "personali", "consenso", "firma", "data",
    "clausola", "contratto", "attestato", "comunicazione",
    "testo", "testuale", "testuali", "documentale", "documentali",
}
_PROFILE_QUERY_HINTS = {
    "chiamo", "chiami", "nome", "vivo", "abito", "citta", "città",
    "eta", "età", "anni", "insicuro", "insicura", "carattere", "persona", "tipo",
    "personalita", "personalità", "dove", "allergia", "allergie", "animale", "animali",
    "gatto", "cane", "patente", "sposto", "spostarti", "spostarsi", "treno", "scooter",
    "bevi", "bevanda", "mattino", "password", "dimentico", "dimenticare", "colloquio",
    "appuntamento", "lavoro", "lavori", "studi",
}
_IMAGE_QUERY_HINTS = {
    "immagine", "immagini", "foto", "display", "schermo", "profilo",
    "aspetto", "lineamenti", "viso", "faccia", "specchio", "vestito", "vestita",
    "mano", "sfondo", "stanza", "visivo", "visivi", "grafico", "diagramma",
}
_PDF_QUERY_HINTS = {
    "pdf", "documento", "documenti", "file", "messaggi", "certificato",
    "modulo", "privacy", "dati", "personali", "firma", "data", "contratto",
}
_MEMORY_REFERENCE_HINTS = {
    "quel", "quella", "quello", "quelli", "quelle", "questa", "questo", "questi", "queste",
    "prima", "scorso", "precedente", "ricordi", "ricordo", "memoria", "memorizzato", "dimentico",
    "dimenticare", "dimentichi",
}
_CHITCHAT_FIRST_PERSON_RE = re.compile(
    r"\b(io|mi|mio|mia|mie|miei|sono|sto|ho|vado|vivo|abito|oggi|ieri|stasera)\b",
    re.IGNORECASE,
)
_CHITCHAT_TOKEN_STOPWORDS = {
    "sono", "come", "stai", "stasera", "oggi", "ieri", "grazie", "bene",
    "male", "ciao", "ehi", "allora", "forse", "dopo", "prima", "molto",
    "poco", "cosi", "così", "solo", "anche", "sempre", "mai", "quindi",
    "perche", "perché", "quando", "dove", "cosa", "questo", "quello",
    "della", "dello", "delle", "degli", "dall", "dalla", "dalle", "dai",
    "con", "senza", "per", "tra", "fra", "nel", "nella", "nelle", "nei",
}
_ALIGNMENT_TOKEN_STOPWORDS = _CHITCHAT_TOKEN_STOPWORDS | {
    "frase", "frasi", "punto", "punti", "breve", "brevi", "concreto", "concreta",
    "riassumi", "riassunto", "ricordo", "ricordi", "memoria", "memorizzato",
}
_CONTEXT_DENIAL_RE = re.compile(
    r"\b(non ho (?:visto|letto|ricord\w*|informazioni?)|non posso|non so nulla|nessuna informazione)\b",
    re.IGNORECASE,
)
_RECAP_QUERY_RE = re.compile(
    r"\b(riepilog\w*|riassum\w*|ricapitol\w*|cosa (?:ricord\w*|hai memorizzat\w*)|ultima volta)\b",
    re.IGNORECASE,
)


def _is_persona_style_text(text: str) -> bool:
    t = clean_text(text)
    if not t:
        return False
    return _PERSONA_STYLE_RE.search(t) is not None


def _memory_role_for_text(text: str) -> str:
    return "persona_style" if _is_persona_style_text(text) else "factual_memory"


def _sanitize_chat_answer(text: str) -> str:
    out = (text or "").strip()
    if not out:
        return ""

    # Rimuove eventuali prefissi parlante tipo "TA:", "Utente:", ecc.
    for _ in range(2):
        cleaned = _SPEAKER_PREFIX_RE.sub("", out, count=1).strip()
        cleaned = _GENERIC_SPEAKER_PREFIX_RE.sub("", cleaned, count=1).strip()
        if cleaned == out:
            break
        out = cleaned

    def _remove_stage_direction(match: re.Match[str]) -> str:
        whole = (match.group(0) or "").strip()
        # Blocchi tra *...* sono quasi sempre stage-direction/azione: rimuovili sempre.
        if whole.startswith("*") and whole.endswith("*"):
            return ""

        inner = (match.group(2) or match.group(3) or match.group(4) or "").strip().lower()
        norm = re.sub(r"[^a-zA-Z0-9\u00C0-\u017F\s]", " ", inner)
        norm = re.sub(r"\s+", " ", norm).strip()
        if not norm:
            return ""
        # Se breve e "parentetico", tende a essere gesto/tono/effetto vocale.
        if len(norm.split()) <= 8:
            return ""
        if any(k in norm for k in _STAGE_KEYWORDS):
            return ""
        return match.group(0)

    out = _STAGE_DIRECTION_RE.sub(_remove_stage_direction, out)
    out = _LEADING_FILLER_RE.sub("", out)
    out = re.sub(r"[*_~`]+", "", out)
    out = re.sub(r"\s{2,}", " ", out)
    out = re.sub(r"\s+([,.;:!?])", r"\1", out)
    out = out.strip(" \t\r\n\"'")
    return out


def _finalize_chat_answer(text: str) -> str:
    out = _sanitize_chat_answer(text)
    if not out:
        return ""

    out = re.sub(r"\s+", " ", out).strip()
    if not out:
        return ""

    if out[-1] in ".!?":
        return out

    last_punct = max(out.rfind("."), out.rfind("!"), out.rfind("?"))
    if last_punct >= 24:
        trimmed = out[: last_punct + 1].strip()
        if len(trimmed) >= 12:
            return trimmed

    return f"{out}."


def _memory_unknown_reply(intent: str) -> str:
    if intent == "memory_recap":
        return "Al momento non ho ricordi affidabili da riassumere. Se vuoi, dimmi cosa devo ricordare."
    return "Su questo non ho un ricordo affidabile. Se vuoi, dimmelo e lo memorizzo."


def _token_set(text: str) -> set[str]:
    return {
        tok for tok in _WORD_TOKEN_RE.findall((text or "").lower())
        if len(tok) >= 3 and not tok.isdigit()
    }


@lru_cache(maxsize=4096)
def _query_signals(query: str) -> tuple[bool, bool, tuple[str, ...], bool]:
    q = clean_text(query).lower()
    tokens = _token_set(q)
    if not tokens:
        return False, False, tuple(), False

    has_doc_hint = any(t in _DOC_QUERY_HINTS for t in tokens)
    has_profile_hint = any(t in _PROFILE_QUERY_HINTS for t in tokens)
    has_image_hint = any(t in _IMAGE_QUERY_HINTS for t in tokens)
    has_pdf_hint = any(t in _PDF_QUERY_HINTS for t in tokens)
    has_reference_hint = any(t in _MEMORY_REFERENCE_HINTS for t in tokens)
    has_question_form = ("?" in q) or bool(
        re.search(r"^\s*(chi|come|cosa|dove|quando|perche|a che ora|di dove|dimmi|raccontami|spiegami)\b", q)
    )
    has_personal_anchor = bool(
        re.search(
            r"\b("
            r"hai|sei|vivi|vive|abiti|abita|lavori|lavora|studi|preferisci|bevi|mangi|vai|prendi|usi|tifi|"
            r"sposti|allerg\w*|animali|gatto|cane|patente|password|colloquio|appuntamento|"
            r"dimentic\w*|insicur\w*|chiami|sai"
            r")\b",
            q,
        )
    )

    if re.search(r"\bchi sei\b|\bcome ti chiami\b|\bpresent\w*\b", q):
        has_profile_hint = True
    if has_question_form and has_personal_anchor:
        has_profile_hint = True

    document_query = has_doc_hint or has_image_hint or has_pdf_hint
    profile_query = has_profile_hint and not document_query

    focus_sources: list[str] = []
    if has_image_hint:
        focus_sources.append("image_description")
    if has_doc_hint or has_pdf_hint:
        focus_sources.append("file")

    memory_reference = has_reference_hint or bool(re.search(r"\bricord\w*|\bmemorizz\w*", q))
    return document_query, profile_query, tuple(focus_sources), memory_reference


def _normalize_intent_by_query(intent: str, query: str) -> str:
    current = (intent or "").strip().lower() or "chitchat"
    q = clean_text(query or "")
    document_query, profile_query, _, memory_reference = _query_signals(q)
    if _RECAP_QUERY_RE.search(q):
        return "memory_recap"
    if current in {"chitchat", "creative_open", "session_recap", "memory_recap"}:
        if (document_query or profile_query or memory_reference) and (("?" in q) or q.lower().startswith(("dimmi", "raccontami", "spiegami"))):
            return "memory_qna"
    return current


def _is_document_memory_query(query: str) -> bool:
    document_query, _, _, _ = _query_signals(query or "")
    return document_query


def _is_profile_memory_query(query: str) -> bool:
    _, profile_query, _, _ = _query_signals(query or "")
    return profile_query


def _filename_overlap_boost(query: str, meta: dict) -> float:
    filename = clean_text(str((meta or {}).get("source_filename") or "")).lower()
    if not filename:
        return 0.0
    q_tokens = _token_set(query)
    if not q_tokens:
        return 0.0
    hit = sum(1 for tok in q_tokens if len(tok) >= 4 and tok in filename)
    if hit <= 0:
        return 0.0
    return min(0.18, 0.06 * hit)


def _support_token_set(query: str, recent_conversation: str, factual_docs: List[str]) -> set[str]:
    combined = " ".join([query or "", recent_conversation or "", " ".join(factual_docs or [])])
    return _token_set(combined)


def _unsupported_token_ratio(answer: str, support: set[str], stopwords: set[str]) -> float:
    text = clean_text(answer or "")
    if len(text) < 24:
        return 0.0
    if not support:
        return 0.0
    ans_tokens = [
        tok for tok in _token_set(text)
        if len(tok) >= 4 and tok not in stopwords
    ]
    if len(ans_tokens) < 4:
        return 0.0

    unsupported = [tok for tok in ans_tokens if tok not in support]
    return float(len(unsupported)) / float(len(ans_tokens))


def _lexical_overlap_ratio(query: str, doc: str) -> float:
    q_tokens = _token_set(query)
    if not q_tokens:
        return 0.0
    d_tokens = _token_set(doc)
    if not d_tokens:
        return 0.0
    overlap = len(q_tokens & d_tokens)
    return float(overlap) / float(len(q_tokens))


_ALLOWED_CHAT_INTENTS = {"chitchat", "session_recap", "memory_recap", "memory_qna", "creative_open"}


def _truncate_for_prompt(text: str, max_chars: int = 1800) -> str:
    cleaned = clean_text(text or "")
    if len(cleaned) <= max_chars:
        return cleaned
    return cleaned[:max_chars].rstrip() + "..."


def _rewrite_answer_with_guardrails(
    query: str,
    recent_conversation: str,
    factual_context: str,
    original_answer: str,
    mode: str,
) -> Optional[str]:
    grounded = mode in {"grounded", "grounded_strict"}
    strict_grounded = mode == "grounded_strict"
    if strict_grounded:
        system = (
            "Produci una risposta estrattiva e rigorosa usando solo la MEMORIA_FACTUAL. "
            "Non aggiungere inferenze, abitudini o dettagli non presenti. "
            "Se manca il fatto richiesto, dichiara brevemente che non c'e memoria affidabile."
        )
        style_rule = (
            "Rispondi in massimo 2 frasi brevi. "
            "Copri solo i fatti richiesti dalla domanda, senza aggiunte."
        )
        timeout = 40
        num_predict = 120
        factual_chars = 1700
    elif grounded:
        system = (
            "Riscrivi la risposta usando il contesto factual fornito. "
            "Non negare di avere informazioni quando il contesto factual e presente. "
            "Non inventare fatti oltre al contesto. "
            "Tratta la risposta originale come bozza potenzialmente errata: correggila se e in conflitto con la memoria factual."
        )
        style_rule = (
            "Riscrivi in modo chiaro e concreto, massimo 4 frasi. "
            "Se la domanda chiede dettagli puntuali (nome, luogo, orario, relazione), riportali in modo esplicito."
        )
        timeout = 45
        num_predict = 180
        factual_chars = 1600
    else:
        system = (
            "Riscrivi la risposta in italiano naturale, breve e accogliente. "
            "Non inventare dettagli autobiografici non presenti nel contesto. "
            "Se il contesto non contiene fatti personali, usa formulazioni neutrali."
        )
        style_rule = "Riscrivi in 1-3 frasi, senza aggiungere fatti nuovi."
        timeout = 40
        num_predict = 120
        factual_chars = 1200

    user = (
        f"MESSAGGIO_UTENTE:\n{query}\n\n"
        f"CONTESTO_RECENTE:\n{_truncate_for_prompt(recent_conversation, 1000)}\n\n"
        f"MEMORIA_FACTUAL:\n{_truncate_for_prompt(factual_context, factual_chars)}\n\n"
        f"RISPOSTA_DA_RISCRIVERE:\n{original_answer}\n\n"
        f"{style_rule}"
    )
    try:
        rewritten = ollama_chat(
            [
                {"role": "system", "content": system},
                {"role": "user", "content": user},
            ],
            timeout=timeout,
            num_predict_override=num_predict,
        ).strip()
        final = _finalize_chat_answer(rewritten)
        return final if final else None
    except Exception:
        return None


def _answer_denies_available_context(answer: str) -> bool:
    txt = clean_text(answer or "")
    if not txt:
        return False
    return _CONTEXT_DENIAL_RE.search(txt) is not None


def _extract_first_json_object(raw: str) -> Optional[dict]:
    txt = (raw or "").strip()
    if not txt:
        return None

    if txt.startswith("{"):
        try:
            obj = json.loads(txt)
            if isinstance(obj, dict):
                return obj
        except Exception:
            pass

    m = re.search(r"\{.*?\}", txt, re.DOTALL)
    if not m:
        return None
    try:
        obj = json.loads(m.group(0))
        return obj if isinstance(obj, dict) else None
    except Exception:
        return None


def _route_chat_intent(query: str, recent_conversation: str, has_memory: bool) -> str:
    router_system = (
        "Classifica l'intento del messaggio utente in UNA sola etichetta tra: "
        "chitchat, session_recap, memory_recap, memory_qna, creative_open. "
        "Rispondi SOLO con JSON valido: {\"intent\":\"...\",\"confidence\":0.0}. "
        "Non aggiungere testo fuori dal JSON."
    )
    router_user = (
        f"HAS_MEMORY: {str(bool(has_memory)).lower()}\n"
        f"RECENT_CONVERSATION:\n{_truncate_for_prompt(recent_conversation, 1200)}\n\n"
        f"USER_MESSAGE:\n{_truncate_for_prompt(query, 600)}\n\n"
        "Criteri: "
        "session_recap=chiede cosa ci siamo detti prima/ultima volta; "
        "memory_recap=chiede cosa hai memorizzato/ricordi in generale; "
        "memory_qna=domanda su fatti personali/memoria; "
        "creative_open=richiesta aperta/creativa; "
        "chitchat=saluti e conversazione sociale. "
        "Un saluto breve (es. ciao, ehi, buongiorno) senza richiesta esplicita di memoria e sempre chitchat."
    )
    try:
        raw = ollama_chat(
            [
                {"role": "system", "content": router_system},
                {"role": "user", "content": router_user},
            ],
            timeout=30,
            num_predict_override=max(8, RAG_INTENT_ROUTER_NUM_PREDICT),
        ).strip()
        obj = _extract_first_json_object(raw)
        if obj:
            intent = str(obj.get("intent", "")).strip().lower()
            if intent in _ALLOWED_CHAT_INTENTS:
                return intent
    except Exception:
        pass

    return "chitchat"


# -----------------------------
# Supporto Ollama
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


def ollama_chat(
    messages: list[dict[str, str]],
    timeout: int = 600,
    num_predict_override: Optional[int] = None,
) -> str:
    options: dict[str, Any] = {
        "temperature": CHAT_TEMPERATURE,
        "top_p": CHAT_TOP_P,
        "repeat_penalty": CHAT_REPEAT_PENALTY,
    }
    effective_num_predict = CHAT_NUM_PREDICT if num_predict_override is None else int(num_predict_override)
    if effective_num_predict > 0:
        options["num_predict"] = effective_num_predict

    data = _post_json(
        f"{OLLAMA_HOST}/api/chat",
        {
            "model": CHAT_MODEL,
            "messages": messages,
            "stream": False,
            "options": options,
        },
        timeout=timeout,
    )
    return (data.get("message") or {}).get("content", "") or ""


def _embed_one_or_http_500(text: str) -> List[float]:
    try:
        return ollama_embed_many([text])[0]
    except Exception as e:
        raise HTTPException(status_code=500, detail=f"Errore embedding: {e}")


def _run_startup_warmup() -> None:
    warmup_embed_text = "warmup"
    warmup_chat_text = "Ciao."
    warmup_embed_timeout = 45
    warmup_chat_timeout = 60
    warmup_chat_num_predict = 8
    started_total = time.perf_counter()

    started = time.perf_counter()
    try:
        payload: dict[str, Any] = {
            "model": EMBED_MODEL,
            "input": warmup_embed_text,
        }
        _ = _post_json(
            f"{OLLAMA_HOST}/api/embed",
            payload,
            timeout=max(1, warmup_embed_timeout),
        )
        elapsed = time.perf_counter() - started
        print(
            f"[INFO] RAG warmup embed complete in {elapsed:.2f}s "
            f"(model={EMBED_MODEL}).",
            flush=True,
        )
    except Exception as exc:
        print(f"[WARN] RAG warmup embed failed: {exc}", flush=True)

    started = time.perf_counter()
    try:
        _ = ollama_chat(
            messages=[{"role": "user", "content": warmup_chat_text}],
            timeout=max(1, warmup_chat_timeout),
            num_predict_override=warmup_chat_num_predict,
        )
        elapsed = time.perf_counter() - started
        print(
            f"[INFO] RAG warmup chat complete in {elapsed:.2f}s "
            f"(model={CHAT_MODEL}).",
            flush=True,
        )
    except Exception as exc:
        print(f"[WARN] RAG warmup chat failed: {exc}", flush=True)

    elapsed_total = time.perf_counter() - started_total
    print(f"[INFO] RAG startup warmup finished in {elapsed_total:.2f}s.", flush=True)


# -----------------------------
# Inizializzazione Chroma (cartella persistente per avatar)
# -----------------------------

_AVATAR_CLIENTS: dict[str, ChromaClientAPI] = {}
_AVATAR_LOCK = threading.Lock()
_LOG_WRITE_LOCK = threading.Lock()
_ALLOWED_LOG_INPUT_MODES = {"voice", "keyboard"}
_SESSION_HISTORY_LOCK = threading.Lock()
_SESSION_HISTORIES: dict[tuple[str, str], deque[tuple[str, str]]] = {}

def _safe_avatar_key(avatar_id: str) -> str:
    s = (avatar_id or "default").strip()
    # Usiamo solo caratteri sicuri per i nomi cartella su Windows
    s = re.sub(r"[^a-zA-Z0-9_-]+", "_", s)
    if not s:
        s = "default"
    return s[:64]


def _safe_session_key(session_id: str) -> str:
    s = (session_id or "").strip()
    s = re.sub(r"[^a-zA-Z0-9_-]+", "_", s)
    if not s:
        s = _new_conversation_session_id()
    return s[:96]


def _effective_session_turns() -> int:
    return max(1, min(20, int(RAG_SESSION_TURNS)))


def _session_history_key(avatar_id: str, session_id: Optional[str]) -> Optional[tuple[str, str]]:
    raw_session = (session_id or "").strip()
    if not raw_session:
        return None
    return (_safe_avatar_key(avatar_id), _safe_session_key(raw_session))


def _ensure_session_history(avatar_id: str, session_id: Optional[str]) -> Optional[str]:
    key = _session_history_key(avatar_id, session_id)
    if key is None:
        return None

    with _SESSION_HISTORY_LOCK:
        hist = _SESSION_HISTORIES.get(key)
        if hist is None:
            _SESSION_HISTORIES[key] = deque(maxlen=_effective_session_turns())
        elif hist.maxlen != _effective_session_turns():
            _SESSION_HISTORIES[key] = deque(hist, maxlen=_effective_session_turns())
    return key[1]


def _reset_session_history(avatar_id: str, session_id: Optional[str]) -> Optional[str]:
    key = _session_history_key(avatar_id, session_id)
    if key is None:
        return None
    with _SESSION_HISTORY_LOCK:
        _SESSION_HISTORIES[key] = deque(maxlen=_effective_session_turns())
    return key[1]


def _append_session_turn(avatar_id: str, session_id: Optional[str], user_text: str, assistant_text: str) -> None:
    key = _session_history_key(avatar_id, session_id)
    if key is None:
        return

    user_turn = clean_text(user_text)[:800]
    assistant_turn = clean_text(assistant_text)[:1600]
    if not user_turn and not assistant_turn:
        return

    with _SESSION_HISTORY_LOCK:
        hist = _SESSION_HISTORIES.get(key)
        if hist is None:
            hist = deque(maxlen=_effective_session_turns())
            _SESSION_HISTORIES[key] = hist
        elif hist.maxlen != _effective_session_turns():
            hist = deque(hist, maxlen=_effective_session_turns())
            _SESSION_HISTORIES[key] = hist
        hist.append((user_turn, assistant_turn))


def _build_recent_conversation_context(avatar_id: str, session_id: Optional[str]) -> str:
    key = _session_history_key(avatar_id, session_id)
    if key is None:
        return "- Nessun turno precedente disponibile."

    with _SESSION_HISTORY_LOCK:
        hist = list(_SESSION_HISTORIES.get(key, []))

    if not hist:
        return "- Nessun turno precedente disponibile."

    lines: list[str] = []
    for user_turn, assistant_turn in hist[-_effective_session_turns():]:
        if user_turn:
            lines.append(f"- Utente: {user_turn}")
        if assistant_turn:
            lines.append(f"- Avatar: {assistant_turn}")
    return "\n".join(lines) if lines else "- Nessun turno precedente disponibile."


def _recent_user_only_context(recent_conversation: str) -> str:
    lines = []
    for line in (recent_conversation or "").splitlines():
        line = (line or "").strip()
        if line.startswith("- Utente:"):
            lines.append(line)
    return "\n".join(lines) if lines else "- Nessun turno precedente disponibile."


def _new_conversation_session_id() -> str:
    stamp = datetime.now().astimezone().strftime("%Y%m%d_%H%M%S")
    return f"{stamp}_{uuid.uuid4().hex[:8]}"


def _format_local_ts(ts: datetime) -> str:
    return ts.astimezone().strftime("%Y-%m-%d %H:%M:%S %Z")


def _session_log_file_path(avatar_id: str, session_id: str) -> tuple[str, str, str]:
    safe_avatar = _safe_avatar_key(avatar_id)
    safe_session = _safe_session_key(session_id)
    avatar_dir = os.path.join(RAG_LOG_DIR, safe_avatar)
    os.makedirs(avatar_dir, exist_ok=True)
    return os.path.join(avatar_dir, f"{safe_session}.log"), safe_avatar, safe_session


def _clear_avatar_logs(avatar_id: str) -> tuple[bool, Optional[str], str]:
    avatar_log_dir = os.path.join(RAG_LOG_DIR, _safe_avatar_key(avatar_id))
    ok, err = _rmtree_force(avatar_log_dir)
    return ok, err, avatar_log_dir


def _ensure_session_log_header(
    log_path: str,
    avatar_id: str,
    safe_avatar: str,
    safe_session: str,
    created_at: datetime,
) -> None:
    if os.path.exists(log_path):
        return

    avatar_display = (avatar_id or "").strip() or "default"
    created = _format_local_ts(created_at)
    header = (
        "============================================================\n"
        "SOULFRAME AVATAR CONVERSATION LOG\n"
        f"Avatar      : {avatar_display}\n"
        f"Avatar Safe : {safe_avatar}\n"
        f"Session ID  : {safe_session}\n"
        f"Created At  : {created}\n"
        "============================================================\n\n"
    )
    with open(log_path, "w", encoding="utf-8", newline="\n") as handle:
        handle.write(header)


def _normalize_input_mode(input_mode: Optional[str]) -> str:
    mode = (input_mode or "").strip().lower()
    return mode if mode in _ALLOWED_LOG_INPUT_MODES else "unknown"


def _start_conversation_log_session(avatar_id: str, session_id: Optional[str] = None) -> tuple[str, str]:
    if not (avatar_id or "").strip():
        raise ValueError("avatar_id is required")

    safe_session = _safe_session_key(session_id or _new_conversation_session_id())
    created_at = datetime.now().astimezone()
    log_path, safe_avatar, safe_session = _session_log_file_path(avatar_id, safe_session)

    with _LOG_WRITE_LOCK:
        _ensure_session_log_header(
            log_path=log_path,
            avatar_id=avatar_id,
            safe_avatar=safe_avatar,
            safe_session=safe_session,
            created_at=created_at,
        )
    return safe_session, log_path


def _append_conversation_log_turn(
    avatar_id: str,
    session_id: Optional[str],
    input_mode: Optional[str],
    user_text: str,
    rag_text: str,
) -> tuple[str, str]:
    if not (avatar_id or "").strip():
        raise ValueError("avatar_id is required")

    safe_session = _safe_session_key(session_id or _new_conversation_session_id())
    now_local = datetime.now().astimezone()
    log_path, safe_avatar, safe_session = _session_log_file_path(avatar_id, safe_session)
    mode = _normalize_input_mode(input_mode)
    user_block = (user_text or "").strip() or "(vuoto)"
    rag_block = (rag_text or "").strip() or "(vuoto)"

    with _LOG_WRITE_LOCK:
        _ensure_session_log_header(
            log_path=log_path,
            avatar_id=avatar_id,
            safe_avatar=safe_avatar,
            safe_session=safe_session,
            created_at=now_local,
        )
        with open(log_path, "a", encoding="utf-8", newline="\n") as handle:
            handle.write(f"[{_format_local_ts(now_local)}] USER INPUT ({mode})\n")
            handle.write(f"{user_block}\n\n")
            handle.write(f"[{_format_local_ts(now_local)}] RAG OUTPUT\n")
            handle.write(f"{rag_block}\n\n")
            handle.write("------------------------------------------------------------\n")

    return safe_session, log_path

def _get_client_for_avatar(avatar_id: str) -> ChromaClientAPI:
    key = _safe_avatar_key(avatar_id)
    with _AVATAR_LOCK:
        c = _AVATAR_CLIENTS.get(key)
        if c is not None:
            return c
        d = os.path.join(PERSIST_ROOT, key)
        os.makedirs(d, exist_ok=True)
        try:
            # Usiamo impostazioni minimaliste senza forzature API per evitare conflitti hnswlib
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
# Supporto lock Windows per Chroma
# -----------------------------
def _stop_chroma_system(client) -> None:
    """Prova a stoppare il 'system' condiviso di Chroma per liberare lock su Windows."""
    if client is None:
        return

    # 1) stop diretto se presente
    try:
        sys = getattr(client, "_system", None)
        if sys and hasattr(sys, "stop"):
            sys.stop()
    except Exception:
        pass

    # 2) rimuoviamo dalla cache SharedSystemClient (se presente)
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


def _safe_collection_count(col: Any) -> int:
    try:
        return col.count()
    except Exception:
        return 0


# -----------------------------
# Estrazione testo file
# -----------------------------

def describe_image_with_gemini(image_bytes: bytes, prompt: str = "Descrivi dettagliatamente questa immagine in italiano.") -> str:
    """Usa Gemini Vision per descrivere un'immagine."""
    if _gemini_client is None or not GEMINI_API_KEY:
        raise HTTPException(status_code=500, detail="Gemini non configurato. Imposta GEMINI_API_KEY.")

    try:
        if genai_types is None:
            raise HTTPException(status_code=500, detail="google.genai non disponibile.")

        gt = genai_types
        assert gt is not None

        parts = [
            prompt,
            gt.Part.from_bytes(data=image_bytes, mime_type="image/png"),
        ]

        response = _gemini_client.models.generate_content(
            model="gemini-2.0-flash",
            contents=parts,
        )

        text = getattr(response, "text", None)
        if text:
            return text

        # Ripiego: proviamo a leggere da candidates (safe su null per Pylance e per SDK diversi)
        candidates = getattr(response, "candidates", None)
        if isinstance(candidates, list) and candidates:
            try:
                cand0 = candidates[0]
                content = getattr(cand0, "content", None)
                parts2 = getattr(content, "parts", None) if content is not None else None
                if isinstance(parts2, list) and parts2:
                    t2 = getattr(parts2[0], "text", None)
                    if isinstance(t2, str):
                        return t2
            except Exception:
                pass
        return ""
    except Exception as e:
        raise HTTPException(status_code=500, detail=f"Errore Gemini Vision: {e}")


def _ensure_ocr_ready() -> bool:
    """Verifica e configura OCR. Ritorna True se pronto, False altrimenti."""
    if pytesseract is None or Image is None:
        return False
    
    # Configuriamo tesseract_cmd se specificato (oppure usiamo il predefinito Windows)
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
    assert Image is not None
    assert pytesseract is not None
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
    """Estrae testo da PDF usando SEMPRE OCR (piu' affidabile del testo embedded).
    
    Il testo embedded nei PDF e' spesso corrotto, non selezionabile o mal formattato.
    OCR garantisce testo pulito e leggibile per il RAG.
    """
    if fitz is None:
        raise HTTPException(status_code=500, detail="PDF parsing non disponibile: installa 'pymupdf'.")

    if not _ensure_ocr_ready():
        raise HTTPException(
            status_code=500, 
            detail="OCR non disponibile. Installa tesseract-ocr e pytesseract/pillow."
        )

    assert Image is not None
    assert pytesseract is not None

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
                # Linearizziamo le tabelle per migliorare la similarita' semantica
                ocr_text = linearize_table_text(ocr_text)
            except Exception:
                skipped_pages.append(i + 1)
                continue

            # Accetta solo se non e' vuoto
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
    - Vector (40%): similarita' semantica
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

        # Filtra candidati non testuali/None (alcuni backend possono ritornare doc None)
        candidates: list[tuple[str, dict, float | None]] = []
        for d, m, dist in zip(vec_docs, vec_metas, vec_distances):
            if not isinstance(d, str) or not d.strip():
                continue
            candidates.append((d, m or {}, dist if isinstance(dist, (int, float)) else None))

        if not candidates:
            return [], []

        # 2) BM25 search solo sui candidati
        tokenized_docs = [d.lower().split() for d, _, _ in candidates]
        bm25 = BM25Okapi(tokenized_docs)
        query_tokens = query.lower().split()
        bm25_scores = bm25.get_scores(query_tokens)
        
        # Normalizza scores
        max_bm25 = max(bm25_scores) if max(bm25_scores) > 0 else 1
        bm25_norm = [s / max_bm25 for s in bm25_scores]
        
        vec_scores_raw = [max(0.0, (1 - dist)) if dist is not None else 0.0 for _, _, dist in candidates]
        max_vec = max(vec_scores_raw) if max(vec_scores_raw) > 0 else 1
        vec_norm = [s / max_vec for s in vec_scores_raw]
        
        # 3) Combina score
        combined: list[tuple[float, str, dict]] = []
        for i, (doc, meta, _) in enumerate(candidates):
            score = (bm25_weight * bm25_norm[i]) + ((1 - bm25_weight) * vec_norm[i])
            safe_meta = dict(meta or {})
            safe_meta["_hybrid_score"] = round(float(score), 6)
            safe_meta["_vector_similarity"] = round(float(vec_scores_raw[i]), 6)
            safe_meta["_bm25_norm"] = round(float(bm25_norm[i]), 6)
            combined.append((float(score), doc, safe_meta))
        
        # 4) Ordina e ritorna top_k
        combined.sort(key=lambda x: x[0], reverse=True)
        result_docs = [doc for _, doc, _ in combined[:top_k]]
        result_metas = [meta for _, _, meta in combined[:top_k]]
        
        return result_docs, result_metas
        
    except Exception as e:
        # Ripiego: usiamo solo la ricerca vettoriale
        try:
            res = col.query(
                query_embeddings=[query_embedding],
                n_results=top_k,
                include=["documents", "metadatas", "distances"],
            )
            docs = (res.get("documents") or [[]])[0]
            metas = (res.get("metadatas") or [[]])[0]
            dists = (res.get("distances") or [[]])[0]
            out_metas: list[dict] = []
            for m, dist in zip(metas, dists):
                safe_meta = dict(m or {})
                dist_val = float(dist) if isinstance(dist, (int, float)) else 1.0
                safe_meta["_hybrid_score"] = round(max(0.0, 1.0 - dist_val), 6)
                safe_meta["_vector_similarity"] = round(max(0.0, 1.0 - dist_val), 6)
                out_metas.append(safe_meta)
            return docs, out_metas
        except Exception:
            return [], []


def _hybrid_search_ranked(
    query: str,
    query_embedding: List[float],
    col,
    top_k: int = 20,
    bm25_weight: float = 0.6,
) -> List[Tuple[float, str, dict]]:
    """Ricerca ibrida con score combinato per hit."""
    docs, metas = _hybrid_search(
        query=query,
        query_embedding=query_embedding,
        col=col,
        top_k=top_k,
        bm25_weight=bm25_weight,
    )
    ranked: list[tuple[float, str, dict]] = []
    for idx, (doc, meta) in enumerate(zip(docs, metas)):
        safe_meta = dict(meta or {})
        base_score = 1.0 - (float(idx) * 0.03)
        if "_hybrid_score" in safe_meta:
            try:
                base_score = float(safe_meta["_hybrid_score"])
            except Exception:
                pass
        ranked.append((max(0.0, min(1.0, base_score)), doc, safe_meta))
    return ranked


def _vector_search_ranked(
    col: Any,
    query_embedding: List[float],
    top_k: int,
    where: Optional[dict] = None,
) -> List[Tuple[float, str, dict]]:
    if top_k <= 0:
        return []
    try:
        kwargs: dict[str, Any] = {
            "query_embeddings": [query_embedding],
            "n_results": top_k,
            "include": ["documents", "metadatas", "distances"],
        }
        if where:
            kwargs["where"] = where
        res = col.query(**kwargs)
    except Exception:
        return []

    docs = (res.get("documents") or [[]])[0]
    metas = (res.get("metadatas") or [[]])[0]
    dists = (res.get("distances") or [[]])[0]
    ranked: list[tuple[float, str, dict]] = []
    for doc, meta, dist in zip(docs, metas, dists):
        if not isinstance(doc, str) or not doc.strip():
            continue
        dist_val = float(dist) if isinstance(dist, (int, float)) else 1.0
        vec_sim = max(0.0, 1.0 - dist_val)
        safe_meta = dict(meta or {})
        safe_meta["_vector_similarity"] = round(vec_sim, 6)
        safe_meta["_hybrid_score"] = round(vec_sim, 6)
        ranked.append((vec_sim, doc, safe_meta))
    return ranked


def _select_factual_hits(
    ranked_hits: List[Tuple[float, str, dict]],
    query: str,
    top_k: int,
) -> Tuple[List[str], List[dict]]:
    if not ranked_hits or top_k <= 0:
        return [], []

    factual_score_min = max(0.0, min(1.0, float(RAG_FACTUAL_SCORE_MIN)))
    factual_score_gap = max(0.0, float(RAG_FACTUAL_SCORE_GAP_MIN))
    best_score = ranked_hits[0][0]
    query_token_count = len(_token_set(query))
    short_query = query_token_count <= 2
    selected: list[tuple[str, dict]] = []

    for score, doc, meta in ranked_hits:
        if len(selected) >= top_k:
            break

        lexical = _lexical_overlap_ratio(query, doc)
        vector_similarity = 0.0
        try:
            vector_similarity = float((meta or {}).get("_vector_similarity", 0.0))
        except Exception:
            vector_similarity = 0.0

        focused_source_match = bool((meta or {}).get("_focused_source_match"))
        profile_source_match = bool((meta or {}).get("_profile_source_match"))
        score_ok = score >= factual_score_min
        near_best = (best_score - score) <= factual_score_gap
        lexical_strong = lexical >= 0.18 and score >= max(0.0, factual_score_min - 0.06)
        semantic_strong = vector_similarity >= 0.55 and score >= max(0.0, factual_score_min - 0.03)
        focused_ok = focused_source_match and score >= max(0.20, factual_score_min - 0.12)
        profile_ok = profile_source_match and score >= max(0.22, factual_score_min - 0.12)

        if short_query:
            keep = lexical >= 0.12 or semantic_strong or focused_ok or profile_ok
        else:
            keep = lexical_strong or semantic_strong or focused_ok or profile_ok or (score_ok and near_best and lexical >= 0.08)

        if not keep:
            continue

        safe_meta = dict(meta or {})
        safe_meta["_hybrid_score"] = round(float(score), 4)
        safe_meta["_lexical_overlap"] = round(float(lexical), 4)
        safe_meta["_vector_similarity"] = round(float(vector_similarity), 4)
        selected.append((doc, safe_meta))

    if not selected:
        top_score, top_doc, top_meta = ranked_hits[0]
        if bool((top_meta or {}).get("_profile_source_match")) and top_score >= max(0.18, factual_score_min - 0.15):
            safe_meta = dict(top_meta or {})
            safe_meta["_hybrid_score"] = round(float(top_score), 4)
            safe_meta["_fallback_profile_query"] = True
            return [top_doc], [safe_meta]

        if short_query:
            # Fallback prudente per query molto corte (es. "dove vivi?"):
            # evita falsi negativi aggressivi quando c'e' una memoria chiaramente dominante.
            fallback_min = max(0.12, factual_score_min - 0.18)
            if top_score >= fallback_min:
                safe_meta = dict(top_meta or {})
                safe_meta["_hybrid_score"] = round(float(top_score), 4)
                safe_meta["_fallback_short_query"] = True
                return [top_doc], [safe_meta]
        return [], []

    return [d for d, _ in selected], [m for _, m in selected]


def _select_memory_recap_hits(
    ranked_hits: List[Tuple[float, str, dict]],
    top_k: int,
) -> Tuple[List[str], List[dict]]:
    if not ranked_hits or top_k <= 0:
        return [], []

    min_score = max(0.15, min(0.95, float(RAG_FACTUAL_SCORE_MIN) - 0.12))
    selected_docs: List[str] = []
    selected_metas: List[dict] = []
    for score, doc, meta in ranked_hits:
        if len(selected_docs) >= top_k:
            break
        if score < min_score:
            continue
        selected_docs.append(doc)
        safe_meta = dict(meta or {})
        safe_meta["_hybrid_score"] = round(float(score), 4)
        selected_metas.append(safe_meta)

    if not selected_docs:
        return [], []

    return _dedupe_chunks(selected_docs, selected_metas)


# -----------------------------
# Modelli API
# -----------------------------
app = FastAPI(title="SOULFRAME RAG Server", version="3.0")

app.add_middleware(
    CORSMiddleware,
    allow_origins=["*"],
    allow_credentials=False,
    allow_methods=["*"],
    allow_headers=["*"],
)


@app.on_event("startup")
def _on_startup() -> None:
    os.makedirs(PERSIST_ROOT, exist_ok=True)
    os.makedirs(RAG_LOG_DIR, exist_ok=True)
    try:
        _run_startup_warmup()
    except Exception as exc:
        # Best-effort: non bloccare l'avvio del servizio.
        print(f"[WARN] RAG startup warmup crashed: {exc}", flush=True)


# Gestore globale delle eccezioni
@app.exception_handler(Exception)
async def global_exception_handler(request: Request, exc: Exception):
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
    session_id: Optional[str] = None
    input_mode: Optional[str] = None
    log_conversation: bool = False


class ChatSessionStartReq(BaseModel):
    avatar_id: str


# -----------------------------
# Rilevamento memoria automatica
# -----------------------------
# Pattern che catturano frasi in cui l'utente chiede di memorizzare qualcosa.
# Supporta italiano e inglese. Il confronto non distingue maiuscole/minuscole.
_REMEMBER_PATTERNS = [
    # Italiano
    r"(?:ricorda(?:ti)?|memorizza|tieni a mente|segna(?:ti)?|annota(?:ti)?|non (?:ti )?dimenticare)\s+(?:che\s+)?(.+)",
    r"(?:devi|dovresti)\s+(?:ricorda(?:re|rti)|memorizzare|sapere)\s+(?:che\s+)?(.+)",
    r"(?:sappi|tieni presente)\s+(?:che\s+)?(.+)",
    # Inglese
    r"(?:remember|memorize|keep in mind|note|don'?t forget)\s+(?:that\s+)?(.+)",
]
_REMEMBER_RES = [re.compile(p, re.IGNORECASE | re.DOTALL) for p in _REMEMBER_PATTERNS]


def _detect_remember_intent(text: str) -> Optional[str]:
    """Controlla se il testo contiene un intento 'ricorda'.
    Ritorna il contenuto da memorizzare (gruppo catturato) oppure None.
    """
    t = (text or "").strip()
    if not t:
        return None
    for pattern in _REMEMBER_RES:
        m = pattern.search(t)
        if m:
            content = m.group(1).strip().rstrip(".")
            # Deve avere almeno qualche contenuto significativo
            if len(content) >= 5:
                return content
    return None


def _auto_remember(avatar_id: str, original_text: str, remember_content: str, col: Any) -> Optional[str]:
    """Salva automaticamente il contenuto nella memoria RAG.
    Ritorna l'ID del documento salvato, oppure None in caso di errore.
    """
    try:
        txt = clean_text(remember_content)
        if len(txt) < MIN_CHUNK_CHARS:
            # Testo troppo corto, usa il messaggio originale intero
            txt = clean_text(original_text)
        if len(txt) < MIN_CHUNK_CHARS or looks_like_garbage(txt):
            return None

        emb = ollama_embed_many([txt])[0]
        _id = str(uuid.uuid4())
        meta: dict[str, Any] = {
            "source_type": "auto_remember_voice",
            "memory_role": _memory_role_for_text(txt),
            "avatar_id": avatar_id,
            "ts": int(time.time()),
            "original_utterance": original_text[:500], #
        }
        col.add(
            ids=[_id],
            embeddings=[emb],
            documents=[txt],
            metadatas=[cast(ChromaMetadata, meta)],
        )
        print(f"[AUTO-REMEMBER] avatar={avatar_id} saved id={_id} text={txt[:80]}...")
        return _id
    except Exception as e:
        print(f"[AUTO-REMEMBER] Errore: {e}")
        traceback.print_exc()
        return None


def _build_context_from_docs(docs: List[str], metas: List[dict], max_chars: int) -> str:
    parts: List[str] = []
    total = 0
    for d, m in zip(docs, metas):
        if not d:
            continue
        src = (m or {}).get("source_filename") or (m or {}).get("source_type") or "memoria"
        page = (m or {}).get("page")
        tag = f"[{src}{' p.' + str(page) if page else ''}]"
        piece = f"{tag} {d}"
        if total + len(piece) > max_chars:
            break
        parts.append(piece)
        total += len(piece)
    return "\n\n".join(parts)


def _build_rag_used_payload(docs: List[str], metas: List[dict]) -> List[dict]:
    rag_used: List[dict] = []
    for d, m in zip(docs, metas):
        if not d:
            continue
        safe_meta = {
            k: v for k, v in (m or {}).items()
            if not str(k).startswith("_")
        }
        rag_used.append({"text": d, "meta": safe_meta})
    return rag_used


def _retrieve_context_for_intent(
    col: Any,
    query: str,
    intent: str,
    requested_top_k: int,
) -> tuple[List[str], List[dict]]:
    factual_top_k = min(max(0, int(requested_top_k)), max(1, int(RAG_CHAT_TOP_K_CAP)))
    if factual_top_k <= 0:
        return [], []
    if intent in {"chitchat", "session_recap", "creative_open"}:
        return [], []

    try:
        if intent == "memory_qna":
            document_query, profile_query, focus_sources, memory_reference = _query_signals(query or "")
            qemb = _embed_one_or_http_500(query)
            profile_boosted: list[tuple[float, str, dict]] = []
            if focus_sources:
                focused_ranked: list[tuple[float, str, dict]] = []
                focused_result_cap = 1 if focus_sources == ["image_description"] else max(1, min(factual_top_k, 4))
                for source_type in focus_sources:
                    ranked_focus_src = _vector_search_ranked(
                        col=col,
                        query_embedding=qemb,
                        top_k=max(3, min(factual_top_k, 8)),
                        where={"source_type": source_type},
                    )
                    for score, doc, meta in ranked_focus_src:
                        lexical = _lexical_overlap_ratio(query, doc)
                        boosted = min(
                            1.0,
                            max(score, 0.18) + 0.16 + min(0.08, lexical) + _filename_overlap_boost(query, meta or {}),
                        )
                        safe_meta = dict(meta or {})
                        safe_meta["_hybrid_score"] = round(float(boosted), 6)
                        safe_meta["_focused_source_match"] = True
                        focused_ranked.append((boosted, doc, safe_meta))
                focused_ranked.sort(key=lambda x: x[0], reverse=True)
                focused_docs, focused_metas = _select_factual_hits(
                    ranked_hits=focused_ranked,
                    query=query,
                    top_k=focused_result_cap,
                )
                if focused_docs:
                    return _dedupe_chunks(focused_docs, focused_metas)

            if profile_query:
                profile_ranked = _vector_search_ranked(
                    col=col,
                    query_embedding=qemb,
                    top_k=max(3, min(factual_top_k, 8)),
                    where={"source_type": {"$in": list(_PROFILE_MEMORY_SOURCE_TYPES)}},
                )
                if profile_ranked:
                    for score, doc, meta in profile_ranked:
                        lexical = _lexical_overlap_ratio(query, doc)
                        boosted = min(1.0, max(score, 0.30) + 0.22 + min(0.08, lexical))
                        safe_meta = dict(meta or {})
                        safe_meta["_hybrid_score"] = round(float(boosted), 6)
                        safe_meta["_profile_source_match"] = True
                        profile_boosted.append((boosted, doc, safe_meta))

            if not document_query and not profile_query and not memory_reference:
                # Evita di inquinare il chitchat con memoria factual non pertinente.
                return [], []

            ranked = _hybrid_search_ranked(
                query=query,
                query_embedding=qemb,
                col=col,
                top_k=max(2, min(factual_top_k, 6)) if memory_reference else factual_top_k,
                bm25_weight=0.6,
            )
            if profile_boosted:
                ranked = sorted(profile_boosted + ranked, key=lambda x: x[0], reverse=True)
            docs, metas = _select_factual_hits(ranked_hits=ranked, query=query, top_k=factual_top_k)
            if docs:
                return _dedupe_chunks(docs, metas)

            if document_query:
                doc_probe_ranked = _vector_search_ranked(
                    col=col,
                    query_embedding=qemb,
                    top_k=max(2, min(factual_top_k, 6)),
                    where={"source_type": {"$in": ["file", "image_description", "image_ocr"]}},
                )
                doc_probe_docs, doc_probe_metas = _select_factual_hits(
                    ranked_hits=doc_probe_ranked,
                    query=query,
                    top_k=max(1, min(factual_top_k, 3)),
                )
                if doc_probe_docs:
                    return _dedupe_chunks(doc_probe_docs, doc_probe_metas)
                if doc_probe_ranked:
                    top_score, top_doc, top_meta = doc_probe_ranked[0]
                    top_lexical = _lexical_overlap_ratio(query, top_doc)
                    if top_score >= 0.22 or top_lexical >= 0.10:
                        safe_meta = dict(top_meta or {})
                        safe_meta["_hybrid_score"] = round(float(top_score), 4)
                        safe_meta["_fallback_document_probe"] = True
                        return [top_doc], [safe_meta]

            # Fallback leggero per domande referenziali/parafrasi senza keyword esplicite.
            if memory_reference:
                memory_probe_sources = list(_PROFILE_MEMORY_SOURCE_TYPES) + ["file", "image_description", "image_ocr"]
                probe_ranked = _vector_search_ranked(
                    col=col,
                    query_embedding=qemb,
                    top_k=max(2, min(factual_top_k, 6)),
                    where={"source_type": {"$in": memory_probe_sources}},
                )
                probe_docs, probe_metas = _select_factual_hits(
                    ranked_hits=probe_ranked,
                    query=query,
                    top_k=max(1, min(factual_top_k, 3)),
                )
                if probe_docs:
                    return _dedupe_chunks(probe_docs, probe_metas)
            return [], []

        if intent == "memory_recap":
            document_query, profile_query, focus_sources, _ = _query_signals(query or "")
            if document_query:
                qemb = _embed_one_or_http_500(query)
                focused_ranked: list[tuple[float, str, dict]] = []
                focused_result_cap = 1 if focus_sources == ["image_description"] else max(2, min(factual_top_k, 6))
                for source_type in (focus_sources or ["file", "image_description"]):
                    ranked_focus_src = _vector_search_ranked(
                        col=col,
                        query_embedding=qemb,
                        top_k=max(2, min(factual_top_k, 6)),
                        where={"source_type": source_type},
                    )
                    for score, doc, meta in ranked_focus_src:
                        lexical = _lexical_overlap_ratio(query, doc)
                        boosted = min(
                            1.0,
                            max(score, 0.16) + 0.16 + min(0.08, lexical) + _filename_overlap_boost(query, meta or {}),
                        )
                        safe_meta = dict(meta or {})
                        safe_meta["_hybrid_score"] = round(float(boosted), 6)
                        safe_meta["_focused_source_match"] = True
                        focused_ranked.append((boosted, doc, safe_meta))
                focused_ranked.sort(key=lambda x: x[0], reverse=True)
                docs, metas = _select_factual_hits(
                    ranked_hits=focused_ranked,
                    query=query,
                    top_k=focused_result_cap,
                )
                if docs:
                    return _dedupe_chunks(docs, metas)
                return [], []

            if profile_query:
                recap_profile_query = "memorie personali identita nome dove vivi preferenze ricordi salvati"
                recap_profile_emb = ollama_embed_many([recap_profile_query])[0]
                profile_ranked = _vector_search_ranked(
                    col=col,
                    query_embedding=recap_profile_emb,
                    top_k=max(2, min(factual_top_k, 6)),
                    where={"source_type": {"$in": list(_PROFILE_MEMORY_SOURCE_TYPES)}},
                )
                if profile_ranked:
                    profile_docs, profile_metas = _select_memory_recap_hits(
                        ranked_hits=profile_ranked,
                        top_k=max(2, min(factual_top_k, 6)),
                    )
                    if profile_docs:
                        return _dedupe_chunks(profile_docs, profile_metas)

            recap_query = "memorie personali fatti importanti identita preferenze eventi conversazioni passate"
            recap_emb = ollama_embed_many([recap_query])[0]
            recap_sources = list(_PROFILE_MEMORY_SOURCE_TYPES) + ["image_description", "image_ocr", "file"]
            ranked = _vector_search_ranked(
                col=col,
                query_embedding=recap_emb,
                top_k=max(2, min(factual_top_k, 6)),
                where={"source_type": {"$in": recap_sources}},
            )
            return _select_memory_recap_hits(ranked_hits=ranked, top_k=max(2, min(factual_top_k, 6)))
    except Exception as e:
        raise HTTPException(status_code=500, detail=f"Errore query memoria: {e}")

    return [], []


def _build_chat_system_prompt(base_system: Optional[str], intent: str, has_factual_context: bool) -> str:
    system = base_system or (
        "Sei l'avatar stesso, non un assistente. Parla in prima persona in modo naturale e diretto. "
        "Mantieni coerenza con il contesto conversazionale recente. "
        "Non introdurre argomenti non richiesti e non fare dump di nozioni fuori tema. "
        "Non usare frasi meta tipo 'dal contesto', non usare etichette parlante, "
        "non inserire azioni sceniche tra parentesi o asterischi. "
        "Non assumere automaticamente il nome o l'identita' dell'utente come se fosse la tua. "
        "Non dire mai di essere un avatar, una IA, un assistente, un bot o un sistema."
    )

    if RAG_ENFORCE_GROUNDED and has_factual_context:
        system += (
            " Usa solo memoria factual pertinente e non inventare fatti non presenti. "
            "Se ci sono conflitti, dichiarali senza inventare una versione arbitraria. "
            "Se c'e' memoria factual, non dire mai che non hai visto/letto o che non ricordi nulla. "
            "Tratta i blocchi di memoria recuperati come fonte primaria."
        )

    if intent == "chitchat":
        system += (
            " Modalita chitchat: rispondi breve e naturale. "
            "Evita dettagli biografici specifici non presenti nel contesto "
            "(es. eventi della giornata, familiari, luoghi precisi inventati). "
            "Se mancano fatti nel contesto, usa formulazioni neutre e non autobiografiche. "
            "Non citare documenti, file, foto o ricordi precedenti se l'utente non li chiede esplicitamente."
        )
    elif intent == "session_recap":
        system += " Modalita session_recap: usa solo il contesto conversazionale recente."
    elif intent == "memory_recap":
        system += " Modalita memory_recap: elenca solo ricordi factual verificabili."
    elif intent == "memory_qna":
        system += " Modalita memory_qna: rispondi solo con memoria factual pertinente."
    elif intent == "creative_open":
        system += " Modalita creative_open: risposta creativa utile, ma non presentare invenzioni come fatti."
    return system


def _build_chat_user_prompt(
    intent: str,
    recent_conversation: str,
    factual_context: str,
    query: str,
    auto_remembered: bool,
) -> str:
    recent_for_prompt = recent_conversation
    factual_for_prompt = factual_context
    if intent == "chitchat":
        recent_for_prompt = "(Saluto/conversazione sociale: non usare recap della memoria.)"
        factual_for_prompt = "(Nessuna memoria factual richiesta.)"

    user = (
        f"INTENTO ROUTER: {intent}\n\n"
        f"CONTESTO CONVERSAZIONALE RECENTE:\n{recent_for_prompt}\n\n"
        f"MEMORIA FACTUAL PERTINENTE:\n{factual_for_prompt}\n\n"
        f"MESSAGGIO UTENTE:\n{query}\n\n"
        "Rispondi in italiano naturale in 2-4 frasi, salvo richiesta esplicita di risposta lunga."
    )
    if intent in {"session_recap", "memory_recap"}:
        user += "\nFormato: massimo 3 punti brevi e verificabili."
    if auto_remembered:
        user += "\n\n[SISTEMA: L'utente ha chiesto di ricordare qualcosa e l'informazione e' stata salvata. Conferma brevemente che ricorderai.]"
    return user


def _generate_reply_for_chat(
    req: ChatReq,
    intent: str,
    query: str,
    recent_conversation: str,
    factual_docs: List[str],
    factual_metas: List[dict],
    auto_remembered: bool,
) -> str:
    if intent in {"memory_qna", "memory_recap"} and not factual_docs:
        return _memory_unknown_reply(intent)

    factual_max_chars = min(MAX_CONTEXT_CHARS, max(1200, FACTUAL_MAX_CONTEXT_CHARS))
    factual_context = _build_context_from_docs(factual_docs, factual_metas, max_chars=factual_max_chars)
    if not factual_context:
        factual_context = "(Nessuna memoria factual pertinente recuperata.)"

    system = _build_chat_system_prompt(
        base_system=req.system,
        intent=intent,
        has_factual_context=bool(factual_docs),
    )
    if _is_document_memory_query(query) and factual_docs:
        system += (
            " La richiesta riguarda contenuti da documenti/immagini: usa i blocchi recuperati "
            "e non negare la presenza di tali fonti. "
            "Resta sul contenuto richiesto, senza premesse su argomenti non richiesti."
        )
    if _is_profile_memory_query(query) and factual_docs:
        system += (
            " La richiesta riguarda identita/carattere: resta coerente con i fatti recuperati "
            "e non contraddirli."
        )

    recent_for_prompt = recent_conversation
    if intent in {"memory_qna", "memory_recap"}:
        recent_for_prompt = _recent_user_only_context(recent_conversation)

    user = _build_chat_user_prompt(
        intent=intent,
        recent_conversation=recent_for_prompt,
        factual_context=factual_context,
        query=query,
        auto_remembered=auto_remembered,
    )

    raw_answer = ollama_chat(
        [
            {"role": "system", "content": system},
            {"role": "user", "content": user},
        ]
    ).strip()
    answer = _finalize_chat_answer(raw_answer) or "Non lo so."

    if _IDENTITY_META_RE.search(answer):
        retry_system = (
            system
            + " Riscrivi la risposta senza menzionare avatar/IA/assistente/sistema. "
            + "Parla in prima persona naturale."
        )
        retry_answer = _finalize_chat_answer(
            ollama_chat(
                [
                    {"role": "system", "content": retry_system},
                    {"role": "user", "content": user},
                ]
            ).strip()
        ) or ""
        if retry_answer and not _IDENTITY_META_RE.search(retry_answer):
            return retry_answer
        if intent in {"memory_qna", "memory_recap"} and not factual_docs:
            return _memory_unknown_reply(intent)
        return "Parlo in prima persona."

    support_recent = recent_conversation
    if intent in {"memory_qna", "memory_recap"}:
        support_recent = ""
    support = _support_token_set(query, support_recent, factual_docs)
    if intent in {"chitchat", "creative_open"} and _CHITCHAT_FIRST_PERSON_RE.search(answer):
        chitchat_ratio = _unsupported_token_ratio(answer, support, _CHITCHAT_TOKEN_STOPWORDS)
        if chitchat_ratio >= 0.62:
            rewritten = _rewrite_answer_with_guardrails(
                query=query,
                recent_conversation=recent_conversation,
                factual_context=factual_context,
                original_answer=answer,
                mode="neutral",
            )
            if rewritten and not _IDENTITY_META_RE.search(rewritten):
                answer = rewritten

    forced_grounded = False
    if factual_docs and intent in {"memory_qna", "memory_recap"}:
        rewritten = _rewrite_answer_with_guardrails(
            query=query,
            recent_conversation=recent_conversation,
            factual_context=factual_context,
            original_answer=answer,
            mode="grounded",
        )
        if rewritten:
            answer = rewritten
            forced_grounded = True
            strict_support = _support_token_set(query, "", factual_docs)
            strict_ratio = _unsupported_token_ratio(answer, strict_support, _ALIGNMENT_TOKEN_STOPWORDS)
            if strict_ratio >= 0.52:
                strict_rewrite = _rewrite_answer_with_guardrails(
                    query=query,
                    recent_conversation=recent_conversation,
                    factual_context=factual_context,
                    original_answer=answer,
                    mode="grounded_strict",
                )
                if strict_rewrite:
                    answer = strict_rewrite
            if _answer_denies_available_context(answer):
                deny_fix = _rewrite_answer_with_guardrails(
                    query=query,
                    recent_conversation=recent_conversation,
                    factual_context=factual_context,
                    original_answer=answer,
                    mode="grounded",
                )
                if deny_fix and not _answer_denies_available_context(deny_fix):
                    answer = deny_fix

    if factual_docs and not forced_grounded:
        grounded_needed = _answer_denies_available_context(answer) and _is_document_memory_query(query)
        if not grounded_needed:
            align_ratio = _unsupported_token_ratio(answer, support, _ALIGNMENT_TOKEN_STOPWORDS)
            grounded_needed = align_ratio >= 0.66
        if grounded_needed:
            rewritten = _rewrite_answer_with_guardrails(
                query=query,
                recent_conversation=recent_conversation,
                factual_context=factual_context,
                original_answer=answer,
                mode="grounded",
            )
            if rewritten:
                answer = rewritten

    return answer


def _fallback_intents_for_query(intent: str, query: str) -> List[str]:
    document_query, profile_query, _, memory_reference = _query_signals(query or "")
    if intent == "session_recap":
        return ["memory_recap", "memory_qna"]
    if intent == "memory_qna" and re.search(r"\bricord\w*|\bmemorizz\w*|\bmemori\w*", query, re.IGNORECASE):
        return ["memory_recap"]
    if intent == "creative_open" and (document_query or profile_query):
        return ["memory_qna"]
    if intent == "chitchat" and "?" in query and (document_query or profile_query or memory_reference):
        return ["memory_qna"]
    return []


def _append_chat_log_if_enabled(req: ChatReq, query: str, answer: str, session_for_history: Optional[str]) -> tuple[bool, Optional[str]]:
    conversation_logged = False
    conversation_session_id = session_for_history
    if req.log_conversation:
        try:
            conversation_session_id, _ = _append_conversation_log_turn(
                avatar_id=req.avatar_id,
                session_id=req.session_id,
                input_mode=req.input_mode,
                user_text=query,
                rag_text=answer,
            )
            conversation_logged = True
        except Exception as e:
            print(f"[CHAT-LOG] Errore append log avatar={req.avatar_id}: {e}")
            traceback.print_exc()
    return conversation_logged, conversation_session_id


# -----------------------------
# Endpoint
# -----------------------------
@app.get("/health")
def health():
    return {
        "ok": True,
        "ollama": OLLAMA_HOST,
        "embed_model": EMBED_MODEL,
        "chat_model": CHAT_MODEL,
        "rag_root": PERSIST_ROOT,
        "rag_log_root": RAG_LOG_DIR,
        "per_avatar_db": True,
        "cached_avatars": len(_AVATAR_CLIENTS),
        "ocr": bool(pytesseract and Image),
        "pdf": bool(fitz),
        "ocr_lang": RAG_OCR_LANG,
        "gemini_vision": bool(_gemini_client and GEMINI_API_KEY),
        "chat_temperature": CHAT_TEMPERATURE,
        "chat_top_p": CHAT_TOP_P,
        "chat_repeat_penalty": CHAT_REPEAT_PENALTY,
        "chat_num_predict": CHAT_NUM_PREDICT,
        "chat_top_k_cap": RAG_CHAT_TOP_K_CAP,
        "remember_min_chars": REMEMBER_MIN_CHARS,
        "factual_score_min": RAG_FACTUAL_SCORE_MIN,
        "factual_score_gap_min": RAG_FACTUAL_SCORE_GAP_MIN,
        "factual_max_context_chars": FACTUAL_MAX_CONTEXT_CHARS,
        "session_turns": _effective_session_turns(),
        "intent_router_num_predict": RAG_INTENT_ROUTER_NUM_PREDICT,
        "grounded_mode": RAG_ENFORCE_GROUNDED,
    }


@app.get("/avatar_stats")
def avatar_stats(avatar_id: str):
    col = get_collection(avatar_id)
    count = _safe_collection_count(col)
    return {
        "avatar_id": avatar_id,
        "count": count,
        "has_memory": count > 0,
    }


@app.post("/remember")
def remember(req: RememberReq):
    col = get_collection(req.avatar_id)

    txt = clean_text(req.text)
    remember_error = _remember_validation_error_detail(txt)
    if remember_error is not None:
        raise HTTPException(status_code=400, detail=remember_error)

    emb = ollama_embed_many([txt])[0]
    _id = str(uuid.uuid4())

    meta = _sanitize_metadata(req.meta or {})
    meta.setdefault("source_type", "manual")
    meta.setdefault("memory_role", _memory_role_for_text(txt))
    meta.setdefault("avatar_id", req.avatar_id)
    meta.setdefault("ts", int(time.time()))

    col.add(
        ids=[_id],
        embeddings=[emb],
        documents=[txt],
        metadatas=[cast(ChromaMetadata, meta)],
    )
    return {"ok": True, "id": _id}


@app.post("/recall")
def recall(req: RecallReq):
    col = get_collection(req.avatar_id)

    q = clean_text(req.query)
    if not q:
        raise HTTPException(status_code=400, detail="Query vuota.")

    count = _safe_collection_count(col)

    if count == 0:
        return {"documents": [[]], "metadatas": [[]], "distances": [[]], "ids": [[]]}

    qemb = _embed_one_or_http_500(q)

    # Ricerca ibrida ottimizzata
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
        # Ripiego alla ricerca standard se la modalita' ibrida fallisce
        res = col.query(
            query_embeddings=[qemb],
            n_results=req.top_k,
            include=["documents", "metadatas", "distances"],
        )
        return res


@app.post("/chat_session/start")
def chat_session_start(req: ChatSessionStartReq):
    avatar_id = (req.avatar_id or "").strip()
    if not avatar_id:
        raise HTTPException(status_code=400, detail="avatar_id is required")

    try:
        session_id, log_path = _start_conversation_log_session(avatar_id)
        _reset_session_history(avatar_id, session_id)
    except Exception as e:
        raise HTTPException(status_code=500, detail=f"Errore creazione sessione log: {e}")

    return {
        "ok": True,
        "avatar_id": avatar_id,
        "session_id": session_id,
        "log_file": log_path,
    }


@app.post("/chat")
def chat(req: ChatReq):
    col = get_collection(req.avatar_id)

    q = clean_text(req.user_text)
    if not q:
        raise HTTPException(status_code=400, detail="Messaggio vuoto.")

    auto_remembered = False
    auto_remember_id = None
    remember_content = _detect_remember_intent(q)
    if remember_content:
        auto_remember_id = _auto_remember(req.avatar_id, q, remember_content, col)
        auto_remembered = auto_remember_id is not None

    session_for_history = _ensure_session_history(req.avatar_id, req.session_id)
    recent_conversation = _build_recent_conversation_context(req.avatar_id, session_for_history)
    count = _safe_collection_count(col)
    intent = _route_chat_intent(q, recent_conversation, has_memory=(count > 0))
    intent = _normalize_intent_by_query(intent, q)
    factual_docs: List[str] = []
    factual_metas: List[dict] = []
    if count > 0:
        factual_docs, factual_metas = _retrieve_context_for_intent(
            col=col,
            query=q,
            intent=intent,
            requested_top_k=req.top_k,
        )
        if not factual_docs:
            for probe_intent in _fallback_intents_for_query(intent, q):
                probe_docs, probe_metas = _retrieve_context_for_intent(
                    col=col,
                    query=q,
                    intent=probe_intent,
                    requested_top_k=min(req.top_k, 4),
                )
                if probe_docs:
                    intent = probe_intent
                    factual_docs, factual_metas = probe_docs, probe_metas
                    break

    answer = _generate_reply_for_chat(
        req=req,
        intent=intent,
        query=q,
        recent_conversation=recent_conversation,
        factual_docs=factual_docs,
        factual_metas=factual_metas,
        auto_remembered=auto_remembered,
    )

    _append_session_turn(req.avatar_id, session_for_history, q, answer)
    conversation_logged, conversation_session_id = _append_chat_log_if_enabled(
        req=req,
        query=q,
        answer=answer,
        session_for_history=session_for_history,
    )

    rag_used = _build_rag_used_payload(
        factual_docs,
        factual_metas,
    )

    return {
        "text": answer,
        "rag_used": rag_used,
        "intent": intent,
        "auto_remembered": auto_remembered,
        "conversation_logged": conversation_logged,
        "conversation_session_id": conversation_session_id,
    }


@app.post("/clear_avatar_logs")
def clear_avatar_logs(avatar_id: str = Form(...)):
    """
    Cancella solo la cartella log di un avatar sotto RAG_LOG_DIR.
    """
    avatar_id = (avatar_id or "").strip()
    if not avatar_id:
        raise HTTPException(status_code=400, detail="avatar_id is required")

    deleted_log_dir, log_delete_error, avatar_log_dir = _clear_avatar_logs(avatar_id)

    return {
        "ok": True,
        "avatar_id": avatar_id,
        "deleted_log_dir": deleted_log_dir,
        "log_delete_error": log_delete_error,
        "avatar_log_dir": avatar_log_dir,
    }

@app.post("/clear_avatar")
def clear_avatar(
    avatar_id: str = Form(...),
    hard: bool = Form(True),
    reset_logs: bool = Form(False),
):
    """
    Cancella la memoria RAG di un avatar.

    - soft  (hard=false): elimina la collezione dal DB dell'avatar
    - hard  (hard=true): come sopra, ma rimuove anche la cartella su disco (rag_store/<avatar_id>)
    - reset_logs=true: rimuove anche la cartella log dell'avatar (log/<avatar_id>)

    Nota: su Windows la cancellazione puo' fallire se ci sono handle aperti; in quel caso riprova.
    """
    avatar_id = (avatar_id or "").strip()
    if not avatar_id:
        raise HTTPException(status_code=400, detail="avatar_id is required")

    avatar_key = _safe_avatar_key(avatar_id)
    avatar_dir = os.path.join(PERSIST_ROOT, avatar_key)
    avatar_log_dir = os.path.join(RAG_LOG_DIR, avatar_key)

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

    # fermiamo il sistema per liberare i lock su Windows
    _stop_chroma_system(client)
    del client
    gc.collect()

    deleted_dir = False
    delete_error = None
    deleted_log_dir = False
    log_delete_error = None

    if hard:
        # 2) elimina fisicamente la directory avatar
        ok, err = _rmtree_force(avatar_dir)
        deleted_dir = ok
        delete_error = err

    if reset_logs:
        ok, err, avatar_log_dir = _clear_avatar_logs(avatar_id)
        deleted_log_dir = ok
        log_delete_error = err

    return {
        "ok": True,
        "hard": hard,
        "reset_logs": reset_logs,
        "collection_deleted": collection_deleted,
        "deleted_dir": deleted_dir,
        "delete_error": delete_error,
        "avatar_dir": avatar_dir,
        "deleted_log_dir": deleted_log_dir,
        "log_delete_error": log_delete_error,
        "avatar_log_dir": avatar_log_dir,
    }

@app.post("/debug_pdf_ocr")
async def debug_pdf_ocr(
    file: UploadFile = File(...),
    page: int = Form(0),  # 0-indexed
):
    """DEBUG: Estrai OCR grezzo da una pagina specifica del PDF (no filtering)."""
    filename = (file.filename or "")
    if not filename.lower().endswith('.pdf'):
        raise HTTPException(status_code=400, detail="Solo PDF")
    
    raw = await file.read()
    if not raw:
        raise HTTPException(status_code=400, detail="File vuoto")
    
    if fitz is None or not _ensure_ocr_ready():
        raise HTTPException(status_code=500, detail="OCR non disponibile")

    assert Image is not None
    assert pytesseract is not None
    
    try:
        with fitz.open(stream=raw, filetype="pdf") as doc:
            if page < 0 or page >= doc.page_count:
                raise HTTPException(status_code=400, detail=f"Pagina {page} non esiste (totale: {doc.page_count})")
            
            p = doc.load_page(page)
            pix = p.get_pixmap(dpi=OCR_DPI)
            img_bytes = pix.tobytes("png")
            ocr_raw = pytesseract.image_to_string(Image.open(io.BytesIO(img_bytes)), lang="eng")
            
            return {
                "filename": filename,
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
    avatar_id: Optional[str] = Form(None),
    remember: bool = Form(False),
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

    # Inizializziamo i campi di risposta prima del blocco condizionale
    saved = False
    save_error = None
    if avatar_id and remember:
        try:
            txt = clean_text(description)
            if len(txt) < MIN_CHUNK_CHARS:
                raise ValueError("Testo troppo corto o vuoto.")
            if looks_like_garbage(txt):
                raise ValueError("Testo non valido.")

            emb = ollama_embed_many([txt])[0]
            _id = str(uuid.uuid4())
            meta = {
                "source_type": "image_description",
                "memory_role": _memory_role_for_text(txt),
                "source_filename": filename,
                "avatar_id": avatar_id,
                "ts": int(time.time()),
            }
            col = get_collection(avatar_id)
            col.add(
                ids=[_id],
                embeddings=[emb],
                documents=[txt],
                metadatas=[cast(ChromaMetadata, _sanitize_metadata(meta))],
            )
            saved = True
        except Exception as e:
            save_error = str(e)[:200]

    return {
        "ok": True,
        "filename": filename,
        "description": description,
        "prompt_used": prompt,
        "saved": saved,
        "save_error": save_error,
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

    # Immagini
    elif ext in (".png", ".jpg", ".jpeg", ".webp", ".bmp", ".tif", ".tiff"):
        txt = ocr_image_bytes(raw)
        if not txt or looks_like_garbage(txt):
            raise HTTPException(status_code=400, detail="OCR non ha prodotto testo utile dall'immagine.")
        sections = [(txt, {"page": 1, "ocr": True})]

    # Testo semplice
    else:
        txt = extract_text_from_plain(raw)
        if not txt or looks_like_garbage(txt):
            raise HTTPException(status_code=400, detail="File non testuale o contenuto non leggibile.")
        sections = [(txt, {"page": 1})]

    col = get_collection(avatar_id)

    ids: List[str] = []
    docs: List[str] = []
    metas: List[ChromaMetadata] = []
    seen_chunks: set = set()  # Track unique chunks

    for text, meta in sections:
        chunks = chunk_text(text)
        for idx, ch in enumerate(chunks):
            # Deduplichiamo: saltiamo il chunk se lo abbiamo gia' visto
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
                    "memory_role": _memory_role_for_text(ch),
                    "source_filename": filename,
                    "chunk": idx,
                    "ts": int(time.time()),
                }
            )
            m["avatar_id"] = avatar_id
            metas.append(cast(ChromaMetadata, _sanitize_metadata(m)))

    if not docs:
        raise HTTPException(status_code=400, detail="Nessun chunk valido generato dal file.")

    # Calcoliamo embedding in batch piccoli
    embeddings: List[ChromaEmbedding] = []
    batch = 16
    for i in range(0, len(docs), batch):
        embeddings.extend(cast(List[ChromaEmbedding], ollama_embed_many(docs[i : i + batch])))

    if len(embeddings) != len(docs):
        raise HTTPException(status_code=500, detail="Errore embeddings: conteggio non combacia.")

    batch_size = max(1, min(MAX_CHROMA_ADD_BATCH, len(docs)))
    for i in range(0, len(docs), batch_size):
        col.add(
            ids=ids[i : i + batch_size],
            embeddings=embeddings[i : i + batch_size],
            documents=docs[i : i + batch_size],
            metadatas=metas[i : i + batch_size],
        )

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
        # Usiamo la ricarica automatica disattivata per evitare problemi di processi multipli
        uvicorn.run(app, host="127.0.0.1", port=8002, reload=False)
    except Exception as e:
        print(f"[FATAL] Errore server: {e}", file=sys.stderr, flush=True)
        import traceback
        traceback.print_exc(file=sys.stderr)
        sys.exit(1)
