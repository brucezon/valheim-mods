#!/usr/bin/env bash
# usage: tools/hexium-publish.sh <package.zip>
# Uploads a Thunderstore-format zip to Hexium under bruceirons-team using .secrets/hexium_token.
set -u
W="$(cd "$(dirname "$0")/.." && pwd)"
T="$(tr -d '\r\n' < "$W/.secrets/hexium_token")"
API="https://hexium.gg/api/experimental"
H="Authorization: Bearer $T"
Z="$1"
NAME="$(basename "$Z")"
SIZE="$(stat -c %s "$Z")"

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
  -d "{\"author_name\":\"bruceirons-team\",\"categories\":[],\"communities\":[\"valheim\"],\"has_nsfw_content\":false,\"upload_uuid\":\"$UUID\",\"community_categories\":{\"valheim\":[]}}" \
  "$API/submission/submit/")"
echo "$sub" | grep -o '"full_name":"[^"]*"\|"detail":"[^"]*"\|"download_url":"[^"]*"\|HTTP [0-9]*' | head -6
