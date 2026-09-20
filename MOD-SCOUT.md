# Mod scout — Thunderstore since the 1.0 release

Survey of every Valheim package on Thunderstore touched since 1.0 shipped (Wed 9 Sep 2026).
Method: `tools/mod-scout.pl 2026-09-09` over the Thunderstore v1 package index, then READMEs of the
interesting ones. Rerun the script for the next scout and append a dated section below.
Companion docs: `OUR-MODS.md` (what we run), `mods/brucenetworking-src/IDEAS.md` (networking survey, 9 Sep).

---

## 16 Sep 2026 (index pulled 15:10)

| Count | |
|---|---|
| 11,313 | packages in the index |
| 1,700 | updated since 9 Sep |
| 757 | **created** since 9 Sep |
| 268 | of those 757 carry Thunderstore's new **AI Generated** tag (35 %) |

### Our profile: who moved

| Mod we run | Update since 1.0 | Note |
|---|---|---|
| Jotunn | 2.30.0 (9 Sep) | the 1.0 build; `ReefTeam-Jotunn` 2.29.3 fork is deprecated, ignore it |
| AzuAutoStore | 3.1.4 (14 Sep) | |
| AzuCraftyBoxes | 1.8.19 (14 Sep) | `fedorovdgap-AzuCraftyBoxes` fork now deprecated |
| Unshamed | 1.0.5 (13 Sep) | still the most-downloaded achievement unblocker (57 k); rivals: AchievementEnabler, NoCheatText, CheaterBypass |
| **PlantEverything** | **Advize 1.21.2 (12 Sep)** | **official 1.0 build. Our `PlantEverything_TEMP` (GPLv3 rebuild of 1.20.1) can be retired** |
| **PlantEasily** | **Advize 2.2.0 (11 Sep)** | **same — retire `PlantEasily_TEMP` (2.1.1)** |
| FortifySkillsRedux | 1.6.0 (15 Sep) | (WORLD-KNOBS: it overrides `SkillReductionRate`) |
| BetterNetworking | — | no release since 2.3.2 (Nov 2023). Still loads clean per README, but see NetworkPerformanceSystem below |
| Official_BepInEx_ConfigurationManager | — | shudnal's `ConfigurationManager` 1.1.21 (15 Sep) is the maintained alternative: config-file editor, split view |
| blacks7ar **Endurance** 1.1.3 (14 Sep) | — | not ours, but same display name as our Endurance. Players searching Thunderstore will find his. Worth a note in FRIENDS.md if anyone installs by hand |

Our bruceirons packages are on Hexium, not Thunderstore, so they do not appear in the index.

### Install candidates (test on `modtest` first)

Ranked by expected value to *our* server: 7 players, some remote over Tailscale, server-only preferred.

1. **MidnightMods / ValheimCommunityPatch 0.27.1** (new 9 Sep, 16 k dl, Jotunn) — "vanilla bug fixes and
   performance fixes, no gameplay changes", ~60 fixes each tagged Server / Client / Both. Server-only install is
   supported and gets the server subset: portal-pairing tag index instead of full rescans, indexed disconnect ZDO
   sweep, background zone pacing by frame time, **teleport ghost players fix**, **unsaved client changes marked
   for world save**, duplicate-ZDO-id tolerance on load, log-spam redirects (`Failed to send data`, container
   lines). Both-sides adds terrain seam fixes, grass rebuild spreading, zone collider baking off-thread.
   Overlaps: "Fix Idle Wear Visits" + "Fix Idle Support Checks" **cover BruceNetworking IDEAS #3** (WearNTear
   server skip) — drop that from our list if this goes in. Runs its rewrites after other mods and checks whether a
   method is already fixed. Jotunn refuses the join on major/minor mismatch, so profile push needed for the
   both-sides variant. **Action: server-only trial now; both-sides at the next profile push.**

2. **MidnightMods / NetworkPerformanceSystem 1.5.0** (new 9 Sep, 5.8 k dl) — built for "groups spread across
   continents", i.e. exactly our Tailscale players. Server-only works with vanilla clients; client install adds
   dead-reckoning latency compensation and a 12-byte live-position channel. What it does is our roadmap:
   per-peer send window sized by bandwidth-delay product from measured RTT (vanilla: fixed 10 KB); a send scheduler
   that services **every** peer each tick (**= IDEAS #1**, the README quotes vanilla at ~5.5 Hz with ten players,
   matching our `SendZDOToPeers2` reading); relay filtering so the host only forwards to peers with the object in
   view (**= IDEAS #2**); ownership of moving objects placed by measured latency with hysteresis, structures left
   vanilla to avoid item-loss races. **Declared incompatible with BetterNetworking, FGN, VBNetTweaks, SkadiNet,
   NetworkTweaks.** It will also fight BruceNetworking's own ownership steering. **Action: decompile survey
   (same treatment as FGN/VBNet in IDEAS.md), then decide: replace BetterNetworking + BruceNetworking with it, or
   borrow the BDP window + hysteresis into BruceNetworking.** Do not run both.

3. **MidnightMods / ProgressivePowers 0.3.3** (new, 9.4 k dl) — Forsaken powers become permanent passives with 7
   mastery levels per power unlocked by boss kills; up to 8 attuned at once (config). Server-authoritative YAML,
   live-synced, no restart. Keys progression on bosses defeated like our Stars section, so the two reinforce each
   other. Low risk, both sides. **Action: propose to the group; needs a profile push.**

4. **ArgusMagnus / ServersideQoL_* 2.0.11** (13 server-only mods, all updated 9 Sep) — designed for vanilla and
   console clients, so nothing on our profile changes. Pieces that add something we lack:
   `TameAssist` (tames follow through portals and into dungeons, taming/growth progress shown),
   `JustSleep` (sleep vote when one player is in bed), `AutoMapTables` (map tables auto-pin portals, ships, ore),
   `CreatureLevelUp` (max creature level rises with world progression — same idea as our Stars, check for
   double-scaling), `MultiplayerTweaks` (ship owner = captain, smelters/stations owned by nearest player, mobs owned
   by closest player, forced map pins). Pieces that duplicate BruceQoL and should NOT be installed: `ContainerSizes`,
   `PrefabConfigurator`, `Player` (infinite stamina), `Skills`. **Action: TameAssist + JustSleep server-only trial.
   And decompile the family once — it is the reference for writing ZDOs the server does not own (the ranch mod's
   accepted limit).**

5. **ZenDragon / ZenRaids 1.2.0** (updated) — replaces workbench spawn-suppression with *lit fire* perimeters
   (visible with the hammer; wood 2.5 days, coal 10, resin longest), raid triggers on global + per-player keys,
   per-biome raid allow-lists (Meadows raid-free by default), min-players-online to raid. Overlaps BruceQoL §14
   (both touch `RandEventSystem`). **Action: idea source, not install — see borrow list.**

### Ideas to borrow into our mods

- **Safe Stamina** (Cartur, new, 2.4 k dl): zero stamina cost for chop/mine/sprint/jump/swim/sneak/build when no
  `BaseAI.IsEnemy` creature is within 25 m, scan cached 0.25 s, tames ignored, unaware enemies still count.
  Client-side per player. → **BruceQoL §3 Player toggle**, cheaper grind without touching combat numbers.
- **Ownership by latency with hysteresis** (NetworkPerformanceSystem) → BruceNetworking ownership steering. The
  hysteresis is the part we do not have; ours flips on distance alone.
- **Server-only pattern for client-visible features** (ServersideQoL): tame follow-through-portals, sleep vote,
  container sizes all done from the dedi with vanilla clients. If it works the way the READMEs imply, the ranch
  mod's "never write to a ZDO a client may own" rule has a known workaround worth understanding before we widen
  anything.
- **Least-progressed-online-player cap** (jg224 / WorldStageDirector 0.5.2): out-of-biome enemies and random raids
  across the whole server are capped by the least-progressed player online; native-biome spawns, bosses, dungeons
  unaffected. Boss credit is personal: damage the boss + be in range for trophy/loot reserved to you, then offer the
  trophy yourself for the key. **Correction 16 Sep:** our world already runs the vanilla `PlayerEvents` key
  (Player based raids), which keys raids on the per-player boss keys of the players *at the raid location* — the
  same idea, and better for a mixed group because one low-progress player online does not suppress everyone's
  raids. WSD only adds the out-of-biome spawn cap and the personal boss-credit system. Not needed. Related:
  CommunityPatch fix 51 "Share Boss Defeat Keys" (client) fixes the vanilla bug where only the boss owner's client
  reliably gets the per-player key, which matters for `PlayerEvents` fairness.
- **Light-perimeter spawn suppression + per-biome raid lists** (ZenRaids) → BruceQoL §14 additions.
- **YAML creature clones with AI presets and rolled modifiers** (sighsorry / CreatureManager 1.1.14): `clonedFrom`
  a vanilla prefab, `copyFrom` AI presets, 32 combat modifiers in 4 groups (max one per group per creature), regional
  Karma that spawns "Enforcer" boss hunts per 3×3 zone block, server-side PNG texture sync by SHA-256. This is the
  **custom-enemy path that needs no Unity project** — clone + configure. Also a worked example of a server-
  authoritative director syncing assets to clients.
- **Event feed for the chronicle / Discord steward** (Proudlock / GsValheimStatsEmitter 0.2.6, server-only):
  patches `Character.RPC_Damage` + `Character.OnDeath`, polls global keys, POSTs JSON snapshots to an HTTP endpoint
  every 120 s (cumulative counters persisted to TSV). Note the stated limit: the dedi only sees damage on
  **server-simulated** creatures, so per-player boss damage needs its optional client mod. Borrow the shape, not
  the mod.
- **Discord webhook content** (TaegukGaming / Valheim_ServerGuard 1.8.1): join/leave/death/**shouts**/raids with
  the in-game raid name + coordinates + status, daily summary to an admin channel, CSV of build/destroy events for
  grief forensics. Requires a client plugin + shared secret, which we do not want; the webhook side is ~50 lines in
  a server-only mod.
- **Server-authoritative admin actions on a dedicated server** (Skitale / ValheimAdminForge 0.4.1, new 10 Sep):
  spawn items/creatures with stars/tame/health, teleport players, set time/weather/wind, toggle spawns; every action
  authorised server-side against `adminlist.txt` with hard caps. Both sides. Directly addresses "admins cannot
  spawn/tame/god on a dedi". Cheat-flag behaviour undocumented — test on `modtest` with an achievements character
  before trusting it.
- **An LLM companion already exists** (RuneFellowship 0.4.44): companions + rideable direwolf in the plugin;
  voice + AI dialogue via a separate desktop sidecar (local / hybrid / ChatGPT). Thunderstore refused to host the
  installer (unauditable downloads, AV flags unresolved). Lesson for our NPC idea: keep the model call on the
  server, ship nothing extra to clients. Other companion mods since 1.0: jg224 DynamicNPCs (mercenaries),
  Lost_Scrolls_II (recruit dvergr), AshValheimMod, Oathbound (classes + biome sieges), MidgardPlus (companions that
  level, 16 k dl).

### Trend notes

- **AI-generated mods are a third of new releases** and Thunderstore now tags them. Quality is uneven; several are
  "narrow compat patches" for abandoned mods (`BackpacksValheim1Compat`, `AzuAutoStoreCompat`). Prefer the original
  author's 1.0 build when one exists — which is exactly the PlantEverything/PlantEasily situation above.
- **Unofficial "1.0 fork" rebuilds** (fedorovdgap, TeamNibake, ValheimPortMods, PONEIS) fill gaps for OdinPlus
  TeleportEverything, aedenthorn's CraftFromContainers, SmartContainers. Two are already deprecated after the
  upstream author shipped. Check `is_deprecated` before recommending any of them.
- **MidnightMods** is the most productive new publisher (CommunityPatch, NetworkPerformanceSystem, ProgressivePowers,
  AchievementEnabler, ImpactfulSkills 0.16.1, DvergerAutomation, ValheimArmory 1.31). Worth watching as a set.
- **Networking is crowded**: NetworkPerformanceSystem, SkadiNet 1.1.5, LeanNet, NetworkTweaks 0.2.0, FGN 1.4.31 all
  updated. Every one of them declares the others incompatible. We run BetterNetworking + BruceNetworking; pick one
  stack deliberately.
- **Crafting-from-chests** is the most duplicated new category (StoreAndCraft 24 k, CraftFromChests 24 k,
  SmartCraftStorage, CraftFromContainers 4.0, NearbyCrafting, ChestFlow, GorilaChestMod, DadsEZContainers…). We have
  AzuCraftyBoxes; nothing here beats it.
- **Sailing**: P377Y Smooth_Sailing (server-synced wind modes), Tommys_Ship_Speed (per-ship speeds, **multiplayer
  rower bonuses**), shudnal LongshipUpgrades 1.0.20. Tommys overlaps Oarsmen — glance at its rower bonus formula.
- **Deep North**: no dedicated Deep North gameplay mods yet beyond category tags; `NoHeavySnow`/`AllHeavySnow` keys
  (WORLD-KNOBS) remain the only snow controls. Seasonality 3.8.3 and Spawn_That / Drop_That / WackysDatabase are
  updated for 1.0 if we ever want data-driven spawns instead of code.

### Action list (short)

1. Swap `PlantEverything_TEMP` → Advize PlantEverything 1.21.2 and `PlantEasily_TEMP` → Advize PlantEasily 2.2.0 at
   the next profile push; delete our rebuilds from Hexium afterwards.
2. Server-only trial: ValheimCommunityPatch, ServersideQoL_TameAssist, ServersideQoL_JustSleep on `modtest`.
3. Decompile survey of NetworkPerformanceSystem 1.5.0 → decision on the BetterNetworking + BruceNetworking stack.
4. BruceQoL §3: Safe Stamina toggle. §14: light-perimeter suppression, per-biome raid list, min-players.
5. Propose ProgressivePowers to the group.
