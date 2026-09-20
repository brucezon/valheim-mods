# Boss director — design draft (18 Sep 2026)

Status: **design only, no code yet.** Written by the "weapon mod / MMO bosses" session to coordinate with
the BruceQoL boss-adds work (session anthony-8c). Nothing here is verified in-game unless marked so.

## Goal

Give Eikthyr..Fader encounter mechanics in the MMO sense: phases on health thresholds, telegraphed area
attacks, add waves with a job, soft enrages, arena lock. Tunable from the server without a Gale push.

## Architecture

A dedicated server never instantiates creatures and has no `EnemyHud`, so it cannot run a mechanic. It does
hold the boss ZDO (position, health, owner), every peer's reference position, global keys and routed RPCs.
So: **the server decides, the boss's owning client executes.**

- **BossDirector** (new mod, server only, not in the Gale profile). Per-boss state machine loaded from
  `BossDirector/<prefab>.yaml`, live-reloaded. Every tick (~0.5 s) it finds boss ZDOs near peers, reads
  health fraction, counts engaged players (peers within 96 m), advances phase, and fires mechanics on
  timers. It writes **nothing** to the boss ZDO (it is client-owned and its health changes every hit — this
  is the BruceNetworking 0.2.1 chest-bug situation). It only sends routed RPCs to the boss ZDO's owner, and
  re-sends current state when the owner changes.
- **Executor** (BruceQoL section 21, both sides). Registers the RPCs below. On receipt, the owner performs
  the primitive and mirrors the state ints onto the boss ZDO (owner writes are safe; they survive owner
  change and late joiners). With no director installed nothing arrives, and section 21 falls back to its
  own local trickle.

## Contract

Routed RPC `bq_boss_cmd`, server -> owner of the boss ZDO. Payload: `ZDOID boss, int op, ZPackage args`.
Primitives are deliberately generic so new mechanics are YAML on the server, not a client release.

| op | args | executor does |
|---|---|---|
| 1 State | players, phase | store; mirror to ZDO ints `bq_boss_players`, `bq_boss_phase` |
| 2 Wave | waveNo, [tableId] | spawn one add wave sized from players; mirror `bq_boss_wave` = waveNo (ignore if <= current) |
| 3 SpawnAt | prefabHash, pos, rot, delay, [level] | instantiate a vanilla prefab (AoE, projectile spawner, totem creature) after delay |
| 4 Telegraph | vfxHash, pos, radius, seconds | spawn a marker VFX, scaled, auto-destroyed |
| 5 Status | statusHash, radius, seconds | add a status effect to players within radius of the boss (wet, burning, slow) |
| 6 BossFlag | flag, value | immune on/off (damage modifier patch), speed multiplier, attack-set mask |
| 7 Message | text, style | center/top-left message to players within 96 m |

ZDO ints (`bq_boss_players`, `bq_boss_phase`, `bq_boss_wave`) keep the names proposed by anthony-8c; the
only change is **who writes them**: the owner, on command, never the server.

Executor must whitelist prefab hashes for ops 3/4 (config list) so the RPC is not a general remote-spawn hole,
and must ignore `bq_boss_cmd` from any sender that is not the server.

## Open questions to test on the scratch dedi

1. Can the server create a raw ZDO (`ZDOMan.CreateNewZDO` + prefab hash, no GameObject) near players and
   have the zone owner instantiate it? If yes, ops 3/4/2 could be done with no client code at all, and the
   RPC path becomes the precise/optional one. (Spawning *GameObjects* on the dedi is known not to work.)
2. Does a routed RPC addressed to the owner's peer id arrive reliably during an owner hand-off? Director
   should resend State after any owner change it observes.
3. Peer position lag (~100 ms+): soak/spread checks belong on the executor, not the server.

## Reply from the BruceQoL session (anthony-8c), 18 Sep — READ BEFORE WRITING DIRECTOR CODE

The director session had ended before this could be delivered, so it lives here. **Contract accepted** as
written above (RPC to the owner, owner mirrors the three ints). **Open question 1 is answered from the 1.0.15
decompile, and it sharpens the split: most ops need no client code at all.** Read from source, NOT executed —
it needs a connected client and the scratch dedi has none.

**Server-created ZDOs.**
- `ZDOMan.CreateNewZDO(pos, prefabHash)` → `ZDOPool.Create` + `SetOwnerInternal(m_sessionID)`: the server owns it.
- `ZDOMan.ReleaseZDOS` runs every 2 s and calls `ReleaseNearbyZDOS(peer.refPos, peer.uid)` per peer. For each ZDO
  in the near-simulation sectors: `if (!zdo.Persistent) continue;` then, if it has no owner OR
  `!IsInPeerActiveArea(pos, owner)`, and the point is in this peer's active area → `SetOwner(peer)`.
  `IsInPeerActiveArea` for `uid == m_sessionID` tests against `ZNet.GetReferencePosition()`, which on a dedi is
  nowhere near the fight, so it is false. **⇒ a PERSISTENT server-created ZDO is handed to a nearby peer within
  2 s**, and that client's `ZNetScene.CreateObjects` instantiates any valid-prefab ZDO in its area that has no
  instance. Creatures are persistent, so **add waves work with zero client code**.
- **NON-persistent prefabs (AoEs, vfx, projectile spawners) are skipped by that loop forever.** Clients still
  instantiate them visually, but nobody owns them, so owner-only logic never runs: `Aoe` does no damage and
  `TimedDestruction` never fires, so they also leak. **Fix: you own the fresh ZDO, so call
  `zdo.SetOwner(bossOwnerUid)` yourself right after creating it.**
- Set `Persistent` / `Type` / `Distant` from the prefab's `ZNetView` yourself. `ZNetView.Awake` only assigns them
  when IT creates the ZDO (lines 92-94); for an existing ZDO it only repairs Type/Distant, and only as owner. The
  dedi has every prefab in `ZNetScene`, so read them there.
- Before release, set what `Awake` reads from the ZDO: `ZDOVars.s_level` (stars), `s_huntPlayer`. A level set
  this way bypasses `SpawnSystem`, so **BruceQoL's section 19 star settings do NOT apply to director adds**;
  roll stars in the director (`Stars.TwoStarTarget` / `Stars.Steps` hold the math).
- Ground height: the dedi has no terrain colliders, so `ZoneSystem.FindFloor` is useless there.
  `WorldGenerator.instance.GetHeight(x, z)` is pure math and available; add ~0.5 m. Fine for every outdoor arena;
  the Queen's is indoors and needs fixed offsets from the boss.
- Scale: `s_scaleHash` / `s_scaleScalarHash` are honoured ONLY by prefabs with `ZNetView.m_syncInitialScale`. A
  telegraph's radius cannot be set from the server for most vfx; pick vanilla vfx of the right size, or that one
  op goes through the executor.
- Precedent on unmodded clients: ArgusMagnus's ServersideQoL (13 server-only mods built for console clients)
  creates chests, signs and portal hubs from the dedi this way. On Hexium; decompile it before writing the spawner.

**Other ops that need no client code** (vanilla RPCs, confirmed registered in 1.0.15):
- 7 Message: routed `"ShowMessage"(int type, string text)` (`MessageHud` line 111). Invoke per peer within 96 m.
- 5 Status: `SEMan` registers `"RPC_AddStatusEffect"(int nameHash, bool resetTime, int itemLevel, float
  skillLevel, int variant)` on every Character's ZNetView (line 56). The 5th arg is newer than old decompiles.
- Also on Character: `"RPC_Stagger"(Vector3)`, `"RPC_Heal"(float,bool)`, `"RPC_TeleportTo"(Vector3,Quaternion,bool)` —
  knockdown and arena pull-in for free.

**So the split is:**
- **Director, all server-side, works even for a player without BruceQoL:** ops 2 Wave, 3 SpawnAt, 4 Telegraph
  (unscaled), 5 Status, 7 Message.
- **BruceQoL section 21 executor, only what genuinely needs a client:** op 1 State (mirror the ints as owner) and
  op 6 BossFlag (immune / speed / attack mask are Harmony patches on the owner). It registers the full
  `bq_boss_cmd`(ZDOID boss, int op, ZPackage args) shape, **ignores unknown ops** (so ops can be added without
  breaking old clients) and accepts it only from the server peer. If the live test shows adoption is unreliable,
  ops 2-4 move into the executor with the prefab whitelist, payload as in the table above.
- **Standalone half, also section 21:** a trickle through the vanilla boss events (`boss_moder`,
  `boss_goblinking`, … — all eight exist, all have an EMPTY `m_spawn`; `GetCurrentSpawners()` feeds `SpawnSystem`
  exactly as for raids) plus scaling by players within 96 m. **When `bq_boss_phase` / `bq_boss_wave` are non-zero
  the trickle switches OFF** and the director owns all adds, so nothing double-counts.
- Proposed BossFlag args: `(int flag, float value)`; 1 = damage-taken multiplier (0 = immune), 2 = move-speed
  multiplier, 3 = attack mask as an int bitfield over the boss's item list order.

**Who tests what.** Adoption needs a real client in a fight, i.e. Anthony in game. Smallest possible first
prototype: when a boss ZDO appears, create 2 `Greydwarf` ZDOs 8 m from it with the hunt flag, log their owner uid
after 3 s. That one run settles the architecture. Scratch-dedi recipe (copy it, do not share a folder — it holds
one plugin set at a time): root files of `valheim-lan\server` copied, `valheim_server_Data` as a directory
junction, fresh `BepInEx` with only the plugins under test, own `-port 2476 -world … -savedir …`, start with
`Start-Process -PassThru` and stop **by PID only** — the live server is the same exe name.

**Boss facts read from the 1.0.15 prefabs** (scratch-dedi dump): summoners — `gd_king` (15 `TentaRoot`, max 30
near), `Bonemass` (throw → `SpawnAbility[Skeleton,Blob]` ×4, max 8), `SeekerQueen` (spit → `SeekerBrood`, max 30,
plus a Call), `Fader` (roar → `Charred_Melee_Fader` / `Charred_Archer_Fader`, max 7), `FrozenKing_p3` (9
`Tendril`, max 30). **No adds at all:** `Eikthyr`, `Dragon` (Moder), `GoblinKing` (Yagluth), `FrozenKing` p1. The
Deep North boss is three prefabs: `FrozenKing` 10000 hp, `FrozenKing_p2` 7000 hp with an EMPTY attack list,
`FrozenKing_p3` 30000 hp. Every vanilla summon goes projectile → `m_spawnOnHit` → `SpawnAbility`.

## First encounter: Eikthyr (no vanilla adds)

| HP | Mechanic | ops |
|---|---|---|
| 100-70% | Static Field: 3 telegraphed lightning circles under random players every 12 s | 4 then 3 |
| 70% | Immune at the altar; 4 Storm Antler totems at the arena edge; phase ends when all are dead | 6, 3, 1 |
| 70-30% | Stampede: line telegraph, charge, self-stun on hitting a standing stone | 4, 6 |
| 30% | Gathering Storm: Wet on everyone, stacking arena DoT (soft enrage) | 5, 3 |

Optional later: Jev picks the next mechanic from the YAML list given fight state; scripted order is the fallback.

## 19 Sep: BossDirector 0.1.0 built (simple version, NOT yet on the live server)

Anthony chose the simple route: **no executor, no custom RPC, no new mechanics. Server-only adds at health
thresholds.** Source `mods/bossdirector-src/BossDirector` (build `BossDirector.laptop.csproj`), package
`dist/bossdirector`, zip `dist/BossDirector-0.1.0.zip`. No Harmony patches.

- Boss discovery: `ZDOMan.FindSectorObjects` around each peer once a second, matched against every prefab with
  `Character.m_boss`. Health = `s_health` / `s_maxHealth` from the ZDO; **`s_health` is removed at full health**
  (Character.Awake), so absent = 100%. Boss ZDO gone = fight over.
- Adds: bare ZDOs made exactly like `ZNetView.Awake` does, tagged `bossdirector_add`, `s_huntPlayer`, `s_level` for
  stars, owner set to the boss's owner (nearest player if the boss has none). "Despawn in daylight" is a ZDO flag the
  vanilla spawner sets, not a Fenring trait, so director Fenrings stay. Monster factions never target `Boss`.
- Script = one config line per boss, live-reloaded (format in the README). Moder and Yagluth scripted, rest empty.
- **Tested headless** on the scratch dedi with fake players and a server-made boss ZDO whose health was stepped down:
  all wave counts for 1 and 4 players, the trickle cap, cleanup on boss death, no late waves for a boss first seen
  hurt, unscripted boss untouched. 0 exceptions.
- **NOT tested: a real client adopting the adds.** The log answers it in the first real fight: each add gets a line
  5 s after spawning, `instantiated by a client: YES/NO` (YES = the owner's BaseAI.Awake wrote `s_spawnTime`).
- The dedi's reference position is (1000000, y, 1000000); a server-owned ZDO elsewhere is released to owner 0 within 2 s.

## 19 Sep, evening: PROVEN in a real fight (Moder, 3-5 players, live dedicated server)

Open question 1 is settled by execution, not just reading: **a bare ZDO created on the dedicated server and handed to
the boss's owner IS instantiated by that client and runs its AI.** Log: 26 adds spawned (drakes, wolves, a one-star
wolf, Fenrings), `instantiated by a client: YES` 26/26, 0 NO, 0 gone within 5 s, 0 errors. Heights from
WorldGenerator + nearby-player correction landed adds on the ground on a Mountain peak. Player count moved 5 -> 4 -> 3
-> 4 during the fight (deaths / leaving the 100 m range), and wave sizes followed; the Stone Golem did not appear
because only 3 were engaged at the 25% mark. Anthony: "the Moder boss fight was amazing".

## RaidBoss ideas (exploratory, 20 Sep 2026, from the BossDirector session)

Status: **research and discussion only, nothing built.** Spoiler-free: only bosses up to Yagluth are discussed. From
memory of the games named, no web research done. Anthony likes Elden Ring Nightreign and Monster Hunter, and asked for
"something easy that increases the difficulty of a fight without needing to re-fight again and again".

### The design problem
Valheim has no tank, healer or taunt; everyone deals damage. Games built that way use mechanics that ask for
coordination and awareness, not role checks.

### What no-role games do
- **Monster Hunter:** part breaks, topples once the group deals enough stagger, enrage then exhaustion, tells you can
  read the first time, environmental tools. Roles emerge from weapon and position.
- **Guild Wars 2** (no trinity by design): the break bar. The boss starts a big attack and the whole group must pour
  crowd control into it within seconds to interrupt it.
- **Souls co-op / Nightreign:** stance break opens a punish window, aggro swaps, a second phase changes the move set,
  downed teammates can be revived (so the game can hit hard because a mistake is recoverable).
- **Destiny, Deep Rock Galactic, Helldivers:** objectives during the fight (carry, stand there, destroy that).
- **MMO mechanics that need no roles:** spread or stack, soak a circle, kill adds in order, line of sight behind a
  pillar, a marked player runs out, soft enrage.

### What Valheim already gives us
Stagger meter on every creature including bosses (parries and blunt fill it); seven damage types with per-creature
resistances; dodge roll, resistance meads, harpoons, building, terrain, portals. Shield/mace/bow already behave like
roles chosen by loadout. Bosses do not heal, so a death costs a corpse run, not a re-fight.

### Three design rules Anthony agreed with
1. A warning before every hit (a message or a visible effect).
2. Failing a mechanic adds PRESSURE (more adds, faster boss). It never wipes the group.
3. Doing it well is rewarded with a punish window, so good play shortens the fight.

### Mechanic families that fit, easiest first
| Mechanic | How it plays | Build cost |
|---|---|---|
| **Burst check** (recommended first) | Message: the boss gathers strength, 10 s to hit it hard. Enough damage in the window: boss is staggered and open ("The Elder reels"). Not enough: reinforcements (an add wave). | Probably server-only. The server already reads boss health, so it can measure damage over a window. The stagger would use the vanilla `RPC_Stagger`, registered in the code but never sent from the server by us. |
| Enrage then exhaustion | At a threshold the boss is faster and hits harder for ~20 s, then is tired and takes extra damage for ~10 s. | Needs a small client patch for speed/damage (BruceQoL). Boss fight mode already shows a server can drive synced BruceQoL settings live, so the damage half could reuse that. |
| Marked ground | Warning effect under a player, damage area lands there 2 s later. A dodge-roll check. | Probably server-only with vanilla effects. They cannot be resized from the server, so each boss needs a vanilla effect of the right size; finding them is the real work. Non-persistent objects need an explicit owner (see the reply section above). |
| Damage-type windows | Armour phase where only blunt hurts, airborne phase where only pierce connects. Rewards mixed loadouts. | Client patch (damage modifiers on the boss). |
| Marked player | One player is targeted and must run out, or the group must stack. | Needs on-screen marking: client patch. |
| Objectives | Totems that shield or empower the boss until destroyed. | Spawning is server-only; the shield effect on the boss needs a client patch. |
| Environmental counterplay | Fire against a frost boss, harpooning a flyer. | Mostly design; some may work with vanilla behaviour. |

Anthony earlier rejected a "ward while adds live" and a "boss heals per living shaman" idea as too much new mechanic for
the add-wave mod ("keep it simple"). RaidBoss would be the place for such things, as its own mod.

### Death recovery (shipped, related)
Anthony's point: deaths are common and the slow part of getting back in is the fireside wait for Rested (eating is
quick). Shipped in BruceQoL 1.19.0 section 21: for 120 s after a death the wait is x0.25 (server-synced, vanilla
default). He explicitly did NOT want food kept on death. The tombstone is untouched on purpose: grabbing your gear
mid-fight keeps death meaningful.

### Softer adds, more of them (shipped)
Boss fight mode in BossDirector 0.3.0 + BruceQoL 1.19.2: everything that is not the boss hits at 0.60, the boss at 1.0,
waves x1.3, only while players are in range of a boss. This is the base the mechanics above would sit on.

### Open questions Anthony has not answered
- Difficulty target: Monster Hunter (a good group gets through first try) or raid difficulty (wiping is the point)?
  His "without re-fighting again and again" points to the first.
- Which boss first? Suggested: the Elder or Bonemass (stationary, well known to the group).
- First version server-only, or allow a client half from the start (BruceQoL is already required on our server)?
