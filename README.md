# Valheim modding workspace

Prep for the **1.0 release (Wed 9 Sep 2026)** — Deep North, PS5 + Switch 2, end of Early Access.
Server went live **Thu 10 Sep 2026** for the cabin LAN; it now runs on for remote play.
**Start at [Current state](#current-state-13-sep-2026-post-lan--start-here)** at the bottom; the rest of
this file is the chronological log that got us there. Per-mod handoff: `OUR-MODS.md`.

## Layout

    baseline/managed/   pre-1.0 game assemblies, copied 7 Sep 2026 (31 MB, 120 dlls)
    baseline/src/       assembly_valheim.dll decompiled -> 603 .cs files
    baseline/appmanifest_892970.acf
    snapshots/<label>/  same pair, captured after an update
    mods/               mod source projects
    refs/               publicized / reference assemblies
    tools/              snapshot.sh, diff-vs-baseline.sh

## Baseline version (pre-1.0)

    CurrentVersion      0.221.12
    m_networkVersion    36
    m_worldVersion      37
    m_playerVersion     43
    buildid             21981559

## Wednesday procedure

    tools/snapshot.sh 1.0            # re-copy + re-decompile the updated game
    tools/diff-vs-baseline.sh 1.0    # removed / new / changed types
    tools/diff-vs-baseline.sh 1.0 Container   # full diff of one class

Removed or changed types are the fix list: any Harmony patch targeting a method whose
signature moved will fail to apply, usually taking the whole plugin down with it.

## Known 1.0 gotchas

- Iron Gate's 1.0 FAQ gives no mod-compat guarantee; expect nearly every BepInEx
  plugin to need a recompile against the new assemblies.
- **Crossplay disables BepInEx.** A modded server means Steam-only — console players
  cannot join. Decide this before inviting people.
- Save formats: if m_worldVersion / m_playerVersion bump, worlds upgrade one-way.
  Back up worlds_local + characters before first 1.0 launch.

## Machine state (7 Sep 2026)

- Valheim installed, **vanilla** — no BepInEx yet
- No dedicated server installed (app 896660), no SteamCMD
- .NET SDK 9.0.203, git 2.54.0, ilspycmd 8.2.0 (global tool)
- Existing worlds: bruceworld, "not warcraft"; characters: bruce, ex

---

## Pre-1.0 control test — 7 Sep 2026, game 0.221.12

Rig: local dedicated server (`server/`, app 896660) + Steam client, both at 0.221.12 / network v36.
BepInEx 5.4.2333. Unity **6000.0.61f1** (Valheim is already on Unity 6).

| Mod | Version | Built for | Server | Client |
|---|---|---|---|---|
| BetterNetworking | 2.3.2 (Nov 2023) | 0.217.28 | loaded, no patch errors | loaded |
| PUP FPS | 1.0.29 (Aug 2026) | current | loaded + initialised | loaded + initialised |
| PlantEasily | 2.1.1 (Apr 2026) | current | not installed (client-only) | loaded |

**Result: all plugins load clean on 0.221.12. Zero Harmony patch failures on either side.**

This is the key baseline. BetterNetworking is 4 minor versions behind and still applies its
patches to ZSteamSocket / ZPlayFabSocket / ZDOMan / ZNet without error — so the transport layer
has been stable since 0.217.28. If it breaks on Wednesday, that is 1.0's doing specifically,
not accumulated rot. That distinction is what this test buys.

Benign noise seen on the headless server, not mod-related:
`ArgumentNullException: ... shader ... ShieldDomeImageEffect.Awake` (graphics effect on a null
GfxDevice), plus normal `Failed to place all <prefab>` worldgen warnings.

### Still untested
Live client<->server connection under the mods (version handshake + compression on the wire),
and anything load-dependent — one local client cannot reproduce multi-peer contention.

## Running the rig

    tools/start-test-server.bat     # or run server/valheim_server.exe directly
    # client: launch Valheim via Steam, Join IP -> 127.0.0.1:2456

Test server: world `modtest`, name `ModTest`, password `testpass123`, `-public 0` (unlisted).

## Mod risk ranking for 1.0

1. **BetterNetworking** — abandoned since Nov 2023, patches the transport layer that 1.0 is
   most likely to have rewritten for PS5/Switch 2 crossplay. Source cloned to `mods/betternetworking`;
   if it breaks, we recompile it ourselves. No author is coming.
2. **PUP FPS** — active, but must match version on host and every client.
3. **PlantEasily** — client-only, author active (repo pushed May 2026). Lowest risk.

## Container mod: SmarterContainers -> AzuAutoStore (7 Sep 2026)

`Roses/SmarterContainers` 1.7.0 was considered and **rejected**:

- last updated 11 Nov 2023 (same dead window as BetterNetworking)
- **closed source** - `website_url` points at Nexus mods/332, no public repo anywhere
- still declares a dependency on the old `denikson-BepInExPack_Valheim-5.4.2202`

The closed source is what settles it. An abandoned mod we hold source for is a repair job;
an abandoned mod with no source is a dead end - if 1.0 breaks it there is no recovery path.

Replaced with **`Azumatt/AzuAutoStore` 3.0.14** (Feb 2026, 501K downloads, repo
`AzumattDev/AzuAutoStore` pushed Nov 2025, depends on current BepInEx 5.4.2333).
Covers the same ground - auto-store to nearby containers, quick stack, favourites to
protect items, item search, per-container rules - plus ItemDrawers/backpack/WardIsLove support.

Verified loading on the server: `Registered 'Azumatt.AzuAutoStore ConfigSync' RPC`.

**Operational note:** unlike SmarterContainers (client-only, optional), AzuAutoStore uses
ServerSync and **kicks clients that do not have it installed** when present on the server.
It pushes config from server to clients, so settings stay consistent - but it becomes a
mandatory install. BetterNetworking and PUP FPS are already mandatory, so this adds no new
burden, but every player needs the full set.

### Current mod set

| Mod | Version | Server | Client | Maintained |
|---|---|---|---|---|
| BetterNetworking | 2.3.2 | yes | yes | **no - Nov 2023, source held** |
| PUP FPS | 1.0.29 | yes | yes | yes - Aug 2026 |
| AzuAutoStore | 3.0.14 | yes | yes | yes - Feb 2026 |
| PlantEasily | 2.1.1 | no | yes | yes - Apr 2026 |

## Azumatt expansion (7 Sep 2026)

Added three more Azumatt mods (all ServerSync — mandatory on every client once on the server):

| Mod | Version | Notes |
|---|---|---|
| AzuCraftyBoxes | 1.8.15 (Aug 2026) | craft/build from nearby containers; pairs with AzuAutoStore |
| SleepSkip | 1.3.0 (Feb 2026) | percentage-vote night skip |
| AzuExtendedPlayerInventory | 2.4.8 (Aug 2026) | extra equipment/quick slots. **Effectively permanent**: removing mid-playthrough can lose items stored in the extra slots |

Considered and skipped: MaxPlayerCount (BetterNetworking already patches the player limit —
its server config exposes `Player Limit`; running both would double-patch the same code).

Server verified with the full set: 6 plugins load, 4 ConfigSync RPCs registered
(AzuEPI, AzuAutoStore, AzuCraftyBoxes, SleepSkip), zero Harmony failures.

### Final mod set (pre-1.0 validated)

| Mod | Version | Server | Client |
|---|---|---|---|
| BetterNetworking | 2.3.2 | x | x |
| PUP FPS | 1.0.29 | x | x |
| AzuAutoStore | 3.0.14 | x | x |
| AzuCraftyBoxes | 1.8.15 | x | x |
| SleepSkip | 1.3.0 | x | x |
| AzuExtendedPlayerInventory | 2.4.8 | x | x |
| PlantEasily | 2.1.1 | – | x |

## Serverside Simulations added (7 Sep 2026)

`mvp/Serverside_Simulations` 1.1.9 (Sep 2025, built for 0.220.5) — moves zone/AI simulation
from clients to the dedicated server, so one laggy player no longer degrades mobs for everyone.
**Server-only** (adds nothing to the client pack); README explicitly recommends BetterNetworking
alongside it. Repo `ddormer/valheim-serverside` semi-active (code Oct 2025, README Mar 2026).

Verified on 0.221.12: loads clean, all zone-ownership patches applied
(ZDOMan_ReleaseNearbyZDOS, ZoneSystem_Update, SpawnSystem_UpdateSpawning, ...).
Note: its zip ships macOS `__MACOSX/._*.dll` junk — strip AppleDouble files when installing.

**Thursday policy (author's own advice):** disable this mod on any new game patch until
re-verified. For the 1.0 launch: keep the dll out of the server until the Wednesday snapshot/diff
confirms its patch targets survived; server-only means it can be dropped in mid-week with zero
client impact. It is the most 1.0-fragile mod in the set after BetterNetworking.

## AzuWearNTearPatches added (7 Sep 2026)

`Azumatt/AzuWearNTearPatches` 1.0.8 (Jun 2025). ServerSync (mandatory on clients), both sides.
Toggles for structural integrity, weather damage, boat/cart damage, per-material integrity.
**All defaults are Off / vanilla behaviour** — installing it changes nothing until configured
in `Azumatt.AzuWearNTearPatches.cfg` (server copy wins; config is locked + synced).

Verified: 8 plugins load clean, ConfigSync RPC registered.

**Interaction to watch:** Serverside Simulations also patches WearNTear
(`WearNTear_UpdateSupport_Patch`) — no load-time conflict, but if structural-integrity
options are flipped later, test with SSS active before Thursday-night surprises.

## Launch-set decisions (7 Sep 2026, late)

- Added `Smoothbrain/StaminaRegenerationFromFood` 1.5.7 (Feb 2026, active). Both sides;
  ServerSync with version floor but not hard-required. DLL is `FoodStaminaRegen.dll`.
- **SSS benched**: `Serverside_Simulations.dll` moved to `mods/benched-plugins/`.
  Launch performance strategy = PUP FPS only; networking layered in later.
  BetterNetworking KEPT (queue/send-rate/compression).
- Smoothbrain `DedicatedServer` 1.0.2 decompiled to `mods/dedicatedserver-decomp/` —
  same total-server-ownership idea as SSS but younger; lacks SSS's ship-driver patch,
  WearNTear collider fix, and event-system handling. Watch list, not launch list.

### BN queue size — findings from source (BN_Patch_QueueSize.cs)

Mechanism: postfix on `GetSendQueueSize` under-reports the queue fill, tricking vanilla's
send loop into queueing more. Options stop at 80KB; source comment: "higher options are
what cause people to run into Steam errors" (larger options existed once and were removed).
Config text: 32KB @ 100% update rate spikes uploads to ~256 KB/s (32KB*0.4*20/s).
Author's tuning rule: things AROUND you lag for others -> raise queue; your CHARACTER
lags for others -> lower update rate / queue. Default _32KB is the safe setting; do not
exceed 48KB for typical home upload; 80KB only on fast symmetric connections.

### Current launch set (8 server / 9 client)
BN 2.3.2, PUP FPS 1.0.29, AzuAutoStore 3.0.14, AzuCraftyBoxes 1.8.15, SleepSkip 1.3.0,
AzuEPI 2.4.8, AzuWearNTearPatches 1.0.8, StaminaRegenFromFood 1.5.7 (+ PlantEasily client-only).

## 1.0 LAUNCH STATE (9 Sep 2026, night before) — start here

Everything above is the pre-1.0 history. What actually ships:

- **Server = the laptop**, folder `Desktop\valheim-lan\` (this kit). `server\` is a link to Steam's
  "Valheim Dedicated Server" install; BepInEx + plugins + configs were laid over it by `laptop-setup.ps1`.
  Launch: `tools\start-lan-server.bat`. Health check: `tools\verify-server.ps1`. Firewall rules added.
- **Distribution = Gale profile `LAN-1.0` only** (code `UFLOAJ`). No local zip. Friends: `docs\FRIENDS.md`.
- **Pack:** BetterNetworking 2.3.2, AzuAutoStore 3.1.2, AzuCraftyBoxes 1.8.18, Jotunn 2.30.0, Unshamed
  (client), plus OUR Hexium packages under `bruceirons-team`: **Endurance** (food stamina curve),
  **BruceQoL** 1.1.0 (structure damage/health per material, Hauling carry skill, area repair, stamina
  multipliers, stack sizes, infinite torches, smarter skill XP), **PlantEasily_TEMP** (client) and
  **PlantEverything_TEMP** (GPLv3 rebuilds of Advize's mods), **BruceNetworking** (server-only).
- **Retired:** AzuWearNTearPatches, PackHorse, FortifySkillsRedux (folded into BruceQoL), PUP FPS and
  CLLC (broken on 1.0, closed source), EAQS/AzuEPI/Backpacks/SleepSkip (dropped).
- **World rules:** `-preset Hard -modifier DeathPenalty Default -setkey "movestaminarate 85"
  -setkey "skillreductionrate 60"` — see `docs\WORLD-KNOBS.md`.
- **Networking:** LAN players join the laptop's GL.iNet address; remote players via Tailscale share
  link + the laptop's 100.x address. Double NAT at the cabin, no port forwarding.
- **Tuning live:** edit `server\BepInEx\config\bruceirons.BruceQoL.cfg` (or `.Endurance.cfg`) on the
  laptop; ServerSync pushes changes to clients within seconds, no restart.
- **Updating mods:** Gale → Pull update on the laptop → `tools\sync-server-from-gale.ps1` → restart.
  Mac player: also `tools\build-mac-kit.ps1` → resend `dist\valheim-mac-mods.zip` (Gale has no macOS build;
  the kit is a snapshot of the profile with the BepInEx pack's macOS launcher).
- Runbook: `docs\LAUNCH.md`. Per-mod 1.0 findings and lessons: `docs\RESULTS.md`.
  Source for our mods lives on the desktop: `valheim-modding\mods\{endurance-src,bruceqol-src,advize-src}`.

## CURRENT STATE (13 Sep 2026, post-LAN) — start here

The cabin LAN (10–11 Sep) is done. The server stays on the laptop and everyone plays remotely over
Tailscale. This workspace is a private git repo: `https://github.com/brucezon/valheim-mods`, branch
`main`; every publish is committed and pushed. Two Claude sessions work in the same folder, so re-read
a file before editing it and check `ModVersion` before bumping.

**Our Hexium packages (team `bruceirons-team`), all live in the Gale profile `LAN-1.0` unless noted:**

| Package | Version | Where | What changed since launch |
|---|---|---|---|
| **BruceQoL** | 1.12.0 | server + clients, version-locked | 16 config sections now. Added after 1.1.0: skill XP % modifiers + death penalty (8), multiplayer scaling knobs (9), container sizes / hover (10), station range + roof (11), beehives (12), combat multipliers + enemy/boss health + parry/held-block compensation (13), raid knobs (14), commandable boars + passive taming replay, Off (15), skill-scaled ore/wood yield (16), bow draw tuning, Off (13). All server-synced and live. |
| **Endurance** | 1.0.0 | server + clients | unchanged |
| **BruceNetworking** | 0.3.0 | server only (in the profile, no-op on clients) | ownership steering moves **creatures only**, skips objects in use and anything within 40 m of its owner. 0.2.1 lost a chest deposit by moving a chest mid-write; never widen `Steer classes`. Post-LAN settings: see `LAUNCH.md` and the note below. |
| **ServerBasedRanch** | 1.0.2 | server only, **not in the profile**, copy by hand | ticks unloaded pens on the server (eat, tame, breed, birth) with vanilla numbers at 50% speed. Runs on the world clock, which freezes when nobody is online — deliberate, see below. |
| **Oarsmen** | 0.1.0 | server + clients (not required) | new 14 Sep: seated players row the Longship/Karve (+25% paddle force per bench, oars drawn through the hull beside occupied benches). Oar placement untuned until the first in-game session. |
| **PlantEasily_TEMP** | 2.1.1 | clients | Advize GPL rebuild, retire when Advize ships 1.0 |
| **PlantEverything_TEMP** | 1.20.1 | server + clients | Advize GPL rebuild, unit tags in descriptions |

Profile additions since launch by the host: OdinShip 0.7.9 (Marlthon), BuildCameraCHE, Official
ConfigurationManager. Final launch line adds `-setkey "playerevents"` to the rules above.

**Post-LAN networking.** Send priority, multi-peer send loop, RPC area of interest and the wear throttle
stay on. Ownership steering: keep on with LAN-first if the host plays from the server's own network;
turn `Ping tiebreak` off if everyone is remote, so nobody's fight is handed to whoever lives nearer the
server. Drop `Log peer stats` or raise `Stats interval` for a long-running server.

**How to change things.** Config: edit the cfg on the laptop or use F1 as admin; ServerSync pushes it
live, and the client log's `Received N configs` line is the proof. Mod update: bump version in source +
`AssemblyInfo` + `manifest.json`, changelog, build, load-test on the desktop test server, zip, publish
with `tools\hexium-publish.ps1`, commit, push; then Gale update + `sync-server-from-gale.ps1` on the
laptop. Details and gotchas: `OUR-MODS.md`.

**Open items, not built:** cart XP scaled by cargo weight (design in chat, 13 Sep); dungeon reset;
per-boat rudder speed override for OdinShip boats.

**Closed 14 Sep without a fix** — all three judged acceptable rather than worth the code. Do not
reopen these without new evidence from a live server:

- *Hauling trains against a wall.* Training counts intended velocity, so walking into a rock still
  earns XP. Not worth a real-displacement check.
- *ServerBasedRanch's dead band.* The item was already stale: 1.0.2 (13 Sep) cut it from a whole zone
  to 32 m. What remains is a ring from 96 m to 128 m around a player where ServerBasedRanch defers to
  the client but the client has not loaded the pen, so nobody ticks it. Accepted because players move
  around constantly — an animal only sits in that ring in passing, never for long. Widening
  ServerBasedRanch into the ring would mean writing to ZDOs a client may own, which is the bug class
  that ate a chest deposit in BruceNetworking 0.2.1.
- *ServerBasedRanch's frozen clock while the server is empty.* `ZNet.UpdateNetTime` only advances
  `m_netTime` when `GetNrOfPlayers() > 0`, so an empty server advances no pens. Kept deliberately:
  it holds ServerBasedRanch, newborn `s_spawnTime` and vanilla `Growup` on one clock. Moving only
  ServerBasedRanch to wall time would breed animals that cannot grow (`Growup` needs a loaded, owned
  instance and measures age on the world clock), filling pens with young that count toward the pen cap
  and stall further breeding.
