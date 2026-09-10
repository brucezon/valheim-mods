using System;
using System.Collections.Generic;
using System.Text;
using HarmonyLib;
using UnityEngine;

namespace BruceNetworking;

// Replaces ZDOMan.ReleaseZDOS (runs only when IsServer, pre-1.0 as well). Vanilla: every 2 s, for the server session and then
// each peer in join order, ReleaseNearbyZDOS releases what that peer owns outside its area and claims
// unowned (or abandoned) objects inside it. Join order therefore decides who wins a shared zone.
//
// We keep the vanilla pass but run it in preference order (LAN first, then lowest ping), then run a
// steering pass that moves ownership from a worse peer to a better peer when the object is well inside
// the better peer's active area, at most once per object per cooldown.
[HarmonyPatch(typeof(ZDOMan), "ReleaseZDOS")]
internal static class ReleaseZDOS_Patch
{
	private static readonly List<ZDOMan.ZDOPeer> s_ordered = new();
	private static readonly Dictionary<long, PeerClassifier.PeerInfo> s_infoByUid = new();
	private static readonly Dictionary<ZDOID, float> s_cooldown = new();
	private static readonly List<ZDO> s_near = new();
	private static readonly Dictionary<string, int> s_moves = new();
	private static float s_lastPrune;
	private static float s_lastStats = -1000f;

	private static bool Prefix(ZDOMan __instance, float dt)
	{
		if (!BruceNetworkingPlugin.OwnershipEnabled.Value || !BruceNetworkingPlugin.IsServer)
		{
			return true;
		}
		__instance.m_releaseZDOTimer += dt;
		if (__instance.m_releaseZDOTimer <= 2f)
		{
			return false;
		}
		__instance.m_releaseZDOTimer = 0f;

		try
		{
			// Vanilla pass, in preference order.
			__instance.ReleaseNearbyZDOS(ZNet.instance.GetReferencePosition(), __instance.m_sessionID);
			BuildOrder(__instance);
			foreach (ZDOMan.ZDOPeer peer in s_ordered)
			{
				__instance.ReleaseNearbyZDOS(peer.m_peer.m_refPos, peer.m_peer.m_uid);
			}

			Steer(__instance);
			MaybeLogStats(__instance);
		}
		catch (Exception e)
		{
			BruceNetworkingPlugin.Log.LogError($"ownership pass failed: {e}");
		}
		return false;
	}

	private static void BuildOrder(ZDOMan zdoMan)
	{
		s_ordered.Clear();
		s_infoByUid.Clear();
		foreach (ZDOMan.ZDOPeer peer in zdoMan.m_peers)
		{
			if (peer?.m_peer == null || !peer.m_peer.IsReady()) continue;
			PeerClassifier.PeerInfo info = PeerClassifier.Get(peer.m_peer);
			s_infoByUid[peer.m_peer.m_uid] = info;
			s_ordered.Add(peer);
		}
		s_ordered.Sort((a, b) => PeerClassifier.Compare(s_infoByUid[a.m_peer.m_uid], s_infoByUid[b.m_peer.m_uid]));
	}

	private static void Steer(ZDOMan zdoMan)
	{
		if (s_ordered.Count < 2) return; // nothing to steer between
		float now = Time.time;
		if (now - s_lastPrune > 60f)
		{
			s_lastPrune = now;
			List<ZDOID> stale = new();
			float cooldown = BruceNetworkingPlugin.TransferCooldown.Value;
			foreach (KeyValuePair<ZDOID, float> kv in s_cooldown)
			{
				if (now - kv.Value > cooldown) stale.Add(kv.Key);
			}
			foreach (ZDOID id in stale) s_cooldown.Remove(id);
		}

		SimulationDistance synced = ZNet.instance.GetSyncedSimulationDistance();
		int near = synced.NearSimulationDistance;
		float margin = Mathf.Max(0f, BruceNetworkingPlugin.InnerMargin.Value);
		SimulationDistance nearOnly = new(near, 0, synced.IsClassic);
		float cooldownSeconds = BruceNetworkingPlugin.TransferCooldown.Value;
		int budget = BruceNetworkingPlugin.MaxTransfersPerPass.Value;
		bool steerVehicles = BruceNetworkingPlugin.SteerVehicles.Value;
		s_moves.Clear();
		int moved = 0;

		foreach (ZDOMan.ZDOPeer candidatePeer in s_ordered)
		{
			if (moved >= budget) break;
			ZNetPeer cand = candidatePeer.m_peer;
			if (cand.m_characterID.IsNone()) continue; // not in the world yet
			// A peer running a smaller near-simulation distance than the server has a smaller active area
			// than the predicate below assumes; never hand it objects it may not have loaded.
			if (cand.m_simulationDistance.NearSimulationDistance < near) continue;
			PeerClassifier.PeerInfo candInfo = s_infoByUid[cand.m_uid];

			Vector2s candZone = ZoneSystem.GetZone(cand.m_refPos);
			s_near.Clear();
			zdoMan.FindSectorObjects(candZone, nearOnly, s_near);
			foreach (ZDO zdo in s_near)
			{
				if (moved >= budget) break;
				if (!zdo.Persistent) continue;
				long owner = zdo.GetOwner();
				if (owner == 0L || owner == cand.m_uid || owner == zdoMan.m_sessionID) continue;
				if (!s_infoByUid.TryGetValue(owner, out PeerClassifier.PeerInfo ownerInfo)) continue; // owner gone: vanilla pass handles it
				if (!PeerClassifier.IsBetter(candInfo, ownerInfo)) continue;

				int prefab = zdo.GetPrefab();
				PrefabClass cls = PrefabClasses.Classify(prefab);
				if (cls == PrefabClass.Player) continue;
				if (!steerVehicles && PrefabClasses.IsVehicle(prefab)) continue;

				if (!WellInsideActiveArea(candZone, zdo.GetPosition(), synced, margin)) continue;

				if (s_cooldown.TryGetValue(zdo.m_uid, out float last) && now - last < cooldownSeconds) continue;

				zdo.SetOwner(cand.m_uid);
				s_cooldown[zdo.m_uid] = now;
				moved++;
				string key = $"{ownerInfo.Name}({ownerInfo.Class}{PingText(ownerInfo)}) -> {candInfo.Name}({candInfo.Class}{PingText(candInfo)})";
				s_moves[key] = s_moves.TryGetValue(key, out int c) ? c + 1 : 1;
			}
		}

		if (moved > 0 && BruceNetworkingPlugin.LogTransfers.Value)
		{
			StringBuilder sb = new();
			sb.Append($"moved ownership of {moved} objects: ");
			foreach (KeyValuePair<string, int> kv in s_moves) sb.Append($"{kv.Value} {kv.Key}; ");
			BruceNetworkingPlugin.Log.LogInfo(sb.ToString());
		}
	}

	private static string PingText(PeerClassifier.PeerInfo info) => info.Ping < 0f ? "" : $" {(int)info.Ping}ms";

	// Mirrors ZNetScene.PointInsideActiveArea (1.0.7) with the boundary pulled inward by `margin` metres:
	// a Chebyshev box of 1.5 zones (1 zone when near == 1) around the zone centre, additionally clipped to
	// a 1.75-zone circle when near == 2 and not classic. Anything the vanilla predicate would call outside
	// the candidate's area must never be handed to it, or its next vanilla pass releases it again.
	private static bool WellInsideActiveArea(Vector2s zone, Vector3 point, SimulationDistance synced, float margin)
	{
		point.y = 0f;
		float zoneSize = ZoneSystem.instance.m_zoneSize;
		float boxZones = synced.NearSimulationDistance == 1 ? 1f : 1.5f;
		Vector3 zonePos = ZoneSystem.GetZonePos(zone);
		zonePos.y = 0f;
		float dx = Mathf.Abs(zonePos.x - point.x);
		float dz = Mathf.Abs(zonePos.z - point.z);
		float box = boxZones * zoneSize - margin;
		if (Mathf.Max(dx, dz) > box) return false;
		if (synced.NearSimulationDistance == 2 && !synced.IsClassic)
		{
			float radius = zoneSize * 1.75f - margin;
			return dx * dx + dz * dz < radius * radius;
		}
		return true;
	}

	private static void MaybeLogStats(ZDOMan zdoMan)
	{
		if (!BruceNetworkingPlugin.LogStats.Value) return;
		float now = Time.time;
		if (now - s_lastStats < BruceNetworkingPlugin.StatsInterval.Value) return;
		s_lastStats = now;
		if (s_ordered.Count == 0) return;

		Dictionary<long, int> owned = new();
		foreach (KeyValuePair<ZDOID, ZDO> kv in zdoMan.m_objectsByID)
		{
			long owner = kv.Value.GetOwner();
			if (owner == 0L) continue;
			owned[owner] = owned.TryGetValue(owner, out int c) ? c + 1 : 1;
		}
		StringBuilder sb = new();
		sb.Append("peers (preference order): ");
		foreach (ZDOMan.ZDOPeer peer in s_ordered)
		{
			PeerClassifier.PeerInfo info = s_infoByUid[peer.m_peer.m_uid];
			owned.TryGetValue(peer.m_peer.m_uid, out int n);
			sb.Append($"{info} owns {n}; ");
		}
		owned.TryGetValue(zdoMan.m_sessionID, out int serverOwned);
		sb.Append($"server owns {serverOwned}");
		sb.Append($" | peer sends {SendZDOToPeers2_Patch.PeerSends}");
		sb.Append($" | rpc forwarded {RPC_RoutedRPC_Patch.Forwarded} suppressed {RPC_RoutedRPC_Patch.Suppressed}");
		sb.Append($" | wear ticks skipped {UpdateWear_Patch.Skipped}");
		BruceNetworkingPlugin.Log.LogInfo(sb.ToString());
	}
}

// Drop cached classification when a peer leaves so a reconnect re-probes its address.
[HarmonyPatch(typeof(ZDOMan), nameof(ZDOMan.RemovePeer))]
internal static class RemovePeer_Patch
{
	private static void Prefix(ZNetPeer netPeer)
	{
		if (netPeer != null) PeerClassifier.Forget(netPeer.m_uid);
	}
}
