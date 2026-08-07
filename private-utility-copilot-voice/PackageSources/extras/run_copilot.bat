@echo off
setlocal
cd /d "%~dp0"
echo Private Voice Co-Pilot Host
echo ===========================
if not exist "CoPilotVoiceHost.exe" (
  echo ERROR: CoPilotVoiceHost.exe not found in %CD%
  echo Publish the .NET host into this extras folder first.
  pause
  exit /b 1
)
start "" "CoPilotVoiceHost.exe"
endlocal
