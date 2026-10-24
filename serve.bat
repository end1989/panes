@echo off
title Panes - Server
cls
cd /d "%~dp0"

echo.
echo  Panes - local server mode
echo  -------------------------
echo  Serving on http://localhost:8080
echo  (this mode enables the YouTube keyboard shortcuts)
echo.
echo  Ctrl+C to stop the server when done.
echo.

REM Launch Chrome after a brief delay so the server is ready
start "" /b cmd /c "timeout /t 1 /nobreak >nul & start chrome.exe --app=http://localhost:8080/index.html --window-size=1920,1080"

REM Try Python launchers in order
py -m http.server 8080 2>nul
if not errorlevel 1 goto :eof
python -m http.server 8080 2>nul
if not errorlevel 1 goto :eof
python3 -m http.server 8080 2>nul
if not errorlevel 1 goto :eof

echo.
echo  Could not start a Python HTTP server.
echo  Install Python 3 from python.org, or use launch.bat
echo  (videos will still play, but keyboard shortcuts will not).
echo.
pause
