#!/usr/bin/env bash
set -euo pipefail

INSTALL_DIR="${INSTALL_DIR:-/opt/soulframe}"
BUILD_DIR="${BUILD_DIR:-/opt/soulframe/webgl}"
BACKEND_DIR="${BACKEND_DIR:-$INSTALL_DIR/backend}"
if [[ -d "$INSTALL_DIR/SOULFRAME_SETUP" ]]; then
  DEFAULT_SETUP_DIR="$INSTALL_DIR/SOULFRAME_SETUP"
else
  DEFAULT_SETUP_DIR="$INSTALL_DIR"
fi
SETUP_DIR="${SETUP_DIR:-$DEFAULT_SETUP_DIR}"
ENV_FILE="${ENV_FILE:-/etc/soulframe/soulframe.env}"
IDLE_ENV_FILE="${IDLE_ENV_FILE:-/etc/soulframe/idle.env}"
BACKUPS_DIR="${BACKUPS_DIR:-$INSTALL_DIR/backups}"
RUNTIME_USER=""
RUNTIME_HOME=""
UPDATE_DIR=""

SUPPORTED_UPDATE_FILES=(
  avatar_asset_server.py
  coqui_tts_server.py
  rag_server.py
  whisper_server.py
  requirements.txt
  setup_soulframe_ubuntu.sh
  sf_admin_ubuntu.sh
)

if [[ $EUID -ne 0 ]]; then
  echo "[ERR] Esegui con sudo/root."
  exit 1
fi

if ! command -v sfctl >/dev/null 2>&1; then
  echo "[ERR] sfctl non trovato. Assicurati di aver eseguito setup_soulframe_ubuntu.sh."
  exit 1
fi

resolve_runtime_user() {
  local user_candidate=""

  if [[ -n "${SUDO_USER:-}" && "${SUDO_USER:-}" != "root" ]]; then
    echo "$SUDO_USER"
    return 0
  fi

  user_candidate=$(stat -c %U "$INSTALL_DIR" 2>/dev/null || true)
  if [[ -n "$user_candidate" && "$user_candidate" != "root" ]]; then
    echo "$user_candidate"
    return 0
  fi

  user_candidate=$(logname 2>/dev/null || true)
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

RUNTIME_USER="$(resolve_runtime_user)"
RUNTIME_HOME="$(resolve_home_dir "$RUNTIME_USER")"
UPDATE_DIR="${UPDATE_DIR:-$RUNTIME_HOME/soulframe_update}"
if [[ -f "$ENV_FILE" ]]; then
  env_update_dir="$(grep -E '^SOULFRAME_UPDATE_DIR=' "$ENV_FILE" 2>/dev/null | tail -n 1 | cut -d= -f2- | tr -d '\r' | xargs || true)"
  if [[ -n "$env_update_dir" ]]; then
    UPDATE_DIR="$env_update_dir"
  fi
fi
mkdir -p "$UPDATE_DIR"
if [[ "$RUNTIME_USER" != "root" ]] && id -u "$RUNTIME_USER" >/dev/null 2>&1; then
  update_group="$(id -gn "$RUNTIME_USER" 2>/dev/null || echo "$RUNTIME_USER")"
  chown "$RUNTIME_USER:$update_group" "$UPDATE_DIR" || true
fi

ALL_UNITS=(
  soulframe-whisper.service
  soulframe-rag.service
  soulframe-avatar.service
  soulframe-tts.service
  soulframe-ollama.service
)

sf_all() {
  local action="$1"
  case "$action" in
    start)
      systemctl start soulframe.target
      systemctl start caddy
      ;;
    stop)
      systemctl stop "${ALL_UNITS[@]}"
      systemctl stop caddy
      ;;
    restart)
      systemctl restart "${ALL_UNITS[@]}"
      systemctl restart caddy
      ;;
    status)
      systemctl status "${ALL_UNITS[@]}" caddy --no-pager
      ;;
    *)
      echo "[ERR] Azione non supportata: $action"
      return 1
      ;;
  esac
}

shutdown_vm() {
  echo "[WARN] Questa azione spegnerà completamente la VM."
  read -r -p "Confermi shutdown VM ora? [s/N]: " yn
  if is_yes "$yn"; then
    echo "[INFO] Arresto servizi e shutdown..."
    sf_all stop || true
    shutdown -h now
  else
    echo "[INFO] Shutdown annullato."
  fi
}

normalize_lf_file() {
  local target="$1"
  sed -i 's/\r$//' "$target"
}

is_yes() {
  [[ "${1,,}" == "s" ]]
}

is_no() {
  [[ "${1,,}" == "n" ]]
}

is_managed_backup_path() {
  local path="$1"
  case "$path" in
    "$INSTALL_DIR"/webgl_backup_*|"$BACKUPS_DIR"/backend_update_*)
      return 0
      ;;
    *)
      return 1
      ;;
  esac
}

load_backup_paths() {
  BACKUP_PATHS=()
  local -a candidates=()
  local candidate ts line path

  shopt -s nullglob
  candidates+=("$INSTALL_DIR"/webgl_backup_*)
  candidates+=("$BACKUPS_DIR"/backend_update_*)
  shopt -u nullglob

  while IFS= read -r line; do
    path="${line#*$'\t'}"
    [[ -n "$path" ]] || continue
    BACKUP_PATHS+=("$path")
  done < <(
    for candidate in "${candidates[@]}"; do
      [[ -d "$candidate" ]] || continue
      ts=$(stat -c %Y "$candidate" 2>/dev/null || echo 0)
      printf '%s\t%s\n' "$ts" "$candidate"
    done | sort -rn
  )
}

delete_backup_path() {
  local target="$1"
  if ! is_managed_backup_path "$target"; then
    echo "[ERR] Path backup non valido: $target"
    return 1
  fi
  if [[ ! -d "$target" ]]; then
    echo "[WARN] Backup non trovato: $target"
    return 0
  fi
  rm -rf "$target"
  echo "[OK] Eliminato backup: $target"
}

manage_backups() {
  while true; do
    load_backup_paths
    local count="${#BACKUP_PATHS[@]}"

    echo
    echo "--------- Gestione Backup ---------"
    echo "Directory backup: $BACKUPS_DIR"
    echo "Totale backup trovati: $count"

    if [[ "$count" -gt 0 ]]; then
      du -sh "${BACKUP_PATHS[@]}" 2>/dev/null || true
    else
      echo "[INFO] Nessun backup presente."
    fi

    echo
    echo "[1] Elimina un backup specifico"
    echo "[2] Elimina tutti i backup"
    echo "[3] Mantieni solo gli ultimi N backup"
    echo "[0] Indietro"
    read -r -p "> " choice

    case "$choice" in
      1)
        if [[ "$count" -eq 0 ]]; then
          echo "[INFO] Nessun backup da eliminare."
          continue
        fi
        echo
        for i in "${!BACKUP_PATHS[@]}"; do
          printf '[%d] %s\n' "$((i + 1))" "${BACKUP_PATHS[$i]}"
        done
        read -r -p "Numero backup da eliminare: " idx
        if ! [[ "$idx" =~ ^[0-9]+$ ]] || (( idx < 1 || idx > count )); then
          echo "[INFO] Indice non valido."
          continue
        fi
        local target="${BACKUP_PATHS[$((idx - 1))]}"
        read -r -p "Confermi eliminazione? [s/N]: " yn
        if is_yes "$yn"; then
          delete_backup_path "$target"
        else
          echo "[INFO] Operazione annullata."
        fi
        ;;
      2)
        if [[ "$count" -eq 0 ]]; then
          echo "[INFO] Nessun backup da eliminare."
          continue
        fi
        read -r -p "Confermi eliminazione di TUTTI i backup? [s/N]: " yn
        if ! is_yes "$yn"; then
          echo "[INFO] Operazione annullata."
          continue
        fi
        for target in "${BACKUP_PATHS[@]}"; do
          delete_backup_path "$target"
        done
        ;;
      3)
        if [[ "$count" -eq 0 ]]; then
          echo "[INFO] Nessun backup presente."
          continue
        fi
        read -r -p "Quanti backup vuoi mantenere? " keep_n
        if ! [[ "$keep_n" =~ ^[0-9]+$ ]]; then
          echo "[INFO] Numero non valido."
          continue
        fi
        if (( keep_n >= count )); then
          echo "[INFO] Nessuna eliminazione necessaria (backup totali: $count)."
          continue
        fi
        local to_delete=$((count - keep_n))
        echo "[WARN] Verranno eliminati $to_delete backup."
        read -r -p "Confermi? [s/N]: " yn
        if ! is_yes "$yn"; then
          echo "[INFO] Operazione annullata."
          continue
        fi
        local i
        for ((i = keep_n; i < count; i++)); do
          delete_backup_path "${BACKUP_PATHS[$i]}"
        done
        ;;
      0)
        return 0
        ;;
      *)
        echo "[INFO] Scelta non valida."
        ;;
    esac
  done
}

resolve_update_target() {
  local file_name="$1"
  UPDATE_TARGET_MODE=644
  UPDATE_TARGET_FILE=""
  UPDATE_TARGET_IS_SETUP=0
  UPDATE_TARGET_IS_ADMIN=0

  case "$file_name" in
    avatar_asset_server.py) UPDATE_TARGET_FILE="$BACKEND_DIR/avatar_asset_server.py" ;;
    coqui_tts_server.py) UPDATE_TARGET_FILE="$BACKEND_DIR/coqui_tts_server.py" ;;
    rag_server.py) UPDATE_TARGET_FILE="$BACKEND_DIR/rag_server.py" ;;
    whisper_server.py) UPDATE_TARGET_FILE="$BACKEND_DIR/whisper_server.py" ;;
    requirements.txt) UPDATE_TARGET_FILE="$BACKEND_DIR/requirements.txt" ;;
    setup_soulframe_ubuntu.sh)
      UPDATE_TARGET_FILE="$SETUP_DIR/setup_soulframe_ubuntu.sh"
      UPDATE_TARGET_MODE=755
      UPDATE_TARGET_IS_SETUP=1
      ;;
    sf_admin_ubuntu.sh)
      UPDATE_TARGET_FILE="$SETUP_DIR/sf_admin_ubuntu.sh"
      UPDATE_TARGET_MODE=755
      UPDATE_TARGET_IS_ADMIN=1
      ;;
    *)
      return 1
      ;;
  esac
  return 0
}

find_latest_file_in_update_dir() {
  local base_dir="$1"
  local file_name="$2"
  if [[ ! -d "$base_dir" ]]; then
    return 0
  fi

  find "$base_dir" -type f -name "$file_name" -printf '%T@|%p\n' 2>/dev/null \
    | sort -nr \
    | head -n 1 \
    | cut -d'|' -f2-
}

discover_update_files() {
  local base_dir="$1"
  local file_name found

  for file_name in "${SUPPORTED_UPDATE_FILES[@]}"; do
    found="$(find_latest_file_in_update_dir "$base_dir" "$file_name")"
    if [[ -n "$found" ]]; then
      echo "$found"
    fi
  done
}

find_latest_build_zip() {
  local base_dir="$1"
  local preferred_zip=""
  local any_zip=""

  if [[ ! -d "$base_dir" ]]; then
    return 0
  fi

  # Preferisci esplicitamente Build.zip nella update dir.
  preferred_zip="$(
    find "$base_dir" -type f \( -iname 'Build.zip' \) -printf '%T@|%p\n' 2>/dev/null \
      | sort -nr \
      | head -n 1 \
      | cut -d'|' -f2-
  )"
  if [[ -n "$preferred_zip" ]]; then
    echo "$preferred_zip"
    return 0
  fi

  any_zip="$(
    find "$base_dir" -type f \( -iname '*.zip' \) -printf '%T@|%p\n' 2>/dev/null \
    | sort -nr \
    | head -n 1 \
    | cut -d'|' -f2-
  )"
  echo "$any_zip"
}

apply_update_file() {
  local src_file="$1"
  local backup_root="$2"
  local -n _updated_targets="$3"
  local -n _updated_any="$4"
  local -n _updated_setup_script="$5"
  local -n _updated_admin_script="$6"

  if [[ ! -f "$src_file" ]]; then
    echo "[ERR] File non trovato: $src_file"
    return 1
  fi

  local file_name target_file mode rel backup_file
  file_name="$(basename "$src_file")"
  if ! resolve_update_target "$file_name"; then
    echo "[ERR] Nome file non supportato: $file_name"
    return 1
  fi

  target_file="$UPDATE_TARGET_FILE"
  mode="$UPDATE_TARGET_MODE"

  rel="$target_file"
  if [[ "$target_file" == "$INSTALL_DIR/"* ]]; then
    rel="${target_file#"$INSTALL_DIR"/}"
  else
    rel="$(basename "$target_file")"
  fi

  backup_file="$backup_root/$rel"
  mkdir -p "$(dirname "$backup_file")"
  if [[ -f "$target_file" ]]; then
    cp -a "$target_file" "$backup_file"
  fi

  mkdir -p "$(dirname "$target_file")"
  install -m "$mode" "$src_file" "$target_file"
  case "$target_file" in
    *.sh|*.py) normalize_lf_file "$target_file" ;;
  esac

  _updated_targets+=("$target_file")
  _updated_any=1
  if [[ "$UPDATE_TARGET_IS_SETUP" -eq 1 ]]; then
    _updated_setup_script=1
  fi
  if [[ "$UPDATE_TARGET_IS_ADMIN" -eq 1 ]]; then
    _updated_admin_script=1
  fi

  echo "[OK] Aggiornato: $file_name -> $target_file"
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

is_path_inside_dir() {
  local target="$1"
  local base="$2"
  local abs_target abs_base

  abs_target="$(canonical_path "$target")"
  abs_base="$(canonical_path "$base")"

  case "$abs_target" in
    "$abs_base"/*|"$abs_base")
      return 0
      ;;
    *)
      return 1
      ;;
  esac
}

cleanup_update_sources_after_confirm() {
  local -a raw_files=("$@")
  local -a files=()
  local file

  for file in "${raw_files[@]}"; do
    [[ -n "$file" ]] || continue
    if [[ -f "$file" ]] && is_path_inside_dir "$file" "$UPDATE_DIR"; then
      files+=("$file")
    fi
  done

  if [[ "${#files[@]}" -eq 0 ]]; then
    return 0
  fi

  # Dedupe mantenendo ordine.
  local -A seen=()
  local -a unique=()
  for file in "${files[@]}"; do
    if [[ -z "${seen[$file]:-}" ]]; then
      unique+=("$file")
      seen[$file]=1
    fi
  done

  echo
  echo "[INFO] File sorgente usati da update (dentro $UPDATE_DIR):"
  printf '  - %s\n' "${unique[@]}"
  read -r -p "Eliminare questi file sorgente ora? [s/N]: " yn
  if ! is_yes "$yn"; then
    echo "[INFO] Pulizia sorgenti annullata."
    return 0
  fi

  for file in "${unique[@]}"; do
    rm -f "$file"
    echo "[OK] Rimosso: $file"
  done
}

extract_zip_payload() {
  local zip_path="$1"
  local payload_dir="$2"
  python3 - "$zip_path" "$payload_dir" <<'PY'
import sys
import zipfile

zip_path = sys.argv[1]
dst = sys.argv[2]
with zipfile.ZipFile(zip_path, "r") as zf:
    zf.extractall(dst)
PY
}

detect_build_source() {
  local base="$1"
  local index_file candidate_dir

  while IFS= read -r -d '' index_file; do
    candidate_dir="$(dirname "$index_file")"

    # Support both canonical Unity output ("Build/") and lowercase variant.
    if [[ -d "$candidate_dir/Build" || -d "$candidate_dir/build" ]]; then
      echo "$candidate_dir"
      return 0
    fi
  done < <(find "$base" -type f -iname 'index.html' -print0 2>/dev/null)

  return 1
}

update_build() {
  local skip_stop="${1:-0}"
  local ask_restart="${2:-1}"
  local auto_zip=""
  auto_zip="$(find_latest_build_zip "$UPDATE_DIR")"
  if [[ -n "$auto_zip" ]]; then
    echo "[INFO] ZIP candidato in $UPDATE_DIR:"
    echo "  $auto_zip"
  fi

  read -r -p "Percorso ZIP nuova build (invio = candidato): " zip_path
  if [[ -z "${zip_path:-}" ]]; then
    zip_path="$auto_zip"
    if [[ -n "$zip_path" ]]; then
      echo "[INFO] ZIP rilevato automaticamente in $UPDATE_DIR:"
      echo "  $zip_path"
    else
      echo "[INFO] Nessun ZIP indicato e nessun ZIP trovato in $UPDATE_DIR."
      return
    fi
  fi
  if [[ ! -f "$zip_path" ]]; then
    echo "[ERR] ZIP non trovato: $zip_path"
    return
  fi

  if [[ "$skip_stop" != "1" ]]; then
    echo "[INFO] Stop servizi..."
    sf_all stop
  fi

  tmp_dir=$(mktemp -d /tmp/sf-build-update.XXXXXX)
  payload_dir="$tmp_dir/payload"
  mkdir -p "$payload_dir"

  extract_zip_payload "$zip_path" "$payload_dir"

  if ! source_dir="$(detect_build_source "$payload_dir")"; then
    rm -rf "$tmp_dir"
    echo "[ERR] ZIP non valido: manca una build Unity (index.html + cartella Build/)."
    return
  fi

  mkdir -p "$BUILD_DIR"
  backup_dir="$(dirname "$BUILD_DIR")/webgl_backup_$(date +%Y%m%d_%H%M%S)"
  if [[ -n "$(ls -A "$BUILD_DIR" 2>/dev/null || true)" ]]; then
    mkdir -p "$backup_dir"
    cp -a "$BUILD_DIR"/. "$backup_dir"/
    echo "[INFO] Backup creato: $backup_dir"
  else
    echo "[INFO] Backup non necessario (build vuota)."
  fi

  if command -v rsync >/dev/null 2>&1; then
    rsync -a --delete "$source_dir"/ "$BUILD_DIR"/
  else
    find "$BUILD_DIR" -mindepth 1 -maxdepth 1 -exec rm -rf {} +
    cp -a "$source_dir"/. "$BUILD_DIR"/
  fi

  rm -rf "$tmp_dir"
  echo "[OK] Build aggiornata in: $BUILD_DIR"
  cleanup_update_sources_after_confirm "$zip_path"

  if [[ "$ask_restart" == "1" ]]; then
    read -r -p "Riavviare i servizi ora? [s/N]: " yn
    if is_yes "$yn"; then
      sf_all start
    fi
  fi
}

update_backend() {
  local skip_stop="${1:-0}"
  local ask_restart="${2:-1}"
  local allow_manual_fallback="${3:-1}"

  if [[ "$skip_stop" != "1" ]]; then
    echo "[INFO] Stop servizi..."
    sf_all stop
  fi

  backup_root="$INSTALL_DIR/backups/backend_update_$(date +%Y%m%d_%H%M%S)"
  mkdir -p "$backup_root"

  local -a updated_targets=()
  local updated_any=0
  local updated_setup_script=0
  local updated_admin_script=0
  local -a auto_files=()
  local -a applied_source_files=()
  local src_file apply_auto auto_choice

  if [[ -d "$UPDATE_DIR" ]]; then
    while IFS= read -r src_file; do
      [[ -n "$src_file" ]] || continue
      auto_files+=("$src_file")
    done < <(discover_update_files "$UPDATE_DIR")
  fi

  apply_auto=0
  if [[ "${#auto_files[@]}" -gt 0 ]]; then
    echo "[INFO] File aggiornati rilevati automaticamente in: $UPDATE_DIR"
    printf '  - %s\n' "${auto_files[@]}"
    read -r -p "Applico questi file automaticamente? [S/n]: " auto_choice
    if [[ "${auto_choice,,}" != "n" ]]; then
      apply_auto=1
    fi
  else
    echo "[INFO] Nessun file supportato trovato in $UPDATE_DIR."
  fi

  if [[ "$apply_auto" -eq 1 ]]; then
    for src_file in "${auto_files[@]}"; do
      if apply_update_file "$src_file" "$backup_root" \
        updated_targets updated_any updated_setup_script updated_admin_script; then
        applied_source_files+=("$src_file")
      fi
    done
  fi

  if [[ "$updated_any" -eq 0 && "$allow_manual_fallback" == "1" ]]; then
    echo "Inserisci uno alla volta i file da aggiornare (fallback manuale)."
    echo "File supportati:"
    printf '  - %s\n' "${SUPPORTED_UPDATE_FILES[@]}"
    echo "Scrivi 'ok' per concludere."

    while true; do
      read -r -p "Percorso file aggiornato (oppure 'ok'): " src_file
      if [[ "${src_file,,}" == "ok" ]]; then
        break
      fi
      if [[ -z "${src_file:-}" ]]; then
        continue
      fi
      if apply_update_file "$src_file" "$backup_root" \
        updated_targets updated_any updated_setup_script updated_admin_script; then
        applied_source_files+=("$src_file")
      fi
    done
  fi

  if [[ "$updated_any" -eq 0 ]]; then
    echo "[INFO] Nessun file aggiornato."
  else
    if [[ "$updated_setup_script" -eq 1 || "$updated_admin_script" -eq 1 ]]; then
      install -m 755 "$SETUP_DIR/sf_admin_ubuntu.sh" /usr/local/bin/sfadmin
      normalize_lf_file /usr/local/bin/sfadmin
      echo "[INFO] sfadmin installato in /usr/local/bin/sfadmin"

      echo "[INFO] Applico aggiornamento setup (SKIP_OLLAMA_PULL=1)..."
      (
        cd "$SETUP_DIR"
        SKIP_OLLAMA_PULL=1 ./setup_soulframe_ubuntu.sh
      )
    fi

    echo "[OK] Backend aggiornato. Backup: $backup_root"
    echo "[INFO] File aggiornati:"
    printf '  - %s\n' "${updated_targets[@]}"
    cleanup_update_sources_after_confirm "${applied_source_files[@]}"
  fi

  if [[ "$ask_restart" == "1" ]]; then
    read -r -p "Riavviare i servizi ora? [s/N]: " yn
    if is_yes "$yn"; then
      systemctl daemon-reload
      sf_all start
    fi
  fi
}

update_all() {
  local auto_zip=""
  local -a auto_files=()
  local src_file
  local has_any=0

  auto_zip="$(find_latest_build_zip "$UPDATE_DIR")"
  while IFS= read -r src_file; do
    [[ -n "$src_file" ]] || continue
    auto_files+=("$src_file")
  done < <(discover_update_files "$UPDATE_DIR")

  echo "[INFO] Sorgenti update rilevate in: $UPDATE_DIR"
  if [[ -n "$auto_zip" ]]; then
    echo "  Build ZIP: $auto_zip"
    has_any=1
  else
    echo "  Build ZIP: non trovato (Build.zip o *.zip)"
  fi

  if [[ "${#auto_files[@]}" -gt 0 ]]; then
    echo "  File backend/setup:"
    printf '    - %s\n' "${auto_files[@]}"
    has_any=1
  else
    echo "  File backend/setup: non trovati"
  fi

  if [[ "$has_any" -eq 0 ]]; then
    echo "[INFO] Nessun artefatto update trovato. Copia file in $UPDATE_DIR e riprova."
    return
  fi

  read -r -p "Procedere con update completo ora? [S/n]: " yn
  if is_no "$yn"; then
    echo "[INFO] Update annullato."
    return
  fi

  echo "[INFO] Stop servizi..."
  sf_all stop

  if [[ -n "$auto_zip" ]]; then
    # Build: invio = usa candidato automatico, altrimenti puoi inserire un percorso diverso.
    update_build 1 0
  else
    echo "[INFO] Update build saltato (nessun ZIP rilevato)."
  fi

  # Backend/setup: usa solo auto-detection nella update dir.
  update_backend 1 0 0

  read -r -p "Riavviare i servizi ora? [s/N]: " yn
  if is_yes "$yn"; then
    systemctl daemon-reload
    sf_all start
  fi
}

edit_params() {
  echo "Scegli file da modificare:"
  echo "  [1] $ENV_FILE"
  echo "  [2] $IDLE_ENV_FILE"
  read -r -p "> " file_choice

  target=""
  case "$file_choice" in
    1) target="$ENV_FILE" ;;
    2) target="$IDLE_ENV_FILE" ;;
    *) echo "[INFO] Scelta non valida."; return ;;
  esac

  if [[ ! -f "$target" ]]; then
    echo "[ERR] File non trovato: $target"
    return
  fi

  "${EDITOR:-nano}" "$target"

  read -r -p "Riavviare i servizi ora? [s/N]: " yn
  if is_yes "$yn"; then
    sf_all restart
  fi
}

while true; do
  echo
  echo "============================================"
  echo "   SOULFRAME - Server Admin (Ubuntu)"
  echo "============================================"
  echo "Update dir: $UPDATE_DIR"
  echo "[1] Aggiorna (Build ZIP + Backend/Setup)"
  echo "[2] Spegni server"
  echo "[3] Riavvia server"
  echo "[4] Modifica parametri"
  echo "[5] Avvia server"
  echo "[6] Spegni VM"
  echo "[7] Gestisci backup"
  echo "[0] Esci"
  read -r -p "> " choice

  case "$choice" in
    1) update_all ;;
    2) sf_all stop ;;
    3) sf_all restart ;;
    4) edit_params ;;
    5) sf_all start ;;
    6) shutdown_vm ;;
    7) manage_backups ;;
    0) exit 0 ;;
    *) echo "[INFO] Scelta non valida." ;;
  esac
done
