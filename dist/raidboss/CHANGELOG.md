# Changelog

## 0.2.0

- Warbands: a pack of a biome's creatures around a starred miniboss, camped at a spot in the open world and pinned on
  everyone's map. One per biome at a time, placed hundreds of metres from every player; the pack appears when someone
  comes within 120 m, with the raid music and circle. The miniboss fights with a boss script of its own (escort waves
  at thresholds) and wears the boss bar, but has no break meter: it staggers and takes a parry like the plain creature
  (`Minibosses have a break meter` turns it on). Killing it pays an idol of the biome's tier. A warband
  nobody comes to moves on after two hours; a biome waits 90 minutes for
  its next one; at most one stands at a time and there is a 30-minute breather after any of them ends. The pin
  carries a countdown ("Warband: Swamp (2h 58m)") and is the only announcement: no centre-screen messages unless a
  server fills the `Message` settings in. Section 11 - Warbands. Admins: warband buttons in the config menu (9 -
  Debug: near me, on me, stop all) or `Start a warband` in the server's config.
- A biome's warbands start once the boss before it is dead (`Unlocked by`: the Elder opens the Swamp, Bonemass the
  Mountain, Moder the Plains, Yagluth the Mistlands); the Meadows and Black Forest ship empty. They stop again once the world is
  two bosses past the biome (`Phase out, bosses ahead`): with Yagluth dead, no more Swamp warbands.
- The minibosses carry an element (Blighted, Rimebound, Emberborn, Stormcalled). A timed block against a starred
  RaidBoss spawn is judged as if it were unstarred (`Parry compensation for starred spawns`, 8 - Mechanics), so a
  three-star's 2.5x hit can be parried and, when it holds, leaks `Starred parry, leak (x)` (2) times what the plain creature's would; a failed parry or a held block takes the full hit. A guarded creature that can be staggered (a
  miniboss with the meter turned on) has its guard down while staggered, so the parry pays out.
- A third star: a `***` creature (level 4) gets the two-star look one size bigger and ★★★ in its name, on top of the
  game's own level formula (four times the health, two and a half times the damage). The warband minibosses are
  three-star.
- Heroic idols, off by default for both heroic kills and warband kills (`Heroic kills pay heroic idols`, `Warband
  kills pay heroic idols`): a marked idol that is certain up to level 6 and above that may break the item by a chance
  per level (20% at 7, 35% from 8) but never drops it a level. Only that idol is spent.
- Minimum version 0.2.0: the map pins and heroic idols are new for every player's game.

## 0.1.19

- New `meter` action: a boss's own break-meter numbers from its script (`meter size0.3 parry0.02 cap0.12 ...`, any field,
  unset ones follow the settings), read as the script loads. `cap` is the most of the meter a single parry may fill.
  Yagluth's script: `meter parry0.02 cap0.12` - a parry is an eighth of his meter, not a quarter, and parrying his
  attacks over and over cannot break him on its own.

## 0.1.18

- Works with RaidArena's arenas in the sky: adds (and hunt waves) spawn at the boss's height when it stands far above
  the world's ground, and the game that brings an add to life sets it down on the arena floor. Two settings under
  2 - Scaling: `Arena: a floor this far above the ground (m)` (50), `Arena: spawn adds this far above the floor (m)` (8).

## 0.1.17

- Yagluth's waves come at 90, 70, 50 and 30% (were 80, 60, 40 and 20), in both kinds of fight; heroic fire strikes
  start at 70%, and his Fuling trickle below 30%.
- Yagluth: one Fuling every 25 s from the start of the fight until 30%, solo too.

## 0.1.16

- Heroic Yagluth fights in three phases: waves, fire strikes from the shamans on, and at 30% "They will not bend or
  break" - the last stand, meteor rain, fire chases, the Fuling trickle and a thunderstorm, all at once.
- New `storm` action: thunderstorm weather and ground lightning (the Thunderstone strike, its own damage removed) around
  the boss for a while, a third of the bolts near each player; little damage, no warning ring.

## 0.1.15

- New `rain` action: rings scattered around every engaged player, one right under them, landing over a few seconds.
  Heroic Yagluth's enrage: "The sky falls" at 30%, then rain every 35 s (with the fire chase every 30 s).
- Falling meteors and impact effects are sized to their ring: a 4 m strike brings a full meteor, a 2.5 m rain ring a
  smaller one.

## 0.1.14

- Fire strikes (and fire chases) come down as one of Yagluth's own meteors, falling over the last 0.6 s and touching
  down on the very frame the ring fills and the damage lands. The meteor is a body only: it cannot hurt, burn or collide.
- Strike impact effects play on impact: built-in delays in them are squeezed (the frost nova had 1.2 s).

## 0.1.13

- New `chase` action: strikes that follow a player - a ring at their feet every 0.9 s for 5 s, so they have to keep
  moving. Heroic Yagluth, below 30%: a fire chase every 30 s (his fire strikes run from 60% to 30%).
- Minimum version 0.1.13: a chase is drawn and judged by each player's own game.

## 0.1.12

- Trait auras: a creature whose trait carries an element wears the game's own aura for it (flames, sparks, frost,
  smoke), toned down and, on a big boss, spread over the body instead of scaled up - in the manner of CLLC's infusions.
  `Trait aura brightness (%)` (60) and `Trait aura size (x)` (1) are each player's own.
- A boss taking a trait says so: `Trait messages` ("Yagluth burns", "Yagluth calls the storm").

## 0.1.11

- Waves rebuilt for every scripted boss: the biggest packs come early, the last wave is a starred elite with a small
  escort. Yagluth's waves are Fuling packs, as in their villages. Moder's wolf wave is at most three wolves and a
  one-star at four players.
- `More adds (x)` defaults to 1: the scripts are the counts that spawn (the old 1.3 is folded into them).
- Wave spacing: a threshold wave waits while more than a third of the previous one is still alive, for up to
  `Next wave waits up to (s)` (30), so bursting the boss through two thresholds no longer brings both waves at once.

## 0.1.10

- Idols fall properly: they could settle on the boss's body and be left hanging in the air once it was gone. They
  still drop where the boss fell. Minimum version 0.1.10.

## 0.1.9

- The bubble setting is now `Visual ward bubble`: it only changes the look, never the ward.

## 0.1.8

- The ward falling no longer breaks the boss: with a shaman every 40 s, one shaman was one break.
- The ward shows as a light-blue "Warded" under the boss's name; the shaman's bubble is off by default.

## 0.1.7

- Heroic Yagluth's ward makes him immune: no damage wears it down. It falls only when the last shaman is dead, and
  then he is broken. The `shield` action takes `immune` for this; hits on any ward no longer feed the break meter.

## 0.1.6

- New `shield` action: a ward the boss's casters keep up, working like the Fuling shaman's shield sized for a boss
  (swallows every hit, breaks after 5% of the boss's health, feeds the break meter when broken, fades when its casters
  are dead). Shown as the shaman's bubble, recoloured. Heroic Yagluth is warded by his shamans; a shaman joins every 40 s.
- A hunt button in the in-game config menu: choose the waves, press "Start hunt on me". Admins only.
- Minimum version 0.1.6.

## 0.1.5

- A staggered creature, or a broken boss, has its guard down: a trait's resistances (Ironhide's pierce and slash, and
  the rest) do not apply while it is staggered, so the stagger pays out in full. Weaknesses still apply.

## 0.1.4

- Hunts: a boss's add waves in the open world, around a player, with no boss - started from the server's config
  (`Start a hunt` = `GoblinKing heroic Anthony`). Looks like a raid: map circle following the player, raid music,
  "You are being hunted". For trying the waves out.

## 0.1.3

- Ground strikes: the impact now lands exactly when the ring fills. Strike effects are played by each player's own game,
  timed to its ring, instead of being made by the server. The lightning strike no longer flashes when the ring appears.

## 0.1.2

- Every melee hit feeds the break meter by its weight (setting `Break meter, melee hits count (x)`, 0.25), not only
  blunt ones: a reason for melee to get up close. Arrows, bolts and magic feed it only through a weakness.
- `guard` takes `melee0.5` (was `blunt0.5`): melee hits fill a guarded boss's meter at twice the usual share.
- Minimum version 0.1.2, for the same reason as 0.1.1.

## 0.1.1

- The break meter fills from the fight, not from raw weapon weight: parries (twice as much as before), cleared waves
  (a quarter of the meter) and damage of a type the boss is weak to count; plain weapon stagger counts a tenth. Two new
  settings: `Break meter, weapon stagger counts (x)` (0.1) and `Break meter, weakness damage counts (x)` (1).
- New `guard` action: the boss takes 30% damage until broken and triple while broken, with a smaller meter that blunt
  damage fills too. Heroic Bonemass is guarded from the start.
- New setting `Break meter in heroic fights` (on): off = no breaks in heroic fights.
- Minimum version 0.1.1: the meter is kept by whichever player's game runs the boss, so every player needs this one.

## 0.1.0

- First version. Everything the server-only BossDirector did (add waves at boss health thresholds scaled by the players
  in range, scripts for the Elder, Bonemass, Moder and Yagluth, world encounters, heroic fights that pay idols), now as
  one mod for the server and every client. BossDirector is retired; do not run both.
- New because the client has the mod too: damage scaling is RaidBoss's own (adds carry their multiplier, the server
  announces how hard a boss hits in a fight) and touches nothing else; kills are reported exactly instead of being
  guessed from objects vanishing; the shorter fireside wait after a death moved here from BruceQoL.
- Heroic fights are asked for at the altar: hold Shift and press Use. Free by default (a server setting can make it cost
  one of the boss's own trophies); the challenge is stored on the altar and shown in its hover text. Dropping the trophy
  by the boss still works.
- A heroic boss hits 20% harder (a single boss can be given its own number) and has 40% more health; every threshold
  wave carries a starred add; a wave can be written separately for heroic fights with the `heroic` and `normal` rule
  markers, as Yagluth's 40% wave is.
- Targeting: parrying a boss's hit makes that boss yours for 12 seconds; the opening rush of melee adds goes
  to players without a shield and spreads over them; once an add has arrived or been intercepted it is vanilla again.
- Mechanics for scripts: a break meter on every scripted boss (one or two breaks a fight: the boss stops and takes
  double damage), wards, telegraphed ground strikes, and traits (Ironhide, Emberborn, Frenzied and more) that adds carry
  and bosses can shift between. Eikthyr gets lightning strikes; heroic Moder, Bonemass and Yagluth use the rest.
- Trickles grow with the group: one add for every player beyond the first, up to three.
- Required on both sides. The minimum version will follow the rule "the previous release, unless a release changes the
  network messages or the object keys", so an update does not lock players out needlessly.
