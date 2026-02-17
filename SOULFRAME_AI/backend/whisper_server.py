import os
import tempfile
from typing import Any

from fastapi import FastAPI, UploadFile, File, Form
from fastapi.middleware.cors import CORSMiddleware
import whisper

MODEL_NAME = os.getenv("WHISPER_MODEL", "small").strip()
DEFAULT_LANGUAGE = "it"

model = whisper.load_model(MODEL_NAME)

app = FastAPI()

app.add_middleware(
    CORSMiddleware,
    allow_origins=["*"],
    allow_credentials=False,
    allow_methods=["*"],
    allow_headers=["*"],
)


def _first_form_value(value: Any, fallback: str) -> str:
    """FastAPI form fields may arrive as multi-value lists at runtime."""
    current = value
    if isinstance(current, list):
        current = current[0] if current else fallback
    return str(current or fallback).strip()


@app.get("/health")
def health():
    return {"ok": True, "model": MODEL_NAME}


@app.post("/transcribe")
async def transcribe(
    file: UploadFile = File(...),
    language: str = Form(default=DEFAULT_LANGUAGE),
):
    filename = file.filename or "audio.wav"
    suffix = os.path.splitext(filename)[1] or ".wav"
    with tempfile.NamedTemporaryFile(delete=False, suffix=suffix) as tmp:
        tmp.write(await file.read())
        tmp_path = tmp.name

    try:
        lang = _first_form_value(language, DEFAULT_LANGUAGE).lower()
        result = model.transcribe(tmp_path, language=lang)
        text_value = _first_form_value(result.get("text", ""), "")
        return {"text": text_value}
    finally:
        try:
            os.remove(tmp_path)
        except OSError:
            pass
