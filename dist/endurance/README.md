# Endurance

Every stamina food grants **Endurance**: extra stamina regeneration per second while the food is
active. Better food gives more, but along a curve that flattens, so early foods matter and late-game
foods don't run away with it.

Default curve (square root, A = 0.196), shown as a percentage of the vanilla base regen (6/s on 1.0):

| Food stamina | 10 | 20 | 40 | 60 | 80 |
|---|---|---|---|---|---|
| Endurance | +10% | +15% | +21% | +25% | +29% |

Each food's tooltip shows its `Endurance: +X%` line, so nothing is hidden.

## Stacking

Active foods are ranked by Endurance and weighted **1, 0.5, 0.25**: your best food counts in full,
the second at half, the third at a quarter. A full belly of top food is still capped at
**3.5 regen/s** (about +60%). Both are configurable.

## Configuration

Config file: `BepInEx/config/bruceirons.Endurance.cfg`. Synced from the server (locked by default,
admins can change it live).

- **Curve**: `SquareRoot` (default), `Linear`, `Declining`, or `Saturating`, with parameters A/B/C.
- **Multiplier for all foods**: global scale after the curve.
- **Stacking weights** and **Total Endurance cap**.
- **2 - Food (scale)**: one entry per food (auto-discovered from the game's item database, so new
  foods just work). Defaults to 1.0; use it to nudge a single food without touching the curve.

Works on dedicated servers and clients. Both sides need it. Incompatible with Valheim Plus.

## Credits and license

Written for a private LAN server. MIT licensed. Bundles Smoothbrain's ServerSync and
LocalizationManager (MIT-0). The idea of food-based stamina regen comes from Smoothbrain's
StaminaRegenerationFromFood; this is a separate mod with its own GUID (`bruceirons.Endurance`),
so do not run both.
