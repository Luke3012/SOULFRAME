# SOULFRAME Setup (Ubuntu)

Questa cartella contiene gli script di deploy e gestione per VM Ubuntu.

## Contenuto

- `setup_soulframe_ubuntu.sh`
  - installazione/configurazione completa del server.
- `sf_admin_ubuntu.sh`
  - console amministrativa (`sfadmin`) per update, servizi e backup.
- `requirements.txt`
  - dipendenze Python di riferimento per Linux.

## Layout VM atteso

```text
/opt/soulframe
+-- backend
|   +-- avatar_asset_server.py
|   +-- coqui_tts_server.py
|   +-- rag_server.py
|   +-- whisper_server.py
|   +-- requirements.txt
|   +-- rag_store/
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

/home/<utente_corrente>
`-- soulframe_update
```

## setup_soulframe_ubuntu.sh

### Cosa fa

- Installa dipendenze sistema (`ffmpeg`, `tesseract`, `nano`, `rsync`, toolchain).
- Rileva automaticamente Python disponibile (`python3.12`, `3.11`, `3.10`, fallback `python3`).
- Installa/avvia Ollama.
- Installa Caddy e genera `Caddyfile` con reverse proxy:
  - `/api/whisper/* -> 127.0.0.1:8001`
  - `/api/rag/* -> 127.0.0.1:8002`
  - `/api/avatar/* -> 127.0.0.1:8003`
  - `/api/tts/* -> 127.0.0.1:8004`
- Prepara log Caddy con permessi corretti (`/var/log/caddy/access.log`, owner `caddy:caddy`).
- Crea venv in `/opt/soulframe/.venv`.
- Crea esplicitamente le cartelle:
  - `/opt/soulframe/webgl`
  - `/opt/soulframe/backups`
- Installa dipendenze Python da `/opt/soulframe/backend/requirements.txt`.
  - Se manca, prova a generarlo con `pipreqs`.
- Crea unit `systemd` per servizi AI.
- Crea helper:
  - `sfctl` (gestione servizi AI),
  - `sfurl` (URL/health),
  - `sfadmin` (console admin).
- Se lo esegui da una cartella diversa da `/opt/soulframe`, copia automaticamente:
  - `setup_soulframe_ubuntu.sh` e `sf_admin_ubuntu.sh` in `/opt/soulframe/SOULFRAME_SETUP`
  - file backend rilevati in `/opt/soulframe/backend` (da root progetto o da `soulframe_update`)
- Se nella stessa cartella dello script trova `SOULFRAME.zip`, lo estrae automaticamente
  (deve contenere `soulframe_update/`).
- Dopo la copia, rilancia automaticamente il setup da `/opt/soulframe/SOULFRAME_SETUP`.
- Al rilancio completato, prova a rimuovere lo script sorgente bootstrap
  (solo se proveniva da `soulframe_update`).
- Normalizza automaticamente i line ending (`CRLF -> LF`) per gli script Linux copiati.
- Crea automaticamente una cartella update per l'utente corrente:
  - default: `/home/<utente_corrente>/soulframe_update`
  - esportata anche come `SOULFRAME_UPDATE_DIR` in `soulframe.env`.
- Crea anche le cartelle backend necessarie se mancanti:
  - `/opt/soulframe/backend/avatar_store`
  - `/opt/soulframe/backend/rag_store`
  - `/opt/soulframe/backend/voices`
  - `/opt/soulframe/backend/voices/avatars`
- Configura auto-shutdown idle con timer.

### Variabili utili

- `CHAT_MODEL_DEFAULT` (default: `llama3.1:8b`)
- `EMBED_MODEL_DEFAULT` (default: `nomic-embed-text`)
- `WHISPER_MODEL_DEFAULT` (default: `medium`)
- `SKIP_OLLAMA_PULL=1` per saltare download modelli Ollama
- `TORCH_INSTALL_CMD` per forzare una build torch/torchaudio specifica (es. CUDA)
- `UPDATE_DROP_DIR` per personalizzare la cartella update automatica

### Note importanti

- `torch/torchaudio` sono installati da `requirements.txt`.
- Se vuoi una build CUDA specifica, usa `TORCH_INSTALL_CMD`.
- `soulframe.env` viene creato con permessi `640`; se disponibile `setfacl`, lo script prova ad aggiungere read all'utente che ha lanciato `sudo`.
- Rilanciare `setup_soulframe_ubuntu.sh` non crea duplicati di servizi/helper: unit file e script helper vengono sovrascritti in modo idempotente.

## sfctl

```bash
sfctl start|stop|restart|status|logs [whisper|rag|avatar|tts|ollama|all]
```

## sfadmin (sf_admin_ubuntu.sh)

Avvio:

```bash
sudo sfadmin
```

Menu:

- `[1]` Aggiorna tutto (Build ZIP + backend/setup)
- `[2]` Spegni server (ferma servizi AI + Caddy)
- `[3]` Riavvia server
- `[4]` Modifica parametri (`/etc/soulframe/soulframe.env`, `/etc/soulframe/idle.env`)
- `[5]` Avvia server
- `[6]` Spegni VM (shutdown macchina)
- `[7]` Gestisci backup (elimina singolo/tutti o mantieni ultimi `N`)
- `[0]` Esci

### Cartella update automatica

`sfadmin` usa `UPDATE_DIR` con questa priorita':

1. variabile ambiente `UPDATE_DIR` se impostata.
2. valore `SOULFRAME_UPDATE_DIR` in `/etc/soulframe/soulframe.env`.
3. fallback `/home/<utente_corrente>/soulframe_update`.

### Update unificato (opzione `[1]`)

Durante `[1]`, `sfadmin` fa in sequenza:

- update build da ZIP (`Build.zip` preferito, altrimenti ultimo `.zip` in `UPDATE_DIR`);
- update backend/setup dai file supportati in `UPDATE_DIR`.

File supportati:

- `avatar_asset_server.py`
- `coqui_tts_server.py`
- `rag_server.py`
- `whisper_server.py`
- `requirements.txt` (copiato in `/opt/soulframe/backend/requirements.txt`)
- `setup_soulframe_ubuntu.sh`
- `sf_admin_ubuntu.sh`

Se aggiorni `setup_soulframe_ubuntu.sh` e/o `sf_admin_ubuntu.sh`, `sfadmin` esegue automaticamente:

```bash
install -m 755 "$SETUP_DIR/sf_admin_ubuntu.sh" /usr/local/bin/sfadmin
cd "$SETUP_DIR" && SKIP_OLLAMA_PULL=1 ./setup_soulframe_ubuntu.sh
```

Per la parte build, se lasci vuoto il percorso ZIP, usa automaticamente:
- prima `Build.zip` (se presente in `UPDATE_DIR`);
- altrimenti il file ZIP piu recente trovato in `UPDATE_DIR`.

### Pulizia file update dopo conferma

Dopo update riuscito:

- puo eliminare il file ZIP sorgente;
- puo eliminare i file sorgente backend/setup applicati.

La pulizia:

- chiede sempre conferma esplicita (`[s/N]`);
- elimina solo file dentro `UPDATE_DIR`.

## Idle auto-shutdown

Configurazione in `/etc/soulframe/idle.env`:

- `IDLE_MINUTES` (default 30)
- `STARTUP_GRACE_MINUTES` (default 10)
- `LOG_FILE` (default `/var/log/caddy/access.log`)
- `LOG_TAIL_LINES` (default 20000, analisi ultime righe log JSON Caddy)
- `TRACK_SSH_ACTIVITY` (default 1, include attività terminali `pts/*`)
- `DRY_RUN` (default 0)

`idle_shutdown.sh`:

- non spegne durante i primi `STARTUP_GRACE_MINUTES` dal boot;
- considera attività web solo su endpoint `/api/*` (ignora richieste statiche/pagine pubbliche);
- se `TRACK_SSH_ACTIVITY=1`, considera anche l'attività SSH interattiva;
- spegne la VM solo se supera la soglia idle.

Test rapido:

```bash
sudo sed -i 's/^DRY_RUN=.*/DRY_RUN=1/' /etc/soulframe/idle.env
sudo /usr/local/bin/idle_shutdown.sh
```

## Caddy e dominio

Prerequisiti:

- record DNS `A` del dominio verso IP pubblico VM
- firewall aperto TCP `80` e `443`

Comandi utili:

```bash
sudo caddy validate --config /etc/caddy/Caddyfile
sudo systemctl status caddy --no-pager
```

## Deploy tipico

```bash
cd /percorso/dove/hai/i/file
sudo ./setup_soulframe_ubuntu.sh
sudo sfadmin
```

### Deploy da pacchetto ZIP unico

Se carichi un solo file `SOULFRAME.zip`, mettilo nella stessa cartella dove esegui `setup_soulframe_ubuntu.sh`.
Lo script estrarra `soulframe_update/` automaticamente, copiera i file in `/opt/soulframe/SOULFRAME_SETUP`,
si rilancera da li e continuera il setup.

## Permessi e line ending (CRLF/LF)

`/opt/soulframe` e i file sotto `/opt` sono normalmente di proprieta `root`.
Per modificare o installare file in quelle cartelle, usa `sudo`.

Lo script prova gia a normalizzare automaticamente i line ending degli script Linux.
Se devi forzare manualmente, usa:

```bash
sudo sed -i 's/\r$//' setup_soulframe_ubuntu.sh
sudo sed -i 's/\r$//' sf_admin_ubuntu.sh
sudo chmod +x setup_soulframe_ubuntu.sh sf_admin_ubuntu.sh
sudo ./setup_soulframe_ubuntu.sh
```
