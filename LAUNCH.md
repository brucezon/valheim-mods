# Valheim 1.0 — LAN Launch Runbook

**1.0 drops Wed 9 Sep 2026 · server live Thu 10 Sep · ~7 players · host = this PC (9800X3D/32GB)**
History and reasoning: `README.md`. This file is what we execute.
**1.0 LANDED — results + current surviving pack: `snapshots/1.0/RESULTS.md`**

## The mod pack — LIVE SET on 1.0.7 (validated 9 Sep; details in `snapshots/1.0/RESULTS.md`)

**Distribution (final, 9 Sep late): Gale profile ONLY — every mod is on Hexium/Thunderstore. No local bundle.**
The three patched author dlls (WearNTear, PackHorse, FortifySkillsRedux) were folded into or replaced
by BruceQoL 1.1.0; `dist/LAN_TempFixes-*.zip` and `mods/benched-plugins/retired-temp/` are history.
Profile = BepInExPack 5.4.2350 + BetterNetworking, AzuAutoStore, AzuCraftyBoxes, Jotunn 2.30.0,
Unshamed, Endurance, BruceQoL, PlantEasily_TEMP, PlantEverything_TEMP.

| Mod | Build in play | Server | Client | Delivered by | When official lands |
|---|---|---|---|---|---|
| BetterNetworking | 2.3.2 (original) | ✔ | ✔ | profile (Thunderstore) | — works on 1.0 |
| AzuAutoStore | 3.1.2 official | ✔ | ✔ | profile (Hexium) | — |
| AzuCraftyBoxes | **1.8.18** official ("deeper fix for 1.0"; sweep clean; on server 9 Sep 16:34) | ✔ | ✔ | profile (Hexium) | — |
| Jötunn (lib) | **2.30.0 official** (9 Sep 21:49 UTC, "updated for 1.0.7"; sweep = 0 stale call sites; on server) | ✔ | ✔ | profile (Hexium/TS) — **update it in the profile, 2.29.2 is broken** | done; Jotunn_TEMP retired to `mods/benched-plugins/retired-temp/` |
| Unshamed | 1.0.0 | — | ✔ | profile (Hexium) | — |
| ~~AzuWearNTearPatches~~ | dropped 9 Sep late — BruceQoL 1.1.0 covers weather-damage toggle + per-material health/resistance | — | — | — | never re-add; it would double-patch WearNTear |
| ~~PackHorse~~ | dropped — BruceQoL 1.1.0 **Hauling** skill (same mechanic, own SkillManager skill id) | — | — | — | never re-add |
| **Endurance** 1.0.0 (our mod, GUID `bruceirons.Endurance`, replaces FoodStaminaRegen_TEMP) | source `mods/endurance-src`, MIT | ✔ | ✔ | profile (Hexium `bruceirons-team/Endurance`) | n/a — it is ours; never add Smoothbrain's StaminaRegenerationFromFood alongside it |
| ~~FortifySkillsRedux~~ | dropped — death floor not wanted; BruceQoL skills section (peak catch-up ×2 XP) + `skillreductionrate` key handle death | — | — | — | never re-add |
| PlantEasily | TEMP 2.1.1.99 (GPLv3; = Advize 2.1.1 source) | — | ✔ | Hexium `bruceirons-team/PlantEasily_TEMP` (published 9 Sep) | add Advize's build → auto-wins, then unlist ours |
| PlantEverything | TEMP 1.20.0.99 (GPLv3; zero code changes, ServerSync from source; on server) | ✔ | ✔ | Hexium `bruceirons-team/PlantEverything_TEMP` (`dist/PlantEverything_TEMP-1.20.0.zip`, source inside) | add Advize's build → auto-wins, then unlist ours |
| **BruceQoL 1.15.0** (ours, MIT, `mods/bruceqol-src`; config `bruceirons.BruceQoL.cfg`) | structures (creature 0.5 / boss 0.75 dmg, stone health 1.5, per-material damage taken, weather toggle, **wear-check throttle 10 s**), **Hauling** skill (+300 carry at 100, cart knobs), player stamina multipliers, area repair 15 m, stack mult, infinite torches (**no per-tick ZDO writes**), SmartSkills-style XP, **per-skill XP % modifiers + death penalty %** (section 8), **multiplayer scaling knobs** (section 9), **chest sizes / hover contents / station range+roof / beehives / re-equip after swim** (1.3.0), **combat multipliers + enemy/boss health** (13), **raid knobs** (14), **parry / held-block difficulty compensation** (1.8.0; server cfg: parry 1, held 0.5 so Hard blocks are judged as Normal), **commandable boars** (1.9.0, section 15: interact to follow/stay), **passive taming/breeding** (1.11.0: unloaded pens replay the vanilla loop on load, eating pen food; pen cap knob; **Off by default since 1.11.1, ServerBasedRanch does it server-side**), **skill-scaled ore/wood yield** (1.10.0, section 16: double at Pickaxes/Wood cutting 100), **bow draw tuning** (1.12.0, section 13, Off: draw-time multipliers at Bows 0 and 100, vanilla 1 / 0.2; e.g. 0.8 / 0.4 for easier early bows and tamer late ones), **dig/raise limit 12 m** (1.13.0, section 17, vanilla 8), **hoe radius x1.5 + raise step + free-raise toggle** (1.14.0, section 18), **star chance by progress** (1.15.0, section 19: out-levelled biomes roll more stars, x2 Meadows after two bosses, cap x4), **ignore cheated world keys**; 1.1.1 fixed the client Awake crash (Hauling skill vs Steam init) | ✔ | ✔ | Hexium `bruceirons-team/BruceQoL` | n/a — ours. ModRequired: clients without it are refused |

| **Oarsmen 0.1.0** (ours, MIT, `mods/oarsmen-src`; config `bruceirons.Oarsmen.cfg`) | seated players row: each Longship/Karve bench occupant adds 25% paddle force in paddle mode (4 rowers = 2x), an oar is drawn through the hull beside every occupied bench and strokes with the rudder paddle; helmsman never counts. Console `oarsmen ship / rowers / dump`. Oar pivot offsets + hole positions untuned until the first in-game session | ✔ | ✔ | Hexium `bruceirons-team/Oarsmen` (add to the profile) | n/a — ours. Not required: a client without it just sees no oars |
| **ServerBasedRanch 1.0.2** (ours, MIT, `mods/serverbasedranch-src`; config `bruceirons.ServerBasedRanch.cfg`; 1.0.2 fixed the loaded margin (32 m, was a whole zone) and the partner test (10 m radius for standing-still animals), log line now says why a pen is blocked) | **server-only**: every 30 s the server advances unloaded, tracked (tamed or once-fed) animals on their saved state with vanilla rules: eat pen food (stacks really drop), tame, love points, pregnancy, birth (offspring created as saved objects, spawn time backdated so vanilla Growup raises them on load). Loaded zones (+1 zone margin) and client-owned objects untouched. Shares the `BQ_lastSim` stamp with BruceQoL's client replay, no double count. Config: tick 30 s, feed radius 8 m, catch-up cap 12 h, **unloaded speed 50%**, pen cap 4, activity log | ✔ | — | server plugins only (Hexium `bruceirons-team/ServerBasedRanch`, NOT in the Gale profile) | n/a — ours |

Server = the same set minus client-only mods (Unshamed, PlantEasily) plus the server-only mods
(BruceNetworking when toggled on, ServerBasedRanch); update the server's `BepInEx/plugins` by hand
whenever the profile changes.

**Waiting on authors** (`mods/broken-on-1.0/`): PUP FPS 1.0.29 (closed source; the "player cloning"
bug — skip until bases are big anyway), CLLC (return-type change, closed source),
StructureDamageTweaks (unlicensed, 2024).
**Dropped by decision:** EquipmentAndQuickSlots (vanilla 1.0 pockets), AzuExtendedPlayerInventory,
Backpacks, SleepSkip.
**Benched toggle:** Serverside Simulations, ported to 1.0 (`mods/serverside-simulations`), server-only:
`tools\sss-on.bat` / `sss-off.bat` + restart.
**Benched toggle #2: BruceNetworking 0.3.0** (ours, server-only, `mods/brucenetworking-src`, README + IDEAS there; Hexium `bruceirons-team/BruceNetworking`):
(1) prefab-class ZDO send priority, (2) LAN-first zone ownership (LAN subnet > lowest ping, vanilla
active-area predicate with 12 m inner margin, 10 s cooldown; **0.3.0: moves Creature class only, never
chests/doors/stations — 0.2.1 lost a remote player's chest deposit**), (3) multi-peer send loop (vanilla services
one peer per frame), (4) RPC area-of-interest (broadcasts with a target object only go to peers within
300 m), (5) wear-tick throttle for server-owned intact pieces. Each has its own toggle. Coexists with BN.
`tools\bn-on.bat` / `bn-off.bat` + restart. Boots clean on 1.0.7; live two-peer validation pending
(force the second PC "remote" in its config and watch for `moved ownership` lines; stats line every 60 s
shows peer sends / rpc suppressed / wear ticks skipped).

## Hard rules

1. **Crossplay OFF** — it disables BepInEx. Steam players only, no PS5/Switch 2.
2. All paired mods are **mandatory on every client** (ServerSync/Jötunn kick mismatches).
3. **Exactly ONE networking mod at a time** (BN / Network / VBNT / FGN all rewrite the same
   send path — mutually incompatible).
4. BN queue size stays **32KB** (source: higher = Steam errors; 32KB already ≈256 KB/s upload
   spikes per peer). Per-player 48KB only if things *around them* lag for others.
5. Player limit lives in BN server config (`Player Limit = 10`).

## Wednesday procedure

```bash
cd /c/Users/cooki/agent-projects/valheim-modding
tools/snapshot.sh 1.0              # archive + decompile 1.0 assemblies
tools/diff-vs-baseline.sh 1.0      # removed/new/changed types = the fix list
tools/diff-vs-baseline.sh 1.0 ZRpc # deep-dive one class
```

1. Update the dedicated server (run twice if exit code 7 — SteamCMD self-update):
   `tools/steamcmd/steamcmd.exe +force_install_dir C:\Users\cooki\agent-projects\valheim-modding\server +login anonymous +app_update 896660 validate +quit`
2. **First diff checks, in order:**
   a. `ZRpc` + `ZNet` — ServerSync's shared patch targets. If changed, ALL Azumatt/Smoothbrain
      mods break at once (and one upstream ServerSync fix heals them all).
   b. `Version.m_worldGenVersion` (baseline = 2) + `WorldGenerator` — tells us whether 1.0 changed how
      seeds map to terrain (create the real world only AFTER updating).
   c. BN's targets: `ZSteamSocket`, `ZPlayFabSocket`, `ZDOMan`, `SteamNetworkingUtils`,
      `PlayFabZLibWorkQueue`.
3. Walk the breakage ranking (below). For each broken mod, check its **source of truth** (table
   above — Hexium for Azumatt/Smoothbrain, Thunderstore for the rest) for an official 1.0
   update BEFORE fixing anything ourselves. Read Iron Gate's patch notes for native networking
   improvements — some BN features may be redundant now.
4. `tools/install-mods.sh` reinstalls BepInEx + pack over both installs (idempotent).
5. Start server (`tools/start-test-server.bat`), check `server/BepInEx/LogOutput.log`:
   expect "12 plugins to load", zero `MissingMethod`/`HarmonyException`/`Could not load`.
6. Client → Join IP `127.0.0.1:2456` (pw `testpass123`) — verify join + no kick. Then build a
   wall, hit it with an axe: LogOutput.log must show "Modify damage by 0.7 due to item $item_hammer"
   (proves StructureDamageTweaks config matches). Set its `Logging Enabled = false` afterwards.
7. Build the Gale profile from the validated pack, sync, share code (see Distribution).

### Breakage-likelihood ranking (= check order)

| # | Mod | Why | Transp. |
|---|---|---|---|
| 1 | BetterNetworking | socket internals + ZDOMan/ZNet = crossplay rework epicenter; no author | 1 |
| 2 | EquipmentAndQuickSlots | 11 transpilers on Humanoid/Hud UI code | 11 |
| 3 | AzuAutoStore | 7 transpilers, Inventory/Container | 7 |
| 4 | AzuWearNTearPatches | 5 transpilers on WearNTear | 5 |
| 5 | PUP FPS | ZNetScene object lifecycle coupling | 0 |
| 6 | PlantEasily | placement UI; client-only so contained | 2 |
| 7 | Jötunn | huge surface, org-maintained, same-day fixes | — |
| 8 | StructureDamageTweaks | WearNTear.ApplyDamage/RPC_Damage/UpdateWear + Terminal; 2024 build | 0 |
| 9 | StaminaRegenFromFood | tiny stable targets | 0 |

### If BetterNetworking breaks

**Launch WITHOUT a networking mod.** At 7 players on a fresh world, vanilla is fine (the send
cap bites big bases/late game, not week one). Do NOT emergency-port. Then adopt, in preference
order, when 1.0-updated and community-vetted:

| Pref | Mod | Why / state |
|---|---|---|
| 1 | **FiresGhettoNetworking** ([Hexium](https://valheim.hexium.gg/mods/incompletionists/FiresGhettoNetworking), decomp `mods/firesghetto-decomp`) | v1.3.10 Jul 2026. Everything verified in code: ZSTD w/ self-test, adaptive per-peer send-rate loop (→2048KB/s), SSS-port server authority (default-off, credited), AI LOD, best config hygiene in genre. Both sides. Risk: biggest surface, quiet since Jul, Hexium-only |
| 2 | **Smoothbrain Network** ([repo](https://github.com/blaxxun-boop/Network), clone `mods/network-smoothbrain`) | v1.1.0 source-only (Aug 30). Send-limit removal + 2x batch + 4MB buffer + fair scheduler + join guard. **Server-only** = nothing to distribute. No compression. Wait for release |
| 3 | **VBNetTweaks** ([TS](https://thunderstore.io/c/valheim/p/VitByr/VBNetTweaks/) · [Hexium](https://valheim.hexium.gg/mods/VitByr/VBNetTweaks) · [repo](https://github.com/vitalikbyrevich/VBNetTweaks), clone `mods/vbnettweaks`) | v0.4.0 published / 0.4.1.x on GitHub, commits daily. Cap raise + priority scheduler + ShipSync. Both sides. GitHub README overclaims (NO ping-based ownership in code); TS/Hexium pages honest. Churny |
| — | BN-Lite (we write it) | Only if all three stall: port send-rate → queue → join-buffer from `mods/betternetworking` (~150 lines, MIT). Skip compression |

Last resort for a custom dll: publish to Hexium (free) under our account → rides Gale sync.

## Distribution — Gale Profile Sync

Gale 1.22.2 installed (official GitHub MSI). Friends doc: `FRIENDS.md` (paste into Discord).

- Wednesday after validation: Gale → Valheim → profile "LAN-1.0" → add pack (Gale searches
  Thunderstore AND Hexium) → Sync (Discord login) → share code. **Profile code: `UFLOAJ`** (created 9 Sep).
- Friends: install Gale → Import profile from code → Launch. Updates: you Push, they auto-Pull.
  Subscribed profiles are locked = no version drift.
- **Local mods do NOT sync** (verified in Gale source: export walks platform mods only).
  Custom dll ⇒ publish to Hexium.
- **Local imports pull NO dependencies**: importing `LAN_TempFixes` alone gives "Failed to read
  BepInEx core directory". Profile code (or Browse → `BepInExPack Valheim` 5.4.2350) must come first.
- **Mac players (no Gale on macOS):** `tools\build-mac-kit.ps1` snapshots the Gale profile into
  `dist\valheim-mac-mods.zip` (BepInEx pack's macOS launcher + every client mod + configs; server-only
  BruceNetworking excluded). Friend runs `install-mac.command`, pastes one Launch Options line into Steam
  (README-MAC.md inside). **Re-run the script and resend the zip after every profile change** — it is a
  snapshot, not a sync. Installer/README sources: `tools\mac-kit\`. **Verified on Apple Silicon 10 Sep:**
  Steam launch line must be `/usr/bin/arch -x86_64 /bin/sh "<game>/start_game_bepinex.sh" %command%`
  (arm64 Steam shell + Intel game/doorstop → "OS error 260" / "inserted dylib could not be loaded"
  without Rosetta). The installer prints the right line for the machine it runs on.
- **Hexium publishing works from the API** (9 Sep): team = `bruceirons-team`, token in
  `.secrets/hexium_token`. Test package `bruceirons-team/LAN_SyncTest` 0.0.1 (no code) went live and
  appeared in `api/v1/package-listing-chunk` (what Gale resolves) within seconds.
  **Private = invisible to Gale** (tested): after setting it private on the site, the package vanished
  from the listing chunk, `api/v1/package/`, and `api/experimental/package/...` (404) — with AND
  without the token. Only the mod page and the direct CDN zip still answer. So a private Hexium
  package cannot ride profile sync; only a PUBLIC one can. Decision: keep the local-zip route for
  patched author dlls (no public reupload of other people's code); public Hexium is an option only
  for our own mod (FoodStaminaRegen). Test package left private; delete from the site when convenient.
- Once the Gale profile is live, **delete the manual BepInEx install from the Steam client
  folder** (it loads regardless of Gale and would double-load everything). Server keeps its
  manual install — Gale manages clients only.

## Thursday launch

- [ ] Back up saves FIRST (`worlds_local/` + `characters/` in
  `C:\Users\cooki\AppData\LocalLow\IronGate\Valheim`) — 1.0 upgrades are one-way.
- [ ] Put the real world name + password into `tools/start-lan-server.bat` (keep `-public 0`);
  launch the server with it, not the test script. First start generates the world.
- [ ] Networking at the cabin: double NAT (AirBnB router + GL.iNet), no port forwarding.
  LAN players → Join IP `<this PC's GL.iNet address>:2456`. Remote players → Tailscale
  (device sharing, 3-user tailnet cap) → Join IP `<Tailscale 100.x address>:2456`.
  **The server runs on the LAPTOP, not this PC.** Full laptop procedure: `SERVER-LAPTOP.md`
  (copy `server\` + `tools\`, set world/password, Tailscale on the laptop + share link, firewall
  script as admin, launch, `tools\verify-server.ps1`). Tailscale on this desktop (`windfury`,
  100.117.178.100) is only useful if this PC ever hosts; the laptop's 100.x address is the one
  friends get. Test Thursday morning: a client on a phone hotspot joins via the laptop's 100.x.
  If `tailscale status` on the laptop shows "relay" for that peer, expect extra latency.
- [ ] Server plugin set = profile minus client-only (Unshamed, PlantEasily): AutoStore 3.1.2,
  CraftyBoxes 1.8.18, BetterNetworking 2.3.2, Endurance 1.0.0, BruceQoL 1.1.0, Jotunn 2.30.0,
  PlantEverything_TEMP 1.20.0.99, plus BruceNetworking (server-only, from the profile via
  `tools\sync-server-from-gale.ps1`). BN config: Player Limit 10, queue 32KB, 256/1024 KB/s.
- [ ] Launch line (final): `-preset Hard -modifier DeathPenalty Default -setkey "movestaminarate 85"
  -setkey "skillreductionrate 60" -setkey "playerevents"` — see `WORLD-KNOBS.md`.
- [ ] Post `FRIENDS.md` + Gale profile code + server IP/password in Discord.
- [ ] Free perf tips for the group: best-connection player enters bases first; laggy zone
  owner portals away+back to re-roll ownership; wired > wifi.

### Failure triage

| Symptom | Cause | Action |
|---|---|---|
| Kicked at join: version/incompatible | mod mismatch | friend: Gale Pull update + relaunch |
| Mobs rubber-band for LAN players / remote player hosting a shared zone | zone-owner lag | `tools\sss-on.bat` then restart the server (1.0-ported Serverside Simulations, server-only, nothing for clients). `sss-off.bat` reverts. Pair with a nightly restart |
| Everything lags, host upload maxed | send rate too high | lower BN max send rate; queue back to 32KB |
| One player teleports for others | their upload saturated | they drop BN update rate to 50% |
| Server won't start | broken mod patch | LogOutput.log names it → pull that dll → restart |

## Paths

| What | Where |
|---|---|
| Workspace / server / logs | `valheim-modding\` · `server\` · `server\BepInEx\LogOutput.log` |
| Client install + logs | `C:\Program Files (x86)\Steam\steamapps\common\Valheim` (+`\BepInEx\LogOutput.log`) |
| Saves | `C:\Users\cooki\AppData\LocalLow\IronGate\Valheim` |
| Pre-1.0 decompile / snapshots | `baseline\src\` / `snapshots\` |
| Mod zips + extracted | `downloads\` |
| Source clones & decompiles | `mods\` (betternetworking, network-smoothbrain, vbnettweaks, firesghetto-decomp, dedicatedserver-decomp) |
| Benched dlls | `mods\benched-plugins\` |

## What Iron Gate has actually confirmed for 1.0 (researched 8 Sep — no changelog published yet)

- **No public test branch** for 1.0 ("everyone gets it at the same time") → nothing to pre-validate.
- Performance: "reduced microstutters when loading zones, faster saving, minor optimisations";
  explicit caveat that **large builds will not be highly impacted**. Continued optimisation post-release.
- **Multiplayer/networking: nothing announced.** Still "1–10 players"; no dedicated-server changes
  listed. Do NOT expect BN's benefits to become redundant — plan unchanged.
- **Chunked save system** (PTB 0.221.13, May 2026): world = folder of small chunks instead of
  .db/.fwl; only dirty chunks written; Iron Gate asked testers to watch "network functionality".
  Unconfirmed whether it ships in 1.0. If it does: (a) back up saves BEFORE first launch (already
  a Thursday checklist item), (b) expect big diffs in ZoneSystem/ZDOMan/save classes — those are
  save-rework churn, not necessarily networking churn; read the diff with that in mind,
  (c) check the save directory layout before moving/copying worlds.
- Crossplay all platforms via `-crossplay` (PlayFab backend). We never pass it (BepInEx rule).
- Old worlds stay playable; Deep North generates only in unexplored areas → **fresh world Thursday**.
- Mods: "no official mod support, cannot guarantee any mods will be functional."
