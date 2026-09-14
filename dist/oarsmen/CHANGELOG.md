# Changelog

## 0.2.0 - 2026-09-14
- **Rowers help the helmsman turn.** New `Steering force per rower (x)` (0.15) adds turning force per
  seated rower on top of the ship's own rudder push. Rowers amplify whatever the helmsman is already
  asking for, so it does nothing with the rudder centred and there is no direction for a rower to
  choose — sitting on a bench is the whole opt-in, and no rower needs a control of their own.
- The steering share is deliberately lower than the paddle share rather than matching it. Valheim
  damps a ship's turning linearly but its speed quadratically, so the same multiplier buys far more
  turn rate than top speed. A full crew at 0.15 is roughly +40% turning force at paddle speed — the
  crew's share is diluted by vanilla's second, speed-proportional turning force, which is untouched,
  so the help is strongest when pivoting or backing off a beach and weakest at speed.
- **Rowers work in reverse.** Backing off a beach with a full crew now counts; previously only the
  forward paddle setting did. Vanilla's own reverse term is its forward term negated, so the crew's
  share is too.
- Both additions follow vanilla's gating exactly: the rudder is only given a push in paddle and
  reverse, and under sail a ship turns purely on its velocity term. Steering help is therefore never
  added under sail, including when `Row under sail` is on. Sailing is unchanged.
- **`Show rowers on the tiller`** (On) appends a `Rowers 3/4` line to the tiller's hover text, so the
  helmsman can see the crew without opening the console.
- **The crew rows in unison.** Benches previously took a random phase offset each, so the oars never
  pulled together and — because the randomness ran per client — no two players saw the same stroke.
  They now share one clock with a slight bow-to-stern ripple, identical on every machine.
- **The stroke follows the ship.** Oars reverse their drive when backing instead of rowing ahead while
  the ship moves astern, and on a hard rudder the inside bank of the turn eases off and drops into a
  back-water stroke while the outside bank keeps pulling, which is how a crew pivots a longship.
  New `Turn stroke bias (x)` (1.6) controls how sharply the banks split; 0 = both always together.
  Both banks keep one stroke frequency even when pulling opposite ways, so the crew stays in time.
- **`Simulate rowers`** (0, section 4) fills empty benches with phantom rowers so one player can test a
  full crew in single player. They row, draw oars and push the ship exactly as players would, which is
  how the oar placement and force multipliers can be tuned without four people. Real rowers count
  first, so it is a floor on the crew rather than an extra, and it only applies while somebody is
  aboard. `oarsmen rowers` marks the fake benches `SIMULATED`, and that line now also prints the
  rudder value, which is what the stroke split keys off.
- **Every boat with seats rows now, without being named in the config.** Rowability is decided by the
  hull actually having `Chair` components rather than by a whitelist, so boats from other mods —
  OdinShip's rowing canoes among them — work the moment they exist. A hull with no seats still never
  rows. **Config change:** the old `Ships` key is replaced by `Only these ships` (empty by default,
  meaning no restriction) plus a new `Excluded ships` blocklist. Both apply live, so boats already in
  the water follow an edit; previously the ship list was only read when a boat loaded. If you had
  customised `Ships`, re-enter it under `Only these ships` — the old key is ignored.
- **`Max rowers` now defaults to 0, meaning every bench counts.** It was 4, which would have left most
  of a big modded hull's seats decorative — OdinShip's warship has far more than four. Vanilla boats
  are unaffected, the Longship having only four seats in the first place. A twelve-bench ship with a
  full crew lands at about four times its own vanilla paddle force, so roughly twice the speed once
  drag answers, and still only in paddle mode.
- **Turning is capped now that crews are uncapped.** Twelve rowers at the default share would have
  reached nearly three times a hull's own rudder force, which is where the physics gets silly.
  `Max steering share (x)` (1) ceilings the crew's total contribution at double the ship's own rudder
  force regardless of headcount — a Longship's four rowers never reach it, a warship's twenty are held
  there. `Max turn rate (degrees per second)` (45) then backstops the result, removing only excess yaw
  and leaving wave roll and pitch alone; it is set clear of vanilla so it guards rather than handles.
  Both only ever remove force the crew added, so a ship rowing vanilla can never be slowed by either.
- `oarsmen ship` and `oarsmen rowers` now print live turn rate in degrees per second alongside the
  rudder value, so the cap can be set from a measurement of your own hull rather than a guess.
- Oar placement is still untuned; see the note at the top of the README.

## 0.1.1 - 2026-09-14
- Listing only, no code change: the plugin dll is byte-for-byte 0.1.0 and the `[BepInPlugin]` version
  stays 0.1.0, so nobody needs to update to stay compatible.
- New icon, and `website_url` now points at the source repo.
- Published with categories (Transportation, Vehicles, Mechanics, Valheim 1.0) — 0.1.0 went out with
  none, so it never appeared under any category filter.
- README: the untuned oar placement is called out up front rather than only in the changelog, and
  there are notes on adding modded ships and on what a client without the mod sees.

## 0.1.0 - 2026-09-14
- First release. Seated players row: each player on a Longship or Karve bench adds `Paddle force per
  rower` (0.25) of the ship's paddle force in paddle mode; an oar is drawn through the hull beside every
  occupied bench and strokes in time with the rudder paddle. Oar placement, size and stroke are
  configurable; `Oar hole positions` snaps oars to the hull's holes once measured. Console:
  `oarsmen ship`, `oarsmen rowers`, `oarsmen dump <prefab>`.
- Not yet tuned in game: the oar pivot offsets and the hole positions are first guesses.
