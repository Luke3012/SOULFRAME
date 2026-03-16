# SOULFRAME Setup (Ubuntu)

[🇬🇧 English](#) | [🇮🇹 Italiano](README.it.md)

---

This folder contains deployment and management scripts for Ubuntu VMs.

## Contents

- `setup_soulframe_ubuntu.sh`
  - complete server installation and configuration.
- `sf_admin_ubuntu.sh`
  - admin console (`sfadmin`) for updates, services, and backups.
- `requirements.txt`
  - Python dependencies reference for Linux.

## Expected VM layout

```text
/opt/soulframe
+-- backend
|   +-- avatar_asset_server.py
|   +-- coqui_tts_server.py
|   +-- rag_server.py
|   +-- whisper_server.py
|   +-- requirements.txt
|   +-- rag_store/
|   +-- log/
|   +-- avatar_store/
|   `-- voices/
+-- webgl
|   +-- index.html
|   +-- Build/
|   +-- StreamingAssets/
|   `-- TemplateData/
+-- SOULFRAME_SETUP/
|   +-- setup_soulframe_ubuntu.sh
|   `-- sf_admin_ubuntu.sh
`-- backups/
    `-- backend_update_YYYYMMDD_HHMMSS/

/etc/soulframe
+-- soulframe.env
`-- idle.env

/usr/local/bin
+-- sfctl
+-- sfadmin
+-- sfurl
`-- idle_shutdown.sh

/home/<current_user>
`-- soulframe_update
```

## setup_soulframe_ubuntu.sh

### What it does

- Installs system dependencies (`ffmpeg`, `tesseract`, `nano`, `rsync`, toolchain).
- Automatically detects the available Python version (`python3.12`, `3.11`, `3.10`, fallback `python3`).
- Installs and starts Ollama.
- Installs Caddy and generates a `Caddyfile` with reverse proxy rules:
  - `/api/whisper/* -> 127.0.0.1:8001`
  - `/api/rag/* -> 127.0.0.1:8002`
  - `/api/avatar/* -> 127.0.0.1:8003`
  - `/api/tts/* -> 127.0.0.1:8004`
- Prepares Caddy logs with the correct permissions (`/var/log/caddy/access.log`, owner `caddy:caddy`).
- Creates the virtual environment in `/opt/soulframe/.venv`.
- Explicitly creates these directories:
  - `/opt/soulframe/webgl`
  - `/opt/soulframe/backups`
- Installs Python dependencies from `/opt/soulframe/backend/requirements.txt`.
  - If that file is missing, it tries to generate it with `pipreqs`.
- Creates `systemd` units for the AI services.
- Creates helper commands:
  - `sfctl` (AI service management)
  - `sfurl` (URL and health checks)
  - `sfadmin` (admin console)
- If you run it from a directory different from `/opt/soulframe`, it automatically copies:
  - `setup_soulframe_ubuntu.sh` and `sf_admin_ubuntu.sh` into `/opt/soulframe/SOULFRAME_SETUP`
  - detected backend files into `/opt/soulframe/backend` (from the project root or from `soulframe_update`)
- If it finds `SOULFRAME.zip` in the same directory as the script, it extracts it automatically
  (it must contain `soulframe_update/`).
- After copying, it automatically relaunches setup from `/opt/soulframe/SOULFRAME_SETUP`.
- Once the relaunch is complete, it attempts to remove the original bootstrap script
  (only if it came from `soulframe_update`).
- Automatically normalizes line endings (`CRLF -> LF`) for copied Linux scripts.
- Automatically creates an update directory for the current user:
  - default: `/home/<current_user>/soulframe_update`
  - also exported as `SOULFRAME_UPDATE_DIR` in `soulframe.env`.
- Also creates the required backend directories if they are missing:
  - `/opt/soulframe/backend/avatar_store`
  - `/opt/soulframe/backend/rag_store`
  - `/opt/soulframe/backend/log`
  - `/opt/soulframe/backend/voices`
  - `/opt/soulframe/backend/voices/avatars`
- Configures idle auto-shutdown with a timer.

### Useful variables

- `CHAT_MODEL_DEFAULT` (default: `llama3.1:8b`)
- `EMBED_MODEL_DEFAULT` (default: `nomic-embed-text`)
- `WHISPER_MODEL_DEFAULT` (default: `medium`)
- `RAG_LOG_DIR` (default generated env: `/home/<runtime_user>/soulframe-logs`, fallback: `/opt/soulframe/backend/log`)
- `SKIP_OLLAMA_PULL=1` to skip downloading Ollama models
- `TORCH_INSTALL_CMD` to force a specific torch/torchaudio build (for example CUDA)
- `UPDATE_DROP_DIR` to customize the automatic update directory

### Important notes

- `torch/torchaudio` are installed from `requirements.txt`.
- If you want a specific CUDA build, use `TORCH_INSTALL_CMD`.
- `soulframe.env` is created with `640` permissions; if `setfacl` is available, the script tries to grant read access to the user who invoked `sudo`.
- Re-running `setup_soulframe_ubuntu.sh` does not create duplicate services or helper commands: unit files and helper scripts are overwritten idempotently.

## sfctl

```bash
sfctl start|stop|restart|status|logs [whisper|rag|avatar|tts|ollama|all]
```

## sfadmin (sf_admin_ubuntu.sh)

Start:

```bash
sudo sfadmin
```

Menu:

- `[1]` Update everything (Build ZIP + backend/setup)
- `[2]` Shut down the server (stop AI services + Caddy)
- `[3]` Restart the server
- `[4]` Edit parameters (`/etc/soulframe/soulframe.env`, `/etc/soulframe/idle.env`)
- `[5]` Start the server
- `[6]` Shut down the VM (machine shutdown)
- `[7]` Manage backups (restore, delete one, delete all, or keep the latest `N`)
- `[8]` Configure the RAG log path (`RAG_LOG_DIR`)
- `[0]` Exit

RAG log note:

- option `[8]` automatically creates the selected directory and tries to assign it to the runtime user
- existing logs from the previous path are not migrated automatically

### Automatic update directory

`sfadmin` uses `UPDATE_DIR` with this priority:

1. the `UPDATE_DIR` environment variable, if set
2. the `SOULFRAME_UPDATE_DIR` value in `/etc/soulframe/soulframe.env`
3. fallback `/home/<current_user>/soulframe_update`.

### Unified update (option `[1]`)

During `[1]`, `sfadmin` performs the following in sequence:

- build update from ZIP (`Build.zip` preferred, otherwise the latest `.zip` in `UPDATE_DIR`)
- backend/setup update from supported files in `UPDATE_DIR`

Supported files:

- `avatar_asset_server.py`
- `coqui_tts_server.py`
- `rag_server.py`
- `whisper_server.py`
- `requirements.txt` (copied into `/opt/soulframe/backend/requirements.txt`)
- `setup_soulframe_ubuntu.sh`
- `sf_admin_ubuntu.sh`

If you update `setup_soulframe_ubuntu.sh` and/or `sf_admin_ubuntu.sh`, `sfadmin` automatically runs:

```bash
install -m 755 "$SETUP_DIR/sf_admin_ubuntu.sh" /usr/local/bin/sfadmin
cd "$SETUP_DIR" && SKIP_OLLAMA_PULL=1 ./setup_soulframe_ubuntu.sh
```

For the build part, if you leave the ZIP path empty, it automatically uses:
- `Build.zip` first, if present in `UPDATE_DIR`
- otherwise the most recent ZIP file found in `UPDATE_DIR`

### Update file cleanup after confirmation

After a successful update:

- it can delete the source ZIP file
- it can delete the applied backend/setup source files

Cleanup behavior:

- always asks for explicit confirmation (`[y/N]` equivalent of the current prompt)
- only deletes files inside `UPDATE_DIR`

### Backup management (option `[7]`)

`Gestione Backup` shows a single catalog ordered by recency for:

- `webgl_backup_*` (WebGL build snapshots)
- `backend_update_*` (backend/setup snapshots)

For each entry, `sfadmin` shows:

- backup type
- full path
- human-readable size when available

Available actions inside the submenu:

- restore one selected backup
- delete one selected backup
- delete all backups
- keep only the latest `N` backups

Restore behavior:

- selecting a `webgl_backup_*` backup restores only `/opt/soulframe/webgl`
- selecting a `backend_update_*` backup restores only the files contained in that snapshot under `/opt/soulframe`
- before restoring, `sfadmin` stops the stack with `sfctl stop all`
- before overwriting anything, `sfadmin` creates a safety backup using the same naming conventions
- after a successful restore, `sfadmin` restarts the stack automatically with `sfctl restart all`
- if the restored backend snapshot includes `setup_soulframe_ubuntu.sh` and/or `sf_admin_ubuntu.sh`, `sfadmin` reinstalls the helper and reruns:

```bash
cd "$SETUP_DIR" && SKIP_OLLAMA_PULL=1 ./setup_soulframe_ubuntu.sh
```

Restore scope note:

- restore is always based on one selected backup
- `backend_update_*` restores only the files present in that snapshot; it does not delete newer files that are not part of the backup

## Idle auto-shutdown

Configuration in `/etc/soulframe/idle.env`:

- `IDLE_MINUTES` (default 30)
- `STARTUP_GRACE_MINUTES` (default 10)
- `LOG_FILE` (default `/var/log/caddy/access.log`)
- `LOG_TAIL_LINES` (default 20000, analyzes the latest lines of the Caddy JSON log)
- `TRACK_SSH_ACTIVITY` (default 1, includes activity on `pts/*` terminals)
- `DRY_RUN` (default 0)

`idle_shutdown.sh`:

- does not shut down during the first `STARTUP_GRACE_MINUTES` after boot
- only considers web activity on `/api/*` endpoints (ignores static requests and public pages)
- if `TRACK_SSH_ACTIVITY=1`, also considers interactive SSH activity
- shuts down the VM only after the idle threshold is exceeded

Quick test:

```bash
sudo sed -i 's/^DRY_RUN=.*/DRY_RUN=1/' /etc/soulframe/idle.env
sudo /usr/local/bin/idle_shutdown.sh
```

## Caddy and domain

Prerequisites:

- DNS `A` record pointing the domain to the VM public IP
- firewall open on TCP `80` and `443`

Useful commands:

```bash
sudo caddy validate --config /etc/caddy/Caddyfile
sudo systemctl status caddy --no-pager
```

## Typical deployment

```bash
cd /path/where/your/files/are
sudo ./setup_soulframe_ubuntu.sh
sudo sfadmin
```

### Deployment from a single ZIP package

If you upload a single `SOULFRAME.zip` file, place it in the same directory where you run `setup_soulframe_ubuntu.sh`.
The script will automatically extract `soulframe_update/`, copy the files into `/opt/soulframe/SOULFRAME_SETUP`,
relaunch itself from there, and continue the setup.

## Permissions and line endings (CRLF/LF)

`/opt/soulframe` and files under `/opt` are normally owned by `root`.
Use `sudo` to modify or install files in those directories.

The script already tries to normalize Linux script line endings automatically.
If you need to force it manually, use:

```bash
sudo sed -i 's/\r$//' setup_soulframe_ubuntu.sh
sudo sed -i 's/\r$//' sf_admin_ubuntu.sh
sudo chmod +x setup_soulframe_ubuntu.sh sf_admin_ubuntu.sh
sudo ./setup_soulframe_ubuntu.sh
```
