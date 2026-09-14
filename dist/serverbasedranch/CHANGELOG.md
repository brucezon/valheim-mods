# Changelog

## 1.0.2 - 2026-09-13
- Fix: the "loaded" margin was a whole zone (64 m) in every direction, so a pen within ~200 m of where a
  player stood was skipped by the server without the client loading it either. Now the game's own active
  area plus 32 m.
- Fix: the partner test used vanilla's 3 m, which vanilla re-checks while the animals wander. Unloaded
  animals stand still, so two boars 4 m apart never bred. New **Partner radius while unloaded** (10 m).
- The activity line now says why animals were blocked (loaded, hungry without food in reach, no partner,
  pen full) and counts love points and conceptions. **Log every sweep** (off) logs it even when idle.

## 1.0.1 - 2026-09-12
- New **Unloaded speed (%)** (50): unloaded pens progress at this fraction of real time.
- Safety pass: a zone counts as loaded one zone earlier than the game's own rule (player positions reach
  the server a moment late); food in a loaded zone or owned by a connected client is never touched (pen
  straddling a zone edge); newborns are placed at the higher of the mother's height and the base terrain
  height, plus a small lift, so they cannot spawn inside a slope. One bad animal no longer aborts a sweep.

## 1.0.0 - 2026-09-12
- First release. Server-side ticking of unloaded pens: eat from the pen, tame, love points, pregnancy,
  birth (offspring created as saved objects with backdated spawn time), vanilla numbers throughout.
  Loaded zones are left to the player's client. Config: tick interval, feed radius, catch-up limit,
  pen cap, activity log.
