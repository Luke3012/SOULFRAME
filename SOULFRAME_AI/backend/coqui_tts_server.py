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

import base64
import io
import os
import re
import struct
import tempfile
import time
from pathlib import Path
from typing import Optional

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

# Se vuoi forzare: cpu | cuda
FORCE_DEVICE = os.getenv("COQUI_TTS_DEVICE", "").strip().lower()

# XTTS spesso lavora bene a 24k
OUTPUT_SAMPLE_RATE = int(os.getenv("COQUI_OUTPUT_SR", "24000"))

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
WARMUP_ON_STARTUP = os.getenv("COQUI_WARMUP_ON_STARTUP", "1").strip().lower() in {"1", "true", "yes", "on"}
WARMUP_TEXT = os.getenv("COQUI_WARMUP_TEXT", "Ciao.")
WARMUP_LANG = os.getenv("COQUI_WARMUP_LANG", DEFAULT_LANG)
WARMUP_AVATAR_ID = os.getenv("COQUI_WARMUP_AVATAR_ID", "default")

REFERENCE_TRIM_SILENCE = os.getenv("COQUI_REFERENCE_TRIM_SILENCE", "1").strip().lower() in {"1", "true", "yes", "on"}
REFERENCE_SILENCE_THRESHOLD = float(os.getenv("COQUI_REFERENCE_SILENCE_THRESHOLD", "0.01"))
REFERENCE_TRIM_PAD_SEC = float(os.getenv("COQUI_REFERENCE_TRIM_PAD_SEC", "0.12"))
REFERENCE_MAX_SEC = float(os.getenv("COQUI_REFERENCE_MAX_SEC", "14"))

# -------------------- Applicazione --------------------

app = FastAPI(title="SOULFRAME Coqui TTS", version="1.2.0")

app.add_middleware(
    CORSMiddleware,
    allow_origins=["*"],
    allow_credentials=False,
    allow_methods=["*"],
    allow_headers=["*"],
)

# -------------------- Variabili globali --------------------

_tts = None
_device_used = None

# -------------------- Helper --------------------


_AVATAR_ID_RX = re.compile(r"[^a-zA-Z0-9_\-]+")

WAIT_PHRASES: list[tuple[str, str]] = [
    ("hm", "Hm"),
    ("beh", "Beh"),
    ("aspetta", "Aspetta"),
    ("si", "Sì"),
    ("un_secondo", "Un secondo"),
]


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

    max_sec = max(1.0, float(REFERENCE_MAX_SEC))
    max_samples = int(max_sec * max(1, int(sr)))
    if wav.size > max_samples:
        wav = wav[:max_samples]

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


def _avatar_voice_path(avatar_id: str) -> Path:
    safe = _safe_avatar_id(avatar_id)
    return Path(AVATAR_VOICES_DIR) / safe / "reference.wav"


def _wait_phrase_path(avatar_id: str, key: str) -> Path:
    safe = _safe_avatar_id(avatar_id)
    return Path(AVATAR_VOICES_DIR) / safe / f"wait_{key}.wav"


def _legacy_wait_phrase_path(avatar_id: str, key: str) -> Path:
    safe = _safe_avatar_id(avatar_id)
    return Path(LEGACY_AVATAR_VOICES_DIR) / safe / f"wait_{key}.wav"


def _resolve_wait_phrase_path(avatar_id: str, key: str) -> Path:
    primary = _wait_phrase_path(avatar_id, key)
    legacy = _legacy_wait_phrase_path(avatar_id, key)

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


def _resolve_speaker_path(safe_avatar_id: str) -> Optional[str]:
    avatar_ref = _avatar_voice_path(safe_avatar_id)
    if _file_has_content(avatar_ref):
        return str(avatar_ref)

    default_ref = Path(DEFAULT_SPEAKER_WAV)
    if _file_has_content(default_ref):
        return str(default_ref)

    return None


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
        print(f"[WARN] Coqui switched to CPU fallback: {reason}", flush=True)
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


def _synth_to_pcm16_bytes(text: str, language: str, speaker_wav_path: str) -> bytes:
    """Genera PCM16 mono (senza header WAV), utile per streaming chunk-by-chunk."""
    text = _clean_tts_text(text)
    if not text.strip():
        raise ValueError("Text vuoto.")

    wav = _tts_generate(text=text, language=language, speaker_wav_path=speaker_wav_path)

    if wav is None:
        raise RuntimeError("TTS ha restituito None.")

    wav = np.asarray(wav, dtype=np.float32).reshape(-1)
    if wav.size < 10:
        raise RuntimeError("Audio generato vuoto (wav troppo corto).")

    wav = np.clip(wav, -1.0, 1.0)
    pcm16 = (wav * 32767.0).astype(np.int16)
    return pcm16.tobytes()


def _wav_header(sample_rate: int, channels: int = 1, bits_per_sample: int = 16, data_size: int = 0) -> bytes:
    byte_rate = sample_rate * channels * bits_per_sample // 8
    block_align = channels * bits_per_sample // 8
    return b"".join(
        [
            b"RIFF",
            struct.pack("<I", 36 + data_size),
            b"WAVE",
            b"fmt ",
            struct.pack("<IHHIIHH", 16, 1, channels, sample_rate, byte_rate, block_align, bits_per_sample),
            b"data",
            struct.pack("<I", data_size),
        ]
    )


def _iter_pcm_stream(
    chunks: list[str],
    language: str,
    speaker_wav_path: str,
    sample_rate: int = OUTPUT_SAMPLE_RATE,
):
    yield _wav_header(sample_rate=sample_rate, channels=1, bits_per_sample=16, data_size=0)
    emitted_audio = False

    for chunk in chunks:
        if not chunk:
            continue
        chunk = _clean_tts_text(chunk)
        if not chunk:
            continue

        try:
            pcm = _synth_to_pcm16_bytes(text=chunk, language=language, speaker_wav_path=speaker_wav_path)
            if not pcm:
                continue
            emitted_audio = True
            yield pcm
        except Exception as e:
            # Non interrompere l'intero stream per un singolo chunk fallito.
            print(f"[WARN] tts_stream chunk skipped: {e}", flush=True)
            continue

    if not emitted_audio:
        # Restituisce un piccolo buffer di silenzio per mantenere stream WAV valido.
        silence = np.zeros(int(sample_rate * 0.15), dtype=np.int16)
        yield silence.tobytes()


def _split_text(text: str, max_chars: int = 220) -> list[str]:
    normalized = re.sub(r"\s+", " ", (text or "").strip())
    if not normalized:
        return []
    if len(normalized) <= max_chars:
        return [normalized]

    parts = re.split(r"(?<=[\.\!\?\:\;])\s+", normalized)
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
                end = min(start + max_chars, len(part))
                split = part.rfind(" ", start, end)
                if split <= start:
                    split = end
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

        if current_len + 1 + len(part) <= max_chars:
            current.append(part)
            current_len += 1 + len(part)
            continue

        chunks.append(" ".join(current))
        current = [part]
        current_len = len(part)

    if current:
        chunks.append(" ".join(current))

    return chunks


def _run_startup_warmup() -> None:
    warmup_lang = (WARMUP_LANG or DEFAULT_LANG).strip() or DEFAULT_LANG
    warmup_text = _clean_tts_text(WARMUP_TEXT or "Ciao.")
    if not warmup_text:
        warmup_text = "Ciao."

    warmup_avatar_id = _safe_avatar_id(WARMUP_AVATAR_ID)
    speaker_path = _resolve_speaker_path(warmup_avatar_id)
    if speaker_path is None:
        print("[WARN] Coqui startup warmup skipped: no speaker reference found.", flush=True)
        return

    started = time.perf_counter()
    _ = _synth_to_pcm16_bytes(text=warmup_text, language=warmup_lang, speaker_wav_path=speaker_path)
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

    if PRELOAD_ON_STARTUP:
        started = time.perf_counter()
        try:
            _ensure_loaded()
            elapsed = time.perf_counter() - started
            print(f"[INFO] Coqui preload complete in {elapsed:.2f}s (device={_device_used}).", flush=True)
        except Exception as exc:
            print(f"[ERR] Coqui preload failed: {exc}", flush=True)
            return

    if WARMUP_ON_STARTUP:
        try:
            if _tts is None:
                _ensure_loaded()
            _run_startup_warmup()
        except Exception as exc:
            print(f"[WARN] Coqui startup warmup failed: {exc}", flush=True)

    return


# -------------------- Percorsi --------------------


@app.get("/health")
def health():
    return {
        "ok": True,
        "model": MODEL_NAME,
        "device": _device_used or "not_loaded",
        "default_lang": DEFAULT_LANG,
        "default_speaker_wav": DEFAULT_SPEAKER_WAV,
        "avatar_voices_dir": AVATAR_VOICES_DIR,
        "ts": int(time.time()),
    }


@app.post("/set_avatar_voice")
async def set_avatar_voice(
    avatar_id: str = Form(...),
    speaker_wav: UploadFile = File(...),
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

    out_path = _avatar_voice_path(safe)
    out_path.parent.mkdir(parents=True, exist_ok=True)
    out_path.write_bytes(data)

    return {"ok": True, "avatar_id": safe, "path": str(out_path), "bytes": len(data)}


@app.get("/avatar_voice")
def avatar_voice_info(avatar_id: str):
    safe = _safe_avatar_id(avatar_id)
    p = _avatar_voice_path(safe)
    return {
        "avatar_id": safe,
        "exists": p.exists(),
        "bytes": p.stat().st_size if p.exists() else 0,
        "path": str(p),
    }


@app.delete("/avatar_voice")
def delete_avatar_voice(avatar_id: str):
    safe = _safe_avatar_id(avatar_id)
    avatar_dir = Path(AVATAR_VOICES_DIR) / safe
    if avatar_dir.exists():
        try:
            import shutil
            shutil.rmtree(avatar_dir)
        except Exception as e:
            raise HTTPException(status_code=500, detail=f"Impossibile cancellare: {e}")
    return {"ok": True, "avatar_id": safe}


@app.post("/generate_wait_phrases")
def generate_wait_phrases(
    avatar_id: str = Form(...),
    language: str = Form(DEFAULT_LANG),
):
    _ensure_loaded()

    safe = _safe_avatar_id(avatar_id)
    language = (language or DEFAULT_LANG).strip() or DEFAULT_LANG

    speaker_path = _resolve_speaker_path(safe)
    if speaker_path is None:
        raise HTTPException(
            status_code=400,
            detail="Nessun riferimento vocale disponibile. Usa /set_avatar_voice prima.",
        )

    output_dir = Path(AVATAR_VOICES_DIR) / safe
    output_dir.mkdir(parents=True, exist_ok=True)

    written: list[str] = []
    for key, phrase in WAIT_PHRASES:
        try:
            wav_bytes = _synth_to_wav_bytes(text=phrase, language=language, speaker_wav_path=speaker_path)
        except Exception as e:
            raise HTTPException(status_code=500, detail=f"Errore TTS per '{phrase}': {e}")

        out_path = output_dir / f"wait_{key}.wav"
        out_path.write_bytes(wav_bytes)
        written.append(out_path.name)

    return {
        "ok": True,
        "avatar_id": safe,
        "count": len(written),
        "files": written,
    }


@app.get(
    "/wait_phrase",
    responses={200: {"content": {"audio/wav": {}}}},
)
def wait_phrase(avatar_id: str, name: str):
    _ensure_loaded()

    safe = _safe_avatar_id(avatar_id)
    key = (name or "").strip().lower()
    if not key:
        raise HTTPException(status_code=400, detail="Parametro 'name' mancante.")

    allowed = {k for k, _ in WAIT_PHRASES}
    if key not in allowed:
        raise HTTPException(status_code=400, detail="Parametro 'name' non valido.")

    path = _resolve_wait_phrase_path(safe, key)
    if not _file_has_content(path):
        speaker_path = _resolve_speaker_path(safe)
        if speaker_path is None:
            raise HTTPException(status_code=404, detail="Wait phrase non trovata.")

        phrase_map = {k: v for k, v in WAIT_PHRASES}
        phrase = phrase_map.get(key, "")
        if not phrase:
            raise HTTPException(status_code=400, detail="Parametro 'name' non valido.")

        try:
            wav_bytes = _synth_to_wav_bytes(text=phrase, language=DEFAULT_LANG, speaker_wav_path=speaker_path)
        except Exception as e:
            raise HTTPException(status_code=500, detail=f"Errore generazione wait phrase: {e}")

        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_bytes(wav_bytes)

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
):
    _ensure_loaded()

    safe = _safe_avatar_id(avatar_id)
    language = (language or DEFAULT_LANG).strip() or DEFAULT_LANG

    speaker_path: Optional[str] = None
    tmp_path: Optional[Path] = None
    speaker_source = "none"

    # 1) file caricato
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
                out_path = _avatar_voice_path(safe)
                out_path.parent.mkdir(parents=True, exist_ok=True)
                out_path.write_bytes(data)

    # 2) voce salvata avatar
    if speaker_path is None:
        avatar_ref = _avatar_voice_path(safe)
        if _file_has_content(avatar_ref):
            speaker_path = str(avatar_ref)
            speaker_source = "avatar"

    # 3) fallback di default
    if speaker_path is None:
        default_ref = Path(DEFAULT_SPEAKER_WAV)
        if _file_has_content(default_ref):
            speaker_path = str(default_ref)
            speaker_source = "default"

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
        if tmp_path is not None:
            try:
                tmp_path.unlink(missing_ok=True)
            except Exception:
                pass

    filename = f"{safe}_{int(time.time())}.wav"
    headers = {
        "Content-Disposition": f'attachment; filename="{filename}"',
        "X-Avatar-Id": safe,
        "X-Coqui-Lang": language,
        "X-Speaker-Source": speaker_source,
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
    split_sentences: bool = Form(True),
    max_chunk_chars: int = Form(220),
):
    _ensure_loaded()

    safe = _safe_avatar_id(avatar_id)
    language = (language or DEFAULT_LANG).strip() or DEFAULT_LANG

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
                out_path = _avatar_voice_path(safe)
                out_path.parent.mkdir(parents=True, exist_ok=True)
                out_path.write_bytes(data)

    if speaker_path is None:
        avatar_ref = _avatar_voice_path(safe)
        if _file_has_content(avatar_ref):
            speaker_path = str(avatar_ref)
            speaker_source = "avatar"

    if speaker_path is None:
        default_ref = Path(DEFAULT_SPEAKER_WAV)
        if _file_has_content(default_ref):
            speaker_path = str(default_ref)
            speaker_source = "default"

    if speaker_path is None:
        raise HTTPException(
            status_code=400,
            detail="Nessun riferimento vocale disponibile. Usa /set_avatar_voice o passa speaker_wav.",
        )

    try:
        if not text.strip():
            raise ValueError("Text vuoto.")
        chunks = _split_text(text, max_chars=max(40, int(max_chunk_chars))) if split_sentences else [text]
    except Exception as e:
        raise HTTPException(status_code=400, detail=f"Invalid input: {e}")

    def _cleanup():
        if tmp_path is not None:
            try:
                tmp_path.unlink(missing_ok=True)
            except Exception:
                pass

    def _stream():
        try:
            yield from _iter_pcm_stream(chunks=chunks, language=language, speaker_wav_path=speaker_path)
        finally:
            _cleanup()

    headers = {
        "X-Avatar-Id": safe,
        "X-Coqui-Lang": language,
        "X-Audio-Rate": str(OUTPUT_SAMPLE_RATE),
        "X-Audio-Format": "pcm_s16le",
        "X-Speaker-Source": speaker_source,
    }
    return StreamingResponse(_stream(), media_type="audio/wav", headers=headers)


@app.post("/tts_json")
async def tts_json(
    text: str = Form(...),
    avatar_id: str = Form("default"),
    language: str = Form(DEFAULT_LANG),
    speaker_wav: Optional[UploadFile] = File(None),
    save_voice: bool = Form(False),
):
    resp = await tts(text=text, avatar_id=avatar_id, language=language, speaker_wav=speaker_wav, save_voice=save_voice)
    b64 = base64.b64encode(resp.body).decode("ascii")  # type: ignore[attr-defined]
    return JSONResponse(
        {
            "ok": True,
            "avatar_id": _safe_avatar_id(avatar_id),
            "language": (language or DEFAULT_LANG).strip() or DEFAULT_LANG,
            "format": "wav",
            "sample_rate": OUTPUT_SAMPLE_RATE,
            "audio_base64": b64,
        }
    )
