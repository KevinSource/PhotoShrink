@echo off
rem Builds the solution and runs the end-to-end tests. Nothing is registered.
cd /d "%~dp0"
if exist test.status del test.status
dotnet build PhotoShrink.sln -c Release -nologo > build.log 2>&1
if errorlevel 1 (
  type build.log
  echo BUILD FAILED
  echo build-failed> test.status
  exit /b 1
)
dotnet PhotoShrink.TestKit\bin\Release\net10.0-windows\PhotoShrink.TestKit.dll PhotoShrink\bin\Release\net10.0-windows\PhotoShrink.exe testrun
> test.status echo tests-exit-%ERRORLEVEL%
