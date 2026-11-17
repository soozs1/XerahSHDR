#!/usr/bin/env bash
# Builds the .upstream port (v0.34.1 + our commits) as a native Windows app.
#
# The port normally builds and runs under WSL, but WSLg hosts every window through
# msrdc.exe: no title bar, no taskbar preview, and a decorated window title. Running the
# same sources natively on Windows gives a normal window with maximize/restore.
#
# Usage: ./build-port-windows.sh
# Output: C:\programs\XerahS-port\src\desktop\app\XerahS.App\bin\Debug\net10.0-windows10.0.26100.0\XerahS.exe
set -euo pipefail

ROOT="$(cd "$(dirname "$0")" && pwd)"
PORT="$ROOT/.upstream"
TARGET="/mnt/c/programs/XerahS-port"
WINDOWS_TARGET='C:\programs\XerahS-port'

if [ ! -d "$PORT" ]; then
    echo "[FATAL] $PORT not found. Create the worktree first: git worktree add .upstream v0.34.1"
    exit 1
fi

echo "[1/3] Packing sources (bin/obj/.git excluded)..."
TAR_FILE="$(mktemp /tmp/xerahs-port-XXXXXX.tar)"
tar --exclude=./bin --exclude=./obj --exclude='*/bin' --exclude='*/obj' --exclude=./.git \
    -cf "$TAR_FILE" -C "$PORT" .

echo "[2/3] Extracting to $TARGET..."
mkdir -p "$TARGET"
tar -xf "$TAR_FILE" -C "$TARGET"
cp "$ROOT/build-windows.cmd" "$ROOT/clean-wsl-leftovers.sh" "$TARGET/"
rm -f "$TAR_FILE"

echo "[3/3] Building on Windows (cmd.exe must run from a local drive)..."
cd "$TARGET"
cmd.exe /c "cd /d $WINDOWS_TARGET && build-windows.cmd --no-pause"
