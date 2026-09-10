using HarmonyLib;

namespace BruceNetworking;

// Replaces ZDOMan.SendZDOToPeers2 on the server. Vanilla 1.0.7 waits 0.05 s, then services ONE peer per
// frame (m_nextSendPeer walks the list), so with N peers each peer is serviced every 0.05 s + N frames.
// We service every connected peer on the same tick. SendZDOs keeps its own per-peer back-pressure
// (the send-queue check, which BetterNetworking's transpiler resizes), so nothing else is needed.
[HarmonyPatch(typeof(ZDOMan), "SendZDOToPeers2")]
internal static class SendZDOToPeers2_Patch
{
	internal static long PeerSends;

	private static bool Prefix(ZDOMan __instance, float dt)
	{
		if (!BruceNetworkingPlugin.SendLoopEnabled.Value || !BruceNetworkingPlugin.IsServer)
		{
			return true;
		}
		if (__instance.m_peers.Count == 0)
		{
			return false;
		}
		__instance.m_sendTimer += dt;
		if (__instance.m_sendTimer < BruceNetworkingPlugin.SendInterval.Value)
		{
			return false;
		}
		__instance.m_sendTimer = 0f;
		__instance.m_nextSendPeer = -1;
		for (int i = 0; i < __instance.m_peers.Count; i++)
		{
			ZDOMan.ZDOPeer peer = __instance.m_peers[i];
			ISocket socket = peer?.m_peer?.m_socket;
			if (socket == null || !socket.IsConnected())
			{
				continue;
			}
			if (__instance.SendZDOs(peer, flush: false))
			{
				PeerSends++;
			}
		}
		return false;
	}
}
