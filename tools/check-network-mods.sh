#!/usr/bin/env bash
# Pulls the three priority/scheduler networking mods from Hexium and runs the 1.0 call-site sweep on each.
set -u
W="$(cd "$(dirname "$0")/.." && pwd)"
WIN="C:/Users/cooki/agent-projects/valheim-modding"
S="$W/refs/1.0/_stage/netmods"
mkdir -p "$S"

echo "=== VBNetTweaks GitHub: last 4 commits ==="
curl -s "https://api.github.com/repos/vitalikbyrevich/VBNetTweaks/commits?per_page=4" | grep -o '"date": *"[^"]*"\|"message": *"[^"]\{0,90\}' | awk 'NR%3!=0' | head -8

for entry in "Smoothbrain/Network/1.1.0" "VitByr/VBNetTweaks/0.4.0" "incompletionists/FiresGhettoNetworking/1.3.10"; do
  ns="${entry%%/*}"; rest="${entry#*/}"; name="${rest%%/*}"; ver="${rest#*/}"
  dir="$S/$ns-$name-$ver"; mkdir -p "$dir"
  url="$(curl -s "https://hexium.gg/api/experimental/package/$ns/$name/$ver/" | grep -o '"download_url":"[^"]*"' | head -1 | cut -d'"' -f4)"
  echo; echo "=== $ns-$name-$ver  ($url) ==="
  curl -sL "$url" -o "$dir/pkg.zip" && (cd "$dir" && unzip -o -q pkg.zip)
  find "$dir" -name '*.dll' | while read -r dll; do
    b="$(basename "$dll")"
    rel="${dll#$W/}"
    echo "-- $b"
    (cd "$W/tools/call-retarget" && dotnet run -c Release --no-build -- "$WIN/$rel" "$WIN/${rel%.dll}.checked.dll" "$WIN/refs/1.0/gamepath/valheim_Data/Managed" 2>&1 | tail -1)
    "$W/tools/ilspycmd/ilspycmd" "$WIN/$rel" -o "$dir/decomp-$b" >/dev/null 2>&1 || ilspycmd "$WIN/$rel" -o "$dir/decomp-$b" >/dev/null 2>&1
    grep -rho 'HarmonyPatch(typeof([A-Za-z_.]*), *\(nameof([A-Za-z_.]*)\|"[A-Za-z_]*"\)' "$dir/decomp-$b" 2>/dev/null | sed 's/HarmonyPatch(typeof(//; s/), *nameof(/./; s/), *"/./; s/[")]//g' | sort -u | tr '\n' ' '
    echo
  done
done
