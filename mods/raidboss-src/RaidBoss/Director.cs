using System;
using System.Collections.Generic;
using UnityEngine;

namespace RaidBoss;

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
		// Heroic fight: the boss's own trophy was lying by the boss before anyone hurt it. Harder, and it pays idols.
		public bool Heroic;
		public int HealthAsks;          // heroic health: requests sent to the boss's owner so far
		public int MeterAsks;
		public float MechTimer;
		public readonly List<Wave> Waves = new List<Wave>();
		public readonly Dictionary<Encounter.Act, int> Cycle = new Dictionary<Encounter.Act, int>();
		public int TrophyHash;
		public string TrophyName;
		public int MaxPlayers;          // most real players seen in range at once; decides the idols
		public Vector3 LastPos;
		public bool Killed;             // a client reported the boss's death
	}

	struct PlayerPos { public long Uid; public Vector3 Pos; public ZDOID Character; }

	static readonly Dictionary<ZDOID, Fight> Fights = new Dictionary<ZDOID, Fight>();
	static readonly Dictionary<int, string> BossPrefabs = new Dictionary<int, string>();
	static readonly Dictionary<int, float> BossBaseHealth = new Dictionary<int, float>();
	static readonly List<PlayerPos> Players = new List<PlayerPos>();
	static readonly List<ZDO> Scan = new List<ZDO>();
	static readonly HashSet<Vector2s> ScannedZones = new HashSet<Vector2s>();
	static readonly List<ZDOID> Ended = new List<ZDOID>();
	static readonly int AddTag = "raidboss_add".GetStableHashCode();
	// Read on the victim's machine (ClientSide.cs): hits from this creature on a player are scaled by it.
	static readonly int AddDamageKey = "raidboss_dmg".GetStableHashCode();
	static float searchTimer = 999f;
	static float BaseMultiplier = 1f;   // the boss-fight-mode wave multiplier this tick, before a heroic fight's own

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
		if (Players.Count > 0 && searchTimer >= RaidBossPlugin.SearchInterval.Value)
		{
			searchTimer = 0f;
			FindBosses();
		}

		// Every player runs this mod, so every player takes the softened add damage: the bigger waves always apply.
		BaseMultiplier = RaidBossPlugin.MoreAdds.Value;
		Encounter.CountMultiplier = BaseMultiplier;
		Ended.Clear();
		Net.ActiveFights.Clear();
		foreach (Fight fight in Fights.Values)
		{
			ZDO boss = ZDOMan.instance.GetZDO(fight.BossId);
			if (boss == null || !boss.IsValid() || boss.GetPrefab() == 0) { Ended.Add(fight.BossId); continue; }
			try
			{
				// What a boss does to a player is worked out on that player's game, so the clients are told which
				// bosses are being fought and how hard each one hits.
				if (UpdateFight(fight, boss, dt))
					Net.ActiveFights[fight.BossId] = RaidBossPlugin.BossDamage.Value * (fight.Heroic ? RaidBossPlugin.HeroicBossDamageFor(fight.Prefab) : 1f);
			}
			catch (Exception e) { RaidBossPlugin.Log.LogError($"{fight.Prefab}: {e}"); }
		}
		foreach (ZDOID id in Ended) EndFight(id);
		Encounter.CountMultiplier = BaseMultiplier;
		Net.BroadcastFights(dt);
		Net.TickWanted(dt);
		TickEffects(dt);
	}

	// A client reported that a creature it was simulating died. Exact, unlike watching ZDOs vanish.
	internal static void OnKill(ZDOID id)
	{
		if (Fights.TryGetValue(id, out Fight fight)) fight.Killed = true;
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
		string raw = RaidBossPlugin.ScriptFor(fight.Prefab);
		if (fight.Script != null && raw == fight.RawScript) return;
		bool[] oldFired = fight.Fired;
		Encounter old = fight.Script;
		fight.RawScript = raw;
		fight.Script = Encounter.Parse(raw);
		foreach (string err in fight.Script.Errors) RaidBossPlugin.Log.LogWarning($"{fight.Prefab} script: {err}");
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
		if (!firstSight) RaidBossPlugin.Log.LogInfo($"{fight.Prefab}: script reloaded mid-fight at {fraction * 100f:0}% health");
	}

	static void StartFight(ZDO boss, string name)
	{
		var fight = new Fight { BossId = boss.m_uid, Prefab = name, LastPos = boss.GetPosition() };
		float fraction = Fraction(boss);
		LoadScript(fight, true, fraction);
		fight.LastFraction = fraction;
		Fights[boss.m_uid] = fight;
		// The trophy this boss drops is the one that challenges it: the first "Trophy..." entry of its own drop table.
		CharacterDrop drops = ZNetScene.instance.GetPrefab(name)?.GetComponent<CharacterDrop>();
		if (drops != null)
			foreach (CharacterDrop.Drop d in drops.m_drops)
				if (d.m_prefab != null && d.m_prefab.name.StartsWith("Trophy", StringComparison.Ordinal))
				{
					fight.TrophyName = d.m_prefab.name;
					fight.TrophyHash = d.m_prefab.name.GetStableHashCode();
					break;
				}
		RaidBossPlugin.Log.LogInfo($"found {name} {boss.m_uid} at {boss.GetPosition():0} health {fraction * 100f:0}% owner {boss.GetOwner()}; " +
			(fight.Script.Rules.Count == 0 ? "no script for this boss" : $"script for 1 player: {fight.Script.Describe(1)}"));
	}

	static void EndFight(ZDOID id)
	{
		if (!Fights.TryGetValue(id, out Fight fight)) return;
		Fights.Remove(id);
		Net.Forget(id);
		int removed = 0, alive = 0;
		foreach (Add add in fight.Adds)
		{
			ZDO zdo = ZDOMan.instance.GetZDO(add.Id);
			if (zdo == null || !zdo.IsValid() || !zdo.GetBool(AddTag)) continue;
			alive++;
			if (!RaidBossPlugin.RemoveAddsOnDeath.Value) continue;
			// Same two calls vanilla uses on the server for a ZDO it wants gone (ZDOMan.RPC_ZDOData, dead ZDOs).
			zdo.SetOwner(ZDOMan.GetSessionID());
			ZDOMan.instance.DestroyZDO(zdo);
			removed++;
		}
		RaidBossPlugin.Log.LogInfo($"{fight.Prefab} {id} is gone (last seen at {fight.LastFraction * 100f:0}%). Adds alive {alive}, removed {removed}.");
		if (fight.Heroic) PayIdols(fight);
	}

	// A kill is what the boss's owner reported (Net.cs). "Gone while nearly dead" is kept as a fallback for a report lost
	// on the way; a boss removed by an admin at full health pays nothing.
	static void PayIdols(Fight fight)
	{
		if (!fight.Killed && fight.LastFraction > 0.1f)
		{
			RaidBossPlugin.Log.LogInfo($"{fight.Prefab}: heroic fight ended with the boss at {fight.LastFraction * 100f:0}% - not a kill, no idols");
			return;
		}
		int tier = RaidBossPlugin.IdolTierFor(fight.Prefab);
		int count = Mathf.Max(0, Mathf.FloorToInt(RaidBossPlugin.IdolBase.Value + RaidBossPlugin.IdolPerPlayer.Value * fight.MaxPlayers + 0.001f));
		if (tier < 0 || count == 0)
		{
			RaidBossPlugin.Log.LogInfo($"{fight.Prefab}: heroic kill by up to {fight.MaxPlayers} player(s): no idols ({(tier < 0 ? "no idol tier is set for this boss" : "too few players")})");
			return;
		}
		GatherPlayers();
		long owner = NearestPlayer(fight.LastPos);
		var given = new List<string>();
		for (int i = 0; i < count; i++)
		{
			bool battle = UnityEngine.Random.value < RaidBossPlugin.IdolBattleShare.Value;
			string item = $"Upgrader{tier}{(battle ? "Weapon" : "Armor")}";
			if (SpawnItem(item, fight.LastPos, owner)) given.Add(item);
		}
		RaidBossPlugin.Log.LogInfo($"{fight.Prefab}: heroic kill by up to {fight.MaxPlayers} player(s): dropped {given.Count} idol(s): {string.Join(", ", given)}");
	}

	// An item lying in the world is its own prefab with an ItemDrop on it; a bare ZDO of that prefab is a stack of one.
	static bool SpawnItem(string prefabName, Vector3 at, long owner)
	{
		GameObject prefab = ZNetScene.instance.GetPrefab(prefabName);
		ZNetView view = prefab != null ? prefab.GetComponent<ZNetView>() : null;
		if (view == null || prefab.GetComponent<ItemDrop>() == null)
		{
			RaidBossPlugin.Log.LogWarning($"'{prefabName}' is not an item in this game, skipped");
			return false;
		}
		Vector2 off = UnityEngine.Random.insideUnitCircle * 2f;
		Vector3 pos = new Vector3(at.x + off.x, 0f, at.z + off.y);
		// A flying boss dies in the air; items go to the ground under it, a little above it so they fall rather than sink.
		pos.y = Mathf.Max(GroundHeight(pos), ZoneSystem.instance.m_waterLevel) + 1.5f;
		int hash = prefab.name.GetStableHashCode();
		ZDO zdo = ZDOMan.instance.CreateNewZDO(pos, hash);
		zdo.Persistent = view.m_persistent;
		zdo.Type = view.m_type;
		zdo.Distant = view.m_distant;
		zdo.SetPrefab(hash);
		zdo.SetRotation(Quaternion.identity);
		if (owner != 0L) zdo.SetOwner(owner);
		return true;
	}

	// A player pressed Shift + Use on the altar: the altar's ZDO carries "raidboss_heroic". Any object near the boss
	// with that flag counts, so nothing here needs to know what each altar prefab is called. The flag is cleared by
	// taking the object for a moment; it goes back to a nearby player like any other persistent object.
	static bool TakeAltarChallenge(Vector3 bossPos)
	{
		float radius = RaidBossPlugin.HeroicAltarRadius.Value;
		SimulationDistance synced = ZNet.instance.GetSyncedSimulationDistance();
		Scan.Clear();
		ZDOMan.instance.FindSectorObjects(ZoneSystem.GetZone(bossPos), new SimulationDistance(1, 0, synced.IsClassic), Scan);
		ZDO altar = null;
		foreach (ZDO zdo in Scan)
		{
			if (!zdo.GetBool(ClientSide.AltarKey) || Flat(zdo.GetPosition(), bossPos) > radius) continue;
			altar = zdo;
			break;
		}
		Scan.Clear();
		if (altar == null) return false;
		altar.SetOwner(ZDOMan.GetSessionID());
		altar.Set(ClientSide.AltarKey, false);
		return true;
	}

	// Looks for the boss's own trophy lying near it. If there is one, it is taken (the whole stack that was dropped).
	static bool TakeTrophy(Fight fight, Vector3 bossPos)
	{
		if (fight.TrophyHash == 0) return false;
		float radius = RaidBossPlugin.HeroicTrophyRadius.Value;
		SimulationDistance synced = ZNet.instance.GetSyncedSimulationDistance();
		Scan.Clear();
		ZDOMan.instance.FindSectorObjects(ZoneSystem.GetZone(bossPos), new SimulationDistance(1, 0, synced.IsClassic), Scan);
		ZDO found = null;
		foreach (ZDO zdo in Scan)
		{
			if (zdo.GetPrefab() != fight.TrophyHash || Flat(zdo.GetPosition(), bossPos) > radius) continue;
			found = zdo;
			break;
		}
		Scan.Clear();
		if (found == null) return false;
		found.SetOwner(ZDOMan.GetSessionID());
		ZDOMan.instance.DestroyZDO(found);
		return true;
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
		float range = RaidBossPlugin.Range.Value;
		int players = 0;
		foreach (PlayerPos p in Players) if (Flat(p.Pos, bossPos) <= range) players++;
		if (players == 0) return false;   // nobody here: timers pause, nothing fires
		fight.LastPos = bossPos;
		if (players > fight.MaxPlayers) fight.MaxPlayers = players;

		// The challenge has to be made before the first blow: once the boss is hurt, a trophy on the ground is just a trophy.
		// Two ways to make it: Shift + Use on the altar (ClientSide.cs; the altar carries the challenge), or the older one,
		// the boss's trophy lying on the ground near the boss.
		string how = null;
		if (!fight.Heroic && RaidBossPlugin.HeroicEnabled.Value && Fraction(boss) >= 0.999f)
		{
			if (TakeAltarChallenge(bossPos)) how = "the altar carried a challenge";
			else if (TakeTrophy(fight, bossPos)) how = $"a {fight.TrophyName} lay within {RaidBossPlugin.HeroicTrophyRadius.Value:0} m and was taken";
		}
		if (how != null)
		{
			fight.Heroic = true;
			RaidBossPlugin.Log.LogInfo($"{fight.Prefab}: {how}. HEROIC fight: boss damage x{RaidBossPlugin.BossDamage.Value * RaidBossPlugin.HeroicBossDamageFor(fight.Prefab):0.##}, boss health x{RaidBossPlugin.HeroicBossHealth.Value:0.##}, waves x{RaidBossPlugin.HeroicMoreAdds.Value:0.##}, every wave carries a star, idols on the kill.");
			if (RaidBossPlugin.HeroicMessage.Value.Length > 0) Message(bossPos, RaidBossPlugin.HeroicMessage.Value);
		}
		// Heroic health: asked of the boss's owner until the boss carries the mark (the owner can change in between).
		float heroicHealth = RaidBossPlugin.HeroicBossHealth.Value;
		if (fight.Heroic && !Mathf.Approximately(heroicHealth, 1f) && fight.HealthAsks < 30 && boss.GetOwner() != 0L && boss.GetFloat(Net.HealthKey, 0f) == 0f)
		{
			fight.HealthAsks++;
			Net.SendOps(boss.GetOwner(), fight.BossId, new Net.Op("hpmult", "", "", heroicHealth));
		}
		fight.MechTimer += dt;
		if (fight.MechTimer >= 1f) { fight.MechTimer = 0f; TickMechanics(fight, boss); }
		Encounter.CountMultiplier = BaseMultiplier * (fight.Heroic ? RaidBossPlugin.HeroicMoreAdds.Value : 1f);
		if (Encounter.CountMultiplier != fight.LastMultiplier)
		{
			fight.LastMultiplier = Encounter.CountMultiplier;
			fight.LastPlayers = -1;   // print the script again with the new counts
		}
		if (RaidBossPlugin.ForcePlayers.Value > 0) players = RaidBossPlugin.ForcePlayers.Value;

		float fraction = Fraction(boss);
		LoadScript(fight, false, fraction);
		if (players != fight.LastPlayers)
		{
			RaidBossPlugin.Log.LogInfo($"{fight.Prefab}: {players} player(s) engaged. Script now: {fight.Script.Describe(players)}");
			fight.LastPlayers = players;
		}
		fight.LastFraction = fraction;
		ReportAdoption(fight, dt);

		bool hurt = fraction < 0.999f;
		for (int i = 0; i < fight.Script.Rules.Count; i++)
		{
			Encounter.Rule rule = fight.Script.Rules[i];
			if (rule.HeroicOnly && !fight.Heroic) continue;
			if (rule.NormalOnly && fight.Heroic) continue;
			if (!rule.Repeating)
			{
				if (fight.Fired[i] || fraction > rule.Threshold) continue;
				fight.Fired[i] = true;
				RaidBossPlugin.Log.LogInfo($"{fight.Prefab}: {rule.Threshold * 100f:0}% wave at {fraction * 100f:0.0}% health, {players} player(s)");
				int before = fight.Adds.Count;
				SpawnRule(fight, boss, rule, players, int.MaxValue);
				AfterRule(fight, boss, rule, before);
				continue;
			}
			if (!hurt || fraction >= rule.Below || fraction < rule.Above) { fight.Timers[i] = 0f; continue; }
			fight.Timers[i] += dt;
			if (fight.Timers[i] < rule.Interval) continue;
			fight.Timers[i] = 0f;
			int cap = Mathf.FloorToInt((RaidBossPlugin.CapBase.Value + RaidBossPlugin.CapPerPlayer.Value * players) * Encounter.CountMultiplier + 0.001f);
			int room = cap - CountAlive(fight);
			if (room <= 0 && rule.Spawns.Count > 0) continue;
			int beforeRepeat = fight.Adds.Count;
			if (rule.Spawns.Count > 0) SpawnRule(fight, boss, rule, players, room);
			AfterRule(fight, boss, rule, beforeRepeat);
		}
		return true;
	}

	// ---- vanilla effects. Every fx_/vfx_/sfx_ prefab in the game is a networked object with its own lifetime, so the
	// server shows one the way it makes an add: a bare ZDO handed to a nearby player. No client code is involved.

	sealed class PendingFx { public float At; public string Names; public Vector3 Pos; }
	static readonly List<PendingFx> pendingFx = new List<PendingFx>();
	static readonly List<KeyValuePair<float, ZDOID>> madeFx = new List<KeyValuePair<float, ZDOID>>();
	static float clock;

	internal static void ShowEffect(string names, Vector3 pos, float delay = 0f)
	{
		if (string.IsNullOrWhiteSpace(names)) return;
		if (delay > 0f) { pendingFx.Add(new PendingFx { At = clock + delay, Names = names, Pos = pos }); return; }
		foreach (string raw in names.Split('+'))
		{
			GameObject prefab = ZNetScene.instance.GetPrefab(raw.Trim());
			ZNetView view = prefab != null ? prefab.GetComponent<ZNetView>() : null;
			if (view == null || prefab.GetComponent<Character>() != null) { RaidBossPlugin.Log.LogWarning($"effect '{raw.Trim()}' is not an effect prefab, skipped"); continue; }
			int hash = prefab.name.GetStableHashCode();
			ZDO zdo = ZDOMan.instance.CreateNewZDO(pos, hash);
			zdo.Persistent = false;
			zdo.Type = view.m_type;
			zdo.Distant = view.m_distant;
			zdo.SetPrefab(hash);
			zdo.SetRotation(Quaternion.identity);
			long owner = NearestPlayer(pos);
			if (owner != 0L) zdo.SetOwner(owner);
			madeFx.Add(new KeyValuePair<float, ZDOID>(clock + 20f, zdo.m_uid));
		}
	}

	static void TickEffects(float dt)
	{
		clock += dt;
		for (int i = pendingFx.Count - 1; i >= 0; i--)
		{
			if (pendingFx[i].At > clock) continue;
			PendingFx fx = pendingFx[i];
			pendingFx.RemoveAt(i);
			try { ShowEffect(fx.Names, fx.Pos); } catch (Exception e) { RaidBossPlugin.Log.LogWarning($"effect failed: {e.Message}"); }
		}
		// an effect without its own lifetime, or one nobody picked up, is cleared away after 20 s
		for (int i = madeFx.Count - 1; i >= 0; i--)
		{
			if (madeFx[i].Key > clock) continue;
			ZDO zdo = ZDOMan.instance.GetZDO(madeFx[i].Value);
			madeFx.RemoveAt(i);
			if (zdo == null || !zdo.IsValid() || zdo.Persistent) continue;
			zdo.SetOwner(ZDOMan.GetSessionID());
			ZDOMan.instance.DestroyZDO(zdo);
		}
	}

	// ---- mechanics: what a rule does besides spawning (Encounter.Act), carried out through the general client tools

	// The adds one rule sent, remembered until they are all dead: that ends a ward, feeds the break meter, and can break the boss.
	sealed class Wave
	{
		public readonly List<ZDOID> Ids = new List<ZDOID>();
		public bool Ward, BreakAfter;
	}

	internal static void OnEvent(ZDOID creature, string what)
	{
		if (Fights.TryGetValue(creature, out Fight fight)) RaidBossPlugin.Log.LogInfo($"{fight.Prefab}: {what} (reported by the boss's owner)");
	}

	static float Arg(string[] args, string prefix, float fallback)
	{
		foreach (string a in args)
			if (a.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && float.TryParse(a.Substring(prefix.Length), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float v)) return v;
		return fallback;
	}

	static void AfterRule(Fight fight, ZDO boss, Encounter.Rule rule, int addsBefore)
	{
		Wave wave = null;
		if (!rule.Repeating && fight.Adds.Count > addsBefore)
		{
			wave = new Wave();
			for (int i = addsBefore; i < fight.Adds.Count; i++) wave.Ids.Add(fight.Adds[i].Id);
			fight.Waves.Add(wave);
		}
		if (rule.Actions.Count == 0) return;
		if (rule.Spawns.Count == 0 && rule.Message.Length > 0) Message(boss.GetPosition(), rule.Message);
		Vector3 bossPos = boss.GetPosition();
		foreach (Encounter.Act act in rule.Actions)
		{
			try
			{
				string first = act.Args.Length > 0 ? act.Args[0] : "";
				switch (act.Verb)
				{
					case "ward":     // ward 0.5 [break]: the boss takes x0.5 until this rule's adds are dead; "break" = then it breaks
						if (wave == null) break;
						wave.Ward = true;
						wave.BreakAfter = Array.Exists(act.Args, a => a.Equals("break", StringComparison.OrdinalIgnoreCase));
						Net.Want(fight.BossId, "raidboss_taken", Mathf.Clamp(Arg(act.Args, "", 0.5f), 0.01f, 1f));
						Net.Want(fight.BossId, "raidboss_label", RaidBossPlugin.WardLabel.Value);
						break;
					case "boss":     // boss Frostbound [20] | boss cycle Frostbound Emberborn [30] | boss none. A number = seconds, then it lapses.
						var names = new List<string>(act.Args);
						float seconds = 0f;
						if (names.Count > 0 && float.TryParse(names[names.Count - 1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float s)) { seconds = s; names.RemoveAt(names.Count - 1); }
						string pick = names.Count > 0 ? names[0] : "none";
						if (pick.Equals("cycle", StringComparison.OrdinalIgnoreCase) && names.Count > 1)
						{
							fight.Cycle.TryGetValue(act, out int turn);
							pick = names[1 + turn % (names.Count - 1)];
							fight.Cycle[act] = turn + 1;
						}
						string mode = pick.Equals("none", StringComparison.OrdinalIgnoreCase) ? "" : RaidBossPlugin.TraitMode(pick);
						if (mode.Length == 0 && !pick.Equals("none", StringComparison.OrdinalIgnoreCase)) { RaidBossPlugin.Log.LogWarning($"{fight.Prefab}: trait '{pick}' is not in the Traits setting"); break; }
						Net.SendOps(boss.GetOwner(), fight.BossId, new Net.Op("set_s", Modes.ModeName, mode), new Net.Op("set_until", Modes.UntilName, "", seconds));
						break;
					case "strike":   // strike frost r4 d2 dmg90 x2: telegraphed ground strikes under random engaged players
						var targets = new List<Vector3>();
						foreach (PlayerPos p in Players) if (Flat(p.Pos, bossPos) <= RaidBossPlugin.Range.Value) targets.Add(p.Pos);
						int count = Mathf.Clamp(Mathf.RoundToInt(Arg(act.Args, "x", 1f)), 1, 12);
						RaidBossPlugin.StrikeEffects(first, out string tell, out string hit);
						for (int i = 0; i < count && targets.Count > 0; i++)
						{
							int at0 = UnityEngine.Random.Range(0, targets.Count);
							Vector3 at = targets[at0];
							if (count <= targets.Count) targets.RemoveAt(at0);
							else at += new Vector3(UnityEngine.Random.Range(-3f, 3f), 0f, UnityEngine.Random.Range(-3f, 3f));
							float delay = Arg(act.Args, "d", 2f);
							Net.SendStrike(at, Arg(act.Args, "r", 4f), delay, first, Arg(act.Args, "dmg", 60f), fight.BossId, "", "");
							ShowEffect(tell, at);
							ShowEffect(hit, at, delay);
						}
						break;
					case "break":
						Net.SendOps(boss.GetOwner(), fight.BossId, new Net.Op("break", "", "", Arg(act.Args, "", 0f)));
						break;
					case "heal":     // heal 5 = 5% of max health
						Net.SendOps(boss.GetOwner(), fight.BossId, new Net.Op("heal", "", "", Arg(act.Args, "", 5f) / 100f));
						break;
					case "weather":  // weather EnvName [seconds]
						Net.SendEnv(first, bossPos, RaidBossPlugin.Range.Value, act.Args.Length > 1 ? Arg(new[] { act.Args[1] }, "", 120f) : 120f);
						break;
					case "effect":
						ShowEffect(first, bossPos);
						break;
					case "status":   // status Wet [remove]
						Net.SendStatus(first, bossPos, RaidBossPlugin.Range.Value, act.Args.Length > 1 && act.Args[1].Equals("remove", StringComparison.OrdinalIgnoreCase));
						break;
				}
				RaidBossPlugin.Log.LogInfo($"{fight.Prefab}: {act}");
			}
			catch (Exception e) { RaidBossPlugin.Log.LogWarning($"{fight.Prefab}: action '{act}' failed: {e.Message}"); }
		}
	}

	// Once a second per fight: the break meter's settings reach the boss once, and finished waves are settled.
	static void TickMechanics(Fight fight, ZDO boss)
	{
		float size = RaidBossPlugin.BreakSize.Value;
		bool healthSettled = !fight.Heroic || Mathf.Approximately(RaidBossPlugin.HeroicBossHealth.Value, 1f) || boss.GetFloat(Net.HealthKey, 0f) != 0f;
		if (size > 0f && fight.Script != null && fight.Script.Rules.Count > 0 && healthSettled && fight.MeterAsks < 30 && boss.GetOwner() != 0L && boss.GetFloat("raidboss_brk_max".GetStableHashCode(), 0f) == 0f)
		{
			fight.MeterAsks++;
			string text = FormattableString.Invariant($"size={size};drain={RaidBossPlugin.BreakDrain.Value};parry={RaidBossPlugin.BreakParry.Value};dur={RaidBossPlugin.BreakSeconds.Value};grow={RaidBossPlugin.BreakGrowth.Value};x={RaidBossPlugin.BreakTaken.Value}");
			Net.SendOps(boss.GetOwner(), fight.BossId, new Net.Op("meter", "", text));
		}
		for (int i = fight.Waves.Count - 1; i >= 0; i--)
		{
			Wave wave = fight.Waves[i];
			bool alive = false;
			foreach (ZDOID id in wave.Ids)
			{
				ZDO zdo = ZDOMan.instance.GetZDO(id);
				if (zdo != null && zdo.IsValid() && zdo.GetBool(AddTag)) { alive = true; break; }
			}
			if (alive) continue;
			fight.Waves.RemoveAt(i);
			var ops = new List<Net.Op>();
			if (wave.Ward)
			{
				bool otherWard = fight.Waves.Exists(w => w.Ward);
				if (!otherWard) { Net.Want(fight.BossId, "raidboss_taken", 1f); Net.Want(fight.BossId, "raidboss_label", ""); }
				if (wave.BreakAfter) ops.Add(new Net.Op("break"));
			}
			if (RaidBossPlugin.BreakWaveChunk.Value > 0f && !wave.BreakAfter) ops.Add(new Net.Op("brk_add", "", "", RaidBossPlugin.BreakWaveChunk.Value));
			if (ops.Count > 0) Net.SendOps(boss.GetOwner(), fight.BossId, ops.ToArray());
			RaidBossPlugin.Log.LogInfo($"{fight.Prefab}: a wave of {wave.Ids.Count} is cleared{(wave.Ward ? ", the ward falls" : "")}{(wave.BreakAfter ? ", the boss breaks" : "")}");
		}
	}

	static void SpawnRule(Fight fight, ZDO boss, Encounter.Rule rule, int players, int room)
	{
		// Heroic threshold waves always carry a star. Where the script already stars something in this wave (for this
		// many players), ONE of those adds gains a star - a one-star becomes a two-star - and that is the wave's star;
		// the rest arrive as written, so a wave of six one-stars does not become six two-stars.
		// Where it stars nothing, the first add of the wave arrives one-star; for a wave that is a single heavy creature,
		// that is the heavy creature. Trickles are left alone: a starred add every half minute would bury the fight.
		// A rule written for heroic fights ("heroic 40%: ...") arrives exactly as written.
		bool heroicWave = fight.Heroic && !rule.Repeating && !rule.HeroicOnly && RaidBossPlugin.HeroicGuaranteedStar.Value;
		bool scriptedStar = false;
		if (heroicWave)
			foreach (Encounter.Spawn s in rule.Spawns)
				if (s.Level > 1 && s.Count(players) > 0) { scriptedStar = true; break; }
		bool promoted = false;

		int made = 0;
		foreach (Encounter.Spawn spawn in rule.Spawns)
		{
			int count = Mathf.Min(spawn.Count(players), room - made);
			for (int i = 0; i < count; i++)
			{
				int level = spawn.Level;
				if (heroicWave)
				{
					if (level > 1) { if (!promoted) { level = Mathf.Min(3, level + 1); promoted = true; } }
					else if (!scriptedStar && !promoted) { level = 2; promoted = true; }
				}
				if (fight.Heroic && level == 1 && UnityEngine.Random.value < RaidBossPlugin.HeroicStarChance.Value) level = 2;
				if (SpawnAdd(fight, boss, spawn, level)) made++;
			}
		}
		if (made > 0 && rule.Message.Length > 0) Message(boss.GetPosition(), rule.Message);
	}

	static bool SpawnAdd(Fight fight, ZDO boss, Encounter.Spawn spawn, int level)
	{
		GameObject prefab = ZNetScene.instance.GetPrefab(spawn.Prefab);
		ZNetView view = prefab != null ? prefab.GetComponent<ZNetView>() : null;
		if (view == null || prefab.GetComponent<Character>() == null)
		{
			RaidBossPlugin.Log.LogWarning($"{fight.Prefab}: '{spawn.Prefab}' is not a creature prefab, skipped");
			return false;
		}
		Vector3 bossPos = boss.GetPosition();
		long owner = boss.GetOwner();
		Vector3 pos = bossPos;
		bool placed = false;
		for (int attempt = 0; attempt < 10 && !placed; attempt++)
		{
			float angle = UnityEngine.Random.Range(0f, Mathf.PI * 2f);
			float dist = UnityEngine.Random.Range(RaidBossPlugin.RingMin.Value, Mathf.Max(RaidBossPlugin.RingMin.Value, RaidBossPlugin.RingMax.Value));
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
		if (level > 1) zdo.Set(ZDOVars.s_level, level);
		if (RaidBossPlugin.AddsHunt.Value) zdo.Set(ZDOVars.s_huntPlayer, true);
		zdo.Set(AddTag, true);
		// The add carries its own damage multiplier; every player's game applies it to this creature's hits on players
		// (ClientSide.cs). It is the only reduction: nothing else in this mod touches what an add deals.
		float dmg = RaidBossPlugin.AddDamage.Value;
		if (dmg > 0f && !Mathf.Approximately(dmg, 1f)) zdo.Set(AddDamageKey, dmg);
		float hp = RaidBossPlugin.AddHealth.Value;
		if (hp > 0f && hp < 0.999f)
		{
			// Character.Awake only recomputes max health when health == max, so both are written: the add arrives
			// with the right maximum for its level and already hurt. Its health bar starts part empty.
			float max = prefab.GetComponent<Character>().m_health * Mathf.Max(1, spawn.Level);
			zdo.Set(ZDOVars.s_maxHealth, max);
			zdo.Set(ZDOVars.s_health, max * hp);
		}
		if (spawn.Trait.Length > 0)
		{
			string mode = RaidBossPlugin.TraitMode(spawn.Trait);
			if (mode.Length > 0) zdo.Set(Modes.ModeName.GetStableHashCode(), mode);
			else RaidBossPlugin.Log.LogWarning($"{fight.Prefab}: trait '{spawn.Trait}' is not in the Traits setting");
		}
		if (owner != 0L) zdo.SetOwner(owner);

		fight.Adds.Add(new Add { Id = zdo.m_uid, Prefab = prefab.name });
		RaidBossPlugin.Log.LogInfo($"{fight.Prefab}: spawned {prefab.name}{new string('*', level - 1)} {zdo.m_uid} at {pos:0.0} ({Flat(pos, bossPos):0} m from the boss) owner {owner}");
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
			if (zdo == null || !zdo.IsValid()) { RaidBossPlugin.Log.LogInfo($"{fight.Prefab}: {add.Prefab} {add.Id} is already gone 5 s after spawning (killed, or rejected)"); continue; }
			// s_spawnTime is written by BaseAI.Awake on the owner, so its presence proves a client instantiated the creature.
			bool awake = zdo.GetLong(ZDOVars.s_spawnTime, 0L) != 0L;
			RaidBossPlugin.Log.LogInfo($"{fight.Prefab}: {add.Prefab} {add.Id} after 5 s: owner {zdo.GetOwner()}, instantiated by a client: {(awake ? "YES" : "NO")}, at {zdo.GetPosition():0.0}");
		}
	}

	static void Message(Vector3 center, string text)
	{
		if (DebugPlayers != null) return;
		float range = RaidBossPlugin.Range.Value;
		foreach (PlayerPos p in Players)
			if (Flat(p.Pos, center) <= range)
				ZRoutedRpc.instance.InvokeRoutedRPC(p.Uid, "ShowMessage", (int)MessageHud.MessageType.Center, text);
	}

	internal static void Reset()
	{
		Fights.Clear();
	}

	// ---- shared with WorldEncounters ----

	// Players within range of a point (flat distance), and the uid of the nearest one.
	internal static int CountPlayers(Vector3 pos, float range, out long nearest)
	{
		GatherPlayers();
		int count = 0;
		foreach (PlayerPos p in Players) if (Flat(p.Pos, pos) <= range) count++;
		nearest = NearestPlayer(pos);
		return count;
	}

	// A creature made the same way as a boss add, but belonging to no fight: it is never removed by BossDirector and
	// never carries the per-add damage number.
	internal static ZDOID SpawnCreature(string prefabName, int level, Vector3 center, float ringMin, float ringMax, long owner, int tag, string why)
	{
		GameObject prefab = ZNetScene.instance.GetPrefab(prefabName);
		ZNetView view = prefab != null ? prefab.GetComponent<ZNetView>() : null;
		if (view == null || prefab.GetComponent<Character>() == null)
		{
			RaidBossPlugin.Log.LogWarning($"{why}: '{prefabName}' is not a creature prefab, skipped");
			return ZDOID.None;
		}
		Vector3 pos = center;
		bool placed = false;
		for (int attempt = 0; attempt < 10 && !placed; attempt++)
		{
			float angle = UnityEngine.Random.Range(0f, Mathf.PI * 2f);
			float dist = UnityEngine.Random.Range(ringMin, Mathf.Max(ringMin, ringMax));
			pos = new Vector3(center.x + Mathf.Cos(angle) * dist, 0f, center.z + Mathf.Sin(angle) * dist);
			pos.y = GroundHeight(pos);
			placed = pos.y > ZoneSystem.instance.m_waterLevel + 0.3f;
		}
		if (!placed) pos.y = Mathf.Max(pos.y, ZoneSystem.instance.m_waterLevel + 0.5f);
		pos.y += 0.5f;

		int hash = prefab.name.GetStableHashCode();
		ZDO zdo = ZDOMan.instance.CreateNewZDO(pos, hash);
		zdo.Persistent = view.m_persistent;
		zdo.Type = view.m_type;
		zdo.Distant = view.m_distant;
		zdo.SetPrefab(hash);
		Vector3 facing = center - pos; facing.y = 0f;
		zdo.SetRotation(facing.sqrMagnitude > 0.01f ? Quaternion.LookRotation(facing) : Quaternion.identity);
		if (level > 1) zdo.Set(ZDOVars.s_level, level);
		if (RaidBossPlugin.AddsHunt.Value) zdo.Set(ZDOVars.s_huntPlayer, true);
		zdo.Set(tag, true);
		if (owner != 0L) zdo.SetOwner(owner);
		RaidBossPlugin.Log.LogInfo($"{why}: spawned {prefab.name}{new string('*', level - 1)} {zdo.m_uid} at {pos:0.0} ({Flat(pos, center):0} m away) owner {owner}");
		return zdo.m_uid;
	}

	internal static bool IsDirectorSpawn(ZDO zdo) => zdo.GetBool(AddTag);
}
