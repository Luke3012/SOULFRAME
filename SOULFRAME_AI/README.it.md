> [🇬🇧 English](README.md) | **🇮🇹 Italiano**

# SOULFRAME AI Services (Servizi AI)

Sistema di servizi AI per SOULFRAME: speech-to-text (Whisper), text-to-speech (Coqui XTTS v2) e RAG (Retrieval-Augmented Generation) con memoria persistente per avatar, basato su Ollama (LLM + embeddings).

## Prerequisiti

### Software Richiesto

#### Windows
- **Python 3.11** ([download](https://www.python.org/downloads/))
- **Ollama** ([download](https://ollama.ai/)) - necessario per embeddings e chat LLM
- **Tesseract OCR** ([download](https://github.com/UB-Mannheim/tesseract/wiki)) - necessario per OCR da PDF/immagini
    - Installa in `C:\Program Files\Tesseract-OCR\` (percorso default)
    - Durante l'installazione, seleziona **lingua italiana** nei componenti aggiuntivi

#### Opzionale
- **CUDA (driver recenti)** - opzionale per accelerare TTS su GPU
- **ffmpeg** - per supporto formati audio aggiuntivi in Whisper

### Modelli Ollama

Dopo aver installato Ollama, scarica i modelli necessari:

```powershell
ollama pull nomic-embed-text
ollama pull llama3:8b-instruct-q4_K_M
```

## Setup

### 1. Ambiente Virtuale (Consigliato)

Crea un ambiente virtuale Python per isolare le dipendenze:

```powershell
py -3.11 -m venv backend\.venv
backend\.venv\Scripts\activate
```

### 2. Installazione Dipendenze

```powershell
pip install -r requirements.txt
```

> **ATTENZIONE (PyTorch cu128)**
> `requirements.txt` usa build standard (`torch`/`torchaudio`).
> Se vuoi usare wheel CUDA specifiche, reinstalla PyTorch esplicitamente:
>
> ```powershell
> pip install torch torchaudio --index-url https://download.pytorch.org/whl/cu128
> ```

**Nota**: L'installazione richiede diversi GB e può richiedere 10-20 minuti.

### 3. Configurazione (Opzionale)

Crea un file `backend/gemini_key.txt` se vuoi usare Gemini Vision per descrivere immagini:

```
TUA_GEMINI_API_KEY_QUI
```

### 4. Voce Default TTS (Opzionale)

Posiziona un file audio di riferimento (WAV) in:

```
backend/voices/default.wav
```

### 5. Variabili ambiente utili (RAG)

- `RAG_DIR`: root della memoria vettoriale per-avatar (default: `backend/rag_store`)
- `RAG_LOG_DIR`: root dei log conversazione per-avatar.
  - Default locale Windows: `backend/log`
  - Default setup Ubuntu: `/home/<utente_runtime>/soulframe-logs` (fallback: `/opt/soulframe/backend/log`)

## Avvio Servizi

### Metodo Automatico (Consigliato)

Usa lo script `ai_services.cmd` per gestire tutti i servizi:

```powershell
ai_services.cmd 1
```

Il menu ti permette di:
- **[1] Start servizi** - avvia Ollama, Whisper, RAG, TTS e Build del Progetto
- **[2] Stop servizi** - termina tutti i processi
- **[3] Restart servizi** - stop + start in sequenza

**Cosa fa ai_services.cmd:**
- Rileva automaticamente l'ambiente virtuale Python (`backend\venv` o `backend\.venv`)
- Verifica se le porte sono già in uso (evita duplicati)
- Avvia ogni servizio in una finestra separata minimizzata
- Configura le variabili d'ambiente necessarie per Whisper/RAG/TTS
- Fornisce link diretti alle UI Swagger (`/docs`)
- Avvia il Build Server in `..\Build` (o `BUILD_DIR` se impostata) e apre `http://localhost:8000`

### Metodo Manuale

Avvia ogni servizio separatamente (5 terminali):

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

## Porte Servizi

- **Whisper**: http://127.0.0.1:8001/docs
- **RAG**: http://127.0.0.1:8002/docs
- **TTS**: http://127.0.0.1:8004/docs
- **Avatar Asset**: http://127.0.0.1:8003/docs
- **Ollama**: http://127.0.0.1:11434
- **Build Server**: http://localhost:8000

## Endpoint in produzione (Linux + Caddy)

Se il frontend WebGL gira dietro Caddy su dominio pubblico, usa i path proxy:

- `/api/whisper/*` -> Whisper
- `/api/rag/*` -> RAG
- `/api/avatar/*` -> Avatar Asset
- `/api/tts/*` -> Coqui TTS

Esempio:

```text
https://soulframe.page/api/avatar/avatars/list
```

Nota deploy Linux:

- non usare endpoint `127.0.0.1:800x` nel browser WebGL pubblico;
- usare sempre `/api/...` dietro Caddy;
- per aggiornare backend/script su VM usare `sudo sfadmin` (opzione `[2]`), che può anche ripulire i file sorgente nella update dir dopo conferma.

## Uso

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

### RAG (Memoria Avatar)

```python
import requests

# Salva un ricordo
requests.post("http://127.0.0.1:8002/remember", json={
    "avatar_id": "alice",
    "text": "Mi piace il gelato al cioccolato",
    "meta": {"source": "conversation"}
})

# Recupera ricordi rilevanti
response = requests.post("http://127.0.0.1:8002/recall", json={
    "avatar_id": "alice",
    "query": "Quali sono i gusti preferiti?",
    "top_k": 5
})
print(response.json()["documents"])
```

### RAG Chat Session + Log conversazione

Per ogni ingresso in MainMode, il frontend avvia una sessione conversazione:

- `POST /chat_session/start` con `avatar_id` restituisce `session_id` e `log_file`
- `POST /chat` accetta anche:
  - `session_id` (opzionale)
  - `input_mode` (`keyboard` o `voice`)
  - `log_conversation` (`true` per append del turno nel file sessione)

```python
import requests

session = requests.post("http://127.0.0.1:8002/chat_session/start", json={
    "avatar_id": "alice"
}).json()

response = requests.post("http://127.0.0.1:8002/chat", json={
    "avatar_id": "alice",
    "user_text": "Ciao, come stai?",
    "top_k": 20,
    "session_id": session["session_id"],
    "input_mode": "keyboard",   # oppure "voice"
    "log_conversation": True
})
print(response.json()["text"])
```

```bash
# 1) Avvia sessione
curl -X POST http://127.0.0.1:8002/chat_session/start \
  -H "Content-Type: application/json" \
  -d '{"avatar_id":"alice"}'

# 2) Chat loggata (sostituisci <session_id>)
curl -X POST http://127.0.0.1:8002/chat \
  -H "Content-Type: application/json" \
  -d '{"avatar_id":"alice","user_text":"Ciao","top_k":20,"session_id":"<session_id>","input_mode":"voice","log_conversation":true}'
```

I log sono salvati in `backend/log/<avatar_id_sanitized>/<session_id>.log`.
I flussi tecnici (es. `setup_voice_generator`) non vengono loggati come conversazione MainMode.

### TTS (Text-to-Speech)

```python
import requests

response = requests.post(
    "http://127.0.0.1:8004/tts",
    data={
        "text": "Ciao, sono un avatar virtuale!",
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

Nota: `avatar_asset_server.py` include una logica di self-healing dei metadata (`file_path`), per gestire deploy/migrazioni in cui i file `.glb` esistono ma il path salvato non è più valido.

Nota: `coqui_tts_server.py` gestisce anche `wait_phrase` in modo resiliente:
- se il file non esiste, prova a generarlo on-demand;
- prova path compatibili legacy e riallinea automaticamente i file nella directory corrente.

## Come testare (curl)

```bash
# Import avatar (sostituisci con l'URL reale di export)
curl -X POST http://127.0.0.1:8003/avatars/import \\
  -H "Content-Type: application/json" \\
  -d "{\\"avatar_id\\":\\"avaturn_demo\\",\\"url\\":\\"https://example.com/avaturn_export.glb\\",\\"gender\\":\\"female\\",\\"bodyId\\":\\"default\\",\\"urlType\\":\\"glb\\"}"

# Scarica modello (verifica che i byte siano > 0)
curl -L http://127.0.0.1:8003/avatars/avaturn_demo/model.glb --output avatar.glb

# Lista avatar (deve contenere sempre LOCAL_model1 e LOCAL_model2)
curl http://127.0.0.1:8003/avatars/list
```

## Build Server

Se non esiste `..\Build`, imposta la variabile ambiente `BUILD_DIR` con il path completo:

```powershell
set BUILD_DIR=C:\Path\To\Build
ai_services.cmd 1
```

Per cambiare i parametri su Windows modifica direttamente `ai_services.cmd`.

## Warmup Coqui al boot

Dopo l'avvio del servizio TTS, il backend esegue una inizializzazione/warmup del modello Coqui
usando una frase breve (`"ciao"`). Questa e' in genere la fase piu lenta del primo startup.

## Warmup RAG/Ollama al boot

All'avvio del servizio RAG, `rag_server` esegue un warmup best-effort di Ollama:

- step embedding su `/api/embed` (modello `EMBED_MODEL`);
- step chat su `/api/chat` (modello `CHAT_MODEL`, con `num_predict` ridotto).

Se Ollama non e' raggiungibile in quel momento, il warmup viene loggato come warning ma
`rag_server` resta attivo (nessun crash di startup).

Nel bootstrap Unity viene atteso anche `RAG /health` (oltre a `TTS /health`) prima di
considerare il sistema completamente pronto.
