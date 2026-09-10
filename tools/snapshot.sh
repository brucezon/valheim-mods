#!/usr/bin/env bash
# Snapshot the CURRENT Valheim install and decompile it, so it can be diffed
# against baseline/ (pre-1.0, 0.221.12). Run after the 1.0 update lands.
#   usage: tools/snapshot.sh [label]     e.g. tools/snapshot.sh 1.0
set -euo pipefail
W="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
LABEL="${1:-current}"
V="/c/Program Files (x86)/Steam/steamapps/common/Valheim"
OUT="$W/snapshots/$LABEL"

mkdir -p "$OUT/managed" "$OUT/src"
cp -r "$V/valheim_Data/Managed/." "$OUT/managed/"
cp "/c/Program Files (x86)/Steam/steamapps/appmanifest_892970.acf" "$OUT/" 2>/dev/null || true

( cd "$OUT/managed" && ilspycmd -p -o "$OUT/src" assembly_valheim.dll >/dev/null 2>&1 )

echo "snapshot '$LABEL' -> $OUT/src   ($(find "$OUT/src" -name '*.cs' | wc -l) files)"
grep -E 'CurrentVersion|m_networkVersion|m_worldVersion|m_playerVersion' "$OUT/src/Version.cs" 2>/dev/null || true
