# Ideas worth borrowing from FiresGhettoNetworking 1.3.10 and VBNetTweaks 0.4.0

Survey of both decompiles (9 Sep, refs/1.0/_stage/netmods). Only ideas that are server-only deployable
and do not collide with BetterNetworking (which owns: Steam send-rate/buffer config, the SendZDOs queue
transpiler, compression). Verified against the 1.0.7 source where noted.

Note: FGN is compiled against pre-1.0 (Vector2i, int area args); every zone/sector feature in it is
broken on 1.0.7. It is an idea source, not a drop-in. VBNetTweaks' remaining features use APIs that
still exist.

## Shortlist (most valuable first)

1. **Multi-peer send loop** (VBNet). Vanilla `ZDOMan.SendZDOToPeers2` (verified 1.0.7): waits 0.05 s,
   then sends to ONE peer per frame, walking `m_nextSendPeer`. With 7 peers each peer is serviced every
   ~0.05 s + 7 frames. Replacement: on each tick send to every connected peer whose send queue is under
   the limit, passing `flush = queue < 20 %` so near-empty queues fill unthrottled. ~60 lines. Risk:
   sort+serialize for all peers lands in one frame; back-pressure is only the per-peer queue check.
2. **RPC area-of-interest routing** (FGN). Prefix `ZRoutedRpc.RPC_RoutedRPC` on the server: for
   broadcasts (target 0) with a target ZDO, forward only to peers within ~256 m of the ZDO position.
   `RPC_WNTHealthChanged` per building hit and `DamageText` currently fan out to everyone. Largest
   bytes/s cut available. ~250 lines. Do NOT copy FGN's outright drops of `RPC_HealthChanged` /
   `DamageText` (breaks HUDs).
3. **`WearNTear.UpdateWear` server skip** (FGN). Skip the tick for server-owned pieces that are
   full-health, dry, not Ashlands, older than 30 s. Pure CPU, ~35 lines, no wire risk.
4. **Distance penalty in the send sort** (FGN idea): beyond ~500 m from the peer add a flat penalty
   for Default-type ZDOs. Fold into our existing bias, gated by #5. ~40 lines.
5. **Congestion gate** (FGN): only apply heuristics (#4, #8) when a peer's `GetSendQueueSize()` is
   above ~50 % of the effective cap (cache 1 s). Calibrate against BN's patched queue semantics. ~40 lines.
6. **ServerSync/Jotunn join-time self-disconnect disarm** (FGN): rewrite the `30f` timeout literal
   near `GetSendQueueSize` call sites in every merged `ServerSync.ConfigSync` copy (and
   `Jotunn.Entities.CustomRPC.Timeout`). Stops slow-link joiners dropping mid config sync. ~50 lines,
   brittle IL patching of third-party code; fail open with a loud log.
7. **Server-side ship handoff** (FGN): prefix `ZRoutedRpc.RouteRPC`, on a granted `RequestRespons`
   set the ship ZDO owner to the requesting peer; suppress `Ship.UpdateOwner` on the dedi. ~60 lines.
   Must be an explicit exception in our ownership steering (ships are already excluded by default).
8. **AI LOD** (FGN): halve `Character.CustomFixedUpdate` rate for non-tamed non-player creatures
   >300 m from the nearest peer, hash-jittered, gated by #5. ~90 lines, CPU only.

## Also seen, not worth it for us

- FGN per-peer ZDO delta serialization: wire-compatible with vanilla clients (Deserialize is additive
  per key), but never diffs strings/bytes, cannot represent key removal until a 5 s keyframe, and keeps
  per-peer snapshots with no eviction. Memory liability on a growing world.
- FGN multi-peer server authority / predictive zone streaming: heavy, and broken on 1.0.7 as written.
  Our SSS port covers the emergency case.
- FGN RandEventSystem rework, Tameable/AudioMan/TerrainComp disables (gameplay changes, not perf).
- FGN AutoTune tiering (2500 lines): hardcode numbers for a known 7-player server instead. The one idea
  worth keeping is a boot-time self-check that no configured value is worse than vanilla.
- VBNet ZSyncTransform smoothing constants, map-marker interpolation, teleport loading-screen trick:
  client-side.
- VBNet `ServerSendCompare` prefab priority: our feature (a), already done. Its implementation shape
  (patch the comparator rather than re-sorting) is what we should keep doing.
