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

Works on the **Longship** (4 benches) and the **Karve** (2 benches). Any ship prefab with benches
can be added to `Ships` in the config, including boats from other mods such as OdinShip — the mod
finds benches by looking for `Chair` components on the ship, so it does not care who built it. Use
`oarsmen dump <prefab>` to check a modded boat actually has them before adding it.

Install on the server and on every client. A client without the mod sees no oars, and if that client
happens to own the ship (it is the one simulating it) the ship rows at vanilla speed; nothing breaks.
Config is server-synced and live.

## Config (`BepInEx/config/bruceirons.Oarsmen.cfg`)

**2 - Rowing:** `Rowing` (On), `Ships` (VikingShip, Karve), `Paddle force per rower (x)` (0.25),
`Steering force per rower (x)` (0.15, set 0 to turn the steering help off), `Max rowers` (4),
`Row under sail` (Off: rowers only add forward force in paddle mode and reverse; steering help is
never added under sail either way), `Show rowers on the tiller` (On).

**3 - Oars:** `Show oars` (On), oar and blade dimensions, and where the oar pivots relative to its
bench: `Pivot outward`, `Pivot up`, `Pivot forward` (metres). `Oar hole positions`: comma-separated
ship-local Z positions of the hull's oar holes; when set, each oar snaps to the closest hole instead
of sitting beside its bench. `Stroke sweep`, `Blade dip`, `Stowed angle` (degrees), and
`Turn stroke bias (x)` (1.6) for how sharply a turn splits the two banks — 0 = both always row
together. All of section 3 is cosmetic; none of it changes how the ship moves.

**Console** (F5): `oarsmen ship` prints the ship you stand on (benches, tiller, paddle and sail
forces, live speed), `oarsmen rowers` lists who is rowing on every ship around, `oarsmen dump
<prefab> [depth]` writes a prefab's hierarchy to `BepInEx/LogOutput.log`. All local, no cheat flag.

## License

MIT. Bundles ServerSync (MIT-0, blaxxun-boop). Written for the bruceirons LAN server.
