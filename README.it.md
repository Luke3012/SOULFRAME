> [🇬🇧 English](README.md) | **🇮🇹 Italiano**

# SOULFRAME

SOULFRAME è una piattaforma WebGL con avatar interattivi e backend AI.
L'idea è semplice: scegli o crei un avatar, parli, il sistema capisce la voce, ragiona con memoria contestuale e risponde in audio.

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

### Build Unity: WebGL + Windows x64

- E' disponibile il menu Unity `SOULFRAME/Build/Build WebGL` e `SOULFRAME/Build/Build Windows x64`.
- Script editor: `Assets/Editor/SoulframeBuildMenu.cs`.
- Output predefiniti:
  - WebGL: cartella `Build/`
  - Windows: `Build_Windows64/SOULFRAME.exe`
- Supporto CLI (batchmode):
  - `-executeMethod SoulframeBuildMenu.BuildWebGLCli`
  - `-executeMethod SoulframeBuildMenu.BuildWindows64Cli`
- Il menu build forza anche il target attivo corretto (`SwitchActiveBuildTarget`) prima della compilazione, per evitare mismatch di simboli editor/player.

### 2) Setup automatico su Ubuntu

- Installazione e provisioning con `SOULFRAME_SETUP/setup_soulframe_ubuntu.sh`.
- Gestione operativa con `SOULFRAME_SETUP/sf_admin_ubuntu.sh` (alias `sfadmin`).
- Include servizi `systemd`, reverse proxy Caddy, update guidati da `soulframe_update` (Build.zip + backend/setup), backup e opzioni di shutdown.

## Python e requirements

- Versioni consigliate: Python 3.11 e 3.12.
- Su Windows, lo script crea il venv con `py -3.11` di default.
- Su Ubuntu, il setup seleziona automaticamente la versione disponibile (priorità: 3.12, poi 3.11, poi 3.10).
- I requirements sono allineati tra ambienti:
  - `SOULFRAME_AI/backend/requirements.txt` (deploy backend)
  - `SOULFRAME_SETUP/requirements.txt` (riferimento setup Linux)

## Design interattivo e UX

Il frontend Unity è pensato per essere diretto da usare:

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
Questa è in genere la fase più lenta del boot TTS.

Per questo motivo il frontend mostra uno stato di inizializzazione dedicato:

- pannello di loading durante il bootstrap iniziale,
- transizioni UI e animazioni dei background rings per accompagnare l'attesa,
- ingresso dell'interfaccia completa solo quando il servizio TTS risulta pronto.

### Setup voce (profilo vocale avatar)

Nel setup voce:

- viene generata una frase italiana lunga (target 50-80 parole),
- la trascrizione della tua lettura viene confrontata con la frase attesa,
- se la similarità è almeno del 70%, il riferimento vocale viene salvato per quell'avatar,
- subito dopo vengono generate anche le wait phrases (es. "hm", "un_secondo") per la conversazione.

## Memoria: cosa può salvare

La memoria RAG può essere alimentata da:

- testo libero/note,
- documenti (PDF, TXT),
- immagini.

Dettagli importanti:

- per i PDF viene usato OCR in modo esplicito, non solo testo embedded;
- per le immagini c'è OCR e, quando configurato, descrizione semantica con Gemini Vision;
- tutto viene salvato per avatar, quindi ogni profilo mantiene il suo contesto separato.

## MainMode

MainMode è la fase operativa della conversazione:

1. tieni premuto `SPACE` per parlare,
2. rilascio -> trascrizione Whisper,
3. richiesta al RAG con memoria dell'avatar,
4. risposta vocale in streaming via Coqui TTS,
5. UI aggiornata con stato, testo utente e risposta.

Da MainMode puoi anche tornare rapidamente a setup voce/setup memoria se vuoi aggiornare il profilo.

## Empirical Test Mode

SOULFRAME include anche un `empirical test mode`, pensato per sessioni di prova guidate.

- Dal `MainMenu` si attiva o disattiva digitando in sequenza i tasti `T-E-S-T`.
- Quando è attivo, il frontend mostra un badge dedicato nel menu e propaga `empirical_test_mode=true` ai servizi backend.
- La modalità usa percorsi dati separati nel backend, così sessioni di test e uso normale non si mescolano tra memoria avatar, riferimenti vocali e cache dei modelli.
- Le conversazioni in MainMode continuano a essere loggate, ma finiscono nell'area log dell'empirical test invece che in quella standard.

Nota operativa:

- nel flusso empirico iniziale, il setup memoria è guidato e ingestione file/immagini è limitata;
- anche la libreria avatar espone filtri e switch dedicati al contesto di test.

## Log Conversazioni Avatar

Il backend salva un log persistente per ogni conversazione MainMode.

- quando entri in MainMode viene creata una nuova sessione e un nuovo file log;
- i log sono separati per avatar in `SOULFRAME_AI/backend/log/<avatar_id>/`;
- ogni turno viene appeso progressivamente nello stesso file della sessione corrente;
- ogni blocco contiene input utente (`keyboard` o `voice`) e output testuale del RAG;
- i file esistenti non vengono cancellati automaticamente.

Esempio nome file:

- `SOULFRAME_AI/backend/log/LOCAL_model1/20260303_151530_a1b2c3d4.log`

Quando l'empirical test mode è attivo, la logica di sessione resta la stessa ma i file vengono scritti nell'area storage dell'empirical test, così i test restano separati dalle esecuzioni standard.

## Limitazioni WebGL (Lip Sync)

Il lip sync di Unity in WebGL ha limitazioni note rispetto all'esecuzione in Play Mode/Desktop.

- Sono stati applicati fix per mantenere la bocca più aperta durante la parlata.
- Nonostante questi fix, il movimento labiale in WebGL può risultare meno preciso/naturale.

## Note Runtime Piattaforma (WebGL vs Windows)

- In WebGL la risposta testuale è configurata senza effetto word-by-word.
- Le richieste TTS inviano un flag `client_platform` per differenziare la logica backend tra WebGL e build native.
- In Windows è disponibile uno scaler runtime dedicato (`WindowsResolutionScaler`) per ridurre il carico del rendering 3D:
  - il rapporto pixel 3D è configurabile da Inspector;
  - l'UI/canvas resta a risoluzione piena.

## Avaturn su Desktop (Windows)

Su WebGL resta attivo il bridge iframe in pagina. Su Desktop/Editor e' stato aggiunto un fallback con browser esterno:

- Unity apre Avaturn nel browser con URL di callback locale.
- L'app avvia un listener locale su `http://127.0.0.1:37821/avaturn-callback`.
- Al callback, Unity riceve il payload avatar e riusa la pipeline esistente di import/download.
- Dopo l'export, la pagina bridge mostra una schermata finale fullscreen "Ritorna a SOULFRAME" nella stessa tab.
- Non viene usata una chiusura forzata del browser/processo.

Note pratiche:

- se la porta e' occupata, il flusso callback fallisce e va cambiata la porta nel componente `AvaturnWebController`;
- e' previsto timeout callback (default 180 secondi) con cleanup listener.

## SOULFRAME_THESIS (LaTeX)

La cartella `SOULFRAME_THESIS/` è inclusa nel repository e contiene i sorgenti LaTeX del progetto di tesi
(`main.tex`, capitoli, bibliografia, classe e risorse).

Note di versionamento:

- vengono tracciati i file sorgente (`.tex`, `.bib`, `.cls`, risorse);
- i file temporanei/generati dalla compilazione LaTeX (`.aux`, `.log`, `.toc`, ecc.) sono ignorati dal `.gitignore`.

## Struttura repo

- `Assets/`: frontend Unity (UI flow, avatar management, WebGL bridge).
- `SOULFRAME_AI/`: servizi AI (Whisper, RAG, TTS, Avatar Asset Server).
- `SOULFRAME_SETUP/`: script setup e amministrazione Windows/Linux.
- `SOULFRAME_THESIS/`: sorgenti LaTeX della tesi e materiali correlati.

## Documentazione tecnica

- Setup Linux/Ubuntu: `SOULFRAME_SETUP/README.md`
- Backend AI (Whisper/RAG/TTS/Avatar): `SOULFRAME_AI/README.md`
- Script di validazione e regressione AI: `SOULFRAME_AI/tools/README.it.md`
