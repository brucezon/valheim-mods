# Changelog

## 0.2.0 - 2026-09-14
- **Rowers help the helmsman turn.** New `Steering force per rower (x)` (0.25) adds turning force per
  seated rower on top of the ship's own rudder push. Rowers amplify whatever the helmsman is already
  asking for, so it does nothing with the rudder centred and there is no direction for a rower to
  choose — sitting on a bench is the whole opt-in, and no rower needs a control of their own.
- **Rowers work in reverse.** Backing off a beach with a full crew now counts; previously only the
  forward paddle setting did. Vanilla's own reverse term is its forward term negated, so the crew's
  share is too.
- Both additions follow vanilla's gating exactly: the rudder is only given a push in paddle and
  reverse, and under sail a ship turns purely on its velocity term. Steering help is therefore never
  added under sail, including when `Row under sail` is on. Sailing is unchanged.
- **`Show rowers on the tiller`** (On) appends a `Rowers 3/4` line to the tiller's hover text, so the
  helmsman can see the crew without opening the console.
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
