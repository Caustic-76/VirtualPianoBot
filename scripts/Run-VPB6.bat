@echo off
setlocal
cd /d "%~dp0.."
where dotnet >nul 2>&1
if errorlevel 1 (
  echo .NET 10 SDK was not found.
  echo Install the .NET 10 SDK from https://dotnet.microsoft.com/download/dotnet/10.0 and run this file again.
  pause
  exit /b 1
)
dotnet run --project "src\VirtualPianoBot\VirtualPianoBot.csproj"
if errorlevel 1 (
  echo.
  echo VPB 6 did not launch. See the error above, then check %%APPDATA%%\VirtualPianoBot\logs.
  pause
  exit /b 1
)
endlocal
