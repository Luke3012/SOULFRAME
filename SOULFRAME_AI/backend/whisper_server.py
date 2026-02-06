import os
import tempfile
from typing import Any
from fastapi import FastAPI, UploadFile, File, Form
from fastapi.middleware.cors import CORSMiddleware
import whisper

MODEL_NAME = os.getenv("WHISPER_MODEL", "small").strip()
model = whisper.load_model(MODEL_NAME)

app = FastAPI()

app.add_middleware(
    CORSMiddleware,
    allow_origins=["*"],
    allow_credentials=False,
    allow_methods=["*"],
    allow_headers=["*"],
)

@app.get("/health")
def health():
    return {"ok": True, "model": MODEL_NAME}

@app.post("/transcribe")
async def transcribe(
    file: UploadFile = File(...),
    language: str = Form(default="it"),
):
    filename = file.filename or "audio.wav"
    suffix = os.path.splitext(filename)[1] or ".wav"
    with tempfile.NamedTemporaryFile(delete=False, suffix=suffix) as tmp:
        tmp.write(await file.read())
        tmp_path = tmp.name

    try:
        # FastAPI form fields can be provided multiple times; be defensive for typing/runtime.
        lang_value: Any = language
        if isinstance(lang_value, list):
            lang_value = lang_value[0] if lang_value else "it"
        lang = str(lang_value or "it").strip().lower()
        result = model.transcribe(tmp_path, language=lang)
        text_value: Any = result.get("text", "")
        if isinstance(text_value, list):
            text_value = text_value[0] if text_value else ""
        return {"text": str(text_value).strip()}
    finally:
        try:
            os.remove(tmp_path)
        except OSError:
            pass
