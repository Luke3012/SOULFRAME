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
- **[1] Start services** - starts Ollama, Whisper, RAG, TTS and Project Build
- **[2] Stop services** - terminates all processes
- **[3] Restart services** - stop + start in sequence

**What ai_services.cmd does:**
- Automatically detects Python virtual environment (`backend\venv` or `backend\.venv`)
- Checks if ports are already in use (avoids duplicates)
- Starts each service in a separate minimized window
- Configures environment variables needed for Whisper/RAG/TTS
- Provides direct links to Swagger UIs (`/docs`)
- Starts Build Server in `..\Build` (or `BUILD_DIR` if set) and opens `http://localhost:8000`

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

If `..\Build` doesn't exist, set the `BUILD_DIR` environment variable with the full path:

```powershell
set BUILD_DIR=C:\Path\To\Build
ai_services.cmd 1
```

Per cambiare i parametri su Windows modifica direttamente `ai_services.cmd`.

## Note

- **Primo avvio TTS**: il download del modello XTTS v2 richiede ~2GB e può richiedere alcuni minuti
- **GPU**: TTS utilizzerà automaticamente CUDA se disponibile (molto più veloce)
- **OCR**: configurato per italiano+inglese, modificabile con env `RAG_OCR_LANG`
- **Memoria RAG**: i database per avatar sono salvati in `backend/rag_store/`
- **Log conversazioni**: salvati per avatar in `backend/log/` (una sessione MainMode = un file `.log`)

### Warmup Coqui al boot

Dopo l'avvio del servizio TTS, il backend esegue una inizializzazione/warmup del modello Coqui
usando una frase breve (`"ciao"`). Questa e' in genere la fase piu lenta del primo startup.

Nel frontend Unity, durante questa fase viene mostrato lo stato di inizializzazione (loading panel
e animazioni dedicate), e l'interfaccia completa viene resa disponibile quando il TTS risulta pronto.

### Warmup RAG/Ollama al boot

All'avvio del servizio RAG, `rag_server` esegue un warmup best-effort di Ollama:

- step embedding su `/api/embed` (modello `EMBED_MODEL`);
- step chat su `/api/chat` (modello `CHAT_MODEL`, con `num_predict` ridotto).

Se Ollama non e' raggiungibile in quel momento, il warmup viene loggato come warning ma
`rag_server` resta attivo (nessun crash di startup).

Nel bootstrap Unity viene atteso anche `RAG /health` (oltre a `TTS /health`) prima di
considerare il sistema completamente pronto.

## Troubleshooting

### "Ollama non raggiungibile"
Verifica che Ollama sia avviato: `ollama serve`

### "OCR non disponibile"
Installa Tesseract e verifica il percorso in `rag_server.py` (`TESSERACT_CMD`)

### "CUDA out of memory"
Usa CPU per TTS: `set COQUI_TTS_DEVICE=cpu` prima di avviare

### Conflitto porte
Modifica le porte in `ai_services.cmd` o termina i processi esistenti

### "Errore TTS: HTTP 500" su `/api/tts/tts_stream`

1. Verifica stato servizio:
   ```bash
   sudo systemctl status soulframe-tts --no-pager
   sudo journalctl -u soulframe-tts -n 200 --no-pager
   ```
2. Verifica presenza voce default:
   ```bash
   ls -lh /opt/soulframe/backend/voices/default.wav
   ```
3. Se nei log trovi:
   `ImportError: cannot import name 'isin_mps_friendly' from transformers.pytorch_utils`
   forza il set compatibile:
   ```bash
   /opt/soulframe/.venv/bin/pip install --upgrade "transformers==4.57.1" "tokenizers==0.22.1"
   sudo systemctl restart soulframe-tts
   ```
4. Se nei log trovi:
   `From Pytorch 2.9, the torchcodec library is required for audio IO`
   installa codec:
   ```bash
   sudo /opt/soulframe/.venv/bin/pip install --upgrade "coqui-tts[codec]==0.27.5" "torchcodec>=0.8.0"
   sudo systemctl restart soulframe-tts
   ```
5. Se nei log trovi prompt licenza Coqui con `EOFError: EOF when reading a line`, imposta:
   ```bash
   echo 'COQUI_TOS_AGREED=1' | sudo tee -a /etc/soulframe/soulframe.env
   sudo systemctl restart soulframe-tts
   ```
   (Usa questa opzione solo se accetti i termini CPML/licenza commerciale Coqui.)
6. Se `pip` fallisce con `Permission denied` su `/opt/soulframe/.venv/...`, usa `sudo` davanti al comando `pip`.
7. Aggiorna `coqui_tts_server.py` all'ultima versione e riavvia il servizio.

### "wait_phrase ... 404 Not Found"

Le versioni recenti del backend provano a rigenerare automaticamente la frase di attesa. Se persiste:
1. Verifica endpoint:
   ```bash
   curl -I "https://<dominio>/api/tts/wait_phrase?avatar_id=LOCAL_model1&name=un_secondo"
   ```
2. Genera esplicitamente le wait phrases:
   ```bash
   curl -X POST https://<dominio>/api/tts/generate_wait_phrases \
     -F "avatar_id=LOCAL_model1" -F "language=it"
   ```
3. Riavvia `soulframe-tts`.

### Setup voce (frase lunga)

Il flusso recente usa una frase di configurazione più ricca (target >= 50 parole) per migliorare la qualità del profilo vocale locale.

### "Download glb failed: 404 Not Found"

Se il frontend riceve 404 su `/avatars/{id}/model.glb`:

1. Verifica health/list del servizio avatar:
   ```bash
   curl http://127.0.0.1:8003/health
   curl http://127.0.0.1:8003/avatars/list
   ```
2. Aggiorna `avatar_asset_server.py` all'ultima versione e riavvia il servizio:
   ```bash
   sudo systemctl restart soulframe-avatar.service
   ```
3. In produzione dietro Caddy verifica di chiamare `/api/avatar/...` e non `127.0.0.1` dal browser.
