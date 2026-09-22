using System;
using System.Collections.Generic;
using System.Globalization;
using HarmonyLib;
using UnityEngine;

namespace RaidBoss;

// Warbands: a pack of a biome's creatures around a starred miniboss, camped at a spot in the open world that is marked
// on everyone's map. Travel there, break the pack, kill the miniboss, and it pays an idol (a heroic idol - certain up
// to a level - if the server says so). A reason to cross the world, and a second source of idols next to heroic fights.
//
// One warband per biome at a time. The server picks a site (that biome, on dry level ground, hundreds of metres from
// every player and away from anything a player built), announces it and pins it. Nothing stands there until a player
// comes within the trigger range: then the miniboss is spawned and a fight is started for it with the warband's own
// script, so everything a boss script can do (escort waves at health thresholds, guard, traits, strikes) works for it,
// and it wears the boss bar. When the miniboss dies the band is cleared; if nobody comes for long enough it moves on.
//
// A warband is written like a boss script with a head:   Boss[:Trait][*..*] [tierN] | <boss script>
//   Plains = GoblinBrute*** tier4 | 100%: guard melee0.5 | 100%: Goblin 3+1 | 50% "The shamans chant": GoblinShaman 1+0
// The script's 100% rules fire the moment the pack is triggered, so they are the escort standing with the miniboss.
internal static class Warbands
{
	internal static readonly int BossKey = "raidboss_warboss".GetStableHashCode();   // on the miniboss: boss bar, parry taunt
	internal const string SureKey = "raidboss_sure";                                  // on an idol: the upgrade cannot fail
	internal const string EventName = "raidboss_warband";
	static readonly int ModeKey = Modes.ModeName.GetStableHashCode();

	sealed class Def
	{
		public Heightmap.Biome Biome;
		public string BiomeName;
		public string Prefab;
		public string Trait = "";
		public int Level = 1;
		public int Tier = -1;
		public string Script = "";
		public string UnlockBoss = "";    // prefab of the boss whose death opens this biome's warbands ("" = open)
		public string UnlockKeyCache;
	}

	sealed class Band
	{
		public Def Def;
		public int Id;
		public Vector3 Site;
		public float Age;
		public bool Spawned;
		public ZDOID BossId;
		public float LastTry = -999f;
		public bool EventOn;
	}

	static readonly List<Def> defs = new List<Def>();
	static readonly Dictionary<Heightmap.Biome, Band> bands = new Dictionary<Heightmap.Biome, Band>();
	static readonly Dictionary<Heightmap.Biome, float> readyAt = new Dictionary<Heightmap.Biome, float>();
	static readonly Dictionary<Heightmap.Biome, float> lastEnd = new Dictionary<Heightmap.Biome, float>();
	static float globalReadyAt;
	static float LastEnd(Def d) => lastEnd.TryGetValue(d.Biome, out float t) ? t : -1f;
	static readonly Dictionary<Heightmap.Biome, float> nextSearch = new Dictionary<Heightmap.Biome, float>();
	static readonly List<ZDO> scan = new List<ZDO>();
	static string parsedFrom;
	static float clock;
	static float pinTimer = 999f;
	static bool pinsDirty;
	static int nextId = 1;
	static bool hooked;

	// Test hook: the site search starts from here instead of the players.
	internal static Vector3? DebugAnchor;
	internal static IEnumerable<(string biome, Vector3 site, bool spawned, ZDOID boss)> Snapshot()
	{
		foreach (Band b in bands.Values) yield return (b.Def.BiomeName, b.Site, b.Spawned, b.BossId);
	}

	static readonly (Heightmap.Biome biome, string name)[] BiomeNames =
	{
		(Heightmap.Biome.Meadows, "Meadows"), (Heightmap.Biome.BlackForest, "Black Forest"), (Heightmap.Biome.Swamp, "Swamp"),
		(Heightmap.Biome.Mountain, "Mountain"), (Heightmap.Biome.Plains, "Plains"), (Heightmap.Biome.Mistlands, "Mistlands"),
		(Heightmap.Biome.AshLands, "Ashlands"), (Heightmap.Biome.DeepNorth, "Deep North"),
	};

	static string NameOf(Heightmap.Biome b)
	{
		foreach (var n in BiomeNames) if (n.biome == b) return n.name;
		return b.ToString();
	}

	static bool ParseBiome(string text, out Heightmap.Biome biome)
	{
		string key = text.Replace(" ", "").Replace("_", "").ToLowerInvariant();
		foreach (var n in BiomeNames)
			if (n.name.Replace(" ", "").ToLowerInvariant() == key || n.biome.ToString().ToLowerInvariant() == key) { biome = n.biome; return true; }
		biome = Heightmap.Biome.None;
		return false;
	}

	static void Log(string s) => RaidBossPlugin.Log.LogInfo("warband: " + s);

	// ---- the config: one entry per biome

	static void Parse()
	{
		string all = string.Join("\n", RaidBossPlugin.WarbandLines());
		if (all == parsedFrom) return;
		parsedFrom = all;
		defs.Clear();
		foreach (KeyValuePair<string, string> kv in RaidBossPlugin.WarbandEntries())
		{
			string raw = (kv.Value ?? "").Trim();
			if (raw.Length == 0) continue;
			if (!ParseBiome(kv.Key, out Heightmap.Biome biome)) { RaidBossPlugin.Log.LogWarning($"warband '{kv.Key}': not a biome"); continue; }
			int bar = raw.IndexOf('|');
			string head = (bar < 0 ? raw : raw.Substring(0, bar)).Trim();
			string script = bar < 0 ? "" : raw.Substring(bar + 1).Trim();
			var def = new Def { Biome = biome, BiomeName = NameOf(biome), Script = script };
			foreach (string word in head.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
			{
				if (word.StartsWith("tier", StringComparison.OrdinalIgnoreCase) && int.TryParse(word.Substring(4), out int t)) { def.Tier = t; continue; }
				string spec = word;
				while (spec.EndsWith("*")) { def.Level++; spec = spec.Substring(0, spec.Length - 1); }
				int colon = spec.IndexOf(':');
				if (colon > 0) { def.Trait = spec.Substring(colon + 1); spec = spec.Substring(0, colon); }
				def.Prefab = spec;
			}
			def.Level = Mathf.Clamp(def.Level, 1, Stars.Max);
			def.UnlockBoss = UnlockBossFor(def.BiomeName);
			if (string.IsNullOrEmpty(def.Prefab)) { RaidBossPlugin.Log.LogWarning($"warband {def.BiomeName}: no miniboss named"); continue; }
			if (def.Tier < 0) def.Tier = DefaultTier(biome);
			Encounter parsed = Encounter.Parse(def.Script);
			foreach (string err in parsed.Errors) RaidBossPlugin.Log.LogWarning($"warband {def.BiomeName} script: {err}");
			defs.Add(def);
			Log($"{def.BiomeName}: {def.Prefab}{(def.Trait.Length > 0 ? ":" + def.Trait : "")}{new string('*', def.Level - 1)}, idol tier {def.Tier}, " +
				$"{(def.UnlockBoss.Length == 0 ? "open from the start" : $"opens when {def.UnlockBoss} is defeated ({UnlockKey(def)}: {(Unlocked(def) ? "set" : "not yet")})")}" +
				$"{(PhasedOut(def) ? ", PHASED OUT (the world's kill level is " + KillLevel() + ")" : "")}, script for 1 player: {parsed.Describe(1)}");
		}
		// a biome that lost its entry loses its band
		var gone = new List<Heightmap.Biome>();
		foreach (Band b in bands.Values) if (!defs.Exists(d => d.Biome == b.Def.Biome)) gone.Add(b.Def.Biome);
		foreach (Heightmap.Biome b in gone) End(bands[b], "its entry was removed", false);
	}

	static int DefaultTier(Heightmap.Biome b)
	{
		switch (b)
		{
			case Heightmap.Biome.Meadows: return 0;
			case Heightmap.Biome.BlackForest: return 1;
			case Heightmap.Biome.Swamp: return 2;
			case Heightmap.Biome.Mountain: return 3;
			case Heightmap.Biome.Plains: return 4;
			case Heightmap.Biome.Mistlands: return 5;
			case Heightmap.Biome.AshLands: return 6;
			case Heightmap.Biome.DeepNorth: return 7;
			default: return -1;
		}
	}

	// ---- the server's tick

	internal static void Tick(float dt)
	{
		if (!hooked) { Director.FightEnded += OnFightEnded; hooked = true; }
		clock += dt;
		if (!RaidBossPlugin.WarbandsEnabled.Value)
		{
			if (bands.Count > 0) { foreach (Band b in new List<Band>(bands.Values)) End(b, "warbands were turned off", false); }
			return;
		}
		Parse();
		string order = (RaidBossPlugin.WarbandOrder.Value ?? "").Trim();
		if (order.Length > 0)
		{
			RaidBossPlugin.WarbandOrder.Value = "";
			try { Order(order); } catch (Exception e) { RaidBossPlugin.Log.LogWarning("warband order: " + e.Message); }
		}

		// Spacing: at most "At most, at once" warbands standing, and a gap after any of them ends before the next one
		// anywhere. Biomes take turns: the one whose last warband is longest ago goes first.
		if (bands.Count < Mathf.Max(1, RaidBossPlugin.WarbandMaxActive.Value) && clock >= globalReadyAt && (Director.PlayerCount > 0 || DebugAnchor != null))
		{
			var eligible = new List<Def>();
			foreach (Def def in defs)
			{
				if (bands.ContainsKey(def.Biome)) continue;
				if (readyAt.TryGetValue(def.Biome, out float ready) && clock < ready) continue;
				if (nextSearch.TryGetValue(def.Biome, out float next) && clock < next) continue;
				if (!Unlocked(def) || PhasedOut(def)) continue;   // its boss is not dead yet, or the world is past it
				eligible.Add(def);
			}
			eligible.Sort((a, b) => LastEnd(a).CompareTo(LastEnd(b)));
			foreach (Def def in eligible)
			{
				nextSearch[def.Biome] = clock + 60f;
				if (FindSite(def, null, RaidBossPlugin.WarbandMinDistance.Value, RaidBossPlugin.WarbandMaxDistance.Value, out Vector3 site)) { Begin(def, site); break; }
			}
		}

		foreach (Band b in new List<Band>(bands.Values))
		{
			try { Step(b, dt); }
			catch (Exception e) { RaidBossPlugin.Log.LogError($"warband {b.Def.BiomeName}: {e}"); End(b, "an error", false); }
		}

		pinTimer += dt;
		if (pinsDirty || pinTimer >= 20f)
		{
			pinTimer = 0f;
			pinsDirty = false;
			Net.SendPins(PinList());
		}
	}

	static void Step(Band b, float dt)
	{
		b.Age += dt;
		float lifetime = RaidBossPlugin.WarbandLifetime.Value * 60f;
		if (!b.Spawned)
		{
			if (lifetime > 0f && b.Age > lifetime) { End(b, "nobody came", true); return; }
			if (PhasedOut(b.Def)) { End(b, "the world has moved past this biome", false); return; }   // quietly; a pack already up is left to be fought
			if (Director.CountPlayers(b.Site, RaidBossPlugin.WarbandTrigger.Value, out long nearest) > 0) Spawn(b, nearest);
			return;
		}
		// the fight itself is the director's; here only the mood at the site and the end
		ZDO boss = ZDOMan.instance.GetZDO(b.BossId);
		if (boss == null || !boss.IsValid()) return;   // EndFight fires OnFightEnded, which ends the band
		if (lifetime > 0f && b.Age > lifetime * 2f) { End(b, "it was left standing too long", true); return; }
		int near = Director.CountPlayers(b.Site, RaidBossPlugin.Range.Value, out _);
		RandomEvent current = RandEventSystem.instance != null ? RandEventSystem.instance.GetCurrentRandomEvent() : null;
		if (near > 0 && !b.EventOn && current == null && Director.DebugPlayers == null)
		{
			RandEventSystem.instance.SetRandomEventByName(EventName, b.Site);
			b.EventOn = true;
		}
		else if (b.EventOn && near == 0)
		{
			if (current != null && current.m_name == EventName) RandEventSystem.instance.ResetRandomEvent();
			b.EventOn = false;
		}
		else if (b.EventOn && current != null && current.m_name == EventName) current.m_pos = b.Site;
	}

	static void Order(string order)
	{
		string[] words = order.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
		if (words[0].Equals("stop", StringComparison.OrdinalIgnoreCase))
		{
			foreach (Band b in new List<Band>(bands.Values)) End(b, "stopped by order", false);
			Log("all warbands stopped");
			return;
		}
		if (!ParseBiome(words[0], out Heightmap.Biome biome)) { Log($"'{words[0]}' is not a biome"); return; }
		Def def = defs.Find(d => d.Biome == biome);
		if (def == null) { Log($"no warband entry for {NameOf(biome)}"); return; }
		if (bands.TryGetValue(biome, out Band old)) End(old, "replaced by order", false);
		string who = words.Length > 1 ? string.Join(" ", words, 1, words.Length - 1) : "";
		Vector3? anchor = Director.PlayerPosition(who);
		if (anchor == null) { Log(who.Length > 0 ? $"no player called '{who}'" : "nobody is connected"); return; }
		if (!FindSite(def, anchor, 150f, 300f, out Vector3 site) && !FindSite(def, anchor, 300f, 600f, out site)) { Log($"no {def.BiomeName} ground within 600 m of {(who.Length > 0 ? who : "the first player")}"); return; }
		readyAt.Remove(biome);
		if (!Unlocked(def)) Log($"{def.BiomeName} is not unlocked yet ({UnlockKey(def)} is not set) - placed by order anyway");
		else if (PhasedOut(def)) Log($"{def.BiomeName} is phased out (the world's kill level is {KillLevel()}) - placed by order anyway");
		Begin(def, site);
	}

	// ---- unlocks: a biome's warbands start once the boss before it is dead (the game's own "defeated_..." key)

	static string UnlockBossFor(string biomeName)
	{
		foreach (string part in (RaidBossPlugin.WarbandUnlocks.Value ?? "").Split(','))
		{
			int eq = part.IndexOf('=');
			if (eq <= 0) continue;
			if (part.Substring(0, eq).Trim().Equals(biomeName, StringComparison.OrdinalIgnoreCase)) return part.Substring(eq + 1).Trim();
		}
		return "";
	}

	// The key a boss sets when it dies: read off its prefab, so it is right for any boss the game has.
	static readonly Dictionary<string, string> bossKeys = new Dictionary<string, string>();

	static string KeyForBoss(string boss)
	{
		if (string.IsNullOrEmpty(boss)) return "";
		if (bossKeys.TryGetValue(boss, out string cached)) return cached;
		GameObject prefab = ZNetScene.instance != null ? ZNetScene.instance.GetPrefab(boss) : null;
		Character c = prefab != null ? prefab.GetComponent<Character>() : null;
		string key = c != null && !string.IsNullOrEmpty(c.m_defeatSetGlobalKey) ? c.m_defeatSetGlobalKey : "defeated_" + boss.ToLowerInvariant();
		bossKeys[boss] = key;
		return key;
	}

	static bool BossDead(string boss) => boss.Length > 0 && ZoneSystem.instance != null && ZoneSystem.instance.GetGlobalKey(KeyForBoss(boss));

	static string UnlockKey(Def def) => KeyForBoss(def.UnlockBoss);

	static bool Unlocked(Def def) => def.UnlockBoss.Length == 0 || BossDead(def.UnlockBoss);

	// ---- phasing out: a biome's warbands stop once the world is a couple of bosses past it

	// The boss of biome tier t is what unlocks tier t + 1, so it is read off the same "Unlocked by" list.
	static string BossOfTier(int t)
	{
		foreach (var n in BiomeNames) if (DefaultTier(n.biome) == t + 1) return UnlockBossFor(n.name);
		return "";
	}

	// The highest biome tier whose own boss is dead in this world (-1 = none).
	static int KillLevel()
	{
		int level = -1;
		for (int t = 0; t < 8; t++) if (BossDead(BossOfTier(t))) level = t;
		return level;
	}

	static bool PhasedOut(Def def)
	{
		int ahead = RaidBossPlugin.WarbandPhaseOut.Value;
		int tier = DefaultTier(def.Biome);
		if (ahead <= 0 || tier < 0) return false;
		return KillLevel() >= tier + ahead;
	}

	// A spot of the right biome: dry, fairly level, far enough from every player, away from anything built, and not on
	// top of another band. Random points in a ring around the anchor (a random player, or the given one).
	static bool FindSite(Def def, Vector3? anchor, float minDist, float maxDist, out Vector3 site)
	{
		site = Vector3.zero;
		WorldGenerator wg = WorldGenerator.instance;
		if (wg == null || ZoneSystem.instance == null) return false;
		Vector3 from = anchor ?? DebugAnchor ?? Director.RandomPlayerPosition();
		float water = ZoneSystem.instance.m_waterLevel;
		for (int attempt = 0; attempt < 100; attempt++)
		{
			float angle = UnityEngine.Random.Range(0f, Mathf.PI * 2f);
			float dist = UnityEngine.Random.Range(minDist, Mathf.Max(minDist, maxDist));
			var p = new Vector3(from.x + Mathf.Cos(angle) * dist, 0f, from.z + Mathf.Sin(angle) * dist);
			if (p.magnitude > 9500f) continue;
			if (wg.GetBiome(p.x, p.z) != def.Biome) continue;
			p.y = wg.GetHeight(p.x, p.z);
			if (p.y < water + 0.5f) continue;   // swamps sit barely above the water; dry enough to stand on is enough
			float h1 = wg.GetHeight(p.x + 8f, p.z), h2 = wg.GetHeight(p.x - 8f, p.z), h3 = wg.GetHeight(p.x, p.z + 8f), h4 = wg.GetHeight(p.x, p.z - 8f);
			if (Mathf.Max(Mathf.Abs(h1 - h2), Mathf.Abs(h3 - h4)) > 6f) continue;
			if (anchor == null && Director.NearestPlayerDistance(p) < minDist) continue;
			bool crowded = false;
			foreach (Band other in bands.Values) if (Utils.DistanceXZ(other.Site, p) < 400f) { crowded = true; break; }
			if (crowded || Built(p)) continue;
			site = p;
			return true;
		}
		return false;
	}

	// Anything a player built within two zones of the point (a creator id on the object).
	static bool Built(Vector3 p)
	{
		scan.Clear();
		SimulationDistance synced = ZNet.instance.GetSyncedSimulationDistance();
		ZDOMan.instance.FindSectorObjects(ZoneSystem.GetZone(p), new SimulationDistance(2, 0, synced.IsClassic), scan);
		foreach (ZDO zdo in scan)
			if (zdo.GetLong(ZDOVars.s_creator, 0L) != 0L && Utils.DistanceXZ(zdo.GetPosition(), p) < 150f) { scan.Clear(); return true; }
		scan.Clear();
		return false;
	}

	static void Begin(Def def, Vector3 site)
	{
		var b = new Band { Def = def, Id = nextId++, Site = site };
		bands[def.Biome] = b;
		pinsDirty = true;
		Log($"{def.BiomeName} warband at {site:0} ({Director.NearestPlayerDistance(site):0} m from the nearest player)");
		string msg = Fill(RaidBossPlugin.WarbandMessage.Value, def);
		if (msg.Length > 0) Director.MessageAll(msg);
	}

	static string Fill(string text, Def def) => (text ?? "").Replace("{biome}", def.BiomeName).Trim();

	static void Spawn(Band b, long owner)
	{
		Def def = b.Def;
		ZDOID id = Director.SpawnCreature(def.Prefab, def.Level, b.Site, 0f, 1f, owner, BossKey, "warband " + def.BiomeName);
		if (id.IsNone()) { End(b, $"'{def.Prefab}' could not be spawned", false); return; }
		ZDO zdo = ZDOMan.instance.GetZDO(id);
		if (def.Trait.Length > 0)
		{
			string mode = RaidBossPlugin.TraitMode(def.Trait);
			if (mode.Length > 0) zdo.Set(ModeKey, mode);
		}
		b.BossId = id;
		b.Spawned = true;
		Director.StartCustomFight(id, def.Prefab, def.Script, RaidBossPlugin.WarbandBossDamage.Value, "warband " + def.BiomeName);
		Log($"{def.BiomeName}: the pack is up - {def.Prefab}{new string('*', def.Level - 1)} {id} at {b.Site:0}");
	}

	static void OnFightEnded(ZDOID id, bool killed, Vector3 at)
	{
		foreach (Band b in bands.Values)
		{
			if (b.BossId != id) continue;
			if (killed) Reward(b, at);
			End(b, killed ? "the miniboss was killed" : "the miniboss vanished", false);
			if (killed)
			{
				string msg = Fill(RaidBossPlugin.WarbandClearedMessage.Value, b.Def);
				if (msg.Length > 0) Director.MessageAll(msg);
			}
			return;
		}
	}

	static void Reward(Band b, Vector3 at)
	{
		int count = Mathf.Max(0, RaidBossPlugin.WarbandIdols.Value);
		if (count == 0 || b.Def.Tier < 0) { Log($"{b.Def.BiomeName}: cleared, no idols ({(count == 0 ? "idols set to 0" : "no tier")})"); return; }
		long owner = Director.NearestPlayerUid(at);
		var given = new List<string>();
		for (int i = 0; i < count; i++)
		{
			bool battle = UnityEngine.Random.value < RaidBossPlugin.IdolBattleShare.Value;
			string item = $"Upgrader{b.Def.Tier}{(battle ? "Weapon" : "Armor")}";
			if (Director.SpawnItem(item, at, owner, RaidBossPlugin.WarbandSureIdols.Value)) given.Add(item);
		}
		Log($"{b.Def.BiomeName}: cleared, dropped {given.Count} {(RaidBossPlugin.WarbandSureIdols.Value ? "warband" : "plain")} idol(s): {string.Join(", ", given)}");
	}

	// The band is over: its creatures go if it expired unfought, its pin goes, and the biome waits for the next one.
	static void End(Band b, string why, bool expired)
	{
		bands.Remove(b.Def.Biome);
		pinsDirty = true;
		if (b.EventOn && RandEventSystem.instance != null)
		{
			RandomEvent current = RandEventSystem.instance.GetCurrentRandomEvent();
			if (current != null && current.m_name == EventName) RandEventSystem.instance.ResetRandomEvent();
		}
		if (b.Spawned)
		{
			ZDO boss = ZDOMan.instance.GetZDO(b.BossId);
			if (boss != null && boss.IsValid())
			{
				boss.SetOwner(ZDOMan.GetSessionID());
				ZDOMan.instance.DestroyZDO(boss);   // EndFight follows on the next tick and removes the escort
			}
		}
		readyAt[b.Def.Biome] = clock + RaidBossPlugin.WarbandCooldown.Value * 60f;
		lastEnd[b.Def.Biome] = clock;
		globalReadyAt = Mathf.Max(globalReadyAt, clock + RaidBossPlugin.WarbandGap.Value * 60f);
		Log($"{b.Def.BiomeName} warband over: {why}. Next one in {RaidBossPlugin.WarbandCooldown.Value:0} min.");
		if (expired)
		{
			string msg = Fill(RaidBossPlugin.WarbandGoneMessage.Value, b.Def);
			if (msg.Length > 0) Director.MessageAll(msg);
		}
	}

	internal static void Reset()
	{
		bands.Clear();
		readyAt.Clear();
		nextSearch.Clear();
		parsedFrom = null;
	}

	// ---- map pins: the server's list, mirrored on every player's map

	internal struct Pin { public int Id; public Vector3 Pos; public string Text; public float EndsIn; }   // EndsIn: seconds until it moves on, -1 = never

	static float EndsIn(Band b)
	{
		float lifetime = RaidBossPlugin.WarbandLifetime.Value * 60f;
		if (lifetime <= 0f) return -1f;
		return Mathf.Max(0f, (b.Spawned ? lifetime * 2f : lifetime) - b.Age);
	}

	static List<Pin> PinList()
	{
		var list = new List<Pin>();
		foreach (Band b in bands.Values)
			list.Add(new Pin { Id = b.Id, Pos = b.Site, Text = Fill(RaidBossPlugin.WarbandPinText.Value, b.Def), EndsIn = EndsIn(b) });
		return list;
	}

	// Each player's game: the pins, and the countdown under each one on the big map. The pin's name is its label plus
	// the time left, ticked down locally between the server's updates.
	sealed class Shown { public Minimap.PinData Pin; public string Text; public float EndsAt; }
	static readonly Dictionary<int, Shown> shown = new Dictionary<int, Shown>();
	static float nameTimer;

	internal static void ApplyPins(List<Pin> list)
	{
		if (Minimap.instance == null) return;
		var keep = new HashSet<int>();
		foreach (Pin p in list)
		{
			keep.Add(p.Id);
			float endsAt = p.EndsIn >= 0f ? Time.time + p.EndsIn : -1f;
			if (shown.TryGetValue(p.Id, out Shown s))
			{
				bool alive = Minimap.instance.m_pins.Contains(s.Pin);
				if (alive && s.Text == p.Text && Utils.DistanceXZ(s.Pin.m_pos, p.Pos) < 1f) { s.EndsAt = endsAt; continue; }
				if (alive) Minimap.instance.RemovePin(s.Pin);
				shown.Remove(p.Id);
			}
			var made = new Shown { Text = p.Text, EndsAt = endsAt };
			made.Pin = Minimap.instance.AddPin(p.Pos, Minimap.PinType.Boss, Label(made), false, false);
			shown[p.Id] = made;
		}
		var gone = new List<int>();
		foreach (KeyValuePair<int, Shown> kv in shown) if (!keep.Contains(kv.Key)) gone.Add(kv.Key);
		foreach (int id in gone)
		{
			if (Minimap.instance.m_pins.Contains(shown[id].Pin)) Minimap.instance.RemovePin(shown[id].Pin);
			shown.Remove(id);
		}
	}

	static string Label(Shown s)
	{
		if (s.EndsAt < 0f) return s.Text;
		float left = Mathf.Max(0f, s.EndsAt - Time.time);
		int h = (int)(left / 3600f), m = (int)(left / 60f) % 60, sec = (int)left % 60;
		return s.Text + (h > 0 ? $" ({h}h {m:00}m)" : $" ({m}:{sec:00})");
	}

	internal static void ClientTick(float dt)
	{
		if (shown.Count == 0 || Minimap.instance == null) return;
		nameTimer += dt;
		if (nameTimer < 1f) return;
		nameTimer = 0f;
		foreach (Shown s in shown.Values)
		{
			string label = Label(s);
			if (s.Pin.m_name == label) continue;
			s.Pin.m_name = label;
			// the name object is made when the map first draws the pin, and set once; refreshed here
			if (s.Pin.m_NamePinData != null && s.Pin.m_NamePinData.PinNameText != null) s.Pin.m_NamePinData.PinNameText.text = label;
		}
	}

	// ---- client side

	// The miniboss counts as a boss on every player's game: the boss bar with the break meter and the mode under its
	// name, and a parry taunts it like a boss.
	[HarmonyPatch(typeof(Character), nameof(Character.IsBoss))]
	static class BossPatch
	{
		static void Postfix(Character __instance, ref bool __result)
		{
			if (__result || __instance.m_nview == null || !__instance.m_nview.IsValid()) return;
			if (__instance.m_nview.GetZDO().GetBool(BossKey)) __result = true;
		}
	}

	internal static bool IsSure(ItemDrop.ItemData item) => item != null && item.m_customData != null && item.m_customData.ContainsKey(SureKey);

	// A heroic idol says what it is.
	[HarmonyPatch(typeof(ItemDrop.ItemData), nameof(ItemDrop.ItemData.GetTooltip), typeof(ItemDrop.ItemData), typeof(int), typeof(bool), typeof(float), typeof(int), typeof(bool))]
	static class TooltipPatch
	{
		static void Postfix(ItemDrop.ItemData item, ref string __result)
		{
			if (!IsSure(item)) return;
			int upTo = RaidBossPlugin.SureIdolUpTo.Value;
			__result += $"\n\n<color=#ffd24a>Heroic idol: an upgrade to level {upTo} or below cannot fail. Above that, {BreakChance(upTo + 1) * 100f:0}% may break the item; the rest succeeds.</color>";
		}
	}

	// The heroic idol's odds above the sure level: "7=0.2, 8=0.35" - a level not listed takes the nearest listed
	// one below it (or the lowest listed).
	internal static float BreakChance(int level)
	{
		float best = -1f; int bestLevel = int.MinValue; float lowest = -1f; int lowestLevel = int.MaxValue;
		foreach (string part in (RaidBossPlugin.SureIdolBreak.Value ?? "").Split(','))
		{
			int eq = part.IndexOf('=');
			if (eq <= 0) continue;
			if (!int.TryParse(part.Substring(0, eq).Trim(), out int l) || !float.TryParse(part.Substring(eq + 1).Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float chance)) continue;
			chance = Mathf.Clamp01(chance);
			if (l <= level && l > bestLevel) { bestLevel = l; best = chance; }
			if (l < lowestLevel) { lowestLevel = l; lowest = chance; }
		}
		if (best >= 0f) return best;
		return lowest >= 0f ? lowest : 0.35f;
	}

	// Refining with a heroic idol: the game rolls against the idol's own numbers, so for this one attempt they are the
	// heroic idol's - certain up to the sure level, above it a break chance by level and no drop to a lower level. The
	// heroic idol is taken here, and the recipe's own idol cost is waived for the attempt, so exactly one idol goes:
	// the warband one.
	[HarmonyPatch(typeof(InventoryGui), "DoCrafting")]
	static class SureCraftPatch
	{
		static ItemDrop.ItemData.SharedData shared;
		static float savedUp, savedBreak;
		static Piece.Requirement req;
		static int savedAmount, savedPerLevel;

		static void Prefix(InventoryGui __instance, Player player)
		{
			shared = null; req = null;
			if (player == null || __instance.m_craftUpgradeItem == null || __instance.m_craftRecipe == null || __instance.m_craftRecipe.m_resources == null) return;
			Piece.Requirement upgrader = null;
			foreach (Piece.Requirement r in __instance.m_craftRecipe.m_resources)
				if (r.m_upgraderResource && r.m_resItem != null) { upgrader = r; break; }
			if (upgrader == null) return;
			string name = upgrader.m_resItem.m_itemData.m_shared.m_name;
			ItemDrop.ItemData sure = null;
			foreach (ItemDrop.ItemData it in player.GetInventory().GetAllItems())
				if (it.m_shared.m_name == name && IsSure(it)) { sure = it; break; }
			if (sure == null) return;
			player.GetInventory().RemoveItem(sure, 1);
			req = upgrader; savedAmount = req.m_amount; savedPerLevel = req.m_amountPerLevel;
			req.m_amount = 0; req.m_amountPerLevel = 0;
			shared = upgrader.m_resItem.m_itemData.m_shared;
			savedUp = shared.m_upgradeChance; savedBreak = shared.m_breakChance;
			int target = __instance.m_craftUpgradeItem.m_quality + 1;   // the level being made, as DoCrafting counts it
			float brk = target <= RaidBossPlugin.SureIdolUpTo.Value ? 0f : BreakChance(target);
			// the game's roll: r in [0,1]; success if upgradeChance >= r, else broken if breakChance >= 1 - r, else down a level
			shared.m_upgradeChance = 1f - brk; shared.m_breakChance = 1f;   // breakChance 1 = never the "down a level" branch
		}

		static void Finalizer()
		{
			if (shared != null) { shared.m_upgradeChance = savedUp; shared.m_breakChance = savedBreak; shared = null; }
			if (req != null) { req.m_amount = savedAmount; req.m_amountPerLevel = savedPerLevel; req = null; }
		}
	}
}
