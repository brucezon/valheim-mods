using System;
using System.Collections.Generic;
using UnityEngine;

namespace BossDirector;

// The server never instantiates a creature, so it cannot "see" a boss fight. What it does have is every ZDO:
//   - a boss is a persistent ZDO, found by scanning the sectors around each connected player for boss prefab hashes;
//   - its owner (the client simulating it) writes ZDOVars.s_health on every change. The key is REMOVED at full health
//     (Character.Awake), so "absent" means 100%;
//   - the ZDO disappearing means the boss died.
// Adds are created the way ZNetView.Awake creates a ZDO, minus the GameObject: CreateNewZDO + Persistent/Type/Distant
// from the prefab + prefab + rotation, then handed to the boss's owner. That client's ZNetScene.CreateObjects
// instantiates any ZDO in its area that has no instance, and as owner it runs the creature's AI.
internal static class Director
{
	sealed class Add
	{
		public ZDOID Id;
		public string Prefab;
		public float Age;
		public bool Reported;
	}

	sealed class Fight
	{
		public ZDOID BossId;
		public string Prefab;
		public string RawScript;
		public Encounter Script;
		public bool[] Fired;
		public float[] Timers;
		public readonly List<Add> Adds = new List<Add>();
		public float LastFraction = 1f;
		public int LastPlayers;
		public float LastMultiplier = 1f;
	}

	struct PlayerPos { public long Uid; public Vector3 Pos; public ZDOID Character; }

	static readonly Dictionary<ZDOID, Fight> Fights = new Dictionary<ZDOID, Fight>();
	static readonly Dictionary<int, string> BossPrefabs = new Dictionary<int, string>();
	static readonly Dictionary<int, float> BossBaseHealth = new Dictionary<int, float>();
	static readonly List<PlayerPos> Players = new List<PlayerPos>();
	static readonly List<ZDO> Scan = new List<ZDO>();
	static readonly HashSet<Vector2s> ScannedZones = new HashSet<Vector2s>();
	static readonly List<ZDOID> Ended = new List<ZDOID>();
	static readonly int AddTag = "bossdirector_add".GetStableHashCode();
	// Read by BruceQoL 1.19+ on the victim's machine: hits from this creature on a player are scaled by it.
	static readonly int AddDamageKey = "bossdirector_dmg".GetStableHashCode();
	static float searchTimer = 999f;

	// Test hook: when set, used instead of the connected peers.
	internal static List<KeyValuePair<long, Vector3>> DebugPlayers;

	internal static IEnumerable<string> BossNames => BossPrefabs.Values;
	internal static int ActiveFights => Fights.Count;

	internal static void CollectBossPrefabs()
	{
		BossPrefabs.Clear();
		BossBaseHealth.Clear();
		foreach (GameObject prefab in ZNetScene.instance.m_prefabs)
		{
			if (prefab == null) continue;
			Character c = prefab.GetComponent<Character>();
			if (c == null || !c.m_boss || prefab.GetComponent<ZNetView>() == null) continue;
			int hash = prefab.name.GetStableHashCode();
			BossPrefabs[hash] = prefab.name;
			BossBaseHealth[hash] = c.m_health;
		}
	}

	static void GatherPlayers()
	{
		Players.Clear();
		if (DebugPlayers != null)
		{
			foreach (var kv in DebugPlayers) Players.Add(new PlayerPos { Uid = kv.Key, Pos = kv.Value });
			return;
		}
		foreach (ZNetPeer peer in ZNet.instance.GetPeers())
		{
			if (peer == null || !peer.IsReady() || peer.m_uid == 0L) continue;
			Players.Add(new PlayerPos { Uid = peer.m_uid, Pos = peer.m_refPos, Character = peer.m_characterID });
		}
		// A player hosting the world is a server too, and is not in the peer list.
		if (!ZNet.instance.IsDedicated())
			Players.Add(new PlayerPos { Uid = ZDOMan.GetSessionID(), Pos = ZNet.instance.GetReferencePosition(), Character = Player.m_localPlayer != null ? Player.m_localPlayer.GetZDOID() : ZDOID.None });
	}

	static float Flat(Vector3 a, Vector3 b)
	{
		float dx = a.x - b.x, dz = a.z - b.z;
		return Mathf.Sqrt(dx * dx + dz * dz);
	}

	internal static void Tick(float dt)
	{
		GatherPlayers();
		// Looking for a new boss walks every object around the players, so it runs rarely; a boss waits at full health
		// until it is found. A fight already known is followed by id, which costs nothing.
		searchTimer += dt;
		if (Players.Count > 0 && searchTimer >= BossDirectorPlugin.SearchInterval.Value)
		{
			searchTimer = 0f;
			FindBosses();
		}

		// Bigger waves go with softer adds: both are on exactly while boss fight mode is.
		Encounter.CountMultiplier = FightMode.Active ? BossDirectorPlugin.MoreAdds.Value : 1f;
		bool engaged = false;
		Ended.Clear();
		foreach (Fight fight in Fights.Values)
		{
			ZDO boss = ZDOMan.instance.GetZDO(fight.BossId);
			if (boss == null || !boss.IsValid() || boss.GetPrefab() == 0) { Ended.Add(fight.BossId); continue; }
			try { engaged |= UpdateFight(fight, boss, dt); }
			catch (Exception e) { BossDirectorPlugin.Log.LogError($"{fight.Prefab}: {e}"); }
		}
		foreach (ZDOID id in Ended) EndFight(id);
		FightMode.Tick(engaged, dt);
	}

	static void FindBosses()
	{
		ScannedZones.Clear();
		SimulationDistance synced = ZNet.instance.GetSyncedSimulationDistance();
		var near = new SimulationDistance(synced.NearSimulationDistance, 0, synced.IsClassic);
		foreach (PlayerPos p in Players)
		{
			Vector2s zone = ZoneSystem.GetZone(p.Pos);
			if (!ScannedZones.Add(zone)) continue;
			Scan.Clear();
			ZDOMan.instance.FindSectorObjects(zone, near, Scan);
			foreach (ZDO zdo in Scan)
			{
				int hash = zdo.GetPrefab();
				if (!BossPrefabs.TryGetValue(hash, out string name) || Fights.ContainsKey(zdo.m_uid)) continue;
				if (zdo.GetBool(ZDOVars.s_tamed)) continue;
				StartFight(zdo, name);
			}
		}
		Scan.Clear();
	}

	static float Fraction(ZDO boss)
	{
		int level = Mathf.Max(1, boss.GetInt(ZDOVars.s_level, 1));
		float fallback = (BossBaseHealth.TryGetValue(boss.GetPrefab(), out float b) ? b : 1000f) * level;
		float max = boss.GetFloat(ZDOVars.s_maxHealth, fallback);
		if (max <= 0f) max = fallback;
		return Mathf.Clamp01(boss.GetFloat(ZDOVars.s_health, max) / max);
	}

	static void LoadScript(Fight fight, bool firstSight, float fraction)
	{
		string raw = BossDirectorPlugin.ScriptFor(fight.Prefab);
		if (fight.Script != null && raw == fight.RawScript) return;
		bool[] oldFired = fight.Fired;
		Encounter old = fight.Script;
		fight.RawScript = raw;
		fight.Script = Encounter.Parse(raw);
		foreach (string err in fight.Script.Errors) BossDirectorPlugin.Log.LogWarning($"{fight.Prefab} script: {err}");
		fight.Fired = new bool[fight.Script.Rules.Count];
		fight.Timers = new float[fight.Script.Rules.Count];
		for (int i = 0; i < fight.Script.Rules.Count; i++)
		{
			Encounter.Rule rule = fight.Script.Rules[i];
			if (rule.Repeating) continue;
			// A threshold the boss is already past never fires late: not after a restart mid-fight, and not when the
			// script is edited mid-fight.
			if (firstSight || old == null) fight.Fired[i] = fraction < 0.999f && fraction <= rule.Threshold;
			else fight.Fired[i] = fraction <= rule.Threshold;
		}
		if (!firstSight) BossDirectorPlugin.Log.LogInfo($"{fight.Prefab}: script reloaded mid-fight at {fraction * 100f:0}% health");
	}

	static void StartFight(ZDO boss, string name)
	{
		var fight = new Fight { BossId = boss.m_uid, Prefab = name };
		float fraction = Fraction(boss);
		LoadScript(fight, true, fraction);
		fight.LastFraction = fraction;
		Fights[boss.m_uid] = fight;
		BossDirectorPlugin.Log.LogInfo($"found {name} {boss.m_uid} at {boss.GetPosition():0} health {fraction * 100f:0}% owner {boss.GetOwner()}; " +
			(fight.Script.Rules.Count == 0 ? "no script for this boss" : $"script for 1 player: {fight.Script.Describe(1)}"));
	}

	static void EndFight(ZDOID id)
	{
		if (!Fights.TryGetValue(id, out Fight fight)) return;
		Fights.Remove(id);
		int removed = 0, alive = 0;
		foreach (Add add in fight.Adds)
		{
			ZDO zdo = ZDOMan.instance.GetZDO(add.Id);
			if (zdo == null || !zdo.IsValid() || !zdo.GetBool(AddTag)) continue;
			alive++;
			if (!BossDirectorPlugin.RemoveAddsOnDeath.Value) continue;
			// Same two calls vanilla uses on the server for a ZDO it wants gone (ZDOMan.RPC_ZDOData, dead ZDOs).
			zdo.SetOwner(ZDOMan.GetSessionID());
			ZDOMan.instance.DestroyZDO(zdo);
			removed++;
		}
		BossDirectorPlugin.Log.LogInfo($"{fight.Prefab} {id} is gone (last seen at {fight.LastFraction * 100f:0}%). Adds alive {alive}, removed {removed}.");
	}

	static int CountAlive(Fight fight)
	{
		int alive = 0;
		for (int i = fight.Adds.Count - 1; i >= 0; i--)
		{
			ZDO zdo = ZDOMan.instance.GetZDO(fight.Adds[i].Id);
			// ZDOs are pooled: an id that resolves to something without our tag has been reused.
			if (zdo == null || !zdo.IsValid() || !zdo.GetBool(AddTag)) { fight.Adds.RemoveAt(i); continue; }
			alive++;
		}
		return alive;
	}

	// Returns whether anybody is in range of this boss (which is what keeps boss fight mode on).
	static bool UpdateFight(Fight fight, ZDO boss, float dt)
	{
		Vector3 bossPos = boss.GetPosition();
		float range = BossDirectorPlugin.Range.Value;
		int players = 0;
		foreach (PlayerPos p in Players) if (Flat(p.Pos, bossPos) <= range) players++;
		if (players == 0) return false;   // nobody here: timers pause, nothing fires
		if (Encounter.CountMultiplier != fight.LastMultiplier)
		{
			fight.LastMultiplier = Encounter.CountMultiplier;
			fight.LastPlayers = -1;   // print the script again with the new counts
		}
		if (BossDirectorPlugin.ForcePlayers.Value > 0) players = BossDirectorPlugin.ForcePlayers.Value;

		float fraction = Fraction(boss);
		LoadScript(fight, false, fraction);
		if (players != fight.LastPlayers)
		{
			BossDirectorPlugin.Log.LogInfo($"{fight.Prefab}: {players} player(s) engaged. Script now: {fight.Script.Describe(players)}");
			fight.LastPlayers = players;
		}
		fight.LastFraction = fraction;
		ReportAdoption(fight, dt);

		bool hurt = fraction < 0.999f;
		for (int i = 0; i < fight.Script.Rules.Count; i++)
		{
			Encounter.Rule rule = fight.Script.Rules[i];
			if (!rule.Repeating)
			{
				if (fight.Fired[i] || fraction > rule.Threshold) continue;
				fight.Fired[i] = true;
				BossDirectorPlugin.Log.LogInfo($"{fight.Prefab}: {rule.Threshold * 100f:0}% wave at {fraction * 100f:0.0}% health, {players} player(s)");
				SpawnRule(fight, boss, rule, players, int.MaxValue);
				continue;
			}
			if (!hurt || fraction >= rule.Below || fraction < rule.Above) { fight.Timers[i] = 0f; continue; }
			fight.Timers[i] += dt;
			if (fight.Timers[i] < rule.Interval) continue;
			fight.Timers[i] = 0f;
			int cap = Mathf.FloorToInt((BossDirectorPlugin.CapBase.Value + BossDirectorPlugin.CapPerPlayer.Value * players) * Encounter.CountMultiplier + 0.001f);
			int room = cap - CountAlive(fight);
			if (room <= 0) continue;
			SpawnRule(fight, boss, rule, players, room);
		}
		return true;
	}

	static void SpawnRule(Fight fight, ZDO boss, Encounter.Rule rule, int players, int room)
	{
		int made = 0;
		foreach (Encounter.Spawn spawn in rule.Spawns)
		{
			int count = Mathf.Min(spawn.Count(players), room - made);
			for (int i = 0; i < count; i++) if (SpawnAdd(fight, boss, spawn)) made++;
		}
		if (made > 0 && rule.Message.Length > 0) Message(boss.GetPosition(), rule.Message);
	}

	static bool SpawnAdd(Fight fight, ZDO boss, Encounter.Spawn spawn)
	{
		GameObject prefab = ZNetScene.instance.GetPrefab(spawn.Prefab);
		ZNetView view = prefab != null ? prefab.GetComponent<ZNetView>() : null;
		if (view == null || prefab.GetComponent<Character>() == null)
		{
			BossDirectorPlugin.Log.LogWarning($"{fight.Prefab}: '{spawn.Prefab}' is not a creature prefab, skipped");
			return false;
		}
		Vector3 bossPos = boss.GetPosition();
		long owner = boss.GetOwner();
		Vector3 pos = bossPos;
		bool placed = false;
		for (int attempt = 0; attempt < 10 && !placed; attempt++)
		{
			float angle = UnityEngine.Random.Range(0f, Mathf.PI * 2f);
			float dist = UnityEngine.Random.Range(BossDirectorPlugin.RingMin.Value, Mathf.Max(BossDirectorPlugin.RingMin.Value, BossDirectorPlugin.RingMax.Value));
			pos = new Vector3(bossPos.x + Mathf.Cos(angle) * dist, 0f, bossPos.z + Mathf.Sin(angle) * dist);
			pos.y = GroundHeight(pos);
			placed = pos.y > ZoneSystem.instance.m_waterLevel + 0.3f;
		}
		if (!placed) pos.y = Mathf.Max(pos.y, ZoneSystem.instance.m_waterLevel + 0.5f);
		pos.y += 0.5f;

		// The owner must be someone who has the area loaded. The boss's owner always does.
		if (owner == 0L || (DebugPlayers == null && owner == ZDOMan.GetSessionID() && ZNet.instance.IsDedicated())) owner = NearestPlayer(pos);

		int hash = prefab.name.GetStableHashCode();
		ZDO zdo = ZDOMan.instance.CreateNewZDO(pos, hash);
		zdo.Persistent = view.m_persistent;
		zdo.Type = view.m_type;
		zdo.Distant = view.m_distant;
		zdo.SetPrefab(hash);
		Vector3 facing = bossPos - pos; facing.y = 0f;
		zdo.SetRotation(facing.sqrMagnitude > 0.01f ? Quaternion.LookRotation(facing) : Quaternion.identity);
		if (spawn.Level > 1) zdo.Set(ZDOVars.s_level, spawn.Level);
		if (BossDirectorPlugin.AddsHunt.Value) zdo.Set(ZDOVars.s_huntPlayer, true);
		zdo.Set(AddTag, true);
		float dmg = BossDirectorPlugin.AddDamage.Value;
		if (dmg > 0f && !Mathf.Approximately(dmg, 1f)) zdo.Set(AddDamageKey, dmg);
		float hp = BossDirectorPlugin.AddHealth.Value;
		if (hp > 0f && hp < 0.999f)
		{
			// Character.Awake only recomputes max health when health == max, so both are written: the add arrives
			// with the right maximum for its level and already hurt. Its health bar starts part empty.
			float max = prefab.GetComponent<Character>().m_health * Mathf.Max(1, spawn.Level);
			zdo.Set(ZDOVars.s_maxHealth, max);
			zdo.Set(ZDOVars.s_health, max * hp);
		}
		if (owner != 0L) zdo.SetOwner(owner);

		fight.Adds.Add(new Add { Id = zdo.m_uid, Prefab = prefab.name });
		BossDirectorPlugin.Log.LogInfo($"{fight.Prefab}: spawned {prefab.name}{new string('*', spawn.Level - 1)} {zdo.m_uid} at {pos:0.0} ({Flat(pos, bossPos):0} m from the boss) owner {owner}");
		return true;
	}

	// The dedicated server has no terrain colliders. WorldGenerator's height is pure math but knows nothing about
	// terrain a location or a player has flattened or raised, so where a player stands nearby and higher, trust that.
	static float GroundHeight(Vector3 pos)
	{
		float gen = WorldGenerator.instance.GetHeight(pos.x, pos.z);
		float best = gen;
		float nearest = float.MaxValue;
		foreach (PlayerPos p in Players)
		{
			float d = Flat(p.Pos, pos);
			if (d >= nearest || d > 40f) continue;
			nearest = d;
			float dy = p.Pos.y - gen;
			best = dy > 0f && dy < 8f ? p.Pos.y : gen;
		}
		return best;
	}

	static long NearestPlayer(Vector3 pos)
	{
		long uid = 0L; float best = float.MaxValue;
		foreach (PlayerPos p in Players)
		{
			float d = Flat(p.Pos, pos);
			if (d < best) { best = d; uid = p.Uid; }
		}
		return uid;
	}

	// The thing this whole design rests on: does a client pick up a creature the server created? Logged once per add.
	static void ReportAdoption(Fight fight, float dt)
	{
		foreach (Add add in fight.Adds)
		{
			if (add.Reported) continue;
			add.Age += dt;
			if (add.Age < 5f) continue;
			add.Reported = true;
			ZDO zdo = ZDOMan.instance.GetZDO(add.Id);
			if (zdo == null || !zdo.IsValid()) { BossDirectorPlugin.Log.LogInfo($"{fight.Prefab}: {add.Prefab} {add.Id} is already gone 5 s after spawning (killed, or rejected)"); continue; }
			// s_spawnTime is written by BaseAI.Awake on the owner, so its presence proves a client instantiated the creature.
			bool awake = zdo.GetLong(ZDOVars.s_spawnTime, 0L) != 0L;
			BossDirectorPlugin.Log.LogInfo($"{fight.Prefab}: {add.Prefab} {add.Id} after 5 s: owner {zdo.GetOwner()}, instantiated by a client: {(awake ? "YES" : "NO")}, at {zdo.GetPosition():0.0}");
		}
	}

	static void Message(Vector3 center, string text)
	{
		if (DebugPlayers != null) return;
		float range = BossDirectorPlugin.Range.Value;
		foreach (PlayerPos p in Players)
			if (Flat(p.Pos, center) <= range)
				ZRoutedRpc.instance.InvokeRoutedRPC(p.Uid, "ShowMessage", (int)MessageHud.MessageType.Center, text);
	}

	internal static void Reset()
	{
		Fights.Clear();
	}
}
