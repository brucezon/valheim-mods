# Changelog

## 0.1.0

- First version. Everything the server-only BossDirector did (add waves at boss health thresholds scaled by the players
  in range, scripts for the Elder, Bonemass, Moder and Yagluth, world encounters, heroic fights that pay idols), now as
  one mod for the server and every client. BossDirector is retired; do not run both.
- New because the client has the mod too: damage scaling is RaidBoss's own (adds carry their multiplier, the server
  announces how hard a boss hits in a fight) and touches nothing else; kills are reported exactly instead of being
  guessed from objects vanishing; the shorter fireside wait after a death moved here from BruceQoL.
- Heroic fights are asked for at the altar: hold Shift and press Use. By default that takes one of the boss's own
  trophies; the challenge is stored on the altar and shown in its hover text. Dropping the trophy by the boss still works.
- Required on both sides. The minimum version will follow the rule "the previous release, unless a release changes the
  network messages or the object keys", so an update does not lock players out needlessly.
