@echo off
title Panes
cls

echo.
echo   ____
echo  ^|  _ \ __ _ _ __   ___  ___
echo  ^| ^|_) / _` ^| '_ \ / _ \/ __^|
echo  ^|  __/ (_^| ^| ^| ^| ^|  __/\__ \
echo  ^|_^|   \__,_^|_^| ^|_^|\___^|^|___/
echo.
echo  Multi-stream viewer. Zero install.
echo.
echo  -----------------------------------
echo.
echo  [1] Standard (then press F11)
echo  [2] Chrome App Mode
echo  [3] Chrome Kiosk Mode (Alt+F4 to exit)
echo.

set /p choice="  Choose (1-3): "

cd /d "%~dp0"

if "%choice%"=="1" (
    echo.
    echo  Opening... press F11 for fullscreen
    start index.html
)

if "%choice%"=="2" (
    echo.
    echo  Opening in App Mode...
    start chrome.exe --app="file:///%cd%/index.html" --window-size=1920,1080
)

if "%choice%"=="3" (
    echo.
    echo  Opening in Kiosk Mode...
    timeout /t 2 >nul
    start chrome.exe --kiosk "file:///%cd%/index.html"
)

echo.
pause
