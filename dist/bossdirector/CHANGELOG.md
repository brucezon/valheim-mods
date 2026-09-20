# Changelog

## 0.3.1

- **One damage reduction, never two.** While boss fight mode is available and on, the per-add `Add damage (x)`
  number is no longer written on adds at all: BruceQoL would have multiplied it on top of the fight-mode enemy
  damage (0.65 x 0.6). A value other than 1 is ignored with a warning in the log. It still works on a server that
  has boss fight mode switched off.

## 0.3.0

- **Boss fight mode.** With BruceQoL on the server, BossDirector changes BruceQoL's own damage settings while players
  are in range of a boss: `Enemy damage to players` to 0.6 and `Boss damage to players` to 1.0 (both settable), and
  restores them 15 s after the last player leaves or the boss dies. BruceQoL pushes synced settings to every
  player at once, so this works for everyone whatever BruceQoL version they run (the separate boss number needs
  1.19.2+; older players get the enemy number from the boss too). Held in memory only: BruceQoL's config file is
  never written, so a crash mid-fight cannot leave the server on the fight values.
- **More adds (x1.3) now simply follows boss fight mode.** The per-player marker check of 0.2.1 is gone.
- `Add damage (x)` now defaults to 1 (off): boss fight mode replaces it, and the two would stack.
- The settings are server-wide while a fight is on; players elsewhere in the world are softened too.

## 0.2.1

- **More adds is now decided per fight, per player.** 0.2.0 sent bigger waves whenever the server itself ran a
  damage-scaling BruceQoL, so a player who had not updated got more adds AND full damage. Now every engaged player
  must carry the marker BruceQoL 1.19.1+ writes on their character; one player without it and that fight runs as
  written for everyone. Checked every second, like the player count; a player between death and respawn is ignored.

## 0.2.0

- **Add damage (x)** (0.65): written on every add; players running BruceQoL 1.19.0+ take that share of the add's
  damage. Bosses and wild creatures are untouched. A player without it takes full damage.
- **More adds while their damage is scaled (x)** (1.3): when the server itself runs such a BruceQoL, every wave
  count and the living-adds cap are multiplied and rounded down (4 becomes 5, 5 becomes 6, 8 becomes 10; 1 to 3 are unchanged). Without it
  the scripts run exactly as written.
- **Add health (x)** (1): adds can arrive already hurt, so they die sooner. Needs nothing on the players' side.
- Defaults retuned from play: no trickle for one or two players, main waves 1, 2, 4, 5, 6, 8 for one to six
  players, Yagluth one Fuling per player at 80%, shamans with escorts, two one-star Fulings per two players at 20%.

## 0.1.8

- Yagluth's 20% guard is two-star again: one two-star Fuling, two from four players, plus an archer from three
  players. Deadly on purpose; write `Goblin*` in the line for a gentler last stand.

## 0.1.7

- Yagluth's shamans (60%) each arrive with a Fuling to support; alone they had nobody to cast for.

## 0.1.6

- Yagluth's 20% guard no longer has a two-star Fuling (it one-shots at that gear level): one one-star Fuling, two
  from four players, plus an archer from three players.

## 0.1.5

- Yagluth's wave messages now tell the Plains lore the game itself gives (the Fuling runestone): "The last of his
  people answer", "Shamans draw upon their king", "A champion of the fallen cities", "They will not bend or break".
- His 20% guard is led by a two-star Fuling, with one-star Fulings from two players and archers from three (one
  archer fewer than before at every group size).

## 0.1.4

- Wave messages back to the original wording of 0.1.1 ("Moder calls her brood", "The forest stirs"); the 0.1.2 and
  0.1.3 rewrites are withdrawn. Text only.

## 0.1.3

- Wave messages shortened to a few words each ("A champion of the drowned", "The mountain stands guard"). Text only.

## 0.1.2

- Wave messages rewritten to fit each biome: the old wood and its shamans for the Elder, sunken crypts and the
  drowned for Bonemass, the mother of drakes and her mountain for Moder, the fallen king and his faithful for
  Yagluth. Text only; an existing config keeps its own lines.

## 0.1.1

- Scripts for **the Elder** (greydwarves, shamans, a brute, and a troll from three players) and **Bonemass** (draugr,
  draugr archers, a draugr elite, and a wraith from three players). Both already summon in vanilla, so these are
  lighter than Moder's; Bonemass has no trickle until the last quarter.
- Moder retuned after a real five-player fight: a wolf joins the 75% brood, and the last guard is one Stone Golem
  plus drakes from three players (was Fenrings).
- Yagluth's 20% wave is a royal guard of one-star Fulings and Fuling archers (was Deathsquitos).
- Proven on a live dedicated server: every add the server created was brought to life by a client (26 of 26).
- An existing config keeps its own lines: new defaults only fill entries that do not exist yet. Delete a boss's line
  (or the file) to get the new default.

## 0.1.0

- First version. Server-only add waves at boss health thresholds, plus repeating trickle rules with a living-adds cap,
  all sized by the players in range.
- Scripts for Moder (drakes, wolves, Stone Golems) and Yagluth (Fulings, shamans, Berserkers,
  Deathsquitos). Every other boss has an empty script.
- One config line per boss, re-read live.
