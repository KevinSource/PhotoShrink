@echo off
rem Builds PhotoShrink, copies it to %LOCALAPPDATA%\Programs\PhotoShrink and adds the
rem right-click menu + Send To shortcuts for the current user (no admin needed).
cd /d "%~dp0"
set "DEST=%LOCALAPPDATA%\Programs\PhotoShrink"
echo Publishing to %DEST% ...
dotnet publish PhotoShrink\PhotoShrink.csproj -c Release -o "%DEST%" -nologo
if errorlevel 1 (
  echo Publish failed.
  pause
  exit /b 1
)
"%DEST%\PhotoShrink.exe" --install
