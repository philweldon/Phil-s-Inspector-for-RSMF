@echo off
rem RSMF Inspector build runner.
rem Waits for a request file written into buildlogs\ and runs ONE of these fixed commands
rem   test    : dotnet test   tests\Rsmf.Tests            (log: buildlogs\test.log)
rem   app     : dotnet build  src\Rsmf.App  (win-x64)      (log: buildlogs\app.log)
rem   publish : dotnet publish of the app and CLI (win-x64, self-contained, single file) into publish\<request id>
rem   smoke   : runs the published CLI (--version, fixture scan) and the published app's start-up self-check
rem             (request: smoke <request id> <publish folder name>)
rem   launch  : starts the published app and leaves it open
rem It never runs anything else. Close this window to stop it.
setlocal EnableExtensions
cd /d "%~dp0"
if not exist buildlogs mkdir buildlogs
if exist buildlogs\request.txt del buildlogs\request.txt
>buildlogs\status.txt echo ready
echo RSMF Inspector build runner is waiting for requests.
echo Leave this window open while a build is running. Close it to stop.
echo.

:wait
if not exist buildlogs\request.txt (
  timeout /t 2 /nobreak >nul
  goto wait
)
set VERB=
set RID=
set ARG=
for /f "usebackq tokens=1,2,3" %%a in ("buildlogs\request.txt") do (
  set VERB=%%a
  set RID=%%b
  set ARG=%%c
)
del buildlogs\request.txt
rem Request id and argument are only ever used as a folder name under publish\: letters and digits only.
echo %RID%%ARG%| findstr /r "^[A-Za-z0-9]*$" >nul || goto wait
if "%RID%"=="" goto wait
set RC=99
echo [%time%] request %RID%: %VERB%
>buildlogs\status.txt echo running %RID% %VERB%
if /i "%VERB%"=="test" call :test
if /i "%VERB%"=="app" call :app
if /i "%VERB%"=="publish" call :publish
if /i "%VERB%"=="smoke" call :smoke
if /i "%VERB%"=="launch" call :launch
if /i "%VERB%"=="pack" call :pack
if /i "%VERB%"=="shots" call :shots
if /i "%VERB%"=="guide" call :guide
if /i "%VERB%"=="samples" call :samples
echo [%time%] finished %RID%: %VERB% exit code %RC%
>buildlogs\status.txt echo done %RID% %VERB% exit=%RC%
goto wait

:test
dotnet test tests\Rsmf.Tests\Rsmf.Tests.csproj -c Release > buildlogs\test.log 2>&1
set RC=%ERRORLEVEL%
exit /b 0

:app
dotnet build src\Rsmf.App\Rsmf.App.csproj -c Release -r win-x64 > buildlogs\app.log 2>&1
set RC=%ERRORLEVEL%
exit /b 0

:publish
dotnet publish src\Rsmf.App\Rsmf.App.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o publish\%RID% > buildlogs\publish.log 2>&1
set RC=%ERRORLEVEL%
if not "%RC%"=="0" exit /b 0
dotnet publish src\Rsmf.Cli\Rsmf.Cli.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o publish\%RID% >> buildlogs\publish.log 2>&1
set RC=%ERRORLEVEL%
exit /b 0

:smoke
publish\%ARG%\rsmf-inspect.exe --version > buildlogs\smoke.log 2>&1
set RC=%ERRORLEVEL%
if not "%RC%"=="0" exit /b 0
publish\%ARG%\rsmf-inspect.exe scan buildlogs\fixtures --out buildlogs\smoke-out --tz America/New_York --quiet >> buildlogs\smoke.log 2>&1
>>buildlogs\smoke.log echo cli-scan-exit=%ERRORLEVEL%
if exist buildlogs\selftest.log del buildlogs\selftest.log
start "" /wait publish\%ARG%\RsmfInspector.exe --selftest "%CD%\buildlogs\selftest.log" "%CD%\buildlogs\fixtures\valid-2.0.rsmf"
set RC=%ERRORLEVEL%
>>buildlogs\smoke.log echo gui-selftest-exit=%RC%
type buildlogs\selftest.log >> buildlogs\smoke.log 2>&1
exit /b 0

:launch
start "" publish\%ARG%\RsmfInspector.exe
set RC=%ERRORLEVEL%
exit /b 0

rem pack    : builds the Microsoft Store package publish\<request id>.msix from a publish folder
rem           (request: pack <request id> <publish folder name>; log: buildlogs\pack.log).
rem           Needs the Windows SDK (makepri.exe and makeappx.exe). The package is not signed:
rem           the Store signs it on submission.
:pack
set RC=2
>buildlogs\pack.log echo pack %RID% from publish\%ARG%
if "%ARG%"=="" goto packbadrequest
if not exist "publish\%ARG%\RsmfInspector.exe" goto packbadrequest
set "LAYOUT=publish\%RID%msix"
if exist "%LAYOUT%" goto packbadrequest
if exist "publish\%RID%.msix" goto packbadrequest
set "KITS=%ProgramFiles(x86)%\Windows Kits\10\bin"
set "KIT="
for /f "delims=" %%k in ('dir /b /ad /o-n "%KITS%\10.*" 2^>nul') do call :pickkit "%%k"
if not defined KIT goto packnokit
>>buildlogs\pack.log echo tools: %KIT%
set "APPVER="
for /f "tokens=3 delims=<>" %%v in ('findstr /c:"<Version>" Directory.Build.props') do set "APPVER=%%v"
findstr /c:"Version=\"%APPVER%.0\"" packaging\AppxManifest.xml >nul
if errorlevel 1 goto packbadversion
mkdir "%LAYOUT%\Assets" >>buildlogs\pack.log 2>&1
copy /y "publish\%ARG%\RsmfInspector.exe" "%LAYOUT%\" >>buildlogs\pack.log 2>&1
copy /y "publish\%ARG%\*.dll" "%LAYOUT%\" >>buildlogs\pack.log 2>&1
copy /y LICENSE "%LAYOUT%\LICENSE.txt" >>buildlogs\pack.log 2>&1
copy /y USER_GUIDE.md "%LAYOUT%\" >>buildlogs\pack.log 2>&1
copy /y THIRD-PARTY-NOTICES.md "%LAYOUT%\" >>buildlogs\pack.log 2>&1
copy /y RULE_GUIDE.csv "%LAYOUT%\" >>buildlogs\pack.log 2>&1
copy /y docs\RULES.md "%LAYOUT%\" >>buildlogs\pack.log 2>&1
xcopy /e /i /q /y samples "%LAYOUT%\samples" >>buildlogs\pack.log 2>&1
copy /y packaging\Assets\*.png "%LAYOUT%\Assets\" >>buildlogs\pack.log 2>&1
copy /y packaging\AppxManifest.xml "%LAYOUT%\" >>buildlogs\pack.log 2>&1
"%KIT%\makepri.exe" createconfig /cf "publish\%RID%prifull.xml" /dq en-US /o >>buildlogs\pack.log 2>&1
findstr /v /i "packaging autoResourcePackage" "publish\%RID%prifull.xml" > "publish\%RID%pri.xml"
"%KIT%\makepri.exe" new /pr packaging /cf "publish\%RID%pri.xml" /mn packaging\AppxManifest.xml /of "%LAYOUT%\resources.pri" /o >>buildlogs\pack.log 2>&1
if errorlevel 1 goto packprifailed
"%KIT%\makeappx.exe" pack /d "%LAYOUT%" /p "publish\%RID%.msix" /o >>buildlogs\pack.log 2>&1
set RC=%ERRORLEVEL%
exit /b 0

:pickkit
if defined KIT exit /b 0
if exist "%KITS%\%~1\x64\makeappx.exe" set "KIT=%KITS%\%~1\x64"
exit /b 0

:packbadrequest
>>buildlogs\pack.log echo The publish folder is missing, or this request id was already used for a package.
exit /b 0

:packnokit
>>buildlogs\pack.log echo The Windows SDK was not found: no makeappx.exe under %KITS%
set RC=3
exit /b 0

:packbadversion
>>buildlogs\pack.log echo packaging\AppxManifest.xml does not carry Version="%APPVER%.0" to match Directory.Build.props.
set RC=4
exit /b 0

:packprifailed
>>buildlogs\pack.log echo makepri failed; no package was made.
set RC=5
exit /b 0

rem shots   : runs the published app's self-check on a copy of the demo conversation (packaging\demo), so the
rem           pictures it saves in buildlogs\shots can be used for the Store listing
rem           (request: shots <request id> <publish folder name>).
:shots
set RC=2
if not exist "publish\%ARG%\RsmfInspector.exe" exit /b 0
if not exist buildlogs\shots\demo mkdir buildlogs\shots\demo
copy /y packaging\demo\export-review.rsmf buildlogs\shots\demo\ >nul
if exist buildlogs\shots\selftest.log del buildlogs\shots\selftest.log
start "" /wait publish\%ARG%\RsmfInspector.exe --selftest "%CD%\buildlogs\shots\selftest.log" "%CD%\buildlogs\shots\demo\export-review.rsmf"
set RC=%ERRORLEVEL%
exit /b 0

rem guide   : writes the full rule guide from a published build to buildlogs\RULE_GUIDE.csv, as UTF-8
rem           (request: guide <request id> <publish folder name>). The file in the project root is
rem           replaced from it by hand after checking.
:guide
set RC=2
if not exist "publish\%ARG%\rsmf-inspect.exe" exit /b 0
cmd /c "chcp 65001 >nul & publish\%ARG%\rsmf-inspect.exe explain --csv > buildlogs\RULE_GUIDE.csv"
set RC=%ERRORLEVEL%
exit /b 0

rem samples : writes the sample set (the files shipped in samples\) to buildlogs\samples-<request id>
rem           by running the one test that generates it (log: buildlogs\samples.log).
:samples
set "RSMF_SAMPLES_OUT=%CD%\buildlogs\samples-%RID%"
dotnet test tests\Rsmf.Tests\Rsmf.Tests.csproj -c Release --filter "FullyQualifiedName~Sample_folder_is_written_with_an_index" > buildlogs\samples.log 2>&1
set RC=%ERRORLEVEL%
set "RSMF_SAMPLES_OUT="
exit /b 0
