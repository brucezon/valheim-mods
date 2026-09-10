#!/bin/bash
# Valheim LAN mods - macOS installer. Copies BepInEx + our mods into the Steam Valheim folder.
# Run from Terminal:  bash install-mac.command      (or double-click if macOS lets you)
set -e
HERE="$(cd "$(dirname "$0")" && pwd)"
KIT="$HERE/mods"

echo "== Valheim LAN mods for macOS =="

# 1) find the game
GAME=""
for c in "$HOME/Library/Application Support/Steam/steamapps/common/Valheim" \
         "/Applications/Steam.app/Contents/../../steamapps/common/Valheim"; do
  [ -d "$c" ] && GAME="$c" && break
done
if [ -z "$GAME" ]; then
  echo "Could not find Valheim. Install it in Steam first, run it once, then run this again."
  echo "If it lives somewhere unusual, run:  bash install-mac.command \"/path/to/Valheim\""
  [ -n "$1" ] && [ -d "$1" ] && GAME="$1"
  [ -z "$GAME" ] && exit 1
fi
APP="$(ls -d "$GAME"/*.app 2>/dev/null | head -1)"
if [ -z "$APP" ]; then echo "No .app found inside $GAME - is the game fully downloaded?"; exit 1; fi
echo "Game:  $GAME"
echo "App:   $(basename "$APP")"

# 2) copy the files in
echo "== copying BepInEx + mods"
cp -R "$KIT/BepInEx" "$GAME/"
cp -R "$KIT/doorstop_libs" "$GAME/"
cp "$KIT/doorstop_config.ini" "$KIT/.doorstop_version" "$KIT/start_game_bepinex.sh" "$GAME/"

# 3) point the launcher at the .app and make it runnable; clear the download quarantine flag
sed -i '' "s|^executable_name=.*|executable_name=\"$(basename "$APP")\"|" "$GAME/start_game_bepinex.sh"
chmod +x "$GAME/start_game_bepinex.sh"
chmod +x "$GAME/doorstop_libs/"* 2>/dev/null || true
xattr -dr com.apple.quarantine "$GAME/BepInEx" "$GAME/doorstop_libs" "$GAME/start_game_bepinex.sh" 2>/dev/null || true

# 4) tell the player what to paste into Steam.
#    Apple Silicon: Steam and its shell run as arm64, but the game and the doorstop library are Intel,
#    so the launcher must run under Rosetta (arch -x86_64) or the library injection is refused.
if [ "$(uname -m)" = "arm64" ]; then
  LAUNCH="/usr/bin/arch -x86_64 /bin/sh \"$GAME/start_game_bepinex.sh\" %command%"
  echo "Apple Silicon Mac detected: launcher will run under Rosetta."
else
  LAUNCH="\"$GAME/start_game_bepinex.sh\" %command%"
fi
echo
echo "== DONE. One last step, in Steam:"
echo "   Library -> right-click Valheim -> Properties -> General -> Launch Options, paste this:"
echo
echo "   $LAUNCH"
echo
printf '%s' "$LAUNCH" | pbcopy 2>/dev/null && echo "   (it is already on your clipboard - just paste)"
echo
echo "Then launch Valheim from Steam as usual. The first start takes longer while mods load."
echo "Check: in game, Settings shows nothing new, but F1 opens a mod settings window, and"
echo "food tooltips show an Endurance line."
