@echo off
rem Builds PhotoShrink as a single PhotoShrink.exe (needs the .NET 10 Desktop Runtime), puts it in
rem %LOCALAPPDATA%\Programs\PhotoShrink and adds the right-click menu + Send To shortcuts for the
rem current user (no admin needed).
cd /d "%~dp0"
set "DEST=%LOCALAPPDATA%\Programs\PhotoShrink"
rem Start clean so files from an older multi-file install don't linger.
if exist "%DEST%" rmdir /s /q "%DEST%"
if exist "%DEST%" (
  echo Could not clear %DEST% - is PhotoShrink still running? Close it and try again.
  pause
  exit /b 1
)
echo Publishing to %DEST% ...
dotnet publish PhotoShrink\PhotoShrink.csproj -c Release -r win-x64 --self-contained false ^
  -p:PublishSingleFile=true -p:DebugType=embedded -o "%DEST%" -nologo
if errorlevel 1 (
  echo Publish failed.
  pause
  exit /b 1
)
echo Installed files:
dir /b "%DEST%"
"%DEST%\PhotoShrink.exe" --install
