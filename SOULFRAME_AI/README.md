# SOULFRAME AI Services

[🇬🇧 English](#) | [🇮🇹 Italiano](README.it.md)

---

AI services system for SOULFRAME: speech-to-text (Whisper), text-to-speech (Coqui XTTS v2) and RAG (Retrieval-Augmented Generation) with persistent memory for avatars, based on Ollama (LLM + embeddings).

## Prerequisites

### Required Software

#### Windows
- **Python 3.11** ([download](https://www.python.org/downloads/))
- **Ollama** ([download](https://ollama.ai/)) - required for embeddings and LLM chat
- **Tesseract OCR** ([download](https://github.com/UB-Mannheim/tesseract/wiki)) - required for OCR from PDF/images
    - Install in `C:\Program Files\Tesseract-OCR\` (default path)
    - During installation, select **Italian language** in additional components

#### Optional
- **CUDA (recent drivers)** - optional to accelerate TTS on GPU
- **ffmpeg** - for additional audio format support in Whisper

### Ollama Models

After installing Ollama, download the necessary models:

```powershell
ollama pull nomic-embed-text
ollama pull llama3:8b-instruct-q4_K_M
```

## Setup

### 1. Virtual Environment (Recommended)

Create a Python virtual environment to isolate dependencies:

```powershell
py -3.11 -m venv backend\.venv
backend\.venv\Scripts\activate
```

### 2. Install Dependencies

```powershell
pip install -r requirements.txt
```

> **WARNING (PyTorch cu128)**
> `requirements.txt` uses standard build (`torch`/`torchaudio`).
> If you want to use specific CUDA wheels, reinstall PyTorch explicitly:
>
> ```powershell
> pip install torch torchaudio --index-url https://download.pytorch.org/whl/cu128
> ```

**Note**: Installation requires several GB and can take 10-20 minutes.

### 3. Configuration (Optional)

Create a `backend/gemini_key.txt` file if you want to use Gemini Vision to describe images:

```
YOUR_GEMINI_API_KEY_HERE
```

### 4. Default TTS Voice (Optional)

Place a reference audio file (WAV) so:

```
backend/voices/default.wav
```

### 5. Useful environment variables (RAG)

- `RAG_DIR`: root of vector memory per-avatar (default: `backend/rag_store`)
- `RAG_LOG_DIR`: root of conversation logs per-avatar.
  - Windows local default: `backend/log`
  - Ubuntu setup default: `/home/<utente_runtime>/soulframe-logs` (fallback: `/opt/soulframe/backend/log`)

## Starting Services

### Automatic Method (Recommended)

Use the `ai_services.cmd` script to manage all services:

```powershell
ai_services.cmd 1
```

The menu allows you to:
- **[1] Start services** - starts Ollama, Whisper, RAG, TTS and the configured frontend
- **[2] Stop services** - terminates all processes
- **[3] Restart services** - stop + start in sequence
- **[4] Debug console** - starts backend services in console/debug mode
- **[5] Configure frontend default** - choose WebGL or Windows executable

**What ai_services.cmd does:**
- Automatically detects Python virtual environment (`backend\venv` or `backend\.venv`)
- Checks if ports are already in use (avoids duplicates)
- Can run either in console mode or background mode depending on menu/action
- Configures environment variables needed for Whisper/RAG/TTS
- Provides direct links to Swagger UIs (`/docs`)
- In WebGL mode, starts Build Server in `..\Build` (or `SOULFRAME_WEBGL_BUILD_DIR`) and opens `http://localhost:8000`
- In Windows mode, launches `..\Build_Windows64\SOULFRAME.exe` (or `SOULFRAME_WINDOWS_EXE`)
- Persists the selected frontend mode in `ai_services.mode.cfg`

### Manual Method

Start each service separately (5 terminals):

```powershell
# Terminal 1 - Ollama
ollama serve

# Terminal 2 - Whisper (Speech-to-Text)
cd backend
.\.venv\Scripts\activate
uvicorn whisper_server:app --host 127.0.0.1 --port 8001

# Terminal 3 - RAG (Retrieval-Augmented Generation)
cd backend
.\.venv\Scripts\activate
uvicorn rag_server:app --host 127.0.0.1 --port 8002

# Terminal 4 - TTS (Text-to-Speech)
cd backend
.\.venv\Scripts\activate
uvicorn coqui_tts_server:app --host 127.0.0.1 --port 8004

# Terminal 5 - Avatar Asset Server (Cache glb)
cd backend
.\.venv\Scripts\activate
uvicorn avatar_asset_server:app --host 127.0.0.1 --port 8003
```

## Service Ports

- **Whisper**: http://127.0.0.1:8001/docs
- **RAG**: http://127.0.0.1:8002/docs
- **TTS**: http://127.0.0.1:8004/docs
- **Avatar Asset**: http://127.0.0.1:8003/docs
- **Ollama**: http://127.0.0.1:11434
- **Build Server**: http://localhost:8000

Notes:

- The Build Server endpoint is relevant only when the selected frontend mode is WebGL.
- In Windows frontend mode, the native executable is launched instead of the browser build.

## Validation and regression tools

The `tools/` folder contains the PowerShell test batteries used to harden the small `llama3:8b-instruct-q4_K_M` setup against the issues that matter in this project: missed retrieval, weak multi-source recap, identity drift, and persona inconsistency.

- Overview and usage: `tools/README.md`
- Main scripts: `run_extreme_stress_test.ps1`, `run_text_coherence_identity_test.ps1`, `run_complex_deficit_case_study.ps1`
- Typical output: Markdown reports written to Desktop with pass/fail summaries and detailed examples

## Production Endpoints (Linux + Caddy)

If the WebGL frontend runs behind Caddy on a public domain, use proxy paths:

- `/api/whisper/*` -> Whisper
- `/api/rag/*` -> RAG
- `/api/avatar/*` -> Avatar Asset
- `/api/tts/*` -> Coqui TTS

Example:

```text
https://soulframe.page/api/avatar/avatars/list
```

Linux deployment notes:

- do not use `127.0.0.1:800x` endpoints in the public WebGL browser;
- always use `/api/...` behind Caddy;
- to update backend/scripts on VM use `sudo sfadmin` (option `[2]`), which can also clean source files in update dir after confirmation.

## Usage

### Whisper (Speech-to-Text)

```python
import requests

with open("audio.wav", "rb") as f:
    response = requests.post(
        "http://127.0.0.1:8001/transcribe",
        files={"file": f},
        data={"language": "it"}
    )
print(response.json()["text"])
```

### RAG (Avatar Memory)

```python
import requests

# Save a memory
requests.post("http://127.0.0.1:8002/remember", json={
    "avatar_id": "alice",
    "text": "I like chocolate ice cream",
    "meta": {"source": "conversation"}
})

# Retrieve relevant memories
response = requests.post("http://127.0.0.1:8002/recall", json={
    "avatar_id": "alice",
    "query": "What are your favorite flavors?",
    "top_k": 5
})
print(response.json()["documents"])
```

### RAG Chat Session + Conversation Log

For each entry into MainMode, the frontend starts a conversation session:

- `POST /chat_session/start` with `avatar_id` returns `session_id` and `log_file`
- `POST /chat` also accepts:
  - `session_id` (optional)
  - `input_mode` (`keyboard` or `voice`)
  - `log_conversation` (`true` to append turn to session file)

```python
import requests

session = requests.post("http://127.0.0.1:8002/chat_session/start", json={
    "avatar_id": "alice"
}).json()

response = requests.post("http://127.0.0.1:8002/chat", json={
    "avatar_id": "alice",
    "user_text": "Hi, how are you?",
    "top_k": 20,
    "session_id": session["session_id"],
    "input_mode": "keyboard",   # or "voice"
    "log_conversation": True
})
print(response.json()["text"])
```

```bash
# 1) Start session
curl -X POST http://127.0.0.1:8002/chat_session/start \
  -H "Content-Type: application/json" \
  -d '{"avatar_id":"alice"}'

# 2) Logged chat (replace <session_id>)
curl -X POST http://127.0.0.1:8002/chat \
  -H "Content-Type: application/json" \
  -d '{"avatar_id":"alice","user_text":"Hi","top_k":20,"session_id":"<session_id>","input_mode":"voice","log_conversation":true}'
```

Logs are saved in `backend/log/<avatar_id_sanitized>/<session_id>.log`.
Technical flows (e.g., `setup_voice_generator`) are not logged as MainMode conversations.

### Empirical test mode

The frontend can enable an empirical test session by typing `T-E-S-T` in `MainMenu`.
When that happens, requests sent to backend services include `empirical_test_mode=true`.

On the backend this keeps empirical runs isolated from normal runs:

- RAG memory uses `backend/empirical_test/rag_store/`
- RAG conversation logs use `backend/empirical_test/log/` by default, or `EMPIRICAL_RAG_LOG_DIR` if configured
- TTS avatar voices and wait phrases use the empirical voice root
- Avatar Asset Server uses `backend/empirical_test/avatar_store/`

Conversation logging still works in the same way as standard MainMode sessions: one session file per conversation, but written under the empirical storage area.

### TTS (Text-to-Speech)

```python
import requests

response = requests.post(
    "http://127.0.0.1:8004/tts",
    data={
        "text": "Hi, I'm a virtual avatar!",
        "avatar_id": "alice",
        "language": "it"
    }
)

with open("output.wav", "wb") as f:
    f.write(response.content)
```

### Avatar Asset Server (Cache .glb)

```python
import requests

payload = {
    "avatar_id": "avaturn_demo",
    "url": "https://example.com/avaturn_export.glb",
    "gender": "female",
    "bodyId": "default",
    "urlType": "glb"
}

response = requests.post("http://127.0.0.1:8003/avatars/import", json=payload)
print(response.json())
```

Note: `avatar_asset_server.py` includes self-healing logic for metadata (`file_path`), to handle deployments/migrations where `.glb` files exist but the saved path is no longer valid.

Note: `coqui_tts_server.py` also handles `wait_phrase` resiliently:
- if the file doesn't exist, tries to generate it on-demand;
- tries compatible legacy paths and automatically realigns files in current directory.

## How to test (curl)

```bash
# Import avatar (replace with real export URL)
curl -X POST http://127.0.0.1:8003/avatars/import \\
  -H "Content-Type: application/json" \\
  -d "{\\"avatar_id\\":\\"avaturn_demo\\",\\"url\\":\\"https://example.com/avaturn_export.glb\\",\\"gender\\":\\"female\\",\\"bodyId\\":\\"default\\",\\"urlType\\":\\"glb\\"}"

# Download model (verify bytes > 0)
curl -L http://127.0.0.1:8003/avatars/avaturn_demo/model.glb --output avatar.glb

# List avatars (must always contain LOCAL_model1 and LOCAL_model2)
curl http://127.0.0.1:8003/avatars/list
```

## Build Server

If `..\Build` doesn't exist, set the `SOULFRAME_WEBGL_BUILD_DIR` environment variable with the full path:

```powershell
set SOULFRAME_WEBGL_BUILD_DIR=C:\Path\To\Build
ai_services.cmd 1
```

Equivalent variables currently supported by `ai_services.cmd`:

- `SOULFRAME_WEBGL_BUILD_DIR` for the WebGL build directory
- `SOULFRAME_WINDOWS_EXE` for the Windows executable path

To change other Windows-side parameters, edit `ai_services.cmd` directly.

## Notes

- **First TTS startup**: downloading the XTTS v2 model requires about 2GB and may take a few minutes
- **GPU**: TTS will automatically use CUDA if available, which is much faster
- **OCR**: configured for Italian + English by default, adjustable with the `RAG_OCR_LANG` environment variable
- **RAG memory**: per-avatar databases are stored in `backend/rag_store/`
- **Conversation logs**: stored per avatar in `backend/log/` (one MainMode session = one `.log` file)
- **Empirical test logs**: stored separately under the empirical storage root, so guided test runs do not pollute standard avatar history

### Coqui warmup at boot

After the TTS service starts, the backend performs a short Coqui warmup
using a brief phrase (`"ciao"`). This is usually the slowest phase of the first startup.

In the Unity frontend, this phase is represented by a dedicated initialization state
(loading panel and related animations), and the full interface becomes available only when TTS is ready.

### RAG/Ollama warmup at boot

When the RAG service starts, `rag_server` performs a best-effort warmup of Ollama:

- embedding step on `/api/embed` (model `EMBED_MODEL`)
- chat step on `/api/chat` (model `CHAT_MODEL`, with reduced `num_predict`)

If Ollama is not reachable at that moment, the warmup is logged as a warning but
`rag_server` stays up and does not crash during startup.

During Unity bootstrap, `RAG /health` is also awaited, together with `TTS /health`,
before the system is considered fully ready.

## Troubleshooting

### "Ollama not reachable"
Verify that Ollama is running: `ollama serve`

### "OCR not available"
Install Tesseract and verify the path in `rag_server.py` (`TESSERACT_CMD`)

### "CUDA out of memory"
Use CPU for TTS: `set COQUI_TTS_DEVICE=cpu` before starting the service

### Port conflict
Change the ports in `ai_services.cmd` or stop the existing processes

### "TTS error: HTTP 500" on `/api/tts/tts_stream`

1. Verify the service status:
   ```bash
   sudo systemctl status soulframe-tts --no-pager
   sudo journalctl -u soulframe-tts -n 200 --no-pager
   ```
2. Check that the default voice file exists:
   ```bash
   ls -lh /opt/soulframe/backend/voices/default.wav
   ```
3. If the logs contain:
  `ImportError: cannot import name 'isin_mps_friendly' from transformers.pytorch_utils`
  force the compatible package set:
   ```bash
   /opt/soulframe/.venv/bin/pip install --upgrade "transformers==4.57.1" "tokenizers==0.22.1"
   sudo systemctl restart soulframe-tts
   ```
4. If the logs contain:
  `From Pytorch 2.9, the torchcodec library is required for audio IO`
  install the required codec packages:
   ```bash
   sudo /opt/soulframe/.venv/bin/pip install --upgrade "coqui-tts[codec]==0.27.5" "torchcodec>=0.8.0"
   sudo systemctl restart soulframe-tts
   ```
5. If the logs show the Coqui license prompt with `EOFError: EOF when reading a line`, set:
   ```bash
   echo 'COQUI_TOS_AGREED=1' | sudo tee -a /etc/soulframe/soulframe.env
   sudo systemctl restart soulframe-tts
   ```
  (Use this only if you accept the CPML / commercial Coqui license terms.)
6. If `pip` fails with `Permission denied` on `/opt/soulframe/.venv/...`, run the `pip` command with `sudo`.
7. Update `coqui_tts_server.py` to the latest version and restart the service.

### "wait_phrase ... 404 Not Found"

Recent backend versions try to regenerate the wait phrase automatically. If the issue persists:
1. Check the endpoint:
   ```bash
  curl -I "https://<domain>/api/tts/wait_phrase?avatar_id=LOCAL_model1&name=un_secondo"
   ```
2. Explicitly generate the wait phrases:
   ```bash
   curl -X POST https://<domain>/api/tts/generate_wait_phrases \
     -F "avatar_id=LOCAL_model1" -F "language=it"
   ```
3. Restart `soulframe-tts`.

### Voice setup (long phrase)

The current flow uses a richer setup sentence (target >= 50 words) to improve the quality of the local voice profile.

### "Download glb failed: 404 Not Found"

If the frontend receives a 404 on `/avatars/{id}/model.glb`:

1. Check the health/list endpoints of the avatar service:
   ```bash
   curl http://127.0.0.1:8003/health
   curl http://127.0.0.1:8003/avatars/list
   ```
2. Update `avatar_asset_server.py` to the latest version and restart the service:
   ```bash
   sudo systemctl restart soulframe-avatar.service
   ```
3. In production behind Caddy, make sure the browser calls `/api/avatar/...` and not `127.0.0.1` directly.
