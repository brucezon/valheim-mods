#!/usr/bin/env bash
# Prints the latest version + date of every package we replaced, depend on, or watch, on Hexium and Thunderstore.
set -u
latest() { # $1 = json
  ver="$(echo "$1" | grep -o '"latest":{[^}]*' | grep -o '"version_number":"[0-9.]*"' | head -1 | cut -d'"' -f4)"
  date="$(echo "$1" | grep -o '"latest":{[^}]*' | grep -o '"date_created":"[^"]*"' | head -1 | cut -d'"' -f4 | cut -c1-16)"
}
echo "== Hexium =="
for p in Azumatt/AzuWearNTearPatches Smoothbrain/PackHorse Smoothbrain/StaminaRegenerationFromFood Azumatt/AzuAutoStore Azumatt/AzuCraftyBoxes ValheimModding/Jotunn Azumatt/Unshamed VitByr/VBNetTweaks incompletionists/FiresGhettoNetworking Smoothbrain/CreatureLevelAndLootControl Smoothbrain/Network Advize/PlantEasily Advize/PlantEverything; do
  latest "$(curl -s "https://hexium.gg/api/experimental/package/$p/")"
  printf '%-48s %-10s %s\n' "$p" "${ver:--}" "${date:--}"
done
echo "== Thunderstore =="
for p in Advize/PlantEasily Advize/PlantEverything PUP82/PUP_FPS CW_Jesse/BetterNetworking_Valheim ValheimModding/Jotunn VitByr/VBNetTweaks RandyKnapp/EquipmentAndQuickSlots Searica/FortifySkillsRedux Azumatt/AzuWearNTearPatches Smoothbrain/PackHorse; do
  latest "$(curl -s "https://thunderstore.io/api/experimental/package/$p/")"
  printf '%-48s %-10s %s\n' "$p" "${ver:--}" "${date:--}"
done
