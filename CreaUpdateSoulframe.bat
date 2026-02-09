@echo off
setlocal EnableDelayedExpansion

cd /d "%~dp0"

:: --- CONFIGURAZIONE ---
set "NOME_DESTINAZIONE=soulframe_update"
set "PERCORSO_DEST=%USERPROFILE%\Desktop\%NOME_DESTINAZIONE%"
set "CARTELLA_WEBGL=Build"

echo ----------------------------------------------------
echo  CREAZIONE PACCHETTO SOULFRAME
echo ----------------------------------------------------

:: 1. CONTROLLI PRELIMINARI
if not exist "%CARTELLA_WEBGL%" (
    echo [ERRORE] La cartella WebGL "%CARTELLA_WEBGL%" non esiste qui!
    echo Assicurati di eseguire il file .bat dalla cartella del progetto.
    pause
    exit /b
)

:: 2. PREPARAZIONE CARTELLA DESTINAZIONE
if exist "%PERCORSO_DEST%" (
    echo  [+] Pulisco la vecchia cartella di destinazione...
    rmdir /s /q "%PERCORSO_DEST%"
)
mkdir "%PERCORSO_DEST%"

:: 3. CREAZIONE ZIP (BUILD)
echo  [+] Creazione di Build.zip in corso...

:: Usa WinRAR (stesso comportamento del packaging manuale), fallback 7-Zip, fallback PowerShell
set "ZIP_PATH=%PERCORSO_DEST%\Build.zip"
set "WINRAR_EXE="
if exist "%ProgramFiles%\WinRAR\WinRAR.exe" set "WINRAR_EXE=%ProgramFiles%\WinRAR\WinRAR.exe"
if exist "%ProgramFiles(x86)%\WinRAR\WinRAR.exe" set "WINRAR_EXE=%ProgramFiles(x86)%\WinRAR\WinRAR.exe"
if not defined WINRAR_EXE (
    for %%I in (WinRAR.exe) do set "WINRAR_EXE=%%~$PATH:I"
)

set "SEVENZIP_EXE="
if exist "%ProgramFiles%\7-Zip\7z.exe" set "SEVENZIP_EXE=%ProgramFiles%\7-Zip\7z.exe"
if exist "%ProgramFiles(x86)%\7-Zip\7z.exe" set "SEVENZIP_EXE=%ProgramFiles(x86)%\7-Zip\7z.exe"
if not defined SEVENZIP_EXE (
    for %%I in (7z.exe) do set "SEVENZIP_EXE=%%~$PATH:I"
)

if defined WINRAR_EXE (
    echo  [+] Uso WinRAR: "%WINRAR_EXE%"
    "%WINRAR_EXE%" a -afzip -ep1 -r -ibck -inul "%ZIP_PATH%" "%CARTELLA_WEBGL%\*"
    if errorlevel 2 (
        echo [ERRORE] WinRAR ha fallito durante la creazione dello ZIP.
        pause
        exit /b
    )
) else if defined SEVENZIP_EXE (
    echo  [+] Uso 7-Zip: "%SEVENZIP_EXE%"
    "%SEVENZIP_EXE%" a -tzip -mx=5 "%ZIP_PATH%" "%CARTELLA_WEBGL%\*"
    if errorlevel 1 (
        echo [ERRORE] 7-Zip ha fallito durante la creazione dello ZIP.
        pause
        exit /b
    )
) else (
    echo  [WARN] WinRAR/7-Zip non trovati. Fallback a PowerShell Compress-Archive.
    powershell -NoProfile -Command "Compress-Archive -Path '%CARTELLA_WEBGL%\*' -DestinationPath '%ZIP_PATH%' -Force"
    if errorlevel 1 (
        echo [ERRORE] C'e' stato un problema durante la compressione PowerShell.
        pause
        exit /b
    )
)

:: 4. COPIA DEI FILE SINGOLI (Script e Backend)
echo  [+] Copia dei file di configurazione...

copy /Y "SOULFRAME_SETUP\setup_soulframe_ubuntu.sh"   "%PERCORSO_DEST%\" >nul
copy /Y "SOULFRAME_SETUP\sf_admin_ubuntu.sh"          "%PERCORSO_DEST%\" >nul
copy /Y "SOULFRAME_AI\backend\requirements.txt"       "%PERCORSO_DEST%\" >nul

copy /Y "SOULFRAME_AI\backend\avatar_asset_server.py" "%PERCORSO_DEST%\" >nul
copy /Y "SOULFRAME_AI\backend\coqui_tts_server.py"    "%PERCORSO_DEST%\" >nul
copy /Y "SOULFRAME_AI\backend\rag_server.py"          "%PERCORSO_DEST%\" >nul
copy /Y "SOULFRAME_AI\backend\whisper_server.py"      "%PERCORSO_DEST%\" >nul

echo.
echo ----------------------------------------------------
echo  OPERAZIONE COMPLETATA CON SUCCESSO!
echo  Cartella creata: %PERCORSO_DEST%
echo ----------------------------------------------------
echo.
