# Our mods — what they are, where they live, how to change them

Handoff doc for any future session. Read this before touching a mod. Companion docs: `LAUNCH.md`
(runbook), `WORLD-KNOBS.md` (world keys), `SERVER-LAPTOP.md` (server machine), `FRIENDS.md` (players),
`snapshots/1.0/RESULTS.md` (chronological findings and lessons, 9–10 Sep 2026).

Hexium team: **bruceirons-team**. Token: `.secrets/hexium_token` (never in docs/commits).
Publish: `tools\hexium-publish.ps1 -Zip dist\<pkg>.zip` (PowerShell; the bash twin is unreliable in
this harness). Every package = Thunderstore layout: `manifest.json`, `README.md`, `CHANGELOG.md`,
`icon.png` (+ `LICENSE`, `plugins\*.dll`, optional `source\`). Package version must increase every publish.

Game: Valheim **1.0.7**, network version 39, Unity 6000.0.75, BepInEx **5.4.2350** (denikson pack;
core is 5.4.23.5). Reference tree for building: `refs\1.0\gamepath\` (`BepInEx\core`, `valheim_Data\Managed`,
`publicized_assemblies\`). Decompiled 1.0 game source: `snapshots\1.0\src\` (grep it before patching).

| Mod | Ours? | Source | Live | Sides | Config file |
|---|---|---|---|---|---|
| **BruceQoL** | yes (MIT) | `mods\bruceqol-src\BruceQoL` | 1.12.0 | both, ModRequired | `bruceirons.BruceQoL.cfg` |
| **Endurance** | yes (MIT) | `mods\endurance-src\Endurance` | 1.0.0 | both | `bruceirons.Endurance.cfg` |
| **BruceNetworking** | yes (fork session) | `mods\brucenetworking-src` (README + IDEAS there) | 0.3.0 | **server only** | `bruceirons.BruceNetworking.cfg` |
| **Oarsmen** | yes (MIT) | `mods\oarsmen-src\Oarsmen` | 0.1.0 | both (not required) | `bruceirons.Oarsmen.cfg` |
| **PlantEasily_TEMP** | no — Advize, GPLv3 rebuild | `mods\advize-src\Advize_PlantEasily` | 2.1.1 (plugin 2.1.1.99) | client | `advize.PlantEasily.cfg` |
| **PlantEverything_TEMP** | no — Advize, GPLv3 rebuild | `mods\advize-src\Advize_PlantEverything` | 1.20.1 (plugin 1.20.0.99) | both | `advize.PlantEverything.cfg` |

Everything else in the profile is an unmodified platform mod (BetterNetworking 2.3.2, AzuAutoStore,
AzuCraftyBoxes, Jotunn 2.30.0, Unshamed, Official_BepInEx_ConfigurationManager, and whatever the host
added later — check the Gale profile `LAN-1.0`, that is the truth).

---

## Building on macOS (added 14 Sep 2026)

All five of our mods build on a Mac, so a clone of this repo plus a Steam Valheim install is a
complete build box — no Windows needed. Verified 14 Sep on macOS 14 / Apple Silicon against the
same game the docs target (1.0.7, network 39, BepInEx 5.4.2350). Only the *build* is portable:
ILRepack for Endurance, `tools\*.ps1`, and the Cecil tools still want Windows or PowerShell.

The repo is source-only — `refs/`, `baseline/`, `snapshots/*/src/` and every `*.dll` are gitignored,
so the reference tree has to be rebuilt locally. Four steps:

1. **.NET SDK** — `brew install --cask dotnet-sdk` fails without a TTY for sudo. Use the official
   script, which needs no root: `curl -fsSL https://dot.net/v1/dotnet-install.sh | bash -s -- --channel 9.0 --install-dir "$HOME/.dotnet"`.
   Then `export DOTNET_ROOT="$HOME/.dotnet"` and put `$HOME/.dotnet` and `$HOME/.dotnet/tools` on PATH.
   `DOTNET_ROOT` is not optional — global tools ship an apphost that cannot find `libhostfxr.dylib` without it.
2. **Reference tree** — the Mac game layout differs from Windows: assemblies live in
   `Valheim/valheim.app/Contents/Resources/Data/Managed`, not `valheim_Data\Managed`. Copy them plus
   `Valheim/BepInEx/core/*.dll` into `refs/1.0/gamepath/valheim_Data/Managed/` and
   `refs/1.0/gamepath/BepInEx/core/` — the csproj HintPaths expect the *Windows* shape, and MSBuild
   translates their backslashes on Unix, so recreate that shape rather than repointing `GamePath`.
3. **Publicized assemblies** — `dotnet tool install -g BepInEx.AssemblyPublicizer.Cli`, then run
   `assembly-publicizer` over `assembly_valheim`, `assembly_guiutils` and `assembly_utils` into
   `publicized_assemblies/<name>_publicized.dll`. The tool targets net6.0, so it needs
   `DOTNET_ROLL_FORWARD=LatestMajor` to run on a 9.0-only SDK.
4. **Build** — unchanged from the Windows instructions, with a POSIX path:
   `dotnet build mods/bruceqol-src/BruceQoL/BruceQoL.csproj -c Release -p:GamePath="$PWD/refs/1.0/gamepath"`.
   `net48` targeting works fine off the SDK's reference assemblies; no mono required.

Confirmed clean (0 warnings, 0 errors): BruceQoL, Endurance, BruceNetworking, Oarsmen, ServerBasedRanch.
Endurance still needs the ILRepack pass on Windows before it is shippable — the Mac build produces a
`Endurance.dll` that has not had YamlDotNet merged in.

## BruceQoL (the big one)

One server-synced config, 16 sections, every value live-editable (ServerSync file watcher + admin
F1 via Configuration Manager). `ModRequired = true`: clients without it are refused. Sections 13
(block compensation) and 16 live in their own files (`BlockCompensation.cs`, `Gathering.cs`) with
`internal static ConfigEntry` fields that Awake fills; the `Toggle` enum is `internal` for that reason.

Sections (see `dist\bruceqol\README.md` for the player-facing wording):
1. General — config lock; **Ignore cheated world keys** (launch-line keys no longer flag the world cheated).
2. Structures — creature 0.5 / boss 0.75 / other-player 1 / environmental 1 damage to pieces; weather
   damage toggle; natural wear; wear-check throttle; per-material **health** (stone 1.5) and **damage taken**.
3. Player — stamina cost multipliers, regen delay, pickup range, **area repair** 15 m, re-equip after swim.
4. Items — stack size multiplier.
5. Fires — infinite torches (On) / fires / braziers, classified by prefab name.
6. Skills — peak catch-up ×2, weapon cross-training ×2 (**melee list**, configurable), swim ×2 + no
   swim loss, sneak-scaled backstab + sneak XP. (SmartSkills ideas, independent code.)
7. **Hauling** — custom skill via SkillManager (MIT-0, compiled from source, patched: `ConfigGroup`,
   `HideEffectFactor`, reflection `LoadImage`). Trains walking at ≥90% load (1 tick/s, same as vanilla
   Run) and pulling a cart ≥100 weight (×2). Effect: **flat** `Extra carry weight at level 100` = 300.
   Cart: load-weight ×, break-force × (both default 1 = off), training toggles. Icon
   `icons\hauling.png` embedded as `BruceQoL.icons.hauling.png`.
8. Skill experience — all-skills gain %, per-skill gain %, death penalty % (fork-added).
9. Multiplayer scaling — vanilla's +30% hp / +4% dmg per extra player, editable (fork-added).
10. Containers — sizes per prefab, chest/fermenter hover (fork-added).
11. Crafting stations — range 30 m, need-roof toggle (fork-added).
12. Beehives — honey count/time (fork-added).
13. Combat — enemy→player, enemy→tame, player→enemy damage ×; enemy/boss health ×. Same code path as
    the Combat world slider, so they **multiply with** `-preset Hard`. Parry / held-block compensation
    (fork, 1.8.0, `BlockCompensation.cs`). **Bow draw tuning** (1.12.0, Off, `BowDraw.cs`): both ends of
    vanilla's `Lerp(drawMin, drawMin*0.2, skill)` as multipliers; prefix replaces
    `Humanoid.GetAttackDrawPercentage` only when On (idea: WackyMole's Tone Down the Twang).
14. Raids — on/off, interval ×, chance ×, duration ×, raids-anywhere, disabled-raid list (names logged at boot).
15. Tames — commandable tames list (default Boar): `Tameable.m_commandable` flipped on Awake (fork-added, 1.9.0).
16. Gathering — extra ore/stone and wood scaled by the finishing hit's Pickaxes / WoodCutting level
    (default 1 = double at 100). `HitData.m_skillLevel` rides on the damage RPC, so the object's owner
    can scale drops without skill sync; the attacker-side prefix on `*.Damage` corrects the level to
    WoodCutting for axes on trees (vanilla sends the Axes level). Owner-side prefixes on
    `TreeBase/TreeLog/Destructible.RPC_Damage`, `MineRock.RPC_Hit`, `MineRock5.DamageArea` open a
    window in which a `DropTable.GetDropList()` postfix appends copies. Follows Smoothbrain
    Mining/Lumberjacking (which add their own skills instead; not used).

Build:
```
dotnet build mods\bruceqol-src\BruceQoL\BruceQoL.csproj -c Release -p:GamePath=<abs>\refs\1.0\gamepath
```
No ILRepack needed (ServerSync + SkillManager compiled from source). Output `bin\Release\BruceQoL.dll`.
Then: bump `ModVersion` in `BruceQoL.cs`, `Properties\AssemblyInfo.cs`, `dist\bruceqol\manifest.json`,
add a CHANGELOG entry, copy dll to `dist\bruceqol\plugins\`, zip, publish.

Test on the desktop server: copy dll to `server\BepInEx\plugins\`, restart `tools\start-test-server.bat`
(world `modtest`, pw `testpass123`), read `server\BepInEx\LogOutput.log` for `Loading [BruceQoL x]`,
`HarmonyException`, `ArgumentException`; confirm the new section appears in the server's cfg.

Gotchas learned the hard way:
- BepInEx config **keys cannot contain** `= \n \t \ " ' [ ]` — an apostrophe in a key name throws in
  Awake and silently kills every later `config()` call. Descriptions may contain anything.
- Unity 6's `ImageConversion.LoadImage` has a `ReadOnlySpan` overload invisible to net48 refs →
  SkillManager copy calls it via reflection. Reference the game's `netstandard.dll` to silence CS1705.
- On 1.0 clients plugin Awake runs before Steam init: anything touching localisation/Steam must defer
  (Endurance retries `Localizer.Load`; Hauling skill is created at the main menu — fork fix 1.1.1).
- SkillManager names a custom skill's config section from a localisation lookup, which on a dedicated
  server returns the raw key (`[skill_2143584628]`) — hence `ConfigGroup`.
- Key renames orphan old values; BepInEx keeps orphan lines. Prefer not to rename after launch.
- `hit.ApplyModifier` in a `RPC_Damage` prefix runs on the victim's owner: both sides need the mod.
- Two Claude sessions edited this file concurrently on 9 Sep; always re-read before editing and check
  `ModVersion` before bumping.

## Endurance

Food gives stamina regen along a curve. Derived from the idea of Smoothbrain's StaminaRegenerationFromFood
(that repo is unlicensed → our own GUID `bruceirons.Endurance`, name, MIT). Curve `SquareRoot`, A=0.196:
10 stam → +10%, 40 → +21%, 80 → +29% of the 1.0 base regen (6/s, prefab-set, not the code's 5).
Stacking weights 1/0.5/0.25, cap 3.5/s. Per-food scale entries auto-generated for all 97 stamina foods.
Tooltip line `Endurance: +X%`. Build like BruceQoL, **then ILRepack** YamlDotNet in
(`tools\ilrepack\ILRepack.exe /internalize ... bin\Release\Endurance.dll bin\Release\YamlDotNet.dll`)
because LocalizationManager needs YamlDotNet; translations in `translations\English.yml`.
Never add Smoothbrain's original alongside it.

## BruceNetworking

Built by the fork session; formerly LanServerOptimizations. Server-only: ZDO send priority by prefab
class, LAN-first zone ownership (LAN subnet > lowest ping, hysteresis), multi-peer send loop, RPC
area-of-interest, wear-tick throttle; each toggleable. Coexists with BetterNetworking. Delivered via the
Gale profile (harmless on clients) → `sync-server-from-gale.ps1` puts it on the server.
**0.3.0 (10 Sep, built on the laptop, live on the server since 10:14 on 11 Sep):** steering only moves
`Steer classes` (default `Creature`), skips objects in use and objects within `Owner keep radius` 40 m of
their owner. 0.2.1 moved chests mid-deposit and the items vanished (owner-revision bump discards the old
owner's in-flight write). Never widen `Steer classes` to Interactive.

## Oarsmen (14 Sep 2026)

Seated players row. `Ship.Awake` postfix adds `OarsBehaviour` to ships listed in `Ships` (VikingShip,
Karve); it finds the ship's `Chair` benches and, every 0.25 s, marks a bench occupied when a player from
`Ship.m_players` stands within 1.2 m of its attach point and is in the bench's attach animation
(`m_animator.GetBool("attach_chair")`, synced by ZSyncAnimation; the local player uses `IsAttached()`).
The tiller (`ShipControlls`) is not a `Chair`, so the helmsman never counts. `Ship.CustomFixedUpdate`
postfix, owner only, paddle mode only (or sail too with `Row under sail`): adds
`forward * m_backwardForce * (1 - |rudder|) * perRower * rowers` at the same point and in the same
impulse form as vanilla's paddle force. Oars are runtime primitives (cylinder shaft + cube blade, hull
material borrowed from the first wood-ish `MeshRenderer`) parented to the ship at
`seat + (side * outward, up, forward)`, optionally snapped to the nearest entry of `Oar hole positions`
(ship-local Z). Rowing: yaw sweep `sin((t + phase) * 6)` like the rudder paddle, blade dipped on the
pull; otherwise held at `Stowed angle`. Console `oarsmen ship | rowers | dump <prefab> [depth]`
registered in a `Terminal.InitTerminal` postfix, logs to LogOutput.log.
ServerSync, `ModRequired = false`. **Untuned:** pivot offsets and hole positions are guesses until the
first in-game session with `oarsmen ship`.

## PlantEasily_TEMP / PlantEverything_TEMP (Advize, GPLv3)

Advize's source (`mods\advize-src`, upstream commit f50a18f0) compiled against 1.0.7 with ServerSync
from source (the official dlls die on the `ZRoutedRpc.Everybody` const change). PlantEasily needed two
one-line call-site fixes; PlantEverything needed none. Our only other change: unit tags
(`[MINUTES]`/`[SECONDS]`/`[ITEM COUNT]`) prefixed to PlantEverything's config descriptions.
Plugin versions are stamped `x.y.z.99` via `tools\version-stamper` so **Advize's real 1.0 update
auto-wins** the moment it is added to the profile; then unlist ours. GPL: every package carries
`source\` + diff. Build with the `*.TEMP.csproj` in each folder (same `-p:GamePath`).

## Retired (do not re-add)

AzuWearNTearPatches, PackHorse, FortifySkillsRedux (folded into BruceQoL; Cecil-patched copies in
`mods\benched-plugins\retired-temp`), Jotunn_TEMP (official 2.30.0 is fixed), PUP FPS and CLLC
(closed source, broken on 1.0), StructureDamageTweaks (unlicensed), EAQS/AzuEPI/Backpacks/SleepSkip
(decisions). Serverside Simulations 1.0 port is benched as `tools\sss-on.bat`.

## Tooling (all in `tools\`)

- `call-retarget` — Cecil: old `Message`/`Changed`/`Everybody` call sites → 1.0. Run on any stale dll;
  the count line doubles as a compatibility sweep (`Message=0 Changed=0 Everybody=0` = clean).
- `version-stamper` — rewrites `[BepInPlugin]` version only. `method-nooper`, `everybody-patcher`.
- `hexium-publish.ps1`, `build-mac-kit.ps1` (+ `mac-kit\`), `sync-server-from-gale.ps1`,
  `verify-server.ps1`, `firewall-allow-server.ps1`, `start-lan-server.bat` (final world rules inside).
- `check-build-mods.ps1`, `find-build-mods.sh` — how the building-damage research was done.

## Distribution

Gale profile `LAN-1.0` (code `UFLOAJ`) carries everything; no local zip any more. Server = laptop
(`Desktop\valheim-lan`, kit in `dist\valheim-lan-server-kit.zip`). Mac players: `dist\valheim-mac-mods.zip`
rebuilt by `build-mac-kit.ps1` after every profile change (Apple Silicon needs the `arch -x86_64`
launch line; the installer prints it).

## Where to pick up

- In-game validation of BruceQoL structures/carts/raids/combat multipliers at non-default values was
  never done by an agent; the host tested some in single player on 10 Sep.
- Hauling training rate felt fast: candidates `Skill gain factor 0.5` + `Training interval 3`.
- Multiplayer scaling (section 9) untestable solo.
- When Azumatt/Smoothbrain/Advize/Searica ship real 1.0 builds: sweep with `call-retarget`, add to
  profile, retire ours (PlantEasily/PlantEverything) or ignore (WearNTear/PackHorse/FSR — BruceQoL owns those now).
