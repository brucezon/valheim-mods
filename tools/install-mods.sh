#!/usr/bin/env bash
# Install BepInEx + the mod set into BOTH the Steam client and the local dedicated server.
# Re-run after any Valheim update.   usage: tools/install-mods.sh
# Keep this list in sync with LAUNCH.md's pack table.
set -euo pipefail
W="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
X="$W/downloads/extracted"
CLIENT="/c/Program Files (x86)/Steam/steamapps/common/Valheim"
SERVER="$W/server"

BEPINEX="$X/BepInExPack_Valheim-5.4.2350/BepInExPack_Valheim"

# name|dll path|where (both/client)
MODS=(
  "BetterNetworking 2.3.2|$X/BetterNetworking-2.3.2/CW_Jesse.BetterNetworking.dll|both"
  "PUP_FPS 1.0.29|$X/PUP_FPS-1.0.29/PUP_FPS.dll|both"
  "AzuAutoStore 3.1.0|$X/AzuAutoStore-3.1.0/AzuAutoStore.dll|both"
  "AzuCraftyBoxes 1.8.17|$X/AzuCraftyBoxes-1.8.17/AzuCraftyBoxes.dll|both"
  "AzuWearNTearPatches 1.0.8|$X/AzuWearNTearPatches-1.0.8/AzuWearNTearPatches.dll|both"
  "StaminaRegenerationFromFood 1.5.7|$X/StaminaRegenerationFromFood-1.5.7/FoodStaminaRegen.dll|both"
  "Jotunn 2.29.2 (lib)|$X/Jotunn-2.29.2/plugins/Jotunn.dll|both"
  "EquipmentAndQuickSlots 3.0.2|$X/EquipmentAndQuickSlots-3.0.2/plugins/EquipmentAndQuickSlots.dll|both"
  "PackHorse 1.0.4|$X/PackHorse-1.0.4/PackHorse.dll|both"
  "CLLC 4.6.4|$X/CLLC-4.6.4/CreatureLevelControl.dll|both"
  "StructureDamageTweaks 1.2.3|$X/StructureDamageTweaks-1.2.3/plugins/StructureDamageTweaks.dll|both"
  "PlantEasily 2.1.1 (client-only)|$X/PlantEasily-2.1.1/Advize_PlantEasily.dll|client"
)

install_bepinex() {
  echo "  BepInEx -> $1"
  cp -r "$BEPINEX/BepInEx" "$1/"
  cp "$BEPINEX/winhttp.dll" "$BEPINEX/doorstop_config.ini" "$BEPINEX/.doorstop_version" "$1/"
  cp -r "$BEPINEX/doorstop_libs" "$1/"
  mkdir -p "$1/BepInEx/plugins"
}

install_mods() {  # $1 = target root, $2 = both|client
  local entry name dll where
  for entry in "${MODS[@]}"; do
    IFS='|' read -r name dll where <<< "$entry"
    if [ "$where" = "both" ] || [ "$where" = "$2" ]; then
      cp "$dll" "$1/BepInEx/plugins/"
      echo "  + $name"
    fi
  done
}

echo "== CLIENT =="
install_bepinex "$CLIENT"
install_mods "$CLIENT" client

echo "== SERVER =="
install_bepinex "$SERVER"
install_mods "$SERVER" server
echo "  (PlantEasily deliberately NOT installed - client-only)"

echo
echo "client plugins:"; ls "$CLIENT/BepInEx/plugins/"
echo "server plugins:"; ls "$SERVER/BepInEx/plugins/"
