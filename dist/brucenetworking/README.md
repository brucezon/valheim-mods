# BruceNetworking

**Server-only.** Install on the dedicated server. Clients need nothing; on a client every patch is a
no-op. Built for Valheim 1.0.7. Coexists with BetterNetworking (which owns compression and the Steam
send-rate / queue settings; this mod touches neither).

Written for a mixed LAN + remote group: some players sit next to the server, some connect over the
internet. Five independent features, each with its own toggle in
`BepInEx/config/bruceirons.BruceNetworking.cfg`.

## 1. LAN-first zone ownership

A dedicated server decides which player simulates a zone (physics, AI), walking peers in
**join order**. So a remote player who arrived first runs the mobs the LAN players are fighting.
BruceNetworking walks peers **LAN first, then lowest ping**, and moves ownership from a worse peer to
a better one when the object is well inside the better peer's area (vanilla's own active-area rule,
pulled inward by 12 m), with a 10 s per-object cooldown and a per-pass cap so nothing flaps.

**What gets moved (0.3.0):** only the classes in `Steer classes`, default **Creature** (mobs, tames,
anything with AI). Chests, doors, stations, signs, item stands, build pieces, plants, rocks and dropped
items are never moved, because only the owner's client writes an object's data and taking ownership
discards the write it had in flight; a remote player's chest deposit vanished that way in 0.2.1. On top of
the class list: an object flagged in use (`Skip objects in use`) or within `Owner keep radius` (40 m) of
its current owner is left alone, so the mob you are personally fighting stays yours.

LAN = the connection's IPv4 is inside `LAN subnets` (defaults 10/8, 172.16/12, 192.168/16, 127/8).
Tailscale's 100.64/10 is deliberately remote. `Force LAN players` / `Force remote players` override by
character name. Players are never moved; ships and carts only if `Steer ships and carts` is on and
`Dynamic` is in the class list.

## 2. Send priority

Vanilla orders each peer's outgoing object updates by distance and staleness only. We add a per-class
bias so players, creatures, doors, chests, stations and vehicles go out before plain build pieces,
and trees/rocks/plants go last. Only reorders within the existing send budget.

## 3. Multi-peer send loop

Vanilla waits 50 ms, then services **one peer per frame**. With 7 players each peer waits 50 ms plus
7 frames. We service every connected peer on each tick. `Send interval` 0.05 = vanilla cadence.

## 4. RPC area of interest

Broadcast RPCs that target an object (building-piece health changes, hit effects, animations) are
forwarded only to peers within `RPC radius` (300 m) of that object. Farther peers cannot have the
object loaded and would discard the RPC anyway. Nothing is dropped outright; RPCs addressed to a
single peer, RPCs without a target object, and far-visible objects are forwarded as vanilla does.

## 5. Wear throttle

For **server-owned** build pieces that are older than 30 s, at full health, dry, and outside the
Ashlands / Deep North, the wear tick (which includes the support physics check) runs at most once per
`Wear interval` (10 s). Throttled, not skipped: a piece that loses its foundation still collapses.

## Log

Every `Stats interval` (60 s): `peers (preference order): Alice[LAN 192.168.8.20 3ms] owns 812;
Bob[remote 100.101.4.7 48ms] owns 40; server owns 0 | peer sends N | rpc forwarded N suppressed N |
wear ticks skipped N | steer skipped: class N in-use N near-owner N`. Ownership moves log as
`moved ownership of N objects: ...`; the steerable class list is logged at load as
`ownership steering may move: Creature`.

MIT. Source included under `source/`. Ideas borrowed from FiresGhettoNetworking and VBNetTweaks are
credited in `source/IDEAS.md`.
