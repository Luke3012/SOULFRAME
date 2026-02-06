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

# -------------------- Config --------------------

HERE = Path(__file__).resolve().parent

MODEL_NAME = os.getenv("COQUI_TTS_MODEL", "tts_models/multilingual/multi-dataset/xtts_v2")
DEFAULT_LANG = os.getenv("COQUI_LANG", "it")

DEFAULT_SPEAKER_WAV = os.getenv("COQUI_DEFAULT_SPEAKER_WAV", str(HERE / "voices" / "default.wav"))
AVATAR_VOICES_DIR = os.getenv("COQUI_AVATAR_VOICES_DIR", str(HERE / "voices" / "avatars"))

# Se vuoi forzare: cpu | cuda
FORCE_DEVICE = os.getenv("COQUI_TTS_DEVICE", "").strip().lower()

# XTTS spesso lavora bene a 24k
OUTPUT_SAMPLE_RATE = int(os.getenv("COQUI_OUTPUT_SR", "24000"))

# -------------------- App --------------------

app = FastAPI(title="SOULFRAME Coqui TTS", version="1.2.0")

app.add_middleware(
    CORSMiddleware,
    allow_origins=["*"],
    allow_credentials=False,
    allow_methods=["*"],
    allow_headers=["*"],
)

# -------------------- Globals --------------------

_tts = None
_device_used = None

# -------------------- Helpers --------------------


_AVATAR_ID_RX = re.compile(r"[^a-zA-Z0-9_\-]+")

WAIT_PHRASES: list[tuple[str, str]] = [
    ("hm", "Hm"),
    ("beh", "Beh"),
    ("aspetta", "Aspetta"),
    ("si", "Sì"),
    ("un_secondo", "Un secondo"),
]


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


def _ensure_loaded() -> None:
    global _tts, _device_used
    if _tts is not None:
        return

    # Import pesanti solo qui
    import torch  # type: ignore
    from TTS.api import TTS  # type: ignore

    if FORCE_DEVICE in ("cpu", "cuda"):
        use_cuda = FORCE_DEVICE == "cuda"
    else:
        use_cuda = bool(torch.cuda.is_available())

    _device_used = "cuda" if use_cuda else "cpu"
    _tts = TTS(MODEL_NAME)
    _tts.to(_device_used)


def _synth_to_wav_bytes(
    text: str,
    language: str,
    speaker_wav_path: str,
    sample_rate: int = OUTPUT_SAMPLE_RATE,
) -> bytes:
    global _tts
    if not text.strip():
        raise ValueError("Text vuoto.")

    wav = _tts.tts(text=text, speaker_wav=speaker_wav_path, language=language)  # type: ignore[attr-defined]
    if wav is None:
        raise RuntimeError("TTS ha restituito None.")

    wav = np.asarray(wav, dtype=np.float32).reshape(-1)
    if wav.size < 10:
        raise RuntimeError("Audio generato vuoto (wav troppo corto).")

    buf = io.BytesIO()
    sf.write(buf, wav, samplerate=sample_rate, format="WAV", subtype="PCM_16")
    return buf.getvalue()


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

    for chunk in chunks:
        if not chunk:
            continue
        wav = _tts.tts(text=chunk, speaker_wav=speaker_wav_path, language=language)  # type: ignore[attr-defined]
        if wav is None:
            continue

        wav = np.asarray(wav, dtype=np.float32).reshape(-1)
        if wav.size < 10:
            continue

        wav = np.clip(wav, -1.0, 1.0)
        pcm16 = (wav * 32767.0).astype(np.int16)
        yield pcm16.tobytes()


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


# -------------------- Lifespan --------------------


@app.on_event("startup")
def _on_startup() -> None:
    Path(AVATAR_VOICES_DIR).mkdir(parents=True, exist_ok=True)
    (HERE / "voices").mkdir(parents=True, exist_ok=True)
    return


# -------------------- Routes --------------------


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

    data = await speaker_wav.read()
    if not data or len(data) < 512:
        raise HTTPException(status_code=400, detail="File speaker_wav vuoto o troppo piccolo.")

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
    safe = _safe_avatar_id(avatar_id)
    key = (name or "").strip().lower()
    if not key:
        raise HTTPException(status_code=400, detail="Parametro 'name' mancante.")

    allowed = {k for k, _ in WAIT_PHRASES}
    if key not in allowed:
        raise HTTPException(status_code=400, detail="Parametro 'name' non valido.")

    path = _wait_phrase_path(safe, key)
    if not path.exists():
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
):
    _ensure_loaded()

    safe = _safe_avatar_id(avatar_id)
    language = (language or DEFAULT_LANG).strip() or DEFAULT_LANG

    speaker_path: Optional[str] = None
    tmp_path: Optional[Path] = None

    # 1) file caricato
    if speaker_wav is not None and speaker_wav.filename:
        data = await speaker_wav.read()
        if data and len(data) >= 512:
            suffix = Path(speaker_wav.filename).suffix or ".wav"
            fd, tmp_name = tempfile.mkstemp(prefix="speaker_", suffix=suffix)
            os.close(fd)
            tmp_path = Path(tmp_name)
            tmp_path.write_bytes(data)
            speaker_path = str(tmp_path)

            if save_voice:
                out_path = _avatar_voice_path(safe)
                out_path.parent.mkdir(parents=True, exist_ok=True)
                out_path.write_bytes(data)

    # 2) voce salvata avatar
    if speaker_path is None:
        avatar_ref = _avatar_voice_path(safe)
        if _file_has_content(avatar_ref):
            speaker_path = str(avatar_ref)

    # 3) fallback default
    if speaker_path is None:
        default_ref = Path(DEFAULT_SPEAKER_WAV)
        if _file_has_content(default_ref):
            speaker_path = str(default_ref)

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

    if speaker_wav is not None and speaker_wav.filename:
        data = await speaker_wav.read()
        if data and len(data) >= 512:
            suffix = Path(speaker_wav.filename).suffix or ".wav"
            fd, tmp_name = tempfile.mkstemp(prefix="speaker_", suffix=suffix)
            os.close(fd)
            tmp_path = Path(tmp_name)
            tmp_path.write_bytes(data)
            speaker_path = str(tmp_path)

            if save_voice:
                out_path = _avatar_voice_path(safe)
                out_path.parent.mkdir(parents=True, exist_ok=True)
                out_path.write_bytes(data)

    if speaker_path is None:
        avatar_ref = _avatar_voice_path(safe)
        if _file_has_content(avatar_ref):
            speaker_path = str(avatar_ref)

    if speaker_path is None:
        default_ref = Path(DEFAULT_SPEAKER_WAV)
        if _file_has_content(default_ref):
            speaker_path = str(default_ref)

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
