@echo off
rem Double-click to install everything needed to build MyTube, then build the APK.
rem No admin rights needed. See README.md > "Set up a new PC".
cd /d "%~dp0"
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0setup-toolchain.ps1" -BuildApk
echo.
pause
