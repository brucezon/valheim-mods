# Changelog

## 0.3.0 — 2026-09-10
- **Ownership steering no longer moves chests, doors, stations, signs, item stands, build pieces, plants,
  rocks or dropped items.** Only the classes listed in the new `Steer classes` setting are moved; the default
  is `Creature`. Reason: only the owner's client writes an object's data, and `SetOwner` bumps the owner
  revision so the old owner's in-flight update is discarded. With remote and LAN players in the same base,
  0.2.1 moved a chest away from the remote player mid-deposit and the items were gone (10 Sep 2026: 494
  single-object moves in 20 minutes, remote → LAN).
- New `Skip objects in use` (default on): an object whose `InUse` flag is set is never moved, whatever its class.
- New `Owner keep radius` (default 40 m): an object within that distance of its current owner is never moved.
- Stats line gains `steer skipped: class N in-use N near-owner N`; the steerable class list is logged at load
  and whenever the setting changes.

## 0.2.1 — 2026-09-09
- RPC area-of-interest radius is now floored by the loaded radius implied by the server's simulation
  distance, so a server launched with a larger `-simulationdistance` never suppresses RPCs for objects a
  peer actually has loaded.
- README wording: server-side ownership arbitration predates 1.0; 1.0 changed the area geometry.

## 0.2.0 — 2026-09-09
- Multi-peer send loop (all connected peers per tick).
- RPC area of interest for broadcasts with a target object (300 m).
- Wear-tick throttle for server-owned intact pieces (10 s).
- Ownership steering now mirrors vanilla's active-area predicate with an inward margin; skips peers
  with a smaller simulation distance; cooldown 10 s.
- Peer classifier unwraps ServerSync's socket wrappers to reach the Steam connection.

## 0.1.0 — 2026-09-09
- Prefab-class send priority; LAN-first / lowest-ping zone ownership.
