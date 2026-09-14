# 1.0 compatibility results — Wed 9 Sep 2026 (run 08:00–08:30)

**Game:** `GameVersion(1,0,7)` · network version **36 → 39** · Unity 6000.0.61 → **6000.0.75** ·
assembly 2.1 → 2.5 MB, 603 → 693 types · chunked save system SHIPPED (`Version.Player.Chunked=45`,
`DeepNorth=46`) · `Version` restructured into enums (`Version.Network/Player/World`).
Pre-1.0 saves backed up: `backups/pre-1.0-20260909-0817/` (192 MB).

**Loader:** BepInEx pack **5.4.2350** (released 09 Sep 12:30 UTC) is REQUIRED (Unity-6 log probes,
"Support Version suffixes"). Installed both sides; chainloader clean. `install-mods.sh` updated.

## Per-mod verdict (server + client boots at 1.0.7)

| Mod | 1.0 status | Cause | Fix path |
|---|---|---|---|
| **BetterNetworking 2.3.2** | ✅ WORKS | every patch target intact by signature; Steamworks rates applied (153600 → 262144 / 1048576) | none |
| AzuCraftyBoxes **1.8.17** | ✅ works | Azumatt recompiled same day (Hexium) | done |
| PUP FPS 1.0.29 | ✅ clean | — | watch |
| Jötunn 2.29.2 | ✅ clean | — | — |
| FortifySkillsRedux 1.5.3 | ✅ clean (needs Jötunn) | — | — |
| AzuWearNTearPatches 1.0.8 | ⚠ loads | old ServerSync; `Everybody` only hit on runtime config change | await Azumatt recompile; don't hot-edit configs |
| PackHorse 1.0.4 | ⚠ loads | same | await Smoothbrain recompile; don't hot-edit configs |
| StructureDamageTweaks 1.2.3 | ❌ console-cmd ctor error | `ConsoleCommand` ctor gained `hideBehindDevCommands` (2024 build) | unlicensed → cannot republish; damage patches may still apply (untested) |
| **AzuAutoStore 3.1.0** | ❌ DEAD | `ZRoutedRpc.Everybody` (now `const`) in type initializer | Azumatt recompile (Hexium) |
| **CLLC 4.6.4** | ❌ DEAD | same | Smoothbrain recompile (Hexium) |
| **StaminaRegenFromFood 1.5.7** | ❌ DEAD | `ItemData.GetTooltip` gained `appending` param → PatchAll throws | Smoothbrain recompile |
| **EquipmentAndQuickSlots 3.0.2** | ❌ DEAD | `Inventory.Changed()` → `Changed(bool,bool)`; Awake throws | RandyKnapp (active) |
| PlantEasily 2.1.1 (client) | ❌ DEAD | `ZInput.AddButton` ambiguous overload | Advize |
| SSS / DedicatedServer (benched) | ❌ broken | `ZDOMan.IsInPeerActiveArea(Vector2i→Vector3)`, `FindSectorObjects(Vector2s, SimulationDistance, …)` | authors |

Dead dlls parked in `mods/broken-on-1.0/`. **Current validated launch set (7):** BN, PUP FPS,
AzuCraftyBoxes 1.8.17, AzuWearNTearPatches, PackHorse, Jötunn, FortifySkillsRedux.

## The story of the day: ServerSync + `const`

Every Azumatt/Smoothbrain mod compiled before today embeds a ServerSync that reads
`ZRoutedRpc.Everybody` as a static field; 1.0 changed it to `const long = 0`. Mods touching it during
type-init die at load (AutoStore, CLLC); the rest only die when `Broadcast` runs (a synced config
value changed at runtime). A plain recompile fixes it (CraftyBoxes 1.8.17 proves it). Expect Azumatt
and Smoothbrain recompiles within 24–48 h — poll **Hexium's last-updated feed**, not Thunderstore.

Other signature changes observed: `Player.PlacePiece(+cheated)`, `Inventory.AddItem` overloads reshaped,
`ConsoleCommand` ctor (+`hideBehindDevCommands`), `ZDOMan.FindSectorObjects` (new `SimulationDistance`
= the Draw Distance setting).

## Update 09:45 — our own 1.0 builds (`mods/temp-builds/`)

| Build | Method | Status |
|---|---|---|
| **AzuAutoStore 3.1.2** | official (Hexium, "1.0 fix part 2") | ✅ replaces TEMP |
| **AzuWearNTearPatches_TEMP** | Cecil patch: `ldsfld ZRoutedRpc::Everybody` → `ldc.i8 0` (3 sites) | ✅ loads, ConfigSync RPC registered |
| **PackHorse_TEMP** | same (3 sites) | ✅ loads, ConfigSync RPC registered |
| **FoodStaminaRegen_TEMP** | source rebuild: `GetTooltip` patch +`typeof(bool)`; ServerSync + LocalizationManager compiled from source (AddWord via reflection), YamlDotNet + translations merged with ILRepack | ✅ server + client clean, ConfigSync RPC registered |
| **Advize_PlantEasily_TEMP** | source rebuild: explicit `ZInput.AddButton` 7-arg overload; `Piece.SetCreator(uid, PlatformUserID)` | ✅ client loads clean |
| CreatureLevelControl_TEMP | Cecil patch | ❌ retired — also calls `ZDO.GetSector()` whose return type changed (Vector2i→Vector2s); needs Smoothbrain |
| EAQS | — | dropped: vanilla 1.0 "Deeper/Wider Pockets" (Haldor) covers it |
| StructureDamageTweaks | — | parked: `ConsoleCommand` ctor change; unlicensed |
| **Unshamed 1.0.0** (new, client-only) | official (Hexium) | ✅ restores achievements with mods |

Tooling that made this repeatable: `tools/everybody-patcher/` (Mono.Cecil), `refs/1.0/gamepath/`
(publicized 1.0 refs in `$(GamePath)` layout), `tools/ilrepack/ILRepack.exe`, and `*.TEMP.csproj`
SDK-style projects inside each cloned source (`mods/stamina-src`, `mods/advize-src`).

Lessons: (1) `IgnoresAccessChecksTo` does NOT stop Mono's MethodAccessException here — Azumatt's
working builds don't carry it either; reflection does. (2) LocalizationManager must be merged into
the plugin (it locates its owner by executing assembly). (3) `Character.Message(...)` gained a
trailing bool in 1.0 (seen in Azumatt's diff). (4) `Version` constants → enums; `m_networkVersion`
etc. are gone.

**Current pack: server 9 / client 11** — BN, PUP FPS, AzuAutoStore 3.1.2, AzuCraftyBoxes 1.8.17,
WNT_TEMP, PackHorse_TEMP, Jötunn, FortifySkillsRedux, FoodStaminaRegen_TEMP (+ client: PlantEasily_TEMP, Unshamed).
Swap each TEMP for the author's official recompile as it lands (poll Hexium).

## Update 09:55 — in-game test found two more things

- **PUP FPS 1.0.29 is BROKEN on 1.0 and was the "character duplicating itself" bug.** Its per-frame
  `InstanceCombinerMod.Update()` throws `MissingMethodException: ZoneSystem.GetZone` (return type
  Vector2i→Vector2s) after cloning renderers and before cleanup → copies pile up every frame.
  Benched to `mods/broken-on-1.0/`. Not shimmable (9 call sites, no Vector2s→Vector2i conversion);
  author is active (Aug 2026) — wait for the update.
- **Root cause of the access-check errors in our TEMP builds:** every published Valheim mod carries
  `[assembly: SecurityPermission(SecurityAction.RequestMinimum, SkipVerification = true)]` +
  `[module: UnverifiableCode]` (the publicizer MSBuild package emits them); Unity's Mono then skips
  JIT accessibility checks. Our CLI-publicized builds lacked them → FieldAccessException /
  MethodAccessException on every private-member access. Fixed via `SkipVerification.cs` in both
  TEMP projects (`IgnoresAccessChecksTo` alone does NOT work on this Mono). Both rebuilt, redeployed,
  clean on server (8 plugins) and client (10 plugins).

## Update 10:20 — FoodStaminaRegen_TEMP is now OUR design (Endurance curve)

Source: `mods/stamina-src/FoodStaminaRegen/FoodStaminaRegen.cs`. Per-food Endurance (regen/s added
to vanilla's 5/s base) = `Curve(foodStamina) × global multiplier × per-food scale`, foods combined
with **diminishing stacking** (best 100%, 2nd 50%, 3rd 25%), then a total cap.

Defaults (all server-synced config in `org.bepinex.plugins.foodstaminaregen.cfg`):
`Curve = SquareRoot, A = 0.196`, `Stacking weights = 1, 0.5, 0.25`, `Total Endurance cap = 3.5`.
NOTE: the live 1.0 Player prefab has base regen **6/s** (code default says 5; prefab wins), so the
in-game tooltip — which divides by the real base — reads ~17% lower than a 5-based table:
20 stam → +15%, 40 → +21%, 60 → +25%, 80 → +29%, 115 → +35%. Full early belly ≈ +26%, endgame ≈ +51%.
Verified in-game 9 Sep (berries = +15%). Accepted as-is; `Curve A = 0.235` would restore the 5-based numbers.
Alternatives one config line away: Linear (A=0.03 = original mod), Declining, Saturating.
Tooltip now shows the true `+X%` vs base. Per-food entries under `[2 - Food (scale)]` are 1.0 multipliers.

Also fixed two upstream 1.0 bugs the original mod has: `Localizer.Load()` in Awake throws on
clients before Steam init (aborting Awake → mod silently inert) — now deferred with retry; and
`ObjectDB.m_items` contains entries without `ItemDrop` in 1.0 → NRE in the food scan — now
null-guarded. Verified: server 8 plugins / client 10, zero errors, 97 foods registered both sides.

## Update 10:50 — FortifySkillsRedux KEPT as `FortifySkillsRedux_TEMP` (surgical fix)

Evaluated in depth: the floor mechanic is sound and the XP multiplier is independent of it (fortify
XP is computed from the raw vanilla factor; the multiplier prefix runs later). The one real defect:
its `Player.OnDeath` *prefix* removes a world key by raw enum index 17 — `DeathDeleteUnequipped`
on 0.221, `PlayerEvents` on 1.0 — i.e. on 1.0 every death would delete the PlayerEvents key.
Fix: `tools/method-nooper` (Mono.Cecil) rewrote that one prefix to an empty method; the finalizer
(the actual floor logic) is untouched. `Active Skill XP Multiplier` set to 1.0 (global + all skills)
so the mod only changes the death rule. Verified: server boots clean (8 plugins). Because FSR's
finalizer overwrites skills on death, the `skillreductionrate` world key stays irrelevant.
Friend pack rebuilt: `dist/valheim-lan-pack-20260909-1.0.7.zip`.

## Update 11:10 — distribution decision: platform-only pack via Gale sync

Gale profile sync carries only platform-hosted mods and can never replace a local dll (verified in
Gale source: export walks `thunderstore_mods()`, import resolves via the public listing index; hidden
Hexium packages are not in that index). To avoid stranding players on stale local files, the launch
pack is reduced to: BetterNetworking 2.3.2, AzuAutoStore 3.1.2, AzuCraftyBoxes 1.8.17, Unshamed (client).
Server validated: 3 plugins, 0 exceptions. Interim world keys (`WORLD-KNOBS.md`) approximate the
missing mods; each returns as its author's official 1.0 build lands. TEMP builds kept in
`mods/temp-builds/` as fallback only.

## Update 11:30 — FINAL distribution: Gale profile + one local bundle, version-stamped TEMPs

cooki's call: local TEMP files may linger as long as official builds win automatically. Verified in
BepInEx's chainloader source: plugins are grouped by GUID and loaded by highest `[BepInPlugin]`
version (ties = enumeration order). So every TEMP is stamped **just below** its original with
`tools/version-stamper` (Mono.Cecil; attribute only — internal ServerSync/RPC version strings untouched):
WNT 1.0.7.99, PackHorse 1.0.3.99, FoodStaminaRegen 1.5.6.99, FSR 1.5.2.99, PlantEasily 2.1.0.99.
Proven on the server: `Skipping [PackHorse 1.0.3.99] because a newer version exists (PackHorse 1.0.4)`.

Rollout: Gale profile (platform mods: BN, AutoStore 3.1.2, CraftyBoxes 1.8.17, Jötunn, Unshamed) +
one-time local import of `dist/LAN_TempFixes-1.0.0.zip` (Thunderstore layout, 5 stamped dlls).
When an author ships a 1.0 build: add it to the profile, push; BepInEx skips the stale local copy.
Stand-in world keys removed from `start-lan-server.bat` (only `movestaminarate 85` remains).
Server validated with the stamped set: 8 plugins, 0 exceptions, FSR XP multiplier re-set to 1.0.

## Update 11:30 — Serverside Simulations ported to 1.0 (server-only toggle)

`mods/temp-builds/Serverside_Simulations_TEMP.dll`, built from `mods/serverside-simulations/src`
(`Core.cs` rewritten for 1.0: Vector2s zones, `FindSectorObjects(zone, SimulationDistance, …)`,
Vector3-based `IsInPeerActiveArea`/`InActiveArea`, and `CreateGhostZones` redirected to
`CreateLocalZones` instead of replacing the grown `ZoneSystem.Update`). All 14 patches applied on
1.0.7 including both transpilers and the MaxObjectsPerFrame IL manipulator; world started; 0 exceptions.
**Benched by default.** Toggle: `tools/sss-on.bat` / `tools/sss-off.bat` + server restart.
Rationale at the cabin: the server IS a LAN player, so "server owns every zone" == "a LAN player owns
every zone" — the in-person group keeps a seamless sim; remote players get server-authoritative
zones at their own ping instead of hosting zones for everyone through the cabin uplink.
Untested until a client joins with it on: watch `Connections N ZDOS:…` grow on the server while a
client idles, and that mobs still move/spawn (SpawnSystem now runs server-side).

## Update 11:45 — runtime-call sweep: three latent breaks found and fixed

Decompiled every dll in the pack and grepped for calls into 1.0-changed members. Load-clean is not
enough: `Character.Message` gained a trailing `bool log` (old 4-arg overload no longer exists), and
`Inventory.Changed()` became `Changed(bool, bool)`. Old-compiled callers throw MissingMethodException
the first time the call runs (e.g. on a skill level-up). Findings:
- **FortifySkillsRedux_TEMP**: 4-arg `Message` in `LevelUpFortifySkill` (every floor level-up), `Changed()` in RestoreItems → both retargeted
- **PackHorse_TEMP**: 4-arg `Message` in the skill-raise patch → retargeted
- **Jötunn 2.29.2** (official, Jul 2026): 3× 4-arg `Message` + `ZRoutedRpc.Everybody` field read in `CustomRPC.SendPackageRoutine` → all fixed; shipped as **`Jotunn_TEMP` stamped 2.29.2.1 — ABOVE the official** so it wins over the profile's broken copy and yields to any future 2.30
- Clean (compiled against 1.0 or no risky calls): AutoStore 3.1.2, CraftyBoxes 1.8.17, Unshamed, BN, WNT_TEMP, FoodStaminaRegen_TEMP, PlantEasily_TEMP, SSS_TEMP
Tool: `tools/call-retarget` (Mono.Cecil) pushes the new default args and swaps the MethodReference.
Server rebooted with the fixed set: clean. Bundle rebuilt: `dist/LAN_TempFixes-1.0.1.zip`.

## Still to do
- Join test from the second PC (BN handshake + ServerSync join path under 1.0).
- Re-poll Hexium for AutoStore / CLLC / Stamina / WNT / PackHorse recompiles; Thunderstore for EAQS,
  PlantEasily. Re-add each as it lands, one boot each.
- Decide SDT: test whether its damage reduction still works despite the console error, or drop it.
- Fresh world for Thursday: create AFTER this validation, under 1.0.7.

## 9 Sep evening — Hexium publishing + Endurance becomes our own mod

- Hexium API publishing works with team `bruceirons-team` (token in `.secrets/`). Private packages are
  invisible to every endpoint Gale resolves against (listing chunk, package list, per-package API, with
  or without token) -> only PUBLIC packages ride profile sync. Test package `LAN_SyncTest` left private.
- **Jotunn 2.30.0** official (21:49 UTC): call-site sweep clean (Message/Changed/Everybody = 0). On server.
  Jotunn_TEMP retired.
- **PlantEasily_TEMP 2.1.0** published publicly (GPLv3 upstream; source + diff inside the package).
- **Endurance 1.0.0** = FoodStaminaRegen_TEMP re-identified as our own mod (upstream repo has NO license,
  so no public reupload under Smoothbrain's GUID/name). GUID `bruceirons.Endurance`, namespace/class
  renamed, translation key `endurance_stat_name`, MIT + bundled MIT-0 ServerSync/LocalizationManager.
  Source `mods/endurance-src/Endurance` (build: dotnet build -p:GamePath=refs/1.0/gamepath, then
  tools/ilrepack merge of YamlDotNet). Server boot: loads, 97 foods registered, ConfigSync RPC up.
  Config file is now `bruceirons.Endurance.cfg`.
- Bundle rebuilt as `dist/LAN_TempFixes-1.0.2.zip` = WNT + PackHorse + FSR only.

## 9 Sep late — LanServerOptimizations 0.1.0 (server-only, ours)

- Source `mods/lso-src/LanServerOptimizations` (MIT). Built clean first pass against 1.0.7 refs, no ILRepack.
- Replaces `ZDOMan.ServerSortSendZDOS` (adds per-prefab-class bias to vanilla's distance-minus-staleness
  sort; BN does not touch that method) and `ZDOMan.ReleaseZDOS` (the dedicated server arbitrates ownership, pre-1.0 too — verified in baseline;
  in join order; we walk peers LAN-first/lowest-ping, then steer ownership with zone margin, cooldown,
  per-pass cap; players never moved, ships/carts off by default).
- LAN class from the Steam connection's remote IPv4 vs configurable CIDRs; ping from
  `GetConnectionRealTimeStatus` (same API FGN uses). Name overrides for testing.
- Server boot: loads, no Harmony errors, config generated. Two-peer steering test still owed.

## 9 Sep late — BruceQoL + PlantEverything_TEMP published
- **BruceQoL 1.0.0** (`mods/bruceqol-src`, MIT, GUID `bruceirons.BruceQoL`, ModRequired): structures
  (creature 0.5 / boss 0.75 / other-player 1 / environmental 1 / natural wear 1, per-material health,
  stone 1.5), player stamina multipliers + regen delay + pickup range, area repair 15 m, stack size
  multiplier, infinite torches/fires/braziers (by prefab name), SmartSkills-style skills (peak catch-up,
  weapon cross-training, swim x2 + no swim loss, sneak backstab). Server boot clean, 553 items scanned,
  ConfigSync RPC up. Hexium: bruceirons-team/BruceQoL. In-game validation still owed (troll hit, area
  repair, torch, stack, skills).
  Lesson: BepInEx config keys cannot contain ' " = [ ] (Awake threw ArgumentException before PatchAll).
- **PlantEverything_TEMP 1.20.0.99**: Advize source compiled unchanged against 1.0.7 with ServerSync from
  source (official dll dies on the Everybody const). Sweep clean, server boot clean. Hexium:
  bruceirons-team/PlantEverything_TEMP (GPLv3, source inside). Both sides.
- AzuWearNTearPatches_TEMP re-stamped 1.0.7.99 -> 1.0.8.99 (it was 1.0.8 code); bundle now 1.0.3.
- Building-damage research: AzuWearNTearPatches has no damage knob (only invulnerability); vanilla has
  no world key for it (EnemyDamage scales characters only); aedenthorn BuildingDamageMod (SHK reupload)
  works but is unsynced/unlicensed and lumps players with creatures -> replaced by BruceQoL.
- The Bash tool went silent this evening; PowerShell ports: tools/hexium-publish.ps1, check-build-mods.ps1.
- Code review vs 1.0.7 source (9 Sep 17:45): (1) active-area predicate is NOT zone-index based —
  `ZNetScene.PointInsideActiveArea` = 1.5-zone Chebyshev box (1 zone when near==1) clipped to a 1.75-zone
  circle when near==2; steering now mirrors it with an inward metre margin (`Inner margin`, 12 m) instead
  of a zone-index margin (corner objects would otherwise be released by the vanilla pass and flap).
  (2) peers with a smaller validated near-sim distance than the server are skipped as targets.
  (3) `RPC_ZDOData` applies a client's owner field whenever its data revision is newer, so a move can
  race an in-flight update from the old owner; vanilla has the same race; cooldown default cut to 10 s
  so the next pass corrects it. (4) ServerSync leaves peers holding a chain of BufferingSocket wrappers
  (one fewer than the number of ServerSync mods); classifier unwraps up to 32 levels and logs the chain.
  (5) `ZDO.m_tempSortValue<0` doubles as the SaveClone flag only on save clones; vanilla's own sort
  writes negatives to live ZDOs, so biasing is safe.

## 9 Sep night — bundle retired, BruceQoL 1.1.0
- **BruceQoL 1.1.0** published (Hexium bruceirons-team/BruceQoL): + **Hauling** skill via SkillManager
  (MIT-0, compiled from source; icon embedded as `BruceQoL.icons.hauling.png`; XP = 1/s while moving
  at >= 90% carry, +100% carry at 100 x effect factor), + Weather damage toggle, + per-material
  "damage taken" multipliers. Server boot clean: BruceQoL ConfigSync + SkillManager ConfigSync RPCs.
  Build notes: Unity 6 ImageConversion has a ReadOnlySpan overload invisible to net48 refs -> the
  SkillManager copy calls LoadImage(Texture2D, byte[]) via reflection; reference the game's
  netstandard.dll to silence CS1705 for ImageConversionModule.
- **Dropped:** AzuWearNTearPatches_TEMP, PackHorse_TEMP, FortifySkillsRedux_TEMP (user: no death
  floor wanted). `mods/benched-plugins/retired-temp/` holds them. **No local bundle any more** —
  FRIENDS.md is 3 steps; Gale profile carries everything.
- Server = 7 plugins (+ LSO toggle). Kit `dist/valheim-lan-server-kit.zip` rebuilt accordingly.
- Hexium PackHorse 1.0.4 re-verified: still pre-1.0 (moot now).

## 9 Sep night — BruceNetworking 0.2.0 (renamed from LanServerOptimizations; published to Hexium)

- GUID `bruceirons.BruceNetworking`, source `mods/brucenetworking-src`, toggles `tools/bn-on.bat` /
  `bn-off.bat`, config `bruceirons.BruceNetworking.cfg`. Server-only; package on Hexium
  `bruceirons-team/BruceNetworking` (source + IDEAS.md inside). Coexists with BetterNetworking.
- 0.2.0 adds, from the FGN/VBNT survey (`mods/brucenetworking-src/IDEAS.md`): multi-peer send loop
  (vanilla `SendZDOToPeers2` services one peer per frame after a 0.05 s wait; verified in 1.0.7 source),
  RPC area-of-interest (broadcasts with a target ZDO only to peers within 300 m; Distant/unknown/targeted
  RPCs untouched; nothing dropped), wear-tick throttle for server-owned intact pieces (throttled, not
  skipped, so support loss is still detected).
- Boots clean on the test server (8 plugins). Live verification still owed: LAN classification after the
  ServerSync wrapper unwrap, one observed ownership move with the second PC forced remote, non-zero
  peer-send / rpc-suppressed counters in the 60 s stats line.

## 9 Sep night — BruceQoL 1.1.1 (client Awake crash + cheated-world flag)

- Client log showed "Received unknown config entry ..." for every BruceQoL entry in sections 3-6 while
  all four dll copies (Hexium, Gale profile, server, source build) were byte-identical. Cause: the
  Hauling `Skill` constructor (SkillManager) resolves `Localization.instance` -> `PlatformPrefs` ->
  `SteamUtils.IsSteamRunningOnSteamDeck` -> "Steamworks is not initialized" on a 1.0 client during
  plugin Awake, aborting Awake before those entries were bound. Same trap as Endurance's Localizer.
  Fix: skill created after all config binds, retried in a `FejdStartup.Start` postfix; hauling patches
  guarded until it exists.
- "Key [movestaminarate 85] is not possible to set via GUI, assume cheated world key" comes from
  `ServerOptionsGUI.WorldContainsCheatedModifiers` (via `Achievements.IsWorldCheated`). Unshamed already
  overrides `Achievements.IsCheatedAtAll` on clients; BruceQoL 1.1.1 adds `Ignore cheated world keys`
  (on) prefixing `WorldContainsCheatedModifiers` and `SetKeyAndValueIsCheat` so it holds for every
  required client regardless of Unshamed.
- Tooling: `tools/dll-has-string.ps1` scans a dll for a literal at any byte alignment (UTF-16 and
  UTF-8); the earlier whole-file Unicode decode missed odd-aligned strings.
- ServerSync MinimumRequiredVersion = 1.1.1: every client must pull the profile update before joining.
- BruceQoL 1.1.2: infinite fires skip vanilla's 2 s `UpdateFireplace` owner block (which wrote
  `s_lastTime` every tick -> ZDO revision bump -> one network update per fire per 2 s) and only call
  `UpdateState`; Weather damage = Off also skips `WearNTear.UpdateCover` (100 m sphere-cast per roofless
  piece every 4 s in rain, on every client) outside Ashlands/Deep North. Both verified against 1.0.7 source.
- BruceQoL 1.1.3: client-side **Wear check throttle** (on, 10 s) on `WearNTear.UpdateWear` for pieces the
  local instance owns that are intact/dry/old/not Ashlands/Deep North — the same throttle-not-skip logic
  as BruceNetworking's server-side one, now covering the zone owner's client (where big bases actually
  tick under LAN-first ownership). Decision: beehive/sap-collector timestamp cadence and the fireplace
  terrain check left as vanilla (user: fuel real fires; not worth it).
- BruceQoL 1.2.0: section "8 - Skill experience" = OdinsQOL-style percentage modifiers, re-implemented
  (OdinsQOL is AGPLv3, clone at mods/odinsqol for reference only; no code copied). Master toggle, All
  skills gain %, one <Skill> gain % per vanilla SkillType (None/All excluded), Death penalty % applied in
  the existing LowerAllSkills prefix (stacks on m_DeathLowerFactor * Game.m_skillReductionRate).
  Hauling XP is SkillManager-driven and is NOT covered by these (use its own skill_ section).
- BruceQoL 1.2.1: section "9 - Multiplayer scaling" exposes Game.m_healthScalePerPlayer (0.3),
  m_damageScalePerPlayer (0.04), m_difficultyScaleRange (100 m), m_difficultyScaleMaxPlayers (5) —
  vanilla has no world key for these; applied in a Game.Awake postfix and on SettingChanged (both sides,
  server-synced; the hit resolver is the damaged character's owner).
- BruceQoL 1.3.0 (OdinsQOL picks, re-implemented): container sizes by prefab (Container.Awake prefix
  sets m_width/m_height before the Inventory is built; root-object prefab name so ship/cart containers
  match), chest contents + fermenter minutes-left on hover (GetHoverText postfixes), crafting-station
  range / no-roof (CraftingStation.Awake postfix on m_rangeBuild / m_craftRequireRoof), beehive
  m_maxHoney / m_secPerUnit, re-equip after swimming (Humanoid.UpdateEquipment pre/postfix; vanilla 1.0
  only re-shows on the toggle key — verified in Player.cs). All server-synced; sizes default to vanilla.
- BruceQoL 1.3.0 was BROKEN: `[HarmonyPatch(typeof(CraftingStation), "Awake")]` — CraftingStation has
  only Start in 1.0.7 — so PatchAll threw "Patching exception in method null" and the plugin's Awake
  aborted (partially patched). 1.3.1 targets Start. Lesson: after every server boot grep the log for
  `HarmonyException|Patching exception|Error loading`, not just the Loading line; the install step now
  does. 1.3.0 left on Hexium (superseded); profile must be on 1.3.1.

## 10 Sep early — overnight sweep

- BruceQoL 1.3.2: `Stations need roof` default Off (server cfg set too); Hexium README rewritten for all
  12 sections. PlantEasily_TEMP restamped **2.1.1.99** (source is Advize 2.1.1; the old 2.1.0.99 sat
  BELOW Thunderstore's pre-1.0 2.1.1 and would have lost to it) and republished as package 2.1.1.
- Poll (`tools/poll-updates.sh`): **Smoothbrain Network 1.1.1** landed on Hexium 10 Sep 02:24 — sweep
  clean (Message/Changed/Everybody = 0). Server-only. Still fallback #2 behind BN (one networking mod
  at a time). **EquipmentAndQuickSlots 3.1.1** (RandyKnapp, 9 Sep 17:43) is a 1.0 build — it is in the
  client profile; server lacks it (EAQS is client-side; runbook still lists it as dropped — decide).
  No 1.0 builds yet for AzuWearNTearPatches (1.0.8), PackHorse (1.0.4), PUP_FPS (1.0.29), VBNetTweaks
  (0.4.0), FGN (1.3.10), Advize PlantEasily/PlantEverything.
- verify-server.ps1: WORLD_NAME/PASSWORD still CHANGEME; firewall has all-profile rules only for the
  CLIENT exe (valheim.exe) — valheim_server.exe is Public-profile only. Run
  `tools\firewall-allow-server.ps1` as admin before the LAN (Tailscale/GL.iNet adapters are Private).
  Tailscale is installed: this PC 100.117.178.100 (windfury), second PC 100.119.71.127 (msi).
- BruceQoL 1.3.3: Station range is a plain radius (default 30 m, vanilla 20) instead of 0=vanilla; live server cfg set to 30.

## 10 Sep — macOS client verified (Apple Silicon)
- Valheim has a native macOS build (Intel-only, Rosetta on Apple Silicon). Gale has no macOS build.
- Kit: `tools/build-mac-kit.ps1` -> `dist/valheim-mac-mods.zip` (BepInEx pack's `start_game_bepinex.sh` +
  `libdoorstop_x64.dylib` + profile plugins/configs). Installer `tools/mac-kit/install-mac.command`.
- Gotchas found live: (1) zip from Windows has no exec bit -> run installer via `bash file`; (2) the
  launcher's executable_name check is relative to cwd -> run from the game folder; (3) **Apple Silicon:**
  Steam's shell is arm64, so DYLD_INSERT_LIBRARIES of the x86_64 doorstop fails ("inserted dylib could
  not be loaded", Steam shows "OS error 260"). Fix = `/usr/bin/arch -x86_64 /bin/sh "<script>" %command%`.
  Installer now emits that line when `uname -m` = arm64. `xattr -cr` may print "Operation not permitted"
  on the undeletable com.apple.provenance attribute; harmless, use `-dr com.apple.quarantine`.

## Terrain modifications (checked 2026-09-10)
- Per-swing TerrainModifier objects are gone since 0.150.3 (2021); a fresh 1.0 world stores all edits in one _TerrainCompiler ZDO per zone (compressed TCData blob). optterrain only matters for pre-2021 worlds.
- 1.0 changes (vs baseline): paint-only regen path (no mesh/collider rebuild for paths/cultivate, hash-deduped saves), RPC_ApplyOperation carries a prefab hash instead of full settings, neighbour grid for zone seams.
- Remaining per-swing cost: small RPC to the zone owner, whole-zone blob resend to peers with the zone loaded (tens of KB compressed when heavily edited), one heightmap regen on every nearby client (ms stutter, coalesced per frame), and a full regen clears the WearNTear support cache of every piece on that heightmap (BruceQoL wear throttle spreads the re-check).
- Decision: no mod. One-time cost while shaping the base; idle zones cost nothing beyond the stored blob. Terraform before the crowd arrives.

## ServerBasedRanch 1.0.0 (12 Sep 2026)
- Server-only passive taming/breeding. Sweeps ZDOMan.m_objectsByID every 30 s; skips animals in any peer's active area (ZNetScene.InActiveArea vs peer.m_refPos) or owned by a connected peer; advances the rest on saved fields (TameLastFeeding, TameTimeLeft, lovePoints, pregnant, tamed) in 10 s steps with the prefab's Tameable/MonsterAI/Procreation numbers.
- Food: dropped stacks live inside the item ZDO's ItemData package (not a plain 'stack' int in 1.0); decremented via ItemDrop.LoadFromZDO/SaveToZDO on a clone of the prefab ItemData; last item = SetOwner(session) + DestroyZDO.
- Birth: ZDOMan.CreateNewZDO + Persistent/Type/Distant from the offspring prefab's ZNetView, SetPrefab, rotation, tamed, level (+quality for egg offspring), spawntime backdated, owner released to 0. Vanilla Growup raises it on load.
- Shares BQ_lastSim with BruceQoL PassiveTames so the two never double count. Untested in play as of writing: needs a pen, food, and a client leaving the zone.
