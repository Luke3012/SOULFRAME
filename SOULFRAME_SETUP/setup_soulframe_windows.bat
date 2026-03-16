@echo off
setlocal EnableExtensions EnableDelayedExpansion
goto :MAIN

:VERIFY_SETUP
echo ============================
echo Verifiche ambiente AI
echo ============================

call :ENSURE_OPTIONAL_PATHS
call :ENSURE_VC_RUNTIME

call :CHECK_COMMAND "ollama.exe" "Ollama CLI"
set "OLLAMA_OK=%CHECK_LAST_OK%"
if "%OLLAMA_OK%"=="1" (
  call :CHECK_OLLAMA_MODEL "%EMBED_MODEL%" "embedding"
  call :CHECK_OLLAMA_MODEL "%CHAT_MODEL%" "chat"
) else (
  echo [WARN] Verifica modelli Ollama saltata: installa Ollama o aggiungilo al PATH.
)

call :CHECK_COMMAND "ffmpeg.exe" "FFmpeg"
set "FFMPEG_OK=%CHECK_LAST_OK%"
if "%FFMPEG_OK%"=="1" (
  call :CHECK_FFMPEG_SHARED
) else (
  echo [WARN] TTS potrebbe fallire con torchcodec: FFmpeg non trovato nel PATH.
)

call :CHECK_TESSERACT

echo [INFO] Verifica pacchetti Python del venv...
"%VENV_PY%" -c "import importlib.util,sys;mods=('torch','torchaudio','torchcodec','TTS');missing=[m for m in mods if importlib.util.find_spec(m) is None];print('[OK] Pacchetti Python TTS presenti.' if not missing else '[WARN] Pacchetti Python mancanti: ' + ', '.join(missing));sys.exit(0 if not missing else 1)"
if errorlevel 1 (
  echo [WARN] Controlla l'installazione delle dipendenze nel venv.
)
call :CHECK_TORCHCODEC_RUNTIME
goto :EOF

:CHECK_COMMAND
set "CHECK_LAST_OK=0"
where %~1 >nul 2>&1
if errorlevel 1 (
  echo [WARN] %~2 non trovato.
  goto :EOF
)
echo [OK] %~2 trovato.
set "CHECK_LAST_OK=1"
goto :EOF

:ENSURE_OPTIONAL_PATHS
call :ENSURE_TOOL_PATH "Tesseract OCR" "tesseract.exe" "C:\Program Files\Tesseract-OCR" "C:\Program Files (x86)\Tesseract-OCR" "%LOCALAPPDATA%\Programs\Tesseract-OCR"
call :ENSURE_FFMPEG_PATH
goto :EOF

:ENSURE_TOOL_PATH
set "ENSURE_LABEL=%~1"
set "ENSURE_EXE=%~2"
set "FOUND_TOOL_DIR="
shift
shift

where %ENSURE_EXE% >nul 2>&1
if not errorlevel 1 goto :EOF

:ENSURE_TOOL_PATH_LOOP
if "%~1"=="" goto :ENSURE_TOOL_PATH_DONE
if exist "%~1\%ENSURE_EXE%" (
  set "FOUND_TOOL_DIR=%~1"
  goto :ENSURE_TOOL_PATH_DONE
)
shift
goto :ENSURE_TOOL_PATH_LOOP

:ENSURE_TOOL_PATH_DONE
if not defined FOUND_TOOL_DIR goto :EOF
call :ADD_TO_USER_PATH_IF_MISSING "%FOUND_TOOL_DIR%"
goto :EOF

:ENSURE_FFMPEG_PATH
set "FOUND_TOOL_DIR="
for /f "usebackq delims=" %%F in (`where ffmpeg.exe 2^>nul`) do (
  set "FFMPEG_FOUND=%%~fF"
  goto :ENSURE_FFMPEG_FOUND
)
goto :ENSURE_FFMPEG_FALLBACK

:ENSURE_FFMPEG_FOUND
for %%D in ("!FFMPEG_FOUND!\..") do set "FOUND_TOOL_DIR=%%~fD"
if exist "!FOUND_TOOL_DIR!\avcodec-62.dll" goto :ENSURE_FFMPEG_APPLY
if exist "!FOUND_TOOL_DIR!\avcodec-61.dll" goto :ENSURE_FFMPEG_APPLY

:ENSURE_FFMPEG_FALLBACK
for %%D in (
  "C:\ffmpeg\bin"
  "C:\Program Files\ffmpeg\bin"
  "C:\Program Files (x86)\ffmpeg\bin"
  "%LOCALAPPDATA%\ffmpeg\bin"
  "%LOCALAPPDATA%\Microsoft\WinGet\Packages\Gyan.FFmpeg.Shared_Microsoft.Winget.Source_8wekyb3d8bbwe\ffmpeg-8.0.1-full_build-shared\bin"
) do (
  if exist "%%~fD\ffmpeg.exe" (
    set "FOUND_TOOL_DIR=%%~fD"
    goto :ENSURE_FFMPEG_APPLY
  )
)
goto :EOF

:ENSURE_FFMPEG_APPLY
set "SOULFRAME_FFMPEG_BIN=%FOUND_TOOL_DIR%"
call :ADD_TO_USER_PATH_IF_MISSING "%FOUND_TOOL_DIR%"
call :SET_USER_ENV_IF_MISSING "SOULFRAME_FFMPEG_BIN" "%FOUND_TOOL_DIR%"
goto :EOF

:ENSURE_VC_RUNTIME
if exist "C:\Windows\System32\vcruntime140.dll" if exist "C:\Windows\System32\msvcp140.dll" if exist "C:\Windows\System32\vcruntime140_1.dll" (
  echo [OK] Runtime Visual C++ presente.
  goto :EOF
)
echo [WARN] Runtime Visual C++ incompleto. Provo ad installarlo con winget...
where winget.exe >nul 2>&1
if errorlevel 1 (
  echo [WARN] winget non disponibile. Installa Microsoft Visual C++ Redistributable x64.
  goto :EOF
)
winget install --id Microsoft.VCRedist.2015+.x64 --exact --accept-package-agreements --accept-source-agreements --silent
if exist "C:\Windows\System32\vcruntime140.dll" if exist "C:\Windows\System32\msvcp140.dll" if exist "C:\Windows\System32\vcruntime140_1.dll" (
  echo [OK] Runtime Visual C++ installato.
) else (
  echo [WARN] Runtime Visual C++ non ancora rilevato.
)
goto :EOF

:ADD_TO_USER_PATH_IF_MISSING
set "TARGET_PATH=%~1"
if "%TARGET_PATH%"=="" goto :EOF

echo ;%PATH%; | findstr /I /C:";%TARGET_PATH%;" >nul
if errorlevel 1 (
  set "PATH=%PATH%;%TARGET_PATH%"
  echo [INFO] Aggiunto al PATH della sessione: %TARGET_PATH%
)

reg query "HKCU\Environment" /v Path >nul 2>&1
if errorlevel 1 (
  set "USER_PATH="
  goto :ADD_TO_USER_PATH_WRITE
)

for /f "tokens=2,*" %%A in ('reg query "HKCU\Environment" /v Path 2^>nul ^| findstr /R /C:"[ ]Path[ ]"') do (
  set "USER_PATH=%%B"
)

:ADD_TO_USER_PATH_WRITE
if not defined USER_PATH (
  set "NEW_USER_PATH=%TARGET_PATH%"
  reg add "HKCU\Environment" /v Path /t REG_EXPAND_SZ /d "%NEW_USER_PATH%" /f >nul
  echo [INFO] Aggiunto al PATH utente: %TARGET_PATH%
  goto :EOF
)

echo ;!USER_PATH!; | findstr /I /C:";%TARGET_PATH%;" >nul
if errorlevel 1 (
  set "NEW_USER_PATH=!USER_PATH!;%TARGET_PATH%"
  reg add "HKCU\Environment" /v Path /t REG_EXPAND_SZ /d "!NEW_USER_PATH!" /f >nul
  echo [INFO] Aggiunto al PATH utente: %TARGET_PATH%
)
goto :EOF

:SET_USER_ENV_IF_MISSING
set "ENV_NAME=%~1"
set "ENV_VALUE=%~2"
set "CURRENT_ENV_VALUE="
if "%ENV_NAME%"=="" goto :EOF
if "%ENV_VALUE%"=="" goto :EOF
for /f "tokens=2,*" %%A in ('reg query "HKCU\Environment" /v "%ENV_NAME%" 2^>nul ^| findstr /R /C:"[ ]%ENV_NAME%[ ]"') do (
  set "CURRENT_ENV_VALUE=%%B"
)
if /I "%CURRENT_ENV_VALUE%"=="%ENV_VALUE%" goto :EOF
reg add "HKCU\Environment" /v "%ENV_NAME%" /t REG_EXPAND_SZ /d "%ENV_VALUE%" /f >nul
echo [INFO] Variabile utente aggiornata: %ENV_NAME%
goto :EOF

:CHECK_OLLAMA_MODEL
set "MODEL_OK=0"
for /f "usebackq delims=" %%L in (`ollama list 2^>nul ^| findstr /I /C:"%~1"`) do set "MODEL_OK=1"
if "%MODEL_OK%"=="0" (
  call :CHECK_OLLAMA_MODEL_LOCAL "%~1"
)
if "%MODEL_OK%"=="1" (
  echo [OK] Modello Ollama %~2 presente: %~1
  goto :EOF
)
echo [WARN] Modello Ollama %~2 mancante: %~1
echo        Installa con: ollama pull %~1
goto :EOF

:CHECK_OLLAMA_MODEL_LOCAL
set "OLLAMA_MODEL_PATH=%~1"
set "OLLAMA_MODEL_PATH=%OLLAMA_MODEL_PATH::=\%"
set "OLLAMA_MODEL_PATH=%USERPROFILE%\.ollama\models\manifests\registry.ollama.ai\library\%OLLAMA_MODEL_PATH%"
if exist "%OLLAMA_MODEL_PATH%" set "MODEL_OK=1"
goto :EOF

:CHECK_FFMPEG_SHARED
set "FFMPEG_SHARED_OK=0"
for %%D in (avcodec-62.dll avcodec-61.dll avcodec-60.dll avutil-60.dll avutil-59.dll avutil-58.dll swresample-6.dll swresample-5.dll swresample-4.dll) do (
  where %%D >nul 2>&1
  if not errorlevel 1 set "FFMPEG_SHARED_OK=1"
)
if "%FFMPEG_SHARED_OK%"=="0" (
  call :RESOLVE_FFMPEG_SHARED_DIR
)
if "%FFMPEG_SHARED_OK%"=="1" (
  echo [OK] DLL FFmpeg condivise rilevate nel PATH.
  goto :EOF
)
echo [WARN] FFmpeg trovato ma non vedo DLL condivise nel PATH.
echo        Per Coqui/TorchCodec su Windows serve di solito la build FFmpeg "full-shared".
goto :EOF

:RESOLVE_FFMPEG_SHARED_DIR
set "FFMPEG_SHARED_DIR="
for /f "usebackq delims=" %%F in (`where ffmpeg.exe 2^>nul`) do (
  for %%D in ("%%~fF\..") do (
    if exist "%%~fD\avcodec-62.dll" (
      set "FFMPEG_SHARED_DIR=%%~fD"
      goto :RESOLVE_FFMPEG_SHARED_DIR_DONE
    )
    if exist "%%~fD\swresample-6.dll" (
      set "FFMPEG_SHARED_DIR=%%~fD"
      goto :RESOLVE_FFMPEG_SHARED_DIR_DONE
    )
  )
)
for %%D in (
  "C:\ffmpeg\bin"
  "C:\Program Files\ffmpeg\bin"
  "C:\Program Files (x86)\ffmpeg\bin"
  "%LOCALAPPDATA%\ffmpeg\bin"
  "%LOCALAPPDATA%\Microsoft\WinGet\Packages\Gyan.FFmpeg.Shared_Microsoft.Winget.Source_8wekyb3d8bbwe\ffmpeg-8.0.1-full_build-shared\bin"
) do (
  if exist "%%~fD\avcodec-62.dll" (
    set "FFMPEG_SHARED_DIR=%%~fD"
    goto :RESOLVE_FFMPEG_SHARED_DIR_DONE
  )
)
:RESOLVE_FFMPEG_SHARED_DIR_DONE
if defined FFMPEG_SHARED_DIR set "FFMPEG_SHARED_OK=1"
goto :EOF

:CHECK_TESSERACT
set "CHECK_LAST_OK=0"
where tesseract.exe >nul 2>&1
if not errorlevel 1 (
  echo [OK] Tesseract OCR trovato nel PATH.
  set "CHECK_LAST_OK=1"
  goto :EOF
)
if exist "C:\Program Files\Tesseract-OCR\tesseract.exe" (
  echo [OK] Tesseract OCR installato in C:\Program Files\Tesseract-OCR\tesseract.exe
  echo [WARN] Tesseract non e' ancora nel PATH della sessione corrente.
  set "CHECK_LAST_OK=1"
  goto :EOF
)
echo [WARN] Tesseract OCR non trovato.
goto :EOF

:CHECK_TORCHCODEC_RUNTIME
set "TORCHCODEC_IMPORT_OK=0"
set "USER_FFMPEG_BIN="
if defined SOULFRAME_FFMPEG_BIN if exist "%SOULFRAME_FFMPEG_BIN%\ffmpeg.exe" (
  set "PATH=%SOULFRAME_FFMPEG_BIN%;%PATH%"
)
for /f "tokens=2,*" %%A in ('reg query "HKCU\Environment" /v SOULFRAME_FFMPEG_BIN 2^>nul ^| findstr /R /C:"[ ]SOULFRAME_FFMPEG_BIN[ ]"') do (
  set "USER_FFMPEG_BIN=%%B"
)
if defined USER_FFMPEG_BIN if exist "!USER_FFMPEG_BIN!\ffmpeg.exe" (
  set "PATH=!USER_FFMPEG_BIN!;%PATH%"
)
echo [INFO] Test runtime torchcodec...
"%VENV_PY%" -c "import torch, torchaudio, torchcodec; print('[OK] Runtime torchcodec operativo.')"
if errorlevel 1 (
  echo [WARN] Runtime torchcodec ancora non operativo.
  echo        Riapri il terminale o usa ai_services.cmd debugaudio per una diagnosi mirata.
  goto :EOF
)
goto :EOF

:MAIN
set "SCRIPT_DIR=%~dp0"
for %%I in ("%SCRIPT_DIR%..") do set "REPO_ROOT=%%~fI"

set "AI_DIR=%REPO_ROOT%\SOULFRAME_AI"
set "BACKEND_DIR=%AI_DIR%\backend"
set "DEFAULT_VENV=%BACKEND_DIR%\venv"
set "REQUIREMENTS=%BACKEND_DIR%\requirements.txt"
set "EMBED_MODEL=nomic-embed-text"
set "CHAT_MODEL=llama3:8b-instruct-q4_K_M"
set "FORCE_PIP_INSTALL=0"
if not exist "%REQUIREMENTS%" set "REQUIREMENTS=%AI_DIR%\requirements.txt"

if not exist "%AI_DIR%" (
  echo [ERRORE] Cartella SOULFRAME_AI non trovata: %AI_DIR%
  exit /b 1
)

if not exist "%BACKEND_DIR%" (
  echo [ERRORE] Cartella backend non trovata: %BACKEND_DIR%
  exit /b 1
)

if not exist "%REQUIREMENTS%" (
  echo [ERRORE] requirements.txt non trovato: %REQUIREMENTS%
  exit /b 1
)

set "VENV_PATH=%~1"
if /I "%~1"=="--force" (
  set "VENV_PATH="
  set "FORCE_PIP_INSTALL=1"
)
if /I "%~2"=="--force" set "FORCE_PIP_INSTALL=1"
if "%VENV_PATH%"=="" (
  set /p VENV_PATH=Inserisci il percorso completo per il venv ^(invio per default: %DEFAULT_VENV%^): 
)
if "%VENV_PATH%"=="" set "VENV_PATH=%DEFAULT_VENV%"

if exist "%VENV_PATH%\Scripts\python.exe" (
  echo [INFO] Venv gia' presente in: %VENV_PATH%
) else (
  echo Creo venv in: %VENV_PATH%
  py -3.11 -m venv "%VENV_PATH%"
)

set "VENV_PY=%VENV_PATH%\Scripts\python.exe"
set "VENV_PIP=%VENV_PATH%\Scripts\pip.exe"

if not exist "%VENV_PY%" (
  echo [ERRORE] Python venv non trovato in %VENV_PATH%
  exit /b 1
)

echo Aggiorno pip
"%VENV_PY%" -m pip install --upgrade pip wheel

set "CORE_DEPS_OK=0"
"%VENV_PY%" -c "import importlib.util,sys;mods=('torch','torchaudio','torchcodec','TTS');missing=[m for m in mods if importlib.util.find_spec(m) is None];sys.exit(0 if not missing else 1)"
if not errorlevel 1 set "CORE_DEPS_OK=1"

if "%FORCE_PIP_INSTALL%"=="1" (
  echo Installo dipendenze da %REQUIREMENTS% ^(forzato^)
  "%VENV_PIP%" install -r "%REQUIREMENTS%"
) else if "%CORE_DEPS_OK%"=="1" (
  echo [INFO] Dipendenze Python core gia' presenti: salto reinstallazione completa.
) else (
  echo Installo dipendenze da %REQUIREMENTS%
  "%VENV_PIP%" install -r "%REQUIREMENTS%"
)

set /p GEMINI_API_KEY=Inserisci GEMINI_API_KEY ^(invio per lasciare invariato/non creare backend\gemini_key.txt^): 
if not "%GEMINI_API_KEY%"=="" (
  set "GEMINI_FILE=%BACKEND_DIR%\gemini_key.txt"
  > "%GEMINI_FILE%" (
    set /p "=%GEMINI_API_KEY%"
  )
  echo.
  echo Salvata GEMINI_API_KEY in %GEMINI_FILE%
)

echo.
call :VERIFY_SETUP
echo.
echo Setup completato.
echo Per avviare i servizi:
echo   cd /d "%AI_DIR%"
echo   ai_services.cmd 1
echo.
echo Per attivare il venv:
echo   %VENV_PATH%\Scripts\activate.bat
echo.
pause

endlocal
exit /b 0
