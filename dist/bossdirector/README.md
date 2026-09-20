# BossDirector

Adds waves of creatures to boss fights at health thresholds, sized by how many players are there, and lets places in
the world answer when they are raided. Built for
Valheim 1.0.15.

> **Server-only. Install on the dedicated server and nowhere else.** Players need nothing, and nothing has to be
> pushed to anyone when a fight is retuned. (Ignore the "Client-only" line on this page; Hexium shows it on every
> package.) It also works for a player hosting a world from the game, which is how to test a fight with cheats.

Vanilla Moder and Yagluth fight alone, so a group gets free shots at them. The Elder and Bonemass do summon, but
nothing that scales with the group. With BossDirector:

**The Elder** (`gd_king`)

| Boss health | Wave | Solo | Four players |
|---|---|---|---|
| hurt, above 25% | one greydwarf every 50 s from three players, up to the living-adds cap | none | 1 |
| 75% | The forest stirs: greydwarves | 1 | 5 |
| 50% | Shamans tend their king: shamans, plus one greydwarf | 1 + 1 | 2 + 1 |
| 25% | The wrath of the forest: a greydwarf brute, plus a troll from three players | 1 | 1 + troll |
| below 25% | one greydwarf every 30 s from three players, up to the cap | none | 1 |

**Bonemass** (`Bonemass`)

| Boss health | Wave | Solo | Four players |
|---|---|---|---|
| 75% | The dead rise from the mire: draugr | 1 | 5 |
| 50% | Archers take aim from the murk: draugr archers, plus a one-star draugr from three players | 1 | 2 + 1* |
| 25% | A champion of the drowned: a draugr elite, plus a wraith from three players | 1 | 1 + wraith |
| below 25% | one draugr every 30 s from three players, up to the cap | none | 1 |

**Moder** (`Dragon`)

| Boss health | Wave | Solo | Four players |
|---|---|---|---|
| hurt, above 25% | one drake every 45 s from three players, up to the living-adds cap | none | 1 |
| 75% | Moder calls her brood: drakes, plus a wolf from two players | 1 | 5 + 1 |
| 50% | The pack answers her call: wolves, plus a one-star wolf from three players | 1 | 5 + 1* |
| 25% | Her last guard descends: a Stone Golem, plus two drakes from three players | 1 | 1 + 2 |
| below 25% | one drake every 25 s from three players, up to the cap | none | 1 |

**Yagluth** (`GoblinKing`)

| Boss health | Wave | Solo | Four players |
|---|---|---|---|
| 80% | The last of his people answer: one Fuling per player | 1 | 4 |
| 60% | Shamans draw upon their king: shamans, each with a Fuling from two players | 1 | 2 + 2 |
| 40% | A champion of the fallen cities: a Berserker, plus a one-star Berserker from four players | 1 | 1 + 1* |
| 20% | They will not bend or break: a Fuling, two one-star Fulings per two players, an archer from three | 1 | 1 + 4* + 1 |
| below 20% | one Fuling every 20 s from three players, up to the cap | none | 1 |

One or two players get no trickle, and their main waves are smaller than a straight line would give (1, 2, 4, 5,
6, 8 for one to six players). These are the plain counts; see `More adds while their damage is scaled` below.

Every other boss has an empty script and is left exactly as vanilla. Any boss can be given one. An existing config
keeps its own lines; a new default only fills an entry that does not exist yet.

## How it works

A dedicated server never creates a creature, so it cannot watch a fight the way a client does. It does hold every
object's data. Every few seconds the mod looks through the objects around each connected player for a new boss. One it
knows is followed by id once a second, which costs nothing: it reads the health the boss's owner writes there, and counts the players within `Engaged range`. When a rule fires it creates the adds
as bare objects in a ring around the boss and hands them to the player whose game is simulating the boss; that game
brings them to life like anything else that streams in. No Harmony patches, no client code, no custom network
messages. Wave announcements use the vanilla message everyone's game already understands.

Adds hunt the players (a setting). When the boss dies, adds still alive vanish (a setting). Adds killed during the
fight drop their normal loot. Star settings from other mods do not apply to adds: stars come from the script only.

A boss first seen already hurt (the server restarted mid-fight) does not replay the waves it is already past, and
neither does a script edited mid-fight. With nobody in range the fight pauses.

## Writing a fight

One config line per boss, in `BepInEx/config/bruceirons.BossDirector.cfg`, section **3 - Encounters**, keyed by the
boss's prefab name. **The file is re-read within a few seconds of being saved; no restart.**

```
75% "Moder calls her brood": Hatchling 1+1 | every 45s above 25%: Hatchling 1+0
```

Rules are separated by `|`. A rule is `TRIGGER "optional message": SPAWNS`.

- `75%` fires once when the boss drops to 75% health.
- `every 45s`, optionally `below 50%` and/or `above 25%`, repeats while the boss is hurt and inside that band.
  Repeating rules stop while the living-adds cap is reached; threshold waves always arrive in full.
- Spawns are comma separated: `Prefab base+perPlayer`. Count = base + perPlayer x players, rounded down.
  `Hatchling 1+1` is 2 solo and 5 with four.   with four or more. `Wolf*` is a one-star wolf, `Wolf**` two stars.

Mistakes in a line are reported in the server log at startup and the rest of the line still runs. At startup the log
also prints every scripted fight worked out for one and for four players.

## Config

**1 - General:** `Enabled` (On), `Boss search interval (s)` (5), `Engaged range (m)` (100), `Adds hunt players` (On),
`Remove adds when the boss dies` (On).

**2 - Scaling:** `Living adds cap, base` (2) and `Living adds cap, per player` (1): 3 solo, 6 with four.
`Spawn ring, inner (m)` (12), `Spawn ring, outer (m)` (20).

`More adds while their damage is scaled (x)` (1.3): while boss fight mode (below) is on, wave counts and the living-adds
cap are multiplied and rounded down, so weaker adds come in greater numbers. `Add health (x)` (1): adds arrive with this
share of their health. `Add damage (x)` (1 = off): an extra per-add multiplier for players on BruceQoL 1.19.0+; boss
fight mode replaces it. The tables above show the plain counts.

**5 - Boss fight mode** (needs BruceQoL on the server, any version since 1.5.0): `Enabled` (On),
`Enemy damage to players during a fight (x)` (0.6), `Boss damage to players during a fight (x)` (1; needs BruceQoL
1.19.2+), `Stays on after the last player leaves (seconds)` (15). While players are in range of a boss, BossDirector sets
BruceQoL's own damage settings on the server and BruceQoL pushes them to every player; afterwards they are put back.
Nothing is written to BruceQoL's config file. Server-wide while it is on. Without BruceQoL, damage is never touched
and waves stay as written.

**3 - Encounters:** one entry per boss prefab in the game.

**4 - Debug:** `Pretend this many players` (0): size every wave as if that many were fighting, so one person can see
a four-player fight.

## World encounters

Away from bosses: kill enough of something at a known place and the place answers, with no message.

| Place | Trigger | What arrives |
|---|---|---|
| Fuling village (`GoblinCamp2`) | 10 Fulings, archers or shamans killed within 10 minutes, within 80 m of the village | one Fuling Berserker, hunting; then that village is quiet for 30 minutes |

Section **6 - World encounters**: `Enabled` (On) and `Encounters`, rules separated by `|`:

```
GoblinCamp2 80m: 10 Goblin,GoblinArcher,GoblinShaman in 600s -> GoblinBrute 1+0, cooldown 1800s
```

`Location radius: kills victims in seconds -> spawns, cooldown seconds`. Spawns use the boss-wave format
(`Prefab base+perPlayer`, `*` = one star), counted from the players within 80 m of the last kill. The reinforcement
appears 14-24 m from that kill and drops its normal loot. Each location keeps its own count and cooldown. The server sees a
creature's object disappear and cannot tell a kill from a despawn; a player must be within 80 m for it to count, and
creatures this mod spawned never count. The log names how many of each location exist in the world and suggests
similar names when a rule matches none.

## Known limits

- The server has no terrain, so an add's height comes from the world generator, or from a nearby player standing a
  little higher (a flattened arena). An add can drop a metre or two on arrival.
- Whether adds land well inside the Queen's indoor arena is untested; her script is empty by default.

## License

MIT. Written for the bruceirons LAN server.
