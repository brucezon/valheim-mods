using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace RaidBoss;

// The messages this mod adds. Everything else still travels as ordinary game data (ZDOs, vanilla messages).
//
//   RaidBoss_Fights   server -> everyone   which bosses are being fought right now and how hard each one hits.
//                     A hit on a player is worked out on that player's own game (Character.RPC_Damage runs on the
//                     victim's owner), so the server cannot scale a boss's damage itself; it tells the clients.
//                     Re-sent every two seconds while a fight is on, so a player who walks in late has it within
//                     moments, and once more when the last fight ends. A client forgets an entry it has not heard
//                     about for ten seconds, so a lost "it is over" cannot leave a boss hitting harder for ever.
//   RaidBoss_Kill     client -> server     a creature this client was simulating died: what, where, and whether this
//                     mod spawned it. Exact, where a server on its own could only watch objects vanish.
//   RaidBoss_Creature server -> a creature's owner   "do this to creature X": a list of small operations (Op). A
//                     creature's data lives on its ZDO, which only its owner may write, so the server asks the owner.
//                     The general tool: heroic health, wards, traits on a boss, healing and breaks all go through it.
//                     An operation a client does not know is skipped, so a newer server does not break an older client.
//   RaidBoss_Strike / _Fx / _Status / _Env   server -> everyone   a telegraphed ground strike, a vanilla effect, a
//                     vanilla status effect, forced weather. Each player's own game shows it and judges its own player.
//   RaidBoss_Event    client -> server     something a boss's owner saw happen (the break meter filled).
internal static class Net
{
	const string FightsRpc = "RaidBoss_Fights";
	const string KillRpc = "RaidBoss_Kill";
	const string CreatureRpc = "RaidBoss_Creature";
	const string StrikeRpc = "RaidBoss_Strike";
	const string ChaseRpc = "RaidBoss_Chase";
	const string StormRpc = "RaidBoss_Storm";
	const string FxRpc = "RaidBoss_Fx";
	const string StatusRpc = "RaidBoss_Status";
	const string EnvRpc = "RaidBoss_Env";
	const string EventRpc = "RaidBoss_Event";
	const string HuntRpc = "RaidBoss_Hunt";
	const string PinRpc = "RaidBoss_Pin";
	internal static readonly int HealthKey = "raidboss_hp".GetStableHashCode();

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
		ZRoutedRpc.instance.Register<ZPackage>(CreatureRpc, RPC_Creature);
		ZRoutedRpc.instance.Register<ZPackage>(StrikeRpc, RPC_Strike);
		ZRoutedRpc.instance.Register<ZPackage>(ChaseRpc, RPC_Chase);
		ZRoutedRpc.instance.Register<ZPackage>(StormRpc, RPC_Storm);
		ZRoutedRpc.instance.Register<ZPackage>(FxRpc, RPC_Fx);
		ZRoutedRpc.instance.Register<ZPackage>(StatusRpc, RPC_Status);
		ZRoutedRpc.instance.Register<ZPackage>(EnvRpc, RPC_Env);
		ZRoutedRpc.instance.Register<ZPackage>(EventRpc, RPC_Event);
		ZRoutedRpc.instance.Register<ZPackage>(HuntRpc, RPC_Hunt);
		ZRoutedRpc.instance.Register<ZPackage>(PinRpc, RPC_Pin);
	}

	// ---- server -> everyone: the warband pins on the map (the whole list, every 20 s and on any change)

	internal static void SendPins(List<Warbands.Pin> pins)
	{
		if (ZRoutedRpc.instance == null) return;
		var pkg = new ZPackage();
		pkg.Write(pins.Count);
		foreach (Warbands.Pin p in pins)
		{
			pkg.Write(p.Id);
			pkg.Write(p.Pos);
			pkg.Write(p.Text ?? "");
		}
		ZRoutedRpc.instance.InvokeRoutedRPC(ZRoutedRpc.Everybody, PinRpc, pkg);
	}

	static void RPC_Pin(long sender, ZPackage pkg)
	{
		if (ZNet.instance == null || (!ZNet.instance.IsServer() && sender != ZRoutedRpc.instance.GetServerPeerID())) return;
		var list = new List<Warbands.Pin>();
		int count = pkg.ReadInt();
		for (int i = 0; i < count && i < 32; i++)
			list.Add(new Warbands.Pin { Id = pkg.ReadInt(), Pos = pkg.ReadVector3(), Text = pkg.ReadString() });
		Warbands.ApplyPins(list);
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

	// ---- server -> a creature's owner

	internal struct Op
	{
		public string Name, Key, Text;
		public float Value;
		public Op(string name, string key = "", string text = "", float value = 0f) { Name = name; Key = key; Text = text; Value = value; }
	}

	internal static void SendOps(long owner, ZDOID creature, params Op[] ops)
	{
		if (ZRoutedRpc.instance == null || owner == 0L || ops.Length == 0) return;
		var pkg = new ZPackage();
		pkg.Write(creature);
		pkg.Write(ops.Length);
		foreach (Op op in ops)
		{
			pkg.Write(op.Name ?? "");
			pkg.Write(op.Key ?? "");
			pkg.Write(op.Text ?? "");
			pkg.Write(op.Value);
		}
		ZRoutedRpc.instance.InvokeRoutedRPC(owner, CreatureRpc, pkg);
	}

	static bool FromServer(long sender) => ZNet.instance != null && ZRoutedRpc.instance != null && (ZNet.instance.IsServer() || sender == ZRoutedRpc.instance.GetServerPeerID());

	static void RPC_Creature(long sender, ZPackage pkg)
	{
		if (!FromServer(sender) || ZDOMan.instance == null) return;
		ZDOID id = pkg.ReadZDOID();
		int count = pkg.ReadInt();
		ZDO zdo = ZDOMan.instance.GetZDO(id);
		bool mine = zdo != null && zdo.IsOwner();
		for (int i = 0; i < count && i < 64; i++)
		{
			var op = new Op(pkg.ReadString(), pkg.ReadString(), pkg.ReadString(), pkg.ReadSingle());
			if (!mine) continue;
			try { Mechanics.Apply(zdo, op); }
			catch (System.Exception e) { RaidBossPlugin.Log.LogWarning($"operation '{op.Name}' failed: {e.Message}"); }
		}
	}

	// Desired values on creatures the server does not own: asked of the owner until the ZDO shows them (a message can
	// be lost, the owner can change). Only for values nothing else writes.
	static readonly Dictionary<ZDOID, Dictionary<string, object>> wanted = new Dictionary<ZDOID, Dictionary<string, object>>();
	static float wantTimer;

	internal static void Want(ZDOID creature, string key, float value) => WantValue(creature, key, value);
	internal static void Want(ZDOID creature, string key, string value) => WantValue(creature, key, value ?? "");

	static void WantValue(ZDOID creature, string key, object value)
	{
		if (!wanted.TryGetValue(creature, out Dictionary<string, object> keys)) wanted[creature] = keys = new Dictionary<string, object>();
		keys[key] = value;
		wantTimer = 99f;
	}

	internal static void Forget(ZDOID creature) => wanted.Remove(creature);

	internal static void TickWanted(float dt)
	{
		wantTimer += dt;
		if (wantTimer < 1f || wanted.Count == 0 || ZDOMan.instance == null) return;
		wantTimer = 0f;
		var gone = new List<ZDOID>();
		var ops = new List<Op>();
		foreach (KeyValuePair<ZDOID, Dictionary<string, object>> kv in wanted)
		{
			ZDO zdo = ZDOMan.instance.GetZDO(kv.Key);
			if (zdo == null) { gone.Add(kv.Key); continue; }
			ops.Clear();
			foreach (KeyValuePair<string, object> w in kv.Value)
			{
				int hash = w.Key.GetStableHashCode();
				if (w.Value is float f) { if (!Mathf.Approximately(zdo.GetFloat(hash, float.MinValue), f)) ops.Add(new Op("set_f", w.Key, "", f)); }
				else if (zdo.GetString(hash, "") != (string)w.Value) ops.Add(new Op("set_s", w.Key, (string)w.Value));
			}
			if (ops.Count > 0) SendOps(zdo.GetOwner(), kv.Key, ops.ToArray());
		}
		foreach (ZDOID id in gone) wanted.Remove(id);
	}

	// ---- server -> everyone

	internal static void SendStrike(Vector3 pos, float radius, float delay, string element, float damage, ZDOID attacker, string tellFx, string hitFx)
	{
		if (ZRoutedRpc.instance == null) return;
		var pkg = new ZPackage();
		pkg.Write(pos); pkg.Write(radius); pkg.Write(delay); pkg.Write(element ?? ""); pkg.Write(damage); pkg.Write(attacker); pkg.Write(tellFx ?? ""); pkg.Write(hitFx ?? "");
		ZRoutedRpc.instance.InvokeRoutedRPC(ZRoutedRpc.Everybody, StrikeRpc, pkg);
	}

	// A chase: strikes that follow one player (by player id) for a while. Every game draws them; each judges its own player.
	internal static void SendChase(long playerId, string element, float radius, float delay, float damage, float every, float seconds, ZDOID attacker, string hitFx)
	{
		if (ZRoutedRpc.instance == null) return;
		var pkg = new ZPackage();
		pkg.Write(playerId); pkg.Write(element ?? ""); pkg.Write(radius); pkg.Write(delay); pkg.Write(damage); pkg.Write(every); pkg.Write(seconds); pkg.Write(attacker); pkg.Write(hitFx ?? "");
		ZRoutedRpc.instance.InvokeRoutedRPC(ZRoutedRpc.Everybody, ChaseRpc, pkg);
	}

	static void RPC_Chase(long sender, ZPackage pkg)
	{
		if (!FromServer(sender)) return;
		Mechanics.Chase(pkg.ReadLong(), pkg.ReadString(), pkg.ReadSingle(), pkg.ReadSingle(), pkg.ReadSingle(), pkg.ReadSingle(), pkg.ReadSingle(), pkg.ReadZDOID(), pkg.ReadString());
	}

	// A storm: the sky turns to a thunderstorm and ground lightning falls around a place for a while. Every game makes its
	// own bolts (a third of them near its own player) and judges its own player; there is no warning ring.
	internal static void SendStorm(Vector3 center, float radius, float seconds, float every, float damage, ZDOID attacker, string boltFx, string weather)
	{
		if (ZRoutedRpc.instance == null) return;
		var pkg = new ZPackage();
		pkg.Write(center); pkg.Write(radius); pkg.Write(seconds); pkg.Write(every); pkg.Write(damage); pkg.Write(attacker); pkg.Write(boltFx ?? ""); pkg.Write(weather ?? "");
		ZRoutedRpc.instance.InvokeRoutedRPC(ZRoutedRpc.Everybody, StormRpc, pkg);
	}

	static void RPC_Storm(long sender, ZPackage pkg)
	{
		if (!FromServer(sender)) return;
		Mechanics.Storm(pkg.ReadVector3(), pkg.ReadSingle(), pkg.ReadSingle(), pkg.ReadSingle(), pkg.ReadSingle(), pkg.ReadZDOID(), pkg.ReadString(), pkg.ReadString());
	}

	static void RPC_Strike(long sender, ZPackage pkg)
	{
		if (!FromServer(sender)) return;
		Mechanics.Strike(pkg.ReadVector3(), pkg.ReadSingle(), pkg.ReadSingle(), pkg.ReadString(), pkg.ReadSingle(), pkg.ReadZDOID(), pkg.ReadString(), pkg.ReadString());
	}

	internal static void SendFx(string prefab, Vector3 pos)
	{
		if (ZRoutedRpc.instance == null) return;
		var pkg = new ZPackage();
		pkg.Write(prefab ?? ""); pkg.Write(pos);
		ZRoutedRpc.instance.InvokeRoutedRPC(ZRoutedRpc.Everybody, FxRpc, pkg);
	}

	static void RPC_Fx(long sender, ZPackage pkg)
	{
		if (!FromServer(sender)) return;
		Mechanics.PlayEffect(pkg.ReadString(), pkg.ReadVector3());
	}

	internal static void SendStatus(string effect, Vector3 pos, float radius, bool remove)
	{
		if (ZRoutedRpc.instance == null) return;
		var pkg = new ZPackage();
		pkg.Write(effect ?? ""); pkg.Write(pos); pkg.Write(radius); pkg.Write(remove);
		ZRoutedRpc.instance.InvokeRoutedRPC(ZRoutedRpc.Everybody, StatusRpc, pkg);
	}

	static void RPC_Status(long sender, ZPackage pkg)
	{
		if (!FromServer(sender)) return;
		Mechanics.PlayerStatus(pkg.ReadString(), pkg.ReadVector3(), pkg.ReadSingle(), pkg.ReadBool());
	}

	internal static void SendEnv(string env, Vector3 pos, float radius, float seconds)
	{
		if (ZRoutedRpc.instance == null) return;
		var pkg = new ZPackage();
		pkg.Write(env ?? ""); pkg.Write(pos); pkg.Write(radius); pkg.Write(seconds);
		ZRoutedRpc.instance.InvokeRoutedRPC(ZRoutedRpc.Everybody, EnvRpc, pkg);
	}

	static void RPC_Env(long sender, ZPackage pkg)
	{
		if (!FromServer(sender)) return;
		Mechanics.ForceWeather(pkg.ReadString(), pkg.ReadVector3(), pkg.ReadSingle(), pkg.ReadSingle());
	}

	// ---- a player -> server: the hunt button (RaidBossPlugin.DrawHuntButtons). Admins only on a dedicated server.

	internal static void RequestHunt(string boss, bool heroic, bool stop)
	{
		if (ZRoutedRpc.instance == null) return;
		var pkg = new ZPackage();
		pkg.Write(boss ?? ""); pkg.Write(heroic); pkg.Write(stop);
		ZRoutedRpc.instance.InvokeRoutedRPC(ZRoutedRpc.instance.GetServerPeerID(), HuntRpc, pkg);
	}

	static void RPC_Hunt(long sender, ZPackage pkg)
	{
		if (ZNet.instance == null || !ZNet.instance.IsServer()) return;
		string boss = pkg.ReadString();
		bool heroic = pkg.ReadBool(), stop = pkg.ReadBool();
		ZNetPeer peer = ZNet.instance.GetPeer(sender);
		string name = peer != null ? peer.m_playerName : (Player.m_localPlayer != null ? Player.m_localPlayer.GetPlayerName() : "");
		if (peer != null && ZNet.instance.IsDedicated() && !ZNet.instance.ListContainsId(ZNet.instance.m_adminList, peer.m_socket.GetHostName()))
		{
			RaidBossPlugin.Log.LogInfo($"hunt: {name} is not an admin; refused");
			ZRoutedRpc.instance.InvokeRoutedRPC(sender, "ShowMessage", (int)MessageHud.MessageType.Center, "Only an admin can start a hunt");
			return;
		}
		string said = stop ? Hunts.Stop() : Hunts.Begin(boss, heroic, name);
		RaidBossPlugin.Log.LogInfo($"hunt (button, {name}): {said}");
		if (stop || !said.Contains(" waves on ")) ZRoutedRpc.instance.InvokeRoutedRPC(sender, "ShowMessage", (int)MessageHud.MessageType.TopLeft, "Hunt: " + said);
	}

	// ---- a boss's owner -> server

	internal static void SendEvent(ZDOID creature, string what)
	{
		if (ZRoutedRpc.instance == null) return;
		var pkg = new ZPackage();
		pkg.Write(creature); pkg.Write(what ?? "");
		ZRoutedRpc.instance.InvokeRoutedRPC(ZRoutedRpc.instance.GetServerPeerID(), EventRpc, pkg);
	}

	static void RPC_Event(long sender, ZPackage pkg)
	{
		if (ZNet.instance == null || !ZNet.instance.IsServer()) return;
		Director.OnEvent(pkg.ReadZDOID(), pkg.ReadString());
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
