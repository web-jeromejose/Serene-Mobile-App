@echo off
setlocal EnableExtensions

set "VIDEO_DIR=%~dp0MyTube\Resources\Raw\videos"

where ffmpeg >nul 2>&1
if errorlevel 1 (
    echo ERROR: ffmpeg was not found in PATH.
    pause
    exit /b 1
)

if not exist "%VIDEO_DIR%" (
    echo ERROR: Video folder not found:
    echo %VIDEO_DIR%
    pause
    exit /b 1
)

set "FOUND=0"

for %%F in ("%VIDEO_DIR%\*.mp4") do (
    if exist "%%~fF" (
        set "FOUND=1"
        echo Converting: %%~nxF

        ffmpeg -hide_banner -y -i "%%~fF" ^
          -vf "scale='min(1280,iw)':-2:force_original_aspect_ratio=decrease,format=yuv420p" ^
          -c:v libx264 -profile:v main -level 4.0 -preset fast -crf 23 ^
          -c:a aac -b:a 128k -movflags +faststart ^
          "%%~dpnF.__compatible.mp4"

        if errorlevel 1 (
            echo FAILED: %%~nxF
            if exist "%%~dpnF.__compatible.mp4" del /q "%%~dpnF.__compatible.mp4"
        ) else if not exist "%%~dpnF.__compatible.mp4" (
            echo FAILED: output was not created for %%~nxF
        ) else (
            move /y "%%~dpnF.__compatible.mp4" "%%~fF" >nul
            echo Replaced with Redmi-compatible version: %%~nxF
        )
    )
)

if "%FOUND%"=="0" echo No MP4 files found in:
if "%FOUND%"=="0" echo %VIDEO_DIR%
if not "%FOUND%"=="0" echo All MP4 files processed.

pause
endlocal
