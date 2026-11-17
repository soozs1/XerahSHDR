#!/usr/bin/env bash
# Builds the Windows app from the WSL checkout.
#
# The runnable EXE only exists in the native Windows copy of the tree, because
# cmd.exe cannot build inside a UNC path (\\wsl.localhost\...) and the desktop
# target framework is Windows-only. This script syncs the sources to that copy
# and invokes its build script.
#
# Override the destination with XERAHS_WIN_TREE if your copy lives elsewhere.
set -euo pipefail
cd "$(dirname "$0")"

WIN_TREE="${XERAHS_WIN_TREE:-/mnt/c/programs/XerahS-develop}"
EXE_REL="src/desktop/app/XerahS.App/bin/Debug/net10.0-windows10.0.26100.0/XerahS.exe"

if [ ! -d "$WIN_TREE" ]; then
    echo "[FATAL] Windows tree not found: $WIN_TREE"
    echo "        Set XERAHS_WIN_TREE=/mnt/c/path/to/XerahS-develop and retry."
    exit 1
fi

echo "[1/4] Cleaning WSL leftovers (node_modules/.bin symlinks, locked.zip)..."
./clean-wsl-leftovers.sh --force || true

echo "[2/4] Syncing sources to $WIN_TREE ..."
tar -C . \
    --exclude='bin' --exclude='obj' --exclude='node_modules' \
    -cf - src tests AGENTS.md Directory.Build.props Directory.Packages.props Directory.Build.rsp build-windows.cmd \
  | tar -C "$WIN_TREE" -xf -

# Mark the destination as a generated mirror so nobody edits it by hand.
{
    echo "This tree is a generated build mirror. Do not edit it."
    echo "Sources live in: $PWD"
    echo "Commit: $(git rev-parse HEAD 2>/dev/null || echo unknown)"
    echo "Synced: $(date -Is)"
} > "$WIN_TREE/.xerahs-mirror"

echo "[3/4] Building on Windows..."
WIN_TREE_WIN="C:\\programs\\XerahS-develop"
if [ "$WIN_TREE" != "/mnt/c/programs/XerahS-develop" ]; then
    WIN_TREE_WIN="$(wslpath -w "$WIN_TREE")"
fi
cmd.exe /c "$WIN_TREE_WIN\\build-windows.cmd" --no-pause

echo "[4/4] Done."
echo "      EXE: $(wslpath -w "$WIN_TREE/$EXE_REL")"
echo "      Log: $(wslpath -w "$WIN_TREE/build.log")"
