"""SOULFRAME - Coqui TTS Server (FastAPI)

TTS multilingua (es. italiano) + voice cloning tramite XTTS v2.

Uso consigliato:
- Carichi UNA volta l'audio di riferimento per un avatar con /set_avatar_voice
- Poi chiami /tts passando solo avatar_id + testo (speaker_wav opzionale)

Nota Swagger (/docs): la risposta di /tts è binaria (audio/wav) e spesso
l'UI la mostra "grigia" senza player. Per ascoltare, salva su file (curl -o...).
Per debugging puoi usare /tts_json che restituisce base64.

Env utili:
- COQUI_TTS_MODEL                (default: tts_models/multilingual/multi-dataset/xtts_v2)
- COQUI_LANG                     (default: it)
- COQUI_DEFAULT_SPEAKER_WAV      (default: backend/voices/default.wav)
- COQUI_AVATAR_VOICES_DIR        (default: backend/voices/avatars)
- COQUI_TTS_DEVICE               (cpu|cuda) (auto se non impostato)
- COQUI_GPT_COND_LEN             (default: 12) secondi di riferimento audio per condizionamento
- COQUI_GPT_COND_CHUNK_LEN       (default: 4) chunk conditioning
- COQUI_TEMPERATURE              (default: 0.65) randomness (più basso = più stabile)
- COQUI_REPETITION_PENALTY       (default: 5.0) penalità ripetizioni
- COQUI_LENGTH_PENALTY            (default: 1.0) penalità lunghezza
- COQUI_TOP_K                    (default: 50) sampling top-k
- COQUI_TOP_P                    (default: 0.85) sampling nucleus
"""

from __future__ import annotations

import asyncio
import base64
import io
import os
import re
import shutil
import tempfile
import time
import unicodedata
import uuid
import wave
from datetime import datetime
from pathlib import Path
from types import SimpleNamespace
from typing import Any, Optional, cast
from urllib.parse import quote

import numpy as np
import soundfile as sf
from fastapi import FastAPI, File, Form, HTTPException, UploadFile
from fastapi.responses import JSONResponse, Response, StreamingResponse, FileResponse
from starlette.middleware.cors import CORSMiddleware

# -------------------- Configurazione --------------------

HERE = Path(__file__).resolve().parent

MODEL_NAME = os.getenv("COQUI_TTS_MODEL", "tts_models/multilingual/multi-dataset/xtts_v2")
DEFAULT_LANG = os.getenv("COQUI_LANG", "it")

DEFAULT_SPEAKER_WAV = os.getenv("COQUI_DEFAULT_SPEAKER_WAV", str(HERE / "voices" / "default.wav"))
AVATAR_VOICES_DIR = os.getenv("COQUI_AVATAR_VOICES_DIR", str(HERE / "voices" / "avatars"))
LEGACY_AVATAR_VOICES_DIR = str(HERE / "voices" / "avatars")
EMPIRICAL_VOICES_ROOT = HERE / "empirical_test" / "voices"

# Se vuoi forzare: cpu | cuda
FORCE_DEVICE = os.getenv("COQUI_TTS_DEVICE", "").strip().lower()

# XTTS spesso lavora bene a 24k
OUTPUT_SAMPLE_RATE = int(os.getenv("COQUI_OUTPUT_SR", "24000"))
STREAM_PCM_CHUNK_BYTES = max(8192, int(os.getenv("COQUI_STREAM_PCM_CHUNK_BYTES", "49152")))
STREAM_WEBGL_FORCE_CHUNKING = os.getenv("COQUI_STREAM_WEBGL_FORCE_CHUNKING", "1").strip().lower() in {"1", "true", "yes", "on"}
REPLY_TTS_SEGMENT_MAX_CHARS = max(40, int(os.getenv("COQUI_REPLY_SEGMENT_MAX_CHARS", "200")))
REPLY_TTS_ALIGNMENT_SAFE_MAX_CHARS = max(80, int(os.getenv("COQUI_REPLY_ALIGNMENT_SAFE_MAX_CHARS", "160")))
REPLY_TTS_SHORT_SENTENCE_MAX_WORDS = max(1, int(os.getenv("COQUI_REPLY_SHORT_SENTENCE_MAX_WORDS", "3")))
REPLY_TTS_MIN_TAIL_WORDS = max(1, int(os.getenv("COQUI_REPLY_MIN_TAIL_WORDS", "3")))
REPLY_TTS_PUNCTUATION_LOOKBACK_CHARS = max(8, int(os.getenv("COQUI_REPLY_PUNCTUATION_LOOKBACK_CHARS", "36")))
LOG_REPLY_SEGMENTS = os.getenv("COQUI_LOG_REPLY_SEGMENTS", "1").strip().lower() in {"1", "true", "yes", "on"}

# Parametri qualità voce XTTS v2 (tuning).
# gpt_cond_len: secondi di audio di riferimento usati per condizionare (più alto = voce più fedele, un po' più lento)
# temperature: casualita' nella generazione (piu' basso = piu' stabile/consistente)
# repetition_penalty: penalizza ripetizioni (aiuta con balbuzie/stuttering)
XTTS_GPT_COND_LEN = int(os.getenv("COQUI_GPT_COND_LEN", "12"))
XTTS_GPT_COND_CHUNK_LEN = int(os.getenv("COQUI_GPT_COND_CHUNK_LEN", "4"))
XTTS_TEMPERATURE = float(os.getenv("COQUI_TEMPERATURE", "0.45"))
XTTS_REPETITION_PENALTY = float(os.getenv("COQUI_REPETITION_PENALTY", "4.0"))
XTTS_LENGTH_PENALTY = float(os.getenv("COQUI_LENGTH_PENALTY", "1.0"))
XTTS_TOP_K = int(os.getenv("COQUI_TOP_K", "40"))
XTTS_TOP_P = float(os.getenv("COQUI_TOP_P", "0.80"))
XTTS_MAX_REF_LEN = int(os.getenv("COQUI_MAX_REF_LEN", "10"))
XTTS_SOUND_NORM_REFS = os.getenv("COQUI_SOUND_NORM_REFS", "0").strip().lower() in {"1", "true", "yes", "on"}

PRELOAD_ON_STARTUP = os.getenv("COQUI_PRELOAD_ON_STARTUP", "1").strip().lower() in {"1", "true", "yes", "on"}
PRELOAD_ALIGNMENT_ON_STARTUP = os.getenv("COQUI_PRELOAD_ALIGNMENT_ON_STARTUP", "1").strip().lower() in {"1", "true", "yes", "on"}
WARMUP_ON_STARTUP = os.getenv("COQUI_WARMUP_ON_STARTUP", "1").strip().lower() in {"1", "true", "yes", "on"}
WARMUP_TEXT = os.getenv("COQUI_WARMUP_TEXT", "Ciao.")
WARMUP_LANG = os.getenv("COQUI_WARMUP_LANG", DEFAULT_LANG)
WARMUP_AVATAR_ID = os.getenv("COQUI_WARMUP_AVATAR_ID", "default")

REFERENCE_TRIM_SILENCE = os.getenv("COQUI_REFERENCE_TRIM_SILENCE", "1").strip().lower() in {"1", "true", "yes", "on"}
REFERENCE_SILENCE_THRESHOLD = float(os.getenv("COQUI_REFERENCE_SILENCE_THRESHOLD", "0.01"))
REFERENCE_TRIM_PAD_SEC = float(os.getenv("COQUI_REFERENCE_TRIM_PAD_SEC", "0.12"))

# -------------------- Applicazione --------------------

app = FastAPI(title="SOULFRAME Coqui TTS", version="1.2.0")

app.add_middleware(
    CORSMiddleware,
    allow_origins=["*"],
    allow_credentials=False,
    allow_methods=["*"],
    allow_headers=["*"],
    expose_headers=[
        "X-Avatar-Id",
        "X-Coqui-Lang",
        "X-Audio-Rate",
        "X-Audio-Format",
        "X-Speaker-Source",
        "X-Empirical-Test-Mode",
        "X-TTS-Alignment-Version",
        "X-TTS-Alignment-Source",
        "X-TTS-Request-Id",
    ],
)

# -------------------- Variabili globali --------------------

_tts = None
_device_used = None
_fa_bundle = None
_fa_model = None
_fa_tokenizer = None
_fa_aligner = None
_fa_device_used = None

# TTS Single-Flight Queue Control
_tts_semaphore = asyncio.Semaphore(1)
_tts_queue_stats = SimpleNamespace(total_requests=0, active_now=0, queued_now=0, rejected_429_count=0)
_incremental_tts_sessions: dict[str, dict[str, Any]] = {}
INCREMENTAL_TIMING_SESSION_TTL_SECONDS = max(30, int(os.getenv("COQUI_INCREMENTAL_TIMING_TTL_SECONDS", "120")))
INCREMENTAL_TIMING_COMPLETED_TTL_SECONDS = max(
    5,
    int(os.getenv("COQUI_INCREMENTAL_TIMING_COMPLETED_TTL_SECONDS", "25")),
)
INCREMENTAL_TIMING_MAX_SESSIONS = max(8, int(os.getenv("COQUI_INCREMENTAL_TIMING_MAX_SESSIONS", "96")))
_incremental_tts_session_stats = SimpleNamespace(pruned_ttl=0, evicted_capacity=0)

# -------------------- Helper --------------------


_AVATAR_ID_RX = re.compile(r"[^a-zA-Z0-9_\-]+")
_TTS_REQUEST_ID_RX = re.compile(r"[^a-zA-Z0-9_\-]+")

WAIT_PHRASES: list[tuple[str, str]] = [
    ("hm", "Hm"),
    ("beh", "Beh"),
    ("aspetta", "Aspetta"),
    ("si", "Sì"),
    ("un_secondo", "Un secondo"),
]


def _prune_incremental_tts_sessions() -> None:
    now = time.time()
    expired_ids: list[str] = []

    for request_id, session in _incremental_tts_sessions.items():
        is_complete = bool(session.get("complete", False))
        ttl_seconds = INCREMENTAL_TIMING_COMPLETED_TTL_SECONDS if is_complete else INCREMENTAL_TIMING_SESSION_TTL_SECONDS
        reference_ts = float(
            session.get(
                "completed_at" if is_complete else "updated_at",
                session.get("updated_at", session.get("started_at", now)),
            )
        )
        if now - reference_ts > ttl_seconds:
            expired_ids.append(request_id)

    for request_id in expired_ids:
        _incremental_tts_sessions.pop(request_id, None)
    _incremental_tts_session_stats.pruned_ttl += len(expired_ids)


def _enforce_incremental_tts_session_capacity(*, protected_request_id: Optional[str] = None) -> None:
    if len(_incremental_tts_sessions) <= INCREMENTAL_TIMING_MAX_SESSIONS:
        return

    def _eviction_sort_key(item: tuple[str, dict[str, Any]]) -> tuple[int, float]:
        _, session = item
        # Eviction policy: complete sessions first, then oldest updated/started.
        priority = 0 if bool(session.get("complete", False)) else 1
        age_ref = float(session.get("updated_at", session.get("started_at", 0.0)))
        return priority, age_ref

    evicted = 0
    for request_id, _ in sorted(_incremental_tts_sessions.items(), key=_eviction_sort_key):
        if len(_incremental_tts_sessions) <= INCREMENTAL_TIMING_MAX_SESSIONS:
            break
        if protected_request_id and request_id == protected_request_id:
            continue
        if _incremental_tts_sessions.pop(request_id, None) is not None:
            evicted += 1

    _incremental_tts_session_stats.evicted_capacity += evicted


def _coerce_tts_request_id(request_id: Optional[str]) -> str:
    candidate = (request_id or "").strip()
    if not candidate:
        candidate = f"{int(time.time() * 1_000_000)}_{uuid.uuid4().hex[:8]}"
    candidate = _TTS_REQUEST_ID_RX.sub("_", candidate)
    return candidate[:96] or f"req_{uuid.uuid4().hex[:8]}"


def _set_incremental_tts_session(
    request_id: str,
    *,
    words: Optional[list[str]] = None,
    word_end_ms: Optional[list[int]] = None,
    segment_end_ms: Optional[list[int]] = None,
    complete: bool = False,
    error: Optional[str] = None,
    create: bool = False,
) -> None:
    _prune_incremental_tts_sessions()
    now = time.time()
    session = _incremental_tts_sessions.get(request_id)
    if create:
        session = {
            "request_id": request_id,
            "words": [],
            "word_end_ms": [],
            "segment_end_ms": [],
            "complete": False,
            "completed_at": None,
            "error": None,
            "started_at": now,
            "updated_at": now,
        }
        _incremental_tts_sessions[request_id] = session
    if session is None:
        return

    if words is not None:
        session["words"] = list(words)
    if word_end_ms is not None:
        session["word_end_ms"] = list(word_end_ms)
    if segment_end_ms is not None:
        session["segment_end_ms"] = list(segment_end_ms)

    session["complete"] = bool(complete)
    session["completed_at"] = now if complete else None
    session["error"] = error
    session["updated_at"] = now
    _enforce_incremental_tts_session_capacity(protected_request_id=request_id)


def _clean_tts_text(text: str) -> str:
    """Pulisce il testo prima della sintesi TTS per evitare che la punteggiatura venga pronunciata.

    XTTS v2 usa la punteggiatura per la prosodia (pause, intonazione), quindi
    manteniamo la punteggiatura utile ma rimuoviamo quella problematica:
    - Punti multipli ("...", "....")
    - Punteggiatura isolata o ridondante
    - Simboli che XTTS potrebbe vocalizzare (*, #, @, etc.)
    - Parentesi, virgolette, trattini decorativi
    """
    if not text:
        return text

    s = text.strip()

    # Rimuovi simboli che TTS potrebbe provare a pronunciare
    s = re.sub(r'[*#@~^|\\{}\[\]<>]', '', s)

    # Rimuovi parentesi e il loro contenuto se contengono solo punteggiatura/numeri
    s = re.sub(r'\([^a-zA-Z\u00C0-\u00FF]*\)', '', s)

    # Virgolette di ogni tipo -> rimuovi
    s = re.sub(r'[\'\"«»„“”‟‘’]', '', s)

    # Ellipsis e punti multipli -> singola virgola (pausa naturale)
    s = re.sub(r'\.{2,}', ',', s)
    s = re.sub(r'\u2026', ',', s)

    # Trattini lunghi/decorativi -> virgola
    s = re.sub(r'[\u2014\u2013]+', ',', s)

    # Slash -> spazio
    s = re.sub(r'/', ' ', s)

    # Underscore -> spazio
    s = re.sub(r'_', ' ', s)

    # Evita letture tipo "punto effe": punti interni a token (es. "abc.def", "A.B", ecc.)
    s = re.sub(r'(?<=\S)\.(?=\S)', ' ', s)

    # Trasformiamo i punti residui in pausa morbida
    s = re.sub(r'\.(?=\s|$)', ',', s)

    # Punteggiatura ripetuta (es. !!, ??, ,,) -> singola
    s = re.sub(r'([!?,;:]){2,}', r'\1', s)

    # Punto doppio -> singolo
    s = re.sub(r'\.\s*\.', '.', s)

    # Rimuovi punteggiatura isolata (spazio + punteggiatura + spazio)
    s = re.sub(r'\s+[.,;:!?]\s+', ' ', s)

    # Normalizza spazi multipli
    s = re.sub(r'\s{2,}', ' ', s)

    # Rimuovi punteggiatura iniziale
    s = re.sub(r'^[.,;:!?\s]+', '', s)

    return s.strip()


def _normalize_alignment_word(word: str) -> str:
    if not word:
        return ""

    normalized = word.replace("’", "'").replace("‘", "'").lower()
    normalized = unicodedata.normalize("NFD", normalized)
    normalized = "".join(ch for ch in normalized if not unicodedata.combining(ch))
    normalized = re.sub(r"[^a-z']+", "", normalized)
    normalized = re.sub(r"'{2,}", "'", normalized)
    return normalized.strip("'")


def _extract_alignment_words(text: str) -> list[str]:
    # Pulizia leggera invece di _clean_tts_text: preserva gli apostrofi interni
    # alle parole italiane (es. "l'utente", "dell'aria"). _clean_tts_text li
    # rimuoveva tutti, causando mismatch con Unity ("l'utente" != "lutente").
    s = (text or "").strip()
    if not s:
        return []
    s = re.sub(r'[*#@~^|\\{}\[\]<>()_]', ' ', s)
    s = re.sub(r'[«»“”„‟"]', '', s)
    s = re.sub(r'\.{2,}|…', ' ', s)
    s = re.sub(r'[—–/]', ' ', s)
    s = re.sub(r'\s{2,}', ' ', s)
    s = s.strip()
    if not s:
        return []
    normalized = s.replace('’', "'").replace('‘', "'").replace('-', ' ')
    normalized = unicodedata.normalize('NFD', normalized.lower())
    normalized = ''.join(ch for ch in normalized if not unicodedata.combining(ch))
    matches = re.findall(r"[a-z]+(?:'[a-z]+)*", normalized)
    return [word for word in (_normalize_alignment_word(match) for match in matches) if word]


def _alignment_matches_expected_text(aligned_words: list[str], expected_text: str) -> bool:
    expected_words = _extract_alignment_words(expected_text)
    if not expected_words or not aligned_words:
        return False
    if len(expected_words) != len(aligned_words):
        return False
    return all(expected == aligned for expected, aligned in zip(expected_words, aligned_words))


def _build_segment_duration_fallback_timing(
    segmented_text_durations: list[tuple[str, int]],
    expected_text: str,
) -> tuple[list[str], list[int]]:
    expected_words = _extract_alignment_words(expected_text)
    if not expected_words or not segmented_text_durations:
        return [], []

    words: list[str] = []
    end_ms: list[int] = []
    cumulative_ms = 0

    for segment_text, segment_duration_ms in segmented_text_durations:
        segment_words = _extract_alignment_words(segment_text)
        cumulative_ms += max(1, int(segment_duration_ms))
        if not segment_words:
            continue

        segment_weights = [max(1, len(word)) for word in segment_words]
        total_weight = sum(segment_weights)
        segment_cumulative = 0
        segment_word_count = len(segment_words)

        for index, (segment_word, weight) in enumerate(zip(segment_words, segment_weights), start=1):
            if index >= segment_word_count:
                local_end_ms = segment_duration_ms
            else:
                share_ms = int(round((weight / float(total_weight)) * segment_duration_ms))
                segment_cumulative += max(1, share_ms)
                local_end_ms = min(segment_duration_ms, segment_cumulative)

            absolute_end_ms = max(1, cumulative_ms - segment_duration_ms + local_end_ms)
            min_allowed = 1 if not end_ms else end_ms[-1]
            absolute_end_ms = max(min_allowed, absolute_end_ms)
            absolute_end_ms = min(cumulative_ms, absolute_end_ms)
            words.append(segment_word)
            end_ms.append(absolute_end_ms)

    if len(words) != len(expected_words):
        return [], []
    if words != expected_words:
        return [], []
    return words, end_ms


# Prepara i bytes del file WAV di riferimento, con validazione, trimming silenzio e normalizzazione.
def _prepare_reference_wav_bytes(data: bytes) -> bytes:
    if not data:
        raise ValueError("File speaker_wav vuoto.")

    try:
        wav, sr = sf.read(io.BytesIO(data), dtype="float32", always_2d=False)
    except Exception as exc:
        raise ValueError(f"Formato speaker_wav non valido: {exc}")

    wav = np.asarray(wav, dtype=np.float32)
    if wav.ndim == 2:
        wav = np.mean(wav, axis=1, dtype=np.float32)
    elif wav.ndim > 2:
        wav = wav.reshape(-1).astype(np.float32)

    wav = np.nan_to_num(wav, nan=0.0, posinf=0.0, neginf=0.0).reshape(-1)
    if wav.size < 128:
        raise ValueError("File speaker_wav troppo corto.")

    if REFERENCE_TRIM_SILENCE:
        threshold = max(1e-4, float(REFERENCE_SILENCE_THRESHOLD))
        non_silent = np.where(np.abs(wav) >= threshold)[0]
        if non_silent.size > 0:
            pad = max(0, int(float(REFERENCE_TRIM_PAD_SEC) * max(1, int(sr))))
            start = max(0, int(non_silent[0]) - pad)
            end = min(wav.size, int(non_silent[-1]) + pad + 1)
            wav = wav[start:end]

    if wav.size < 128:
        raise ValueError("File speaker_wav troppo corto dopo preprocess.")

    peak = float(np.max(np.abs(wav)))
    if peak > 1e-6:
        wav = (wav / peak) * 0.97

    out = io.BytesIO()
    sf.write(out, wav, samplerate=int(sr), format="WAV", subtype="PCM_16")
    return out.getvalue()


def _safe_avatar_id(avatar_id: str) -> str:
    avatar_id = (avatar_id or "").strip()
    if not avatar_id:
        return "default"
    avatar_id = _AVATAR_ID_RX.sub("_", avatar_id)
    return avatar_id[:64]


def _voices_root(empirical_test_mode: bool = False) -> Path:
    return EMPIRICAL_VOICES_ROOT if empirical_test_mode else Path(DEFAULT_SPEAKER_WAV).parent


def _avatar_voice_dir(empirical_test_mode: bool = False) -> Path:
    return (_voices_root(empirical_test_mode) / "avatars") if empirical_test_mode else Path(AVATAR_VOICES_DIR)


def _default_speaker_path(empirical_test_mode: bool = False) -> Path:
    return (_voices_root(empirical_test_mode) / "default.wav") if empirical_test_mode else Path(DEFAULT_SPEAKER_WAV)


def _avatar_voice_path(avatar_id: str, empirical_test_mode: bool = False) -> Path:
    safe = _safe_avatar_id(avatar_id)
    return _avatar_voice_dir(empirical_test_mode) / safe / "reference.wav"


def _avatar_voice_snapshot_dir(avatar_id: str, empirical_test_mode: bool = False) -> Path:
    safe = _safe_avatar_id(avatar_id)
    return _voices_root(empirical_test_mode) / "_snapshots" / "avatars" / safe


def _wait_phrase_path(avatar_id: str, key: str, empirical_test_mode: bool = False) -> Path:
    safe = _safe_avatar_id(avatar_id)
    return _avatar_voice_dir(empirical_test_mode) / safe / f"wait_{key}.wav"


def _legacy_wait_phrase_path(avatar_id: str, key: str, empirical_test_mode: bool = False) -> Path:
    safe = _safe_avatar_id(avatar_id)
    if empirical_test_mode:
        return _wait_phrase_path(avatar_id, key, empirical_test_mode=True)
    return Path(LEGACY_AVATAR_VOICES_DIR) / safe / f"wait_{key}.wav"


def _resolve_wait_phrase_path(avatar_id: str, key: str, empirical_test_mode: bool = False) -> Path:
    primary = _wait_phrase_path(avatar_id, key, empirical_test_mode)
    legacy = _legacy_wait_phrase_path(avatar_id, key, empirical_test_mode)

    if _file_has_content(primary):
        return primary

    if legacy != primary and _file_has_content(legacy):
        try:
            primary.parent.mkdir(parents=True, exist_ok=True)
            primary.write_bytes(legacy.read_bytes())
            return primary
        except Exception:
            return legacy

    return primary


def _file_has_content(path: Path, min_bytes: int = 512) -> bool:
    try:
        return path.exists() and path.is_file() and path.stat().st_size >= min_bytes
    except Exception:
        return False


def _resolve_speaker_path(safe_avatar_id: str, empirical_test_mode: bool = False) -> Optional[str]:
    avatar_ref = _avatar_voice_path(safe_avatar_id, empirical_test_mode)
    if _file_has_content(avatar_ref):
        return str(avatar_ref)

    default_ref = _default_speaker_path(empirical_test_mode)
    if _file_has_content(default_ref):
        return str(default_ref)

    return None


def _normalize_language(language: str) -> str:
    return (language or DEFAULT_LANG).strip() or DEFAULT_LANG


def _cleanup_temp_path(tmp_path: Optional[Path]) -> None:
    if tmp_path is None:
        return
    try:
        tmp_path.unlink(missing_ok=True)
    except Exception:
        pass


async def _prepare_request_speaker(
    safe_avatar_id: str,
    speaker_wav: Optional[UploadFile],
    save_voice: bool,
    empirical_test_mode: bool = False,
) -> tuple[Optional[str], Optional[Path], str]:
    """Resolve speaker reference preserving priority: upload -> avatar -> default."""
    speaker_path: Optional[str] = None
    tmp_path: Optional[Path] = None
    speaker_source = "none"

    if speaker_wav is not None and speaker_wav.filename:
        data = await speaker_wav.read()
        if data and len(data) >= 512:
            try:
                data = _prepare_reference_wav_bytes(data)
            except ValueError as exc:
                raise HTTPException(status_code=400, detail=str(exc))

            suffix = Path(speaker_wav.filename).suffix or ".wav"
            fd, tmp_name = tempfile.mkstemp(prefix="speaker_", suffix=suffix)
            os.close(fd)
            tmp_path = Path(tmp_name)
            tmp_path.write_bytes(data)
            speaker_path = str(tmp_path)
            speaker_source = "upload"

            if save_voice:
                out_path = _avatar_voice_path(safe_avatar_id, empirical_test_mode)
                out_path.parent.mkdir(parents=True, exist_ok=True)
                out_path.write_bytes(data)

    if speaker_path is None:
        avatar_ref = _avatar_voice_path(safe_avatar_id, empirical_test_mode)
        if _file_has_content(avatar_ref):
            speaker_path = str(avatar_ref)
            speaker_source = "avatar"

    if speaker_path is None:
        default_ref = _default_speaker_path(empirical_test_mode)
        if _file_has_content(default_ref):
            speaker_path = str(default_ref)
            speaker_source = "default"

    return speaker_path, tmp_path, speaker_source


def _generate_wait_phrase_files(
    avatar_id: str,
    language: str,
    empirical_test_mode: bool = False,
) -> list[str]:
    _ensure_loaded()

    safe = _safe_avatar_id(avatar_id)
    language = _normalize_language(language)
    speaker_path = _resolve_speaker_path(safe, empirical_test_mode)
    if speaker_path is None:
        raise RuntimeError("Nessun riferimento vocale disponibile. Usa /set_avatar_voice prima.")

    output_dir = _avatar_voice_dir(empirical_test_mode) / safe
    output_dir.mkdir(parents=True, exist_ok=True)

    written: list[str] = []
    for key, phrase in WAIT_PHRASES:
        wav_bytes = _synth_to_wav_bytes(text=phrase, language=language, speaker_wav_path=speaker_path)
        out_path = output_dir / f"wait_{key}.wav"
        out_path.write_bytes(wav_bytes)
        written.append(out_path.name)

    return written


def _is_cuda_related_error(exc: Exception) -> bool:
    msg = str(exc).lower()
    markers = (
        "cuda",
        "cudnn",
        "cublas",
        "out of memory",
        "device-side",
        "driver",
        "hip",
    )
    return any(k in msg for k in markers)


def _load_tts_on_device(device: str):
    from TTS.api import TTS  # type: ignore

    model = TTS(MODEL_NAME)
    model.to(device)
    return model


def _switch_to_cpu_fallback(reason: Exception) -> bool:
    global _tts, _device_used
    if _device_used == "cpu":
        return False
    try:
        _tts = _load_tts_on_device("cpu")
        _device_used = "cpu"
        print("[WARN] Coqui switched to CPU fallback.", flush=True)
        return True
    except Exception as cpu_exc:
        print(f"[ERR] Coqui CPU fallback failed: {cpu_exc}", flush=True)
        return False


def _ensure_loaded() -> None:
    global _tts, _device_used
    if _tts is not None:
        return

    # Import pesanti solo qui
    import torch  # type: ignore

    preferred_device = "cuda" if bool(torch.cuda.is_available()) else "cpu"
    if FORCE_DEVICE in ("cpu", "cuda"):
        preferred_device = FORCE_DEVICE

    try:
        _tts = _load_tts_on_device(preferred_device)
        _device_used = preferred_device
    except Exception as e:
        if preferred_device == "cuda" and _is_cuda_related_error(e):
            if not _switch_to_cpu_fallback(e):
                raise
        else:
            raise


def _load_alignment_on_device(device: str):
    import torchaudio  # type: ignore

    bundle = torchaudio.pipelines.MMS_FA
    model = bundle.get_model().to(device)
    model.eval()
    return bundle, model, bundle.get_tokenizer(), bundle.get_aligner()


def _switch_alignment_to_cpu_fallback(reason: Exception) -> bool:
    global _fa_bundle, _fa_model, _fa_tokenizer, _fa_aligner, _fa_device_used
    if _fa_device_used == "cpu":
        return False
    try:
        _fa_bundle, _fa_model, _fa_tokenizer, _fa_aligner = _load_alignment_on_device("cpu")
        _fa_device_used = "cpu"
        print(f"[WARN] MMS_FA switched to CPU fallback: {reason}", flush=True)
        return True
    except Exception as cpu_exc:
        print(f"[ERR] MMS_FA CPU fallback failed: {cpu_exc}", flush=True)
        return False


def _ensure_alignment_loaded() -> None:
    global _fa_bundle, _fa_model, _fa_tokenizer, _fa_aligner, _fa_device_used
    if _fa_model is not None and _fa_bundle is not None and _fa_tokenizer is not None and _fa_aligner is not None:
        return

    import torch  # type: ignore

    preferred_device = _device_used or ("cuda" if bool(torch.cuda.is_available()) else "cpu")
    if preferred_device not in ("cpu", "cuda"):
        preferred_device = "cpu"

    try:
        _fa_bundle, _fa_model, _fa_tokenizer, _fa_aligner = _load_alignment_on_device(preferred_device)
        _fa_device_used = preferred_device
    except Exception as exc:
        if preferred_device == "cuda" and _is_cuda_related_error(exc):
            if not _switch_alignment_to_cpu_fallback(exc):
                raise
        else:
            raise


def _tts_generate(text: str, language: str, speaker_wav_path: str):
    global _tts

    synth_kwargs: dict = dict(
        text=text,
        speaker_wav=speaker_wav_path,
        language=language,
    )

    def _run_once():
        try:
            synth_kwargs.update(
                gpt_cond_len=XTTS_GPT_COND_LEN,
                gpt_cond_chunk_len=XTTS_GPT_COND_CHUNK_LEN,
                temperature=XTTS_TEMPERATURE,
                repetition_penalty=XTTS_REPETITION_PENALTY,
                length_penalty=XTTS_LENGTH_PENALTY,
                top_k=XTTS_TOP_K,
                top_p=XTTS_TOP_P,
                max_ref_len=XTTS_MAX_REF_LEN,
                sound_norm_refs=XTTS_SOUND_NORM_REFS,
            )
            return _tts.tts(**synth_kwargs)  # type: ignore[attr-defined]
        except TypeError:
            # Il modello non supporta parametri extra (fallback base)
            return _tts.tts(text=text, speaker_wav=speaker_wav_path, language=language)  # type: ignore[attr-defined]

    try:
        return _run_once()
    except Exception as e:
        if _device_used == "cuda" and _is_cuda_related_error(e) and _switch_to_cpu_fallback(e):
            return _run_once()
        raise


def _synth_to_wav_bytes(
    text: str,
    language: str,
    speaker_wav_path: str,
    sample_rate: int = OUTPUT_SAMPLE_RATE,
) -> bytes:
    text = _clean_tts_text(text)
    if not text.strip():
        raise ValueError("Text vuoto.")

    wav = _tts_generate(text=text, language=language, speaker_wav_path=speaker_wav_path)
    if wav is None:
        raise RuntimeError("TTS ha restituito None.")

    wav = np.asarray(wav, dtype=np.float32).reshape(-1)
    if wav.size < 10:
        raise RuntimeError("Audio generato vuoto (wav troppo corto).")

    buf = io.BytesIO()
    sf.write(buf, wav, samplerate=sample_rate, format="WAV", subtype="PCM_16")
    return buf.getvalue()


def _read_wav_mono_float32(wav_bytes: bytes) -> tuple[np.ndarray, int]:
    waveform, sample_rate = sf.read(io.BytesIO(wav_bytes), dtype="float32", always_2d=False)
    waveform = np.asarray(waveform, dtype=np.float32)
    if waveform.ndim == 2:
        waveform = np.mean(waveform, axis=1, dtype=np.float32)
    elif waveform.ndim > 2:
        waveform = waveform.reshape(-1).astype(np.float32)
    waveform = np.nan_to_num(waveform, nan=0.0, posinf=0.0, neginf=0.0).reshape(-1)
    if waveform.size == 0:
        raise ValueError("Audio WAV vuoto.")
    return waveform, int(sample_rate)


def _align_words_from_wav_bytes(wav_bytes: bytes, transcript_text: str) -> tuple[list[str], list[int]]:
    _ensure_alignment_loaded()
    if (
        _fa_bundle is None
        or _fa_model is None
        or _fa_tokenizer is None
        or _fa_aligner is None
        or _fa_device_used is None
    ):
        raise RuntimeError("Forced alignment non disponibile.")

    words = _extract_alignment_words(transcript_text)
    if not words:
        raise ValueError("Transcript alignment vuoto.")

    import torch  # type: ignore
    import torchaudio  # type: ignore

    fa_bundle = cast(Any, _fa_bundle)
    fa_model = cast(Any, _fa_model)
    fa_tokenizer = cast(Any, _fa_tokenizer)
    fa_aligner = cast(Any, _fa_aligner)
    fa_device_used = cast(str, _fa_device_used)

    waveform_np, source_sample_rate = _read_wav_mono_float32(wav_bytes)
    waveform = torch.from_numpy(waveform_np).unsqueeze(0)
    target_sample_rate = int(fa_bundle.sample_rate)
    if source_sample_rate != target_sample_rate:
        waveform = torchaudio.functional.resample(waveform, source_sample_rate, target_sample_rate)

    waveform = waveform.to(dtype=torch.float32)
    waveform_duration_ms = max(1, int(round((waveform.shape[1] / float(target_sample_rate)) * 1000.0)))

    def _run_alignment_once() -> tuple[list[str], list[int]]:
        with torch.inference_mode():
            emissions, _ = fa_model(waveform.to(fa_device_used))
            emissions = torch.log_softmax(emissions, dim=-1)

        emission = emissions[0].detach().cpu()
        token_groups = fa_tokenizer(words)
        word_spans = fa_aligner(emission, token_groups)
        if len(word_spans) != len(words):
            raise RuntimeError("Conteggio word spans non coerente.")

        num_frames = int(emission.shape[0])
        if num_frames <= 0:
            raise RuntimeError("Emission frames vuoti.")

        end_ms: list[int] = []
        last_end_ms = 0
        for spans in word_spans:
            if not spans:
                raise RuntimeError("Word span vuoto.")

            frame_end = int(spans[-1].end)
            word_end_ms = int(round((frame_end / float(num_frames)) * waveform_duration_ms))
            min_end_ms = 1 if not end_ms else last_end_ms
            word_end_ms = min(waveform_duration_ms, max(min_end_ms, word_end_ms))
            end_ms.append(word_end_ms)
            last_end_ms = word_end_ms

        return words, end_ms

    try:
        return _run_alignment_once()
    except Exception as exc:
        if _fa_device_used == "cuda" and _is_cuda_related_error(exc) and _switch_alignment_to_cpu_fallback(exc):
            return _run_alignment_once()
        raise


def _extract_wav_pcm_payload(wav_bytes: bytes) -> tuple[bytes, int, int, int]:
    try:
        with wave.open(io.BytesIO(wav_bytes), "rb") as reader:
            sample_rate = int(reader.getframerate())
            channels = int(reader.getnchannels())
            sample_width = int(reader.getsampwidth())
            frame_count = int(reader.getnframes())
            compression = reader.getcomptype()
            pcm_bytes = reader.readframes(frame_count)
    except Exception as exc:
        raise RuntimeError(f"WAV segment non valido: {exc}") from exc

    if sample_width != 2 or compression != "NONE":
        raise RuntimeError("Il reply TTS segmentato richiede WAV PCM16 lineare.")
    if frame_count <= 0 or not pcm_bytes:
        raise RuntimeError("Payload PCM del reply vuoto.")

    return pcm_bytes, sample_rate, channels, frame_count


def _wav_header(sample_rate: int, channels: int = 1, bits_per_sample: int = 16, data_size: int = 0) -> bytes:
    byte_rate = sample_rate * channels * bits_per_sample // 8
    block_align = channels * bits_per_sample // 8
    return b"".join(
        [
            b"RIFF",
            (36 + data_size).to_bytes(4, byteorder="little", signed=False),
            b"WAVE",
            b"fmt ",
            (16).to_bytes(4, byteorder="little", signed=False),
            (1).to_bytes(2, byteorder="little", signed=False),
            channels.to_bytes(2, byteorder="little", signed=False),
            sample_rate.to_bytes(4, byteorder="little", signed=False),
            byte_rate.to_bytes(4, byteorder="little", signed=False),
            block_align.to_bytes(2, byteorder="little", signed=False),
            bits_per_sample.to_bytes(2, byteorder="little", signed=False),
            b"data",
            data_size.to_bytes(4, byteorder="little", signed=False),
        ]
    )


def _is_short_split_sentence(text: str, max_chars: int) -> bool:
    stripped = (text or "").strip()
    if not stripped:
        return False

    word_count = len(_extract_alignment_words(stripped))
    return word_count <= REPLY_TTS_SHORT_SENTENCE_MAX_WORDS


def _count_split_words(text: str) -> int:
    return len(_extract_alignment_words(text))


def _find_preferred_split_index(text: str, start: int, end: int) -> int:
    punctuation_chars = {",", ";", ":"}
    punctuation_window_start = max(start + 1, end - REPLY_TTS_PUNCTUATION_LOOKBACK_CHARS)

    for index in range(end - 1, punctuation_window_start - 1, -1):
        current = text[index]
        if current in punctuation_chars:
            next_index = index + 1
            if next_index >= len(text) or text[next_index] == " ":
                return index + 1

    split = text.rfind(" ", start, end)
    if split > start:
        return split

    return end


def _log_reply_segments(segments: list[str], *, request_id: Optional[str] = None, max_chars: int = REPLY_TTS_SEGMENT_MAX_CHARS) -> None:
    if not LOG_REPLY_SEGMENTS or not segments:
        return

    request_suffix = f", request_id={request_id}" if request_id else ""
    print(
        f"[{datetime.now().isoformat()}] TTS segments planned (count={len(segments)}, max_chars={max_chars}{request_suffix})",
        flush=True,
    )
    for index, segment in enumerate(segments, start=1):
        print(
            f"[{datetime.now().isoformat()}]   segment {index}/{len(segments)} len={len(segment)} words={_count_split_words(segment)} :: {segment}",
            flush=True,
        )


def _split_text(text: str, max_chars: int = REPLY_TTS_SEGMENT_MAX_CHARS) -> list[str]:
    normalized = re.sub(r"\s+", " ", (text or "").strip())
    if not normalized:
        return []
    if len(normalized) <= max_chars:
        return [normalized]

    raw_parts = [part.strip() for part in re.split(r"(?<=[\.\!\?\:\;])\s+", normalized) if part and part.strip()]
    parts: list[str] = []
    pending_prefix = ""
    for index, part in enumerate(raw_parts):
        current = f"{pending_prefix} {part}".strip() if pending_prefix else part
        has_next = index < len(raw_parts) - 1

        if has_next and _is_short_split_sentence(current, max_chars):
            pending_prefix = current
            continue

        parts.append(current)
        pending_prefix = ""

    if pending_prefix:
        if parts:
            merged = f"{parts[-1]} {pending_prefix}".strip()
            if len(merged) <= max_chars:
                parts[-1] = merged
            else:
                parts.append(pending_prefix)
        else:
            parts.append(pending_prefix)

    chunks: list[str] = []
    current: list[str] = []
    current_len = 0

    for part in parts:
        part = part.strip()
        if not part:
            continue
        if len(part) > max_chars:
            if current:
                chunks.append(" ".join(current))
                current = []
                current_len = 0
            start = 0
            while start < len(part):
                remaining = len(part) - start
                if remaining <= max_chars:
                    chunk = part[start:].strip()
                    if chunk:
                        chunks.append(chunk)
                    break

                end = min(start + max_chars, len(part))
                split = _find_preferred_split_index(part, start, end)

                remaining_after_split = part[split:].strip()
                if remaining_after_split and _count_split_words(remaining_after_split) <= REPLY_TTS_MIN_TAIL_WORDS:
                    backtrack_end = max(start + 1, split - 1)
                    backtrack = _find_preferred_split_index(part, start, backtrack_end)
                    if backtrack > start and backtrack < split:
                        split = backtrack

                chunk = part[start:split].strip()
                if chunk:
                    chunks.append(chunk)
                start = split
                while start < len(part) and part[start] == " ":
                    start += 1
            continue

        if current_len == 0:
            current = [part]
            current_len = len(part)
            continue

        candidate_len = current_len + 1 + len(part)
        if candidate_len <= max_chars:
            current.append(part)
            current_len = candidate_len
            continue

        chunks.append(" ".join(current))
        current = [part]
        current_len = len(part)

    if current:
        chunks.append(" ".join(current))

    return chunks


def _resolve_tts_stream_segments(text: str, reply_segment_max_chars: Optional[int]) -> tuple[int, list[str]]:
    segment_max_chars = REPLY_TTS_SEGMENT_MAX_CHARS
    if reply_segment_max_chars is not None:
        segment_max_chars = max(40, min(400, int(reply_segment_max_chars)))

    effective_max_chars = segment_max_chars
    if len((text or "").strip()) > REPLY_TTS_ALIGNMENT_SAFE_MAX_CHARS:
        effective_max_chars = min(segment_max_chars, REPLY_TTS_ALIGNMENT_SAFE_MAX_CHARS)

    segments = [segment for segment in _split_text(text, max_chars=effective_max_chars) if _clean_tts_text(segment).strip()]
    return effective_max_chars, segments


def _stream_pcm_payload(pcm_bytes: bytes, *, chunk_for_webgl: bool) -> Any:
    if not chunk_for_webgl:
        yield pcm_bytes
        return

    step = max(8192, STREAM_PCM_CHUNK_BYTES)
    for start in range(0, len(pcm_bytes), step):
        yield pcm_bytes[start:start + step]


def _run_startup_warmup() -> None:
    warmup_lang = (WARMUP_LANG or DEFAULT_LANG).strip() or DEFAULT_LANG
    warmup_text = _clean_tts_text(WARMUP_TEXT or "Ciao.")
    if not warmup_text:
        warmup_text = "Ciao."

    warmup_avatar_id = _safe_avatar_id(WARMUP_AVATAR_ID)
    speaker_path = _resolve_speaker_path(warmup_avatar_id)
    if speaker_path is None:
        print("[WARN] Coqui startup warmup skipped.", flush=True)
        return

    started = time.perf_counter()
    _ = _synth_to_wav_bytes(text=warmup_text, language=warmup_lang, speaker_wav_path=speaker_path)
    elapsed = time.perf_counter() - started
    print(
        f"[INFO] Coqui startup warmup complete in {elapsed:.2f}s "
        f"(lang={warmup_lang}, avatar_id={warmup_avatar_id}).",
        flush=True,
    )


# -------------------- Lifespan --------------------


@app.on_event("startup")
def _on_startup() -> None:
    Path(AVATAR_VOICES_DIR).mkdir(parents=True, exist_ok=True)
    (HERE / "voices").mkdir(parents=True, exist_ok=True)
    _avatar_voice_dir(empirical_test_mode=True).mkdir(parents=True, exist_ok=True)
    _voices_root(empirical_test_mode=True).mkdir(parents=True, exist_ok=True)

    if PRELOAD_ON_STARTUP:
        started = time.perf_counter()
        try:
            _ensure_loaded()
            elapsed = time.perf_counter() - started
            print(f"[INFO] Coqui preload complete in {elapsed:.2f}s (device={_device_used}).", flush=True)
        except Exception as exc:
            print(f"[ERR] Coqui preload failed: {exc}", flush=True)
            return

    if PRELOAD_ALIGNMENT_ON_STARTUP:
        started = time.perf_counter()
        try:
            if _tts is None:
                _ensure_loaded()
            _ensure_alignment_loaded()
            elapsed = time.perf_counter() - started
            print(f"[INFO] MMS_FA preload complete in {elapsed:.2f}s (device={_fa_device_used}).", flush=True)
        except Exception as exc:
            print(f"[WARN] MMS_FA preload failed: {exc}", flush=True)

    if WARMUP_ON_STARTUP:
        try:
            if _tts is None:
                _ensure_loaded()
            _run_startup_warmup()
        except Exception as exc:
            print("[WARN] Coqui startup warmup failed.", flush=True)

    return


# -------------------- Percorsi --------------------


@app.get("/health")
def health():
    _prune_incremental_tts_sessions()
    return {
        "ok": True,
        "model": MODEL_NAME,
        "device": _device_used or "not_loaded",
        "default_lang": DEFAULT_LANG,
        "default_speaker_wav": DEFAULT_SPEAKER_WAV,
        "avatar_voices_dir": AVATAR_VOICES_DIR,
        "empirical_avatar_voices_dir": str(_avatar_voice_dir(empirical_test_mode=True)),
        "incremental_timing_sessions": len(_incremental_tts_sessions),
        "incremental_timing_session_ttl_seconds": INCREMENTAL_TIMING_SESSION_TTL_SECONDS,
        "incremental_timing_completed_ttl_seconds": INCREMENTAL_TIMING_COMPLETED_TTL_SECONDS,
        "incremental_timing_max_sessions": INCREMENTAL_TIMING_MAX_SESSIONS,
        "incremental_timing_pruned_ttl_total": int(_incremental_tts_session_stats.pruned_ttl),
        "incremental_timing_evicted_capacity_total": int(_incremental_tts_session_stats.evicted_capacity),
        "ts": int(time.time()),
    }


@app.get("/tts_timing")
def tts_timing(request_id: str):
    _prune_incremental_tts_sessions()
    safe_request_id = _coerce_tts_request_id(request_id)
    session = _incremental_tts_sessions.get(safe_request_id)
    if session is None:
        return {
            "ok": True,
            "request_id": safe_request_id,
            "words": [],
            "word_end_ms": [],
            "segment_end_ms": [],
            "complete": False,
            "error": None,
        }

    return {
        "ok": True,
        "request_id": safe_request_id,
        "words": list(session.get("words", [])),
        "word_end_ms": list(session.get("word_end_ms", [])),
        "segment_end_ms": list(session.get("segment_end_ms", [])),
        "complete": bool(session.get("complete", False)),
        "error": session.get("error"),
    }


@app.post("/set_avatar_voice")
async def set_avatar_voice(
    avatar_id: str = Form(...),
    speaker_wav: UploadFile = File(...),
    empirical_test_mode: bool = Form(False),
):
    safe = _safe_avatar_id(avatar_id)
    if not speaker_wav.filename:
        raise HTTPException(status_code=400, detail="File speaker_wav mancante.")

    raw_data = await speaker_wav.read()
    if not raw_data or len(raw_data) < 512:
        raise HTTPException(status_code=400, detail="File speaker_wav vuoto o troppo piccolo.")
    try:
        data = _prepare_reference_wav_bytes(raw_data)
    except ValueError as exc:
        raise HTTPException(status_code=400, detail=str(exc))

    out_path = _avatar_voice_path(safe, empirical_test_mode)
    out_path.parent.mkdir(parents=True, exist_ok=True)
    out_path.write_bytes(data)

    wait_phrase_generation_error: Optional[str] = None
    try:
        _generate_wait_phrase_files(
            avatar_id=safe,
            language=DEFAULT_LANG,
            empirical_test_mode=empirical_test_mode,
        )
    except Exception as exc:
        wait_phrase_generation_error = str(exc)
        print(f"[WARN] Wait phrase preload failed for avatar '{safe}': {exc}", flush=True)

    return {
        "ok": True,
        "avatar_id": safe,
        "path": str(out_path),
        "bytes": len(data),
        "empirical_test_mode": empirical_test_mode,
        "wait_phrases_ready": wait_phrase_generation_error is None,
    }


@app.get("/avatar_voice")
def avatar_voice_info(avatar_id: str, empirical_test_mode: bool = False):
    safe = _safe_avatar_id(avatar_id)
    p = _avatar_voice_path(safe, empirical_test_mode)
    return {
        "avatar_id": safe,
        "exists": p.exists(),
        "bytes": p.stat().st_size if p.exists() else 0,
        "path": str(p),
        "empirical_test_mode": empirical_test_mode,
    }


@app.delete("/avatar_voice")
def delete_avatar_voice(avatar_id: str, empirical_test_mode: bool = False):
    safe = _safe_avatar_id(avatar_id)
    avatar_dir = _avatar_voice_dir(empirical_test_mode) / safe
    if avatar_dir.exists():
        try:
            import shutil
            shutil.rmtree(avatar_dir)
        except Exception as e:
            raise HTTPException(status_code=500, detail=f"Impossibile cancellare: {e}")
    return {"ok": True, "avatar_id": safe, "empirical_test_mode": empirical_test_mode}


@app.post("/avatar_voice_backup")
def avatar_voice_backup(
    avatar_id: str = Form(...),
    empirical_test_mode: bool = Form(False),
):
    safe = _safe_avatar_id(avatar_id)
    avatar_dir = _avatar_voice_dir(empirical_test_mode) / safe
    snapshot_dir = _avatar_voice_snapshot_dir(safe, empirical_test_mode)

    if not _file_has_content(_avatar_voice_path(safe, empirical_test_mode)):
        return {"ok": True, "avatar_id": safe, "backed_up": False, "empirical_test_mode": empirical_test_mode}

    try:
        if snapshot_dir.exists():
            shutil.rmtree(snapshot_dir)
        snapshot_dir.parent.mkdir(parents=True, exist_ok=True)
        shutil.copytree(avatar_dir, snapshot_dir)
    except Exception as exc:
        raise HTTPException(status_code=500, detail=f"Impossibile creare backup voce: {exc}")

    return {"ok": True, "avatar_id": safe, "backed_up": True, "empirical_test_mode": empirical_test_mode}


@app.post("/avatar_voice_restore")
def avatar_voice_restore(
    avatar_id: str = Form(...),
    empirical_test_mode: bool = Form(False),
):
    safe = _safe_avatar_id(avatar_id)
    avatar_dir = _avatar_voice_dir(empirical_test_mode) / safe
    snapshot_dir = _avatar_voice_snapshot_dir(safe, empirical_test_mode)

    if not snapshot_dir.exists():
        return {"ok": True, "avatar_id": safe, "restored": False, "empirical_test_mode": empirical_test_mode}

    try:
        if avatar_dir.exists():
            shutil.rmtree(avatar_dir)
        avatar_dir.parent.mkdir(parents=True, exist_ok=True)
        shutil.copytree(snapshot_dir, avatar_dir)
    except Exception as exc:
        raise HTTPException(status_code=500, detail=f"Impossibile ripristinare voce: {exc}")

    return {"ok": True, "avatar_id": safe, "restored": True, "empirical_test_mode": empirical_test_mode}


@app.get(
    "/wait_phrase",
    responses={200: {"content": {"audio/wav": {}}}},
)
def wait_phrase(avatar_id: str, name: str, empirical_test_mode: bool = False):
    safe = _safe_avatar_id(avatar_id)
    key = (name or "").strip().lower()
    if not key:
        raise HTTPException(status_code=400, detail="Parametro 'name' mancante.")

    allowed = {k for k, _ in WAIT_PHRASES}
    if key not in allowed:
        raise HTTPException(status_code=400, detail="Parametro 'name' non valido.")

    path = _resolve_wait_phrase_path(safe, key, empirical_test_mode)
    if not _file_has_content(path):
        raise HTTPException(status_code=404, detail="Wait phrase non trovata.")

    return FileResponse(path, media_type="audio/wav", filename=path.name)


@app.post(
    "/tts",
    responses={200: {"content": {"audio/wav": {}}}},
)
async def tts(
    text: str = Form(...),
    avatar_id: str = Form("default"),
    language: str = Form(DEFAULT_LANG),
    speaker_wav: Optional[UploadFile] = File(None),
    save_voice: bool = Form(False),
    empirical_test_mode: bool = Form(False),
):
    _ensure_loaded()

    safe = _safe_avatar_id(avatar_id)
    language = _normalize_language(language)
    speaker_path, tmp_path, speaker_source = await _prepare_request_speaker(
        safe_avatar_id=safe,
        speaker_wav=speaker_wav,
        save_voice=save_voice,
        empirical_test_mode=empirical_test_mode,
    )

    if speaker_path is None:
        raise HTTPException(
            status_code=400,
            detail="Nessun riferimento vocale disponibile. Usa /set_avatar_voice o passa speaker_wav.",
        )

    try:
        wav_bytes = _synth_to_wav_bytes(text=text, language=language, speaker_wav_path=speaker_path)
    except Exception as e:
        raise HTTPException(status_code=500, detail=f"TTS synthesis error: {e}")
    finally:
        _cleanup_temp_path(tmp_path)

    filename = f"{safe}_{int(time.time())}.wav"
    headers = {
        "Content-Disposition": f'attachment; filename="{filename}"',
        "X-Avatar-Id": safe,
        "X-Coqui-Lang": language,
        "X-Speaker-Source": speaker_source,
        "X-Empirical-Test-Mode": "true" if empirical_test_mode else "false",
    }
    return Response(content=wav_bytes, media_type="audio/wav", headers=headers)


@app.post(
    "/tts_stream",
    responses={200: {"content": {"audio/wav": {}}}},
)
async def tts_stream(
    text: str = Form(...),
    avatar_id: str = Form("default"),
    language: str = Form(DEFAULT_LANG),
    speaker_wav: Optional[UploadFile] = File(None),
    save_voice: bool = Form(False),
    reply_segment_max_chars: Optional[int] = Form(None),
    request_id: Optional[str] = Form(None),
    client_platform: Optional[str] = Form(None),
    empirical_test_mode: bool = Form(False),
):
    _tts_queue_stats.total_requests += 1
    print(f"[{datetime.now().isoformat()}] TTS request #{_tts_queue_stats.total_requests} received")

    client_platform_norm = (client_platform or "").strip().lower()
    is_webgl_client = client_platform_norm in {"webgl", "web", "unity_webgl"}

    raw_request_id = (request_id or "").strip()
    incremental_timing_enabled = bool(raw_request_id) and not is_webgl_client
    safe_request_id = _coerce_tts_request_id(raw_request_id) if incremental_timing_enabled else ""
    if incremental_timing_enabled:
        _set_incremental_tts_session(safe_request_id, create=True)

    _ensure_loaded()

    safe = _safe_avatar_id(avatar_id)
    language = _normalize_language(language)
    speaker_path, tmp_path, speaker_source = await _prepare_request_speaker(
        safe_avatar_id=safe,
        speaker_wav=speaker_wav,
        save_voice=save_voice,
        empirical_test_mode=empirical_test_mode,
    )

    if speaker_path is None:
        if incremental_timing_enabled:
            _set_incremental_tts_session(safe_request_id, complete=True, error="Nessun riferimento vocale disponibile.")
        raise HTTPException(
            status_code=400,
            detail="Nessun riferimento vocale disponibile. Usa /set_avatar_voice o passa speaker_wav.",
        )

    if not text.strip():
        _cleanup_temp_path(tmp_path)
        if incremental_timing_enabled:
            _set_incremental_tts_session(safe_request_id, complete=True, error="Text vuoto.")
        raise HTTPException(status_code=400, detail="Invalid input: Text vuoto.")

    effective_max_chars, segments = _resolve_tts_stream_segments(text, reply_segment_max_chars)
    if not segments:
        _cleanup_temp_path(tmp_path)
        if incremental_timing_enabled:
            _set_incremental_tts_session(safe_request_id, complete=True, error="Reply segmentato vuoto.")
        raise HTTPException(status_code=400, detail="Invalid input: Reply segmentato vuoto.")
    _log_reply_segments(
        segments,
        request_id=safe_request_id if incremental_timing_enabled else None,
        max_chars=effective_max_chars)

    async def _stream_progressive():
        cumulative_words: list[str] = []
        cumulative_word_end_ms: list[int] = []
        cumulative_segment_end_ms: list[int] = []
        cumulative_ms = 0
        sample_rate = OUTPUT_SAMPLE_RATE
        channels = 1
        header_sent = False

        try:
            for segment_index, segment_text in enumerate(segments, start=1):
                try:
                    await asyncio.wait_for(_tts_semaphore.acquire(), timeout=10.0)
                except asyncio.TimeoutError as exc:
                    _tts_queue_stats.rejected_429_count += 1
                    if incremental_timing_enabled:
                        _set_incremental_tts_session(safe_request_id, complete=True, error="Timeout waiting for TTS synthesis slot.")
                    raise RuntimeError("Timeout waiting for TTS synthesis slot.") from exc

                try:
                    _tts_queue_stats.active_now += 1
                    print(f"[{datetime.now().isoformat()}] TTS segment {segment_index}/{len(segments)} synthesis STARTED")
                    wav_bytes = _synth_to_wav_bytes(text=segment_text, language=language, speaker_wav_path=speaker_path)
                finally:
                    _tts_queue_stats.active_now -= 1
                    _tts_semaphore.release()

                pcm_bytes, segment_rate, segment_channels, frame_count = _extract_wav_pcm_payload(wav_bytes)
                if not header_sent:
                    sample_rate = segment_rate
                    channels = segment_channels
                    yield _wav_header(sample_rate=sample_rate, channels=channels, bits_per_sample=16, data_size=0)
                    header_sent = True
                elif sample_rate != segment_rate or channels != segment_channels:
                    raise RuntimeError("I segmenti reply TTS hanno formato audio incoerente.")

                segment_duration_ms = max(1, int(round((frame_count / float(segment_rate)) * 1000.0)))
                if incremental_timing_enabled:
                    segment_words: list[str] = []
                    segment_local_end_ms: list[int] = []

                    try:
                        segment_words, segment_local_end_ms = _align_words_from_wav_bytes(wav_bytes, segment_text)
                    except Exception as exc:
                        print(f"[WARN] Incremental alignment fallback on segment {segment_index}: {exc}", flush=True)
                        segment_words, segment_local_end_ms = _build_segment_duration_fallback_timing(
                            segmented_text_durations=[(segment_text, segment_duration_ms)],
                            expected_text=segment_text,
                        )

                    if segment_words and len(segment_words) == len(segment_local_end_ms):
                        previous_word_end = cumulative_word_end_ms[-1] if cumulative_word_end_ms else 0
                        for word, local_end_ms in zip(segment_words, segment_local_end_ms):
                            absolute_end_ms = cumulative_ms + max(1, int(local_end_ms))
                            absolute_end_ms = max(previous_word_end + 1 if previous_word_end > 0 else 1, absolute_end_ms)
                            absolute_end_ms = min(cumulative_ms + segment_duration_ms, absolute_end_ms)
                            cumulative_words.append(word)
                            cumulative_word_end_ms.append(absolute_end_ms)
                            previous_word_end = absolute_end_ms

                    cumulative_ms += segment_duration_ms
                    cumulative_segment_end_ms.append(cumulative_ms)
                    _set_incremental_tts_session(
                        safe_request_id,
                        words=cumulative_words,
                        word_end_ms=cumulative_word_end_ms,
                        segment_end_ms=cumulative_segment_end_ms,
                    )

                for pcm_chunk in _stream_pcm_payload(
                    pcm_bytes,
                    chunk_for_webgl=is_webgl_client and STREAM_WEBGL_FORCE_CHUNKING,
                ):
                    yield pcm_chunk

                print(f"[{datetime.now().isoformat()}] TTS segment {segment_index}/{len(segments)} transmitted")

            if incremental_timing_enabled:
                _set_incremental_tts_session(safe_request_id, complete=True)
            print(f"[{datetime.now().isoformat()}] TTS progressive stream COMPLETE")
        except Exception as exc:
            if incremental_timing_enabled:
                _set_incremental_tts_session(safe_request_id, complete=True, error=str(exc))
            print(f"[{datetime.now().isoformat()}] TTS progressive stream FAILED: {exc}")
            raise
        finally:
            _cleanup_temp_path(tmp_path)

    headers = {
        "X-Avatar-Id": safe,
        "X-Coqui-Lang": language,
        "X-Audio-Rate": str(OUTPUT_SAMPLE_RATE),
        "X-Audio-Format": "wav",
        "X-Speaker-Source": speaker_source,
        "X-Client-Platform": client_platform_norm or "unknown",
        "X-Empirical-Test-Mode": "true" if empirical_test_mode else "false",
        "Cache-Control": "no-cache, no-transform",
        "Pragma": "no-cache",
        "X-Accel-Buffering": "no",
    }
    if incremental_timing_enabled:
        headers["X-TTS-Request-Id"] = safe_request_id
    print(
        f"[{datetime.now().isoformat()}] TTS progressive streaming started "
        f"(request_id={safe_request_id if incremental_timing_enabled else 'disabled'})")
    return StreamingResponse(_stream_progressive(), media_type="audio/wav", headers=headers)


@app.post("/tts_json")
async def tts_json(
    text: str = Form(...),
    avatar_id: str = Form("default"),
    language: str = Form(DEFAULT_LANG),
    speaker_wav: Optional[UploadFile] = File(None),
    save_voice: bool = Form(False),
    empirical_test_mode: bool = Form(False),
):
    resp = await tts(
        text=text,
        avatar_id=avatar_id,
        language=language,
        speaker_wav=speaker_wav,
        save_voice=save_voice,
        empirical_test_mode=empirical_test_mode,
    )
    b64 = base64.b64encode(resp.body).decode("ascii")  # type: ignore[attr-defined]
    return JSONResponse(
        {
            "ok": True,
            "avatar_id": _safe_avatar_id(avatar_id),
            "language": (language or DEFAULT_LANG).strip() or DEFAULT_LANG,
            "format": "wav",
            "sample_rate": OUTPUT_SAMPLE_RATE,
            "empirical_test_mode": empirical_test_mode,
            "audio_base64": b64,
        }
    )
