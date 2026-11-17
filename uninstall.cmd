@echo off
rem Removes the right-click menu, the Send To shortcuts and the installed program folder.
rem Your JPEG-quality setting in %APPDATA%\PhotoShrink is left alone.
set "DEST=%LOCALAPPDATA%\Programs\PhotoShrink"
if not exist "%DEST%\PhotoShrink.exe" (
  echo PhotoShrink is not installed in %DEST%.
  pause
  exit /b 1
)
"%DEST%\PhotoShrink.exe" --uninstall
if errorlevel 1 exit /b 1
rmdir /s /q "%DEST%"
