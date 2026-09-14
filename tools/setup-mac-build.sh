#!/usr/bin/env bash
#
# setup-mac-build.sh — turn a Mac with Steam Valheim into a build box for this repo.
#
# The repo is source-only: refs/, baseline/, snapshots/*/src/ and every *.dll are gitignored,
# so a fresh clone cannot build until the reference tree is rebuilt from the local game install.
# This script does that, plus the toolchain. Safe to re-run; it overwrites refs/1.0/gamepath.
#
# Usage:  tools/setup-mac-build.sh [--game-path <Valheim dir>] [--channel 9.0]
#
# After it finishes, every build needs these in the environment (the script prints them):
#   export DOTNET_ROOT="$HOME/.dotnet"
#   export PATH="$HOME/.dotnet:$HOME/.dotnet/tools:$PATH"
#
# What it does NOT do: ILRepack (Endurance still needs the Windows merge step before shipping),
# the PowerShell publish/sync tools, or the Cecil tools under tools/. Those stay Windows-only.
#
# See OUR-MODS.md, "Building on macOS", for why each step is shaped this way.

set -euo pipefail

REPO="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
GAME="$HOME/Library/Application Support/Steam/steamapps/common/Valheim"
CHANNEL="9.0"

while [[ $# -gt 0 ]]; do
	case "$1" in
		--game-path) GAME="$2"; shift 2 ;;
		--channel)   CHANNEL="$2"; shift 2 ;;
		-h|--help)   sed -n '2,20p' "${BASH_SOURCE[0]}"; exit 0 ;;
		*) echo "unknown argument: $1" >&2; exit 2 ;;
	esac
done

say() { printf '\n==> %s\n' "$1"; }

# --- 0. locate the game -----------------------------------------------------------------
# macOS keeps the assemblies inside the app bundle; Windows has them at valheim_Data\Managed.
MANAGED_SRC="$GAME/valheim.app/Contents/Resources/Data/Managed"
CORE_SRC="$GAME/BepInEx/core"

[[ -d "$MANAGED_SRC" ]] || { echo "No managed assemblies at: $MANAGED_SRC" >&2
	echo "Point --game-path at the Steam Valheim folder (the one containing valheim.app)." >&2; exit 1; }
[[ -d "$CORE_SRC" ]] || { echo "No BepInEx at: $CORE_SRC" >&2
	echo "Install the BepInEx pack into the game folder first (Gale, or the mac kit installer)." >&2; exit 1; }

say "Game: $GAME"
if [[ -f "$GAME/BepInEx/LogOutput.log" ]]; then
	grep -m1 -oE 'Valheim version: [0-9.]+ \(network version [0-9]+\)' "$GAME/BepInEx/LogOutput.log" || true
	grep -m1 -oE 'BepInExPack Valheim version [0-9.]+' "$GAME/BepInEx/LogOutput.log" || true
fi

# --- 1. .NET SDK ------------------------------------------------------------------------
# The Homebrew cask runs a pkg installer under sudo, which fails in a non-interactive shell.
# The official script installs into $HOME and needs no root.
export DOTNET_ROOT="$HOME/.dotnet"
export PATH="$DOTNET_ROOT:$DOTNET_ROOT/tools:$PATH"

if [[ -x "$DOTNET_ROOT/dotnet" ]]; then
	say ".NET SDK already present: $(dotnet --version)"
else
	say "Installing .NET SDK $CHANNEL into $DOTNET_ROOT"
	tmp="$(mktemp -t dotnet-install)"
	curl -fsSL https://dot.net/v1/dotnet-install.sh -o "$tmp"
	bash "$tmp" --channel "$CHANNEL" --install-dir "$DOTNET_ROOT"
	rm -f "$tmp"
	say ".NET SDK installed: $(dotnet --version)"
fi

# --- 2. reference tree ------------------------------------------------------------------
# The csproj HintPaths use the Windows layout ($(GamePath)\valheim_Data\Managed\...) and MSBuild
# rewrites those backslashes on Unix, so recreate the Windows shape rather than repointing GamePath.
GP="$REPO/refs/1.0/gamepath"
say "Building reference tree at refs/1.0/gamepath"
rm -rf "$GP"
mkdir -p "$GP/BepInEx/core" "$GP/valheim_Data/Managed/publicized_assemblies"
cp "$CORE_SRC"/*.dll "$GP/BepInEx/core/"
cp "$MANAGED_SRC"/*.dll "$GP/valheim_Data/Managed/"
echo "    BepInEx/core:          $(ls -1 "$GP/BepInEx/core"/*.dll | wc -l | tr -d ' ') dlls"
echo "    valheim_Data/Managed:  $(ls -1 "$GP/valheim_Data/Managed"/*.dll | wc -l | tr -d ' ') dlls"

# --- 3. publicized assemblies -----------------------------------------------------------
# Our mods reach private game members via IgnoresAccessChecksTo, which needs publicized refs.
if ! command -v assembly-publicizer >/dev/null 2>&1; then
	say "Installing BepInEx.AssemblyPublicizer.Cli"
	dotnet tool install -g BepInEx.AssemblyPublicizer.Cli
fi

# The publicizer targets net6.0; roll it forward so a 9.0-only SDK can host it.
export DOTNET_ROLL_FORWARD=LatestMajor
say "Publicizing game assemblies"
for a in assembly_valheim assembly_guiutils assembly_utils; do
	assembly-publicizer "$GP/valheim_Data/Managed/$a.dll" \
		-o "$GP/valheim_Data/Managed/publicized_assemblies/${a}_publicized.dll"
done

# --- 4. verify --------------------------------------------------------------------------
say "Building every mod in mods/"
failed=0
while IFS= read -r proj; do
	name="$(basename "$proj" .csproj)"
	if dotnet build "$proj" -c Release -p:GamePath="$GP" >/tmp/mac-build-$name.log 2>&1; then
		printf '    ok    %s\n' "$name"
	else
		printf '    FAIL  %s  (see /tmp/mac-build-%s.log)\n' "$name" "$name"
		failed=1
	fi
done < <(find "$REPO/mods" -name '*.csproj' | sort)

if [[ $failed -ne 0 ]]; then
	say "Some projects failed to build."
	exit 1
fi

cat <<EOF

==> Build box ready.

Put these in your shell before building by hand:

    export DOTNET_ROOT="\$HOME/.dotnet"
    export PATH="\$HOME/.dotnet:\$HOME/.dotnet/tools:\$PATH"

Then, for example:

    dotnet build mods/bruceqol-src/BruceQoL/BruceQoL.csproj -c Release \\
        -p:GamePath="$GP"

Remember: Endurance still needs the ILRepack (YamlDotNet) pass on Windows before it ships.
EOF
