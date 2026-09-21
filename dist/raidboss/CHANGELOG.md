# Changelog

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
