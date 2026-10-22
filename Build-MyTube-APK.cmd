@echo off
rem Double-click to rebuild the MyTube APK after you change the app or catalog.json.
rem Run Install-MyTube-Tools.cmd once first on a new PC.
cd /d "%~dp0"
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0build-android.ps1" -OpenFolder
echo.
pause
