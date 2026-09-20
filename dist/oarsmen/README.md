# Oarsmen

Seated players row the ship. Built for Valheim 1.0.12.

> **Install on the dedicated server and on every client.** (Ignore the "Client-only" line on this
> page — Hexium shows that on every package, including server-only ones.) The mod is not required:
> a client without it simply sees no oars.
>
> **Oar placement is measured on the Longship** as of 0.3.0 — the pivot offsets are no longer the
> first guesses 0.1.0 shipped with. The Karve has not been checked in game, and a boat from another
> mod may still want a nudge: `Pivot outward/up/forward`, or `Snap oars to the hull` to have the mod
> find the planking itself. `oarsmen ship` prints what you are standing on to help.

Vanilla ships paddle with one fixed force no matter who is aboard, and the Longship's benches and oar
holes are decoration. With Oarsmen, every player sitting on one of the ship's benches is a rower:

- **In paddle mode** (the slow setting, sail furled) each rower adds **Paddle force per rower** of the
  ship's paddle force. Default 0.35, so a full Longship crew of four paddles with 2.4 times the force.
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
  backs water — the same stroke rowed the other way, at full length — while the outside bank keeps
  pulling, which is how a crew pivots a longship on the spot. Sail
  out or ship stopped: the oars are shipped fore and aft along the hull, blades aft, lifted clear of
  the water. Bench empty: no oar.
- The helmsman at the tiller is not a rower. Someone has to steer; the rest sit down and pull. The
  tiller's hover text shows `Rowers 3/4` so the helmsman can see who is actually pulling.
- **A seat is not always a bench.** The Longship carries seven places to sit: four rowing benches, the
  helm, and two spots where a passenger holds fast — at the mast and out at the figurehead. Only the
  benches row. The helm is recognised by sitting at the tiller's own attach point, the hold-fast spots
  by the animation vanilla puts you in, so the rule carries to boats this mod has never seen.

**Sailing is untouched.** Vanilla only gives the rudder a push in paddle and reverse — under sail a
ship turns on its speed through the water, and that is left exactly as it was. The crew helps you
paddle and manoeuvre; it does not help you tack.

**If you do switch on `Row under sail`, the oars fade out as the ship picks up speed.** A blade only
bites while it is moving through the water faster than the hull is, so the crew's help falls away as
the square of the speed they have left, and stops entirely at `Speed where oars stop helping` (5 m/s).

To put that in context, since the game never shows you a speed: a Longship paddles at about 3.2 m/s
and sails between 3.6 into the wind and 9.4 with a full tailwind; a Karve paddles at 3.1 and sails
between 2.8 and 7.0. So at the default the crew hauls hard getting under way and has bowed out by the
time any sail is drawing properly — rowing is how you leave a calm or claw off a lee shore, not a way
to go faster. Raise it toward 7 if you want rowers to still count while sailing into a headwind; drop
it toward 4 to make them purely a way to get moving. Paddle mode and reverse ignore this setting
entirely and keep their flat force, matching vanilla's own.

**The oars ship themselves as the sail takes over.** The stroke shortens and the blades lift out of
the water on the same curve as the force, so the crew is never seen thrashing away at a speed where
they are achieving nothing: full strokes becalmed, easing off through 2–3 m/s, held clear by about 4.
It is a blend rather than a switch, so nothing snaps. A hard rudder is different: the inside bank turns
its stroke around rather than stopping, because a crew pivoting a boat backs water, it does not sit
there with its oars up.

**Every boat with benches rows — nothing needs listing in the config.** The mod looks for `Chair`
components on the hull and drops the ones that are not rowing stations — the helm, and anywhere a
passenger holds fast rather than sits — so the Longship (4 benches of its 7 seats) and Karve work out
of the box, and so do boats from other mods, OdinShip's rowing canoes included, the moment they exist.
A hull with nowhere to sit never rows. If a modded boat's seats are not being counted, `oarsmen ship`
prints the attach animation of every one of them; add it to `Rowing seat animations` or take it out of
`Hold-fast seat animations`.

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

## Pre-1.0 sails on Valheim 1.0

Valheim 1.0 replaced the sail system: a sail is now a MagicaCloth sliding between furled, mid and
unfurled points, and the game only moves it on prefabs that carry the new fields. A boat from a mod
that has not migrated - OdinShip 0.7.9's hulls, at the time of writing - accepts Half and Full but its
sail stays furled, because nothing scales the old-style sail object any more. **Pre-1.0 sails deploy**
(On, section 5) runs the pre-1.0 sail routine for exactly those ships: the sail scales between furled,
half and full as it did before 1.0, and its cloth simulates only while set. Ships with 1.0 sails are
never touched, so it is safe to leave on. Visual only, per client. oarsmen ship prints a sail: line
showing which system a hull has. Turn it off once the boat's author has migrated the prefabs.

Since 0.5.0 that routine is only the fallback. **1.0 sails on pre-1.0 hulls** (On) gives such a hull a
real 1.0 sail instead: vanilla's own sail rig (the same one the Raft, Karve, Longship and Drakkar share)
is cloned under the hull's mast, made exactly as wide as the sail it replaces with the tops level and the
foot reaching the old foot, and the ship is handed to vanilla's 1.0 routine. It then furls, half-sets and
fills like a Karve, with MagicaCloth and the vanilla sail sound. The old cloth is hidden; its rope lines
stay and follow. **Keep the hull sail canvas** (On) dresses the new sail in the old sail's material, turned
the right way round (the two meshes run their texture in opposite directions); switch it Off for the
vanilla canvas if a boat's emblem looks wrong. It keeps following the hull afterwards: OdinShip's sail designs (H on the Big Cargo Ship and War Ship) change the new sail too. **1.0 sail size** (1) scales the result. OdinShip 0.8.1's
six sailing hulls all take the graft; its two canoes have their mast switched off and are left alone. A
hull the graft cannot handle falls back to the routine above. Both settings follow live, for ships already
afloat, and oarsmen ship prints a second line saying what was done and with which numbers.

## Config (`BepInEx/config/bruceirons.Oarsmen.cfg`)

**2 - Rowing:** `Rowing` (On), `Only these ships` (empty = every boat with benches),
`Excluded ships` (empty), `Hold-fast seat animations` (`attach_mast,attach_dragon,attach_bed` — seats you hang on
at rather than row from), `Rowing seat animations` (empty = every seat that is not one of those or the
helm; set `attach_sitship` to allow vanilla ship benches only), `Paddle force per rower (x)` (0.35),
`Steering force per rower (x)` (0.15, set 0 to turn the steering help off), `Max steering share (x)` (1 = the crew can at most double the ship's own rudder force),
`Max turn rate (degrees per second)` (45, a backstop on yaw only), `Max rowers` (0 = every bench counts),
`Row under sail` (Off: rowers only add forward force in paddle mode and reverse; steering help is
never added under sail either way), `Speed where oars stop helping (m/s)` (5),
`Show rowers on the tiller` (On).

**3 - Oars:** `Show oars` (On), oar and blade dimensions, and where the oar pivots relative to its
bench: `Pivot outward` (0.6), `Pivot up` (0.26), `Pivot forward` (0.19), all metres and all measured
on the Longship. `Snap oars to the hull` (Off) ignores `Pivot outward` and finds the planking itself,
for a hull those numbers do not suit, with `Oar hole inset from the hull` (0) to nudge the result.
`Oar hole positions`: comma-separated ship-local Z positions of the hull's oar holes; when set, each
oar snaps to the closest hole instead of sitting beside its bench. `Stroke sweep` (40),
`Stroke rate (strokes per minute)` (26 — a working crew, not a racing sprint),
`Drive share of the stroke` (0.45 — the loaded half, blade in the water; the rest carries the oar
forward again), `Blade dip` (35, set that deep because the oar holes sit well above the waterline),
`Recovery lift` (22 degrees above the dip, enough to clear the water coming forward), `Catch blend` (0.16 of a stroke: how long the blade takes to drop in at the catch and come out at the finish, centred on each),
`Feather angle` (90 — the blade turns flat on the recovery and squares up at the catch; 0 = off),
`Stowed oars` (`AlongHull`, or
`Outboard` for oars left standing out to the side), `Stowed angle` (12) for how far a stowed oar is
lifted clear, `Stow time (seconds)` (0.8) for how long an oar takes to ship itself and swing back out,
and `Turn stroke bias (x)` (1.6) for how much rudder it takes before the inside bank backs water
instead of pulling ahead — it turns over at 1 divided by this, and 0 = both
always row together. All of section 3 is cosmetic; none of it changes how the ship moves.

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
