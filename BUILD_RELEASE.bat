@echo off
setlocal
cd /d "%~dp0"
set /p VPB6_VERSION=<"%~dp0VERSION.txt"
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\Publish-Release.ps1"
if errorlevel 1 (
  echo.
  echo VPB 6 release build failed.
  pause
  exit /b 1
)
if exist "%ProgramFiles(x86)%\Inno Setup 6\ISCC.exe" (
  echo.
  echo Building VPB 6 installer...
  "%ProgramFiles(x86)%\Inno Setup 6\ISCC.exe" "%~dp0installer\VPB6.iss"
  if errorlevel 1 (
    echo.
    echo Installer build failed.
    pause
    exit /b 1
  )
  echo.
  echo ========================================
  echo VPB 6 installer is ready.
  echo release\VirtualPianoBot-%VPB6_VERSION%-Setup.exe
  echo release\VirtualPianoBot-%VPB6_VERSION%-win-x64.zip
  echo release\update-manifest.json
  if exist "release\VirtualPianoBot-Update.zip" echo release\VirtualPianoBot-Update.zip
  echo ========================================
) else (
  echo.
  echo Clean app payload created in release\app
  echo Update package and update-manifest.json are created in release.
  echo Install Inno Setup 6 to create the Setup.exe.
)
exit /b 0
