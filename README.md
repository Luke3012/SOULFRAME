# SOULFRAME

[🇬🇧 English](#) | [🇮🇹 Italiano](README.it.md)

---

SOULFRAME is a WebGL platform with interactive avatars and AI backend.
The idea is simple: pick or create an avatar, speak, the system understands your voice, reasons with contextual memory, and responds in audio.

## What it does

- Management of local and imported 3D avatars (with server-side caching of `.glb` assets).
- End-to-end voice conversation: recording, transcription, response, speech synthesis.
- Avatar memory with persistent RAG (documents, images, text notes).
- Deployment both locally (Windows) and on server (Ubuntu) with dedicated scripts.

## Where it runs

### 1) Locally on Windows

- Environment setup with `SOULFRAME_SETUP/setup_soulframe_windows.bat`.
- Service start/stop/restart with `SOULFRAME_AI/ai_services.cmd`.
- Update/deploy management with `SoulframeControlCenter.bat`:
  - `s/c/r`: start, stop, restart services (`SOULFRAME_AI/ai_services.cmd 1/2/3`)
  - Git A/B stream switching (`.git`, `.git_stream_a`, `.git_stream_b`)
  - `git push`/`git pull` from menu
  - commit recovery (soft revert or hard reset with confirmation)
  - creation of `soulframe_update` package for Ubuntu
- Typical workflow: rapid development and functional testing locally.

### 2) Automated setup on Ubuntu

- Installation and provisioning with `SOULFRAME_SETUP/setup_soulframe_ubuntu.sh`.
- Operational management with `SOULFRAME_SETUP/sf_admin_ubuntu.sh` (alias `sfadmin`).
- Includes `systemd` services, Caddy reverse proxy, guided updates via `soulframe_update` (Build.zip + backend/setup), backups and shutdown options.

## Python and requirements

- Recommended versions: Python 3.11 and 3.12.
- On Windows, the script creates the venv with `py -3.11` by default.
- On Ubuntu, setup automatically selects the available version (priority: 3.12, then 3.11, then 3.10).
- Requirements are aligned across environments:
  - `SOULFRAME_AI/backend/requirements.txt` (backend deployment)
  - `SOULFRAME_SETUP/requirements.txt` (Linux setup reference)

## Interactive design and UX

The Unity frontend is designed to be straightforward to use:

- clear keyboard commands (e.g., `SPACE` to speak, `Enter` to confirm, `Esc/Back` to go back),
- contextual on-screen hints (hint bar) based on UI state,
- smooth transitions and subtle animations to maintain flow,
- dynamic background rings that accompany boot, setup, and long operations.

## AI Pipeline (STT, RAG, TTS)

- STT: Whisper transcribes audio (`/transcribe`).
- RAG: backend uses Ollama + ChromaDB for per-avatar memory, with hybrid semantic + keyword search.
- TTS: Coqui XTTS v2 generates voice responses (`/tts`, `/tts_stream`).

### Coqui initialization at boot

At startup, Coqui-TTS is initialized with a short phrase ("ciao") to warm up the model.
This is generally the slowest phase of TTS boot.

### RAG/Ollama warmup at boot

At startup, `rag_server` also runs a best-effort warmup on Ollama:

- embedding warmup on `/api/embed`,
- chat warmup on `/api/chat` with a very short generation.

If Ollama is not ready yet, warmup logs a warning but does not block service startup.

For this reason, the frontend displays a dedicated initialization state:

- loading panel during initial bootstrap,
- UI transitions and background ring animations to accompany the wait,
- full interface entry only when TTS and RAG services are ready.

### Voice setup (avatar voice profile)

In voice setup:

- a long Italian phrase is generated (target 50-80 words),
- transcription of your reading is compared with the expected phrase,
- if similarity is at least 70%, the voice reference is saved for that avatar,
- wait phrases (e.g., "hm", "un_secondo") are then generated for conversation.

## Memory: what it can save

RAG memory can be fed by:

- free text/notes,
- documents (PDF, TXT),
- images.

Important details:

- for PDFs, OCR is used explicitly, not just embedded text;
- for images there is OCR and, when configured, semantic description with Gemini Vision;
- everything is saved per avatar, so each profile maintains its own separate context.

## MainMode

MainMode is the operational phase of conversation:

1. hold `SPACE` to speak,
2. release -> Whisper transcription,
3. RAG request with avatar memory,
4. voice response streaming via Coqui TTS,
5. UI updated with state, user text, and response.

From MainMode you can also quickly return to voice setup/memory setup if you want to update the profile.

## Avatar Conversation Logs

The backend saves a persistent log for every MainMode conversation.

- when you enter MainMode a new session and log file are created;
- logs are separated per avatar in `SOULFRAME_AI/backend/log/<avatar_id>/`;
- each turn is progressively appended to the current session file;
- each block contains user input (`keyboard` or `voice`) and textual RAG output;
- existing files are not automatically deleted.

Example filename:

- `SOULFRAME_AI/backend/log/LOCAL_model1/20260303_151530_a1b2c3d4.log`

## WebGL Limitations (Lip Sync)

Unity's lip sync in WebGL has known limitations compared to Play Mode/Desktop execution.

- Fixes have been applied to keep the mouth more open during speech.
- Despite these fixes, lip movement in WebGL may be less precise/natural.

## SOULFRAME_THESIS (LaTeX)

The `SOULFRAME_THESIS/` folder is included in the repository and contains LaTeX thesis sources
(`main.tex`, chapters, bibliography, class, and resources).

Versioning notes:

- source files are tracked (`.tex`, `.bib`, `.cls`, resources);
- temporary/generated files from LaTeX compilation (`.aux`, `.log`, `.toc`, etc.) are ignored by `.gitignore`.

## Repo structure

- `Assets/`: Unity frontend (UI flow, avatar management, WebGL bridge).
- `SOULFRAME_AI/`: AI services (Whisper, RAG, TTS, Avatar Asset Server).
- `SOULFRAME_SETUP/`: setup scripts and Windows/Linux administration.
- `SOULFRAME_THESIS/`: LaTeX thesis sources and related materials.

## Technical documentation

- Linux/Ubuntu setup: `SOULFRAME_SETUP/README.md`
- Backend AI (Whisper/RAG/TTS/Avatar): `SOULFRAME_AI/README.md`
