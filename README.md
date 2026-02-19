# SOULFRAME

SOULFRAME e' una piattaforma WebGL con avatar interattivi e backend AI.
L'idea e' semplice: scegli o crei un avatar, parli, il sistema capisce la voce, ragiona con memoria contestuale e risponde in audio.

## Cosa fa, in pratica

- Gestione avatar 3D locali e importati (con cache server-side degli asset `.glb`).
- Conversazione vocale end-to-end: registrazione, trascrizione, risposta, sintesi vocale.
- Memoria per avatar con RAG persistente (documenti, immagini, note testuali).
- Deploy sia locale (Windows) sia server (Ubuntu) con script dedicati.

## Dove gira

### 1) Locale su Windows

- Setup ambiente con `SOULFRAME_SETUP/setup_soulframe_windows.bat`.
- Avvio/stop/restart servizi con `SOULFRAME_AI/ai_services.cmd`.
- Gestione update/deploy con `SoulframeControlCenter.bat`:
  - `s/c/r`: avvio, chiusura, riavvio servizi (`SOULFRAME_AI/ai_services.cmd 1/2/3`)
  - switch stream Git A/B (`.git`, `.git_stream_a`, `.git_stream_b`)
  - `git push`/`git pull` da menu
  - ripristino commit (soft revert o hard reset con conferma)
  - creazione pacchetto `soulframe_update` per Ubuntu
- Workflow tipico: sviluppo rapido e test funzionali in locale.

### 2) Setup automatico su Ubuntu

- Installazione e provisioning con `SOULFRAME_SETUP/setup_soulframe_ubuntu.sh`.
- Gestione operativa con `SOULFRAME_SETUP/sf_admin_ubuntu.sh` (alias `sfadmin`).
- Include servizi `systemd`, reverse proxy Caddy, update guidati da `soulframe_update` (Build.zip + backend/setup), backup e opzioni di shutdown.

## Python e requirements

- Versioni consigliate: Python 3.11 e 3.12.
- Su Windows, lo script crea il venv con `py -3.11` di default.
- Su Ubuntu, il setup seleziona automaticamente la versione disponibile (priorita: 3.12, poi 3.11, poi 3.10).
- I requirements sono allineati tra ambienti:
  - `SOULFRAME_AI/backend/requirements.txt` (deploy backend)
  - `SOULFRAME_SETUP/requirements.txt` (riferimento setup Linux)

## Design interattivo e UX

Il frontend Unity e' pensato per essere diretto da usare:

- comandi da tastiera chiari (es. `SPACE` per parlare, `Enter` per confermare, `Esc/Back` per tornare),
- hint contestuali a schermo (hint bar) in base allo stato UI,
- transizioni leggere e piccole animazioni per non spezzare il flusso,
- background rings dinamici che accompagnano boot, setup e operazioni lunghe.

## Pipeline AI (STT, RAG, TTS)

- STT: Whisper trascrive l'audio (`/transcribe`).
- RAG: il backend usa Ollama + ChromaDB per memoria per-avatar, con ricerca ibrida semantica + keyword.
- TTS: Coqui XTTS v2 genera la risposta vocale (`/tts`, `/tts_stream`).

### Inizializzazione Coqui al boot

All'avvio, Coqui-TTS viene inizializzato con una frase breve ("ciao") per fare warmup del modello.
Questa e' in genere la fase piu' lenta del boot TTS.

Per questo motivo il frontend mostra uno stato di inizializzazione dedicato:

- pannello di loading durante il bootstrap iniziale,
- transizioni UI e animazioni dei background rings per accompagnare l'attesa,
- ingresso dell'interfaccia completa solo quando il servizio TTS risulta pronto.

### Setup voce (profilo vocale avatar)

Nel setup voce:

- viene generata una frase italiana lunga (target 50-80 parole),
- la trascrizione della tua lettura viene confrontata con la frase attesa,
- se la similarita' e' almeno del 70%, il riferimento vocale viene salvato per quell'avatar,
- subito dopo vengono generate anche le wait phrases (es. "hm", "un_secondo") per la conversazione.

## Memoria: cosa puo' salvare

La memoria RAG puo' essere alimentata da:

- testo libero/note,
- documenti (PDF, TXT),
- immagini.

Dettagli importanti:

- per i PDF viene usato OCR in modo esplicito, non solo testo embedded;
- per le immagini c'e' OCR e, quando configurato, descrizione semantica con Gemini Vision;
- tutto viene salvato per avatar, quindi ogni profilo mantiene il suo contesto separato.

## MainMode

MainMode e' la fase operativa della conversazione:

1. tieni premuto `SPACE` per parlare,
2. rilascio -> trascrizione Whisper,
3. richiesta al RAG con memoria dell'avatar,
4. risposta vocale in streaming via Coqui TTS,
5. UI aggiornata con stato, testo utente e risposta.

Da MainMode puoi anche tornare rapidamente a setup voce/setup memoria se vuoi aggiornare il profilo.

## Limitazioni WebGL (Lip Sync)

Il lip sync di Unity in WebGL ha limitazioni note rispetto all'esecuzione in Play Mode/Desktop.

- Sono stati applicati fix per mantenere la bocca piu' aperta durante la parlata.
- Nonostante questi fix, il movimento labiale in WebGL puo' risultare meno preciso/naturale.

## Tesi (LaTeX)

La cartella `Tesi/` e' inclusa nel repository e contiene i sorgenti LaTeX del progetto di tesi
(`main.tex`, capitoli, bibliografia, classe e risorse).

Note di versionamento:

- vengono tracciati i file sorgente (`.tex`, `.bib`, `.cls`, risorse);
- i file temporanei/generati dalla compilazione LaTeX (`.aux`, `.log`, `.toc`, ecc.) sono ignorati dal `.gitignore`.

## Struttura repo

- `Assets/`: frontend Unity (UI flow, avatar management, WebGL bridge).
- `SOULFRAME_AI/`: servizi AI (Whisper, RAG, TTS, Avatar Asset Server).
- `SOULFRAME_SETUP/`: script setup e amministrazione Windows/Linux.
- `Tesi/`: sorgenti LaTeX della tesi e materiali correlati.

## Documentazione tecnica

- Setup Linux/Ubuntu: `SOULFRAME_SETUP/README.md`
- Backend AI (Whisper/RAG/TTS/Avatar): `SOULFRAME_AI/README.md`
