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
- PDF: usa sempre OCR (400 DPI di default) - testo embedded spesso corrotto
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
from dataclasses import dataclass
from datetime import datetime
import gc
import stat
import shutil
import threading
import traceback
from typing import Any, Optional, List, Tuple, Sequence, TYPE_CHECKING, cast

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

# PyMuPDF4LLM: pymupdf-layout ha un bug ONNX noto (int32 vs int64) che causa crash
# su PDF reali -> NON importare pymupdf.layout; pymupdf4llm funziona in legacy mode.
_pymupdf4llm_available = False
try:
    import pymupdf  # serve per aprire Document e per fallback OCR
except Exception:
    pymupdf = None  # type: ignore
try:
    import pymupdf4llm
    _pymupdf4llm_available = True
except Exception:
    pymupdf4llm = None  # type: ignore

if TYPE_CHECKING:
    from chromadb.api import ClientAPI as ChromaClientAPI
    from chromadb.api.types import Embedding as ChromaEmbedding
    from chromadb.api.types import Metadata as ChromaMetadata
else:
    ChromaClientAPI = Any  # type: ignore[misc,assignment]
    ChromaEmbedding = Any  # type: ignore[misc,assignment]
    ChromaMetadata = Any  # type: ignore[misc,assignment]

def _env_bool(name: str, default: bool) -> bool:
    raw = os.getenv(name)
    if raw is None:
        return default
    return raw.strip().lower() in {"1", "true", "yes", "y", "on"}

def _default_empirical_log_dir(default_log_dir: str, empirical_root: str) -> str:
    env_value = os.getenv("EMPIRICAL_RAG_LOG_DIR", "").strip().strip('"')
    if env_value:
        return env_value

    normalized_default = os.path.abspath(os.path.normpath(default_log_dir))
    if os.name != "nt" and normalized_default.startswith("/home/"):
        return os.path.join(normalized_default, "empirical_test")

    return os.path.join(empirical_root, "log")

OLLAMA_HOST = os.getenv("OLLAMA_HOST", "http://127.0.0.1:11434").rstrip("/")
EMBED_MODEL = os.getenv("EMBED_MODEL", "nomic-embed-text")
CHAT_MODEL = os.getenv("CHAT_MODEL", "llama3:8b-instruct-q4_K_M")
CHAT_TEMPERATURE = float(os.getenv("CHAT_TEMPERATURE", "0.45"))
CHAT_TOP_P = float(os.getenv("CHAT_TOP_P", "0.9"))
CHAT_REPEAT_PENALTY = float(os.getenv("CHAT_REPEAT_PENALTY", "1.08"))
CHAT_NUM_PREDICT = int(os.getenv("CHAT_NUM_PREDICT", "420"))

PERSIST_ROOT = os.getenv("RAG_DIR", os.path.join(os.path.dirname(__file__), "rag_store"))
PERSIST_ROOT = PERSIST_ROOT.strip().strip('"')
PERSIST_ROOT = os.path.abspath(os.path.normpath(PERSIST_ROOT))
os.makedirs(PERSIST_ROOT, exist_ok=True)

RAG_LOG_DIR = os.getenv("RAG_LOG_DIR", os.path.join(os.path.dirname(__file__), "log"))
RAG_LOG_DIR = RAG_LOG_DIR.strip().strip('"')
RAG_LOG_DIR = os.path.abspath(os.path.normpath(RAG_LOG_DIR))
os.makedirs(RAG_LOG_DIR, exist_ok=True)

EMPIRICAL_TEST_ROOT = os.path.abspath(os.path.normpath(os.path.join(os.path.dirname(__file__), "empirical_test")))
EMPIRICAL_PERSIST_ROOT = os.path.join(EMPIRICAL_TEST_ROOT, "rag_store")
EMPIRICAL_RAG_LOG_DIR = _default_empirical_log_dir(RAG_LOG_DIR, EMPIRICAL_TEST_ROOT)
EMPIRICAL_RAG_LOG_DIR = os.path.abspath(os.path.normpath(EMPIRICAL_RAG_LOG_DIR))
os.makedirs(EMPIRICAL_PERSIST_ROOT, exist_ok=True)
os.makedirs(EMPIRICAL_RAG_LOG_DIR, exist_ok=True)

CHUNK_CHARS = int(os.getenv("RAG_CHUNK_CHARS", "3500"))
CHUNK_OVERLAP = int(os.getenv("RAG_CHUNK_OVERLAP", "500"))
MIN_CHUNK_CHARS = int(os.getenv("RAG_MIN_CHUNK_CHARS", "20"))
REMEMBER_MIN_CHARS = int(os.getenv("RAG_REMEMBER_MIN_CHARS", "10"))
MAX_CHROMA_ADD_BATCH = int(os.getenv("RAG_CHROMA_ADD_BATCH", "5000"))
MAX_CONTEXT_CHARS = int(os.getenv("RAG_MAX_CONTEXT_CHARS", "6000"))
FACTUAL_MAX_CONTEXT_CHARS = int(os.getenv("RAG_FACTUAL_MAX_CONTEXT_CHARS", "3600"))
RAG_FACTUAL_SCORE_MIN = float(os.getenv("RAG_FACTUAL_SCORE_MIN", "0.52"))
RAG_FACTUAL_SCORE_GAP_MIN = float(os.getenv("RAG_FACTUAL_SCORE_GAP_MIN", "0.14"))
RAG_SESSION_TURNS = int(os.getenv("RAG_SESSION_TURNS", "8"))
RAG_CHAT_TOP_K_CAP = int(os.getenv("RAG_CHAT_TOP_K_CAP", "8"))
RAG_INTENT_ROUTER_NUM_PREDICT = int(os.getenv("RAG_INTENT_ROUTER_NUM_PREDICT", "32"))
RAG_INTENT_CONFIDENCE_MIN = float(os.getenv("RAG_INTENT_CONFIDENCE_MIN", "0.58"))
RAG_QUERY_REWRITE_MAX_TOKENS = int(os.getenv("RAG_QUERY_REWRITE_MAX_TOKENS", "7"))
RAG_ENABLE_QUERY_REWRITE = _env_bool("RAG_ENABLE_QUERY_REWRITE", True)
RAG_GROUNDING_SCORE_MIN = float(os.getenv("RAG_GROUNDING_SCORE_MIN", "0.48"))
RAG_ENFORCE_GROUNDED = _env_bool("RAG_ENFORCE_GROUNDED", True)

# OCR / Tesseract
RAG_OCR_LANG = os.getenv("RAG_OCR_LANG", "ita+eng").strip()          # es: "ita" oppure "ita+eng"
TESSERACT_CMD = os.getenv("TESSERACT_CMD", "").strip().strip('"')
DEFAULT_TESSERACT_CMD = r"C:\Program Files\Tesseract-OCR\tesseract.exe"
OCR_DPI = int(os.getenv("RAG_OCR_DPI", "400"))  # DPI OCR predefinito per PDF/immagini

# TESSDATA_PREFIX: pymupdf4llm (layout mode) richiede questa env per trovare i dati Tesseract.
# Se non impostata, la settiamo al percorso standard (Windows).
if not os.environ.get("TESSDATA_PREFIX"):
    _default_tessdata = os.path.join(os.path.dirname(DEFAULT_TESSERACT_CMD), "tessdata")
    if os.path.isdir(_default_tessdata):
        os.environ["TESSDATA_PREFIX"] = _default_tessdata

# Gemini Vision (opzionale)
GEMINI_API_KEY = os.getenv("GEMINI_API_KEY", "").strip()
# Se non trovata in env, prova a leggerla da file
if not GEMINI_API_KEY:
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

# rimuove caratteri di controllo (ma mantiene tab/newline)
_CTRL_RE = re.compile(r"[\x00-\x08\x0B\x0C\x0E-\x1F\x7F]")
_WS_RE = re.compile(r"[ \t]+")

def clean_text(s: str, ocr: bool = False) -> str:
    """Normalizza il testo; il flag ocr resta per compatibilita' con i call-site esistenti."""
    if not s:
        return ""
    s = s.replace("\r\n", "\n").replace("\r", "\n")
    s = _CTRL_RE.sub("", s)
    s = _WS_RE.sub(" ", s)
    s = re.sub(r"\n{3,}", "\n\n", s)
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
# Prefissi meta-risposta prodotti dal modello (es. "RISPOSTA CORRETTA:", "La risposta corretta e':")
_META_ANSWER_PREFIX_RE = re.compile(
    r"^\s*(?:(?:la\s+)?risposta(?:\s+corretta)?\s*(?:e'|è|:)|ecco\s+la\s+risposta\s*:)\s*",
    re.IGNORECASE,
)
_WORD_TOKEN_RE = re.compile(r"\b[\w']+\b", re.UNICODE)
_IDENTITY_META_RE = re.compile(
    r"\b("
    r"sono un avatar|sono un assistente|sono un'ia|sono una ia|sono un ai|"
    r"modello linguistico|language model|sono un bot|sono un sistema|"
    r"come (?:avatar|assistente|ia|ai|bot)|in quanto (?:avatar|assistente|ia|ai|bot)"
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
    "chiamo", "chiami", "chiama", "nome", "vivo", "abito", "citta", "città",
    "eta", "età", "insicuro", "insicura", "carattere", "persona", "tipo",
    "personalita", "personalità", "dove", "allergia", "allergie", "animale", "animali",
    "gatto", "cane", "patente", "sposto", "spostarti", "spostarsi", "treno", "scooter",
    "bevi", "bevanda", "mattino", "password", "dimentico", "dimenticare", "colloquio",
    "appuntamento", "lavoro", "lavori", "studi", "utente", "interlocutore", "profilo",
}
_IMAGE_QUERY_HINTS = {
    "immagine", "immagini", "foto", "display", "schermo", "profilo",
    "aspetto", "lineamenti", "viso", "faccia", "specchio", "vestito", "vestita",
    "mano", "sfondo", "stanza", "visivo", "visivi", "grafico", "diagramma",
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
_STRONG_VISUAL_ANCHORS = {"foto", "immagine", "immagini", "schermata", "diagramma", "grafico", "specchio", "visivo", "visivi"}
_STRONG_FILE_ANCHORS = {"fattura", "fatture", "documento", "documenti", "pdf", "file", "contratto", "certificato", "modulo"}
_MULTI_SOURCE_CONJUNCTION_RE = re.compile(
    r"(?:cosa (?:sai|ricordi)\b.+?(?:,|\be\b).+?(?:,|\be\b))"
    r"|(?:riepilog\w*|riassum\w*).+?(?:includ\w*|compres\w*|che riguard\w*)",
    re.IGNORECASE,
)
_MEMORY_SUBJECT_AVATAR = "avatar_self"
_MEMORY_SUBJECT_USER = "user"
_MEMORY_SUBJECT_AMBIGUOUS = "ambiguous"
_MEMORY_SUBJECT_EXTERNAL = "external"
_EXTERNAL_MEMORY_SOURCE_TYPES = ("file", "image_description", "image_ocr")
_REFERENCE_QUERY_STOPWORDS = (
    _ALIGNMENT_TOKEN_STOPWORDS
    | _DOC_QUERY_HINTS
    | _PROFILE_QUERY_HINTS
    | _IMAGE_QUERY_HINTS
    | _MEMORY_REFERENCE_HINTS
    | {
        "dimmi", "raccontami", "spiegami", "parlami", "qualche",
        "sulla", "sullo", "sulle", "sugli", "della", "delle",
        "degli", "dello", "sul", "sui", "dei", "del", "di",
        "che", "chi", "cosa", "come",
    }
)
_AVATAR_PROFILE_QUERY_RE = re.compile(
    r"\b("
    r"chi sei|come ti chiami|qual(?:e|['’]e) il tuo nome|quanti anni hai|che et(?:a|à) hai|"
    r"dove vivi|dove abiti|che lavoro fai|cosa fai nella vita|cosa ti piace|"
    r"quali sono i tuoi gusti|hai allerg\w*|hai animali|hai la patente|"
    r"preferisci|bevi|mangi|lavori|studi"
    r")\b"
    r"|(?:parlami|raccontami)\b.{0,20}\bdi\s+te"
    r"|(?:presentati|descriviti)\b",
    re.IGNORECASE,
)
_USER_PROFILE_QUERY_RE = re.compile(
    r"\b("
    r"cosa sai di me|ti ricordi di me|su di me|come mi chiamo|qual(?:e|['’]e) il mio nome|"
    r"quanti anni ho|che et(?:a|à) ho|dove vivo|dove abito|cosa mi piace|"
    r"quali sono i miei gusti|ho allerg\w*|ho animali|ho la patente|ti ricordi il mio|"
    r"cosa sai dell['’]utente|cosa sai dell['’]interlocutore|su l['’]utente|su l['’]interlocutore|"
    r"sull['’]utente|sull['’]interlocutore|ti ricordi dell['’]utente|ti ricordi dell['’]interlocutore|"
    r"(?:cosa|che cosa)\s+ricord\w*\s+(?:di me|su di me)|"
    r"come si chiama l['’](?:utente|interlocutore)|"
    r"nome dell['’](?:utente|interlocutore)"
    r")\b",
    re.IGNORECASE,
)
_FIRST_PERSON_MEMORY_RE = re.compile(
    r"\b("
    r"io|me|mi|mio|mia|miei|mie|mi chiamo|ho|sono|vivo|abito|preferisco|"
    r"mi piace|lavoro|studio|bevo|mangio|uso|vado|prendo"
    r")\b",
    re.IGNORECASE,
)
_SECOND_PERSON_MEMORY_RE = re.compile(
    r"\b("
    r"tu|te|ti|tuo|tua|tuoi|tue|ti chiami|hai|sei|vivi|abiti|preferisci|"
    r"ti piace|lavori|studi|bevi|mangi|usi|vai|prendi"
    r")\b",
    re.IGNORECASE,
)
_EXPLICIT_USER_MEMORY_RE = re.compile(r"\b(?:utente|user|interlocutore|proprietari\w*)\b", re.IGNORECASE)
_EXPLICIT_AVATAR_MEMORY_RE = re.compile(r"\b(?:avatar|assistente|personaggio)\b", re.IGNORECASE)

def _is_persona_style_text(text: str) -> bool:
    t = clean_text(text)
    if not t:
        return False
    return _PERSONA_STYLE_RE.search(t) is not None

def _memory_role_for_text(text: str) -> str:
    return "persona_style" if _is_persona_style_text(text) else "factual_memory"

def _normalize_memory_subject(value: Any) -> str:
    raw = clean_text(str(value or "")).strip().lower()
    if raw in {_MEMORY_SUBJECT_AVATAR, _MEMORY_SUBJECT_USER, _MEMORY_SUBJECT_AMBIGUOUS, _MEMORY_SUBJECT_EXTERNAL}:
        return raw
    return ""

@lru_cache(maxsize=4096)

def _infer_profile_query_target(query: str) -> str:
    q = clean_text(query or "").lower()
    if not q:
        return _MEMORY_SUBJECT_AMBIGUOUS

    if _USER_PROFILE_QUERY_RE.search(q):
        return _MEMORY_SUBJECT_USER
    if _AVATAR_PROFILE_QUERY_RE.search(q):
        return _MEMORY_SUBJECT_AVATAR

    has_question_form = ("?" in q) or bool(
        re.search(r"^\s*(chi|come|cosa|dove|quando|perche|a che ora|di dove|dimmi|raccontami|spiegami)\b", q)
    )
    if has_question_form:
        if re.search(r"\b(io|me|mi|mio|mia|miei|mie|ho|sono|vivo|abito|preferisco|mi piace|lavoro|studio)\b", q):
            return _MEMORY_SUBJECT_USER
        if re.search(r"\b(tu|te|ti|tuo|tua|tuoi|tue|hai|sei|vivi|abiti|preferisci|ti piace|lavori|studi)\b", q):
            return _MEMORY_SUBJECT_AVATAR
    return _MEMORY_SUBJECT_AMBIGUOUS

def _infer_memory_subject(
    text: str,
    meta: Optional[dict] = None,
    *,
    default_subject: str = _MEMORY_SUBJECT_AMBIGUOUS,
    original_utterance: str = "",
) -> str:
    normalized_meta_subject = _normalize_memory_subject((meta or {}).get("memory_subject"))
    if normalized_meta_subject:
        return normalized_meta_subject

    source_type = clean_text(str((meta or {}).get("source_type") or "")).lower()
    if source_type in {"file", "image_description", "image_ocr"}:
        return _MEMORY_SUBJECT_EXTERNAL

    text_clean = clean_text(text or "")
    original_clean = clean_text(original_utterance or str((meta or {}).get("original_utterance") or ""))
    combined = "\n".join(part for part in [original_clean, text_clean] if part).lower()
    if not combined:
        return default_subject

    if _EXPLICIT_USER_MEMORY_RE.search(combined):
        return _MEMORY_SUBJECT_USER
    if _EXPLICIT_AVATAR_MEMORY_RE.search(combined):
        return _MEMORY_SUBJECT_AVATAR

    first_person = bool(_FIRST_PERSON_MEMORY_RE.search(combined))
    second_person = bool(_SECOND_PERSON_MEMORY_RE.search(combined))

    if source_type == "auto_remember_voice":
        target = _infer_profile_query_target(original_clean or text_clean)
        if target in {_MEMORY_SUBJECT_AVATAR, _MEMORY_SUBJECT_USER}:
            return target
        if second_person and not first_person:
            return _MEMORY_SUBJECT_AVATAR
        if first_person and not second_person:
            return _MEMORY_SUBJECT_USER
        return _MEMORY_SUBJECT_AMBIGUOUS

    if second_person and not first_person:
        return _MEMORY_SUBJECT_AVATAR
    if first_person and not second_person:
        return default_subject

    return default_subject

def _effective_memory_subject(doc: str, meta: Optional[dict] = None) -> str:
    source_type = clean_text(str((meta or {}).get("source_type") or "")).lower()
    if source_type in {"manual", "manual_note"}:
        default_subject = _MEMORY_SUBJECT_AVATAR
    elif source_type == "auto_remember_voice":
        default_subject = _MEMORY_SUBJECT_AMBIGUOUS
    elif source_type in {"file", "image_description", "image_ocr"}:
        default_subject = _MEMORY_SUBJECT_EXTERNAL
    else:
        default_subject = _MEMORY_SUBJECT_AMBIGUOUS

    return _infer_memory_subject(
        doc,
        meta,
        default_subject=default_subject,
        original_utterance=str((meta or {}).get("original_utterance") or ""),
    )

def _profile_subject_matches_target(subject: str, target: str) -> bool:
    normalized_target = _normalize_memory_subject(target)
    normalized_subject = _normalize_memory_subject(subject)
    if not normalized_target or normalized_target == _MEMORY_SUBJECT_AMBIGUOUS:
        return True
    if normalized_subject == normalized_target:
        return True
    return normalized_subject == _MEMORY_SUBJECT_AMBIGUOUS

def _annotate_memory_subject(meta: Optional[dict], doc: str) -> dict:
    safe_meta = dict(meta or {})
    safe_meta.setdefault("memory_subject", _effective_memory_subject(doc, safe_meta))
    return safe_meta

def _src_type(meta: Optional[dict]) -> str:
    """Normalizza source_type da metadata."""
    return str((meta or {}).get("source_type") or "").strip().lower()

def _sanitize_chat_answer(text: str) -> str:
    out = (text or "").strip()
    if not out:
        return ""

    for _ in range(2):
        cleaned = _SPEAKER_PREFIX_RE.sub("", out, count=1).strip()
        cleaned = _GENERIC_SPEAKER_PREFIX_RE.sub("", cleaned, count=1).strip()
        if cleaned == out:
            break
        out = cleaned

    def _remove_stage_direction(match: re.Match[str]) -> str:
        whole = (match.group(0) or "").strip()
        if whole.startswith("*") and whole.endswith("*"):
            return ""

        inner = (match.group(2) or match.group(3) or match.group(4) or "").strip().lower()
        norm = re.sub(r"[^a-zA-Z0-9\u00C0-\u017F\s]", " ", inner)
        norm = re.sub(r"\s+", " ", norm).strip()
        if not norm:
            return ""
        if len(norm.split()) <= 8:
            return ""
        if any(k in norm for k in _STAGE_KEYWORDS):
            return ""
        return match.group(0)

    out = _STAGE_DIRECTION_RE.sub(_remove_stage_direction, out)
    out = _LEADING_FILLER_RE.sub("", out)
    out = _META_ANSWER_PREFIX_RE.sub("", out).strip()
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

def _memory_unknown_reply(intent: str, query: str = "") -> str:
    if intent == "memory_recap":
        return "Al momento non mi viene in mente nulla di preciso da riassumere."
    if _build_query_plan(query).profile_query:
        target = _infer_profile_query_target(query)
        if target == _MEMORY_SUBJECT_AVATAR:
            return "Su questo non ho abbastanza elementi per risponderti con sicurezza."
        if target == _MEMORY_SUBJECT_USER:
            return "Su questo non ho abbastanza elementi affidabili su di te per risponderti bene."
    return "Su questo non ho abbastanza elementi per risponderti con sicurezza."

def _auto_remember_confirmation(query: str) -> str:
    target = _infer_profile_query_target(query)
    if target == _MEMORY_SUBJECT_USER:
        return "Ok, terrò a mente questa cosa su di te."
    if target == _MEMORY_SUBJECT_AVATAR:
        return "Ok, terrò a mente questa cosa su di me."
    return "Ok, terrò a mente questa cosa."

def _token_set(text: str) -> set[str]:
    # Separa token con apostrofo italiano (es. "l'utente" → "utente")
    tokens = []
    for tok in _WORD_TOKEN_RE.findall((text or "").lower()):
        if "'" in tok or "\u2019" in tok:
            tokens.extend(re.split(r"[''\u2019]", tok))
        else:
            tokens.append(tok)
    return {t for t in tokens if len(t) >= 3 and not t.isdigit()}

_ALLOWED_CHAT_INTENTS = {"chitchat", "session_recap", "memory_recap", "memory_qna", "creative_open"}

@dataclass(frozen=True)

class QueryFacet:
    topic: str
    anchor_terms: tuple[str, ...]
    preferred_family: str  # "manual", "file", "image_description", "any"
    required: bool

@dataclass(frozen=True)

class QueryPlan:
    cleaned_query: str
    document_query: bool = False
    profile_query: bool = False
    visual_query: bool = False
    definition_query: bool = False
    memory_reference: bool = False
    focus_sources: tuple[str, ...] = ()
    profile_target: str = _MEMORY_SUBJECT_AMBIGUOUS
    explicit_recap: bool = False
    normalized_intent: str = "chitchat"
    fallback_intents: tuple[str, ...] = ()
    visual_strength: float = 0.0
    source_preference: str = "any"
    wants_multi_source_coverage: bool = False
    topical_terms: tuple[str, ...] = ()

@lru_cache(maxsize=4096)

def _build_query_plan(query: str, intent: str = "") -> QueryPlan:
    cleaned_query = clean_text(query or "")
    q = cleaned_query.lower()
    normalized_intent_hint = clean_text(intent or "").strip().lower()
    tokens = _token_set(q)
    reference_tokens = _reference_content_tokens(q)

    if not tokens:
        normalized_intent = normalized_intent_hint if normalized_intent_hint in _ALLOWED_CHAT_INTENTS else "chitchat"
        return QueryPlan(
            cleaned_query=cleaned_query,
            normalized_intent=normalized_intent,
            fallback_intents=("memory_recap", "memory_qna") if normalized_intent == "session_recap" else (),
        )

    has_doc_hint = any(t in _DOC_QUERY_HINTS for t in tokens)
    has_profile_hint = any(t in _PROFILE_QUERY_HINTS for t in tokens)
    has_image_hint = any(t in _IMAGE_QUERY_HINTS for t in tokens)
    has_reference_hint = any(t in _MEMORY_REFERENCE_HINTS for t in tokens)
    has_question_form = ("?" in q) or bool(
        re.search(r"^\s*(chi|come|cosa|dove|quando|perche|a che ora|di dove|dimmi|raccontami|spiegami|parlami)\b", q)
    )
    profile_target = _infer_profile_query_target(q)
    has_personal_anchor = bool(
        re.search(
            r"\b("
            r"hai|sei|vivi|vive|abiti|abita|lavori|lavora|studi|preferisci|bevi|mangi|vai|prendi|usi|tifi|"
            r"sposti|allerg\w*|animali|gatto|cane|patente|password|colloquio|appuntamento|"
            r"dimentic\w*|insicur\w*|chiami|chiama|ricord\w*|sai"
            r")\b",
            q,
        )
    )

    if re.search(r"\bchi sei\b|\bcome ti chiami\b|\bpresent\w*\b", q):
        has_profile_hint = True
    if has_question_form and has_personal_anchor:
        has_profile_hint = True
    if profile_target in {_MEMORY_SUBJECT_AVATAR, _MEMORY_SUBJECT_USER}:
        has_profile_hint = True

    document_query = has_doc_hint or has_image_hint
    profile_query = has_profile_hint
    focus_sources: list[str] = []
    if has_image_hint:
        focus_sources.append("image_description")
    if has_doc_hint:
        focus_sources.append("file")

    memory_reference = has_reference_hint or bool(re.search(r"\bricord\w*|\bmemorizz\w*", q))
    definition_query = bool(reference_tokens) and bool(
        re.search(
            r"\b(che cos(?:a)?['’]?e|cos(?:a)?['’]?e|cosa significa|cosa vuol dire|cosa indica|chi e|chi e'|chi è)\b",
            q,
            re.IGNORECASE,
        )
    )
    visual_query = document_query and bool(focus_sources) and all(source == "image_description" for source in focus_sources)
    explicit_recap = bool(re.search(r"\b(riepilog\w*|riassum\w*|ricapitol\w*)\b", q, re.IGNORECASE))
    memory_routing_signal = document_query or profile_query or memory_reference or definition_query

    normalized_intent = normalized_intent_hint if normalized_intent_hint in _ALLOWED_CHAT_INTENTS else ""
    if explicit_recap:
        normalized_intent = "memory_recap"
    elif _RECAP_QUERY_RE.search(q):
        if document_query or profile_query or definition_query or (memory_reference and reference_tokens):
            normalized_intent = "memory_qna"
        else:
            normalized_intent = "memory_recap"
    elif normalized_intent in {"chitchat", "creative_open", "session_recap", "memory_recap"}:
        if memory_routing_signal and (("?" in q) or q.startswith(("dimmi", "raccontami", "spiegami", "parlami"))):
            normalized_intent = "memory_qna"
        elif profile_query and normalized_intent in {"chitchat", "creative_open"}:
            normalized_intent = "memory_qna"
    elif not normalized_intent:
        if memory_routing_signal and (("?" in q) or q.startswith(("dimmi", "raccontami", "spiegami", "parlami"))):
            normalized_intent = "memory_qna"
        else:
            normalized_intent = "chitchat"

    # Fallback: se l'intent finale è memory_qna con domanda esplicita
    # ma nessun hint documentale forte è presente, abilita document_query
    # per cercare anche nei file.  Se profile_query era attivo solo da
    # inferenza debole (es. "ho"/"sono" in una domanda), disabilitalo
    # per non filtrare via i contenuti external/file.
    if has_question_form and normalized_intent == "memory_qna" and not document_query:
        _explicit_profile = any(t in _PROFILE_QUERY_HINTS for t in tokens) or has_personal_anchor
        if not memory_routing_signal or (profile_query and not _explicit_profile):
            document_query = True
            if not _explicit_profile:
                profile_query = False
            memory_routing_signal = True

    fallback_intents: list[str] = []
    if normalized_intent == "session_recap":
        fallback_intents.extend(["memory_recap", "memory_qna"])
    elif normalized_intent == "memory_qna" and not visual_query and re.search(r"\bricord\w*|\bmemorizz\w*|\bmemori\w*", q, re.IGNORECASE):
        fallback_intents.append("memory_recap")
    elif normalized_intent == "creative_open" and (document_query or profile_query):
        fallback_intents.append("memory_qna")
    elif normalized_intent == "chitchat" and "?" in q and memory_routing_signal:
        fallback_intents.append("memory_qna")

    has_strong_visual = bool(tokens & _STRONG_VISUAL_ANCHORS)
    has_strong_file = bool(tokens & _STRONG_FILE_ANCHORS)
    if has_strong_visual and not has_strong_file:
        visual_strength = 1.0
        source_preference = "image"
    elif has_strong_visual and has_strong_file:
        visual_strength = 0.5
        source_preference = "mixed"
    elif has_image_hint and not has_strong_file:
        visual_strength = 0.7
        source_preference = "image"
    elif has_doc_hint:
        visual_strength = 0.0
        source_preference = "file"
    else:
        visual_strength = 0.0
        source_preference = "any"

    wants_multi_source = bool(explicit_recap and (
        (profile_query and document_query)
        or len(focus_sources) >= 2
        or _MULTI_SOURCE_CONJUNCTION_RE.search(q)
    ))
    topical_terms = tuple(reference_tokens) if reference_tokens else tuple(tokens - _REFERENCE_QUERY_STOPWORDS)

    return QueryPlan(
        cleaned_query=cleaned_query,
        document_query=document_query,
        profile_query=profile_query,
        visual_query=visual_query,
        definition_query=definition_query,
        memory_reference=memory_reference,
        focus_sources=tuple(focus_sources),
        profile_target=profile_target if profile_query else _MEMORY_SUBJECT_AMBIGUOUS,
        explicit_recap=explicit_recap,
        normalized_intent=normalized_intent,
        fallback_intents=tuple(fallback_intents),
        visual_strength=visual_strength,
        source_preference=source_preference,
        wants_multi_source_coverage=wants_multi_source,
        topical_terms=topical_terms,
    )

_FACET_SPLIT_RE = re.compile(r",\s*(?:e\s+)?|(?:\be\b)\s+(?:cosa\b\s+)?|(?:\be\b)\s+", re.IGNORECASE)
_FACET_CLAUSE_RE = re.compile(
    r"(?:cosa\s+(?:sai|ricord\w*|mostr\w*)\s+(?:del(?:la|lo|l[''e]|le|l|gli|i)?|di|su(?:l(?:la|lo|l[''e]|le|gli|i)?)?|il|la|lo|le|i|gli|l[''a])\s*)",
    re.IGNORECASE,
)
_FACET_VISUAL_FAMILY_RE = re.compile(r"\b(immagine|immagini|foto|schermata|diagramma|grafico|visivo|visivi|specchio)\b", re.IGNORECASE)
_FACET_FILE_FAMILY_RE = re.compile(r"\b(fattura|fatture|pdf|documento|documenti|file|contratto|certificato|modulo)\b", re.IGNORECASE)
_FACET_MANUAL_FAMILY_RE = re.compile(r"\b(utente|interlocutor\w*|profilo|nome|chiami|identit\w*|avatar)\b", re.IGNORECASE)

def _extract_requested_facets(query: str, plan: QueryPlan) -> list[QueryFacet]:
    """Extract topic facets from a multi-source or multi-topic query.

    Each facet carries anchor terms and a preferred source family so that
    downstream hit-selection can guarantee per-facet coverage.
    """
    q = clean_text(query or "").lower()
    if not q:
        return []

    raw_parts: list[str] = []
    clause_parts = _FACET_CLAUSE_RE.split(q)
    clause_parts = [p.strip().strip(",").strip() for p in clause_parts if p and p.strip()]
    first_clause = _FACET_CLAUSE_RE.search(q)
    if first_clause and first_clause.start() > 0 and clause_parts:
        clause_parts = clause_parts[1:]
    if len(clause_parts) >= 2:
        raw_parts = clause_parts
    else:
        conj_parts = _FACET_SPLIT_RE.split(q)
        conj_parts = [p.strip() for p in conj_parts if p and len(p.strip()) >= 3]
        if len(conj_parts) >= 2:
            raw_parts = conj_parts
        else:
            raw_parts = [q]

    facets: list[QueryFacet] = []
    seen_topics: set[str] = set()
    for part in raw_parts:
        part_tokens = _token_set(part)
        anchor_terms = tuple(t for t in part_tokens if len(t) >= 4 and t not in _REFERENCE_QUERY_STOPWORDS)
        if not anchor_terms:
            continue
        topic = " ".join(sorted(anchor_terms)[:3])
        if topic in seen_topics:
            continue
        seen_topics.add(topic)
        if _FACET_MANUAL_FAMILY_RE.search(part):
            family = "manual"
        elif _FACET_VISUAL_FAMILY_RE.search(part):
            family = "image_description"
        elif _FACET_FILE_FAMILY_RE.search(part):
            family = "file"
        else:
            family = "any"
        facets.append(QueryFacet(
            topic=part.strip()[:80],
            anchor_terms=anchor_terms,
            preferred_family=family,
            required=True,
        ))
    return facets

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

def _reference_content_tokens(query: str) -> set[str]:
    return {
        tok for tok in _token_set(query)
        if len(tok) >= 4 and tok not in _REFERENCE_QUERY_STOPWORDS
    }

def _reference_overlap_ratio(query: str, doc: str, meta: Optional[dict] = None) -> float:
    q_tokens = _reference_content_tokens(query)
    if not q_tokens:
        return 0.0

    support_tokens = _token_set(doc)
    filename = clean_text(str((meta or {}).get("source_filename") or "")).lower()
    if filename:
        support_tokens |= _token_set(filename)
    if not support_tokens:
        return 0.0

    overlap = len(q_tokens & support_tokens)
    return float(overlap) / float(len(q_tokens))

def _has_reference_evidence(
    query: str,
    doc: str,
    meta: Optional[dict] = None,
    *,
    allow_lexical_only: bool = False,
) -> bool:
    ref_overlap = _reference_overlap_ratio(query, doc, meta)
    if ref_overlap >= 0.34:
        return True

    filename_boost = _filename_overlap_boost(query, meta or {})
    if filename_boost >= 0.06:
        return True

    if allow_lexical_only and _lexical_overlap_ratio(query, doc) >= 0.18:
        return True

    return False

def _external_chunk_noise_penalty(doc: str, meta: Optional[dict]) -> float:
    source_type = _src_type(meta)
    if source_type not in _EXTERNAL_MEMORY_SOURCE_TYPES:
        return 0.0

    txt = clean_text(doc or "").lower()
    if not txt:
        return 0.0

    penalty = 0.0
    unique_tokens = len(_token_set(txt))
    if unique_tokens < 10:
        penalty += 0.05

    noisy_markers = (
        "copyright", "tutti i diritti riservati", "non rispondere a questa email",
        "messaggio automatico", "scopri come proteggerti", "privacy",
    )
    if any(marker in txt for marker in noisy_markers):
        penalty += 0.08

    if txt.count("@") >= 1:
        penalty += 0.04

    return min(0.16, penalty)

def _rank_external_probe_hits(
    col: Any,
    query: str,
    query_embedding: List[float],
    top_k: int,
) -> List[Tuple[float, str, dict]]:
    if top_k <= 0:
        return []
    ranked = _vector_search_ranked(
        col=col,
        query_embedding=query_embedding,
        top_k=max(3, min(top_k, 8)),
        where={"source_type": {"$in": list(_EXTERNAL_MEMORY_SOURCE_TYPES)}},
    )
    if not ranked:
        return []

    boosted: list[tuple[float, str, dict]] = []
    for score, doc, meta in ranked:
        lexical = _lexical_overlap_ratio(query, doc)
        filename_boost = _filename_overlap_boost(query, meta or {})
        content_overlap = _reference_overlap_ratio(query, doc, meta)
        penalty = _external_chunk_noise_penalty(doc, meta)
        adjusted = min(
            1.0,
            max(score, 0.14)
            + min(0.18, content_overlap * 0.22)
            + filename_boost
            - penalty,
        )
        safe_meta = dict(meta or {})
        safe_meta["_vector_similarity"] = round(float((meta or {}).get("_vector_similarity", score)), 6)
        safe_meta["_lexical_overlap"] = round(float(lexical), 6)
        safe_meta["_reference_content_overlap"] = round(float(content_overlap), 6)
        safe_meta["_filename_overlap_boost"] = round(float(filename_boost), 6)
        safe_meta["_external_noise_penalty"] = round(float(penalty), 6)
        safe_meta["_external_reference_probe"] = True
        safe_meta["_hybrid_score"] = round(float(adjusted), 6)
        boosted.append((adjusted, doc, safe_meta))

    boosted.sort(key=lambda x: x[0], reverse=True)
    return boosted

def _rank_source_probe_hits(
    col: Any,
    query: str,
    query_embedding: List[float],
    top_k: int,
) -> List[Tuple[float, str, dict]]:
    if top_k <= 0:
        return []

    plan = _build_query_plan(query)
    pref = plan.source_preference
    if pref == "image":
        source_order = ["image_description", "image_ocr", "file"]
    elif pref == "mixed":
        source_order = ["image_description", "file", "image_ocr"]
    else:
        source_order = ["file", "image_description", "image_ocr"] if not plan.visual_query else ["image_description", "image_ocr", "file"]
    combined: list[tuple[float, str, dict]] = []
    seen_keys: set[tuple[str, str]] = set()

    for priority, source_type in enumerate(source_order):
        ranked = _vector_search_ranked(
            col=col,
            query_embedding=query_embedding,
            top_k=max(3, min(top_k, 6)),
            where={"source_type": source_type},
        )
        for score, doc, meta in ranked:
            safe_meta = dict(meta or {})
            key = (clean_text(doc or "")[:240].lower(), str(safe_meta.get("source_filename") or ""))
            if key in seen_keys:
                continue
            seen_keys.add(key)
            boosted_score = min(1.0, float(score) + max(0.0, 0.05 - (priority * 0.02)))
            safe_meta["_hybrid_score"] = round(float(boosted_score), 6)
            safe_meta["_source_probe"] = source_type
            combined.append((boosted_score, doc, safe_meta))

    if _reference_content_tokens(query) and not plan.visual_query:
        try:
            raw_files = col.get(
                where={"source_type": "file"},
                include=["documents", "metadatas"],
                limit=max(512, top_k * 256),
                offset=0,
            )
            file_docs = raw_files.get("documents") or []
            file_metas = raw_files.get("metadatas") or []
            for doc, meta in zip(file_docs, file_metas):
                if not isinstance(doc, str) or not doc.strip():
                    continue
                safe_meta = dict(meta or {})
                filename_boost = _filename_overlap_boost(query, safe_meta)
                if filename_boost < 0.06:
                    continue
                key = (clean_text(doc or "")[:240].lower(), str(safe_meta.get("source_filename") or ""))
                if key in seen_keys:
                    continue
                seen_keys.add(key)
                lexical = _lexical_overlap_ratio(query, doc)
                ref_overlap = _reference_overlap_ratio(query, doc, safe_meta)
                boosted_score = min(
                    1.0,
                    0.28 + filename_boost + min(0.14, ref_overlap * 0.22) + min(0.06, lexical),
                )
                safe_meta["_hybrid_score"] = round(float(boosted_score), 6)
                safe_meta["_source_probe"] = "file_filename"
                safe_meta["_filename_overlap_boost"] = round(float(filename_boost), 6)
                safe_meta["_reference_overlap"] = round(float(ref_overlap), 6)
                safe_meta["_lexical_overlap"] = round(float(lexical), 6)
                combined.append((boosted_score, doc, safe_meta))
        except Exception:
            pass

    return _rerank_reference_hits(query=query, ranked_hits=combined, strict_external=True)

def _rerank_reference_hits(
    query: str,
    ranked_hits: List[Tuple[float, str, dict]],
    *,
    strict_external: bool = False,
) -> List[Tuple[float, str, dict]]:
    if not ranked_hits:
        return []

    reference_tokens = _reference_content_tokens(query)
    plan = _build_query_plan(query)
    visual_query = plan.visual_query
    visual_strength = plan.visual_strength
    reranked: list[tuple[float, str, dict]] = []
    for score, doc, meta in ranked_hits:
        safe_meta = dict(meta or {})
        source_type = _src_type(safe_meta)
        lexical = _lexical_overlap_ratio(query, doc)
        ref_overlap = _reference_overlap_ratio(query, doc, safe_meta)
        filename_boost = _filename_overlap_boost(query, safe_meta)
        filename_signal = min(1.0, filename_boost / 0.18) if filename_boost > 0.0 else 0.0
        topical_signal = max(ref_overlap, filename_signal) if reference_tokens else max(lexical, ref_overlap, filename_signal)

        adjusted = max(0.0, float(score))

        if source_type in _EXTERNAL_MEMORY_SOURCE_TYPES:
            noise_penalty = _external_chunk_noise_penalty(doc, safe_meta)
            adjusted = min(
                1.0,
                max(adjusted, 0.14)
                + min(0.18, ref_overlap * 0.22)
                + filename_boost
                - noise_penalty,
            )
            safe_meta["_external_noise_penalty"] = round(float(noise_penalty), 6)
        else:
            adjusted = min(1.0, adjusted + min(0.12, ref_overlap * 0.18))

        if source_type == "image_description":
            if visual_query:
                vs_factor = max(0.4, visual_strength)
                adjusted = min(
                    1.0,
                    adjusted + min(0.16 * vs_factor, ref_overlap * 0.26 + filename_boost + max(0.0, lexical - 0.06) * 0.18),
                )
            elif reference_tokens:
                penalty = max(0.04, 0.14 * (1.0 - visual_strength))
                adjusted = max(0.0, adjusted - penalty)
            if topical_signal <= 0.0:
                adjusted = max(0.0, adjusted - 0.22)
        elif source_type == "file" and reference_tokens and not visual_query:
            adjusted = min(
                1.0,
                adjusted + min(0.16, ref_overlap * 0.18 + filename_boost + max(0.0, lexical - 0.08) * 0.10),
            )
        elif source_type in _EXTERNAL_MEMORY_SOURCE_TYPES and topical_signal <= 0.0:
            adjusted = max(0.0, adjusted - 0.14)

        safe_meta["_lexical_overlap"] = round(float(lexical), 6)
        safe_meta["_reference_overlap"] = round(float(ref_overlap), 6)
        safe_meta["_reference_content_overlap"] = round(float(ref_overlap), 6)
        safe_meta["_filename_overlap_boost"] = round(float(filename_boost), 6)
        safe_meta["_hybrid_score"] = round(float(adjusted), 6)

        if strict_external and source_type in _EXTERNAL_MEMORY_SOURCE_TYPES and topical_signal <= 0.0:
            continue

        reranked.append((adjusted, doc, safe_meta))

    reranked.sort(
        key=lambda x: (
            x[0],
            float((x[2] or {}).get("_reference_overlap", 0.0)),
            float((x[2] or {}).get("_filename_overlap_boost", 0.0)),
            float((x[2] or {}).get("_lexical_overlap", 0.0)),
        ),
        reverse=True,
    )

    def _sig(m: dict, *, include_lexical: bool = True) -> float:
        """Max topical signal from rerank metadata."""
        fb = float(m.get("_filename_overlap_boost", 0.0))
        return max(
            float(m.get("_reference_overlap", 0.0)),
            min(1.0, fb / 0.18) if fb > 0.0 else 0.0,
            float(m.get("_lexical_overlap", 0.0)) if include_lexical else 0.0,
        )

    if reference_tokens and not visual_query:
        best_file_score = 0.0
        best_file_signal = 0.0
        for adj, _, meta in reranked:
            sm = meta or {}
            if _src_type(sm) != "file":
                continue
            best_file_score = max(best_file_score, float(adj))
            best_file_signal = max(best_file_signal, _sig(sm))
        if best_file_score >= 0.18 and best_file_signal >= 0.18:
            filtered: list[tuple[float, str, dict]] = []
            for adj, doc, meta in reranked:
                if _src_type(meta or {}) == "image_description":
                    isig = _sig(meta or {})
                    if isig + 0.04 < best_file_signal:
                        continue
                    if float(adj) + 0.02 < best_file_score and isig < best_file_signal:
                        continue
                filtered.append((adj, doc, meta))
            if filtered:
                reranked = filtered

    if strict_external:
        best_image_signal = 0.0
        for _, _, meta in reranked:
            if _src_type(meta or {}) == "image_description":
                best_image_signal = max(best_image_signal, _sig(meta or {}, include_lexical=not reference_tokens))
        if best_image_signal >= 0.35:
            filtered: list[tuple[float, str, dict]] = []
            for adj, doc, meta in reranked:
                if _src_type(meta or {}) == "image_description":
                    isig = _sig(meta or {}, include_lexical=not reference_tokens)
                    if isig + 0.05 < (best_image_signal * 0.6):
                        continue
                filtered.append((adj, doc, meta))
            if filtered:
                reranked = filtered

    return reranked

def _has_visual_factual_hits(metas: List[dict]) -> bool:
    return any(_src_type(meta) in {"image_description", "image_ocr"} for meta in metas or [])

def _filter_ranked_hits_for_profile_target(
    ranked_hits: List[Tuple[float, str, dict]],
    profile_target: Optional[str],
) -> List[Tuple[float, str, dict]]:
    if not ranked_hits:
        return []

    filtered: list[tuple[float, str, dict]] = []
    target = profile_target or _MEMORY_SUBJECT_AMBIGUOUS
    for score, doc, meta in ranked_hits:
        subject = _effective_memory_subject(doc, meta)
        if not _profile_subject_matches_target(subject, target):
            continue
        safe_meta = _annotate_memory_subject(meta, doc)
        safe_meta["_profile_target"] = profile_target
        filtered.append((score, doc, safe_meta))
    return filtered

def _boost_profile_memory_hits(
    query: str,
    ranked_hits: List[Tuple[float, str, dict]],
    profile_target: Optional[str],
) -> List[Tuple[float, str, dict]]:
    if not ranked_hits:
        return []

    boosted_hits: list[tuple[float, str, dict]] = []
    target = profile_target or _MEMORY_SUBJECT_AMBIGUOUS
    for score, doc, meta in ranked_hits:
        subject = _effective_memory_subject(doc, meta)
        if not _profile_subject_matches_target(subject, target):
            continue
        lexical = _lexical_overlap_ratio(query, doc)
        boosted = min(1.0, max(score, 0.30) + 0.22 + min(0.08, lexical))
        safe_meta = _annotate_memory_subject(meta, doc)
        safe_meta["_hybrid_score"] = round(float(boosted), 6)
        safe_meta["_profile_source_match"] = True
        safe_meta["_profile_target"] = profile_target
        boosted_hits.append((boosted, doc, safe_meta))
    return boosted_hits

def _retrieve_profile_boosted_hits(
    col: Any,
    query: str,
    query_embedding: List[float],
    top_k: int,
    *,
    profile_target: Optional[str],
) -> List[Tuple[float, str, dict]]:
    ranked_profile = _vector_search_ranked(
        col=col,
        query_embedding=query_embedding,
        top_k=max(3, min(top_k, 8)),
        where={"source_type": {"$in": list(_PROFILE_MEMORY_SOURCE_TYPES)}},
    )
    return _boost_profile_memory_hits(
        query=query,
        ranked_hits=ranked_profile,
        profile_target=profile_target,
    )

def _boost_definition_profile_hits(
    query: str,
    ranked_hits: List[Tuple[float, str, dict]],
) -> List[Tuple[float, str, dict]]:
    if not ranked_hits:
        return []

    boosted_hits: list[tuple[float, str, dict]] = []
    for score, doc, meta in ranked_hits:
        safe_meta = _annotate_memory_subject(meta, doc)
        source_type = _src_type(safe_meta)
        if source_type not in _PROFILE_MEMORY_SOURCE_TYPES:
            continue

        ref_overlap = _reference_overlap_ratio(query, doc, safe_meta)
        lexical = _lexical_overlap_ratio(query, doc)
        if ref_overlap <= 0.0 and lexical < 0.12:
            continue

        boosted = min(1.0, max(score, 0.26) + 0.18 + min(0.16, ref_overlap * 0.30) + min(0.06, lexical))
        safe_meta["_hybrid_score"] = round(float(boosted), 6)
        safe_meta["_definition_profile_match"] = True
        safe_meta["_reference_overlap"] = round(float(ref_overlap), 6)
        safe_meta["_lexical_overlap"] = round(float(lexical), 6)
        boosted_hits.append((boosted, doc, safe_meta))

    boosted_hits.sort(key=lambda x: x[0], reverse=True)
    return boosted_hits

def _retrieve_definition_context(
    col: Any,
    query: str,
    query_embedding: List[float],
    top_k: int,
) -> tuple[List[str], List[dict]]:
    if top_k <= 0:
        return [], []

    ranked_definition_profile = _vector_search_ranked(
        col=col,
        query_embedding=query_embedding,
        top_k=max(3, min(top_k, 8)),
        where={"source_type": {"$in": list(_PROFILE_MEMORY_SOURCE_TYPES)}},
    )
    boosted_definition_profile = _boost_definition_profile_hits(
        query=query,
        ranked_hits=ranked_definition_profile,
    )
    docs_definition, metas_definition = _select_factual_hits(
        ranked_hits=boosted_definition_profile,
        query=query,
        top_k=max(1, min(top_k, 2)),
    )
    if not docs_definition:
        return [], []
    return _dedupe_chunks(docs_definition, metas_definition)

def _is_pure_remember_request(query: str) -> bool:
    q = clean_text(query or "")
    if not q:
        return False
    if _detect_remember_intent(q) is None:
        return False
    if "?" in q:
        return False
    if re.search(r"\b(dimmi|spiegami|raccontami|e poi|oltre|adesso|ad esempio)\b", q, re.IGNORECASE):
        return False
    return True

def _answer_has_profile_perspective_mismatch(answer: str, profile_target: str) -> bool:
    txt = clean_text(answer or "").lower()
    if not txt or profile_target not in {_MEMORY_SUBJECT_AVATAR, _MEMORY_SUBJECT_USER}:
        return False

    first_person = bool(re.search(r"\b(io|mi|mio|mia|miei|mie|sono|ho|vivo|abito|mi chiamo|preferisco|adoro|bevo|mangio|lavoro|studio)\b", txt))
    second_person = bool(re.search(r"\b(tu|ti|tuo|tua|tuoi|tue|sei|hai|vivi|abiti|ti chiami|preferisci|adori|bevi|mangi|lavori|studi)\b", txt))
    if profile_target == _MEMORY_SUBJECT_USER and first_person:
        return True
    if profile_target == _MEMORY_SUBJECT_AVATAR and second_person:
        return True
    return False

def _answer_has_incomplete_profile_claim(answer: str) -> bool:
    txt = clean_text(answer or "")
    if not txt:
        return False
    patterns = [
        r"\b(?:il|la|i|le)\s+(?:tuo|tua|tuoi|tue|mio|mia|miei|mie)\s+[A-Za-zÀ-ÿ'’\-]+(?:\s+[A-Za-zÀ-ÿ'’\-]+){0,2}\s+(?:e'|è)\s*(?:[.,;!?]|\be\b|\bma\b|$)",
        r"\b(?:mi|ti)\s+chiamo\s*(?:[.,;!?]|\be\b|\bma\b|$)",
    ]
    return any(re.search(pattern, txt, re.IGNORECASE) for pattern in patterns)

def _answer_has_meta_framing(answer: str) -> bool:
    txt = clean_text(answer or "")
    if not txt:
        return False
    return bool(re.search(r"\b(secondo la memoria|secondo le informazioni raccolte|dal contesto|in base alla memoria)\b", txt, re.IGNORECASE))

def _answer_distorts_profile_relation(answer: str, factual_context: str) -> bool:
    """Detect when the answer transforms a relational phrase into an identity claim.

    Example distortions to catch:
    - context: "l'utente chiama specchio quieto la postazione vicino alla finestra"
      answer: "tu ti chiami specchio quieto" or "il tuo nome e' specchio quieto"
    - context: "indica il kit fotografico"
      answer: "si chiama kit fotografico"
    """
    ans = clean_text(answer or "").lower()
    ctx = clean_text(factual_context or "").lower()
    if not ans or not ctx:
        return False

    _RELATION_RE = re.compile(
        r"\b(?:chiama|indica|significa|identifica|si riferisce a|vuol dire)\s+([a-zA-ZÀ-ÿ'' ]{3,40}?)(?:\s+(?:la|il|lo|le|i|gli|una?|l[''a])\b|\s*[.,;!?]|\s+(?:postazione|zona|piano|kit|punto|area|luogo|raccoglitore|codice|contenitore))",
        re.IGNORECASE,
    )
    relation_terms: set[str] = set()
    for m in _RELATION_RE.finditer(ctx):
        term = clean_text(m.group(1)).strip().lower()
        if term and len(term) >= 3:
            relation_terms.add(term)

    if not relation_terms:
        return False

    _IDENTITY_CLAIM_RE = re.compile(
        r"\b(?:(?:mi|ti|si)\s+chiam[oai]|(?:il\s+(?:tuo|mio)\s+nome\s+(?:e'|è))|(?:sono|sei)\s+)",
        re.IGNORECASE,
    )
    identity_match = _IDENTITY_CLAIM_RE.search(ans)
    if not identity_match:
        return False

    claim_end = identity_match.end()
    after_claim = ans[claim_end:claim_end + 60].strip()
    for term in relation_terms:
        if term in after_claim:
            return True

    return False

def _apply_profile_answer_repairs(
    answer: str,
    query: str,
    recent_conversation: str,
    factual_context: str,
    profile_target: str,
) -> str:
    if profile_target not in {_MEMORY_SUBJECT_AVATAR, _MEMORY_SUBJECT_USER}:
        return answer

    current = answer
    profile_mode = "profile_user" if profile_target == _MEMORY_SUBJECT_USER else "profile_avatar"
    repair_checks = [
        (
            lambda text: _answer_has_profile_perspective_mismatch(text, profile_target),
            lambda text: not _answer_has_profile_perspective_mismatch(text, profile_target),
        ),
        (
            _answer_has_incomplete_profile_claim,
            lambda text: not _answer_has_incomplete_profile_claim(text),
        ),
        (
            _answer_has_meta_framing,
            lambda text: not _answer_has_meta_framing(text),
        ),
        (
            lambda text: _answer_distorts_profile_relation(text, factual_context),
            lambda text: not _answer_distorts_profile_relation(text, factual_context),
        ),
    ]

    for should_repair, repair_ok in repair_checks:
        if not should_repair(current):
            continue
        repaired = _rewrite_answer_with_guardrails(
            query=query,
            recent_conversation=recent_conversation,
            factual_context=factual_context,
            original_answer=current,
            mode=profile_mode,
        )
        if repaired and repair_ok(repaired):
            current = repaired

    return current

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

def _is_vague_memory_query(query: str, plan: QueryPlan) -> bool:
    q = clean_text(query or "").lower()
    if not q:
        return False
    if plan.document_query or plan.definition_query:
        return False
    token_count = len(_token_set(q))
    if token_count > max(2, int(RAG_QUERY_REWRITE_MAX_TOKENS)):
        return False
    return bool(
        re.search(
            r"\b(e\s+poi|altro|altre|cos['’]?altro|dimmi di piu|parlami di me|raccontami di me|hobby|passioni|tempo libero|descrivimi|chi sono)\b",
            q,
            re.IGNORECASE,
        )
        or plan.profile_query
        or plan.memory_reference
    )

def _rewrite_query_for_memory_retrieval(query: str, recent_conversation: str) -> str:
    if not RAG_ENABLE_QUERY_REWRITE:
        return query

    system = (
        "Riformula la richiesta utente per retrieval di memoria personale. "
        "Mantieni intent e lingua, NON aggiungere fatti nuovi, NON cambiare il significato. "
        "Rispondi SOLO con JSON valido: {\"rewritten_query\":\"...\"}."
    )
    user = (
        f"CONTESTO_RECENTE:\n{_truncate_for_prompt(recent_conversation, 700)}\n\n"
        f"RICHIESTA_ORIGINALE:\n{_truncate_for_prompt(query, 280)}\n\n"
        "Riformula in una singola domanda specifica e concisa ottimizzata per cercare fatti in memoria."
    )
    try:
        raw = ollama_chat(
            [
                {"role": "system", "content": system},
                {"role": "user", "content": user},
            ],
            timeout=18,
            num_predict_override=64,
        ).strip()
        obj = _extract_first_json_object(raw)
        if obj:
            rewritten = clean_text(str(obj.get("rewritten_query", "")))
            if rewritten and len(_token_set(rewritten)) >= 2:
                return rewritten
    except Exception:
        pass
    return query

def _compute_grounding_score(answer: str, query: str, factual_docs: List[str]) -> float:
    if not factual_docs:
        return 1.0
    factual_blob = " ".join(factual_docs)
    support_tokens = _support_token_set(query, "", factual_docs)
    unsupported_ratio = _unsupported_token_ratio(answer, support_tokens, _ALIGNMENT_TOKEN_STOPWORDS)
    answer_to_facts_overlap = _lexical_overlap_ratio(answer, factual_blob)
    denial_penalty = 0.32 if _answer_denies_available_context(answer) else 0.0
    score = 1.0 - (unsupported_ratio * 0.72) - denial_penalty + min(0.22, answer_to_facts_overlap * 0.45)
    return max(0.0, min(1.0, float(score)))

def _detect_grounding_contradiction(answer: str, query: str, factual_docs: List[str]) -> bool:
    if not factual_docs:
        return False
    factual_blob = " ".join(factual_docs)
    overlap = _lexical_overlap_ratio(answer, factual_blob)
    support_tokens = _support_token_set(query, "", factual_docs)
    unsupported_ratio = _unsupported_token_ratio(answer, support_tokens, _ALIGNMENT_TOKEN_STOPWORDS)
    return bool(len(clean_text(answer or "")) >= 40 and overlap < 0.07 and unsupported_ratio >= 0.74)

def _build_chat_quality_metrics(
    intent: str,
    query: str,
    factual_docs: List[str],
    factual_metas: List[dict],
    answer: str,
    rewritten_query: str,
) -> dict[str, Any]:
    grounding_score = _compute_grounding_score(answer, query, factual_docs)
    contradiction = _detect_grounding_contradiction(answer, query, factual_docs)
    top_score = 0.0
    avg_score = 0.0
    if factual_metas:
        scores = []
        for meta in factual_metas:
            try:
                scores.append(float((meta or {}).get("_hybrid_score", 0.0)))
            except Exception:
                continue
        if scores:
            top_score = max(scores)
            avg_score = sum(scores) / float(len(scores))

    return {
        "intent": intent,
        "rewritten_query_used": rewritten_query != query,
        "retrieval_count": len(factual_docs),
        "top_retrieval_score": round(float(top_score), 4),
        "avg_retrieval_score": round(float(avg_score), 4),
        "grounding_score": round(float(grounding_score), 4),
        "contradiction_detected": contradiction,
    }

def _lexical_overlap_ratio(query: str, doc: str) -> float:
    q_tokens = _token_set(query)
    if not q_tokens:
        return 0.0
    d_tokens = _token_set(doc)
    if not d_tokens:
        return 0.0
    overlap = len(q_tokens & d_tokens)
    return float(overlap) / float(len(q_tokens))

def _truncate_for_prompt(text: str, max_chars: int = 1800) -> str:
    cleaned = clean_text(text or "")
    if len(cleaned) <= max_chars:
        return cleaned
    return cleaned[:max_chars].rstrip() + "..."

def _extractive_visual_answer(query: str, factual_docs: List[str]) -> str:
    sentences: list[str] = []
    for doc in factual_docs:
        if not doc:
            continue
        normalized_doc = re.sub(
            r"^(?:certo,?\s*)?(?:ecco\s+una\s+descrizione\s+dettagliata\s+dell['’]immagine:?\s*)",
            "",
            clean_text(doc),
            flags=re.IGNORECASE,
        )
        parts = re.split(r"(?<=[.!?])\s+", normalized_doc)
        for part in parts:
            cleaned = clean_text(part)
            if len(cleaned) >= 24:
                sentences.append(cleaned)

    if not sentences:
        return ""

    query_tokens = _reference_content_tokens(query)
    _STRUCTURAL_RE = re.compile(
        r"\b(freccia|frecce|collega|collegat\w*|connett\w*|connessi\w*|"
        r"component\w*|element\w*|blocco|blocchi|modulo|moduli|"
        r"server|client|proxy|database|architettura|protocollo|"
        r"invia|riceve|richiesta|risposta|request|response|flusso|"
        r"contiene|include|mostra|rappresent\w*|indica|etichett\w*|"
        r"riquadro|rettangolo|cerchio|linea|sezione|parte|"
        r"sopra|sotto|sinistra|destra|centro|alto|basso)\b",
        re.IGNORECASE,
    )
    ranked_sentences: list[tuple[float, str]] = []
    for idx, sentence in enumerate(sentences):
        overlap = 0.0
        if query_tokens:
            overlap = float(len(query_tokens & _token_set(sentence))) / float(len(query_tokens))
        base = 0.15 if idx == 0 else 0.0
        structural_bonus = 0.08 if _STRUCTURAL_RE.search(sentence) else 0.0
        ranked_sentences.append((overlap + base + structural_bonus, sentence))

    ranked_sentences.sort(key=lambda x: x[0], reverse=True)
    max_sentences = 4
    chosen: list[str] = []
    seen: set[str] = set()
    for _, sentence in ranked_sentences:
        key = sentence.lower()
        if key in seen:
            continue
        seen.add(key)
        chosen.append(sentence)
        if len(chosen) >= max_sentences:
            break

    return " ".join(chosen).strip()

def _extractive_definition_answer(
    query: str,
    factual_docs: List[str],
    factual_metas: List[dict],
) -> str:
    query_tokens = _reference_content_tokens(query)
    candidates: list[tuple[float, str]] = []

    for idx, (doc, meta) in enumerate(zip(factual_docs, factual_metas)):
        cleaned_doc = clean_text(doc)
        if not cleaned_doc:
            continue

        for sentence in re.split(r"(?<=[.!?])\s+", cleaned_doc):
            cleaned_sentence = clean_text(sentence)
            if len(cleaned_sentence) < 18:
                continue

            lexical = _lexical_overlap_ratio(query, cleaned_sentence)
            ref_overlap = _reference_overlap_ratio(query, cleaned_sentence, meta)
            definitional_hint = 0.0
            if re.search(r"\b(indica|significa|vuol dire|identifica|si riferisce|e'|è|sono)\b", cleaned_sentence, re.IGNORECASE):
                definitional_hint = 0.12
            lead_bonus = 0.04 if idx == 0 else 0.0
            token_bonus = 0.0
            if query_tokens:
                token_bonus = min(0.12, float(len(query_tokens & _token_set(cleaned_sentence))) * 0.04)
            score = ref_overlap + min(0.25, lexical * 0.5) + definitional_hint + lead_bonus + token_bonus
            candidates.append((score, cleaned_sentence))

    if not candidates:
        return ""

    candidates.sort(key=lambda x: x[0], reverse=True)
    best = candidates[0][1].strip()
    if best and best[-1] not in ".!?":
        best += "."
    return best

def _answer_definition_query(
    query: str,
    factual_docs: List[str],
    factual_metas: List[dict],
) -> Optional[str]:
    if not _build_query_plan(query).definition_query or not factual_docs:
        return None

    extractive = _extractive_definition_answer(query, factual_docs, factual_metas)
    if extractive:
        return _finalize_chat_answer(extractive) or extractive

    factual_context = _build_context_from_docs(
        factual_docs,
        factual_metas,
        max_chars=min(MAX_CONTEXT_CHARS, 1400),
    )
    if not factual_context:
        return None

    raw_answer = ollama_chat(
        [
            {
                "role": "system",
                "content": (
                    "Rispondi a una domanda definitoria usando solo la MEMORIA_FACTUAL. "
                    "Sii estrattivo e rigoroso. "
                    "Non aggiungere inferenze, esempi o dettagli non presenti. "
                    "Non usare formule meta come 'secondo la memoria' o 'dal contesto'. "
                    "Rispondi in massimo 2 frasi brevi."
                ),
            },
            {
                "role": "user",
                "content": f"DOMANDA:\n{query}\n\nMEMORIA_FACTUAL:\n{factual_context}",
            },
        ],
        timeout=45,
        num_predict_override=90,
    ).strip()
    answer = _finalize_chat_answer(raw_answer) or ""
    if not answer:
        return None

    strict_answer = _rewrite_answer_with_guardrails(
        query=query,
        recent_conversation="",
        factual_context=factual_context,
        original_answer=answer,
        mode="grounded_strict",
    )
    if strict_answer:
        answer = strict_answer

    final_answer = _finalize_chat_answer(answer) or answer
    if _answer_has_meta_framing(final_answer):
        return None
    return final_answer or None

def _build_recall_response(docs: List[str], metas: List[dict]) -> dict:
    return {
        "documents": [docs],
        "metadatas": [metas],
        "distances": [[0] * len(docs)],
        "ids": [[f"doc_{i}" for i in range(len(docs))]],
    }

_REWRITE_CONFIGS: dict[str, tuple[str, str, int, int, int]] = {
    "grounded_strict": (
        "Produci una risposta estrattiva e rigorosa usando solo la MEMORIA_FACTUAL. "
        "Non aggiungere inferenze, abitudini o dettagli non presenti. "
        "Se manca il fatto richiesto, dillo in modo breve e naturale, senza formule ripetitive o troppo rigide. "
        "Non usare formule meta come 'risposta riscritta', 'secondo la memoria', 'dal contesto' o simili.",
        "Rispondi in massimo 2 frasi brevi. Copri solo i fatti richiesti dalla domanda, senza aggiunte.",
        40, 120, 1700,
    ),
    "visual_grounded_strict": (
        "Produci una risposta estrattiva e rigorosa usando solo la MEMORIA_FACTUAL visiva recuperata. "
        "Mantieni solo dettagli letteralmente supportati dai blocchi. "
        "Non introdurre sinonimi descrittivi, non sostituire colori, non inferire identita, emozioni, proprieta' o oggetti non citati. "
        "Se un dettaglio non compare chiaramente, omettilo. "
        "Non usare formule meta come 'secondo la memoria' o 'dal contesto'.",
        "Rispondi in massimo 2 frasi molto concrete. Copri solo i dettagli richiesti dalla domanda, senza abbellimenti.",
        40, 100, 1400,
    ),
    "visual_grounded": (
        "Riscrivi la risposta usando solo la MEMORIA_FACTUAL visiva recuperata. "
        "Mantieni solo dettagli espliciti e osservabili gia' presenti nei blocchi. "
        "Non inferire identita, intenzioni, proprieta', abitudini o colori non chiaramente presenti. "
        "Se un dettaglio non e' chiaro nella memoria factual, non aggiungerlo. "
        "Non usare formule meta come 'secondo la memoria', 'dal contesto' o simili.",
        "Rispondi in massimo 2 frasi brevi e concrete. Riusa i dettagli visivi recuperati senza arricchirli.",
        40, 120, 1500,
    ),
    "profile_avatar": (
        "Riscrivi la risposta in italiano naturale usando solo la MEMORIA_FACTUAL. "
        "La domanda riguarda l'identita o le caratteristiche dell'avatar stesso. "
        "Parla in prima persona naturale. "
        "Non usare seconda persona per fatti che riguardano te. "
        "Se uno o piu dettagli richiesti non compaiono chiaramente nella memoria factual, dillo brevemente senza inventarli. "
        "Non produrre mai frasi tronche, campi vuoti o affermazioni lasciate a meta. "
        "Non usare formule meta come 'risposta riscritta', 'secondo la memoria', 'dal contesto' o simili. "
        "Non premettere 'La risposta corretta e' o formule simili.",
        "Riscrivi in 1-3 frasi chiare e concrete. Cita i dettagli specifici dalla MEMORIA_FACTUAL (nomi, luoghi, fatti) e ammetti in modo breve cosa non sai.",
        45, 160, 1700,
    ),
    "profile_user": (
        "Riscrivi la risposta in italiano naturale usando solo la MEMORIA_FACTUAL. "
        "La domanda riguarda l'interlocutore. "
        "Rivolgiti a lui in seconda persona naturale. "
        "Non trasformare i suoi fatti in fatti tuoi e non usare prima persona autobiografica per rispondere. "
        "Se uno o piu dettagli richiesti non compaiono chiaramente nella memoria factual, dillo brevemente senza inventarli. "
        "Non produrre mai frasi tronche, campi vuoti o affermazioni lasciate a meta. "
        "Non usare formule meta come 'risposta riscritta', 'secondo la memoria', 'dal contesto' o simili. "
        "Non premettere 'La risposta corretta e' o formule simili.",
        "Riscrivi in 1-3 frasi chiare e concrete. Cita i dettagli specifici dalla MEMORIA_FACTUAL (nomi, luoghi, fatti) e ammetti in modo breve cosa non sai.",
        45, 160, 1700,
    ),
    "grounded": (
        "Riscrivi la risposta usando il contesto factual fornito. "
        "Non negare di avere informazioni quando il contesto factual e presente. "
        "Non inventare fatti oltre al contesto. "
        "Tratta la risposta originale come bozza potenzialmente errata: correggila se e in conflitto con la memoria factual. "
        "Non usare formule meta come 'risposta riscritta', 'secondo la memoria', 'dal contesto' o simili. "
        "Non premettere 'La risposta corretta e' o formule simili.",
        "Riscrivi in modo chiaro e concreto, massimo 4 frasi. "
        "Se la domanda chiede dettagli puntuali (nome, luogo, orario, relazione), riportali in modo esplicito.",
        45, 180, 1600,
    ),
    "neutral": (
        "Riscrivi la risposta in italiano naturale, breve e accogliente. "
        "Non inventare dettagli autobiografici non presenti nel contesto. "
        "Se il contesto non contiene fatti personali, usa formulazioni neutrali. "
        "Non usare formule meta come 'risposta riscritta', 'secondo la memoria', 'dal contesto' o simili.",
        "Riscrivi in 1-3 frasi, senza aggiungere fatti nuovi.",
        40, 120, 1200,
    ),
}

_PROFILE_SYS_SUFFIX = {
    _MEMORY_SUBJECT_USER: (
        " La domanda riguarda l'interlocutore: rispondi rivolgendoti a lui in seconda persona "
        "e usa forme come 'hai', 'ti chiami', 'vivi'. Non trasformare i suoi fatti in fatti tuoi. "
        "Se manca uno dei dettagli richiesti, dillo esplicitamente senza inventarlo."
    ),
    _MEMORY_SUBJECT_AVATAR: (
        " La domanda riguarda te: rispondi in prima persona solo con i fatti recuperati. "
        "Se manca uno dei dettagli richiesti, dillo esplicitamente senza inventarlo."
    ),
}
_PROFILE_USR_SUFFIX = {
    _MEMORY_SUBJECT_USER: (
        "\n\n[VINCOLO PROFILO: la domanda riguarda l'interlocutore. "
        "Rispondi in seconda persona. Usa solo i fatti presenti nella memoria factual. "
        "Se un dettaglio richiesto non e' presente chiaramente, dillo brevemente e non inventarlo.]"
    ),
    _MEMORY_SUBJECT_AVATAR: (
        "\n\n[VINCOLO PROFILO: la domanda riguarda te. "
        "Rispondi in prima persona. Usa solo i fatti presenti nella memoria factual. "
        "Se un dettaglio richiesto non e' presente chiaramente, dillo brevemente e non inventarlo.]"
    ),
}

def _rewrite_answer_with_guardrails(
    query: str,
    recent_conversation: str,
    factual_context: str,
    original_answer: str,
    mode: str,
) -> Optional[str]:
    system, style_rule, timeout, num_predict, factual_chars = _REWRITE_CONFIGS.get(
        mode, _REWRITE_CONFIGS["neutral"]
    )
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
        return _finalize_chat_answer(rewritten) or None
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

def _route_chat_intent(query: str, recent_conversation: str, has_memory: bool) -> QueryPlan:
    plan = _build_query_plan(query)
    if not plan.cleaned_query:
        return plan
    if plan.explicit_recap or _RECAP_QUERY_RE.search(plan.cleaned_query):
        return plan
    # Segnali forti dal plan: non rischiare che il router LLM li degradi a chitchat
    if plan.profile_query or plan.document_query or plan.definition_query:
        return plan

    router_system = (
        "Classifica l'intento del messaggio utente in UNA sola etichetta tra: "
        "chitchat, session_recap, memory_recap, memory_qna, creative_open. "
        "Rispondi SOLO con JSON valido: {\"intent\":\"...\",\"confidence\":0.0}. "
        "Non aggiungere testo fuori dal JSON."
    )
    router_user = (
        f"HAS_MEMORY: {str(bool(has_memory)).lower()}\n"
        f"RECENT_CONVERSATION:\n{_truncate_for_prompt(recent_conversation, 1200)}\n\n"
        f"USER_MESSAGE:\n{_truncate_for_prompt(plan.cleaned_query, 600)}\n\n"
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
            confidence = 0.0
            try:
                confidence = float(obj.get("confidence", 0.0))
            except Exception:
                confidence = 0.0
            if intent in _ALLOWED_CHAT_INTENTS and confidence >= max(0.0, min(1.0, RAG_INTENT_CONFIDENCE_MIN)):
                return _build_query_plan(plan.cleaned_query, intent)
    except Exception:
        pass

    return plan

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

_AVATAR_CLIENTS: dict[tuple[str, str], ChromaClientAPI] = {}
_AVATAR_LOCK = threading.Lock()
_LOG_WRITE_LOCK = threading.Lock()
_ALLOWED_LOG_INPUT_MODES = {"voice", "keyboard"}
_SESSION_HISTORY_LOCK = threading.Lock()
_SESSION_HISTORIES: dict[tuple[str, str, str], deque[tuple[str, str]]] = {}

def _safe_avatar_key(avatar_id: str) -> str:
    s = (avatar_id or "default").strip()
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

def _mode_key(empirical_test_mode: bool) -> str:
    return "empirical" if empirical_test_mode else "default"

def _storage_roots(empirical_test_mode: bool) -> tuple[str, str]:
    if empirical_test_mode:
        return EMPIRICAL_PERSIST_ROOT, EMPIRICAL_RAG_LOG_DIR
    return PERSIST_ROOT, RAG_LOG_DIR

def _effective_session_turns() -> int:
    return max(1, min(20, int(RAG_SESSION_TURNS)))

def _session_history_key(avatar_id: str, session_id: Optional[str], empirical_test_mode: bool = False) -> Optional[tuple[str, str, str]]:
    raw_session = (session_id or "").strip()
    if not raw_session:
        return None
    return (_mode_key(empirical_test_mode), _safe_avatar_key(avatar_id), _safe_session_key(raw_session))

def _ensure_session_history(avatar_id: str, session_id: Optional[str], empirical_test_mode: bool = False) -> Optional[str]:
    key = _session_history_key(avatar_id, session_id, empirical_test_mode)
    if key is None:
        return None

    with _SESSION_HISTORY_LOCK:
        hist = _SESSION_HISTORIES.get(key)
        if hist is None:
            _SESSION_HISTORIES[key] = deque(maxlen=_effective_session_turns())
        elif hist.maxlen != _effective_session_turns():
            _SESSION_HISTORIES[key] = deque(hist, maxlen=_effective_session_turns())
    return key[2]

def _reset_session_history(avatar_id: str, session_id: Optional[str], empirical_test_mode: bool = False) -> Optional[str]:
    key = _session_history_key(avatar_id, session_id, empirical_test_mode)
    if key is None:
        return None
    with _SESSION_HISTORY_LOCK:
        _SESSION_HISTORIES[key] = deque(maxlen=_effective_session_turns())
    return key[2]

def _reset_all_session_histories_for_avatar(avatar_id: str, empirical_test_mode: bool = False) -> None:
    avatar_key = _safe_avatar_key(avatar_id)
    mode = _mode_key(empirical_test_mode)
    with _SESSION_HISTORY_LOCK:
        keys_to_remove = [key for key in _SESSION_HISTORIES.keys() if key[0] == mode and key[1] == avatar_key]
        for key in keys_to_remove:
            _SESSION_HISTORIES.pop(key, None)

def _append_session_turn(
    avatar_id: str,
    session_id: Optional[str],
    user_text: str,
    assistant_text: str,
    empirical_test_mode: bool = False,
) -> None:
    key = _session_history_key(avatar_id, session_id, empirical_test_mode)
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

def _build_recent_conversation_context(avatar_id: str, session_id: Optional[str], empirical_test_mode: bool = False) -> str:
    key = _session_history_key(avatar_id, session_id, empirical_test_mode)
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

def _session_log_file_path(avatar_id: str, session_id: str, empirical_test_mode: bool = False) -> tuple[str, str, str]:
    _, log_root = _storage_roots(empirical_test_mode)
    safe_avatar = _safe_avatar_key(avatar_id)
    safe_session = _safe_session_key(session_id)
    avatar_dir = os.path.join(log_root, safe_avatar)
    return os.path.join(avatar_dir, f"{safe_session}.log"), safe_avatar, safe_session

def _clear_avatar_logs(avatar_id: str, empirical_test_mode: bool = False) -> tuple[bool, Optional[str], str]:
    _, log_root = _storage_roots(empirical_test_mode)
    avatar_log_dir = os.path.join(log_root, _safe_avatar_key(avatar_id))
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

def _start_conversation_log_session(
    avatar_id: str,
    session_id: Optional[str] = None,
    empirical_test_mode: bool = False,
) -> tuple[str, str]:
    if not (avatar_id or "").strip():
        raise ValueError("avatar_id is required")

    safe_session = _safe_session_key(session_id or _new_conversation_session_id())
    log_path, _, safe_session = _session_log_file_path(avatar_id, safe_session, empirical_test_mode)
    return safe_session, log_path

def _append_conversation_log_turn(
    avatar_id: str,
    session_id: Optional[str],
    input_mode: Optional[str],
    user_text: str,
    rag_text: str,
    empirical_test_mode: bool = False,
) -> tuple[str, str]:
    if not (avatar_id or "").strip():
        raise ValueError("avatar_id is required")

    safe_session = _safe_session_key(session_id or _new_conversation_session_id())
    now_local = datetime.now().astimezone()
    log_path, safe_avatar, safe_session = _session_log_file_path(avatar_id, safe_session, empirical_test_mode)
    mode = _normalize_input_mode(input_mode)
    user_block = (user_text or "").strip() or "(vuoto)"
    rag_block = (rag_text or "").strip() or "(vuoto)"

    with _LOG_WRITE_LOCK:
        os.makedirs(os.path.dirname(log_path), exist_ok=True)
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

def _get_client_for_avatar(avatar_id: str, empirical_test_mode: bool = False) -> ChromaClientAPI:
    safe_avatar = _safe_avatar_key(avatar_id)
    key = (_mode_key(empirical_test_mode), safe_avatar)
    persist_root, _ = _storage_roots(empirical_test_mode)
    with _AVATAR_LOCK:
        c = _AVATAR_CLIENTS.get(key)
        if c is not None:
            return c
        d = os.path.join(persist_root, safe_avatar)
        os.makedirs(d, exist_ok=True)
        try:
            c = chromadb.PersistentClient(path=d)
        except Exception as e:
            raise RuntimeError(
                f"Impossibile aprire il database Chroma in '{d}'. "
                f"Controlla permessi/percorso. Errore: {e}"
            )
        _AVATAR_CLIENTS[key] = c
        return c

def get_collection(avatar_id: str, empirical_test_mode: bool = False):
    client = _get_client_for_avatar(avatar_id, empirical_test_mode)
    return client.get_or_create_collection(name="memory")

def _avatar_persist_dir(avatar_id: str, empirical_test_mode: bool = False) -> str:
    persist_root, _ = _storage_roots(empirical_test_mode)
    return os.path.join(persist_root, _safe_avatar_key(avatar_id))

def _avatar_memory_snapshot_dir(avatar_id: str, empirical_test_mode: bool = False) -> str:
    persist_root, _ = _storage_roots(empirical_test_mode)
    return os.path.join(persist_root, "_snapshots", _safe_avatar_key(avatar_id))

def _release_avatar_client_handles(avatar_id: str, empirical_test_mode: bool = False) -> str:
    avatar_key = _safe_avatar_key(avatar_id)
    avatar_dir = _avatar_persist_dir(avatar_id, empirical_test_mode)
    client = _AVATAR_CLIENTS.pop((_mode_key(empirical_test_mode), avatar_key), None)
    try:
        if client is None and os.path.isdir(avatar_dir):
            client = chromadb.PersistentClient(path=avatar_dir)
    except Exception:
        client = None

    _stop_chroma_system(client)
    del client
    gc.collect()
    return avatar_dir

def _stop_chroma_system(client) -> None:
    """Prova a stoppare il 'system' condiviso di Chroma per liberare lock su Windows."""
    if client is None:
        return

    try:
        sys = getattr(client, "_system", None)
        if sys and hasattr(sys, "stop"):
            sys.stop()
    except Exception:
        pass

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

    tcmd = TESSERACT_CMD or DEFAULT_TESSERACT_CMD
    if tcmd and os.path.exists(tcmd):
        pytesseract.pytesseract.tesseract_cmd = tcmd

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

def extract_text_from_pdf(pdf_bytes: bytes) -> List[Tuple[str, dict]]:
    """Estrae testo da PDF tramite pymupdf4llm (Markdown strutturato).

    - PDF nativi: estrazione diretta via pymupdf4llm
    - PDF scannerizzati: se pymupdf4llm non estrae testo, fallback OCR via Tesseract
    """
    if not _pymupdf4llm_available:
        raise HTTPException(status_code=500, detail="PDF parsing non disponibile: installa 'pymupdf4llm'.")
    assert pymupdf is not None and pymupdf4llm is not None

    ocr_ready = _ensure_ocr_ready()

    # Apri Document da bytes (pymupdf4llm non accetta bytes direttamente)
    doc = pymupdf.open(stream=pdf_bytes, filetype="pdf")

    # Estrai testo via pymupdf4llm
    pages: Any = []
    try:
        pages = pymupdf4llm.to_markdown(doc, page_chunks=True)
    except Exception as exc:
        doc.close()
        raise HTTPException(status_code=500, detail=f"Errore estrazione PDF: {exc}")

    sections: List[Tuple[str, dict]] = []
    empty_page_indices: list[int] = []
    total_pages = len(pages)

    for i, page_data in enumerate(pages):
        raw_text = page_data.get("text", "") if isinstance(page_data, dict) else str(page_data)
        text = clean_text(raw_text)
        if text and len(text.strip()) >= 5 and not looks_like_garbage(text):
            meta_info = page_data.get("metadata", {}) if isinstance(page_data, dict) else {}
            page_num = meta_info.get("page", i + 1) if isinstance(meta_info, dict) else (i + 1)
            meta = {
                "page": page_num,
                "method": "pymupdf4llm",
                "ocr": False,
                "text_length": len(text),
            }
            sections.append((text, meta))
        else:
            empty_page_indices.append(i)

    # Fallback OCR per pagine vuote (PDF scannerizzati): rasterizza + pytesseract
    if empty_page_indices and ocr_ready and pymupdf is not None:
        assert pytesseract is not None
        assert Image is not None
        try:
            for idx in empty_page_indices:
                if idx < doc.page_count:
                    page = doc[idx]
                    pix = page.get_pixmap(dpi=OCR_DPI)
                    img_bytes_png = pix.tobytes("png")
                    ocr_text = ocr_image_bytes(img_bytes_png)
                    if ocr_text and len(ocr_text.strip()) >= 5:
                        meta = {
                            "page": idx + 1,
                            "method": "ocr_fallback",
                            "ocr": True,
                            "text_length": len(ocr_text),
                        }
                        sections.append((ocr_text, meta))
        except Exception:
            pass  # fallback OCR best-effort, non blocca

    doc.close()

    if not sections:
        raise HTTPException(
            status_code=400,
            detail=f"Nessun testo estratto dal PDF ({total_pages} pagine). Contenuto non leggibile."
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

        # Taglia al confine naturale solo se c'è più testo oltre la finestra
        if end < n:
            window = text[start:end]
            cut = max(window.rfind("\n\n"), window.rfind(". "), window.rfind("\n"))
            if cut > 200:
                end = start + cut + (2 if window[cut : cut + 2] == ". " else 0)

        chunk = clean_text(text[start:end])
        if chunk and len(chunk) >= MIN_CHUNK_CHARS and not looks_like_garbage(chunk):
            chunks.append(chunk)

        if end >= n:
            break
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

def _hybrid_search_ranked(
    query: str,
    query_embedding: List[float],
    col,
    top_k: int = 20,
    bm25_weight: float = 0.6,
) -> List[Tuple[float, str, dict]]:
    """Ricerca ibrida BM25+vector → lista ranked (score, doc, meta)."""
    try:
        candidate_k = min(top_k * 3, 100)
        res = col.query(
            query_embeddings=[query_embedding],
            n_results=candidate_k,
            include=["documents", "metadatas", "distances"],
        )
        vec_docs = (res.get("documents") or [[]])[0]
        vec_metas = (res.get("metadatas") or [[]])[0]
        vec_distances = (res.get("distances") or [[]])[0]

        candidates: list[tuple[str, dict, float | None]] = []
        for d, m, dist in zip(vec_docs, vec_metas, vec_distances):
            if not isinstance(d, str) or not d.strip():
                continue
            candidates.append((d, m or {}, dist if isinstance(dist, (int, float)) else None))
        if not candidates:
            return []

        tokenized_docs = [d.lower().split() for d, _, _ in candidates]
        bm25 = BM25Okapi(tokenized_docs)
        bm25_scores = bm25.get_scores(query.lower().split())
        max_bm25 = max(bm25_scores) if max(bm25_scores) > 0 else 1
        bm25_norm = [s / max_bm25 for s in bm25_scores]

        vec_scores_raw = [max(0.0, (1 - dist)) if dist is not None else 0.0 for _, _, dist in candidates]
        max_vec = max(vec_scores_raw) if max(vec_scores_raw) > 0 else 1
        vec_norm = [s / max_vec for s in vec_scores_raw]

        ranked: list[tuple[float, str, dict]] = []
        for i, (doc, meta, _) in enumerate(candidates):
            score = (bm25_weight * bm25_norm[i]) + ((1 - bm25_weight) * vec_norm[i])
            safe_meta = dict(meta or {})
            safe_meta["_hybrid_score"] = round(float(score), 6)
            safe_meta["_vector_similarity"] = round(float(vec_scores_raw[i]), 6)
            safe_meta["_bm25_norm"] = round(float(bm25_norm[i]), 6)
            ranked.append((float(score), doc, safe_meta))
        ranked.sort(key=lambda x: x[0], reverse=True)
        return ranked[:top_k]

    except Exception:
        try:
            res = col.query(
                query_embeddings=[query_embedding],
                n_results=top_k,
                include=["documents", "metadatas", "distances"],
            )
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
                safe_meta["_hybrid_score"] = round(vec_sim, 6)
                safe_meta["_vector_similarity"] = round(vec_sim, 6)
                ranked.append((vec_sim, doc, safe_meta))
            return ranked
        except Exception:
            return []

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
    profile_target: Optional[str] = None,
) -> Tuple[List[str], List[dict]]:
    if not ranked_hits or top_k <= 0:
        return [], []

    factual_score_min = max(0.0, min(1.0, float(RAG_FACTUAL_SCORE_MIN)))
    factual_score_gap = max(0.0, float(RAG_FACTUAL_SCORE_GAP_MIN))
    best_score = ranked_hits[0][0]
    query_token_count = len(_token_set(query))
    short_query = query_token_count <= 2
    has_reference_tokens = bool(_reference_content_tokens(query))
    selected: list[tuple[str, dict]] = []

    for score, doc, meta in ranked_hits:
        if len(selected) >= top_k:
            break

        subject = _effective_memory_subject(doc, meta)
        if profile_target and not _profile_subject_matches_target(subject, profile_target):
            continue

        lexical = _lexical_overlap_ratio(query, doc)
        vector_similarity = 0.0
        try:
            vector_similarity = float((meta or {}).get("_vector_similarity", 0.0))
        except Exception:
            vector_similarity = 0.0

        focused_source_match = bool((meta or {}).get("_focused_source_match"))
        profile_source_match = bool((meta or {}).get("_profile_source_match"))
        definition_profile_match = bool((meta or {}).get("_definition_profile_match"))
        external_reference_probe = bool((meta or {}).get("_external_reference_probe"))
        source_type = _src_type(meta)
        external_source = source_type in _EXTERNAL_MEMORY_SOURCE_TYPES
        reference_evidence = _has_reference_evidence(
            query,
            doc,
            meta,
            allow_lexical_only=(source_type == "image_description"),
        )
        score_ok = score >= factual_score_min
        near_best = (best_score - score) <= factual_score_gap
        lexical_strong = lexical >= 0.18 and score >= max(0.0, factual_score_min - 0.06)
        semantic_strong = vector_similarity >= 0.55 and score >= max(0.0, factual_score_min - 0.03)
        focused_ok = focused_source_match and score >= max(0.20, factual_score_min - 0.12)
        profile_ok = profile_source_match and score >= max(0.22, factual_score_min - 0.12)
        definition_profile_ok = definition_profile_match and score >= max(0.22, factual_score_min - 0.12)
        external_probe_ok = external_reference_probe and (
            bool((meta or {}).get("_reference_overlap", 0.0) >= 0.34)
            or bool((meta or {}).get("_reference_content_overlap", 0.0) >= 0.34)
            or bool((meta or {}).get("_filename_overlap_boost", 0.0) >= 0.06)
        )

        if short_query:
            keep = lexical >= 0.12 or semantic_strong or focused_ok or profile_ok or definition_profile_ok or external_probe_ok
        else:
            keep = lexical_strong or semantic_strong or focused_ok or profile_ok or definition_profile_ok or external_probe_ok or (score_ok and near_best and lexical >= 0.08)

        if keep and has_reference_tokens and external_source and not (focused_ok or external_probe_ok) and not reference_evidence:
            keep = False

        if not keep:
            continue

        safe_meta = _annotate_memory_subject(meta, doc)
        safe_meta["_hybrid_score"] = round(float(score), 4)
        safe_meta["_lexical_overlap"] = round(float(lexical), 4)
        safe_meta["_vector_similarity"] = round(float(vector_similarity), 4)
        selected.append((doc, safe_meta))

    if not selected:
        top_score, top_doc, top_meta = ranked_hits[0]
        top_subject = _effective_memory_subject(top_doc, top_meta)
        profile_fallback_min = max(0.24, factual_score_min - 0.08)
        if (
            bool((top_meta or {}).get("_profile_source_match"))
            and top_score >= profile_fallback_min
            and (not profile_target or _profile_subject_matches_target(top_subject, profile_target))
            and (top_subject != _MEMORY_SUBJECT_AMBIGUOUS or top_score >= factual_score_min)
        ):
            safe_meta = _annotate_memory_subject(top_meta, top_doc)
            safe_meta["_hybrid_score"] = round(float(top_score), 4)
            safe_meta["_fallback_profile_query"] = True
            return [top_doc], [safe_meta]

        if short_query:
            fallback_min = max(0.12, factual_score_min - 0.18)
            if top_score >= fallback_min and (not profile_target or _profile_subject_matches_target(top_subject, profile_target)):
                safe_meta = _annotate_memory_subject(top_meta, top_doc)
                safe_meta["_hybrid_score"] = round(float(top_score), 4)
                safe_meta["_fallback_short_query"] = True
                return [top_doc], [safe_meta]
        return [], []

    return [d for d, _ in selected], [m for _, m in selected]

def _select_memory_recap_hits(
    ranked_hits: List[Tuple[float, str, dict]],
    top_k: int,
    required_source_types: Sequence[str] = (),
) -> Tuple[List[str], List[dict]]:
    if not ranked_hits or top_k <= 0:
        return [], []

    min_score = max(0.15, min(0.95, float(RAG_FACTUAL_SCORE_MIN) - 0.12))
    candidates: list[tuple[float, int, str, dict, str]] = []
    source_priority = {
        "manual": 0,
        "manual_note": 1,
        "file": 2,
        "image_description": 3,
        "image_ocr": 4,
        "auto_remember_voice": 5,
    }
    for score, doc, meta in ranked_hits:
        if score < min_score:
            continue
        safe_meta = _annotate_memory_subject(meta, doc)
        source_type = _src_type(safe_meta)
        score_floor = min_score + (0.04 if source_type == "auto_remember_voice" else 0.0)
        if score < score_floor:
            continue
        safe_meta["_hybrid_score"] = round(float(score), 4)
        subject = str(safe_meta.get("memory_subject") or "")
        bucket = f"{source_type}:{subject}" if source_type in _PROFILE_MEMORY_SOURCE_TYPES else source_type
        candidates.append((float(score), source_priority.get(source_type, 99), doc, safe_meta, bucket))

    if not candidates:
        return [], []

    selected_docs: List[str] = []
    selected_metas: List[dict] = []
    used_buckets: set[str] = set()
    used_keys: set[str] = set()

    normalized_required = [clean_text(source_type or "").lower() for source_type in required_source_types if clean_text(source_type or "")]
    for required_source in normalized_required:
        best_match: Optional[tuple[float, int, str, dict, str]] = None
        for candidate in candidates:
            score, priority, doc, safe_meta, bucket = candidate
            source_type = _src_type(safe_meta)
            family = "manual" if source_type in _PROFILE_MEMORY_SOURCE_TYPES else source_type
            doc_key = clean_text(doc or "").lower()
            if not doc_key or doc_key in used_keys or family != required_source:
                continue
            if best_match is None or score > best_match[0]:
                best_match = candidate
        if best_match is None:
            continue
        _, _, doc, safe_meta, bucket = best_match
        doc_key = clean_text(doc or "").lower()
        selected_docs.append(doc)
        selected_metas.append(safe_meta)
        used_buckets.add(bucket)
        used_keys.add(doc_key)
        if len(selected_docs) >= top_k:
            return _dedupe_chunks(selected_docs, selected_metas)

    for score, priority, doc, safe_meta, bucket in sorted(candidates, key=lambda x: (-x[0], x[1])):
        if len(selected_docs) >= top_k:
            break
        doc_key = clean_text(doc or "").lower()
        if not doc_key or bucket in used_buckets or doc_key in used_keys:
            continue
        selected_docs.append(doc)
        selected_metas.append(safe_meta)
        used_buckets.add(bucket)
        used_keys.add(doc_key)

    for score, priority, doc, safe_meta, bucket in sorted(candidates, key=lambda x: (x[1], -x[0])):
        if len(selected_docs) >= top_k:
            break
        doc_key = clean_text(doc or "").lower()
        if not doc_key or doc_key in used_keys:
            continue
        selected_docs.append(doc)
        selected_metas.append(safe_meta)
        used_keys.add(doc_key)

    if not selected_docs:
        return [], []

    return _dedupe_chunks(selected_docs, selected_metas)

def _select_multi_facet_hits(
    ranked_hits: List[Tuple[float, str, dict]],
    facets: list[QueryFacet],
    top_k: int,
) -> Tuple[List[str], List[dict]]:
    """Select hits ensuring at least one per requested facet when possible."""
    if not ranked_hits or not facets or top_k <= 0:
        return [], []

    selected_docs: list[str] = []
    selected_metas: list[dict] = []
    used_keys: set[str] = set()

    def _hit_matches_facet(doc: str, meta: dict, facet: QueryFacet) -> bool:
        source_type = _src_type(meta)
        family_ok = facet.preferred_family == "any"
        if not family_ok:
            if facet.preferred_family == "manual":
                family_ok = source_type in _PROFILE_MEMORY_SOURCE_TYPES
            else:
                family_ok = source_type == facet.preferred_family
        if not family_ok:
            return False
        doc_tokens = _token_set(doc)
        filename = clean_text(str((meta or {}).get("source_filename") or "")).lower()
        if filename:
            doc_tokens |= _token_set(filename)
        anchor_hits = sum(1 for t in facet.anchor_terms if t in doc_tokens)
        return anchor_hits >= 1

    for facet in facets:
        best: Optional[Tuple[float, str, dict]] = None
        for score, doc, meta in ranked_hits:
            doc_key = clean_text(doc or "")[:240].lower()
            if doc_key in used_keys:
                continue
            if _hit_matches_facet(doc, meta, facet):
                if best is None or score > best[0]:
                    best = (score, doc, meta)
        if best is not None:
            _, doc, meta = best
            doc_key = clean_text(doc or "")[:240].lower()
            selected_docs.append(doc)
            selected_metas.append(meta)
            used_keys.add(doc_key)
            if len(selected_docs) >= top_k:
                return _dedupe_chunks(selected_docs, selected_metas)

    for score, doc, meta in ranked_hits:
        if len(selected_docs) >= top_k:
            break
        doc_key = clean_text(doc or "")[:240].lower()
        if doc_key in used_keys:
            continue
        selected_docs.append(doc)
        selected_metas.append(meta)
        used_keys.add(doc_key)

    if not selected_docs:
        return [], []
    return _dedupe_chunks(selected_docs, selected_metas)

def _build_structured_recap_context(
    docs: List[str],
    metas: List[dict],
    facets: list[QueryFacet],
    max_chars: int,
) -> str:
    """Build context grouped by facet/source for structured recap generation."""
    if not docs:
        return ""

    groups: dict[str, list[Tuple[str, dict]]] = {}
    for doc, meta in zip(docs, metas):
        source_type = _src_type(meta)
        if source_type in _PROFILE_MEMORY_SOURCE_TYPES:
            group_key = "profilo"
        elif source_type == "image_description":
            group_key = "immagine"
        elif source_type == "file":
            group_key = "documento"
        else:
            group_key = "altro"
        groups.setdefault(group_key, []).append((doc, meta))

    label_map = {
        "profilo": "PROFILO/IDENTITA",
        "immagine": "CONTENUTO VISIVO",
        "documento": "CONTENUTO DOCUMENTALE",
        "altro": "ALTRO",
    }
    parts: list[str] = []
    total = 0
    for group_key in ["profilo", "documento", "immagine", "altro"]:
        items = groups.get(group_key, [])
        if not items:
            continue
        label = label_map.get(group_key, group_key.upper())
        section_header = f"--- {label} ---"
        if total + len(section_header) + 4 > max_chars:
            break
        parts.append(section_header)
        total += len(section_header) + 2
        for doc, meta in items:
            safe_meta = _annotate_memory_subject(meta, doc)
            src = safe_meta.get("source_filename") or safe_meta.get("source_type") or "memoria"
            piece = f"[{src}] {doc}"
            if total + len(piece) > max_chars:
                break
            parts.append(piece)
            total += len(piece) + 2

    return "\n\n".join(parts)

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
    os.makedirs(EMPIRICAL_PERSIST_ROOT, exist_ok=True)
    os.makedirs(EMPIRICAL_RAG_LOG_DIR, exist_ok=True)
    try:
        _run_startup_warmup()
    except Exception as exc:
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
    empirical_test_mode: bool = False

class RecallReq(BaseModel):
    avatar_id: str
    query: str
    top_k: int = 20
    empirical_test_mode: bool = False

class ChatReq(BaseModel):
    avatar_id: str
    user_text: str
    top_k: int = 20
    system: Optional[str] = None
    session_id: Optional[str] = None
    input_mode: Optional[str] = None
    log_conversation: bool = False
    empirical_test_mode: bool = False

class ChatSessionStartReq(BaseModel):
    avatar_id: str
    empirical_test_mode: bool = False

# Pattern che catturano frasi in cui l'utente chiede di memorizzare qualcosa.
# Supporta italiano e inglese. Il confronto non distingue maiuscole/minuscole.
_REMEMBER_PATTERNS = [
    r"(?:ricorda(?:ti)?|memorizza|tieni a mente|segna(?:ti)?|annota(?:ti)?|non (?:ti )?dimenticare)\s+(?:che\s+)?(.+)",
    r"(?:devi|dovresti)\s+(?:ricorda(?:re|rti)|memorizzare|sapere)\s+(?:che\s+)?(.+)",
    r"(?:sappi|tieni presente)\s+(?:che\s+)?(.+)",
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
            txt = clean_text(original_text)
        if len(txt) < MIN_CHUNK_CHARS or looks_like_garbage(txt):
            return None

        emb = ollama_embed_many([txt])[0]
        _id = str(uuid.uuid4())
        meta: dict[str, Any] = {
            "source_type": "auto_remember_voice",
            "memory_role": _memory_role_for_text(txt),
            "memory_subject": _infer_memory_subject(
                txt,
                {"source_type": "auto_remember_voice", "original_utterance": original_text},
                default_subject=_MEMORY_SUBJECT_AMBIGUOUS,
                original_utterance=original_text,
            ),
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
        safe_meta = _annotate_memory_subject(m, d)
        src = safe_meta.get("source_filename") or safe_meta.get("source_type") or "memoria"
        page = safe_meta.get("page")
        tag_parts = [f"{src}{' p.' + str(page) if page else ''}"]
        if safe_meta.get("source_type") in _PROFILE_MEMORY_SOURCE_TYPES:
            subject = str(safe_meta.get("memory_subject") or "")
            if subject == _MEMORY_SUBJECT_AVATAR:
                tag_parts.append("soggetto: avatar")
            elif subject == _MEMORY_SUBJECT_USER:
                tag_parts.append("soggetto: utente")
            elif subject == _MEMORY_SUBJECT_AMBIGUOUS:
                tag_parts.append("soggetto: ambiguo")
        tag = f"[{' | '.join(tag_parts)}]"
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
        annotated_meta = _annotate_memory_subject(m, d)
        safe_meta = {
            k: v for k, v in annotated_meta.items()
            if not str(k).startswith("_")
        }
        rag_used.append({"text": d, "meta": safe_meta})
    return rag_used

def _focused_source_retrieval(
    col: Any, query: str, qemb: List[float],
    sources: tuple[str, ...] | list[str], top_k_cap: int, score_floor: float = 0.18,
) -> list[tuple[float, str, dict]]:
    """Boosted vector search per source type — dedup di memory_qna/memory_recap."""
    focused: list[tuple[float, str, dict]] = []
    for source_type in sources:
        ranked = _vector_search_ranked(
            col=col, query_embedding=qemb,
            top_k=max(2, min(top_k_cap, 8)),
            where={"source_type": source_type},
        )
        for score, doc, meta in ranked:
            lexical = _lexical_overlap_ratio(query, doc)
            boosted = min(1.0, max(score, score_floor) + 0.16 + min(0.08, lexical) + _filename_overlap_boost(query, meta or {}))
            safe_meta = dict(meta or {})
            safe_meta["_hybrid_score"] = round(float(boosted), 6)
            safe_meta["_focused_source_match"] = True
            focused.append((boosted, doc, safe_meta))
    return _rerank_reference_hits(query=query, ranked_hits=focused, strict_external=False)

def _retrieve_context_for_intent(
    col: Any,
    query: str,
    intent: str,
    requested_top_k: int,
    *,
    plan: Optional[QueryPlan] = None,
) -> tuple[List[str], List[dict]]:
    query_plan = plan or _build_query_plan(query, intent)
    effective_intent = query_plan.normalized_intent
    factual_top_k = min(max(0, int(requested_top_k)), max(1, int(RAG_CHAT_TOP_K_CAP)))
    if factual_top_k <= 0:
        return [], []
    if effective_intent in {"chitchat", "session_recap", "creative_open"}:
        return [], []

    try:
        if effective_intent == "memory_qna":
            document_query = query_plan.document_query
            profile_query = query_plan.profile_query
            focus_sources = query_plan.focus_sources
            memory_reference = query_plan.memory_reference
            reference_tokens = _reference_content_tokens(query or "")
            definition_query = query_plan.definition_query
            profile_target = query_plan.profile_target if profile_query else _MEMORY_SUBJECT_AMBIGUOUS
            qemb = _embed_one_or_http_500(query)
            visual_memory_query = query_plan.visual_query
            profile_boosted: list[tuple[float, str, dict]] = []
            if focus_sources and not reference_tokens:
                focused_ranked = _focused_source_retrieval(col, query, qemb, focus_sources, factual_top_k, score_floor=0.18)
                focused_docs, focused_metas = _select_factual_hits(
                    ranked_hits=focused_ranked, query=query,
                    top_k=1 if focus_sources == ("image_description",) else max(1, min(factual_top_k, 4)),
                )
                if focused_docs:
                    return _dedupe_chunks(focused_docs, focused_metas)
                if visual_memory_query:
                    return [], []

            if profile_query:
                profile_boosted = _retrieve_profile_boosted_hits(
                    col=col,
                    query=query,
                    query_embedding=qemb,
                    top_k=factual_top_k,
                    profile_target=profile_target,
                )

            if not (document_query or profile_query or memory_reference or definition_query):
                return [], []

            if definition_query and not document_query and not profile_query:
                definition_docs, definition_metas = _retrieve_definition_context(
                    col=col,
                    query=query,
                    query_embedding=qemb,
                    top_k=factual_top_k,
                )
                if definition_docs:
                    return definition_docs, definition_metas

            ranked = _hybrid_search_ranked(
                query=query,
                query_embedding=qemb,
                col=col,
                top_k=max(2, min(factual_top_k, 6)) if (memory_reference or definition_query) else factual_top_k,
                bm25_weight=0.6,
            )
            if reference_tokens and not profile_query:
                source_probe_ranked = _rank_source_probe_hits(
                    col=col,
                    query=query,
                    query_embedding=qemb,
                    top_k=max(2, min(factual_top_k, 6)),
                )
                if source_probe_ranked:
                    ranked = sorted(source_probe_ranked + ranked, key=lambda x: x[0], reverse=True)
                ranked = _rerank_reference_hits(
                    query=query,
                    ranked_hits=ranked,
                    strict_external=bool(document_query or memory_reference or definition_query),
                )
            if profile_query:
                ranked = _filter_ranked_hits_for_profile_target(
                    ranked_hits=ranked,
                    profile_target=profile_target,
                )
            if profile_boosted:
                ranked = sorted(profile_boosted + ranked, key=lambda x: x[0], reverse=True)
            docs, metas = _select_factual_hits(
                ranked_hits=ranked,
                query=query,
                top_k=factual_top_k,
                profile_target=profile_target if profile_query else None,
            )
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
                    top_reference_ok = _has_reference_evidence(
                        query,
                        top_doc,
                        top_meta,
                        allow_lexical_only=(_src_type(top_meta) == "image_description"),
                    )
                    if (not reference_tokens and (top_score >= 0.22 or top_lexical >= 0.10)) or (reference_tokens and top_reference_ok):
                        safe_meta = dict(top_meta or {})
                        safe_meta["_hybrid_score"] = round(float(top_score), 4)
                        safe_meta["_fallback_document_probe"] = True
                        return [top_doc], [safe_meta]

            if memory_reference:
                fallback_top_k = max(1, min(factual_top_k, 3))
                probe_k = max(2, min(factual_top_k, 6))
                all_mem_sources = list(_PROFILE_MEMORY_SOURCE_TYPES) + ["file", "image_description", "image_ocr"]
                probe_lists: list[list[tuple[float, str, dict]]] = [
                    _rank_source_probe_hits(col=col, query=query, query_embedding=qemb, top_k=probe_k),
                    _rank_external_probe_hits(col=col, query=query, query_embedding=qemb, top_k=probe_k),
                ]
                broad = _vector_search_ranked(col=col, query_embedding=qemb, top_k=probe_k, where={"source_type": {"$in": all_mem_sources}})
                if not document_query and not profile_query:
                    broad = _rerank_reference_hits(query=query, ranked_hits=broad, strict_external=True)
                probe_lists.append(broad)
                for ranked in probe_lists:
                    fb_docs, fb_metas = _select_factual_hits(ranked_hits=ranked, query=query, top_k=fallback_top_k)
                    if fb_docs:
                        return _dedupe_chunks(fb_docs, fb_metas)
            return [], []

        if effective_intent == "memory_recap":
            document_query = query_plan.document_query
            profile_query = query_plan.profile_query
            focus_sources = query_plan.focus_sources
            reference_tokens = _reference_content_tokens(query or "")
            strong_recap_query = query_plan.explicit_recap
            if document_query and not reference_tokens:
                qemb = _embed_one_or_http_500(query)
                focused_sources = focus_sources or ("file", "image_description")
                focused_ranked = _focused_source_retrieval(col, query, qemb, focused_sources, factual_top_k, score_floor=0.16)
                docs, metas = _select_factual_hits(
                    ranked_hits=focused_ranked, query=query,
                    top_k=1 if focus_sources == ("image_description",) else max(2, min(factual_top_k, 6)),
                )
                if docs:
                    return _dedupe_chunks(docs, metas)
                return [], []

            if reference_tokens or strong_recap_query:
                qemb = _embed_one_or_http_500(query)
                recap_sources = list(_PROFILE_MEMORY_SOURCE_TYPES) + list(_EXTERNAL_MEMORY_SOURCE_TYPES)
                ranked = _hybrid_search_ranked(
                    query=query,
                    query_embedding=qemb,
                    col=col,
                    top_k=max(4, min(max(factual_top_k * 2, 6), 12)),
                    bm25_weight=0.62,
                )
                ranked = [
                    (score, doc, meta)
                    for score, doc, meta in ranked
                    if _src_type(meta) in recap_sources
                ]
                if ranked:
                    if profile_query:
                        profile_ranked = _retrieve_profile_boosted_hits(
                            col=col,
                            query=query,
                            query_embedding=qemb,
                            top_k=max(2, min(factual_top_k, 6)),
                            profile_target=query_plan.profile_target,
                        )
                        if profile_ranked:
                            ranked = sorted(profile_ranked + ranked, key=lambda x: x[0], reverse=True)
                    external_ranked = _rank_source_probe_hits(
                        col=col,
                        query=query,
                        query_embedding=qemb,
                        top_k=max(3, min(factual_top_k, 6)),
                    )
                    if external_ranked:
                        ranked = sorted(external_ranked + ranked, key=lambda x: x[0], reverse=True)
                    ranked = _rerank_reference_hits(
                        query=query,
                        ranked_hits=ranked,
                        strict_external=False,
                    )
                    required_source_types: list[str] = []
                    if profile_query:
                        required_source_types.append("manual")
                    if strong_recap_query and reference_tokens:
                        required_source_types.extend(["file", "image_description"])
                    elif focus_sources:
                        for source_type in focus_sources:
                            normalized_source = clean_text(source_type or "").lower()
                            if normalized_source:
                                required_source_types.append(normalized_source)

                    facets = _extract_requested_facets(query, query_plan)
                    if len(facets) >= 2 and query_plan.wants_multi_source_coverage:
                        docs, metas = _select_multi_facet_hits(
                            ranked_hits=ranked,
                            facets=facets,
                            top_k=max(len(facets), min(factual_top_k, 6)),
                        )
                        if docs and len(docs) >= 2:
                            return _dedupe_chunks(docs, metas)

                    docs, metas = _select_memory_recap_hits(
                        ranked_hits=ranked,
                        top_k=max(2, min(factual_top_k, 6)),
                        required_source_types=required_source_types,
                    )
                    if docs:
                        return _dedupe_chunks(docs, metas)

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
                        required_source_types=("manual",),
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
            required_source_types: list[str] = ["manual"] if profile_query else []
            return _select_memory_recap_hits(
                ranked_hits=ranked,
                top_k=max(2, min(factual_top_k, 6)),
                required_source_types=required_source_types,
            )
    except Exception as e:
        raise HTTPException(status_code=500, detail=f"Errore query memoria: {e}")

    return [], []

def _build_chat_system_prompt(base_system: Optional[str], intent: str, has_factual_context: bool) -> str:
    system = base_system or (
        "Sei l'avatar stesso, non un assistente. Parla in prima persona in modo naturale e diretto. "
        "Se la memoria include indicazioni sul tuo carattere o stile, rispettale in modo lieve e naturale. "
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
        system += (
            " Modalita memory_recap: elenca solo ricordi factual verificabili. "
            "Se la richiesta cita piu temi o piu fonti, coprili tutti se supportati, senza fissarti su una sola memoria."
        )
    elif intent == "memory_qna":
        system += (
            " Modalita memory_qna: rispondi usando solo la memoria factual pertinente. "
            "Se il fatto richiesto e' presente nella memoria, riportalo esplicitamente senza negarlo. "
            "Se invece manca, rispondi in modo semplice e naturale senza formule meccaniche ripetute."
        )
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
        "Rispondi in italiano naturale in 3-6 frasi, salvo richiesta esplicita di risposta lunga."
    )
    if intent in {"session_recap", "memory_recap"}:
        user += "\nFormato: fino a 5 punti brevi e verificabili. Copri ogni tema richiesto se supportato dalla memoria factual, senza fermarti al primo."
    if auto_remembered:
        user += "\n\n[SISTEMA: L'utente ha chiesto di ricordare qualcosa e l'informazione e' stata salvata. Conferma brevemente che ricorderai.]"
    return user

def _chat_fast_path(
    intent: str,
    query: str,
    query_plan: QueryPlan,
    factual_docs: List[str],
    factual_metas: List[dict],
    auto_remembered: bool,
) -> Optional[str]:
    if auto_remembered and _is_pure_remember_request(query):
        return _auto_remember_confirmation(query)

    if intent in {"memory_qna", "memory_recap"} and not factual_docs:
        return _memory_unknown_reply(intent, query)

    visual_memory_query = query_plan.visual_query
    if visual_memory_query and factual_docs and not _has_visual_factual_hits(factual_metas):
        return _memory_unknown_reply(intent, query)
    if visual_memory_query and _has_visual_factual_hits(factual_metas):
        total_desc_chars = sum(len(d) for d in factual_docs)
        if total_desc_chars <= 300:
            extractive_visual = _extractive_visual_answer(query, factual_docs)
            if extractive_visual:
                return _finalize_chat_answer(extractive_visual) or extractive_visual
        return None

    definition_answer = _answer_definition_query(
        query=query,
        factual_docs=factual_docs,
        factual_metas=factual_metas,
    )
    if definition_answer:
        return definition_answer
    return None

def _chat_generate(
    req: ChatReq,
    intent: str,
    query: str,
    query_plan: QueryPlan,
    recent_conversation: str,
    factual_docs: List[str],
    factual_metas: List[dict],
    auto_remembered: bool,
) -> tuple[str, str, str, bool, str]:
    factual_max_chars = min(MAX_CONTEXT_CHARS, max(1200, FACTUAL_MAX_CONTEXT_CHARS))

    facets = _extract_requested_facets(query, query_plan)
    if len(facets) >= 2 and query_plan.wants_multi_source_coverage and factual_docs:
        factual_context = _build_structured_recap_context(
            factual_docs, factual_metas, facets, max_chars=factual_max_chars,
        )
    else:
        factual_context = _build_context_from_docs(factual_docs, factual_metas, max_chars=factual_max_chars)
    if not factual_context:
        factual_context = "(Nessuna memoria factual pertinente recuperata.)"

    system = _build_chat_system_prompt(
        base_system=req.system,
        intent=intent,
        has_factual_context=bool(factual_docs),
    )
    if query_plan.document_query and factual_docs:
        system += (
            " La richiesta riguarda contenuti da documenti/immagini: usa i blocchi recuperati "
            "e non negare la presenza di tali fonti. "
            "Resta sul contenuto richiesto, senza premesse su argomenti non richiesti. "
            "Se i blocchi recuperati contengono il termine cercato o il file pertinente, sintetizza i dettagli supportati invece di dire che non ricordi. "
            "Se la richiesta chiede un elenco o una lista, riporta TUTTI gli elementi presenti nei blocchi recuperati senza troncare."
        )

    visual_memory_query = query_plan.visual_query
    if visual_memory_query and _has_visual_factual_hits(factual_metas):
        system += (
            " La richiesta riguarda contenuti visivi: descrivi almeno 2-3 elementi strutturali reali "
            "presenti nei blocchi recuperati (componenti, relazioni, etichette, elementi principali). "
            "Non limitarti al titolo o a una frase generica. "
            "Non inferire colori, identita, proprieta' o oggetti non chiaramente menzionati."
        )

    if query_plan.wants_multi_source_coverage and len(facets) >= 2 and factual_docs:
        facet_labels = ", ".join(f.topic for f in facets[:5])
        system += (
            f" La richiesta e' un riepilogo multi-sorgente. Devi coprire TUTTI i temi richiesti: {facet_labels}. "
            "Per ogni tema, riporta almeno un dettaglio concreto dalla memoria factual. "
            "Non fissarti su una sola sorgente ignorando le altre."
        )

    profile_target = query_plan.profile_target if query_plan.profile_query else _MEMORY_SUBJECT_AMBIGUOUS
    if query_plan.profile_query and factual_docs:
        system += (
            " La richiesta riguarda identita/carattere: resta coerente con i fatti recuperati "
            "e non contraddirli. Se un blocco e' marcato come soggetto: utente, trattalo come "
            "informazione sull'interlocutore e non come autobiografia tua."
        )
        system += _PROFILE_SYS_SUFFIX.get(profile_target, "")

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
    if query_plan.profile_query and factual_docs:
        user += _PROFILE_USR_SUFFIX.get(profile_target, "")

    if query_plan.wants_multi_source_coverage and len(facets) >= 2 and factual_docs:
        generation_override = 400
    elif intent == "memory_recap" and factual_docs:
        generation_override = 340
    elif query_plan.document_query and factual_docs:
        generation_override = 360
    else:
        generation_override = None
    raw_answer = ollama_chat(
        [
            {"role": "system", "content": system},
            {"role": "user", "content": user},
        ],
        num_predict_override=generation_override,
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
            answer = retry_answer
        elif intent in {"memory_qna", "memory_recap"} and not factual_docs:
            answer = _memory_unknown_reply(intent, query)
        else:
            answer = "Parlo in prima persona."

    support_recent = recent_conversation
    if intent in {"memory_qna", "memory_recap"}:
        support_recent = ""
    return answer, factual_context, profile_target, visual_memory_query, support_recent

def _chat_repair(
    intent: str,
    query: str,
    query_plan: QueryPlan,
    recent_conversation: str,
    factual_docs: List[str],
    factual_metas: List[dict],
    answer: str,
    factual_context: str,
    profile_target: str,
    visual_memory_query: bool,
    support_recent: str,
) -> str:
    support = _support_token_set(query, support_recent, factual_docs)

    def _try_rewrite(mode: str) -> Optional[str]:
        return _rewrite_answer_with_guardrails(
            query=query, recent_conversation=recent_conversation,
            factual_context=factual_context, original_answer=answer, mode=mode,
        )

    if intent in {"chitchat", "creative_open"} and _CHITCHAT_FIRST_PERSON_RE.search(answer):
        if _unsupported_token_ratio(answer, support, _CHITCHAT_TOKEN_STOPWORDS) >= 0.62:
            rewritten = _try_rewrite("neutral")
            if rewritten and not _IDENTITY_META_RE.search(rewritten):
                answer = rewritten

    if factual_docs:
        is_memory_intent = intent in {"memory_qna", "memory_recap"}
        need_grounding = is_memory_intent
        if not need_grounding:
            need_grounding = (
                (_answer_denies_available_context(answer) and query_plan.document_query)
                or _unsupported_token_ratio(answer, support, _ALIGNMENT_TOKEN_STOPWORDS) >= 0.66
            )

        if need_grounding:
            if visual_memory_query and _has_visual_factual_hits(factual_metas):
                mode = "visual_grounded_strict"
            elif query_plan.profile_query:
                mode = "profile_user" if profile_target == _MEMORY_SUBJECT_USER else "profile_avatar"
            elif is_memory_intent:
                mode = "grounded"
            else:
                mode = "grounded_strict" if query_plan.document_query else "grounded"

            rewritten = _try_rewrite(mode)
            if rewritten:
                answer = rewritten
                # Un solo passaggio di correzione se ancora allucinato o negante
                if is_memory_intent and (
                    _answer_denies_available_context(answer)
                    or _unsupported_token_ratio(answer, _support_token_set(query, "", factual_docs), _ALIGNMENT_TOKEN_STOPWORDS) >= 0.52
                ):
                    strict_mode = ("profile_user" if profile_target == _MEMORY_SUBJECT_USER else "profile_avatar") if query_plan.profile_query else "grounded_strict"
                    strict_fix = _rewrite_answer_with_guardrails(
                        query=query, recent_conversation=recent_conversation,
                        factual_context=factual_context, original_answer=answer, mode=strict_mode,
                    )
                    if strict_fix and not _answer_denies_available_context(strict_fix):
                        answer = strict_fix

        if is_memory_intent:
            grounding_score = _compute_grounding_score(answer, query, factual_docs)
            contradiction_detected = _detect_grounding_contradiction(answer, query, factual_docs)
            if grounding_score < max(0.0, min(1.0, RAG_GROUNDING_SCORE_MIN)) or contradiction_detected:
                strict_mode = (
                    "profile_user"
                    if query_plan.profile_query and profile_target == _MEMORY_SUBJECT_USER
                    else "profile_avatar"
                    if query_plan.profile_query
                    else "grounded_strict"
                )
                strict_fix = _rewrite_answer_with_guardrails(
                    query=query,
                    recent_conversation=recent_conversation,
                    factual_context=factual_context,
                    original_answer=answer,
                    mode=strict_mode,
                )
                if strict_fix:
                    answer = strict_fix
                    grounding_score = _compute_grounding_score(answer, query, factual_docs)
                    contradiction_detected = _detect_grounding_contradiction(answer, query, factual_docs)
                if grounding_score < max(0.0, min(1.0, RAG_GROUNDING_SCORE_MIN)) or contradiction_detected:
                    answer = _memory_unknown_reply(intent, query)

    if query_plan.profile_query and factual_docs:
        answer = _apply_profile_answer_repairs(
            answer=answer, query=query, recent_conversation=recent_conversation,
            factual_context=factual_context, profile_target=profile_target,
        )

    return answer

_PROPER_NAME_RE = re.compile(
    r"\b([A-ZÀ-ÿ][a-zà-ÿ]{2,15}(?:\s+[A-ZÀ-ÿ][a-zà-ÿ]{2,15}){0,3})\b",
)

def _extract_external_proper_names(
    factual_docs: List[str], factual_metas: List[dict],
) -> set[str]:
    """Extract proper-name candidates that appear only in external (file/image) sources."""
    names: set[str] = set()
    for doc, meta in zip(factual_docs, factual_metas):
        stype = _src_type(meta)
        if stype not in {"file", "image_description", "image_ocr"}:
            continue
        for m in _PROPER_NAME_RE.finditer(doc or ""):
            candidate = m.group(1).strip()
            if len(candidate) < 4:
                continue
            names.add(candidate.lower())
    return names

def _answer_contaminates_identity_from_external(
    answer: str,
    factual_docs: List[str],
    factual_metas: List[dict],
    profile_target: str,
) -> bool:
    """Return True if the answer claims an external-source name as user/avatar identity."""
    if profile_target not in {_MEMORY_SUBJECT_AVATAR, _MEMORY_SUBJECT_USER}:
        return False
    external_names = _extract_external_proper_names(factual_docs, factual_metas)
    if not external_names:
        return False

    manual_names: set[str] = set()
    for doc, meta in zip(factual_docs, factual_metas):
        stype = _src_type(meta)
        if stype in _PROFILE_MEMORY_SOURCE_TYPES:
            for m in _PROPER_NAME_RE.finditer(doc or ""):
                manual_names.add(m.group(1).strip().lower())

    suspect_names = external_names - manual_names
    if not suspect_names:
        return False

    ans_lower = clean_text(answer or "").lower()
    _ID_CLAIM = re.compile(
        r"\b(?:(?:mi|ti|si)\s+chiam[oai]|(?:il\s+(?:tuo|mio|suo)\s+nome\s+(?:e'|è))|(?:sono|sei)\s+)\s*",
        re.IGNORECASE,
    )
    for match in _ID_CLAIM.finditer(ans_lower):
        after = ans_lower[match.end():match.end() + 60]
        for name in suspect_names:
            if name in after:
                return True
    return False

_ENGLISH_FUNCTION_WORDS = frozenset({
    "the", "is", "are", "was", "were", "that", "this", "have", "has", "had",
    "from", "with", "which", "been", "would", "could", "should", "their",
    "about", "into", "some", "also", "than", "other", "these", "those",
    "according", "between", "through", "during", "before", "after",
})

def _looks_english(text: str) -> bool:
    """Return True if the text appears to be primarily in English."""
    words = re.findall(r"[a-zA-Z]+", text.lower())
    if len(words) < 6:
        return False
    en_count = sum(1 for w in words if w in _ENGLISH_FUNCTION_WORDS)
    return en_count / len(words) >= 0.12

_FACET_DENIAL_RE = re.compile(
    r"(?:non\s+(?:so|ricordo|ho|possiedo|dispongo|sono a conoscenza di)\s+(?:nulla|niente|informazioni?|dettagli?|dati)\b)",
    re.IGNORECASE,
)

def _check_facet_coverage(answer: str, facets: list[QueryFacet]) -> list[QueryFacet]:
    """Return list of required facets NOT covered in the answer."""
    if not facets:
        return []
    ans_tokens = _token_set(answer)
    ans_lower = clean_text(answer or "").lower()
    ans_sentences = [s.strip() for s in re.split(r"[.!?]+", ans_lower) if s.strip()]
    missing: list[QueryFacet] = []
    for facet in facets:
        if not facet.required:
            continue
        found = False
        for term in facet.anchor_terms:
            if term not in ans_tokens and term not in ans_lower:
                continue
            denied = False
            for sent in ans_sentences:
                if term in sent and _FACET_DENIAL_RE.search(sent):
                    denied = True
                    break
            if not denied:
                found = True
                break
        if not found:
            missing.append(facet)
    return missing

def _check_visual_emptiness(
    answer: str,
    factual_metas: List[dict],
    visual_memory_query: bool,
) -> bool:
    """Return True if visual context exists but answer is too shallow (<=1 sentence, <50 chars)."""
    if not visual_memory_query:
        return False
    if not _has_visual_factual_hits(factual_metas):
        return False
    sentences = [s.strip() for s in re.split(r"(?<=[.!?])\s+", clean_text(answer or "")) if len(s.strip()) >= 10]
    return len(sentences) <= 1 or len(clean_text(answer or "")) < 50

def _verify_answer_coverage(
    answer: str,
    query: str,
    query_plan: QueryPlan,
    factual_docs: List[str],
    factual_metas: List[dict],
    factual_context: str,
    profile_target: str,
    visual_memory_query: bool,
    recent_conversation: str,
) -> str:
    """Final coherence gate — one targeted retry if the answer fails coverage checks."""
    if not factual_docs:
        return answer

    facets = _extract_requested_facets(query, query_plan)
    retry_needed = False
    retry_hint = ""
    missing: list[QueryFacet] = []

    if len(facets) >= 2 and query_plan.wants_multi_source_coverage:
        missing = _check_facet_coverage(answer, facets)
        if missing:
            missing_labels = ", ".join(f.topic for f in missing[:3])
            retry_needed = True
            retry_hint += (
                f" La risposta non copre: {missing_labels}."
                f" IMPORTANTE: aggiungi almeno 1-2 frasi per CIASCUN argomento mancante"
                f" usando i dati dalla MEMORIA_FACTUAL."
            )

    if profile_target in {_MEMORY_SUBJECT_AVATAR, _MEMORY_SUBJECT_USER}:
        if _answer_contaminates_identity_from_external(answer, factual_docs, factual_metas, profile_target):
            retry_needed = True
            retry_hint += (
                " La risposta attribuisce nomi da documenti esterni come identita dell'utente/avatar."
                " Usa solo i fatti da memoria manuale per le identita."
            )

    if _check_visual_emptiness(answer, factual_metas, visual_memory_query):
        retry_needed = True
        retry_hint += (
            " La risposta visiva e' troppo scarna: aggiungi almeno 2-3 elementi"
            " strutturali dal contesto visivo recuperato."
        )

    distortion_detected = False
    if _answer_distorts_profile_relation(answer, factual_context):
        distortion_detected = True
        retry_needed = True
        retry_hint += (
            " ERRORE CRITICO: la risposta trasforma un'associazione relazionale in identita personale."
            " Se il contesto dice 'chiama X la postazione' o 'indica X il piano',"
            " X e' il NOME della postazione/pianocodice, NON il nome dell'utente."
            " Riformula usando: 'l'utente chiama X la postazione' oppure 'secondo l'utente, X e' la postazione'."
        )

    english_detected = False
    if _looks_english(answer):
        english_detected = True
        retry_needed = True
        retry_hint += " La risposta e' in inglese: riscrivi INTERAMENTE in italiano."

    if not retry_needed:
        return answer

    visual_retry = _check_visual_emptiness(answer, factual_metas, visual_memory_query)
    multi_facet_check = bool(len(facets) >= 2 and query_plan.wants_multi_source_coverage and missing)
    retry_context = factual_context[:2400]
    facet_template = ""
    if multi_facet_check and factual_docs:
        missing_families = {f.preferred_family for f in missing if f.preferred_family != "any"}
        missing_anchors: set[str] = set()
        for f in missing:
            missing_anchors.update(f.anchor_terms)
        priority_parts: list[str] = []
        other_parts: list[str] = []
        bullets: list[str] = []
        for doc, meta in zip(factual_docs, factual_metas):
            src_type = _src_type(meta)
            doc_lower = clean_text(doc or "").lower()
            is_priority = src_type in missing_families or any(a in doc_lower for a in missing_anchors)
            (priority_parts if is_priority else other_parts).append(doc[:600])
        retry_context = "\n\n".join(priority_parts + other_parts)[:2800]
        for facet in facets:
            best_excerpt = ""
            for doc, meta in zip(factual_docs, factual_metas):
                doc_lower = clean_text(doc or "").lower()
                if (facet.preferred_family != "any" and _src_type(meta) == facet.preferred_family) or any(a in doc_lower for a in facet.anchor_terms):
                    best_excerpt = doc[:200].strip()
                    break
            bullets.append(f"- {facet.topic}: {best_excerpt or '(dettagli nella MEMORIA_FACTUAL)'}")
        facet_template = "\n".join(bullets)

    retry_system = (
        "Riscrivi la risposta correggendo SOLO i difetti indicati."
        " Usa la MEMORIA_FACTUAL per integrare i pezzi mancanti."
        " Non rimuovere le parti gia corrette." + retry_hint
    )

    if english_detected and not multi_facet_check and not visual_retry and not distortion_detected:
        retry_system = (
            "Traduci la seguente risposta in italiano naturale. "
            "Mantieni lo stesso contenuto senza aggiungere o rimuovere informazioni."
        )

    retry_user = f"DOMANDA: {query}\n\nMEMORIA_FACTUAL:\n{retry_context}\n\nRISPOSTA DA CORREGGERE:\n{answer}\n\n"
    if facet_template:
        retry_user += f"PUNTI DA COPRIRE:\n{facet_template}\n\n"
    retry_predict = 400 if (visual_retry or multi_facet_check) else 280
    try:
        retried = _finalize_chat_answer(
            ollama_chat(
                [
                    {"role": "system", "content": retry_system},
                    {"role": "user", "content": retry_user},
                ],
                timeout=50,
                num_predict_override=retry_predict,
            ).strip()
        )
    except Exception:
        return answer

    if not retried:
        return answer

    improved = not _IDENTITY_META_RE.search(retried)
    if improved and len(facets) >= 2 and query_plan.wants_multi_source_coverage:
        improved = len(_check_facet_coverage(retried, facets)) < len(_check_facet_coverage(answer, facets))
    if improved and visual_retry and _check_visual_emptiness(retried, factual_metas, visual_memory_query):
        improved = False
    if improved and distortion_detected and _answer_distorts_profile_relation(retried, factual_context):
        improved = False
    if improved and english_detected and _looks_english(retried):
        improved = False

    return retried if improved else answer

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
                empirical_test_mode=req.empirical_test_mode,
            )
            conversation_logged = True
        except Exception as e:
            print(f"[CHAT-LOG] Errore append log avatar={req.avatar_id}: {e}")
            traceback.print_exc()
    return conversation_logged, conversation_session_id

@app.get("/health")

def health():
    return {
        "ok": True,
        "ollama": OLLAMA_HOST,
        "embed_model": EMBED_MODEL,
        "chat_model": CHAT_MODEL,
        "rag_root": PERSIST_ROOT,
        "rag_log_root": RAG_LOG_DIR,
        "empirical_rag_root": EMPIRICAL_PERSIST_ROOT,
        "empirical_rag_log_root": EMPIRICAL_RAG_LOG_DIR,
        "per_avatar_db": True,
        "cached_avatars": len(_AVATAR_CLIENTS),
        "ocr": bool(pytesseract and Image),
        "pdf": _pymupdf4llm_available,
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
        "intent_confidence_min": RAG_INTENT_CONFIDENCE_MIN,
        "query_rewrite_enabled": RAG_ENABLE_QUERY_REWRITE,
        "query_rewrite_max_tokens": RAG_QUERY_REWRITE_MAX_TOKENS,
        "grounding_score_min": RAG_GROUNDING_SCORE_MIN,
        "factual_max_context_chars": FACTUAL_MAX_CONTEXT_CHARS,
        "session_turns": _effective_session_turns(),
        "intent_router_num_predict": RAG_INTENT_ROUTER_NUM_PREDICT,
        "grounded_mode": RAG_ENFORCE_GROUNDED,
    }

@app.get("/avatar_stats")

def avatar_stats(avatar_id: str, empirical_test_mode: bool = False):
    col = get_collection(avatar_id, empirical_test_mode)
    count = _safe_collection_count(col)
    return {
        "avatar_id": avatar_id,
        "count": count,
        "has_memory": count > 0,
        "empirical_test_mode": empirical_test_mode,
    }

@app.post("/remember")

def remember(req: RememberReq):
    col = get_collection(req.avatar_id, req.empirical_test_mode)

    txt = clean_text(req.text)
    remember_error = _remember_validation_error_detail(txt)
    if remember_error is not None:
        raise HTTPException(status_code=400, detail=remember_error)

    emb = ollama_embed_many([txt])[0]
    _id = str(uuid.uuid4())

    meta = _sanitize_metadata(req.meta or {})
    meta.setdefault("source_type", "manual")
    meta.setdefault("memory_role", _memory_role_for_text(txt))
    meta.setdefault(
        "memory_subject",
        _infer_memory_subject(
            txt,
            meta,
            default_subject=_MEMORY_SUBJECT_AVATAR,
        ),
    )
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
    col = get_collection(req.avatar_id, req.empirical_test_mode)

    q = clean_text(req.query)
    if not q:
        raise HTTPException(status_code=400, detail="Query vuota.")

    memory_count = _safe_collection_count(col)
    fallback_top_k = min(req.top_k, 4)

    if memory_count == 0:
        return _build_recall_response([], [])

    try:
        recall_plan = _build_query_plan(q, "memory_qna")
        recall_intent = recall_plan.normalized_intent
        docs_hybrid, metas_hybrid = _retrieve_context_for_intent(
            col=col,
            query=q,
            intent=recall_intent,
            requested_top_k=req.top_k,
            plan=recall_plan,
        )
        if recall_plan.visual_query and not _has_visual_factual_hits(metas_hybrid):
            docs_hybrid, metas_hybrid = [], []
        if not docs_hybrid and not recall_plan.visual_query:
            for probe_intent in recall_plan.fallback_intents:
                probe_plan = _build_query_plan(q, probe_intent)
                probe_docs, probe_metas = _retrieve_context_for_intent(
                    col=col,
                    query=q,
                    intent=probe_intent,
                    requested_top_k=fallback_top_k,
                    plan=probe_plan,
                )
                if probe_docs:
                    docs_hybrid, metas_hybrid = probe_docs, probe_metas
                    break

        return _build_recall_response(docs_hybrid, metas_hybrid)
    except Exception as e:
        qemb = _embed_one_or_http_500(q)
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
        session_id, log_path = _start_conversation_log_session(
            avatar_id,
            empirical_test_mode=req.empirical_test_mode,
        )
        _reset_session_history(avatar_id, session_id, req.empirical_test_mode)
    except Exception as e:
        raise HTTPException(status_code=500, detail=f"Errore creazione sessione log: {e}")

    return {
        "ok": True,
        "avatar_id": avatar_id,
        "session_id": session_id,
        "log_file": log_path,
        "empirical_test_mode": req.empirical_test_mode,
    }

@app.post("/chat")

def chat(req: ChatReq):
    col = get_collection(req.avatar_id, req.empirical_test_mode)

    q = clean_text(req.user_text)
    if not q:
        raise HTTPException(status_code=400, detail="Messaggio vuoto.")

    auto_remembered = False
    auto_remember_id = None
    remember_content = _detect_remember_intent(q)
    if remember_content:
        auto_remember_id = _auto_remember(req.avatar_id, q, remember_content, col)
        auto_remembered = auto_remember_id is not None

    session_for_history = _ensure_session_history(req.avatar_id, req.session_id, req.empirical_test_mode)
    recent_conversation = _build_recent_conversation_context(req.avatar_id, session_for_history, req.empirical_test_mode)
    memory_count = _safe_collection_count(col)
    fallback_top_k = min(req.top_k, 4)
    query_plan = _route_chat_intent(q, recent_conversation, has_memory=(memory_count > 0))
    intent = query_plan.normalized_intent
    retrieval_query = q
    if memory_count > 0 and intent in {"memory_qna", "memory_recap"} and _is_vague_memory_query(q, query_plan):
        retrieval_query = _rewrite_query_for_memory_retrieval(q, recent_conversation)

    factual_docs: List[str] = []
    factual_metas: List[dict] = []
    if memory_count > 0:
        factual_docs, factual_metas = _retrieve_context_for_intent(
            col=col,
            query=retrieval_query,
            intent=intent,
            requested_top_k=req.top_k,
            plan=query_plan,
        )
        if query_plan.visual_query and not _has_visual_factual_hits(factual_metas):
            factual_docs, factual_metas = [], []
        if not factual_docs and not query_plan.visual_query:
            for probe_intent in query_plan.fallback_intents:
                probe_plan = _build_query_plan(retrieval_query, probe_intent)
                probe_docs, probe_metas = _retrieve_context_for_intent(
                    col=col,
                    query=retrieval_query,
                    intent=probe_intent,
                    requested_top_k=fallback_top_k,
                    plan=probe_plan,
                )
                if probe_docs:
                    query_plan = probe_plan
                    intent = query_plan.normalized_intent
                    factual_docs, factual_metas = probe_docs, probe_metas
                    break

    answer = _chat_fast_path(
        intent=intent,
        query=q,
        query_plan=query_plan,
        factual_docs=factual_docs,
        factual_metas=factual_metas,
        auto_remembered=auto_remembered,
    )
    if answer is None:
        answer, factual_context, profile_target, visual_memory_query, support_recent = _chat_generate(
            req=req,
            intent=intent,
            query=q,
            query_plan=query_plan,
            recent_conversation=recent_conversation,
            factual_docs=factual_docs,
            factual_metas=factual_metas,
            auto_remembered=auto_remembered,
        )
        answer = _chat_repair(
            intent=intent,
            query=q,
            query_plan=query_plan,
            recent_conversation=recent_conversation,
            factual_docs=factual_docs,
            factual_metas=factual_metas,
            answer=answer,
            factual_context=factual_context,
            profile_target=profile_target,
            visual_memory_query=visual_memory_query,
            support_recent=support_recent,
        )
        answer = _verify_answer_coverage(
            answer=answer,
            query=q,
            query_plan=query_plan,
            factual_docs=factual_docs,
            factual_metas=factual_metas,
            factual_context=factual_context,
            profile_target=profile_target,
            visual_memory_query=visual_memory_query,
            recent_conversation=recent_conversation,
        )

    quality_metrics = _build_chat_quality_metrics(
        intent=intent,
        query=q,
        factual_docs=factual_docs,
        factual_metas=factual_metas,
        answer=answer,
        rewritten_query=retrieval_query,
    )
    quality_metrics["intent_confidence_min"] = round(float(max(0.0, min(1.0, RAG_INTENT_CONFIDENCE_MIN))), 3)
    print(f"[CHAT_QUALITY] {json.dumps(quality_metrics, ensure_ascii=False)}")

    _append_session_turn(req.avatar_id, session_for_history, q, answer, req.empirical_test_mode)
    conversation_logged, conversation_session_id = _append_chat_log_if_enabled(
        req=req,
        query=q,
        answer=answer,
        session_for_history=session_for_history,
    )

    return {
        "text": answer,
        "rag_used": _build_rag_used_payload(factual_docs, factual_metas),
        "intent": intent,
        "auto_remembered": auto_remembered,
        "conversation_logged": conversation_logged,
        "conversation_session_id": conversation_session_id,
    }

@app.post("/clear_avatar_logs")

def clear_avatar_logs(avatar_id: str = Form(...), empirical_test_mode: bool = Form(False)):
    """
    Cancella solo la cartella log di un avatar sotto RAG_LOG_DIR.
    """
    avatar_id = (avatar_id or "").strip()
    if not avatar_id:
        raise HTTPException(status_code=400, detail="avatar_id is required")

    deleted_log_dir, log_delete_error, avatar_log_dir = _clear_avatar_logs(avatar_id, empirical_test_mode)

    return {
        "ok": True,
        "avatar_id": avatar_id,
        "deleted_log_dir": deleted_log_dir,
        "log_delete_error": log_delete_error,
        "avatar_log_dir": avatar_log_dir,
        "empirical_test_mode": empirical_test_mode,
    }

@app.post("/avatar_memory_backup")

def avatar_memory_backup(
    avatar_id: str = Form(...),
    empirical_test_mode: bool = Form(False),
):
    avatar_id = (avatar_id or "").strip()
    if not avatar_id:
        raise HTTPException(status_code=400, detail="avatar_id is required")

    avatar_dir = _release_avatar_client_handles(avatar_id, empirical_test_mode)
    snapshot_dir = _avatar_memory_snapshot_dir(avatar_id, empirical_test_mode)

    if not os.path.isdir(avatar_dir):
        return {"ok": True, "avatar_id": avatar_id, "backed_up": False, "empirical_test_mode": empirical_test_mode}

    try:
        if os.path.isdir(snapshot_dir):
            _rmtree_force(snapshot_dir)
        os.makedirs(os.path.dirname(snapshot_dir), exist_ok=True)
        shutil.copytree(avatar_dir, snapshot_dir)
    except Exception as exc:
        raise HTTPException(status_code=500, detail=f"Impossibile creare backup memoria: {exc}")

    return {"ok": True, "avatar_id": avatar_id, "backed_up": True, "empirical_test_mode": empirical_test_mode}

@app.post("/avatar_memory_restore")

def avatar_memory_restore(
    avatar_id: str = Form(...),
    empirical_test_mode: bool = Form(False),
):
    avatar_id = (avatar_id or "").strip()
    if not avatar_id:
        raise HTTPException(status_code=400, detail="avatar_id is required")

    snapshot_dir = _avatar_memory_snapshot_dir(avatar_id, empirical_test_mode)
    if not os.path.isdir(snapshot_dir):
        return {"ok": True, "avatar_id": avatar_id, "restored": False, "empirical_test_mode": empirical_test_mode}

    avatar_dir = _release_avatar_client_handles(avatar_id, empirical_test_mode)
    _reset_all_session_histories_for_avatar(avatar_id, empirical_test_mode)

    try:
        if os.path.isdir(avatar_dir):
            ok, err = _rmtree_force(avatar_dir)
            if not ok and os.path.isdir(avatar_dir):
                raise RuntimeError(err or "rimozione directory avatar fallita")
        os.makedirs(os.path.dirname(avatar_dir), exist_ok=True)
        shutil.copytree(snapshot_dir, avatar_dir)
    except Exception as exc:
        raise HTTPException(status_code=500, detail=f"Impossibile ripristinare memoria: {exc}")

    return {"ok": True, "avatar_id": avatar_id, "restored": True, "empirical_test_mode": empirical_test_mode}

@app.post("/clear_avatar")

def clear_avatar(
    avatar_id: str = Form(...),
    hard: bool = Form(True),
    reset_logs: bool = Form(False),
    empirical_test_mode: bool = Form(False),
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
    persist_root, log_root = _storage_roots(empirical_test_mode)
    avatar_dir = os.path.join(persist_root, avatar_key)
    avatar_log_dir = os.path.join(log_root, avatar_key)

    client = _AVATAR_CLIENTS.pop((_mode_key(empirical_test_mode), avatar_key), None)
    try:
        if client is None:
            client = chromadb.PersistentClient(path=avatar_dir)
    except Exception:
        client = None

    collection_deleted = False
    try:
        if client is not None:
            try:
                client.delete_collection(name="memory")
                collection_deleted = True
            except Exception:
                pass
    except Exception:
        pass

    _stop_chroma_system(client)
    del client
    gc.collect()

    deleted_dir = False
    delete_error = None
    deleted_log_dir = False
    log_delete_error = None

    if hard:
        ok, err = _rmtree_force(avatar_dir)
        deleted_dir = ok
        delete_error = err

    if reset_logs:
        ok, err, avatar_log_dir = _clear_avatar_logs(avatar_id, empirical_test_mode)
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
        "empirical_test_mode": empirical_test_mode,
    }

@app.post("/debug_pdf_ocr")

async def debug_pdf_ocr(
    file: UploadFile = File(...),
    page: int = Form(0),  # 0-indexed
):
    """DEBUG: Estrai testo grezzo da una pagina specifica del PDF via pymupdf4llm."""
    filename = (file.filename or "")
    if not filename.lower().endswith('.pdf'):
        raise HTTPException(status_code=400, detail="Solo PDF")

    raw = await file.read()
    if not raw:
        raise HTTPException(status_code=400, detail="File vuoto")

    if not _pymupdf4llm_available:
        raise HTTPException(status_code=500, detail="pymupdf4llm non disponibile")
    assert pymupdf is not None and pymupdf4llm is not None

    ocr_ready = _ensure_ocr_ready()
    doc = pymupdf.open(stream=raw, filetype="pdf")
    try:
        pages_data = pymupdf4llm.to_markdown(doc, pages=[page], page_chunks=True)
    except Exception as e:
        doc.close()
        raise HTTPException(status_code=500, detail=f"Errore estrazione: {str(e)[:100]}")
    doc.close()

    if not pages_data:
        raise HTTPException(status_code=400, detail=f"Pagina {page} non trovata o vuota")
    page_text = pages_data[0].get("text", "") if isinstance(pages_data[0], dict) else str(pages_data[0])
    return {
        "filename": filename,
        "page": page,
        "method": "pymupdf4llm",
        "ocr_enabled": ocr_ready,
        "text_length": len(page_text),
        "text": page_text[:3000] if len(page_text) > 3000 else page_text,
    }

@app.post("/describe_image")

async def describe_image(
    file: UploadFile = File(...),
    prompt: str = Form("Descrivi dettagliatamente questa immagine in italiano."),
    avatar_id: Optional[str] = Form(None),
    remember: bool = Form(False),
    empirical_test_mode: bool = Form(False),
):
    """Descrivi un'immagine usando Gemini Vision e opzionalmente la salva in memoria."""
    if _gemini_client is None or not GEMINI_API_KEY:
        raise HTTPException(status_code=500, detail="Gemini non configurato. Imposta GEMINI_API_KEY.")

    filename = file.filename or "image"
    raw = await file.read()
    if not raw:
        raise HTTPException(status_code=400, detail="File vuoto.")

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
                "memory_subject": _MEMORY_SUBJECT_EXTERNAL,
                "source_filename": filename,
                "avatar_id": avatar_id,
                "ts": int(time.time()),
            }
            col = get_collection(avatar_id, empirical_test_mode)
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
        "empirical_test_mode": empirical_test_mode,
    }

@app.post("/ingest_file")

async def ingest_file(
    avatar_id: str = Form(...),
    file: UploadFile = File(...),
    empirical_test_mode: bool = Form(False),
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

    if ext == ".pdf":
        sections = extract_text_from_pdf(raw)
        if not sections:
            raise HTTPException(status_code=400, detail="Nessun testo estratto dal PDF (OCR fallito o testo illeggibile).")

    elif ext in (".png", ".jpg", ".jpeg", ".webp", ".bmp", ".tif", ".tiff"):
        txt = ocr_image_bytes(raw)
        if not txt or looks_like_garbage(txt):
            raise HTTPException(status_code=400, detail="OCR non ha prodotto testo utile dall'immagine.")
        sections = [(txt, {"page": 1, "ocr": True})]

    else:
        txt = extract_text_from_plain(raw)
        if not txt or looks_like_garbage(txt):
            raise HTTPException(status_code=400, detail="File non testuale o contenuto non leggibile.")
        sections = [(txt, {"page": 1})]

    col = get_collection(avatar_id, empirical_test_mode)

    ids: List[str] = []
    docs: List[str] = []
    metas: List[ChromaMetadata] = []
    seen_chunks: set = set()  # Track unique chunks

    for text, meta in sections:
        chunks = chunk_text(text)
        for idx, ch in enumerate(chunks):
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
                    "memory_subject": _MEMORY_SUBJECT_EXTERNAL,
                    "source_filename": filename,
                    "chunk": idx,
                    "ts": int(time.time()),
                }
            )
            m["avatar_id"] = avatar_id
            metas.append(cast(ChromaMetadata, _sanitize_metadata(m)))

    if not docs:
        raise HTTPException(status_code=400, detail="Nessun chunk valido generato dal file.")

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
        uvicorn.run(app, host="127.0.0.1", port=8002, reload=False)
    except Exception as e:
        print(f"[FATAL] Errore server: {e}", file=sys.stderr, flush=True)
        import traceback
        traceback.print_exc(file=sys.stderr)
        sys.exit(1)

