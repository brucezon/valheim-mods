using HarmonyLib;
using UnityEngine;

namespace BruceNetworking;

// Replaces ZRoutedRpc.RPC_RoutedRPC on the server. Vanilla forwards every broadcast routed RPC
// (target peer 0) to every ready peer except the sender: building-piece health changes, hit effects,
// animation triggers, and so on, regardless of where the recipient is. A peer that cannot have the
// target object loaded drops the RPC on arrival anyway (HandleRoutedRPC -> FindInstance == null), so
// we only forward broadcasts with a known target object to peers within `RPC radius` of it.
//
// Untouched: RPCs addressed to one peer, RPCs with no target object (chat, pings, global events),
// RPCs whose target object is unknown to the server, and objects flagged Distant (visible from far).
[HarmonyPatch(typeof(ZRoutedRpc), "RPC_RoutedRPC")]
internal static class RPC_RoutedRPC_Patch
{
	internal static long Forwarded;
	internal static long Suppressed;
	private static readonly ZRoutedRpc.RoutedRPCData s_data = new();
	private static readonly ZPackage s_out = new();

	private static bool Prefix(ZRoutedRpc __instance, ZRpc rpc, ZPackage pkg)
	{
		if (!BruceNetworkingPlugin.RpcAoiEnabled.Value || !__instance.m_server || !BruceNetworkingPlugin.IsServer)
		{
			return true;
		}

		ZRoutedRpc.RoutedRPCData data = s_data;
		data.Deserialize(pkg);
		long me = __instance.m_id;

		if (data.m_targetPeerID == me || data.m_targetPeerID == 0L)
		{
			__instance.HandleRoutedRPC(data);
		}
		if (data.m_targetPeerID == me)
		{
			return false;
		}
		if (data.m_targetPeerID != 0L)
		{
			__instance.RouteRPC(data); // addressed to one peer: vanilla path
			return false;
		}

		bool filter = TryGetTargetPosition(data.m_targetZDO, out Vector3 pos);
		float radius = Mathf.Max(BruceNetworkingPlugin.RpcRadius.Value, LoadedRadius());
		float r2 = radius * radius;

		s_out.Clear();
		data.Serialize(s_out);
		foreach (ZNetPeer peer in __instance.m_peers)
		{
			if (peer.m_uid == data.m_senderPeerID || !peer.IsReady())
			{
				continue;
			}
			if (filter)
			{
				Vector3 d = peer.m_refPos - pos;
				d.y = 0f;
				if (d.sqrMagnitude > r2)
				{
					Suppressed++;
					continue;
				}
			}
			peer.m_rpc.Invoke("RoutedRPC", s_out);
			Forwarded++;
		}
		return false;
	}

	// Farthest a non-Distant object can be from a peer and still be loaded by it, derived from the
	// server's synced simulation distance (1.0: a circle of `near` zones, or the classic square).
	// The configured radius is a floor; this keeps the filter safe if the server is launched with a
	// larger -simulationdistance than the default.
	private static float LoadedRadius()
	{
		if (ZNet.instance == null || ZoneSystem.instance == null) return 0f;
		SimulationDistance sim = ZNet.instance.GetSyncedSimulationDistance();
		float zone = ZoneSystem.instance.m_zoneSize;
		int near = Mathf.Max(1, sim.NearSimulationDistance);
		if (sim.IsClassic)
		{
			return 1.4143f * (near * zone + zone * 0.5f); // corner of the square
		}
		return (near + 1) * zone;
	}

	private static bool TryGetTargetPosition(ZDOID id, out Vector3 pos)
	{
		pos = default;
		if (id.IsNone() || ZDOMan.instance == null)
		{
			return false;
		}
		ZDO zdo = ZDOMan.instance.GetZDO(id);
		if (zdo == null || zdo.Distant)
		{
			return false;
		}
		pos = zdo.GetPosition();
		return true;
	}
}
