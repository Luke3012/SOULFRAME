#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR=$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)
INSTALL_DIR=${INSTALL_DIR:-/opt/soulframe}
BACKEND_DIR="$INSTALL_DIR/backend"
WEBGL_DIR="$INSTALL_DIR/webgl"
BACKUPS_DIR="$INSTALL_DIR/backups"
VENV_DIR="$INSTALL_DIR/.venv"
ENV_DIR="/etc/soulframe"
ENV_FILE="$ENV_DIR/soulframe.env"
IDLE_ENV_FILE="$ENV_DIR/idle.env"
PYTHON_BIN=${PYTHON_BIN:-python3}
TORCH_INSTALL_CMD=${TORCH_INSTALL_CMD:-""}
EMBED_MODEL_DEFAULT=${EMBED_MODEL_DEFAULT:-nomic-embed-text}
CHAT_MODEL_DEFAULT=${CHAT_MODEL_DEFAULT:-llama3.1:8b}
WHISPER_MODEL_DEFAULT=${WHISPER_MODEL_DEFAULT:-medium}
SKIP_OLLAMA_PULL=${SKIP_OLLAMA_PULL:-0}
SOULFRAME_DOMAIN_INPUT=${SOULFRAME_DOMAIN:-}
PYTHON_APT_PKGS=()
RUNTIME_USER=${SUDO_USER:-}
RUNTIME_HOME=""
UPDATE_DROP_DIR=""
BACKEND_SOURCE_DIR=""
SOURCE_SETUP_DIR="$SCRIPT_DIR"
SETUP_TARGET_DIR="$INSTALL_DIR/SOULFRAME_SETUP"
BOOTSTRAP_ORIGIN_DIR="${SOULFRAME_BOOTSTRAP_ORIGIN_DIR:-}"
BACKEND_CORE_FILES=(
  avatar_asset_server.py
  coqui_tts_server.py
  rag_server.py
  whisper_server.py
)

if [[ $EUID -ne 0 ]]; then
  echo "[ERR] Esegui lo script come root (sudo)."
  exit 1
fi

SETUP_LOG_FILE=${SETUP_LOG_FILE:-/tmp/soulframe_setup_$(date +%Y%m%d_%H%M%S).log}
BOX_WIDTH=${BOX_WIDTH:-76}
touch "$SETUP_LOG_FILE" >/dev/null 2>&1 || true
chmod 600 "$SETUP_LOG_FILE" >/dev/null 2>&1 || true

repeat_char() {
  local char="$1"
  local count="${2:-0}"
  if (( count <= 0 )); then
    echo ""
    return 0
  fi
  printf "%${count}s" "" | tr ' ' "$char"
}

box_border() {
  printf '+%s+\n' "$(repeat_char '-' $((BOX_WIDTH - 2)))"
}

box_line() {
  local text="${1:-}"
  local inner_width=$((BOX_WIDTH - 4))
  local clipped="$text"
  if (( ${#clipped} > inner_width )); then
    clipped="${clipped:0:inner_width}"
  fi
  printf '| %-*s |\n' "$inner_width" "$clipped"
}

box_block() {
  local title="${1:-}"
  shift || true
  box_border
  box_line "$title"
  while (($#)); do
    box_line "$1"
    shift
  done
  box_border
}

run_quiet() {
  local step="$1"
  shift
  local pid=""
  local start_ts=0
  local now_ts=0
  local elapsed=0
  local frame_i=0
  local frame=""
  local clear_width=120
  local -a frames=('|' '/' '-' '\')

  box_block "[STEP] $step" "Log: $SETUP_LOG_FILE"

  "$@" >>"$SETUP_LOG_FILE" 2>&1 &
  pid=$!
  start_ts=$(date +%s)

  if [[ -t 1 ]]; then
    while kill -0 "$pid" 2>/dev/null; do
      now_ts=$(date +%s)
      elapsed=$((now_ts - start_ts))
      frame="${frames[$((frame_i % ${#frames[@]}))]}"
      printf '\r[WORK] %s %s (%ss)' "$frame" "$step" "$elapsed"
      frame_i=$((frame_i + 1))
      sleep 0.2
    done
    printf '\r%*s\r' "$clear_width" ""
  else
    while kill -0 "$pid" 2>/dev/null; do
      sleep 1
    done
  fi

  if wait "$pid"; then
    box_block "[OK] $step"
  else
    box_block "[ERR] $step" "Controlla: $SETUP_LOG_FILE"
    tail -n 40 "$SETUP_LOG_FILE" >&2 || true
    return 1
  fi
}

resolve_runtime_user() {
  local user_candidate=""

  if [[ -n "${SUDO_USER:-}" && "${SUDO_USER:-}" != "root" ]]; then
    echo "$SUDO_USER"
    return 0
  fi

  user_candidate=$(logname 2>/dev/null || true)
  if [[ -n "$user_candidate" && "$user_candidate" != "root" ]]; then
    echo "$user_candidate"
    return 0
  fi

  user_candidate=$(stat -c %U "$SCRIPT_DIR" 2>/dev/null || true)
  if [[ -n "$user_candidate" && "$user_candidate" != "root" ]]; then
    echo "$user_candidate"
    return 0
  fi

  echo "root"
}

resolve_home_dir() {
  local user_name="$1"
  local home_dir=""

  if [[ -n "$user_name" ]]; then
    home_dir=$(getent passwd "$user_name" | cut -d: -f6 || true)
  fi

  if [[ -z "$home_dir" ]]; then
    if [[ "$user_name" == "root" ]]; then
      home_dir="/root"
    elif [[ -n "$user_name" ]]; then
      home_dir="/home/$user_name"
    else
      home_dir="/opt/soulframe"
    fi
  fi

  echo "$home_dir"
}

has_backend_core_files() {
  local dir="$1"
  local file_name
  for file_name in "${BACKEND_CORE_FILES[@]}"; do
    if [[ ! -f "$dir/$file_name" ]]; then
      return 1
    fi
  done
  return 0
}

canonical_path() {
  local path="$1"
  if command -v realpath >/dev/null 2>&1; then
    realpath -m "$path" 2>/dev/null || echo "$path"
    return 0
  fi
  readlink -f "$path" 2>/dev/null || echo "$path"
}

paths_equal() {
  local left right
  left="$(canonical_path "$1")"
  right="$(canonical_path "$2")"
  [[ "$left" == "$right" ]]
}

copy_tree() {
  local src="$1"
  local dst="$2"
  mkdir -p "$dst"
  if command -v rsync >/dev/null 2>&1; then
    rsync -a --delete "$src/" "$dst/"
  else
    cp -a "$src/." "$dst/"
  fi
}

extract_soulframe_zip_if_present() {
  local zip_path="$SCRIPT_DIR/SOULFRAME.zip"
  local update_dir="$SCRIPT_DIR/soulframe_update"

  if [[ ! -f "$zip_path" ]]; then
    return 0
  fi

  if [[ -d "$update_dir" && -f "$update_dir/setup_soulframe_ubuntu.sh" ]]; then
    echo "[INFO] Cartella soulframe_update già presente: salto estrazione SOULFRAME.zip."
    return 0
  fi

  if ! command -v python3 >/dev/null 2>&1; then
    echo "[ERR] python3 non disponibile: impossibile estrarre $zip_path"
    echo "Estrai manualmente SOULFRAME.zip in $SCRIPT_DIR e riprova."
    exit 1
  fi

  echo "[INFO] Estraggo SOULFRAME.zip in $SCRIPT_DIR ..."
  python3 - "$zip_path" "$SCRIPT_DIR" <<'PY'
import os
import sys
import zipfile

zip_path = sys.argv[1]
dst = sys.argv[2]
os.makedirs(dst, exist_ok=True)
with zipfile.ZipFile(zip_path, "r") as zf:
    zf.extractall(dst)
PY

  if [[ ! -d "$update_dir" || ! -f "$update_dir/setup_soulframe_ubuntu.sh" ]]; then
    echo "[WARN] SOULFRAME.zip estratto ma cartella soulframe_update/setup_soulframe_ubuntu.sh non trovata."
    echo "[WARN] Continuo con la sorgente corrente: $SCRIPT_DIR"
  fi
}

resolve_setup_source_dir() {
  local -a candidates=(
    "$SCRIPT_DIR/soulframe_update"
    "$SCRIPT_DIR"
  )
  local c=""

  for c in "${candidates[@]}"; do
    if [[ -f "$c/setup_soulframe_ubuntu.sh" && -f "$c/sf_admin_ubuntu.sh" ]]; then
      (cd "$c" && pwd)
      return 0
    fi
  done

  (cd "$SCRIPT_DIR" && pwd)
}

resolve_backend_source_dir() {
  local -a candidates=()
  local origin_dir="${BOOTSTRAP_ORIGIN_DIR:-}"
  local current_setup_dir="${SOURCE_SETUP_DIR:-$SCRIPT_DIR}"
  local candidate=""

  if [[ -n "$origin_dir" ]]; then
    candidates+=(
      "$origin_dir/../SOULFRAME_AI/backend"
      "$origin_dir/SOULFRAME_AI/backend"
      "$origin_dir/backend"
      "$origin_dir"
    )
  fi

  candidates+=(
    "$current_setup_dir/../SOULFRAME_AI/backend"
    "$current_setup_dir/SOULFRAME_AI/backend"
    "$current_setup_dir/backend"
    "$current_setup_dir/soulframe_update"
    "$current_setup_dir"
    "$SCRIPT_DIR/soulframe_update"
    "$SCRIPT_DIR/../SOULFRAME_AI/backend"
    "$SCRIPT_DIR/SOULFRAME_AI/backend"
    "$SCRIPT_DIR/backend"
    "$SCRIPT_DIR"
    "$BACKEND_DIR"
  )

  for candidate in "${candidates[@]}"; do
    if [[ -d "$candidate" ]] && has_backend_core_files "$candidate"; then
      (cd "$candidate" && pwd)
      return 0
    fi
  done

  return 1
}

bootstrap_setup_location() {
  local -a args=("$@")
  local self_script target_script source_script

  extract_soulframe_zip_if_present
  SOURCE_SETUP_DIR="$(resolve_setup_source_dir)"

  mkdir -p "$SETUP_TARGET_DIR"
  target_script="$SETUP_TARGET_DIR/setup_soulframe_ubuntu.sh"
  self_script="$(canonical_path "${BASH_SOURCE[0]}")"

  if ! paths_equal "$SOURCE_SETUP_DIR" "$SETUP_TARGET_DIR"; then
    echo "[INFO] Copio setup in $SETUP_TARGET_DIR (sorgente: $SOURCE_SETUP_DIR)"
    copy_tree "$SOURCE_SETUP_DIR" "$SETUP_TARGET_DIR"
  fi

  if [[ -f "$SETUP_TARGET_DIR/setup_soulframe_ubuntu.sh" ]]; then
    sed -i 's/\r$//' "$SETUP_TARGET_DIR/setup_soulframe_ubuntu.sh"
    chmod +x "$SETUP_TARGET_DIR/setup_soulframe_ubuntu.sh"
  fi
  if [[ -f "$SETUP_TARGET_DIR/sf_admin_ubuntu.sh" ]]; then
    sed -i 's/\r$//' "$SETUP_TARGET_DIR/sf_admin_ubuntu.sh"
    chmod +x "$SETUP_TARGET_DIR/sf_admin_ubuntu.sh"
  fi

  if [[ "${SOULFRAME_BOOTSTRAPPED:-0}" != "1" ]]; then
    if [[ -f "$target_script" ]] && ! paths_equal "$self_script" "$target_script"; then
      echo "[INFO] Rilancio setup da: $target_script"
      SOULFRAME_BOOTSTRAPPED=1 \
      SOULFRAME_BOOTSTRAP_SOURCE="$self_script" \
      SOULFRAME_BOOTSTRAP_ORIGIN_DIR="$SOURCE_SETUP_DIR" \
      "$target_script" "${args[@]}"
      exit $?
    fi
  fi

  source_script="${SOULFRAME_BOOTSTRAP_SOURCE:-}"
  if [[ "${SOULFRAME_BOOTSTRAPPED:-0}" == "1" && -n "$source_script" && -f "$source_script" ]]; then
    if ! paths_equal "$source_script" "$target_script"; then
      if [[ "$source_script" == *"/soulframe_update/"* ]]; then
        rm -f "$source_script" || true
        echo "[INFO] Script bootstrap sorgente rimosso: $source_script"
      else
        echo "[INFO] Script bootstrap sorgente mantenuto: $source_script"
      fi
    fi
  fi
}

normalize_domain() {
  local d="$1"
  d="${d#http://}"
  d="${d#https://}"
  d="${d%%/*}"
  d="${d,,}"
  echo "$d"
}

ensure_env_key() {
  local file_path="$1"
  local key="$2"
  local value="$3"
  if [[ ! -f "$file_path" ]]; then
    return 0
  fi
  if ! grep -qE "^${key}=" "$file_path" 2>/dev/null; then
    echo "${key}=${value}" >> "$file_path"
  fi
}

resolve_python_packages() {
  local requested_bin="$1"
  local -a candidates=()
  local candidate=""

  if [[ -n "$requested_bin" ]]; then
    candidates+=("$requested_bin")
  fi
  candidates+=("python3.12" "python3.11" "python3.10" "python3")

  for candidate in "${candidates[@]}"; do
    if apt-cache show "$candidate" >/dev/null 2>&1 \
      && apt-cache show "${candidate}-dev" >/dev/null 2>&1 \
      && apt-cache show "${candidate}-venv" >/dev/null 2>&1; then
      PYTHON_BIN="$candidate"
      PYTHON_APT_PKGS=("$candidate" "${candidate}-dev" "${candidate}-venv")
      return 0
    fi
  done

  return 1
}

RUNTIME_USER="$(resolve_runtime_user)"
RUNTIME_HOME="$(resolve_home_dir "$RUNTIME_USER")"
UPDATE_DROP_DIR="${UPDATE_DROP_DIR:-$RUNTIME_HOME/soulframe_update}"

bootstrap_setup_location "$@"

box_block "SOULFRAME Setup" "Inizializzazione in corso" "Log dettagliato: $SETUP_LOG_FILE"
run_quiet "Aggiorno indice pacchetti apt" apt-get update

if ! resolve_python_packages "${PYTHON_BIN:-}"; then
  echo "[ERR] Nessun pacchetto Python compatibile trovato (python3.x + -dev + -venv)."
  echo "Controlla i repository apt configurati e riprova."
  exit 1
fi
box_block "[INFO] Python rilevato" "$PYTHON_BIN"

run_quiet "Installo dipendenze di sistema" \
  env DEBIAN_FRONTEND=noninteractive apt-get install -y \
  build-essential \
  ca-certificates \
  curl \
  ffmpeg \
  git \
  jq \
  nano \
  rsync \
  libsndfile1 \
  pkg-config \
  tesseract-ocr \
  tesseract-ocr-ita \
  tesseract-ocr-eng \
  "${PYTHON_APT_PKGS[@]}" \
  debian-keyring \
  debian-archive-keyring \
  apt-transport-https

if ! command -v ollama >/dev/null 2>&1; then
  run_quiet "Installo Ollama" bash -c "set -euo pipefail; tmp=\$(mktemp); curl -fsSL https://ollama.com/install.sh -o \"\$tmp\"; sh \"\$tmp\"; rm -f \"\$tmp\""
else
  box_block "[INFO] Ollama" "Gia installato, salto installazione."
fi

run_quiet "Abilito servizio Ollama" systemctl enable --now ollama

if [[ "$SKIP_OLLAMA_PULL" != "1" ]]; then
  run_quiet "Scarico modello embedding ($EMBED_MODEL_DEFAULT)" ollama pull "$EMBED_MODEL_DEFAULT"
  run_quiet "Scarico modello chat ($CHAT_MODEL_DEFAULT)" ollama pull "$CHAT_MODEL_DEFAULT"
else
  box_block "[INFO] Ollama pull" "SKIP_OLLAMA_PULL=1: download modelli saltato."
fi

if ! command -v caddy >/dev/null 2>&1; then
  run_quiet "Aggiungo chiave repository Caddy" bash -c "set -euo pipefail; tmp=\$(mktemp); curl -1sLf 'https://dl.cloudsmith.io/public/caddy/stable/gpg.key' -o \"\$tmp\"; gpg --dearmor -o /usr/share/keyrings/caddy-stable-archive-keyring.gpg \"\$tmp\"; rm -f \"\$tmp\""
  run_quiet "Aggiungo repository Caddy" bash -c "set -euo pipefail; curl -1sLf 'https://dl.cloudsmith.io/public/caddy/stable/debian.deb.txt' > /etc/apt/sources.list.d/caddy-stable.list"
  run_quiet "Aggiorno indice apt (Caddy)" apt-get update
  run_quiet "Installo Caddy" env DEBIAN_FRONTEND=noninteractive apt-get install -y caddy
else
  box_block "[INFO] Caddy" "Gia installato, salto installazione."
fi

box_block "[INFO] Setup scripts pronti" "$SETUP_TARGET_DIR"
mkdir -p "$UPDATE_DROP_DIR"
if [[ "$RUNTIME_USER" != "root" ]] && id -u "$RUNTIME_USER" >/dev/null 2>&1; then
  update_group="$(id -gn "$RUNTIME_USER" 2>/dev/null || echo "$RUNTIME_USER")"
  chown "$RUNTIME_USER:$update_group" "$UPDATE_DROP_DIR" || true
fi
chmod 775 "$UPDATE_DROP_DIR" || true

mkdir -p "$BACKEND_DIR" "$WEBGL_DIR" "$BACKUPS_DIR"
if BACKEND_SOURCE_DIR="$(resolve_backend_source_dir)"; then
  echo "[INFO] Backend sorgente rilevato: $BACKEND_SOURCE_DIR"
  if command -v rsync >/dev/null 2>&1; then
    rsync -a "$BACKEND_SOURCE_DIR/" "$BACKEND_DIR/"
  else
    cp -a "$BACKEND_SOURCE_DIR/." "$BACKEND_DIR/"
  fi
fi

if [[ ! -f "$BACKEND_DIR/avatar_asset_server.py" || ! -f "$BACKEND_DIR/coqui_tts_server.py" || ! -f "$BACKEND_DIR/rag_server.py" || ! -f "$BACKEND_DIR/whisper_server.py" ]]; then
  echo "[ERR] Backend incompleto in $BACKEND_DIR"
  echo "Devono esistere i file:"
  echo "  - avatar_asset_server.py"
  echo "  - coqui_tts_server.py"
  echo "  - rag_server.py"
  echo "  - whisper_server.py"
  echo "Suggerimento: esegui setup da una cartella con i file backend"
  echo "(root progetto oppure cartella soulframe_update)."
  exit 1
fi

mkdir -p \
  "$BACKEND_DIR/avatar_store" \
  "$BACKEND_DIR/rag_store" \
  "$BACKEND_DIR/voices" \
  "$BACKEND_DIR/voices/avatars"

if [[ ! -d "$VENV_DIR" ]]; then
  $PYTHON_BIN -m venv "$VENV_DIR"
fi

run_quiet "Aggiorno pip e wheel" \
  "$VENV_DIR/bin/python" -m pip install \
  --disable-pip-version-check --progress-bar off --upgrade pip wheel

REQ_FILE="$BACKEND_DIR/requirements.txt"
if [[ ! -f "$REQ_FILE" ]]; then
  echo "[WARN] requirements.txt non trovato, lo genero con pipreqs."
  run_quiet "Installo pipreqs" \
    "$VENV_DIR/bin/python" -m pip install \
    --disable-pip-version-check --progress-bar off pipreqs
  run_quiet "Genero requirements.txt da backend" \
    "$VENV_DIR/bin/pipreqs" "$BACKEND_DIR" --force
  REQ_FILE="$BACKEND_DIR/requirements.txt"
fi

run_quiet "Installo dipendenze Python backend" \
  "$VENV_DIR/bin/pip" install \
  --disable-pip-version-check --progress-bar off --upgrade -r "$REQ_FILE"

if ! "$VENV_DIR/bin/python" - <<'PY' >/dev/null 2>&1
from transformers.pytorch_utils import isin_mps_friendly  # noqa: F401
PY
then
  echo "[WARN] transformers incompatibile con Coqui rilevato, applico fix automatico..."
  run_quiet "Fix compatibilita transformers/tokenizers" \
    "$VENV_DIR/bin/pip" install \
    --disable-pip-version-check --progress-bar off --upgrade \
    "transformers==4.57.1" "tokenizers==0.22.1"
fi

if ! "$VENV_DIR/bin/python" - <<'PY' >/dev/null 2>&1
from TTS.api import TTS  # noqa: F401
PY
then
  echo "[WARN] Coqui richiede dipendenze codec audio: applico fix automatico..."
  run_quiet "Installo dipendenze codec Coqui (torchcodec)" \
    "$VENV_DIR/bin/pip" install \
    --disable-pip-version-check --progress-bar off --upgrade \
    "coqui-tts[codec]==0.27.5" "torchcodec>=0.8.0"
fi

if [[ -n "$TORCH_INSTALL_CMD" ]]; then
  echo "[INFO] Installazione torch/torchaudio con comando personalizzato."
  run_quiet "Eseguo TORCH_INSTALL_CMD personalizzato" bash -c "$TORCH_INSTALL_CMD"
else
  cat <<'TORCH_NOTE'
[NOTE] Torch/torchaudio installati da requirements.txt.
Se vuoi una build CUDA specifica, usa TORCH_INSTALL_CMD con il comando corretto:
https://pytorch.org/get-started/locally/
Esempio CUDA 12.8:
  pip install torch torchaudio --index-url https://download.pytorch.org/whl/cu128
TORCH_NOTE
fi

mkdir -p "$ENV_DIR"
if [[ ! -f "$ENV_FILE" ]]; then
  GEMINI_API_KEY_INPUT=""
  read -r -p "Inserisci GEMINI_API_KEY (invio per lasciare vuoto): " GEMINI_API_KEY_INPUT
  while true; do
    if [[ -z "${SOULFRAME_DOMAIN_INPUT:-}" ]]; then
      read -r -p "Inserisci dominio pubblico per SOULFRAME (es. soulframe.tuodominio.dev): " SOULFRAME_DOMAIN_INPUT
    fi
    SOULFRAME_DOMAIN_INPUT="$(normalize_domain "$SOULFRAME_DOMAIN_INPUT")"
    if [[ "$SOULFRAME_DOMAIN_INPUT" =~ ^[a-z0-9]([a-z0-9-]*[a-z0-9])?(\.[a-z0-9]([a-z0-9-]*[a-z0-9])?)+$ ]]; then
      break
    fi
    echo "[ERR] Dominio non valido: '$SOULFRAME_DOMAIN_INPUT'"
    SOULFRAME_DOMAIN_INPUT=""
  done
  TESSDATA_PREFIX=$(dpkg -L tesseract-ocr-eng 2>/dev/null | grep -m 1 '/tessdata$' || true)
  if [[ -z "$TESSDATA_PREFIX" ]]; then
    TESSDATA_PREFIX="/usr/share/tesseract-ocr/4.00/tessdata"
  fi
  cat <<EOF_ENV > "$ENV_FILE"
# SOULFRAME env
RAG_DIR=$BACKEND_DIR/rag_store
RAG_OCR_LANG=ita+eng
TESSDATA_PREFIX=$TESSDATA_PREFIX

# Ollama
OLLAMA_HOST=http://127.0.0.1:11434
EMBED_MODEL=$EMBED_MODEL_DEFAULT
CHAT_MODEL=$CHAT_MODEL_DEFAULT
CHAT_TEMPERATURE=0.45
CHAT_TOP_P=0.9
CHAT_REPEAT_PENALTY=1.08
CHAT_NUM_PREDICT=220

# Whisper
WHISPER_MODEL=$WHISPER_MODEL_DEFAULT

# Coqui TTS
COQUI_TTS_MODEL=tts_models/multilingual/multi-dataset/xtts_v2
COQUI_LANG=it
COQUI_DEFAULT_SPEAKER_WAV=$BACKEND_DIR/voices/default.wav
COQUI_AVATAR_VOICES_DIR=$BACKEND_DIR/voices/avatars
COQUI_TTS_DEVICE=cuda
# Imposta a 1 solo se accetti i termini Coqui CPML / licenza commerciale.
COQUI_TOS_AGREED=1

# Opzionali
GEMINI_API_KEY=${GEMINI_API_KEY_INPUT}
SOULFRAME_DOMAIN=${SOULFRAME_DOMAIN_INPUT}
SOULFRAME_UPDATE_DIR=${UPDATE_DROP_DIR}
EOF_ENV
  chmod 640 "$ENV_FILE"
  if [[ -n "${SUDO_USER:-}" && "${SUDO_USER:-}" != "root" ]] && command -v setfacl >/dev/null 2>&1; then
    setfacl -m "u:${SUDO_USER}:r" "$ENV_FILE" || true
  fi
else
  echo "[INFO] Env file già presente: $ENV_FILE"
fi

if ! grep -qE '^SOULFRAME_UPDATE_DIR=' "$ENV_FILE" 2>/dev/null; then
  echo "SOULFRAME_UPDATE_DIR=${UPDATE_DROP_DIR}" >> "$ENV_FILE"
fi

SOULFRAME_DOMAIN_EFFECTIVE="$(grep -E '^SOULFRAME_DOMAIN=' "$ENV_FILE" 2>/dev/null | tail -n 1 | cut -d= -f2- | tr -d '\r' | xargs || true)"
SOULFRAME_DOMAIN_EFFECTIVE="$(normalize_domain "${SOULFRAME_DOMAIN_EFFECTIVE:-}")"
if [[ -z "$SOULFRAME_DOMAIN_EFFECTIVE" ]]; then
  echo "[ERR] SOULFRAME_DOMAIN non trovato in $ENV_FILE"
  echo "Aggiungi una riga: SOULFRAME_DOMAIN=tuodominio.tld"
  exit 1
fi

if [[ ! -f "$IDLE_ENV_FILE" ]]; then
  cat <<EOF_IDLE > "$IDLE_ENV_FILE"
# Minuti di inattività prima dello shutdown
IDLE_MINUTES=30

# Minuti di attesa dopo il boot prima di iniziare a valutare l'idle
STARTUP_GRACE_MINUTES=10

# Log accessi del web server (usato per mtime)
LOG_FILE=/var/log/caddy/access.log

# Numero di righe recenti da analizzare nel log JSON di Caddy
# (vengono considerate solo richieste /api/*)
LOG_TAIL_LINES=20000

# Se 1, considera anche attività terminali SSH (pts/*) nel calcolo idle.
# Una sessione SSH aperta ma inattiva NON blocca lo shutdown oltre soglia.
TRACK_SSH_ACTIVITY=1

# Set DRY_RUN=1 per testare senza spegnere la VM
DRY_RUN=0
EOF_IDLE
  chmod 640 "$IDLE_ENV_FILE"
else
  echo "[INFO] Idle env file già presente: $IDLE_ENV_FILE"
fi

# Compatibilità: aggiunge chiavi nuove se mancanti in installazioni precedenti.
ensure_env_key "$IDLE_ENV_FILE" "STARTUP_GRACE_MINUTES" "10"
ensure_env_key "$IDLE_ENV_FILE" "LOG_TAIL_LINES" "20000"
ensure_env_key "$IDLE_ENV_FILE" "TRACK_SSH_ACTIVITY" "1"
ensure_env_key "$ENV_FILE" "COQUI_TOS_AGREED" "1"

cat <<'UNIT' > /etc/systemd/system/soulframe-whisper.service
[Unit]
Description=SOULFRAME Whisper Server
After=network.target

[Service]
Type=simple
WorkingDirectory=/opt/soulframe/backend
EnvironmentFile=/etc/soulframe/soulframe.env
ExecStart=/opt/soulframe/.venv/bin/uvicorn whisper_server:app --host 127.0.0.1 --port 8001
Restart=always
RestartSec=5
TimeoutStartSec=180

[Install]
WantedBy=soulframe.target
UNIT

cat <<'UNIT' > /etc/systemd/system/soulframe-rag.service
[Unit]
Description=SOULFRAME RAG Server
After=network.target ollama.service soulframe-ollama.service
Requires=ollama.service

[Service]
Type=simple
WorkingDirectory=/opt/soulframe/backend
EnvironmentFile=/etc/soulframe/soulframe.env
ExecStart=/opt/soulframe/.venv/bin/uvicorn rag_server:app --host 127.0.0.1 --port 8002
Restart=always
RestartSec=5
TimeoutStartSec=180

[Install]
WantedBy=soulframe.target
UNIT

cat <<'UNIT' > /etc/systemd/system/soulframe-avatar.service
[Unit]
Description=SOULFRAME Avatar Asset Server
After=network.target

[Service]
Type=simple
WorkingDirectory=/opt/soulframe/backend
EnvironmentFile=/etc/soulframe/soulframe.env
ExecStart=/opt/soulframe/.venv/bin/uvicorn avatar_asset_server:app --host 127.0.0.1 --port 8003
Restart=always
RestartSec=5
TimeoutStartSec=120

[Install]
WantedBy=soulframe.target
UNIT

cat <<'UNIT' > /etc/systemd/system/soulframe-tts.service
[Unit]
Description=SOULFRAME Coqui TTS Server
After=network.target

[Service]
Type=simple
WorkingDirectory=/opt/soulframe/backend
EnvironmentFile=/etc/soulframe/soulframe.env
ExecStart=/opt/soulframe/.venv/bin/uvicorn coqui_tts_server:app --host 127.0.0.1 --port 8004
Restart=always
RestartSec=5
TimeoutStartSec=240

[Install]
WantedBy=soulframe.target
UNIT

cat <<'UNIT' > /etc/systemd/system/soulframe-ollama.service
[Unit]
Description=SOULFRAME Ollama Wrapper
After=network.target

[Service]
Type=oneshot
ExecStart=/usr/bin/systemctl start ollama
ExecStop=/usr/bin/systemctl stop ollama
RemainAfterExit=yes

[Install]
WantedBy=soulframe.target
UNIT

cat <<'UNIT' > /etc/systemd/system/soulframe.target
[Unit]
Description=SOULFRAME Services Target
Wants=soulframe-ollama.service soulframe-whisper.service soulframe-rag.service soulframe-avatar.service soulframe-tts.service
After=network.target

[Install]
WantedBy=multi-user.target
UNIT

mkdir -p "$WEBGL_DIR" "$BACKUPS_DIR"
CADDY_SITE_LABEL="$SOULFRAME_DOMAIN_EFFECTIVE"
cat <<CADDY > /etc/caddy/Caddyfile
${CADDY_SITE_LABEL} {
  encode zstd gzip
  root * $WEBGL_DIR
  file_server
  log {
    output file /var/log/caddy/access.log
  }

  handle_path /api/whisper/* {
    reverse_proxy 127.0.0.1:8001
  }

  handle_path /api/rag/* {
    reverse_proxy 127.0.0.1:8002
  }

  handle_path /api/avatar/* {
    reverse_proxy 127.0.0.1:8003 {
      header_up X-Forwarded-Prefix /api/avatar
    }
  }

  # Compatibilità con URL legacy assolute restituite in precedenza dal backend avatar.
  handle /avatars/* {
    reverse_proxy 127.0.0.1:8003
  }

  handle_path /api/tts/* {
    reverse_proxy 127.0.0.1:8004
  }
}
CADDY

if id -u caddy >/dev/null 2>&1; then
  install -d -m 755 -o caddy -g caddy /var/log/caddy
  touch /var/log/caddy/access.log
  chown caddy:caddy /var/log/caddy/access.log
else
  echo "[WARN] Utente caddy non trovato: salto setup permessi log."
fi

cat <<'IDLE' > /usr/local/bin/idle_shutdown.sh
#!/usr/bin/env bash
set -euo pipefail

ENV_FILE="/etc/soulframe/idle.env"

if [[ ! -f "$ENV_FILE" ]]; then
  echo "[WARN] Env file non trovato: $ENV_FILE"
  exit 0
fi

set -a
# shellcheck source=/etc/soulframe/idle.env
source "$ENV_FILE"
set +a

IDLE_MINUTES=${IDLE_MINUTES:-30}
STARTUP_GRACE_MINUTES=${STARTUP_GRACE_MINUTES:-10}
LOG_FILE=${LOG_FILE:-/var/log/caddy/access.log}
LOG_TAIL_LINES=${LOG_TAIL_LINES:-20000}
TRACK_SSH_ACTIVITY=${TRACK_SSH_ACTIVITY:-1}
DRY_RUN=${DRY_RUN:-0}

uptime_seconds=$(cut -d. -f1 /proc/uptime 2>/dev/null || echo 0)
startup_grace_seconds=$((STARTUP_GRACE_MINUTES * 60))
if (( uptime_seconds < startup_grace_seconds )); then
  exit 0
fi

idle_token_to_seconds() {
  local token="${1:-}"
  token="${token// /}"
  case "$token" in
    ""|"?") echo 864000 ;;
    ".") echo 0 ;;
    "old") echo 864000 ;;
    *)
      if [[ "$token" =~ ^([0-9]+):([0-9]+)$ ]]; then
        echo $((10#${BASH_REMATCH[1]} * 3600 + 10#${BASH_REMATCH[2]} * 60))
      elif [[ "$token" =~ ^([0-9]+)m$ ]]; then
        echo $((10#${BASH_REMATCH[1]} * 60))
      elif [[ "$token" =~ ^([0-9]+)s$ ]]; then
        echo $((10#${BASH_REMATCH[1]}))
      elif [[ "$token" =~ ^([0-9]+)h$ ]]; then
        echo $((10#${BASH_REMATCH[1]} * 3600))
      elif [[ "$token" =~ ^([0-9]+)d$ ]]; then
        echo $((10#${BASH_REMATCH[1]} * 86400))
      else
        echo 864000
      fi
      ;;
  esac
}

now=$(date +%s)
web_last_activity=0
ssh_last_activity=0

get_web_last_activity() {
  if [[ ! -f "$LOG_FILE" ]]; then
    echo 0
    return 0
  fi

  if ! command -v jq >/dev/null 2>&1; then
    # Fallback: senza jq usiamo mtime file (comportamento legacy).
    stat -c %Y "$LOG_FILE" 2>/dev/null || echo 0
    return 0
  fi

  local ts=""
  ts="$(
    tail -n "$LOG_TAIL_LINES" "$LOG_FILE" 2>/dev/null | jq -r '
      select((.ts? != null) and ((.request.uri // "") | startswith("/api/"))) | .ts
    ' 2>/dev/null | tail -n 1
  )"

  if [[ -z "$ts" ]]; then
    echo 0
  else
    awk -v x="$ts" 'BEGIN { print int(x) }'
  fi
}

get_ssh_last_activity() {
  if [[ "$TRACK_SSH_ACTIVITY" != "1" ]]; then
    echo 0
    return 0
  fi

  local min_idle=""
  local idle_token=""
  local idle_seconds_token=0

  while IFS= read -r idle_token; do
    [[ -n "$idle_token" ]] || continue
    idle_seconds_token="$(idle_token_to_seconds "$idle_token")"
    if [[ -z "$min_idle" || "$idle_seconds_token" -lt "$min_idle" ]]; then
      min_idle="$idle_seconds_token"
    fi
  done < <(who -u 2>/dev/null | awk '$2 ~ /^pts\// { print $(NF-2) }')

  if [[ -z "$min_idle" ]]; then
    echo 0
  else
    echo $((now - min_idle))
  fi
}

web_last_activity="$(get_web_last_activity)"
ssh_last_activity="$(get_ssh_last_activity)"

last_activity="$web_last_activity"
if (( ssh_last_activity > last_activity )); then
  last_activity="$ssh_last_activity"
fi

if (( last_activity <= 0 )); then
  exit 0
fi

idle_seconds=$((now - last_activity))
limit_seconds=$((IDLE_MINUTES * 60))

if (( idle_seconds > limit_seconds )); then
  if [[ "$DRY_RUN" == "1" ]]; then
    echo "[DRY_RUN] Shutdown triggered (idle ${idle_seconds}s > ${limit_seconds}s, api_last=${web_last_activity}, ssh_last=${ssh_last_activity})"
    exit 0
  fi
  shutdown -h now
fi
IDLE
chmod +x /usr/local/bin/idle_shutdown.sh

cat <<'UNIT' > /etc/systemd/system/soulframe-idle-shutdown.service
[Unit]
Description=SOULFRAME Idle Shutdown Check

[Service]
Type=oneshot
ExecStart=/usr/local/bin/idle_shutdown.sh
UNIT

cat <<'UNIT' > /etc/systemd/system/soulframe-idle-shutdown.timer
[Unit]
Description=Run SOULFRAME idle shutdown check every 60 seconds

[Timer]
OnBootSec=60
OnUnitActiveSec=60
Unit=soulframe-idle-shutdown.service

[Install]
WantedBy=timers.target
UNIT

cat <<'SFCTL' > /usr/local/bin/sfctl
#!/usr/bin/env bash
set -euo pipefail

ACTION=${1:-}
SERVICE=${2:-}
ALL_UNITS=(
  soulframe-whisper.service
  soulframe-rag.service
  soulframe-avatar.service
  soulframe-tts.service
  soulframe-ollama.service
)

usage() {
  echo "Uso: sfctl {start|stop|restart|status|logs} [servizio]"
  echo "Servizi: whisper | rag | avatar | tts | ollama | all"
}

service_name() {
  case "$1" in
    whisper) echo "soulframe-whisper.service" ;;
    rag) echo "soulframe-rag.service" ;;
    avatar) echo "soulframe-avatar.service" ;;
    tts) echo "soulframe-tts.service" ;;
    ollama) echo "soulframe-ollama.service" ;;
    all|"") echo "__all__" ;;
    *) echo "" ;;
  esac
}

UNIT=$(service_name "$SERVICE")
if [[ -z "$UNIT" ]]; then
  usage
  exit 1
fi

case "$ACTION" in
  start|stop|restart|status)
    if [[ "$UNIT" == "__all__" ]]; then
      case "$ACTION" in
        start)
          systemctl start soulframe.target
          ;;
        stop)
          # Stopping the target alone may not stop all wanted units.
          systemctl stop "${ALL_UNITS[@]}"
          ;;
        restart)
          systemctl restart "${ALL_UNITS[@]}"
          ;;
        status)
          systemctl status "${ALL_UNITS[@]}" --no-pager
          ;;
      esac
    else
      systemctl "$ACTION" "$UNIT"
    fi
    ;;
  logs)
    if [[ "$SERVICE" == "all" || -z "$SERVICE" ]]; then
      journalctl -u soulframe-whisper.service -u soulframe-rag.service -u soulframe-avatar.service -u soulframe-tts.service -u ollama.service -f
    else
      journalctl -u "$UNIT" -f
    fi
    ;;
  *)
    usage
    exit 1
    ;;
esac
SFCTL
chmod +x /usr/local/bin/sfctl

cat <<'SFURL' > /usr/local/bin/sfurl
#!/usr/bin/env bash
set -euo pipefail

OPEN=0
if [[ "${1:-}" == "--open" ]]; then
  OPEN=1
fi

ENV_FILE="/etc/soulframe/soulframe.env"
if [[ ! -r "$ENV_FILE" ]]; then
  echo "[ERR] Permesso negato su $ENV_FILE"
  echo "Esegui: sudo sfurl"
  echo "Oppure abilita read al tuo utente:"
  echo "  sudo setfacl -m u:$USER:r $ENV_FILE"
  exit 1
fi

domain="$(grep -E '^SOULFRAME_DOMAIN=' "$ENV_FILE" 2>/dev/null | tail -n 1 | cut -d= -f2- | tr -d '\r' | xargs || true)"
domain="${domain#http://}"
domain="${domain#https://}"
domain="${domain%%/*}"

if [[ -z "$domain" ]]; then
  echo "[ERR] Dominio non trovato in /etc/soulframe/soulframe.env"
  exit 1
fi

base_url="https://$domain"
echo "SOULFRAME URL:"
echo "  $base_url"
echo
echo "Comando rapido PowerShell:"
echo "  Start-Process $base_url"
echo
echo "Health:"
echo "  $base_url/api/whisper/health"
echo "  $base_url/api/rag/health"
echo "  $base_url/api/avatar/health"
echo "  $base_url/api/tts/health"

if [[ "$OPEN" == "1" ]]; then
  if command -v xdg-open >/dev/null 2>&1; then
    xdg-open "$base_url" >/dev/null 2>&1 || true
  else
    echo "[INFO] xdg-open non disponibile su questa VM (probabilmente headless)."
  fi
fi
SFURL
chmod +x /usr/local/bin/sfurl

if [[ -f "$INSTALL_DIR/SOULFRAME_SETUP/sf_admin_ubuntu.sh" ]]; then
  install -m 755 "$INSTALL_DIR/SOULFRAME_SETUP/sf_admin_ubuntu.sh" /usr/local/bin/sfadmin
  sed -i 's/\r$//' /usr/local/bin/sfadmin
else
  echo "[WARN] sf_admin_ubuntu.sh non trovato in $INSTALL_DIR/SOULFRAME_SETUP"
fi

run_quiet "Ricarico configurazione systemd" systemctl daemon-reload
run_quiet "Abilito target SOULFRAME" systemctl enable soulframe.target
if ! caddy validate --config /etc/caddy/Caddyfile; then
  echo "[ERR] Caddyfile non valido: /etc/caddy/Caddyfile"
  exit 1
fi
run_quiet "Abilito e avvio Caddy" systemctl enable --now caddy
run_quiet "Abilito timer idle shutdown" systemctl enable --now soulframe-idle-shutdown.timer

cat <<POST

Setup completato.

Comandi utili (esegui dalla VM):
  source /etc/soulframe/soulframe.env
  cd /opt/soulframe/backend

Avvio rapido:
  sfctl start
  sfctl status
  sfctl logs rag
  sfadmin
  sfurl
  sfurl --open

Ollama:
  systemctl status ollama

Caddy:
  # HTTPS automatico su: ${SOULFRAME_DOMAIN_EFFECTIVE}
  systemctl reload caddy
  systemctl status caddy

Auto-shutdown:
  systemctl status soulframe-idle-shutdown.timer
  journalctl -u soulframe-idle-shutdown.service -f
  # dry-run:
  sed -i 's/^DRY_RUN=.*/DRY_RUN=1/' /etc/soulframe/idle.env
  /usr/local/bin/idle_shutdown.sh
  # disabilita temporaneamente:
  systemctl stop soulframe-idle-shutdown.timer

URL:
  SOULFRAME: https://${SOULFRAME_DOMAIN_EFFECTIVE}
  Whisper:   https://${SOULFRAME_DOMAIN_EFFECTIVE}/api/whisper/health
  RAG:       https://${SOULFRAME_DOMAIN_EFFECTIVE}/api/rag/health
  Avatar:    https://${SOULFRAME_DOMAIN_EFFECTIVE}/api/avatar/health
  TTS:       https://${SOULFRAME_DOMAIN_EFFECTIVE}/api/tts/health
  Ollama:    http://127.0.0.1:11434

Cartella update automatica:
  ${UPDATE_DROP_DIR}
POST


