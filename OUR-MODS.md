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
| **Oarsmen** | yes (MIT) | `mods\oarsmen-src\Oarsmen` | 0.2.0 | both (not required) | `bruceirons.Oarsmen.cfg` |
| **ServerBasedRanch** | yes (MIT) | `mods\serverbasedranch-src\ServerBasedRanch` | 1.0.2 | **server only**, not in the profile | `bruceirons.ServerBasedRanch.cfg` |
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

**0.2.0 (14 Sep)** — rowers now help steer, and work in reverse. Both terms are added to the same
`Vector3` at the same stern point vanilla uses (`transform.position + forward * m_stearForceOffset`,
offset −10), mirroring `Ship.CustomFixedUpdate`'s own two terms: thrust
`forward * m_backwardForce * (1 - |rudder|)` and rudder push `right * m_stearForce * -rudder`, each
negated for `Speed.Back`. Vanilla steers by pushing the **stern sideways**, not by `AddTorque` — match
that shape if this is ever extended, or it stops feeling like the same boat.

Two design decisions worth keeping:

- **No per-rower UI, by construction.** The steering share scales with `m_rudderValue`, so rowers
  amplify the helmsman rather than choosing a direction. It is zero with the rudder centred, which
  means there is nothing to aim, nothing to toggle, and no extra state to sync — sitting down is the
  opt-in. A rower-chosen direction (back-water one bank) would need real UI and is a different mod.
- **Steering is gated to Slow and Back, even when `Row under sail` is on.** That is vanilla's own
  gating: under sail the rudder gets no push at all and turning comes from the velocity term. Adding
  steering there would change how sailing works, which was explicitly out of scope.
- **The steering share defaults lower than the paddle share (0.15 vs 0.25), on purpose.** The two look
  symmetric in the config but the physics is not. Linear drag is quadratic (`m_dampingForward` squares
  the speed) so doubling thrust buys only about √2 speed, while `m_body.angularVelocity -=
  angularVelocity * m_angularDamping` is linear, so steady-state turn rate tracks applied torque
  almost proportionally. The same multiplier therefore moves turning far more than it moves speed.
  Do not "tidy" the two defaults back into matching.

Scale, for tuning: vanilla turns from **two** stern forces, and rowers scale only one. The rudder term
is `m_stearForce` (class default 0.5) and the other is `speed * m_stearVelForceFactor` (0.1), so they
compare as `0.5` vs `speed/10`. At a standstill the rudder term is everything; at ~2.5 m/s it is about
two thirds; at 5 m/s they are equal. So a full crew at 0.15 is roughly +40% turning force at paddle
speed and less as you accelerate — help where you manoeuvre, not where you cruise. Prefabs may override
`m_stearForce`; `oarsmen ship` prints the real value for whatever you are standing on.

**Ship discovery (0.2.0).** Rowability is a property of the hull, not of a list: `Ship.Awake` attaches
`OarsBehaviour` to anything with a `Chair` child, so other mods' boats (OdinShip's canoes) row without
being named. `Chair` is also what excludes the helm — the tiller is a `ShipControlls`. The two config
lists (`Only these ships`, `Excluded ships`) are then re-read every frame via `AllowedByLists`, which
only touches two `HashSet`s, and flip `OarsBehaviour.Active`; the bench scan stays at Awake because a
hull's seats cannot change. That split is deliberate: it makes the lists behave like the rest of the
config (edit and boats already afloat follow) without a component scan per frame. Going inactive clears
the crew and hides the oars, so the hull reverts to vanilla cleanly.

**`Max rowers` defaults to 0 = uncapped**, so every bench on a big modded hull pulls (OdinShip's
warship is the case that forced it). Watch the ordering in `RowForcePatch`: a plain
`Min(count, cap)` reads 0 as "nobody rows" and silently disables the mod, so 0 is branched on
explicitly. Scaling is proportional, not absolute — the share multiplies that hull's own
`m_backwardForce` — so twelve benches at 0.25 is ×4 paddle force, about ×2 speed after quadratic drag,
paddle mode only. Vanilla hulls are unchanged because the Longship has four seats anyway.

**Rowing under sail is modelled, not flat.** `Row under sail` (still Off by default) multiplies the
crew's thrust by `(1 - v/V)²`, v being speed along the hull and V `Speed where oars stop helping` (5 m/s).
A blade only bites while it is moving through the water faster than the hull, so thrust goes as the
square of the speed the blade has left and is zero once the hull outruns it. Squared, not linear:
linear leaves a twelve-rower crew still adding a full paddle force at 4 m/s, which is exactly the
"rowing at speed should be futile" case it exists to prevent.

`SailBite()` is shared by the force patch and the animation deliberately — the oars lift on exactly the
curve that governs the force, so the crew is never drawn pulling hard at a speed where they contribute
nothing. Two copies of that curve would drift the first time one was tuned. The stow is a
`Lerp(stowed, driveDip, effort)` rather than a threshold, which also smoothed out the bank a hard
rudder cancels: those oars now come up instead of snapping to the stowed angle.

The falloff is **deliberately not applied in paddle or reverse**. Vanilla's own paddle force is flat
and speed-independent there, with quadratic hull drag doing the limiting, so matching it keeps those
modes pure augmentation; under sail vanilla applies no paddle force at all, so the model is ours to
choose rather than vanilla's to contradict. Applying it everywhere would also have cut paddle rowing
to ~0.36 of its tuned value at 2 m/s.

Real speeds, for picking V without guessing: Longship paddles ~3.16 m/s and sails 3.6 (into wind) to
9.4 (full tailwind); Karve paddles ~3.14 and sails 2.8 to 7.0 (Valheim wiki). So V=5 puts the crew's
help almost entirely in the acceleration phase - 14% left at paddle speed, 8% at a Longship's slowest
sailing speed, nothing at 5 and above. V=7 would be the value if rowers should still count beating
into a headwind.

Momentum carries across speed changes — `RPC_Forward`/`RPC_Backward` only step the `m_speed` enum, and
the sole write to `m_body.linearVelocity` derives from the current velocity — so "row up to speed, then
switch to sail" works, and the falloff makes the crew bow out of it on its own.

**The water gate — the trap a postfix sets for you.** `Ship.CustomFixedUpdate` wraps *every* force it
applies in `if (!(num2 > m_disableLevel))`, where `num2` is the centre of mass against the average of
five water samples across the float collider. Out of the water, vanilla applies nothing at all and lets
the jump finish ballistically. A postfix runs regardless, so until 0.2.0 the crew kept rowing an
airborne hull, along `transform.forward`, which in a heavy sea points at the sky. `InWater()` mirrors
the test exactly — five samples, not just the centre, because a pitching hull is exactly when one
sample and five disagree. The `m_previous*` fields are `WaterVolume` lookup caches rather than
accumulated state, so re-reading them cannot disturb vanilla's own call earlier in the tick.
**Any future force added here must sit behind that gate too.**

Known and accepted: thrust follows the hull's pitch, because it is `transform.forward` exactly as
vanilla's paddle is. On a 30° wave face half the crew's push becomes vertical, and a twelve-bench crew
is pushing four times a hull's paddle force, so roughly 2x that force goes into the launch. It is
vanilla's own behaviour amplified rather than anything new, and it only applies in the water where
buoyancy and damping answer. If heavy-sea testing shows boats leaping, the lever is a toggle to flatten
the crew's push to the horizontal — deliberately not added yet, since it would diverge from vanilla on
a guess.

**Two steering guards, deliberately at opposite ends.** Uncapping the crew made these necessary.
`Max steering share` (1) ceilings the crew's requested share before the force is built — the provably
safe half, since it only trims something we are adding. `Max turn rate` (45 deg/s) clamps the result
afterwards, and only the yaw about `transform.up`, subtracting just the excess: clamping
`m_body.angularVelocity` wholesale would kill wave roll and pitch and make every sea look flat. It runs
only on ticks where the crew contributed, so an uncrewed hull is never touched by either. Angular
velocity is the right thing to guard because vanilla overwrites *linear* velocity each tick from a
quadratic drag model but only damps rotation, and gently (`m_angularDamping` 0.01), so torque is what
accumulates. `Describe()` prints live turn rate so the cap can be set from a measurement — the 45
default is a guard rail chosen clear of vanilla, not a measured figure.

**Config key renamed, once.** `Ships` ("VikingShip, Karve") became `Only these ships` (empty). The
rename was the point — BepInEx keeps an existing value in the cfg, so had the key stayed, every server
that already had a cfg would have silently kept the old whitelist and never seen auto-discovery. Safe
here only because 0.2.0 was unpublished and 0.1.0 had ~12 downloads; the usual rule in this repo still
stands — do not rename keys after something is deployed.

**Testing it single-handed.** `Simulate rowers` (section 4, default 0) tops the crew up with phantoms:
`Scan()` fills empty benches bow to stern until the total reaches the target, after counting the real
ones, and flags them `simulated` so `oarsmen rowers` can mark them. They are indistinguishable to
everything downstream — force, oars, hover text — which is the point. Gated on `ship.m_players.Count > 0`
so it cannot animate or move derelict boats if it is left on. Server-synced and therefore admin-locked
like the rest, which matters because it changes physics, not just visuals. This is the intended way to
tune the oar pivots and the force multipliers without four players.

**Stroke animation (0.2.0).** Cosmetic only — force is computed independently in the postfix. Each
bench gets a signed `power = clamp(dirSign * (1 - TurnStrokeBias * rudder * side), -1, 1)`: sign picks
which half of the fore-aft swing the blade is submerged for (aft half pulling ahead, forward half
backing water), magnitude scales the sweep so a cancelled-out bank barely moves. Two traps, both hit
while writing it:

- **The `dirSign` multiplies the whole expression, not just the base.** Vanilla negates its steer force
  when backing (`num17 = -1`), so the same rudder swings the bow the other way; if the bank split does
  not flip with it, the oars visually pivot the boat against the direction it is actually turning.
  Verified numerically against vanilla's yaw before committing — the first version was wrong.
- **One stroke frequency for both banks**, even when they pull opposite ways. Giving the backing bank
  its own rate (as vanilla does for its steering paddle, `sin(t * -3)` vs `sin(t * 6)`) drifts the crew
  out of time, and rowing in time is the point.

Bench phase is a deterministic bow-to-stern stagger (`i * 0.04 s`), not `Random`. The old random phase
meant every client drew the same ship with a different stroke pattern.

Not done, and deliberately: differential rowing by applying force at bench positions. Moving the
application point off the stern induces pitch (`AddForceAtPosition` forward of the centre of mass), so
if it is ever wanted, build it as a pure couple — equal and opposite lateral forces on the two banks —
rather than by relocating thrust. Rower state also still rides on per-client animation flags rather
than the ship's ZDO; disagreement is cosmetic only (which oars are drawn), because
`RowForcePatch` returns early unless `m_nview.IsOwner()`.

## ServerBasedRanch

Server-only, nothing on clients, and **not in the Gale profile** — copy the dll to the server by hand.
A dedicated server holds every object's saved state (ZDO) whether or not a zone is loaded, but vanilla
only advances taming and breeding through components, which exist only in loaded zones. This ticks the
unloaded ones directly on their saved fields with vanilla's own numbers, at `Unloaded speed` 50% by
default: hungry animals eat real item stacks in the pen, fed wild ones tame, fed tame ones gain love,
conceive and give birth. `Plugin.cs` is config + a timer; all the work is `Ranch.cs`.

What it ticks is decided by `IsLoadedOrClientOwned` (`Ranch.cs:157`): anything inside vanilla's active
area for a connected peer, owned by a connected peer, or within a 32 m margin outside that area is left
to the client. The margin exists because peer positions reach the server a moment late. The `BQ_lastSim`
ZDO key is shared with BruceQoL's client-side replay so whoever ticks an animal stamps it and the other
side finds no elapsed time to double-count.

**Two accepted limits — decided 14 Sep, don't re-litigate without live evidence.** Both are written up
in README's closed-items list with the full reasoning; briefly:

- The 32 m margin leaves a ring (96–128 m from a player) that neither side ticks. Accepted: players
  move, so nothing sits there for long. 1.0.2 already cut this from a whole zone. Never widen the mod
  into the ring — that means writing to ZDOs a client may own, the BruceNetworking 0.2.1 chest bug.
- Pens freeze while the server is empty, because `ZNet.UpdateNetTime` only advances `m_netTime` when
  `GetNrOfPlayers() > 0` and `Ranch.Tick` reads `ZNet.instance.GetTime()`. Accepted deliberately: it
  keeps this mod, newborn `s_spawnTime` (`Ranch.cs:385`) and vanilla `Growup` on one clock. Switching
  only this mod to wall time breeds animals that cannot grow — `Growup.GrowUpdate` needs a loaded,
  owned instance and measures age on the world clock — so pens fill with young that count toward
  `Max animals per pen` and stall further breeding.

Version drift to watch: `dist\serverbasedranch\` and the source were both on **1.0.2** (13 Sep) while
both docs still said 1.0.1 until 14 Sep. Check `Plugin.cs`, `dist\serverbasedranch\manifest.json` and
the changelog agree before any bump.

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
