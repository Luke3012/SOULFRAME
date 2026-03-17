# SOULFRAME

[🇬🇧 English](#) | [🇮🇹 Italiano](README.it.md)

---

SOULFRAME is a cross-platform experience for interactive avatars with AI backend, currently maintained for both WebGL and Windows.
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

### Unity Build: WebGL + Windows x64

- Unity menu supports both targets:
  - `SOULFRAME/Build/Build WebGL`
  - `SOULFRAME/Build/Build Windows x64`
- Build script: `Assets/Editor/SoulframeBuildMenu.cs`.
- Default output folders:
  - WebGL: `Build/`
  - Windows: `Build_Windows64/SOULFRAME.exe`
- CLI support (batchmode):
  - `-executeMethod SoulframeBuildMenu.BuildWebGLCli`
  - `-executeMethod SoulframeBuildMenu.BuildWindows64Cli`
- The build menu switches the active build target before building to avoid editor/player symbol mismatch issues.
- WebGL builds are emitted with clean build cache, hashed filenames, and browser data caching disabled to reduce stale-cache/runtime mismatch issues between consecutive builds.

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

## Empirical Test Mode

SOULFRAME also includes an `empirical test mode` designed for guided test sessions.

- From `MainMenu`, type the key sequence `T-E-S-T` to toggle it.
- When enabled, the frontend shows a dedicated badge in the menu and propagates `empirical_test_mode=true` to the backend services.
- The mode uses isolated backend data paths so test sessions do not mix with the normal avatar memory, voice references, or cached avatar assets.
- MainMode conversations are still logged, but they are written to the empirical test log area instead of the standard one.

Operational note:

- in the initial empirical flow, setup memory is guided and file/image ingestion is restricted;
- the avatar library also exposes empirical-only filtering/switching behaviors used during test sessions.

## Avatar Conversation Logs

The backend saves a persistent log for every MainMode conversation.

- when you enter MainMode a new session and log file are created;
- logs are separated per avatar in `SOULFRAME_AI/backend/log/<avatar_id>/`;
- each turn is progressively appended to the current session file;
- each block contains user input (`keyboard` or `voice`) and textual RAG output;
- existing files are not automatically deleted.

Example filename:

- `SOULFRAME_AI/backend/log/LOCAL_model1/20260303_151530_a1b2c3d4.log`

When empirical test mode is active, the same session logic is used but logs are redirected to the empirical test storage area, keeping standard runs and empirical runs separated.

## WebGL Limitations (Lip Sync)

Unity's lip sync in WebGL has known limitations compared to Play Mode/Desktop execution.

- Fixes have been applied to keep the mouth more open during speech.
- Despite these fixes, lip movement in WebGL may be less precise/natural.

## Platform Behavior (WebGL vs Windows)

- WebGL reply rendering is configured without word-by-word text flow.
- TTS stream requests include a client platform flag so backend behavior can differ between WebGL and native builds.
- File/image memory ingestion uses the native OS picker on Windows and a dedicated browser file picker bridge on WebGL.
- On Windows, the scene uses a dedicated runtime scaler (`WindowsResolutionScaler`) for 3D rendering performance tuning:
  - 3D render scale is configurable from Inspector.
  - UI/canvas remains full-resolution.

## Local Frontend Modes on Windows

When running locally through `SOULFRAME_AI/ai_services.cmd`, the frontend can be started in two modes:

- WebGL: starts the static build server from `Build/` and opens the browser on `http://127.0.0.1:8000`
- Windows: launches `Build_Windows64/SOULFRAME.exe`

The selected mode is persisted in `SOULFRAME_AI/ai_services.mode.cfg` and can be changed from the script menu.

## Avaturn on Desktop (Windows)

- Desktop/Editor uses an external browser bridge with local callback listener.
- Typical flow:
  - Unity opens the local bridge page.
  - Avatar export posts JSON to local callback (`/avaturn-callback`).
  - Unity receives payload and continues the normal avatar import pipeline.
- The post-export bridge page is now single-tab/fullscreen style ("Return to SOULFRAME") with no forced browser auto-close.

Notes:

- Browser auto-close cannot be guaranteed by web standards when the tab is not script-closable.
- If callback port is occupied, change `callbackPort` in `AvaturnWebController`.

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
- AI validation and regression scripts: `SOULFRAME_AI/tools/README.md`
