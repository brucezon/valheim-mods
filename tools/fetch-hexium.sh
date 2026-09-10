#!/usr/bin/env bash
# usage: tools/fetch-hexium.sh <Namespace/Name/Version> [...]
# Downloads each package from Hexium into refs/1.0/_stage/hexium/<ns>-<name>-<ver>/ and runs the
# call-site sweep (Message/Changed/Everybody) on every dll inside.
set -u
W="$(cd "$(dirname "$0")/.." && pwd)"
WIN="C:/Users/cooki/agent-projects/valheim-modding"
for entry in "$@"; do
  ns="${entry%%/*}"; rest="${entry#*/}"; name="${rest%%/*}"; ver="${rest#*/}"
  dir="$W/refs/1.0/_stage/hexium/$ns-$name-$ver"; mkdir -p "$dir"
  url="$(curl -s "https://hexium.gg/api/experimental/package/$ns/$name/$ver/" | grep -o '"download_url":"[^"]*"' | head -1 | cut -d'"' -f4)"
  echo "=== $ns-$name-$ver  $url"
  curl -sL "$url" -o "$dir/pkg.zip" && (cd "$dir" && unzip -o -q pkg.zip)
  find "$dir" -name '*.dll' | while read -r dll; do
    rel="${dll#$W/}"
    (cd "$W/tools/call-retarget" && dotnet run -c Release --no-build -- "$WIN/$rel" "$WIN/${rel%.dll}.checked.dll" "$WIN/refs/1.0/gamepath/valheim_Data/Managed" 2>&1 | tail -1)
  done
done
