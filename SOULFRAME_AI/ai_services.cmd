@echo off
setlocal EnableExtensions EnableDelayedExpansion

REM ============================================================
REM SOULFRAME - AI Services Launcher
REM  Args:
REM   ai_services.cmd 1  -> start
REM   ai_services.cmd 2  -> stop (kill per porta - SOLO python)
REM   ai_services.cmd    -> menu
REM ============================================================

set "ROOT=%~dp0"
set "BACKEND=%ROOT%backend"

REM Prefer backend\venv, fallback backend\.venv
set "VENV_DIR=%BACKEND%\venv"
if not exist "%VENV_DIR%\Scripts\python.exe" set "VENV_DIR=%BACKEND%\.venv"

set "PY=%VENV_DIR%\Scripts\python.exe"
if not exist "%PY%" (
  echo [ERRORE] Non trovo python nel venv:
  echo    %PY%
  echo Controlla che esista: %BACKEND%\venv oppure %BACKEND%\.venv
  exit /b 1
)

set "WHISPER_PORT=8001"
set "RAG_PORT=8002"
set "TTS_PORT=8004"
set "OLLAMA_PORT=11434"

REM Coqui XTTS v2 (se coqui_tts_server.py legge queste env)
set "TTS_HOME=%ROOT%models\coqui_tts"
set "COQUI_TTS_MODEL=tts_models/multilingual/multi-dataset/xtts_v2"
set "COQUI_LANG=it"
set "COQUI_DEFAULT_SPEAKER_WAV=%BACKEND%\voices\default.wav"

if "%~1"=="1" goto START
if "%~1"=="2" goto STOP
goto MENU

:MENU
echo.
echo ============================================
echo    SOULFRAME AI Services
echo ============================================
echo [1] Start servizi
echo [2] Stop  servizi (kill per porta)
echo.
set /p "CHOICE=> "
if "%CHOICE%"=="1" goto START
if "%CHOICE%"=="2" goto STOP
exit /b 0

:START
echo.
echo Avvio servizi...

call :ENSURE_DIRS

REM --- Ollama (solo se non in ascolto) ---
call :PORT_IS_LISTENING %OLLAMA_PORT%
if "!LISTENING!"=="1" (
  echo    Ollama gia' attivo su %OLLAMA_PORT%
) else (
  echo    Avvio Ollama...
  REM Avvio con cmd /c. Si chiudera' solo quando il processo sulla porta 11434 verra' killato.
  start "SOULFRAME_OLLAMA" /min cmd /c "ollama serve"
)

REM --- Whisper ---
call :PORT_IS_LISTENING %WHISPER_PORT%
if "!LISTENING!"=="1" (
  echo    Whisper gia' attivo su %WHISPER_PORT%
) else (
  start "SOULFRAME_WHISPER" /D "%BACKEND%" cmd /c ""%PY%" -m uvicorn whisper_server:app --host 127.0.0.1 --port %WHISPER_PORT% --log-level info"
)

REM --- RAG ---
call :PORT_IS_LISTENING %RAG_PORT%
if "!LISTENING!"=="1" (
  echo    RAG gia' attivo su %RAG_PORT%
) else (
  start "SOULFRAME_RAG" /D "%BACKEND%" cmd /c ""%PY%" -m uvicorn rag_server:app --host 127.0.0.1 --port %RAG_PORT% --log-level info"
)

REM --- Coqui TTS ---
call :PORT_IS_LISTENING %TTS_PORT%
if "!LISTENING!"=="1" (
  echo    TTS gia' attivo su %TTS_PORT%
) else (
  start "SOULFRAME_TTS" /D "%BACKEND%" cmd /c "set ""TTS_HOME=%TTS_HOME%"" && set ""COQUI_TTS_MODEL=%COQUI_TTS_MODEL%"" && set ""COQUI_LANG=%COQUI_LANG%"" && set ""COQUI_DEFAULT_SPEAKER_WAV=%COQUI_DEFAULT_SPEAKER_WAV%"" && ^"%PY%^" -m uvicorn coqui_tts_server:app --host 127.0.0.1 --port %TTS_PORT% --log-level info"
)

echo.
echo Servizi:
echo    Whisper: http://127.0.0.1:%WHISPER_PORT%/docs
echo    RAG:      http://127.0.0.1:%RAG_PORT%/docs
echo    TTS:      http://127.0.0.1:%TTS_PORT%/docs    (health: /health)
echo.
exit /b 0

:STOP
echo.
echo Stop servizi...
REM Kill Python services
call :KILL_PORT_PY %WHISPER_PORT%
call :KILL_PORT_PY %RAG_PORT%
call :KILL_PORT_PY %TTS_PORT%

REM Kill Ollama (speciale, non e' python)
call :KILL_PORT_FORCE %OLLAMA_PORT%

echo Fatto.
exit /b 0

REM ========================= HELPERS =========================

:ENSURE_DIRS
if not exist "%ROOT%models" mkdir "%ROOT%models" >nul 2>&1
if not exist "%TTS_HOME%" mkdir "%TTS_HOME%" >nul 2>&1
if not exist "%BACKEND%\voices" mkdir "%BACKEND%\voices" >nul 2>&1
exit /b 0

:PORT_IS_LISTENING
set "LISTENING=0"
for /f "tokens=5" %%p in ('netstat -ano ^| findstr /R /C:":%~1 .*LISTENING" /C:":%~1 .*IN ASCOLTO"') do (
  set "LISTENING=1"
)
exit /b 0

:KILL_PORT_PY
set "PORT=%~1"
for /f "tokens=5" %%p in ('netstat -ano ^| findstr /R /C:":%PORT% .*LISTENING" /C:":%PORT% .*IN ASCOLTO"') do (
  call :KILL_PID_IF_PY %%p %PORT%
)
exit /b 0

:KILL_PID_IF_PY
setlocal DisableDelayedExpansion
set "PID=%~1"
set "PORT=%~2"
set "PN="
for /f "tokens=1 delims=," %%n in ('tasklist /FI "PID eq %PID%" /FO CSV /NH') do set "PN=%%~n"

if /I "%PN%"=="python.exe" (
  echo    [KILL] porta %PORT% -> PID %PID% (python.exe)
  taskkill /PID %PID% /F /T >nul 2>&1
) else (
  echo    [SKIP] porta %PORT% -> PID %PID% (%PN% - non e python)
)
endlocal
exit /b 0

REM --- NUOVO HELPER PER OLLAMA ---
:KILL_PORT_FORCE
set "PORT=%~1"
for /f "tokens=5" %%p in ('netstat -ano ^| findstr /R /C:":%PORT% .*LISTENING" /C:":%PORT% .*IN ASCOLTO"') do (
  echo    [KILL] OLLAMA porta %PORT% -> PID %%p
  REM Forza la chiusura di qualsiasi processo su questa porta (Ollama)
  taskkill /PID %%p /F /T >nul 2>&1
)
exit /b 0