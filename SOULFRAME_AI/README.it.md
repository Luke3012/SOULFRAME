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
- **[1] Start servizi** - avvia Ollama, Whisper, RAG, TTS e il frontend configurato
- **[2] Stop servizi** - termina tutti i processi
- **[3] Restart servizi** - stop + start in sequenza
- **[4] Debug console** - avvia i servizi backend in modalità console/debug
- **[5] Configura frontend default** - scegli WebGL o eseguibile Windows

**Cosa fa ai_services.cmd:**
- Rileva automaticamente l'ambiente virtuale Python (`backend\venv` o `backend\.venv`)
- Verifica se le porte sono già in uso (evita duplicati)
- Può lavorare sia in console mode sia in background mode a seconda dell'azione scelta
- Configura le variabili d'ambiente necessarie per Whisper/RAG/TTS
- Fornisce link diretti alle UI Swagger (`/docs`)
- In modalità WebGL avvia il Build Server in `..\Build` (o `SOULFRAME_WEBGL_BUILD_DIR`) e apre `http://localhost:8000`
- In modalità Windows lancia `..\Build_Windows64\SOULFRAME.exe` (o `SOULFRAME_WINDOWS_EXE`)
- Salva la modalità frontend scelta in `ai_services.mode.cfg`

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

Note:

- L'endpoint Build Server vale solo quando il frontend selezionato è WebGL.
- In modalità Windows viene avviato l'eseguibile nativo invece della build browser.

## Strumenti di validazione e regressione

La cartella `tools/` contiene le batterie PowerShell usate per rendere più affidabile il setup piccolo `llama3:8b-instruct-q4_K_M` contro i problemi che contano in questo progetto: retrieval mancati, riepiloghi multi-sorgente deboli, derive identitarie e incoerenze di persona.

- Panoramica e uso: `tools/README.it.md`
- Script principali: `run_extreme_stress_test.ps1`, `run_text_coherence_identity_test.ps1`, `run_complex_deficit_case_study.ps1`
- Output tipico: report Markdown scritti sul Desktop con sintesi pass/fail ed esempi dettagliati

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
- per aggiornare backend/script su VM usare `sudo sfadmin` (opzione `[1]`), che può anche ripulire i file sorgente nella update dir dopo conferma.

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

### Empirical test mode

Il frontend può avviare una sessione empirica digitando `T-E-S-T` nel `MainMenu`.
Quando succede, le richieste ai servizi backend includono `empirical_test_mode=true`.

Sul backend questo mantiene separate le esecuzioni empiriche da quelle normali:

- la memoria RAG usa `backend/empirical_test/rag_store/`
- i log conversazione RAG usano di default `backend/empirical_test/log/`, oppure `EMPIRICAL_RAG_LOG_DIR` se configurata
- voci avatar e wait phrases del TTS usano la root empirica dedicata
- l'Avatar Asset Server usa `backend/empirical_test/avatar_store/`

Anche in questa modalità il logging conversazionale funziona come in MainMode standard: un file per sessione, ma scritto sotto l'area storage empirica.

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

Se non esiste `..\Build`, imposta la variabile ambiente `SOULFRAME_WEBGL_BUILD_DIR` con il path completo:

```powershell
set SOULFRAME_WEBGL_BUILD_DIR=C:\Path\To\Build
ai_services.cmd 1
```

Variabili equivalenti attualmente supportate da `ai_services.cmd`:

- `SOULFRAME_WEBGL_BUILD_DIR` per la cartella build WebGL
- `SOULFRAME_WINDOWS_EXE` per il path dell'eseguibile Windows

Per cambiare gli altri parametri su Windows modifica direttamente `ai_services.cmd`.

## Note

- **Primo startup TTS**: il download del modello XTTS v2 richiede circa 2GB e può richiedere alcuni minuti.
- **GPU**: il TTS usa automaticamente CUDA se disponibile, con prestazioni molto migliori.
- **OCR**: configurato di default per italiano + inglese, modificabile tramite la variabile ambiente `RAG_OCR_LANG`.
- **Memoria RAG**: i database per avatar sono salvati in `backend/rag_store/`.
- **Log conversazioni**: sono salvati per avatar in `backend/log/` (una sessione MainMode = un file `.log`).
- **Log empirical test**: sono salvati separatamente sotto la root storage empirica, così i test guidati non inquinano la cronologia standard degli avatar.

### Warmup Coqui al boot

Dopo l'avvio del servizio TTS, il backend esegue una breve inizializzazione/warmup del modello Coqui
usando una frase corta (`"ciao"`). Questa è in genere la fase più lenta del primo startup.

Nel frontend Unity, questa fase è rappresentata da uno stato di inizializzazione dedicato
(loading panel e animazioni correlate), e l'interfaccia completa diventa disponibile solo quando TTS è pronto.

### Warmup RAG/Ollama al boot

All'avvio del servizio RAG, `rag_server` esegue un warmup best-effort di Ollama:

- step embedding su `/api/embed` (modello `EMBED_MODEL`)
- step chat su `/api/chat` (modello `CHAT_MODEL`, con `num_predict` ridotto)

Se Ollama non è raggiungibile in quel momento, il warmup viene loggato come warning ma
`rag_server` resta attivo e non va in crash durante l'avvio.

Nel bootstrap Unity viene atteso anche `RAG /health`, insieme a `TTS /health`,
prima di considerare il sistema completamente pronto.

## Troubleshooting

### "Ollama non raggiungibile"
Verifica che Ollama sia in esecuzione: `ollama serve`

### "OCR non disponibile"
Installa Tesseract e verifica il path in `rag_server.py` (`TESSERACT_CMD`)

### "CUDA out of memory"
Usa la CPU per il TTS: `set COQUI_TTS_DEVICE=cpu` prima di avviare il servizio

### Conflitto porte
Cambia le porte in `ai_services.cmd` oppure ferma i processi esistenti

### "TTS error: HTTP 500" su `/api/tts/tts_stream`

1. Verifica lo stato del servizio:

    ```bash
    sudo systemctl status soulframe-tts --no-pager
    sudo journalctl -u soulframe-tts -n 200 --no-pager
    ```

2. Controlla che il file voce di default esista:

    ```bash
    ls -lh /opt/soulframe/backend/voices/default.wav
    ```

3. Se nei log compare:

    `ImportError: cannot import name 'isin_mps_friendly' from transformers.pytorch_utils`

    forza il set compatibile di pacchetti:

    ```bash
    /opt/soulframe/.venv/bin/pip install --upgrade "transformers==4.57.1" "tokenizers==0.22.1"
    sudo systemctl restart soulframe-tts
    ```

4. Se nei log compare:

    `From Pytorch 2.9, the torchcodec library is required for audio IO`

    installa i pacchetti codec richiesti:

    ```bash
    sudo /opt/soulframe/.venv/bin/pip install --upgrade "coqui-tts[codec]==0.27.5" "torchcodec>=0.8.0"
    sudo systemctl restart soulframe-tts
    ```

5. Se i log mostrano il prompt licenza Coqui con `EOFError: EOF when reading a line`, imposta:

    ```bash
    echo 'COQUI_TOS_AGREED=1' | sudo tee -a /etc/soulframe/soulframe.env
    sudo systemctl restart soulframe-tts
    ```

    (Usalo solo se accetti i termini di licenza CPML / commerciale Coqui.)

6. Se `pip` fallisce con `Permission denied` su `/opt/soulframe/.venv/...`, esegui il comando `pip` con `sudo`.
7. Aggiorna `coqui_tts_server.py` all'ultima versione e riavvia il servizio.

### "wait_phrase ... 404 Not Found"

Le versioni recenti del backend provano a rigenerare automaticamente la wait phrase. Se il problema persiste:

1. Controlla l'endpoint:

    ```bash
    curl -I "https://<domain>/api/tts/wait_phrase?avatar_id=LOCAL_model1&name=un_secondo"
    ```

2. Genera esplicitamente le wait phrases:

    ```bash
    curl -X POST https://<domain>/api/tts/generate_wait_phrases \
      -F "avatar_id=LOCAL_model1" -F "language=it"
    ```

3. Riavvia `soulframe-tts`.

### Setup voce (frase lunga)

Il flusso attuale usa una frase di setup più ricca (target >= 50 parole) per migliorare la qualità del profilo vocale locale.

### "Download glb failed: 404 Not Found"

Se il frontend riceve un 404 su `/avatars/{id}/model.glb`:

1. Controlla gli endpoint health/list del servizio avatar:

    ```bash
    curl http://127.0.0.1:8003/health
    curl http://127.0.0.1:8003/avatars/list
    ```

2. Aggiorna `avatar_asset_server.py` all'ultima versione e riavvia il servizio:

    ```bash
    sudo systemctl restart soulframe-avatar.service
    ```

3. In produzione dietro Caddy, assicurati che il browser chiami `/api/avatar/...` e non `127.0.0.1` direttamente.
