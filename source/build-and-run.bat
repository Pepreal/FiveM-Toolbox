@echo off
rem Builds FiveM Toolbox from the source and starts it. Double-click this file.
cd /d "%~dp0"
echo Building FiveM Toolbox from the source ...
powershell -NoProfile -ExecutionPolicy Bypass -File build.ps1
if errorlevel 1 (
  echo.
  echo The build failed. Copy the red lines above and open an issue on GitHub with them.
  pause
  exit /b 1
)
echo.
echo Build finished. Starting the program ...
start "" "%~dp0..\app\FiveMToolbox.exe"