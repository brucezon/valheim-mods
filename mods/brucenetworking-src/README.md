# BruceNetworking (server-only)

GUID `bruceirons.BruceNetworking`. Install on the dedicated server only. Clients need nothing.
Toggle: `tools\bn-on.bat` / `tools\bn-off.bat` + server restart. Config:
`server\BepInEx\config\bruceirons.BruceNetworking.cfg`.

Build: `dotnet build mods\brucenetworking-src\BruceNetworking\BruceNetworking.csproj -c Release -p:GamePath=refs\1.0\gamepath`
(no ILRepack needed: no bundled libraries).

## 1. Send priority (`ZDOMan.ServerSortSendZDOS` replaced)

Vanilla 1.0 orders each peer's outgoing ZDOs by `distance - min(staleness, 100) * 1.5`, lower first, then
groups by ZDO type. When a peer's send budget (10 KB queue) is saturated, that means a roof shingle
20 m away and the door in front of you compete as equals. We add a per-class bias (metres):

| Class | Components | Default |
|---|---|---|
| Player | Player | -300 |
| Creature | Character, BaseAI | -120 |
| Interactive | Door, Container, CookingStation, Smelter, Fermenter, Fireplace, Beehive, Bed, CraftingStation, ItemStand, Sign, Turret, Windmill | -90 |
| Dynamic | Ship, Vagon, Projectile, ItemDrop, Fish | -60 |
| Structure | WearNTear / Piece only | 0 |
| Nature | Plant, Pickable, TreeBase, TreeLog, Destructible, MineRock(5) | +40 |

Classification inspects the prefab's components once per prefab hash and caches it. Only changes the
order inside the existing budget, so it coexists with BetterNetworking (which patches the socket
queue and `ZDOMan.AddPeer`, not the sort).

## 2. Ownership steering (`ZDOMan.ReleaseZDOS` replaced)

On a dedicated server (before 1.0 as well as after; 1.0 only changed the area geometry and zone types)
the server arbitrates who owns (simulates) objects: every 2 s it walks its peers in **join
order** and each peer claims unowned objects in its active area. Join order decides who simulates a
shared zone, so a remote player who arrived first keeps running the mobs that LAN players fight.

We keep the vanilla pass but walk peers in **preference order**: LAN first, then lowest ping. Then a
steering pass moves ownership from a worse peer to a better one when:

- the better peer is in the world, runs at least the server's near simulation distance, and the object is at least `Inner margin` metres inside its active
  area, which mirrors vanilla's 1.5-zone box / 1.75-zone circle (default 12 m, so nothing on the edge bounces),
- the object is not a player, and not a ship/cart unless `Steer ships and carts` is on,
- the object has not been moved in the last `Transfer cooldown` seconds (default 10),
- the pass has not exceeded `Max transfers per pass` (default 300).

"Better" = LAN over remote; within the same class, ping lower by at least `Ping delta` (default 60 ms)
when `Ping tiebreak` is on.

LAN detection: the remote IPv4 of the peer's Steam networking connection matched against
`LAN subnets` (defaults: 10/8, 172.16/12, 192.168/16, 127/8; Tailscale 100.64/10 is deliberately
remote). `Force LAN players` / `Force remote players` override by name, which is also how to test
steering on one LAN: force the second PC "remote" and watch the log.

Ping: Steam's `GetConnectionRealTimeStatus` ping, smoothed. Logged in the peer stats line every
`Stats interval` seconds together with each peer's owned-object count.

## 3. Multi-peer send loop (`ZDOMan.SendZDOToPeers2` replaced) — 0.2.0

Vanilla 1.0.7 waits 0.05 s, then sends to ONE peer per frame, walking `m_nextSendPeer`. With 7 peers
each peer is serviced every 0.05 s + 7 frames. We service every connected peer on the same tick.
`SendZDOs` keeps its own per-peer back-pressure (send-queue check, resized by BetterNetworking).
`Send interval` default 0.05 (vanilla cadence); 0.02 is VBNetTweaks' choice.

## 4. RPC area of interest (`ZRoutedRpc.RPC_RoutedRPC` replaced) — 0.2.0

Broadcast routed RPCs (target peer 0) that name a target object are forwarded only to peers within
`RPC radius` (300 m) of that object. A peer farther away cannot have the object loaded and would drop
the RPC on arrival (`HandleRoutedRPC` → `FindInstance` null). Untouched: RPCs addressed to one peer,
RPCs with no target object (chat, pings, events), unknown objects, and objects flagged Distant.
Unlike FiresGhettoNetworking, nothing is dropped outright.

## 5. Wear throttle (`WearNTear.UpdateWear` prefix) — 0.2.0

For SERVER-owned pieces older than 30 s, full health, dry, not Ashlands, not Deep North: run the wear
tick (which includes the `UpdateSupport` physics overlap) at most once per `Wear interval` (10 s).
Throttled, not skipped: a piece that loses its foundation still collapses within one interval. Only
server-owned pieces are affected: the spawn area under our ownership rules, or everything with SSS on.

## Log lines to look for

- `peers (preference order): Alice[LAN 192.168.8.20 3ms] owns 812; Bob[remote 100.101.4.7 48ms] owns 40; server owns 0`
- `moved ownership of 37 objects: 37 Bob(remote 48ms) -> Alice(LAN 3ms);`
- with `Log send order sample`: `send order for Alice (140 queued): Player:Player:-410  piece_chest:Interactive:-180 ...`
