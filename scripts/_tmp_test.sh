#!/bin/sh
# Temporary helper: run the test suite against the already-built test assembly.
set -u

export DOTNET_ROOT=/home/XR/.dotnet
export PATH=/home/XR/.local/bin:/home/XR/node/bin:/home/XR/.dotnet:/usr/local/sbin:/usr/local/bin:/usr/sbin:/usr/bin:/sbin:/bin
export DOTNET_CLI_TELEMETRY_OPTOUT=1
export DOTNET_NOLOGO=1
export DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1
export MSBUILDDISABLENODEREUSE=1
export NUGET_PACKAGES=/home/XR/.nuget/packages
export DISPLAY=:1

cd /home/XR/programs/XerahS-develop

LOG=/home/XR/programs/XerahS-develop/_tmp_test_baseline.log
echo "=== test start ===" > "$LOG"

DOTNET=/home/XR/.dotnet/dotnet
PROJ=tests/XerahS.Tests/XerahS.Tests.csproj

"$DOTNET" test "$PROJ" --no-build \
  -p:RuntimeIdentifier=linux-x64 \
  -p:AppendRuntimeIdentifierToOutputPath=false \
  -p:BuildWebUI=false \
  -p:VideoEditorProjectReferenceBuildWebUI=false \
  >> "$LOG" 2>&1
R=$?
echo "=== test exit: $R ===" >> "$LOG"
exit "$R"
