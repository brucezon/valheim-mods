using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace RaidBoss;

// The two messages this mod adds. Everything else still travels as ordinary game data (ZDOs, vanilla messages).
//
//   RaidBoss_Fights   server -> everyone   which bosses are being fought right now and how hard each one hits.
//                     A hit on a player is worked out on that player's own game (Character.RPC_Damage runs on the
//                     victim's owner), so the server cannot scale a boss's damage itself; it tells the clients.
//                     Re-sent every two seconds while a fight is on, so a player who walks in late has it within
//                     moments, and once more when the last fight ends. A client forgets an entry it has not heard
//                     about for ten seconds, so a lost "it is over" cannot leave a boss hitting harder for ever.
//   RaidBoss_Kill     client -> server     a creature this client was simulating died: what, where, and whether this
//                     mod spawned it. Exact, where a server on its own could only watch objects vanish.
internal static class Net
{
	const string FightsRpc = "RaidBoss_Fights";
	const string KillRpc = "RaidBoss_Kill";

	// Server side: filled by the director every tick.
	internal static readonly Dictionary<ZDOID, float> ActiveFights = new Dictionary<ZDOID, float>();
	// Client side: what the server last said, and when.
	static readonly Dictionary<ZDOID, float> bossDamage = new Dictionary<ZDOID, float>();
	static float heardAt = -999f;

	static float sendTimer;
	static int lastSentCount = -1;
	static ZRoutedRpc registeredOn;

	[HarmonyPatch(typeof(ZNet), "Awake")]
	static class ZNetAwakePatch
	{
		static void Postfix() => Register();
	}

	internal static void Register()
	{
		if (ZRoutedRpc.instance == null || ZRoutedRpc.instance == registeredOn) return;
		registeredOn = ZRoutedRpc.instance;
		bossDamage.Clear();
		lastSentCount = -1;
		ZRoutedRpc.instance.Register<ZPackage>(FightsRpc, RPC_Fights);
		ZRoutedRpc.instance.Register<ZPackage>(KillRpc, RPC_Kill);
	}

	// ---- server -> clients

	internal static void BroadcastFights(float dt)
	{
		if (ZRoutedRpc.instance == null) return;
		sendTimer += dt;
		bool changed = ActiveFights.Count != lastSentCount;
		if (!changed && (ActiveFights.Count == 0 || sendTimer < 2f)) return;
		sendTimer = 0f;
		lastSentCount = ActiveFights.Count;
		var pkg = new ZPackage();
		pkg.Write(ActiveFights.Count);
		foreach (KeyValuePair<ZDOID, float> kv in ActiveFights)
		{
			pkg.Write(kv.Key);
			pkg.Write(kv.Value);
		}
		ZRoutedRpc.instance.InvokeRoutedRPC(ZRoutedRpc.Everybody, FightsRpc, pkg);
	}

	static void RPC_Fights(long sender, ZPackage pkg)
	{
		// Only the server may say how hard a boss hits.
		if (ZNet.instance == null || (!ZNet.instance.IsServer() && sender != ZRoutedRpc.instance.GetServerPeerID())) return;
		bossDamage.Clear();
		int count = pkg.ReadInt();
		for (int i = 0; i < count && i < 64; i++)
		{
			ZDOID id = pkg.ReadZDOID();
			bossDamage[id] = Mathf.Clamp(pkg.ReadSingle(), 0.05f, 10f);
		}
		heardAt = Time.time;
	}

	// The multiplier for a hit from this attacker, if it is a boss in a fight the server told us about.
	internal static bool TryGetBossDamage(ZDOID attacker, out float mult)
	{
		mult = 1f;
		if (bossDamage.Count == 0) return false;
		if (Time.time - heardAt > 10f) { bossDamage.Clear(); return false; }
		return bossDamage.TryGetValue(attacker, out mult);
	}

	// ---- client -> server

	internal static void ReportKill(ZDOID id, int prefab, Vector3 pos, bool ours)
	{
		if (ZRoutedRpc.instance == null || ZNet.instance == null) return;
		var pkg = new ZPackage();
		pkg.Write(id);
		pkg.Write(prefab);
		pkg.Write(pos);
		pkg.Write(ours);
		ZRoutedRpc.instance.InvokeRoutedRPC(ZRoutedRpc.instance.GetServerPeerID(), KillRpc, pkg);
	}

	static void RPC_Kill(long sender, ZPackage pkg)
	{
		if (ZNet.instance == null || !ZNet.instance.IsServer()) return;
		ZDOID id = pkg.ReadZDOID();
		int prefab = pkg.ReadInt();
		Vector3 pos = pkg.ReadVector3();
		bool ours = pkg.ReadBool();
		Director.OnKill(id);
		WorldEncounters.OnKill(prefab, pos, ours);
	}
}
