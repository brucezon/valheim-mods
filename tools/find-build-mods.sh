#!/usr/bin/env bash
# Search the cached Hexium listing (/tmp/c3.json) and Thunderstore listing (/tmp/ts.json)
# for mods that tune building damage / piece health. Prints full_name, latest version, updated, description.
set -u
PAT='structur|building health|piece health|building damage|damage to (player )?(building|structure|piece)|wear ?n ?tear|wearntear|buildhealth|building.*(durab|health|damage)'
for f in /tmp/c3.json /tmp/ts.json; do
  echo "=== $(basename "$f") ==="
  # split into one package per line at '{"name":' boundaries, then filter
  sed 's/{"name":/\n{"name":/g' "$f" | grep -iE "$PAT" | while IFS= read -r line; do
    fn="$(echo "$line" | grep -o '"full_name":"[^"]*"' | head -1 | cut -d'"' -f4)"
    ver="$(echo "$line" | grep -o '"version_number":"[^"]*"' | head -1 | cut -d'"' -f4)"
    upd="$(echo "$line" | grep -o '"date_updated":"[^"]\{0,10\}' | head -1 | cut -d'"' -f4)"
    desc="$(echo "$line" | grep -o '"description":"[^"]\{0,150\}' | head -1 | cut -d'"' -f4)"
    dep="$(echo "$line" | grep -o '"is_deprecated":[a-z]*' | head -1 | cut -d: -f2)"
    printf '%-48s %-9s %-11s dep=%-5s %s\n' "$fn" "$ver" "$upd" "$dep" "$desc"
  done | sort -k3 -r | head -40
done
