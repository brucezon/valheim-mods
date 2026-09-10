#!/usr/bin/env bash
# Creates mods/endurance-src/Endurance from the stamina-src tree with our own identity.
set -eu
W="$(cd "$(dirname "$0")/.." && pwd)"
SRC="$W/mods/stamina-src/FoodStaminaRegen"
E="$W/mods/endurance-src/Endurance"
rm -rf "$W/mods/endurance-src"
mkdir -p "$E/Properties" "$E/translations"

cp "$SRC/FoodStaminaRegen.cs" "$E/Endurance.cs"
cp "$SRC/FoodStaminaRegen.TEMP.csproj" "$E/Endurance.csproj"
for f in ConfigurationManagerAttributes.cs ServerSync.src.cs IgnoresAccessChecksTo.cs LocalizationManager.src.cs LocalizationAccess.cs SkipVerification.cs; do
  cp "$SRC/$f" "$E/"
done
cp "$SRC/Properties/AssemblyInfo.cs" "$E/Properties/"
echo 'endurance_stat_name: "Endurance"' > "$E/translations/English.yml"

sed -i \
  -e 's/namespace FoodStaminaRegen;/namespace Endurance;/' \
  -e 's/public class FoodStaminaRegen : BaseUnityPlugin/public class EndurancePlugin : BaseUnityPlugin/' \
  -e 's/private static FoodStaminaRegen mod = null!;/private static EndurancePlugin mod = null!;/' \
  -e 's/ModName = "Stamina Regeneration from Food"/ModName = "Endurance"/' \
  -e 's/ModVersion = "1.5.7"/ModVersion = "1.0.0"/' \
  -e 's/ModGUID = "org.bepinex.plugins.foodstaminaregen"/ModGUID = "bruceirons.Endurance"/' \
  -e 's/[$]fsr_stat_name/$endurance_stat_name/' \
  "$E/Endurance.cs"

sed -i \
  -e 's#<AssemblyName>FoodStaminaRegen_TEMP</AssemblyName>#<AssemblyName>Endurance</AssemblyName>#' \
  -e 's#<RootNamespace>FoodStaminaRegen</RootNamespace>#<RootNamespace>Endurance</RootNamespace>#' \
  -e 's#<Compile Include="FoodStaminaRegen.cs" />#<Compile Include="Endurance.cs" />#' \
  -e 's#<OutputPath>bin\\TEMP\\</OutputPath>#<OutputPath>bin\\Release\\</OutputPath>#' \
  "$E/Endurance.csproj"

echo "== identity =="
grep -n 'ModName\|ModVersion\|ModGUID\|^namespace\|class EndurancePlugin\|stat_name\|mod = null' "$E/Endurance.cs"
echo "== csproj =="
grep -n 'AssemblyName\|RootNamespace\|Compile Include="Endurance\|OutputPath' "$E/Endurance.csproj"
echo "== AssemblyInfo =="
grep -v '^//' "$E/Properties/AssemblyInfo.cs" | grep -v '^$' || true
echo "== old bin/TEMP =="
ls "$SRC/bin/TEMP"
echo "== ilrepack =="
ls "$W/tools/ilrepack" | head -5
