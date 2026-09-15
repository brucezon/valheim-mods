# Changelog

## 0.3.2 - 2026-09-15
- **The blade drops into the water instead of snapping in.** The entry was run inside the drive alone,
  over a fifth of it - 9% of a stroke, about a fifth of a second at 26 a minute - and the recovery
  contributed nothing to it, so the oar came forward clear of the water and then slapped in at the catch.
  The entry is now centred on the catch and the extraction on the finish, each spread over the new
  **`Catch blend (share of stroke)`** (0.16): the blade starts dropping through the last of the recovery
  and is buried a little way into the drive, and comes out the same way around the finish. The feather
  follows the same blend, so the blade squares up as it goes in and turns flat as it comes out.
  Capped at the shorter of the drive and the recovery so the two blends can never overlap.

## 0.3.1 - 2026-09-15
- **Beds are not benches.** `Hold-fast seat animations` now defaults to
  `attach_mast,attach_dragon,attach_bed`. OdinShip's Big Cargo Ship carries a bed ('Cama') that is a
  Chair with the `attach_bed` animation, so 0.3.0 counted it as a fifth rowing bench and gave it an oar;
  the ship now rows with its four stool seats forward, which is what the hull was built for. Existing
  config files keep their old value: add `attach_bed` to the list by hand (or delete the line to take
  the new default) if you already have a `bruceirons.Oarsmen.cfg`.

## 0.3.0 - 2026-09-14
- **Only rowing benches row.** A seat is not the same thing as a bench, and the Longship has seven
  Chairs: the four benches, the helm seat, and two places a passenger holds fast — the mast and the
  figurehead. 0.2.0 took every one of them, so a four-oared boat rowed with seven oars, one of them
  out at the dragon head above head height, and a crew of seven pushed a hull built for four. Vanilla
  tells them apart by the animation it puts the player into (`attach_sitship` on the benches,
  `attach_mast` and `attach_dragon` where you are hanging on), and the mod now reads the same thing.
- **The helmsman never gets an oar.** The helm seat is excluded by position rather than by name: the
  chair sharing the tiller's attach point is the helmsman's, whoever wrote it and whatever it is
  called. On the Longship the two sit 8 cm apart. Its animation is `attach_chair`, an ordinary seat
  animation a modded hull may well use for a real bench, so matching on that would have cost other
  boats their crews.
- New **`Hold-fast seat animations`** (`attach_mast,attach_dragon`) and **`Rowing seat animations`**
  (empty = every seat that is not the helm or a hold-fast point). The first is the blocklist, the
  second an optional allowlist — set it to `attach_sitship` to restrict rowing to vanilla ship benches
  exactly. Both apply live: a hull already in the water rebuilds its benches when you edit them.
- **Oar placement is measured now, not guessed.** `Pivot outward` 0.75 → **0.6**, `Pivot up` 0.55 →
  **0.26**, `Pivot forward` 0 → **0.19**, taken off the Longship in game. The 0.1.0 note about the
  offsets being first guesses is retired for that hull; the Karve has not been checked.
- **Stowed oars lie along the hull.** An oar whose rower is not pulling used to stand straight out to
  the side, which is not what a crew does with an oar it is not using. New **`Stowed oars`**
  (`AlongHull` default, `Outboard` for the old look) ships them fore and aft, blades aft. The turn
  runs on the same blend the stroke fades on, so the oar swings in as the crew eases off rather than
  flicking round, and `Stowed angle` still sets how far it is lifted clear of the water.
- **`Snap oars to the hull`** (Off): places the pivot on the hull's own side, read off the row of box
  colliders vanilla builds the sides from, instead of at a fixed offset from the bench. One offset
  cannot fit a hull that tapers — the Longship's side stands 2.4 m from the keel amidships and 1.6 m
  at the forward benches, while its benches are inset 1.5 and 0.8 — so this is the lever for a modded
  boat whose oars come out in the wrong place. `Oar hole inset from the hull` nudges the result.
  Off by default because the measured offsets above already fit the vanilla hulls.
- **The crew rows at a crew's pace.** New **`Stroke rate (strokes per minute)`** (26). The stroke was
  previously hard-coded to the rate vanilla wiggles the steering paddle at, sin(t × 6), which is 57
  strokes a minute — a racing sprint rather than a crew moving a loaded longship. Nothing depended on
  the two agreeing: the paddle is vanilla's own animation on a different part of the boat, and the
  crew's force is flat rather than stroke-timed, so the rate is now purely a matter of how it looks.
  The bow-to-stern ripple is held in radians of the stroke rather than seconds, so it keeps its shape
  at any rate.
- **`Blade dip`** 22 → **35 degrees**, so the blades reach the water. The oar holes sit well above the
  waterline and a shallower stroke had the crew rowing air just above the surface.
- **The stroke is a stroke now, not a sine.** The oar used to swing on `sin`, with the blade's depth
  keyed separately to where in that arc it had got to. Two things were wrong with that. The depth should
  follow which way the oar is *travelling* rather than where it has reached — a quarter-cycle out, so
  the blade was buried at the two ends of the swing where the oar is momentarily stopped and lifted
  through the middle where it moves fastest, and the square-up and feather landed mid-sweep instead of
  at the catch and the finish. And a sine is never still, so even with that corrected the oar waves
  rather than rows.
  The stroke is now built as two eased halves of one cycle — a drive and a recovery — so the oar comes
  to rest at the catch and at the finish, and **the blade being buried and the oar driving are the same
  interval by construction** rather than two curves that have to be held a quarter-cycle apart. That
  relationship is what has been wrong since 0.1.0, twice over; it is no longer possible to get it wrong.
  New **`Drive share of the stroke`** (0.45) sets how much of the cycle is the loaded half: a crew pulls
  hard and comes forward at more leisure, so it sits below half.
- **Fix: the inside bank no longer ships its oars mid-turn.** How hard a bank pulled was `|power|`, and
  because a bank's power crosses zero on its way to negative, feeding in rudder took the inside bank's
  stroke down to nothing — and nothing is what ships an oar, so that bank stowed itself along the hull
  during the turn and then came back out backing water. Only the *direction* comes off the rudder now:
  both banks row a full stroke throughout and the inside one simply rows it backwards, easing across
  rather than flipping. A deadband keeps a rudder held near the turnover point from fluttering the bank
  between ahead and astern.
- **New `Recovery lift (degrees)`** (22): how far the blade rises above the drive angle to clear the
  water on the way forward. `Blade dip` puts the blade in, this takes it out; previously the recovery
  angle was hard-coded at 35% of the dip.
- **Blades feather on the recovery.** New **`Feather angle (degrees)`** (90, 0 = off): the blade turns
  flat as it leaves the water and squares up again at the catch, rolled about the oar's own axis.
- **Fix: the blade no longer jolts twice a stroke.** The pitch was switched between the drive angle and
  the feathered one the instant the swing crossed centre — 35 degrees to 12 in a single frame, at the
  fastest point of the stroke, on every oar on the boat. It now turns over the last part of each
  half-stroke, the way a blade squares up at the catch and feathers at the finish. Nothing to do with
  the simulated crew: real rowers got exactly the same jolt, there were just never four of them to
  watch at once.
- **Oars ship themselves smoothly from wherever they were.** Stowing was eased only when the crew's
  bite ran out under sail; every other way of ending a stroke — the helmsman dropping to Stop, the sail
  going up, a rower standing — set the stowed pose outright, so the oar teleported into it. Each oar
  now carries its own stow blend, eased over the new **`Stow time (seconds)`** (0.8), and the swing, the
  blade angle and the turn along the hull all hang off that one number. The stroke clock keeps running
  while anyone is still easing off, so a crew told to stop finishes the stroke it is in instead of
  freezing mid-swing and then rotating.
- An oar whose rower has stood up stays drawn until it has finished shipping itself, rather than
  vanishing out of the air mid-stroke. It comes back the same way: a rower sitting down swings their
  oar out from stowed instead of having it appear already pulling.
- **Fix: the simulated crew no longer stutters.** Vanilla's onboard trigger flickers — walk the deck
  near the rail and it reports nobody aboard for a frame or two — and the phantom rowers were gated
  directly on it, so oars and force dropped out and came back with it. The gate now holds for a
  second after the last player is reported off the boat, which rides out the flicker while still
  stopping the phantoms when you actually step ashore.
- **Fix: `Simulate rowers` no longer throws in Configuration Manager.** With no range on the setting
  it was drawn as a free text box and every keystroke that was not a whole number threw a
  `FormatException` out of the config UI. It has a range (0–32) and a slider now.
- `Simulate rowers` also fills benches that are actually benches, so a Longship at 4 now puts its
  phantoms on the four rowing benches rather than on the figurehead, both forward benches and the mast.

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
- **Fix: the crew no longer rows a boat that has left the water.** Vanilla applies all of its forces —
  buoyancy, damping, paddle, rudder — inside one gate, and that gate is the hull still being in the
  water; lift a boat off a wave crest and vanilla stops pushing entirely. Oarsmen's force is added in a
  postfix, which does not inherit that gate, so in heavy seas the crew kept rowing through mid-air —
  and along the hull's forward axis, which points at the sky when the bow is pitched up, so they were
  rowing it higher. The same five-sample water test vanilla uses now gates the crew's force too.
- **`Row under sail` is no longer a flat bonus.** An oar only bites while the blade is moving through
  the water faster than the hull is, so the crew's contribution now falls away as the square of the
  speed they have left — `(1 - speed/V)²` — reaching nothing at the new `Speed where oars stop helping (m/s)`
  (5). A full crew pulls its whole weight becalmed, about a third of it at 2 m/s, and nothing by 4.
  Rowing gets you out of a calm or a foul wind and is futile once the sail is really pulling, which
  makes the setting a way to get under way rather than a flat speed increase. Squared rather than
  linear because that is what a blade does, and because linear left a big crew still usefully rowing
  at cruising speed. Paddle mode and reverse are unaffected and keep vanilla's own flat paddle force.
- **The oars ship themselves as the sail takes over.** With `Row under sail` on, the crew used to keep
  stroking at full sweep however fast the ship was going, long after they had stopped contributing
  anything. The stroke now shortens and the blades lift progressively as the crew's bite runs out —
  full strokes becalmed, easing off through 2–3 m/s, held clear of the water by about 4 — driven by the
  same curve as the force, so what you see is what the ship is getting. The lift is a blend rather than
  a threshold, so nothing snaps.
- The same blend improves hard turns: the bank a rudder has cancelled now lifts its oars smoothly
  instead of jumping to the stowed angle.
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
