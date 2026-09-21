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

**Eikthyr** (`Eikthyr`): no adds, the first boss stays a duel. Below 90% health, lightning strikes land under a player every
20 s in a normal fight, and two at a time every 12 s in a heroic one, each after a warning ring on the ground (see
Mechanics).

The biggest waves come early; the last one is a starred elite with a small escort, when the players are most worn down.

**The Elder** (`gd_king`)

| Boss health | Wave | Solo | Four players |
|---|---|---|---|
| hurt, above 25% | greydwarves every 50 s from two players, up to the living-adds cap | none | 3 |
| 75% | The forest stirs: greydwarves | 3 | 6 |
| 50% | Shamans tend their king: shamans with greydwarves | 1 + 1 | 2 + 3 |
| 25% | The wrath of the forest: a one-star greydwarf brute with an escort, a troll from three players | 1* | 1* + 2 + troll |
| below 25% | greydwarves every 30 s from two players, up to the cap | none | 3 |

**Bonemass** (`Bonemass`)

| Boss health | Wave | Solo | Four players |
|---|---|---|---|
| 75% | The dead rise from the mire: draugr | 3 | 6 |
| 50% | Archers take aim from the murk: draugr archers with draugr | 1 + 1 | 3 + 4 |
| 25% | A champion of the drowned: a one-star draugr elite with an escort, a wraith from three players | 1* | 1* + 2 + wraith |
| below 25% | draugr every 30 s from two players, up to the cap | none | 3 |

**Moder** (`Dragon`)

| Boss health | Wave | Solo | Four players |
|---|---|---|---|
| hurt, above 25% | drakes every 45 s from two players, up to the living-adds cap | none | 3 |
| 75% | Moder calls her brood: drakes, wolves from two players | 2 | 5 + 2 |
| 50% | The pack answers her call: wolves, a one-star wolf from four players | 1 | 3 + 1* |
| 25% | Her last guard descends: a Stone Golem, drakes from two players | 1 | 1 + 2 |
| below 25% | drakes every 25 s from two players, up to the cap | none | 3 |
| heroic only, while hurt | a wolf every 30 s from two players, up to the cap | none | 1 |

**Yagluth** (`GoblinKing`)

| Boss health | Wave | Solo | Four players |
|---|---|---|---|
| 80% | The last of his people answer: a Fuling pack | 5 | 8 |
| 60% | Shamans draw upon their king: shamans with a Fuling pack, archers from two players | 1 + 3 | 2 + 6 + 2 |
| 40% | A champion of the fallen cities: a Berserker with an escort, a one-star Berserker too from four players | 1 + 1 | 1 + 1* + 3 |
| 20% | They will not bend or break: one-star Fulings with an escort, an archer from three players | 1* | 3* + 2 + 1 |
| below 20% | Fulings every 20 s from two players, up to the cap | none | 3 |

These are the counts that spawn: `More adds (x)` is 1, so the scripts are taken as written (above 1, every count and
the cap are multiplied and rounded down). A threshold wave waits while more than a third of the previous one is still
alive, for up to `Next wave waits up to (s)` (30), so a group that bursts the boss through two thresholds meets the
waves one after another. Every other boss has an empty script and is left exactly as vanilla; any boss can be given one.

The repeating trickles send one add for every player beyond the first, up to three: none solo, 1 for two players, 2
for three, 3 for four or more.

Adds hunt the players, deal `Add damage (x)` (0.9) of their normal damage to players, and vanish when the boss dies;
adds killed during the fight drop their normal loot. A boss first seen already hurt (the server restarted mid-fight)
does not replay the waves it is already past. With nobody in range the fight pauses.

## Heroic fights and idols

A harder fight that the players choose, and the only one that pays idols. **Hold Shift and press Use on the boss's altar
before summoning.** It is free, so a group can take the heroic fight the first time it meets a boss. The altar's hover
text then reads "Heroic fight: the challenge is set" for everyone, and when the boss appears the server shows "The
challenge is accepted". The challenge stays on the altar until a boss takes it up, across server restarts. A server
can make it cost one of the boss's own trophies instead (`The challenge costs a trophy`), which means beating the boss
normally once first.

The older way still works: drop the boss's own trophy on the ground within 15 m of the boss before anyone hurts it; the
server takes the whole stack that was dropped. A trophy on an item stand does not count.

- the boss hits 20% harder and has 40% more health (`Boss health (x)`, on top of whatever it spawned with);
- waves and the cap are multiplied by 1.25;
- every threshold wave carries a star: where the script already stars an add in that wave, one of them gains a star (a
  one-star becomes a two-star); where it stars nothing, the first add of the wave arrives one-star, and for a wave that
  is a single heavy creature that is the heavy creature. Trickles are not starred;
- a wave can be written separately for heroic fights (see Writing a fight). Yagluth's 40% wave is: a one-star Berserker
  solo, a two-star Berserker from two players, and a plain one beside it from four;
- extra rules: frost strikes under Moder, a ward on Bonemass's champion wave, Ironhide Berserkers, fire strikes, then below 30% fire
  chases, and shifting traits at Yagluth's (see Mechanics);
- **the kill drops idols: players minus one.** None solo, 1 for two players, 2 for three, 3 for four, counted as the most
  players in range at once during the fight. Each is randomly a Battle or a Protection idol of the boss's tier: Wooden
  for Eikthyr, Bronze for the Elder, Iron for Bonemass, Silver for Moder, Black Metal for Yagluth, and so on up.

In vanilla, idols come only from treasure chests at a few per cent a chest.

## Mechanics

Waves are only part of a fight. A script rule can also do things, and several are used by the default scripts:

- **Break meter.** Bosses cannot be staggered in vanilla. Here every scripted boss has a meter under its name, filled
  by playing the fight: a parry adds a big share, clearing a whole threshold wave a quarter of the meter, every melee
  hit a share of its weight - sword, axe, mace, spear, knife or fists, so there is a reason to get up close - and damage
  of a type the boss is weak to (its own weaknesses, or its trait's) counts in full. Arrows, bolts and magic feed it
  only through a weakness. It is big: expect one or two breaks a fight. Full = **Broken**: the boss stops
  acting and takes double damage for 8 seconds, and the meter then needs half as much again. A flying boss breaks when
  it lands.
- **Ward** (`ward 0.5 break`). The boss takes half damage until every add of that wave is dead; "Warded" shows under
  its name. With `break`, the ward falling breaks the boss.
- **Guard** (`guard taken0.3 broken3 melee0.5 size0.2`). The boss takes only 30% damage until it is broken, and triple
  damage while broken. Its meter is smaller and does not grow, and melee hits fill it twice as fast, so breaks come
  round again and again: melee players break it with parries and their weapons while the ranged players deal with the adds,
  then everyone turns on it for the break. "Guarded" shows under its name. Needs the break meter; with breaks off it is
  skipped.
- **Shield** (`shield immune refresh25 range35 by:GoblinShaman`). A ward the boss's casters raise and keep up, shown
  as "Warded" under the boss's name in the ward colour (light blue; `Visual ward bubble` also puts the Fuling shaman's
  bubble on it - only the look). While it holds it swallows every hit whole. `immune`: nothing wears it down - it falls only when the last
  caster is dead. Without `immune` it breaks after `pool` (a share of the boss's health, 0.05) and pays `feed` of a break
  meter (0.5). A living caster within 35 m raises it again 25 s after it falls. Hits on a ward do not feed the break
  meter. Heroic Yagluth: his shamans make him immune until they are dead (no break when it falls); a shaman joins the fight every 40 s.
- **Ground strikes** (`strike lightning r3.5 d2 dmg20 x2`). A coloured ring fills on the ground under a player for the
  warning time, then fire, frost, lightning or poison lands there (fire as a falling meteor that touches down as the ring fills): dodge-roll through it or step out. It cannot be
  blocked.
- **Rain** (`rain fire r2.5 d1.5 over4 dmg80 each5 near12`). Rings scattered around every engaged player - five each, one
  right where they stand, the rest within 12 m - landing over 4 s. Heroic Yagluth's enrage below 30%.
- **Chase** (`chase fire r3 d1.2 dmg90 every0.9 for5`). Strikes that follow one player: a ring at their feet every 0.9 s
  for 5 s, each landing when it fills (1.2 s). Stand still and you are hit; keep moving and each lands where you were.
  Dodgeable, not blockable. `x2` chases two players.
- **Traits** (`GoblinBrute:Ironhide 1+0` on an add, `boss Emberborn`, `boss Frenzied 12` or
  `boss cycle Emberborn Stormcalled 20` on the boss). Named sets of changes, defined in the `Traits` setting:

  | Trait | Effect |
  |---|---|
  | Ironhide | resists pierce and slash |
  | Brittle | weak to blunt |
  | Stonebound | resists blunt |
  | Rimebound | hits carry 30% extra frost; resists frost, weak to fire |
  | Emberborn | hits carry 30% extra fire; resists fire, weak to frost |
  | Stormcalled | hits carry 30% extra lightning; resists lightning |
  | Blighted | hits carry 30% extra poison; resists poison |
  | Frenzied | attacks come round 1.5x as fast |
  | Fleet | moves 25% faster |
  | Renewing | heals 0.5% of its max health a second |
  | Wrathful | Frenzied, and a little faster |

  A trait that carries an element also puts the game's own aura for it on the creature - flames for Emberborn, sparks
  for Stormcalled, frost for Rimebound, smoke for Blighted - toned down (`Trait aura brightness (%)`, 60) and, on a big
  boss, spread over its body rather than made bigger. A boss taking a trait says so (`Trait messages`: "Yagluth burns").
  An add's trait is a prefix on its name; a boss's is shown under its name. A boss can shift between traits during a
  fight, and one given for a number of seconds lapses by itself. While a creature is staggered - or a boss is
  broken - its trait's resistances are off, so a stagger on an Ironhide Berserker pays out in full. Resistances show in the game's own damage colours:
  yellow for a weakness, grey for a resistance.
- `heal 5` (5% of max health), `break` (break now), `weather SnowStorm 60`, `status Wet`, `effect fx_name`.

The default scripts use them in heroic fights: frost strikes under Moder, Bonemass guarded from the start and
warded at his champion wave, Ironhide
Berserkers, fire strikes, and below 30% meteor rain and fire chases at Yagluth's, who shifts between Emberborn and Stormcalled. Eikthyr has lightning strikes in
both kinds of fight.

## Who they go after

Vanilla has no threat: every two seconds a creature turns to whoever is closest. Two rules change that, for bosses and
for this mod's adds only.

- **A parry is a taunt.** Parry any hit from a boss (bosses only) and that boss is yours for `A parry holds the boss for` (12 s; the first two or three
  pass while it is staggered). The
  latest parry wins.
- **The opening rush goes to the back line.** A melee add's first target is a player without a shield in hand, the one
  with the fewest adds sent at them so far, so a wave spreads out. The rush ends when the add comes within 4 m of any
  player (it arrived, or someone stepped in its way) or after 20 s. From then on the add is vanilla: it goes for the
  closest player, so whoever intercepted it keeps it while they stay the nearest. If everyone holds a shield there is no
  rush. Archers, casters and flyers never rush (`Adds that never rush`).

## World encounters

Away from bosses: kill enough of something at a known place and the place answers, with no message.

| Place | Trigger | What arrives |
|---|---|---|
| Fuling village (`GoblinCamp2`) | 10 Fulings, archers or shamans killed within 10 minutes, within 80 m of the village | one Fuling Berserker, hunting (a one-star Berserker instead when three or more players are there); then that village is quiet for 30 minutes |

## Hunts: the waves without the boss

A way to try a boss's waves in the open world. An admin can press **Start hunt on me** in the in-game config
menu (F1, RaidBoss, 9 - Debug) after choosing `Hunt: waves of` and `Hunt: heroic`; **Stop hunt** ends it. Or, in the
SERVER's config, section **9 - Debug**, set `Start a hunt` to the
boss's prefab name, optionally `heroic`, optionally a player's name, and save:

```
Start a hunt = GoblinKing heroic Anthony
```

It starts like a raid: the red circle on the map, on that player and following them, the raid music, and "You are
being hunted". The boss's waves then arrive around the player one after another, as in the fight - counts, stars,
traits and wave messages included - the next when the last is dead, or after `Hunt, next wave after (s)` (90). The
trickles run in between. After the last wave: "The hunt is over". `stop` ends one early. The server clears the line once
it has read it. Boss mechanics - ground strikes, wards, guards, breaks - are left out: a hunt is only the waves. A hunt
will not start while a real raid is on.

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

The client part runs on the player's own game: it gives bosses and adds a second opinion on their target (above); it
adds the Shift + Use challenge to boss altars; it keeps the break meter, applies wards and traits, and draws and lands
ground strikes; it scales the damage a player takes from a boss in a
fight (the server announces which bosses, and by how much) or from an add (the add carries its number); it reports
kills exactly to the server; it shortens the fireside wait after a death; and, when it is the one simulating a boss, it
changes that boss when the server asks (heroic health, a ward, a trait). Vanilla effects are made by the server. Wild creatures, tames, structures and the
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
- Start a rule with `normal` or `heroic` to use it only in that kind of fight: `normal 40% ...: GoblinBrute 1+0 | heroic
  40% ...: GoblinBrute** 1+0`. A `heroic` rule arrives exactly as written, without the automatic star.

World encounters (section 4) are one entry, rules separated by `|`:

```
GoblinCamp2 80m: 10 Goblin,GoblinArcher,GoblinShaman in 600s -> GoblinBrute 1+0 @1-2, GoblinBrute* 1+0 @3+, cooldown 1800s
```

Mistakes in a line are reported in the server log and the rest of the line still runs. At startup the log prints every
scripted fight worked out for one and for four players.

## Config

Only **1 - General: Enabled**, **6 - Recovery** and **7 - Targeting** are pushed from the server to the players; everything else is read
by the server alone.

- **1 - General:** `Config is locked`, `Enabled`, `Engaged range (m)` (100), `Adds hunt players`, `Remove adds when the
  boss dies`, `Boss search interval (s)` (5).
- **2 - Scaling:** `Living adds cap, base` (2) and `per player` (1), `Spawn ring, inner` (12) and `outer` (20),
  `Add damage (x)` (0.9), `Add health (x)` (1), `More adds (x)` (1.3), `Boss damage during a fight (x)` (1). The two
  damage numbers multiply with any other mod's enemy-damage setting.
- **3 - Boss fights:** one entry per boss prefab in the game (server only).
- **4 - World encounters**, **5 - Heroic fights**, **6 - Recovery:** as above.
- **7 - Targeting:** `A parry holds the boss for (seconds)` (12), `Melee adds rush players without a shield` (on), `Adds that
  never rush`. Pushed from the server.
- **8 - Mechanics:** the break meter (`Break meter size` 0.4 of the boss's max health, drain, parry and cleared-wave
  shares, melee and weakness shares, `Break length (s)` 8, `Break damage taken (x)` 2, growth 1.5, `Break meter in heroic fights` on), `Traits`, `Strike effects`, `Ward label`.
- **9 - Debug:** `Pretend this many players` (0).

## Known limits

- The server has no terrain, so heights come from the world generator, or from a nearby player standing a little higher
  (a flattened arena). An add can drop a metre or two on arrival.
- Whether adds land well inside an indoor arena is untested; those bosses' scripts are empty by default.
- 0.1.0 has been tested headless on a dedicated server. The client part - targeting, the break meter, strikes,
  traits - has not yet been through a real fight.

## License

MIT. Written for the bruceirons LAN server.
