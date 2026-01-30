import os
import tempfile
from fastapi import FastAPI, UploadFile, File, Form
import whisper

MODEL_NAME = os.getenv("WHISPER_MODEL", "small").strip()
model = whisper.load_model(MODEL_NAME)

app = FastAPI()

@app.post("/transcribe")
async def transcribe(
    file: UploadFile = File(...),
    language: str = Form(default="it"),
):
    suffix = os.path.splitext(file.filename)[1] or ".wav"
    with tempfile.NamedTemporaryFile(delete=False, suffix=suffix) as tmp:
        tmp.write(await file.read())
        tmp_path = tmp.name

    try:
        result = model.transcribe(tmp_path, language=language.strip().lower())
        return {"text": result.get("text", "").strip()}
    finally:
        try:
            os.remove(tmp_path)
        except OSError:
            pass
