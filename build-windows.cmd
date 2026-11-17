@echo off
REM Builds XerahS.App for Windows and prints the resulting EXE path.
REM
REM Why the extra switches:
REM   -m:1 / nodeReuse=false / UseSharedCompilation=false
REM       single-node build without persistent MSBuild/compiler servers; those
REM       servers keep DLLs locked and produce MSB3027 "failed to copy" retries.
REM   EnableAppDrivenPluginBuild=false / SkipBundlePlugins=true
REM       plugin builds fill the disk and are not needed for a local run.
REM   BuildWebUI=false / VideoEditorProjectReferenceBuildWebUI=false
REM       reuse the already built ShareX.VideoEditor/frontend/dist instead of
REM       running npm ci (which hangs on WSL-created node_modules/.bin items).
setlocal
cd /d "%~dp0"

echo [1/3] Stopping running XerahS instances (they lock bin\ DLLs)...
taskkill /F /IM XerahS.exe >nul 2>&1
taskkill /F /IM XerahS.Assistant.exe >nul 2>&1
taskkill /F /IM xerahs-watchfolder-daemon.exe >nul 2>&1

echo [2/3] Cleaning WSL/test leftovers...
where bash >nul 2>&1
if errorlevel 1 (
    echo       bash not found - skipped. Run ./clean-wsl-leftovers.sh --force from WSL.
) else (
    bash clean-wsl-leftovers.sh --force
)

echo [3/3] Building XerahS.App...
dotnet build src\desktop\app\XerahS.App\XerahS.App.csproj ^
    -m:1 -p:nodeReuse=false -p:UseSharedCompilation=false ^
    -p:EnableAppDrivenPluginBuild=false -p:SkipBundlePlugins=true ^
    -p:BuildWebUI=false -p:VideoEditorProjectReferenceBuildWebUI=false
if errorlevel 1 (
    echo.
    echo BUILD FAILED
    exit /b 1
)

echo.
echo BUILD OK
echo EXE: %CD%\src\desktop\app\XerahS.App\bin\Debug\net10.0-windows10.0.26100.0\XerahS.exe
exit /b 0
