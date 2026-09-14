#!/usr/bin/env bash
# usage: tools/hexium-publish.sh <package.zip> [comma,separated,categories]
# Uploads a Thunderstore-format zip to Hexium under bruceirons-team using .secrets/hexium_token.
#
# Categories default to none, which is what every package shipped with up to 14 Sep 2026 — the cost
# is that the mod never shows up under any category filter. Valid slugs:
#   curl https://hexium.gg/api/experimental/community/valheim/category/
# The listing's "Installation Type: Client-only" badge is not a category and not ours to set; it is
# a site-wide default shown even on server-only packages. State the install target in the README.
set -u
W="$(cd "$(dirname "$0")/.." && pwd)"
T="$(tr -d '\r\n' < "$W/.secrets/hexium_token")"
API="https://hexium.gg/api/experimental"
H="Authorization: Bearer $T"
Z="$1"
NAME="$(basename "$Z")"
# stat is GNU-only with -c; macOS needs -f%z. This script is the one that runs on the Mac.
SIZE="$(stat -c %s "$Z" 2>/dev/null || stat -f%z "$Z")"

# Build a JSON array from the optional comma-separated second argument.
CATS_JSON='[]'
if [ "${2:-}" != "" ]; then
	CATS_JSON="$(printf '%s' "$2" | awk -F, '{
		out=""
		for (i = 1; i <= NF; i++) {
			gsub(/^[ \t]+|[ \t]+$/, "", $i)
			if ($i == "") continue
			out = out (out == "" ? "" : ",") "\"" $i "\""
		}
		printf "[%s]", out
	}')"
fi
echo "categories=$CATS_JSON"

init="$(curl -s -H "$H" -H "Content-Type: application/json" \
  -d "{\"filename\":\"$NAME\",\"file_size_bytes\":$SIZE}" "$API/usermedia/initiate-upload/")"
UUID="$(echo "$init" | grep -o '"uuid":"[^"]*"' | head -1 | cut -d'"' -f4)"
PARTS="$(echo "$init" | grep -o '"part_number":[0-9]*' | wc -l)"
echo "uuid=$UUID parts=$PARTS"
if [ "$PARTS" != "1" ]; then echo "multi-part upload not implemented (size $SIZE)"; exit 1; fi
URL="$(echo "$init" | grep -o '"url":"[^"]*"' | head -1 | cut -d'"' -f4 | sed 's#\\/#/#g')"

etag="$(curl -s -X PUT --data-binary @"$Z" -D - -o /dev/null "$URL" | grep -i '^etag' | tr -d '\r' | sed 's/^[Ee][Tt]ag: //')"
echo "etag=$etag"
fin="$(curl -s -H "$H" -H "Content-Type: application/json" \
  -d "{\"parts\":[{\"ETag\":$etag,\"PartNumber\":1}]}" "$API/usermedia/$UUID/finish-upload/")"
echo "finish: $(echo "$fin" | grep -o '"status":"[^"]*"')"

sub="$(curl -s -w '\nHTTP %{http_code}' -H "$H" -H "Content-Type: application/json" \
  -d "{\"author_name\":\"bruceirons-team\",\"categories\":$CATS_JSON,\"communities\":[\"valheim\"],\"has_nsfw_content\":false,\"upload_uuid\":\"$UUID\",\"community_categories\":{\"valheim\":$CATS_JSON}}" \
  "$API/submission/submit/")"
echo "$sub" | grep -o '"full_name":"[^"]*"\|"detail":"[^"]*"\|"download_url":"[^"]*"\|HTTP [0-9]*' | head -6
