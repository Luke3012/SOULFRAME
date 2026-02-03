# SOULFRAME AI Services

Sistema di servizi AI per SOULFRAME: speech-to-text (Whisper), text-to-speech (Coqui XTTS), e RAG (Retrieval-Augmented Generation) con memoria persistente per avatar.

## Prerequisiti

### Software Richiesto

#### Windows
- **Python 3.10+** ([download](https://www.python.org/downloads/))
- **Ollama** ([download](https://ollama.ai/)) - necessario per embeddings e chat LLM
- **Tesseract OCR** ([download](https://github.com/UB-Mannheim/tesseract/wiki)) - necessario per OCR da PDF/immagini
  - Installa in `C:\Program Files\Tesseract-OCR\` (percorso default)
  - Durante l'installazione, seleziona **lingua italiana** nei componenti aggiuntivi

#### Opzionale
- **CUDA Toolkit 12.1+** - per accelerazione GPU (consigliato per TTS)
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
cd backend
python -m venv venv
.\venv\Scripts\activate
```

### 2. Installazione Dipendenze

```powershell
pip install -r requirements.txt
```

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

## Avvio Servizi

### Metodo Automatico (Consigliato)

Usa lo script `ai_services.cmd` per gestire tutti i servizi:

```powershell
ai_services.cmd
```

Il menu ti permette di:
- **[1] Start servizi** - avvia Ollama, Whisper, RAG, TTS e Build del Progetto
- **[2] Stop servizi** - termina tutti i processi

**Cosa fa ai_services.cmd:**
- Rileva automaticamente l'ambiente virtuale Python (`backend\venv` o `backend\.venv`)
- Verifica se le porte sono già in uso (evita duplicati)
- Avvia ogni servizio in una finestra separata minimizzata
- Configura le variabili d'ambiente necessarie per TTS
- Fornisce link diretti alle UI Swagger (`/docs`)
- Avvia il Build Server in `..\Build` (o `BUILD_DIR` se impostata) e apre `http://localhost:8000`

### Metodo Manuale

Avvia ogni servizio separatamente (4 terminali):

```powershell
# Terminal 1 - Ollama
ollama serve

# Terminal 2 - Whisper (Speech-to-Text)
cd backend
.\venv\Scripts\activate
uvicorn whisper_server:app --host 127.0.0.1 --port 8001

# Terminal 3 - RAG (Retrieval-Augmented Generation)
cd backend
.\venv\Scripts\activate
uvicorn rag_server:app --host 127.0.0.1 --port 8002

# Terminal 4 - TTS (Text-to-Speech)
cd backend
.\venv\Scripts\activate
uvicorn coqui_tts_server:app --host 127.0.0.1 --port 8004

# Terminal 5 - Avatar Asset Server (Cache glb)
cd backend
.\venv\Scripts\activate
uvicorn avatar_asset_server:app --host 127.0.0.1 --port 8003
```

## Porte Servizi

- **Whisper**: http://127.0.0.1:8001/docs
- **RAG**: http://127.0.0.1:8002/docs
- **TTS**: http://127.0.0.1:8004/docs
- **Avatar Asset**: http://127.0.0.1:8003/docs
- **Ollama**: http://127.0.0.1:11434
- **Build Server**: http://localhost:8000

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

## Note

- **Primo avvio TTS**: il download del modello XTTS v2 richiede ~2GB e può richiedere alcuni minuti
- **GPU**: TTS utilizzerà automaticamente CUDA se disponibile (molto più veloce)
- **OCR**: configurato per italiano+inglese, modificabile con env `RAG_OCR_LANG`
- **Memoria RAG**: i database per avatar sono salvati in `backend/rag_store/`

## Troubleshooting

### "Ollama non raggiungibile"
Verifica che Ollama sia avviato: `ollama serve`

### "OCR non disponibile"
Installa Tesseract e verifica il percorso in `rag_server.py` (riga 117)

### "CUDA out of memory"
Usa CPU per TTS: `set COQUI_TTS_DEVICE=cpu` prima di avviare

### Conflitto porte
Modifica le porte in `ai_services.cmd` o termina i processi esistenti
