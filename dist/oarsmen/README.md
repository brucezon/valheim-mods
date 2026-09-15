# Oarsmen

Seated players row the ship. Built for Valheim 1.0.7.

> **Install on the dedicated server and on every client.** (Ignore the "Client-only" line on this
> page — Hexium shows that on every package, including server-only ones.) The mod is not required:
> a client without it simply sees no oars.
>
> **Still untuned.** Oars row and push the ship correctly, but where each oar sits — the pivot
> offsets and the hull's oar-hole positions — are first guesses that have not been checked in game
> yet. Expect to nudge `Pivot outward/up/forward` for your ship. `oarsmen ship` prints what you are
> standing on to help.

Vanilla ships paddle with one fixed force no matter who is aboard, and the Longship's benches and oar
holes are decoration. With Oarsmen, every player sitting on one of the ship's benches is a rower:

- **In paddle mode** (the slow setting, sail furled) each rower adds **Paddle force per rower** of the
  ship's paddle force. Default 0.25, so a full Longship crew of four paddles with twice the force.
  Speed rises less than force, because the water pushes back harder the faster you go.
- **In reverse** the same crew backs the same way — useful getting off a beach with a loaded ship.
- **Rowers help the helmsman turn.** Each one adds **Steering force per rower** (0.15) of the ship's
  own rudder force, so a crewed ship comes round faster than a single sailor could bring it. The crew
  amplifies whatever the helmsman is asking for rather than steering on their own: with the rudder
  centred it does nothing, and there is no direction for a rower to pick. **Nobody needs a control
  of their own — sitting on a bench is the whole opt-in, and standing up is how you stop.**
  The help is strongest when pivoting or backing off a beach and fades as you pick up speed, because
  vanilla's other turning force grows with speed and the crew does not scale it.
- **An oar appears through the hull beside every occupied bench.** The crew pulls in unison, with a
  slight bow-to-stern ripple, and the stroke follows what the ship is doing: oars drive astern to push
  you ahead, reverse their drive when you are backing, and on a hard rudder the inside bank of the turn
  eases off and backs water while the outside bank keeps pulling — how a crew pivots a longship. Sail
  out or ship stopped: the oars are held level and still. Bench empty: no oar.
- The helmsman at the tiller is not a rower. Someone has to steer; the rest sit down and pull. The
  tiller's hover text shows `Rowers 3/4` so the helmsman can see who is actually pulling.

**Sailing is untouched.** Vanilla only gives the rudder a push in paddle and reverse — under sail a
ship turns on its speed through the water, and that is left exactly as it was. The crew helps you
paddle and manoeuvre; it does not help you tack.

**If you do switch on `Row under sail`, the oars fade out as the ship picks up speed.** A blade only
bites while it is moving through the water faster than the hull is, so the crew's help falls away as
the square of the speed they have left, and is worth nothing at all above `Rowing cuts out above`
(5 m/s). In practice a full crew pulls its whole weight when becalmed, about a third of it at 2 m/s,
and nothing by 4 — rowing gets you out of a calm or a foul wind, and is futile once the sail is really
pulling. Paddle mode and reverse are not affected and keep their flat force, matching vanilla's own.

**The oars ship themselves as the sail takes over.** The stroke shortens and the blades lift out of
the water on the same curve as the force, so the crew is never seen thrashing away at a speed where
they are achieving nothing: full strokes becalmed, easing off through 2–3 m/s, held clear by about 4.
It is a blend rather than a switch, so nothing snaps. The same thing happens to the bank a hard rudder
has cancelled — those oars come up instead of pretending to pull.

**Every boat with seats rows — nothing needs listing in the config.** The mod looks for `Chair`
components on the hull, so the Longship (4 benches) and Karve (2) work out of the box and so do boats
from other mods, OdinShip's rowing canoes included, the moment they exist. A hull with no seats never
rows, because there is nowhere to sit. The tiller is not a seat in this sense — it is a different
component — so the helmsman is never counted as a rower.

**A big hull rewards a big crew.** Every bench counts by default, so a warship with twelve seats is
pulled by twelve rowers, not by the first four. The gain is proportional rather than absolute — the
crew's share multiplies that hull's own paddle force — so a full twelve puts a warship at about four
times its vanilla paddle force, which is roughly twice the speed once the water fights back. It is
still paddle mode only; the sail is untouched. Cap it with `Max rowers` if a modded ship turns out
too quick, though lowering `Paddle force per rower` is the gentler lever.

**Turning is capped, so a huge crew cannot throw the boat around.** Two guards, both of which only
ever remove force the crew added — a ship rowing vanilla is never touched by either. `Max steering
share` ceilings the crew's total contribution at double the hull's own rudder force, so twenty rowers
turn no harder than twelve; a Longship's four never reach it. `Max turn rate` then limits how fast the
bow may actually swing, taking off only the excess yaw and leaving roll and pitch from the waves
alone. Run `oarsmen ship` while turning hard to read your hull's real turn rate before changing it.

Use `Excluded ships` to opt a boat out, and `Only these ships` to restrict rowing to a named few.
Both take comma-separated prefab names and both apply live, so a boat already in the water follows the
change. `oarsmen dump <prefab>` shows whether a hull has chairs at all.

Install on the server and on every client. A client without the mod sees no oars, and if that client
happens to own the ship (it is the one simulating it) the ship rows at vanilla speed; nothing breaks.
Config is server-synced and live.

## Config (`BepInEx/config/bruceirons.Oarsmen.cfg`)

**2 - Rowing:** `Rowing` (On), `Only these ships` (empty = every boat with seats),
`Excluded ships` (empty), `Paddle force per rower (x)` (0.25),
`Steering force per rower (x)` (0.15, set 0 to turn the steering help off), `Max steering share (x)` (1 = the crew can at most double the ship's own rudder force),
`Max turn rate (degrees per second)` (45, a backstop on yaw only), `Max rowers` (0 = every bench counts),
`Row under sail` (Off: rowers only add forward force in paddle mode and reverse; steering help is
never added under sail either way), `Rowing cuts out above (m/s)` (5),
`Show rowers on the tiller` (On).

**3 - Oars:** `Show oars` (On), oar and blade dimensions, and where the oar pivots relative to its
bench: `Pivot outward`, `Pivot up`, `Pivot forward` (metres). `Oar hole positions`: comma-separated
ship-local Z positions of the hull's oar holes; when set, each oar snaps to the closest hole instead
of sitting beside its bench. `Stroke sweep`, `Blade dip`, `Stowed angle` (degrees), and
`Turn stroke bias (x)` (1.6) for how sharply a turn splits the two banks — 0 = both always row
together. All of section 3 is cosmetic; none of it changes how the ship moves.

**4 - Debug:** `Simulate rowers` (0) — **testing aid for single player.** Pretend at least this many
benches are manned, filled bow to stern, so one person can see and feel a full crew: the phantoms row,
draw oars and push the ship exactly as players would. Real rowers count first, so it is a floor on the
crew rather than an extra. Only applies while somebody is aboard, so derelict boats stay put. This is
how you tune `Pivot outward/up/forward` and the force multipliers without rounding up three friends —
set it to 4, take a Longship out, and watch. Leave it at 0 on a real server. `Log rower changes` (Off).

**Console** (F5): `oarsmen ship` prints the ship you stand on (benches, tiller, paddle and sail
forces, live speed), `oarsmen rowers` lists who is rowing on every ship around, marking simulated benches `SIMULATED`, `oarsmen dump
<prefab> [depth]` writes a prefab's hierarchy to `BepInEx/LogOutput.log`. All local, no cheat flag.

## License

MIT. Bundles ServerSync (MIT-0, blaxxun-boop). Written for the bruceirons LAN server.
