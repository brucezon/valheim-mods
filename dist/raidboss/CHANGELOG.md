# Changelog

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
