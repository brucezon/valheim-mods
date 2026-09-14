# Oarsmen

Seated players row the ship. Built for Valheim 1.0.7.

> **Install on the dedicated server and on every client.** (Ignore the "Client-only" line on this
> page — Hexium shows that on every package, including server-only ones.) The mod is not required:
> a client without it simply sees no oars.
>
> **0.1.x is untuned.** Oars row and push the ship correctly, but where each oar sits — the pivot
> offsets and the hull's oar-hole positions — are first guesses that have not been checked in game
> yet. Expect to nudge `Pivot outward/up/forward` for your ship. `oarsmen ship` prints what you are
> standing on to help.

Vanilla ships paddle with one fixed force no matter who is aboard, and the Longship's benches and oar
holes are decoration. With Oarsmen, every player sitting on one of the ship's benches is a rower:

- **In paddle mode** (the slow setting, sail furled) each rower adds **Paddle force per rower** of the
  ship's paddle force. Default 0.25, so a full Longship crew of four paddles with twice the force.
  Speed rises less than force, because the water pushes back harder the faster you go.
- **An oar appears through the hull beside every occupied bench** and strokes in time with the rudder
  paddle while the ship is rowing. Sail out or ship stopped: the oars are held level and still. Bench
  empty: no oar.
- The helmsman at the tiller is not a rower. Someone has to steer; the rest sit down and pull.

Works on the **Longship** (4 benches) and the **Karve** (2 benches). Any ship prefab with benches
can be added to `Ships` in the config, including boats from other mods such as OdinShip — the mod
finds benches by looking for `Chair` components on the ship, so it does not care who built it. Use
`oarsmen dump <prefab>` to check a modded boat actually has them before adding it.

Install on the server and on every client. A client without the mod sees no oars, and if that client
happens to own the ship (it is the one simulating it) the ship rows at vanilla speed; nothing breaks.
Config is server-synced and live.

## Config (`BepInEx/config/bruceirons.Oarsmen.cfg`)

**2 - Rowing:** `Rowing` (On), `Ships` (VikingShip, Karve), `Paddle force per rower (x)` (0.25),
`Max rowers` (4), `Row under sail` (Off: rowers only count in paddle mode).

**3 - Oars:** `Show oars` (On), oar and blade dimensions, and where the oar pivots relative to its
bench: `Pivot outward`, `Pivot up`, `Pivot forward` (metres). `Oar hole positions`: comma-separated
ship-local Z positions of the hull's oar holes; when set, each oar snaps to the closest hole instead
of sitting beside its bench. `Stroke sweep`, `Blade dip`, `Stowed angle` (degrees).

**Console** (F5): `oarsmen ship` prints the ship you stand on (benches, tiller, paddle and sail
forces, live speed), `oarsmen rowers` lists who is rowing on every ship around, `oarsmen dump
<prefab> [depth]` writes a prefab's hierarchy to `BepInEx/LogOutput.log`. All local, no cheat flag.

## License

MIT. Bundles ServerSync (MIT-0, blaxxun-boop). Written for the bruceirons LAN server.
