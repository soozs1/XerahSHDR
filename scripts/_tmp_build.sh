#!/bin/sh
# Temporary helper: restore + build the test project.
set -u

export DOTNET_ROOT=/home/XR/.dotnet
export PATH=/home/XR/.local/bin:/home/XR/node/bin:/home/XR/.dotnet:/usr/local/sbin:/usr/local/bin:/usr/sbin:/usr/bin:/sbin:/bin
export DOTNET_CLI_TELEMETRY_OPTOUT=1
export DOTNET_NOLOGO=1
export DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1
export MSBUILDDISABLENODEREUSE=1
export NUGET_PACKAGES=/home/XR/.nuget/packages

cd /home/XR/programs/XerahS-develop

LOG=/home/XR/programs/XerahS-develop/_tmp_build_baseline.log
echo "=== restore+start ===" > "$LOG"

DOTNET=/home/XR/.dotnet/dotnet
PROJ=tests/XerahS.Tests/XerahS.Tests.csproj

# Single RID keeps native-asset copy bounded; extensions path is RID-agnostic
# after the Directory.Build.props fix, so restore/build agree on the assets file.
"$DOTNET" restore "$PROJ" \
  -p:RuntimeIdentifier=linux-x64 \
  -m:1 -p:nodeReuse=false -p:UseSharedCompilation=false \
  >> "$LOG" 2>&1
R=$?
echo "=== restore exit: $R ===" >> "$LOG"
if [ "$R" -ne 0 ]; then exit "$R"; fi

"$DOTNET" build "$PROJ" --no-restore \
  -p:RuntimeIdentifier=linux-x64 \
  -p:AppendRuntimeIdentifierToOutputPath=false \
  -p:BuildWebUI=false \
  -p:VideoEditorProjectReferenceBuildWebUI=false \
  -m:1 -p:nodeReuse=false -p:UseSharedCompilation=false \
  >> "$LOG" 2>&1
R=$?
echo "=== build exit: $R ===" >> "$LOG"
exit "$R"
