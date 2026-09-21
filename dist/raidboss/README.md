# RaidBoss

Boss fights built for groups: waves of adds sized by how many players are there, harder re-fights you choose that pay
idols, places in the world that answer a raid, and a quicker return to the fight after a death. Built for Valheim
1.0.15.

> **Install on the server AND on every client.** A client without it is refused by the server, and a client with it
> cannot join a server without it. (Hexium shows "Client-only" on every package; ignore it.) The server makes every
> decision; the client part is small and exists for the few things a server cannot do.

Replaces the server-only **BossDirector**, which is retired. Do not run both.

## Boss fights

Vanilla Moder and Yagluth fight alone, so a group gets free shots at them. The Elder and Bonemass do summon, but
nothing that scales with the group. With RaidBoss:

**The Elder** (`gd_king`)

| Boss health | Wave | Solo | Four players |
|---|---|---|---|
| hurt, above 25% | greydwarves every 50 s from two players, up to the living-adds cap | none | 2 |
| 75% | The forest stirs: greydwarves | 1 | 5 |
| 50% | Shamans tend their king: shamans, plus one greydwarf | 1 + 1 | 2 + 1 |
| 25% | The wrath of the forest: a greydwarf brute, plus a troll from three players | 1 | 1 + troll |
| below 25% | greydwarves every 30 s from two players, up to the cap | none | 2 |

**Bonemass** (`Bonemass`)

| Boss health | Wave | Solo | Four players |
|---|---|---|---|
| 75% | The dead rise from the mire: draugr | 1 | 5 |
| 50% | Archers take aim from the murk: draugr archers, plus a one-star draugr from three players | 1 | 2 + 1* |
| 25% | A champion of the drowned: a draugr elite, plus a wraith from three players | 1 | 1 + wraith |
| below 25% | draugr every 30 s from two players, up to the cap | none | 2 |

**Moder** (`Dragon`)

| Boss health | Wave | Solo | Four players |
|---|---|---|---|
| hurt, above 25% | drakes every 45 s from two players, up to the living-adds cap | none | 2 |
| 75% | Moder calls her brood: drakes, plus a wolf from two players | 1 | 5 + 1 |
| 50% | The pack answers her call: wolves, plus a one-star wolf from three players | 1 | 5 + 1* |
| 25% | Her last guard descends: a Stone Golem, plus two drakes from three players | 1 | 1 + 2 |
| below 25% | drakes every 25 s from two players, up to the cap | none | 2 |

**Yagluth** (`GoblinKing`)

| Boss health | Wave | Solo | Four players |
|---|---|---|---|
| 80% | The last of his people answer: one Fuling per player | 1 | 4 |
| 60% | Shamans draw upon their king: shamans, each with a Fuling from two players | 1 | 2 + 2 |
| 40% | A champion of the fallen cities: a Berserker, plus a one-star Berserker from four players | 1 | 1 + 1* |
| 20% | They will not bend or break: a Fuling, two one-star Fulings per two players, an archer from three | 1 | 1 + 4* + 1 |
| below 20% | Fulings every 20 s from two players, up to the cap | none | 2 |

These are the counts as written. `More adds (x)` (1.3) then multiplies every count and the cap and rounds down: 4
becomes 5, 5 becomes 6, 8 becomes 10, and 1 to 3 stay as they are. Every other boss has an empty script and is left
exactly as vanilla; any boss can be given one.

Adds hunt the players, deal `Add damage (x)` (0.9) of their normal damage to players, and vanish when the boss dies;
adds killed during the fight drop their normal loot. A boss first seen already hurt (the server restarted mid-fight)
does not replay the waves it is already past. With nobody in range the fight pauses.

## Heroic fights and idols

A harder fight that the players choose, and the only one that pays idols. **Hold Shift and press Use on the boss's altar
before summoning.** It takes one of that boss's own trophies from your inventory (a server setting; it can be free), the
altar's hover text then reads "Heroic fight: the challenge is set" for everyone, and when the boss appears the server
shows "The challenge is accepted". A first kill can never be heroic, because nobody has the trophy yet. The challenge
stays on the altar until a boss takes it up, across server restarts.

The older way still works: drop the boss's own trophy on the ground within 15 m of the boss before anyone hurts it; the
server takes the whole stack that was dropped. A trophy on an item stand does not count.

- the boss hits 20% harder;
- waves and the cap are multiplied again by 1.25 (about 1.6 in all);
- a quarter of the plain adds arrive with one star (never two);
- **the kill drops idols: players minus one.** None solo, 1 for two players, 2 for three, 3 for four, counted as the most
  players in range at once during the fight. Each is randomly a Battle or a Protection idol of the boss's tier: Wooden
  for Eikthyr, Bronze for the Elder, Iron for Bonemass, Silver for Moder, Black Metal for Yagluth, and so on up.

In vanilla, idols come only from treasure chests at a few per cent a chest.

## World encounters

Away from bosses: kill enough of something at a known place and the place answers, with no message.

| Place | Trigger | What arrives |
|---|---|---|
| Fuling village (`GoblinCamp2`) | 10 Fulings, archers or shamans killed within 10 minutes, within 80 m of the village | one Fuling Berserker, hunting (a one-star Berserker instead when three or more players are there); then that village is quiet for 30 minutes |

## Recovery

Dying in a hard fight costs a corpse run, your food and a wait by the fire. For `Counts as just died for` (120 s) after a
death, the fireside wait before Rested arrives is multiplied by `Resting time after a death` (0.25). Food, the tombstone
and the length of the buff are untouched.

## How it works

A dedicated server never creates a creature, so it cannot watch a fight the way a client does. It does hold every
object's data. Every few seconds it looks through the objects around each connected player for a new boss; one it knows
is followed by id once a second. When a rule fires it creates the adds as bare objects in a ring around the boss and hands
them to the player whose game is simulating the boss; that game brings them to life like anything else that streams in.
Idols are made the same way. Wave announcements use the vanilla centre-screen message.

The client part does four things, all on the player's own game: it adds the Shift + Use challenge to boss altars; it scales the damage a player takes from a boss in a
fight (the server announces which bosses, and by how much) or from an add (the add carries its number); it reports
kills exactly to the server; and it shortens the fireside wait after a death. Wild creatures, tames, structures and the
settings of other mods are never touched.

## Writing a fight

One config line per boss, in the SERVER's `BepInEx/config/bruceirons.RaidBoss.cfg`, section **3 - Boss fights**, keyed by
the boss's prefab name. **The file is re-read within a few seconds of being saved; no restart.**

```
75% "Moder calls her brood": Hatchling 1+1 | every 45s above 25%: Hatchling 1+0
```

Rules are separated by `|`. A rule is `TRIGGER "optional message": SPAWNS`.

- `75%` fires once when the boss drops to 75% health.
- `every 45s`, optionally `below 50%` and/or `above 25%`, repeats while the boss is hurt and inside that band.
  Repeating rules stop while the living-adds cap is reached; threshold waves always arrive in full.
- Spawns are comma separated: `Prefab base+perPlayer`. Count = base + perPlayer x players, rounded down.
  `Hatchling 1+1` is 2 solo and 5 with four. `StoneGolem 0+0.25` only appears with four or more. `Wolf*` is a one-star
  wolf, `Wolf**` two stars. Add `@1-2`, `@3+` or `@4` after an entry to use it only for that many players, so one
  creature can replace another as the group grows: `GoblinBrute 1+0 @1-2, GoblinBrute* 1+0 @3+`.

World encounters (section 4) are one entry, rules separated by `|`:

```
GoblinCamp2 80m: 10 Goblin,GoblinArcher,GoblinShaman in 600s -> GoblinBrute 1+0 @1-2, GoblinBrute* 1+0 @3+, cooldown 1800s
```

Mistakes in a line are reported in the server log and the rest of the line still runs. At startup the log prints every
scripted fight worked out for one and for four players.

## Config

Only **1 - General: Enabled** and **6 - Recovery** are pushed from the server to the players; everything else is read
by the server alone.

- **1 - General:** `Config is locked`, `Enabled`, `Engaged range (m)` (100), `Adds hunt players`, `Remove adds when the
  boss dies`, `Boss search interval (s)` (5).
- **2 - Scaling:** `Living adds cap, base` (2) and `per player` (1), `Spawn ring, inner` (12) and `outer` (20),
  `Add damage (x)` (0.9), `Add health (x)` (1), `More adds (x)` (1.3), `Boss damage during a fight (x)` (1). The two
  damage numbers multiply with any other mod's enemy-damage setting.
- **3 - Boss fights:** one entry per boss prefab in the game (server only).
- **4 - World encounters**, **5 - Heroic fights**, **6 - Recovery:** as above.
- **7 - Debug:** `Pretend this many players` (0).

## Known limits

- The server has no terrain, so heights come from the world generator, or from a nearby player standing a little higher
  (a flattened arena). An add can drop a metre or two on arrival.
- Whether adds land well inside an indoor arena is untested; those bosses' scripts are empty by default.
- 0.1.0 has been tested headless on a dedicated server. The client part has not yet been through a real fight.

## License

MIT. Written for the bruceirons LAN server.
