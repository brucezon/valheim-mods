# Changelog

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
