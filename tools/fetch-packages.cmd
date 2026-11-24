@echo off
rem RSMF Inspector: one-time download of NuGet packages into ..\nuget-offline
rem It only runs "dotnet restore" on tools\fetch\Fetch.csproj. Nothing is installed.
setlocal
cd /d "%~dp0"
set LOG=%~dp0fetch-packages.log
echo dotnet version: > "%LOG%"
dotnet --version >> "%LOG%" 2>&1
if errorlevel 1 (
  echo The .NET SDK was not found on PATH.
  echo RESULT: FAILED no dotnet >> "%LOG%"
  pause
  exit /b 1
)
echo Downloading packages, this can take a minute...
dotnet restore "%~dp0fetch\Fetch.csproj" --packages "%~dp0..\nuget-offline" >> "%LOG%" 2>&1
if errorlevel 1 (
  echo RESULT: FAILED >> "%LOG%"
  echo Restore FAILED. See tools\fetch-packages.log
) else (
  echo RESULT: OK >> "%LOG%"
  echo Done. Packages are in Documents\RsmfInspector\nuget-offline
)
pause
