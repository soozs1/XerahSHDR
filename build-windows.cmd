@echo off
REM Builds XerahS.App for Windows and prints the resulting EXE path.
REM
REM Run it from a LOCAL drive (C:\...). It cannot run straight out of
REM \\wsl.localhost\... because cmd.exe cannot build in a UNC working directory.
REM
REM Extra switches explained:
REM   -m:1 / nodeReuse=false / UseSharedCompilation=false
REM       single-node build without persistent MSBuild/compiler servers; those
REM       keep DLLs locked and produce MSB3027 "failed to copy" retries.
REM   EnableAppDrivenPluginBuild=false / SkipBundlePlugins=true
REM       plugin builds fill the disk and are not needed for a local run.
REM   BuildWebUI=false / VideoEditorProjectReferenceBuildWebUI=false
REM       reuse the already built ShareX.VideoEditor/frontend/dist instead of
REM       running npm ci (which hangs on WSL-created node_modules/.bin items).
REM
REM NOTE: environment variable names are inherited by MSBuild as global
REM properties, so nothing here may be called OutDir/OutputPath/etc. (an OUTDIR
REM variable once sent every assembly into <project>\src\desktop\app\...).
setlocal EnableExtensions
set "ROOT=%~dp0"
set "LOG=%ROOT%build.log"
set "PROJ=src\desktop\app\XerahS.App\XerahS.App.csproj"
REM Pick the TFM the build produced instead of hard-coding one; the baseline
REM moved from net10.0 to net11.0 and will move again.
set "APP_BIN_DIR="
for /d %%D in ("src\desktop\app\XerahS.App\bin\Debug\net*-windows*") do set "APP_BIN_DIR=%%D"
if not defined APP_BIN_DIR set "APP_BIN_DIR=src\desktop\app\XerahS.App\bin\Debug\net11.0-windows10.0.26100.0"
et11.0-windows10.0.26100.0"
set "PAUSE_AT_END=1"
if /I "%~1"=="--no-pause" set "PAUSE_AT_END=0"

echo === XerahS build ===
echo Folder : %ROOT%
echo Log    : %LOG%
if not exist "%ROOT%.git" (
    echo Note   : this tree has no .git - it is a generated build mirror.
    echo          Edit sources in the WSL checkout and rebuild with build-windows.sh.
)
echo.

if "%ROOT:~0,2%"=="\\" (
    echo [FATAL] This script is running from a UNC/network path:
    echo         %ROOT%
    echo         cmd.exe refuses to build in a UNC working directory.
    echo         Copy the repository to a local drive and run it from there, e.g.
    echo             C:\programs\XerahS-develop\build-windows.cmd
    goto :fail
)

cd /d "%ROOT%"
if errorlevel 1 (
    echo [FATAL] Cannot enter %ROOT%
    goto :fail
)

REM The upstream baseline moved to .NET 11. Prefer the per-user SDK when it exists,
REM because the machine-wide install may still be on an older major version.
if exist "%LOCALAPPDATA%\Microsoft\dotnet\dotnet.exe" set "PATH=%LOCALAPPDATA%\Microsoft\dotnet;%PATH%"
where dotnet >nul 2>&1
if errorlevel 1 (
    echo [FATAL] dotnet is not on PATH. Install the .NET 10 SDK or open a shell where it resolves.
    goto :fail
)

echo [1/4] Stopping running XerahS instances (they lock bin\ DLLs)...
taskkill /F /IM XerahS.exe >nul 2>&1
taskkill /F /IM XerahS.Assistant.exe >nul 2>&1
taskkill /F /IM xerahs-watchfolder-daemon.exe >nul 2>&1

echo [2/4] Cleaning WSL/test leftovers...
where bash >nul 2>&1
if errorlevel 1 (
    echo       bash not found - skipped. Run ./clean-wsl-leftovers.sh --force from WSL.
) else (
    bash clean-wsl-leftovers.sh --force
)

echo [3/4] Building %PROJ% ...
dotnet build "%PROJ%" -m:1 -p:nodeReuse=false -p:UseSharedCompilation=false -p:EnableAppDrivenPluginBuild=false -p:SkipBundlePlugins=true -p:BuildWebUI=false -p:VideoEditorProjectReferenceBuildWebUI=false
set "BUILD_RC=%ERRORLEVEL%"
echo BUILD_RC=%BUILD_RC% > "%LOG%"
echo Folder=%ROOT%>> "%LOG%"
if not "%BUILD_RC%"=="0" (
    echo.
    echo [FAIL] dotnet build exited with %BUILD_RC%. Full output is above.
    goto :fail
)

echo [4/4] Verifying output...
if not exist "%APP_BIN_DIR%\XerahS.exe" (
    echo [FAIL] Build reported success but %APP_BIN_DIR%\XerahS.exe is missing.
    echo        Check that the project builds for net10.0-windows10.0.26100.0.
    goto :fail
)

for %%F in ("%APP_BIN_DIR%\XerahS.exe") do set "EXE_STAMP=%%~tF"

echo.
echo BUILD OK
echo EXE  : %CD%\%APP_BIN_DIR%\XerahS.exe
echo Built: %EXE_STAMP%
echo BUILD_RC=0 >> "%LOG%"
if "%PAUSE_AT_END%"=="1" (
    echo.
    echo Opening the output folder...
    explorer "%CD%\%APP_BIN_DIR%"
    pause
)
endlocal
exit /b 0

:fail
echo.
echo BUILD FAILED - see the messages above.
echo.
if "%PAUSE_AT_END%"=="1" pause
endlocal
exit /b 1
