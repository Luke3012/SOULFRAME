@echo off
setlocal EnableExtensions

set "SCRIPT_DIR=%~dp0"
for %%I in ("%SCRIPT_DIR%..") do set "REPO_ROOT=%%~fI"

set "AI_DIR=%REPO_ROOT%\SOULFRAME_AI"
set "BACKEND_DIR=%AI_DIR%\backend"
set "DEFAULT_VENV=%BACKEND_DIR%\venv"
set "REQUIREMENTS=%BACKEND_DIR%\requirements.txt"
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

echo Installo dipendenze da %REQUIREMENTS%
"%VENV_PIP%" install -r "%REQUIREMENTS%"

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
echo Setup completato.
echo Per avviare i servizi:
echo   cd /d "%AI_DIR%"
echo   ai_services.cmd 1
echo.
echo Per attivare il venv:
echo   %VENV_PATH%\Scripts\activate.bat

endlocal
