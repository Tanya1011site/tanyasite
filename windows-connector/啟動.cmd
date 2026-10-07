@echo off
cd /d "%~dp0"
echo Starting MEGA quote connector...
echo.
if not exist "%~dp0Start.ps1" (
  echo ERROR: Start.ps1 is missing. Extract the whole ZIP first.
  pause
  exit /b 1
)
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Start.ps1"
if errorlevel 1 (
  echo.
  echo Startup failed. Please send a screenshot of this window.
) else (
  echo.
  echo Startup command completed. Check for the connector window.
)
pause
