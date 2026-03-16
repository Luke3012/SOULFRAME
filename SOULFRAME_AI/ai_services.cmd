@echo off
setlocal EnableExtensions EnableDelayedExpansion

REM ============================================================
REM SOULFRAME - AI Services Launcher
REM  Args:
REM   ai_services.cmd 1      -> start console mode
REM   ai_services.cmd 2      -> stop services
REM   ai_services.cmd 3      -> restart background mode
REM   ai_services.cmd debug  -> debug console mode
REM   ai_services.cmd        -> menu
REM ============================================================

set "ROOT=%~dp0"
set "BACKEND=%ROOT%backend"
set "BUILD_DIR=%SOULFRAME_WEBGL_BUILD_DIR%"
if not defined BUILD_DIR set "BUILD_DIR=%ROOT%..\Build"
set "WINDOWS_EXE=%SOULFRAME_WINDOWS_EXE%"
if not defined WINDOWS_EXE set "WINDOWS_EXE=%ROOT%..\Build_Windows64\SOULFRAME.exe"
set "FRONTEND_CONFIG=%ROOT%ai_services.mode.cfg"
set "FRONTEND_MODE=WEBGL"

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

set "AUDIO_FFMPEG_BIN=%SOULFRAME_FFMPEG_BIN%"
if not defined AUDIO_FFMPEG_BIN if exist "%LOCALAPPDATA%\Microsoft\WinGet\Packages\Gyan.FFmpeg.Shared_Microsoft.Winget.Source_8wekyb3d8bbwe\ffmpeg-8.0.1-full_build-shared\bin\ffmpeg.exe" set "AUDIO_FFMPEG_BIN=%LOCALAPPDATA%\Microsoft\WinGet\Packages\Gyan.FFmpeg.Shared_Microsoft.Winget.Source_8wekyb3d8bbwe\ffmpeg-8.0.1-full_build-shared\bin"
if not defined AUDIO_FFMPEG_BIN if exist "C:\ffmpeg\bin\ffmpeg.exe" set "AUDIO_FFMPEG_BIN=C:\ffmpeg\bin"
if defined AUDIO_FFMPEG_BIN set "PATH=%AUDIO_FFMPEG_BIN%;%PATH%"

set "WHISPER_PORT=8001"
set "RAG_PORT=8002"
set "TTS_PORT=8004"
set "AVATAR_ASSET_PORT=8003"
set "OLLAMA_PORT=11434"
set "BUILD_PORT=8000"

set "RAG_DIR=%BACKEND%\rag_store"
set "RAG_LOG_DIR=%BACKEND%\log"
set "RAG_OCR_LANG=ita+eng"
set "TESSDATA_PREFIX=C:\Program Files\Tesseract-OCR\tessdata"
set "OLLAMA_HOST=http://127.0.0.1:11434"
set "EMBED_MODEL=nomic-embed-text"
set "CHAT_MODEL=llama3:8b-instruct-q4_K_M"
set "CHAT_TEMPERATURE=0.45"
set "CHAT_TOP_P=0.9"
set "CHAT_REPEAT_PENALTY=1.08"
set "CHAT_NUM_PREDICT=280"
set "RAG_CHAT_TOP_K_CAP=8"
set "RAG_FACTUAL_SCORE_MIN=0.33"
set "RAG_FACTUAL_SCORE_GAP_MIN=0.08"
set "RAG_FACTUAL_MAX_CONTEXT_CHARS=3600"
set "RAG_SESSION_TURNS=8"
set "RAG_INTENT_ROUTER_NUM_PREDICT=32"
set "WHISPER_MODEL=small"

REM Coqui XTTS v2 (se coqui_tts_server.py legge queste env)
set "TTS_HOME=%ROOT%models\coqui_tts"
set "COQUI_TTS_MODEL=tts_models/multilingual/multi-dataset/xtts_v2"
set "COQUI_LANG=it"
set "COQUI_DEFAULT_SPEAKER_WAV=%BACKEND%\voices\default.wav"
set "COQUI_AVATAR_VOICES_DIR=%BACKEND%\voices\avatars"
set "COQUI_TTS_DEVICE=cuda"

call :LOAD_FRONTEND_CONFIG

if "%~1"=="1" goto CONSOLE_START
if "%~1"=="2" goto STOP
if "%~1"=="3" goto BACKGROUND_RESTART
if /I "%~1"=="debug" goto DEBUG_CONSOLE_START
goto MENU

:MENU
title SOULFRAME AI Services - Menu
echo.
echo ============================================
echo    SOULFRAME AI Services
echo ============================================
echo [1] Start servizi (console mode)
echo [2] Stop servizi
echo [3] Restart servizi (background mode)
echo [4] Debug console
echo [5] Configura frontend default ^(WebGL/Windows^)
echo.
set /p "CHOICE=> "
if "%CHOICE%"=="1" goto CONSOLE_START
if "%CHOICE%"=="2" goto STOP
if "%CHOICE%"=="3" goto BACKGROUND_RESTART
if "%CHOICE%"=="4" goto DEBUG_CONSOLE_START
if "%CHOICE%"=="5" goto CONFIG_FRONTEND
exit /b 0

:CONFIG_FRONTEND
call :CHOOSE_FRONTEND_MODE
goto MENU

:CONSOLE_START
title SOULFRAME AI Services - Console Mode
echo.
echo Avvio servizi in console mode...
call :CHOOSE_FRONTEND_MODE
set "CONSOLE_KIND=FULL"
set "SERVICE_MODE=CONSOLE"
call :RESET_HOME_SUMMARY
call :START_FULL_HIDDEN
call :OPEN_WEB_PAGE_DELAYED
goto CONSOLE_LOOP

:DEBUG_CONSOLE_START
title SOULFRAME AI Services - Debug Console
echo.
echo Avvio servizi AI in debug console...
set "CONSOLE_KIND=DEBUG"
set "SERVICE_MODE=CONSOLE"
call :RESET_HOME_SUMMARY
call :START_DEBUG_HIDDEN
goto CONSOLE_LOOP

:BACKGROUND_RESTART
title SOULFRAME AI Services - Background Mode
echo.
echo Restart servizi in background mode...
call :CHOOSE_FRONTEND_MODE
set "SERVICE_MODE=BACKGROUND"
set "BACKGROUND_RESTARTING=1"
set "CONSOLE_KIND="
set "INTERNAL_CLOSE="
call :RESET_HOME_SUMMARY
call :STOP
call :START_FULL_SEPARATE
set "BACKGROUND_RESTARTING="
exit /b 0

:CONSOLE_LOOP
call :DRAW_CONSOLE_SCREEN
:CONSOLE_PROMPT
set "CONSOLE_CMD="
set /p "CONSOLE_CMD=SOULFRAME services> "
if not defined CONSOLE_CMD goto CONSOLE_LOOP

if /I "%CONSOLE_CMD%"=="c" goto CONSOLE_CLOSE
if /I "%CONSOLE_CMD%"=="close" goto CONSOLE_CLOSE
if /I "%CONSOLE_CMD%"=="h" goto CONSOLE_HELP
if /I "%CONSOLE_CMD%"=="help" goto CONSOLE_HELP
if /I "%CONSOLE_CMD%"=="status" goto CONSOLE_STATUS
if /I "%CONSOLE_CMD%"=="r" goto CONSOLE_REBOOT
if /I "%CONSOLE_CMD%"=="reboot" goto CONSOLE_REBOOT
if /I "%CONSOLE_CMD%"=="s" goto CONSOLE_START_PAGE
if /I "%CONSOLE_CMD%"=="start" goto CONSOLE_START_PAGE

echo [WARN] Comando non riconosciuto: %CONSOLE_CMD%
goto CONSOLE_REFRESH_PAUSE

:CONSOLE_HELP
echo.
echo close  - chiude tutti i servizi ed esce
echo reboot - riavvia i servizi nella console corrente
echo status - mostra lo stato porte principali
if /I "%CONSOLE_KIND%"=="FULL" (
  if /I "%FRONTEND_MODE%"=="WEBGL" (
    echo start  - riapre la pagina WebGL nel browser
  ) else (
    echo start  - rilancia l'app Windows
  )
)
goto CONSOLE_REFRESH_PAUSE

:CONSOLE_STATUS
echo.
call :SHOW_STATUS
goto CONSOLE_REFRESH_PAUSE

:CONSOLE_START_PAGE
if /I not "%CONSOLE_KIND%"=="FULL" (
  echo [WARN] La pagina web non e' disponibile in debug console.
  goto CONSOLE_REFRESH
)

if /I "%FRONTEND_MODE%"=="WEBGL" (
  set "URL=http://127.0.0.1:%BUILD_PORT%"
  start "" "%URL%"
) else (
  call :RESOLVE_WINDOWS_EXE
  if errorlevel 1 goto CONSOLE_REFRESH
  start "SOULFRAME_WINDOWS" "%RESOLVED_WINDOWS_EXE%"
)
goto CONSOLE_REFRESH

:CONSOLE_REBOOT
set "PREV_CONSOLE_KIND=%CONSOLE_KIND%"
set "INTERNAL_CLOSE="
echo.
if /I "%PREV_CONSOLE_KIND%"=="DEBUG" (
  echo Riavvio servizi AI in debug console...
) else (
  echo Riavvio servizi in console mode...
)
call :RESET_HOME_SUMMARY
call :STOP
set "SERVICE_MODE=CONSOLE"
set "CONSOLE_KIND=%PREV_CONSOLE_KIND%"
if /I "%CONSOLE_KIND%"=="DEBUG" (
  call :START_DEBUG_HIDDEN
) else (
  call :START_FULL_HIDDEN
  call :OPEN_WEB_PAGE_DELAYED
)
goto CONSOLE_REFRESH

:CONSOLE_REFRESH
goto CONSOLE_LOOP

:CONSOLE_REFRESH_PAUSE
echo.
pause
goto CONSOLE_LOOP

:CONSOLE_CLOSE
set "INTERNAL_CLOSE=1"
set "CONSOLE_KIND="
goto STOP

:START_FULL_HIDDEN
call :ENSURE_DIRS
call :START_OLLAMA_HIDDEN
call :START_WHISPER_HIDDEN
call :START_RAG_HIDDEN
call :START_TTS_HIDDEN
call :START_AVATAR_HIDDEN
call :START_FRONTEND_HIDDEN
call :PRINT_FULL_URLS
exit /b 0

:START_DEBUG_HIDDEN
call :ENSURE_DIRS
call :START_OLLAMA_HIDDEN
call :START_RAG_HIDDEN
echo.
echo Servizi debug:
echo    RAG:      http://127.0.0.1:%RAG_PORT%/docs
echo.
exit /b 0

:START_FULL_SEPARATE
call :ENSURE_DIRS
call :START_OLLAMA_WINDOW
call :START_WHISPER_WINDOW
call :START_RAG_WINDOW
call :START_TTS_WINDOW
call :START_AVATAR_WINDOW
call :START_FRONTEND_WINDOW
call :PRINT_FULL_URLS
call :OPEN_WEB_PAGE_DELAYED
exit /b 0

:START_FRONTEND_HIDDEN
if /I "%FRONTEND_MODE%"=="WINDOWS" (
  call :START_WINDOWS_APP
) else (
  call :START_BUILD_HIDDEN
)
exit /b 0

:START_FRONTEND_WINDOW
if /I "%FRONTEND_MODE%"=="WINDOWS" (
  call :START_WINDOWS_APP
) else (
  call :START_BUILD_WINDOW
)
exit /b 0

:PRINT_FULL_URLS
echo.
echo Servizi:
echo    Whisper: http://127.0.0.1:%WHISPER_PORT%/docs
echo    RAG:      http://127.0.0.1:%RAG_PORT%/docs
echo    TTS:      http://127.0.0.1:%TTS_PORT%/docs    (health: /health)
echo    Avatar:   http://127.0.0.1:%AVATAR_ASSET_PORT%/docs
if /I "%FRONTEND_MODE%"=="WEBGL" (
  echo    Frontend: WebGL su http://127.0.0.1:%BUILD_PORT%
) else (
  echo    Frontend: Windows app ^(%WINDOWS_EXE%^)
)
echo.
set "HOME_SERVICES_URLS=1"
exit /b 0

:SHOW_STATUS
call :SHOW_PORT_STATUS "Build" %BUILD_PORT%
call :SHOW_PORT_STATUS "Whisper" %WHISPER_PORT%
call :SHOW_PORT_STATUS "RAG" %RAG_PORT%
call :SHOW_PORT_STATUS "Avatar" %AVATAR_ASSET_PORT%
call :SHOW_PORT_STATUS "TTS" %TTS_PORT%
call :SHOW_PORT_STATUS "Ollama" %OLLAMA_PORT%
exit /b 0

:SHOW_PORT_STATUS
call :PORT_IS_LISTENING %~2
if "!LISTENING!"=="1" (
  echo [OK] %~1 su porta %~2
) else (
  echo [--] %~1 non in ascolto su %~2
)
exit /b 0

:OPEN_WEB_PAGE
set "URL=http://127.0.0.1:%BUILD_PORT%"
start "" "%URL%"
exit /b 0

:OPEN_WEB_PAGE_DELAYED
if /I not "%FRONTEND_MODE%"=="WEBGL" exit /b 0
set "URL=http://127.0.0.1:%BUILD_PORT%"
powershell -NoProfile -ExecutionPolicy Bypass -Command "Start-Sleep -Seconds 2; Start-Process $env:URL | Out-Null"
exit /b 0

:LOAD_FRONTEND_CONFIG
if exist "%FRONTEND_CONFIG%" (
  for /f "usebackq tokens=1,* delims==" %%A in ("%FRONTEND_CONFIG%") do (
    if /I "%%~A"=="FRONTEND_MODE" set "FRONTEND_MODE=%%~B"
  )
)
if /I not "%FRONTEND_MODE%"=="WEBGL" if /I not "%FRONTEND_MODE%"=="WINDOWS" set "FRONTEND_MODE=WEBGL"
exit /b 0

:SAVE_FRONTEND_CONFIG
(
  echo FRONTEND_MODE=%FRONTEND_MODE%
) > "%FRONTEND_CONFIG%"
exit /b 0

:CHOOSE_FRONTEND_MODE
call :LOAD_FRONTEND_CONFIG
echo.
echo Configurazione frontend corrente: %FRONTEND_MODE%
echo [1] WebGL ^(Build + browser su localhost:%BUILD_PORT%^)
echo [2] Windows ^(SOULFRAME.exe^)
set "FRONTEND_CHOICE="
set /p "FRONTEND_CHOICE=Seleziona frontend [1/2, Invio=%FRONTEND_MODE%]: "
if "%FRONTEND_CHOICE%"=="" goto CHOOSE_FRONTEND_SAVE
if "%FRONTEND_CHOICE%"=="1" (
  set "FRONTEND_MODE=WEBGL"
  goto CHOOSE_FRONTEND_SAVE
)
if "%FRONTEND_CHOICE%"=="2" (
  set "FRONTEND_MODE=WINDOWS"
  goto CHOOSE_FRONTEND_SAVE
)
echo [WARN] Scelta non valida, mantengo: %FRONTEND_MODE%

:CHOOSE_FRONTEND_SAVE
call :SAVE_FRONTEND_CONFIG
echo [OK] Frontend impostato su: %FRONTEND_MODE%
exit /b 0

:STOP
title SOULFRAME AI Services - Stop
echo.
echo Stop servizi...
for %%P in (%WHISPER_PORT% %RAG_PORT% %TTS_PORT% %AVATAR_ASSET_PORT% %BUILD_PORT%) do (
  for /f "tokens=5" %%p in ('netstat -ano ^| findstr /R /C:":%%P .*LISTENING" /C:":%%P .*IN ASCOLTO"') do (
    set "STOP_PID=%%p"
    set "STOP_PN="
    for /f "tokens=1 delims=," %%n in ('tasklist /FI "PID eq %%p" /FO CSV /NH') do set "STOP_PN=%%~n"
    if /I "!STOP_PN!"=="python.exe" (
      echo    [KILL] porta %%P -^> PID !STOP_PID! ^(python.exe^)
      taskkill /PID !STOP_PID! /F /T >nul 2>&1
    ) else (
      echo    [SKIP] porta %%P -^> PID !STOP_PID! ^(!STOP_PN! - non e python^)
    )
  )
)

taskkill /FI "WINDOWTITLE eq WebGL Python Server*" /F /T >nul 2>&1
taskkill /FI "WINDOWTITLE eq SOULFRAME_BUILD*" /F /T >nul 2>&1
taskkill /FI "WINDOWTITLE eq SOULFRAME_WHISPER*" /F /T >nul 2>&1
taskkill /FI "WINDOWTITLE eq SOULFRAME_RAG*" /F /T >nul 2>&1
taskkill /FI "WINDOWTITLE eq SOULFRAME_TTS*" /F /T >nul 2>&1
taskkill /FI "WINDOWTITLE eq SOULFRAME_AVATAR_ASSET*" /F /T >nul 2>&1
taskkill /FI "WINDOWTITLE eq SOULFRAME_OLLAMA*" /F /T >nul 2>&1

for /f "tokens=5" %%p in ('netstat -ano ^| findstr /R /C:":%OLLAMA_PORT% .*LISTENING" /C:":%OLLAMA_PORT% .*IN ASCOLTO"') do (
  echo    [KILL] OLLAMA porta %OLLAMA_PORT% -^> PID %%p
  taskkill /PID %%p /F /T >nul 2>&1
)

if not defined CONSOLE_KIND (
  if not defined BACKGROUND_RESTARTING (
    taskkill /FI "WINDOWTITLE eq SOULFRAME AI Services*" /F /T >nul 2>&1
    taskkill /FI "WINDOWTITLE eq SOULFRAME AI Services - Menu*" /F /T >nul 2>&1
    taskkill /FI "WINDOWTITLE eq SOULFRAME AI Services - Console Mode*" /F /T >nul 2>&1
    taskkill /FI "WINDOWTITLE eq SOULFRAME AI Services - Debug Console*" /F /T >nul 2>&1
    taskkill /FI "WINDOWTITLE eq SOULFRAME AI Services - Background Mode*" /F /T >nul 2>&1
    call :CLOSE_EXTERNAL_AI_SERVICE_CONSOLES
  )
)

echo Fatto.
if defined INTERNAL_CLOSE exit /b 0
exit /b 0

REM ========================= HELPERS =========================

:DRAW_CONSOLE_SCREEN
cls
echo ------------------------------------------------------------
echo  Console Mode attiva
echo ------------------------------------------------------------
if /I "%CONSOLE_KIND%"=="FULL" (
  echo  Comandi: s=start frontend, reboot, status, help, close
) else (
  echo  Comandi: status, reboot, help, close
)
echo ------------------------------------------------------------
if defined HOME_SUMMARY_PENDING (
  if defined HOME_MSG_OLLAMA echo !HOME_MSG_OLLAMA!
  if defined HOME_MSG_WHISPER echo !HOME_MSG_WHISPER!
  if defined HOME_MSG_RAG echo !HOME_MSG_RAG!
  if defined HOME_MSG_TTS echo !HOME_MSG_TTS!
  if defined HOME_MSG_AVATAR echo !HOME_MSG_AVATAR!
  if defined HOME_MSG_BUILD_1 echo.
  if defined HOME_MSG_BUILD_1 echo !HOME_MSG_BUILD_1!
  if defined HOME_MSG_BUILD_2 echo !HOME_MSG_BUILD_2!
  if defined HOME_SERVICES_URLS (
    echo Servizi:
    echo    Whisper: http://127.0.0.1:%WHISPER_PORT%/docs
    echo    RAG:      http://127.0.0.1:%RAG_PORT%/docs
    echo    TTS:      http://127.0.0.1:%TTS_PORT%/docs    ^(health: /health^)
    echo    Avatar:   http://127.0.0.1:%AVATAR_ASSET_PORT%/docs
  )
  echo ------------------------------------------------------------
  call :RESET_HOME_SUMMARY
)
exit /b 0

:ENSURE_DIRS
if not exist "%ROOT%models" mkdir "%ROOT%models" >nul 2>&1
if not exist "%TTS_HOME%" mkdir "%TTS_HOME%" >nul 2>&1
if not exist "%BACKEND%\voices" mkdir "%BACKEND%\voices" >nul 2>&1
if not exist "%BACKEND%\log" mkdir "%BACKEND%\log" >nul 2>&1
exit /b 0

:PORT_IS_LISTENING
set "LISTENING=0"
for /f "tokens=5" %%p in ('netstat -ano ^| findstr /R /C:":%~1 .*LISTENING" /C:":%~1 .*IN ASCOLTO"') do (
  set "LISTENING=1"
)
exit /b 0

:PROCESS_PATTERN_IS_RUNNING
set "PROCESS_RUNNING=0"
exit /b 0

:RESET_HOME_SUMMARY
set "HOME_SUMMARY_PENDING=1"
set "HOME_MSG_OLLAMA="
set "HOME_MSG_WHISPER="
set "HOME_MSG_RAG="
set "HOME_MSG_TTS="
set "HOME_MSG_AVATAR="
set "HOME_MSG_BUILD_1="
set "HOME_MSG_BUILD_2="
set "HOME_SERVICES_URLS="
exit /b 0

:CLOSE_EXTERNAL_AI_SERVICE_CONSOLES
for /f %%I in ('powershell -NoProfile -ExecutionPolicy Bypass -Command "$targets = Get-CimInstance Win32_Process -ErrorAction SilentlyContinue | Where-Object { $_.Name -eq 'cmd.exe' -and $_.CommandLine -match 'ai_services\\.cmd' }; foreach ($p in $targets) { $p.ProcessId }"') do (
  taskkill /PID %%I /F /T >nul 2>&1
)
exit /b 0

:START_HIDDEN_PROCESS
set "SP_WORKDIR=%~1"
set "SP_EXE=%~2"
set "SP_ARGS=%~3"
powershell -NoProfile -ExecutionPolicy Bypass -Command "$wd = [System.IO.Path]::GetFullPath($env:SP_WORKDIR); $exe = $env:SP_EXE; $args = $env:SP_ARGS; Start-Process -FilePath $exe -ArgumentList $args -WorkingDirectory $wd -WindowStyle Hidden | Out-Null"
exit /b 0

:START_WINDOW_PROCESS
start "%~1" /min /D "%~2" cmd /c ""%~3" %~4"
exit /b 0

:START_OLLAMA_HIDDEN
call :PORT_IS_LISTENING %OLLAMA_PORT%
if "!LISTENING!"=="1" (
  echo    Ollama gia' attivo su %OLLAMA_PORT%
  set "HOME_MSG_OLLAMA=   Ollama gia' attivo su %OLLAMA_PORT%"
) else (
  echo    Avvio Ollama...
  set "HOME_MSG_OLLAMA=   Avvio Ollama..."
  call :START_HIDDEN_PROCESS "%ROOT%" "ollama.exe" "serve"
)
exit /b 0

:START_OLLAMA_WINDOW
call :PORT_IS_LISTENING %OLLAMA_PORT%
if "!LISTENING!"=="1" (
  echo    Ollama gia' attivo su %OLLAMA_PORT%
) else (
  echo    Avvio Ollama...
  start "SOULFRAME_OLLAMA" /min cmd /c "ollama serve"
)
exit /b 0

:START_WHISPER_HIDDEN
call :PORT_IS_LISTENING %WHISPER_PORT%
if "!LISTENING!"=="1" (
  echo    Whisper gia' attivo su %WHISPER_PORT%
  set "HOME_MSG_WHISPER=   Whisper gia' attivo su %WHISPER_PORT%"
) else (
  echo    Avvio Whisper...
  set "HOME_MSG_WHISPER=   Avvio Whisper..."
  call :START_HIDDEN_PROCESS "%BACKEND%" "%PY%" "-m uvicorn whisper_server:app --host 127.0.0.1 --port %WHISPER_PORT% --log-level info --no-use-colors"
)
exit /b 0

:START_WHISPER_WINDOW
if /I not "%SERVICE_MODE%"=="BACKGROUND" exit /b 0
call :PORT_IS_LISTENING %WHISPER_PORT%
if "!LISTENING!"=="1" (
  echo    Whisper gia' attivo su %WHISPER_PORT%
) else (
  call :START_WINDOW_PROCESS "SOULFRAME_WHISPER" "%BACKEND%" "%PY%" "-m uvicorn whisper_server:app --host 127.0.0.1 --port %WHISPER_PORT% --log-level info --no-use-colors"
)
exit /b 0

:START_RAG_HIDDEN
call :PORT_IS_LISTENING %RAG_PORT%
if "!LISTENING!"=="1" (
  echo    RAG gia' attivo su %RAG_PORT%
  set "HOME_MSG_RAG=   RAG gia' attivo su %RAG_PORT%"
) else (
  echo    Avvio RAG...
  set "HOME_MSG_RAG=   Avvio RAG..."
  call :START_HIDDEN_PROCESS "%BACKEND%" "%PY%" "-m uvicorn rag_server:app --host 127.0.0.1 --port %RAG_PORT% --log-level info --no-use-colors"
)
exit /b 0

:START_RAG_WINDOW
if /I not "%SERVICE_MODE%"=="BACKGROUND" exit /b 0
call :PORT_IS_LISTENING %RAG_PORT%
if "!LISTENING!"=="1" (
  echo    RAG gia' attivo su %RAG_PORT%
) else (
  call :START_WINDOW_PROCESS "SOULFRAME_RAG" "%BACKEND%" "%PY%" "-m uvicorn rag_server:app --host 127.0.0.1 --port %RAG_PORT% --log-level info --no-use-colors"
)
exit /b 0

:START_TTS_HIDDEN
call :PORT_IS_LISTENING %TTS_PORT%
if "!LISTENING!"=="1" (
  echo    TTS gia' attivo su %TTS_PORT%
  set "HOME_MSG_TTS=   TTS gia' attivo su %TTS_PORT%"
) else (
  echo    Avvio TTS...
  set "HOME_MSG_TTS=   Avvio TTS..."
  call :START_HIDDEN_PROCESS "%BACKEND%" "%PY%" "-m uvicorn coqui_tts_server:app --host 127.0.0.1 --port %TTS_PORT% --log-level info --no-use-colors"
)
exit /b 0

:START_TTS_WINDOW
if /I not "%SERVICE_MODE%"=="BACKGROUND" exit /b 0
call :PORT_IS_LISTENING %TTS_PORT%
if "!LISTENING!"=="1" (
  echo    TTS gia' attivo su %TTS_PORT%
) else (
  call :START_WINDOW_PROCESS "SOULFRAME_TTS" "%BACKEND%" "%PY%" "-m uvicorn coqui_tts_server:app --host 127.0.0.1 --port %TTS_PORT% --log-level info --no-use-colors"
)
exit /b 0

:START_AVATAR_HIDDEN
call :PORT_IS_LISTENING %AVATAR_ASSET_PORT%
if "!LISTENING!"=="1" (
  echo    Avatar Asset Server gia' attivo su %AVATAR_ASSET_PORT%
  set "HOME_MSG_AVATAR=   Avatar Asset Server gia' attivo su %AVATAR_ASSET_PORT%"
) else (
  echo    Avvio Avatar Asset Server...
  set "HOME_MSG_AVATAR=   Avvio Avatar Asset Server..."
  call :START_HIDDEN_PROCESS "%BACKEND%" "%PY%" "-m uvicorn avatar_asset_server:app --host 127.0.0.1 --port %AVATAR_ASSET_PORT% --log-level info --no-use-colors"
)
exit /b 0

:START_AVATAR_WINDOW
if /I not "%SERVICE_MODE%"=="BACKGROUND" exit /b 0
call :PORT_IS_LISTENING %AVATAR_ASSET_PORT%
if "!LISTENING!"=="1" (
  echo    Avatar Asset Server gia' attivo su %AVATAR_ASSET_PORT%
) else (
  call :START_WINDOW_PROCESS "SOULFRAME_AVATAR_ASSET" "%BACKEND%" "%PY%" "-m uvicorn avatar_asset_server:app --host 127.0.0.1 --port %AVATAR_ASSET_PORT% --log-level info --no-use-colors"
)
exit /b 0

:START_BUILD_HIDDEN
echo.
echo Avvio Build Server...
set "HOME_MSG_BUILD_1=   Avvio Build Server..."
call :RESOLVE_BUILD_DIR
if errorlevel 1 exit /b 1
call :PORT_IS_LISTENING %BUILD_PORT%
if "!LISTENING!"=="1" (
  echo    Build server gia' attivo su %BUILD_PORT%
  set "HOME_MSG_BUILD_2=   Build server gia' attivo su %BUILD_PORT%"
) else (
  set "HOME_MSG_BUILD_2=   Build dir: %RESOLVED_BUILD_DIR%"
  call :START_HIDDEN_PROCESS "%RESOLVED_BUILD_DIR%" "%PY%" "-m http.server %BUILD_PORT% --bind 127.0.0.1"
  ping 127.0.0.1 -n 2 >nul
)
exit /b 0

:START_BUILD_WINDOW
if /I not "%SERVICE_MODE%"=="BACKGROUND" exit /b 0
echo.
echo Avvio Build Server...
call :RESOLVE_BUILD_DIR
if errorlevel 1 exit /b 1
call :PORT_IS_LISTENING %BUILD_PORT%
if "!LISTENING!"=="1" (
  echo    Build server gia' attivo su %BUILD_PORT%
) else (
  call :START_WINDOW_PROCESS "SOULFRAME_BUILD" "%RESOLVED_BUILD_DIR%" "%PY%" "-m http.server %BUILD_PORT% --bind 127.0.0.1"
  ping 127.0.0.1 -n 2 >nul
)
exit /b 0

:START_WINDOWS_APP
echo.
echo Avvio app Windows...
set "HOME_MSG_BUILD_1=   Avvio app Windows..."
call :RESOLVE_WINDOWS_EXE
if errorlevel 1 exit /b 1
set "HOME_MSG_BUILD_2=   Exe: %RESOLVED_WINDOWS_EXE%"
start "SOULFRAME_WINDOWS" "%RESOLVED_WINDOWS_EXE%"
exit /b 0

:RESOLVE_BUILD_DIR
set "BUILD_DIR_ENV=%BUILD_DIR%"
set "RESOLVED_BUILD_DIR="
if not "%BUILD_DIR_ENV%"=="" set "RESOLVED_BUILD_DIR=%BUILD_DIR_ENV%"
if "%RESOLVED_BUILD_DIR%"=="" set "RESOLVED_BUILD_DIR=%ROOT%..\Build"
if not exist "%RESOLVED_BUILD_DIR%" (
  echo [ERRORE] Build directory non trovata: %RESOLVED_BUILD_DIR%
  exit /b 1
)
echo    Build dir: %RESOLVED_BUILD_DIR%
exit /b 0

:RESOLVE_WINDOWS_EXE
set "RESOLVED_WINDOWS_EXE=%WINDOWS_EXE%"
if "%RESOLVED_WINDOWS_EXE%"=="" set "RESOLVED_WINDOWS_EXE=%ROOT%..\Build_Windows64\SOULFRAME.exe"
if not exist "%RESOLVED_WINDOWS_EXE%" (
  echo [ERRORE] Windows executable non trovato: %RESOLVED_WINDOWS_EXE%
  exit /b 1
)
echo    Exe: %RESOLVED_WINDOWS_EXE%
exit /b 0
