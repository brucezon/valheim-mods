using System.Collections.Generic;
using System.Text;
using HarmonyLib;
using UnityEngine;

namespace BruceNetworking;

// Replaces ZDOMan.ServerSortSendZDOS. Vanilla 1.0 sort value = distance - min(staleness,100)*1.5, lower sends
// first, then ZDOMan.ServerSendCompare groups by ZDO type. We keep all of that and add a per-class bias.
[HarmonyPatch(typeof(ZDOMan), "ServerSortSendZDOS")]
internal static class ServerSortSendZDOS_Patch
{
	private static float s_lastSample = -100f;

	private static bool Prefix(List<ZDO> objects, Vector3 refPos, ZDOMan.ZDOPeer peer)
	{
		if (!BruceNetworkingPlugin.PriorityEnabled.Value || !BruceNetworkingPlugin.IsServer)
		{
			return true;
		}
		float time = Time.time;
		foreach (ZDO zdo in objects)
		{
			float value = Vector3.Distance(zdo.GetPosition(), refPos);
			float staleness = 100f;
			if (peer.m_zdos.TryGetValue(zdo.m_uid, out ZDOMan.ZDOPeer.PeerZDOInfo info))
			{
				staleness = Mathf.Clamp(time - info.m_syncTime, 0f, 100f);
			}
			value -= staleness * 1.5f;
			value += PrefabClasses.Bias(zdo.GetPrefab());
			zdo.m_tempSortValue = value;
		}
		ZDOMan.s_compareReceiver = peer.m_peer.m_uid;
		objects.Sort(ZDOMan.ServerSendCompare);

		if (BruceNetworkingPlugin.LogSendOrder.Value && time - s_lastSample > 5f && objects.Count > 0)
		{
			s_lastSample = time;
			StringBuilder sb = new();
			sb.Append($"send order for {peer.m_peer.m_playerName} ({objects.Count} queued): ");
			int n = Mathf.Min(12, objects.Count);
			for (int i = 0; i < n; i++)
			{
				ZDO z = objects[i];
				string name = ZNetScene.instance != null ? ZNetScene.instance.GetPrefab(z.GetPrefab())?.name ?? z.GetPrefab().ToString() : z.GetPrefab().ToString();
				sb.Append($"{name}:{PrefabClasses.Classify(z.GetPrefab())}:{z.m_tempSortValue:0}  ");
			}
			BruceNetworkingPlugin.Log.LogInfo(sb.ToString());
		}
		return false;
	}
}
