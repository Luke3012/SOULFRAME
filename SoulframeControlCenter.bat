@echo off
setlocal EnableExtensions EnableDelayedExpansion
rem SOULFRAME Control Center: switch stream Git A/B, push/pull, revert commit e creazione pacchetto soulframe_update.

cd /d "%~dp0"

set "DIR_ACTIVE=.git"
set "DIR_A=.git_stream_a"
set "DIR_B=.git_stream_b"
set "AI_SERVICES_CMD=%~dp0SOULFRAME_AI\ai_services.cmd"

set "NOME_DESTINAZIONE=soulframe_update"
set "PERCORSO_DEST=%USERPROFILE%\Desktop\%NOME_DESTINAZIONE%"
set "CARTELLA_WEBGL=Build"
set "EXIT_AFTER_BACKUP=0"

call :Bootstrap
if errorlevel 1 (
    echo.
    echo Premi un tasto per chiudere...
    pause >nul
    exit /b 1
)

:MAIN_MENU
cls
call :DetectActiveSlot
call :RenderMenu
set "MENU_CHOICE="
set /p "MENU_CHOICE=Seleziona opzione [1a/1b/1c/2/3/4/5/6/7/0]: "

if "%MENU_CHOICE%"=="0" goto END
if /I "%MENU_CHOICE%"=="1a" goto MENU_SERVER_START
if /I "%MENU_CHOICE%"=="1b" goto MENU_SERVER_STOP
if /I "%MENU_CHOICE%"=="1c" goto MENU_SERVER_RESTART
if "%MENU_CHOICE%"=="7" goto MENU_REVERT
if "%MENU_CHOICE%"=="6" goto MENU_BACKUP
if "%MENU_CHOICE%"=="5" goto MENU_PULL
if "%MENU_CHOICE%"=="4" goto MENU_PUSH
if "%MENU_CHOICE%"=="3" goto MENU_SWITCH_B
if "%MENU_CHOICE%"=="2" goto MENU_SWITCH_A
echo.
echo [WARN] Selezione non valida: "%MENU_CHOICE%"
call :PauseScreen
goto MAIN_MENU

:MENU_SERVER_START
call :RunAiServices 1
goto END

:MENU_SERVER_STOP
call :RunAiServices 2
goto END

:MENU_SERVER_RESTART
call :RunAiServices 3
call :PauseScreen
goto MAIN_MENU

:MENU_REVERT
call :GitRevertMenu
call :PauseScreen
goto MAIN_MENU

:MENU_BACKUP
set "EXIT_AFTER_BACKUP=0"
call :CreateBackup
if "!EXIT_AFTER_BACKUP!"=="1" goto END
call :PauseScreen
goto MAIN_MENU

:MENU_PULL
call :GitPull
call :PauseScreen
goto MAIN_MENU

:MENU_PUSH
call :GitPush
call :PauseScreen
goto MAIN_MENU

:MENU_SWITCH_B
call :SwitchSlot B
call :PauseScreen
goto MAIN_MENU

:MENU_SWITCH_A
call :SwitchSlot A
call :PauseScreen
goto MAIN_MENU

:END
echo.
echo Uscita dal tool SOULFRAME.
endlocal
exit /b 0

:Bootstrap
if not exist "%DIR_ACTIVE%\*" call :TryRecoverActiveGit
if not exist "%DIR_ACTIVE%\*" (
    echo [ERRORE] Cartella "%DIR_ACTIVE%" non trovata e ripristino automatico fallito.
    echo         Esegui questo script nella root del progetto SOULFRAME.
    echo         Se hai solo ".git_stream_a" o ".git_stream_b", rinomina manualmente in ".git".
    exit /b 1
)
call :DetectActiveSlot
if /I "%ACTIVE_SLOT%"=="?" echo [WARN] Impossibile determinare con certezza lo stream attivo.
exit /b 0

:TryRecoverActiveGit
if exist "%DIR_ACTIVE%\*" exit /b 0
if exist "%DIR_A%\*" if not exist "%DIR_B%\*" (
    echo [WARN] ".git" mancante, ripristino da "%DIR_A%".
    move "%DIR_A%" "%DIR_ACTIVE%" >nul
    exit /b 0
)
if exist "%DIR_B%\*" if not exist "%DIR_A%\*" (
    echo [WARN] ".git" mancante, ripristino da "%DIR_B%".
    move "%DIR_B%" "%DIR_ACTIVE%" >nul
    exit /b 0
)
if exist "%DIR_A%\*" if exist "%DIR_B%\*" (
    echo [WARN] ".git" mancante e entrambi gli stream presenti.
    echo [WARN] Ripristino predefinito da "%DIR_A%".
    move "%DIR_A%" "%DIR_ACTIVE%" >nul
)
exit /b 0

:RenderMenu
set "STATUS_LABEL=SCONOSCIUTO"
if /I "%ACTIVE_SLOT%"=="A" set "STATUS_LABEL=A (STANDARD)"
if /I "%ACTIVE_SLOT%"=="B" set "STATUS_LABEL=B (DEV)"

set "A_STATUS=ASSENTE O ATTIVO"
if exist "%DIR_A%\*" set "A_STATUS=ARCHIVIATO"
if exist "%DIR_A%" if not exist "%DIR_A%\*" set "A_STATUS=ERRORE (file)"

set "B_STATUS=ASSENTE O ATTIVO"
if exist "%DIR_B%\*" set "B_STATUS=ARCHIVIATO"
if exist "%DIR_B%" if not exist "%DIR_B%\*" set "B_STATUS=ERRORE (file)"

echo ------------------------------------------------------------
echo                  SOULFRAME CONTROL CENTER
echo ------------------------------------------------------------
echo  Stream attivo: %STATUS_LABEL%
echo  Slot A: %A_STATUS%   ^|   Slot B: %B_STATUS%
echo ------------------------------------------------------------
echo  [1a] Avvia server AI/WebGL
echo  [1b] Chiudi server AI/WebGL
echo  [1c] Riavvia server AI/WebGL
echo  [2]  Carica stream A (STANDARD)
echo  [3]  Carica stream B (DEV)
echo  [4]  Git push (add + commit + push)
echo  [5]  Git pull
echo  [6]  Backup deploy Ubuntu
echo  [7]  Ripristino commit (soft/hard)
echo  [0] Esci
echo ------------------------------------------------------------
exit /b 0

:DetectActiveSlot
set "ACTIVE_SLOT=?"
if exist "%DIR_A%\*" if not exist "%DIR_B%\*" set "ACTIVE_SLOT=B"
if exist "%DIR_B%\*" if not exist "%DIR_A%\*" set "ACTIVE_SLOT=A"
if /I "%ACTIVE_SLOT%"=="?" if not exist "%DIR_A%\*" if not exist "%DIR_B%\*" if exist "%DIR_ACTIVE%\*" set "ACTIVE_SLOT=A"
exit /b 0

:SwitchSlot
set "TARGET=%~1"
if /I "%TARGET%"=="A" (
    set "TARGET_DIR=%DIR_A%"
    set "PARK_DIR=%DIR_B%"
    set "TARGET_LABEL=A STANDARD"
) else (
    set "TARGET_DIR=%DIR_B%"
    set "PARK_DIR=%DIR_A%"
    set "TARGET_LABEL=B DEV"
)

call :DetectActiveSlot
echo.
echo ------------------------------------------------------------
echo  SWITCH STREAM -^> !TARGET_LABEL!
echo ------------------------------------------------------------

if not exist "%DIR_ACTIVE%\*" (
    echo [ERRORE] Cartella "%DIR_ACTIVE%" non trovata.
    exit /b 1
)
if /I "%ACTIVE_SLOT%"=="%TARGET%" (
    echo [OK] !TARGET_LABEL! e' gia attivo.
    exit /b 0
)
if not exist "%TARGET_DIR%\*" (
    if /I "%TARGET%"=="A" if not exist "%DIR_A%\*" if not exist "%DIR_B%\*" (
        echo [INFO] Nessuno slot archiviato trovato: considero A come stream attivo.
        exit /b 0
    )
    echo [ERRORE] Cartella "%TARGET_DIR%" non trovata.
    echo         Non posso caricare !TARGET_LABEL!.
    exit /b 1
)

if exist "%PARK_DIR%\*" (
    echo [INFO] Pulizia preventiva di "%PARK_DIR%"...
    rmdir /s /q "%PARK_DIR%"
)
if exist "%PARK_DIR%" if not exist "%PARK_DIR%\*" (
    echo [INFO] Pulizia file anomalo "%PARK_DIR%"...
    del /f /q "%PARK_DIR%"
)
if exist "%PARK_DIR%" (
    echo [ERRORE] Impossibile ripulire "%PARK_DIR%".
    exit /b 1
)

attrib -h -s "%DIR_ACTIVE%" >nul 2>nul
attrib -h -s "%TARGET_DIR%" >nul 2>nul
attrib -h -s "%PARK_DIR%" >nul 2>nul

echo [INFO] Archiviazione stream corrente in "%PARK_DIR%"...
move "%DIR_ACTIVE%" "%PARK_DIR%" >nul
if errorlevel 1 (
    echo [ERRORE] Move "%DIR_ACTIVE%" -^> "%PARK_DIR%" fallita.
    exit /b 1
)

echo [INFO] Carico "%TARGET_DIR%" come "%DIR_ACTIVE%"...
move "%TARGET_DIR%" "%DIR_ACTIVE%" >nul
if errorlevel 1 (
    echo [ERRORE] Move "%TARGET_DIR%" -^> "%DIR_ACTIVE%" fallita.
    echo [INFO] Provo a ripristinare il vecchio stato...
    if exist "%PARK_DIR%\*" move "%PARK_DIR%" "%DIR_ACTIVE%" >nul
    exit /b 1
)

attrib +h "%DIR_ACTIVE%" >nul 2>nul
if exist "%PARK_DIR%\*" attrib +h "%PARK_DIR%" >nul 2>nul

echo [OK] Stream caricato: !TARGET_LABEL!
exit /b 0

:EnsureGit
if not exist "%DIR_ACTIVE%\*" (
    echo [ERRORE] Cartella "%DIR_ACTIVE%" non trovata.
    exit /b 1
)
where git >nul 2>nul
if errorlevel 1 (
    echo [ERRORE] Git non trovato nel PATH.
    exit /b 1
)
exit /b 0

:RunAiServices
if not exist "%AI_SERVICES_CMD%" (
    echo [ERRORE] File non trovato: "%AI_SERVICES_CMD%"
    exit /b 1
)

if "%~1"=="1" (
    echo [INFO] Avvio servizi con:
    echo        "%AI_SERVICES_CMD%" 1
)
if "%~1"=="2" (
    echo [INFO] Chiusura servizi con:
    echo        "%AI_SERVICES_CMD%" 2
)
if "%~1"=="3" (
    echo [INFO] Riavvio servizi con:
    echo        "%AI_SERVICES_CMD%" 3
)

call "%AI_SERVICES_CMD%" %~1
if errorlevel 1 (
    echo [ERRORE] ai_services.cmd ha restituito errore.
    exit /b 1
)
echo [OK] Operazione servizi completata.
exit /b 0

:GitPush
call :EnsureGit
if errorlevel 1 exit /b 1

echo.
echo ------------------------------------------------------------
echo  GIT PUSH
echo ------------------------------------------------------------
set "COMMIT_MESSAGE="
echo Inserisci messaggio commit (vuoto = "update").
set /p "COMMIT_MESSAGE=Messaggio commit [update]: "
if not defined COMMIT_MESSAGE set "COMMIT_MESSAGE=update"

echo [git] add .
git add .
if errorlevel 1 (
    echo [ERRORE] git add fallito.
    exit /b 1
)

echo [git] commit -m "%COMMIT_MESSAGE%"
git commit -m "%COMMIT_MESSAGE%"
if errorlevel 1 (
    echo [INFO] Commit non creato: nessuna modifica o warning. Continuo con push.
)

echo [git] push
git push
if errorlevel 1 (
    echo [ERRORE] git push fallito.
    exit /b 1
)

echo [OK] Push completato.
exit /b 0

:GitPull
call :EnsureGit
if errorlevel 1 exit /b 1

echo.
echo ------------------------------------------------------------
echo  GIT PULL
echo ------------------------------------------------------------
echo [git] pull
git pull
if errorlevel 1 (
    echo [ERRORE] git pull fallito.
    exit /b 1
)
echo [OK] Pull completato.
exit /b 0

:GitRevertMenu
call :EnsureGit
if errorlevel 1 exit /b 1

echo.
echo ------------------------------------------------------------
echo  RIPRISTINO COMMIT
echo ------------------------------------------------------------

set "TMP_HASHES=%TEMP%\sf_commits_%RANDOM%_%RANDOM%.txt"
git log -n 15 --pretty=format:"%%H" > "%TMP_HASHES%"
if errorlevel 1 (
    echo [ERRORE] Impossibile leggere la cronologia git.
    if exist "%TMP_HASHES%" del /f /q "%TMP_HASHES%" >nul 2>nul
    exit /b 1
)

set "COMMIT_COUNT=0"
for /f "usebackq delims=" %%H in ("%TMP_HASHES%") do (
    set /a COMMIT_COUNT+=1
    set "COMMIT_HASH[!COMMIT_COUNT!]=%%H"
    for /f "delims=" %%L in ('git show -s --date^=short --format^="%%h - %%s - %%an - %%ad" %%H') do (
        echo [!COMMIT_COUNT!] %%L
    )
)

if "%COMMIT_COUNT%"=="0" (
    echo [WARN] Nessun commit disponibile.
    if exist "%TMP_HASHES%" del /f /q "%TMP_HASHES%" >nul 2>nul
    exit /b 1
)

echo.
set "REVERT_INDEX="
set /p "REVERT_INDEX=Seleziona numero commit da ripristinare [0 annulla]: "
if "%REVERT_INDEX%"=="0" (
    echo [INFO] Operazione annullata.
    if exist "%TMP_HASHES%" del /f /q "%TMP_HASHES%" >nul 2>nul
    exit /b 0
)

echo(%REVERT_INDEX%| findstr /r "^[0-9][0-9]*$" >nul
if errorlevel 1 (
    echo [ERRORE] Selezione non valida.
    if exist "%TMP_HASHES%" del /f /q "%TMP_HASHES%" >nul 2>nul
    exit /b 1
)

if %REVERT_INDEX% LSS 1 (
    echo [ERRORE] Selezione fuori intervallo.
    if exist "%TMP_HASHES%" del /f /q "%TMP_HASHES%" >nul 2>nul
    exit /b 1
)
if %REVERT_INDEX% GTR %COMMIT_COUNT% (
    echo [ERRORE] Selezione fuori intervallo.
    if exist "%TMP_HASHES%" del /f /q "%TMP_HASHES%" >nul 2>nul
    exit /b 1
)

call set "TARGET_HASH=%%COMMIT_HASH[%REVERT_INDEX%]%%"
if not defined TARGET_HASH (
    echo [ERRORE] Commit non trovato.
    if exist "%TMP_HASHES%" del /f /q "%TMP_HASHES%" >nul 2>nul
    exit /b 1
)

echo.
for /f "delims=" %%L in ('git show -s --date^=short --format^="%%h - %%s - %%an - %%ad" %TARGET_HASH%') do (
    echo Selezionato: %%L
)
echo [1] Soft revert: git revert --no-edit
echo [2] Hard reset: git reset --hard
set "REVERT_MODE="
set /p "REVERT_MODE=Scegli modalita' [1/2, altro annulla]: "

if "%REVERT_MODE%"=="1" (
    echo [git] revert --no-edit %TARGET_HASH%
    git revert --no-edit "%TARGET_HASH%"
    if errorlevel 1 (
        echo [ERRORE] Soft revert fallito. Potrebbero esserci conflitti.
        if exist "%TMP_HASHES%" del /f /q "%TMP_HASHES%" >nul 2>nul
        exit /b 1
    )
    echo [OK] Soft revert completato. Esegui push per pubblicarlo.
    if exist "%TMP_HASHES%" del /f /q "%TMP_HASHES%" >nul 2>nul
    exit /b 0
)

if "%REVERT_MODE%"=="2" (
    echo [ATTENZIONE] Hard reset distruttivo su modifiche locali.
    set "HARD_CONFIRM="
    set /p "HARD_CONFIRM=Digita HARD per confermare: "
    if /I not "%HARD_CONFIRM%"=="HARD" (
        echo [INFO] Operazione annullata.
        if exist "%TMP_HASHES%" del /f /q "%TMP_HASHES%" >nul 2>nul
        exit /b 0
    )
    echo [git] reset --hard %TARGET_HASH%
    git reset --hard "%TARGET_HASH%"
    if errorlevel 1 (
        echo [ERRORE] Hard reset fallito.
        if exist "%TMP_HASHES%" del /f /q "%TMP_HASHES%" >nul 2>nul
        exit /b 1
    )
    echo [OK] Hard reset completato. Esegui push se vuoi riallineare il remoto.
    if exist "%TMP_HASHES%" del /f /q "%TMP_HASHES%" >nul 2>nul
    exit /b 0
)

echo [INFO] Operazione annullata.
if exist "%TMP_HASHES%" del /f /q "%TMP_HASHES%" >nul 2>nul
exit /b 0

:CreateBackup
echo.
echo ------------------------------------------------------------
echo  BACKUP DEPLOY UBUNTU
echo ------------------------------------------------------------

if not exist "%CARTELLA_WEBGL%" (
    echo [ERRORE] La cartella "%CARTELLA_WEBGL%" non esiste.
    echo         Esegui prima una build WebGL.
    exit /b 1
)

if exist "%PERCORSO_DEST%" (
    echo [INFO] Pulisco la vecchia destinazione...
    rmdir /s /q "%PERCORSO_DEST%"
)
mkdir "%PERCORSO_DEST%"

set "ZIP_PATH=%PERCORSO_DEST%\Build.zip"
set "WINRAR_EXE="
if exist "%ProgramFiles%\WinRAR\WinRAR.exe" set "WINRAR_EXE=%ProgramFiles%\WinRAR\WinRAR.exe"
if exist "%ProgramFiles(x86)%\WinRAR\WinRAR.exe" set "WINRAR_EXE=%ProgramFiles(x86)%\WinRAR\WinRAR.exe"
if not defined WINRAR_EXE for %%I in (WinRAR.exe) do set "WINRAR_EXE=%%~$PATH:I"

set "SEVENZIP_EXE="
if exist "%ProgramFiles%\7-Zip\7z.exe" set "SEVENZIP_EXE=%ProgramFiles%\7-Zip\7z.exe"
if exist "%ProgramFiles(x86)%\7-Zip\7z.exe" set "SEVENZIP_EXE=%ProgramFiles(x86)%\7-Zip\7z.exe"
if not defined SEVENZIP_EXE for %%I in (7z.exe) do set "SEVENZIP_EXE=%%~$PATH:I"

echo [INFO] Creo Build.zip...
if defined WINRAR_EXE (
    echo [INFO] Uso WinRAR: "%WINRAR_EXE%"
    "%WINRAR_EXE%" a -afzip -ep1 -r -ibck -inul "%ZIP_PATH%" "%CARTELLA_WEBGL%\*"
    if errorlevel 2 (
        echo [ERRORE] WinRAR ha fallito durante la creazione dello ZIP.
        exit /b 1
    )
) else if defined SEVENZIP_EXE (
    echo [INFO] Uso 7-Zip: "%SEVENZIP_EXE%"
    "%SEVENZIP_EXE%" a -tzip -mx=5 "%ZIP_PATH%" "%CARTELLA_WEBGL%\*"
    if errorlevel 1 (
        echo [ERRORE] 7-Zip ha fallito durante la creazione dello ZIP.
        exit /b 1
    )
) else (
    echo [WARN] WinRAR/7-Zip non trovati. Uso PowerShell Compress-Archive.
    powershell -NoProfile -Command "Compress-Archive -Path '%CARTELLA_WEBGL%\*' -DestinationPath '%ZIP_PATH%' -Force"
    if errorlevel 1 (
        echo [ERRORE] Problema durante la compressione con PowerShell.
        exit /b 1
    )
)

echo [INFO] Copia file deploy...
call :CopyRequired "SOULFRAME_SETUP\setup_soulframe_ubuntu.sh" "%PERCORSO_DEST%\" || exit /b 1
call :CopyRequired "SOULFRAME_SETUP\sf_admin_ubuntu.sh" "%PERCORSO_DEST%\" || exit /b 1
call :CopyRequired "SOULFRAME_AI\backend\requirements.txt" "%PERCORSO_DEST%\" || exit /b 1
call :CopyRequired "SOULFRAME_AI\backend\avatar_asset_server.py" "%PERCORSO_DEST%\" || exit /b 1
call :CopyRequired "SOULFRAME_AI\backend\coqui_tts_server.py" "%PERCORSO_DEST%\" || exit /b 1
call :CopyRequired "SOULFRAME_AI\backend\rag_server.py" "%PERCORSO_DEST%\" || exit /b 1
call :CopyRequired "SOULFRAME_AI\backend\whisper_server.py" "%PERCORSO_DEST%\" || exit /b 1

echo [OK] Backup completato.
echo      Cartella pronta: "%PERCORSO_DEST%"
echo.
set "OPEN_AFTER_BACKUP="
set /p "OPEN_AFTER_BACKUP=Aprire la cartella di backup ed uscire? [S/N]: "
if /I "%OPEN_AFTER_BACKUP%"=="S" (
    start "" explorer "%PERCORSO_DEST%"
    set "EXIT_AFTER_BACKUP=1"
    echo [OK] Cartella aperta. Chiusura programma...
)
if /I "%OPEN_AFTER_BACKUP%"=="SI" (
    start "" explorer "%PERCORSO_DEST%"
    set "EXIT_AFTER_BACKUP=1"
    echo [OK] Cartella aperta. Chiusura programma...
)
if /I "%OPEN_AFTER_BACKUP%"=="Y" (
    start "" explorer "%PERCORSO_DEST%"
    set "EXIT_AFTER_BACKUP=1"
    echo [OK] Cartella aperta. Chiusura programma...
)
exit /b 0

:CopyRequired
if not exist "%~1" (
    echo [ERRORE] File mancante: "%~1"
    exit /b 1
)
copy /Y "%~1" "%~2" >nul
if errorlevel 1 (
    echo [ERRORE] Copia fallita: "%~1"
    exit /b 1
)
exit /b 0

:PauseScreen
echo.
pause
exit /b 0
