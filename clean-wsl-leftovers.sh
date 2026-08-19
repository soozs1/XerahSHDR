#!/usr/bin/env bash
#
# clean-wsl-leftovers.sh
#
# Removes the WSL-created leftovers that break copying the XerahS tree to Windows
# (or running Windows builds/npm ci) — see AGENTS.md §8.
#
# What it cleans:
#   1. POSIX symlinks in ShareX.VideoEditor/frontend/node_modules/.bin
#      (exactly 6 files from WSL npm: jiti, nanoid, rolldown, tsc, tsserver, vite).
#      Windows fs.rmdir cannot delete these, so any copy step or npm ci on the
#      Windows side hangs forever ("remaining 6 files" / MSB3077 ENOTEMPTY).
#      Only symlinks are removed — plain files (Windows .cmd/.ps1 wrappers) are left alone.
#   2. *.lock* zip/test artifacts under **/bin/** left behind by the Unix-only
#      SettingsBaseBackupDiagnosticsTests test (e.g. .../bin/Debug/net10.0/<hash>/backups/2026-01/locked.zip).
#      Those have chmod 0555/0666 (no write/execute), so neither the test TearDown
#      nor Windows Explorer can delete them; they block recursive copies.
#
# Usage:
#   ./clean-wsl-leftovers.sh          # dry-run first (prints what would be removed)
#   ./clean-wsl-leftovers.sh --apply  # actually delete
#   ./clean-wsl-leftovers.sh --force  # --apply + chmod -R u+w on locked dirs first
#
# Exit codes: 0 ok, 1 nothing found in dry-run is not an error (still 0).

set -u

PROJECT_ROOT="$(cd "$(dirname "$0")" && pwd)"
APPLY=0
FORCE=0
for arg in "$@"; do
    case "$arg" in
        --apply) APPLY=1 ;;
        --force) APPLY=1; FORCE=1 ;;
        --help|-h)
            sed -n '2,30p' "$0" | sed 's/^# \{0,1\}//'
            exit 0
            ;;
    esac
done

RED=$'\033[31m'; GREEN=$'\033[32m'; YELLOW=$'\033[33m'; NC=$'\033[0m'
removed_bin=0
removed_zip=0
removed_bytes=0

say()  { printf '%s\n' "$*"; }
info() { printf '%s[INFO]%s %s\n' "$GREEN" "$NC" "$*"; }
warn() { printf '%s[WARN]%s %s\n' "$YELLOW" "$NC" "$*"; }
err()  { printf '%s[ERR]%s %s\n' "$RED" "$NC" "$*"; }

# ── 1. .bin POSIX symlinks ────────────────────────────────────────────────────
BIN_DIRS=$(find "$PROJECT_ROOT/ShareX.VideoEditor/frontend/node_modules" -maxdepth 2 -type d -name .bin 2>/dev/null)
if [ -n "$BIN_DIRS" ]; then
    while IFS= read -r dir; do
        symlinks=$(find "$dir" -maxdepth 1 -type l 2>/dev/null)
        if [ -n "$symlinks" ]; then
            while IFS= read -r f; do
                [ -z "$f" ] && continue
                sz=$(stat -c %s "$f" 2>/dev/null || echo 0)
                if [ "$APPLY" -eq 1 ]; then
                    rm -f "$f" && removed_bin=$((removed_bin + 1)) && removed_bytes=$((removed_bytes + sz))
                    info "removed symlink: $f"
                else
                    warn "would remove symlink: $f"
                fi
            done <<< "$symlinks"
        fi
    done <<< "$BIN_DIRS"
fi

# ── 2. locked.zip / volatile test artifacts under **/bin/** ───────────────────
# Matches the SettingsBaseBackupDiagnosticsTests leftovers:
#   <project>/tests/**/bin/**/backups/**/locked.zip
# and anything else named like *.lock*.zip under bin trees.
ZIP_FILES=$(find "$PROJECT_ROOT/tests" -path "*/bin/*" \( -iname "locked.zip" -o -iname "*.lock*.zip" \) -type f 2>/dev/null)
if [ -n "$ZIP_FILES" ]; then
    while IFS= read -r f; do
        [ -z "$f" ] && continue
        sz=$(stat -c %s "$f" 2>/dev/null || echo 0)
        # The parent month dir (e.g. backups/2026-01) is often chmod 0666/0555
        # (no execute), which blocks recursive deletes on Windows. Force perms first.
        if [ "$APPLY" -eq 1 ]; then
            if [ "$FORCE" -eq 1 ]; then
                chmod -R u+w "$(dirname "$f")" 2>/dev/null
                chmod 777 "$(dirname "$f")" 2>/dev/null
            fi
            # Delete the whole hash-folder chain up to the test bin root when safe:
            # <...>/bin/Debug/net10.0/<hash>/...
            parent="$(dirname "$f")"
            while [ "$parent" != "$PROJECT_ROOT" ]; do
                base="$(basename "$parent")"
                if [ "$base" = "bin" ] || [ "$base" = "Debug" ] || [ "$base" = "Release" ]; then
                    break
                fi
                parent="$(dirname "$parent")"
            done
            # parent now points at the deepest "bin/Debug|Release" ancestor; remove the child
            target="$(dirname "$f")"
            # Walk up until the first hash-like (32 hex) dir and delete that subtree
            walk="$target"
            root_to_delete=""
            while [ "$walk" != "$PROJECT_ROOT" ]; do
                base="$(basename "$walk")"
                if echo "$base" | grep -qE '^[0-9a-f]{32}$'; then
                    root_to_delete="$walk"
                    break
                fi
                walk="$(dirname "$walk")"
            done
            if [ -n "$root_to_delete" ]; then
                size=$(du -sk "$root_to_delete" 2>/dev/null | awk '{print $1}')
                rm -rf "$root_to_delete" && removed_zip=$((removed_zip + 1)) && removed_bytes=$((removed_bytes + size * 1024))
                info "removed test artifact tree: $root_to_delete"
            else
                rm -f "$f" && removed_zip=$((removed_zip + 1)) && removed_bytes=$((removed_bytes + sz))
                info "removed: $f"
            fi
        else
            warn "would remove test artifact: $f"
        fi
    done <<< "$ZIP_FILES"
fi

# ── also drop the settings-backup-tests scratch dirs (read-only chmod leftovers) ──
SCRATCH=$(find "$PROJECT_ROOT/tests" -path "*/bin/*" -type d -name "settings-backup-tests" 2>/dev/null)
if [ -n "$SCRATCH" ]; then
    while IFS= read -r f; do
        [ -z "$f" ] && continue
        if [ "$APPLY" -eq 1 ]; then
            chmod -R u+w "$f" 2>/dev/null
            rm -rf "$f" && info "removed scratch dir: $f"
        else
            warn "would remove scratch dir: $f"
        fi
    done <<< "$SCRATCH"
fi

# ── summary ───────────────────────────────────────────────────────────────────
if [ "$APPLY" -eq 0 ]; then
    echo
    info "Dry run. Re-run with --apply to delete, or --force to also chmod locked dirs."
    exit 0
fi

freed_mb=$(( removed_bytes / 1024 / 1024 ))
echo
info "Done: symlinks removed=$removed_bin, zip artifacts removed=$removed_zip, freed≈${freed_mb} MB"
[ "$removed_bin" -eq 0 ] && [ "$removed_zip" -eq 0 ] && warn "Nothing to clean — the tree is already clean."
exit 0