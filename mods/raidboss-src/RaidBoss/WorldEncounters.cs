using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using UnityEngine;

namespace RaidBoss;

// Encounters away from bosses: kill enough of something at a known place and the place answers.
//
//   GoblinCamp2 80m: 10 Goblin,GoblinArcher,GoblinShaman in 600s -> GoblinBrute 1+0, cooldown 1800s
//
// How the server knows:
//   - a kill: ZDOMan.m_onZDODestroyed fires on the server for every ZDO that is destroyed, with the ZDO still
//     readable. A creature's ZDO is destroyed when it dies. It is ALSO destroyed when it despawns, which the server
//     cannot tell apart, so this is "vanished near a player", good enough for a camp being cleared in daylight.
//     Zone unloading does not destroy anything, and creatures this mod spawned are never counted.
//   - a place: ZoneSystem.m_locationInstances holds every location the world generator placed, by prefab name.
// No messages: the reinforcement just arrives. Rules are separated by | and re-read live with the rest of the config.
internal static class WorldEncounters
{
	sealed class Def
	{
		public string Raw;
		public string Location;
		public float Radius;
		public int Kills;
		public readonly HashSet<int> Victims = new HashSet<int>();
		public float Window;
		public float Cooldown;
		public readonly List<Encounter.Spawn> Spawns = new List<Encounter.Spawn>();
		public List<Vector3> Sites;   // positions of the matching locations, found lazily
	}

	sealed class Site
	{
		public readonly Queue<float> KillTimes = new Queue<float>();
		public float ReadyAt;
	}

	struct Pending { public Def Def; public Vector3 Site; public Vector3 At; }

	static readonly Regex RuleRx = new Regex(@"^(\S+)\s+(\d+(?:\.\d+)?)\s*m\s*:\s*(\d+)\s+(.+?)\s+in\s+(\d+(?:\.\d+)?)\s*s\s*->\s*(.+)$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
	static readonly Regex CooldownRx = new Regex(@",?\s*cooldown\s+(\d+(?:\.\d+)?)\s*s\s*$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
	static readonly int Tag = "raidboss_enc".GetStableHashCode();

	static readonly List<Def> defs = new List<Def>();
	static readonly Dictionary<Vector3, Site> sites = new Dictionary<Vector3, Site>();
	static readonly List<Pending> pending = new List<Pending>();
	static string parsedFrom;
	static ZDOMan hookedTo;
	static int lastLocationCount = -1;

	static float F(string s) => float.Parse(s, CultureInfo.InvariantCulture);

	static void Parse(string text)
	{
		parsedFrom = text;
		defs.Clear();
		foreach (string rawRule in (text ?? "").Split('|'))
		{
			string s = rawRule.Trim();
			if (s.Length == 0) continue;
			var def = new Def { Raw = s };
			Match cd = CooldownRx.Match(s);
			if (cd.Success) { def.Cooldown = F(cd.Groups[1].Value); s = s.Substring(0, cd.Index).Trim(); }
			Match m = RuleRx.Match(s);
			if (!m.Success)
			{
				RaidBossPlugin.Log.LogWarning($"world encounter '{rawRule.Trim()}' is not 'Location 80m: 10 PrefabA,PrefabB in 600s -> Prefab 1+0, cooldown 1800s'");
				continue;
			}
			def.Location = m.Groups[1].Value;
			def.Radius = F(m.Groups[2].Value);
			def.Kills = Math.Max(1, int.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture));
			def.Window = Math.Max(1f, F(m.Groups[5].Value));
			foreach (string v in m.Groups[4].Value.Split(','))
			{
				string name = v.Trim();
				if (name.Length == 0) continue;
				if (ZNetScene.instance.GetPrefab(name) == null) RaidBossPlugin.Log.LogWarning($"world encounter at {def.Location}: '{name}' is not a prefab in this game");
				def.Victims.Add(name.GetStableHashCode());
			}
			foreach (string sp in m.Groups[6].Value.Split(','))
			{
				if (sp.Trim().Length == 0) continue;
				if (Encounter.TryParseSpawn(sp, out Encounter.Spawn spawn)) def.Spawns.Add(spawn);
				else RaidBossPlugin.Log.LogWarning($"world encounter at {def.Location}: spawn '{sp.Trim()}' is not 'Prefab[*] base+perPlayer'");
			}
			if (def.Victims.Count == 0 || def.Spawns.Count == 0) continue;
			def.Sites = FindSites(def.Location);
			RaidBossPlugin.Log.LogInfo($"world encounter: {def.Kills} kills within {def.Radius:0} m of a '{def.Location}' ({def.Sites.Count} in this world) inside {def.Window:0} s -> {m.Groups[6].Value.Trim()}, then quiet for {def.Cooldown:0} s");
			if (def.Sites.Count == 0 && ZoneSystem.instance.m_locationInstances.Count > 0) RaidBossPlugin.Log.LogWarning($"no location called '{def.Location}' in this world. Names that look similar: {Similar(def.Location)}");
			defs.Add(def);
		}
	}

	static List<Vector3> FindSites(string location)
	{
		var list = new List<Vector3>();
		foreach (ZoneSystem.LocationInstance inst in ZoneSystem.instance.m_locationInstances.Values)
			if (inst.m_location != null && string.Equals(inst.m_location.m_prefabName, location, StringComparison.OrdinalIgnoreCase)) list.Add(inst.m_position);
		return list;
	}

	static string Similar(string location)
	{
		var names = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
		string stem = location.Length > 4 ? location.Substring(0, 4) : location;
		foreach (ZoneSystem.LocationInstance inst in ZoneSystem.instance.m_locationInstances.Values)
			if (inst.m_location?.m_prefabName != null && inst.m_location.m_prefabName.IndexOf(stem, StringComparison.OrdinalIgnoreCase) >= 0) names.Add(inst.m_location.m_prefabName);
		return names.Count == 0 ? "(none)" : string.Join(", ", names);
	}

	// Once a second from the plugin: keeps the hook and the parsed rules current, and performs the spawns that kills
	// queued (creating ZDOs from inside ZDOMan's own destroy handling is asking for trouble).
	internal static void Tick()
	{
		if (ZDOMan.instance != hookedTo)
		{
			hookedTo = ZDOMan.instance;
			sites.Clear();
			pending.Clear();
			parsedFrom = null;
			lastLocationCount = -1;
		}
		if (hookedTo == null) return;
		if (!RaidBossPlugin.WorldEncountersEnabled.Value) { pending.Clear(); return; }
		if (RaidBossPlugin.WorldEncounterRules.Value != parsedFrom) Parse(RaidBossPlugin.WorldEncounterRules.Value);
		// The world's location list fills over the first seconds after the world opens (and can grow later), so the
		// sites are looked up again whenever its size changes.
		int locations = ZoneSystem.instance.m_locationInstances.Count;
		if (locations != lastLocationCount)
		{
			lastLocationCount = locations;
			foreach (Def def in defs)
			{
				int before = def.Sites.Count;
				def.Sites = FindSites(def.Location);
				if (def.Sites.Count != before) RaidBossPlugin.Log.LogInfo($"world encounter: {def.Sites.Count} '{def.Location}' in this world");
			}
		}

		foreach (Pending p in pending)
		{
			int players = Math.Max(1, Director.CountPlayers(p.At, 80f, out long nearest));
			foreach (Encounter.Spawn spawn in p.Def.Spawns)
			{
				int count = spawn.PlainCount(players);
				for (int i = 0; i < count; i++)
					Director.SpawnCreature(spawn.Prefab, spawn.Level, p.At, 14f, 24f, nearest, Tag, $"{p.Def.Location} at {p.Site:0}");
			}
		}
		pending.Clear();
	}

	// A client reported a creature it was simulating died (Net.cs). 'ours' = this mod spawned it; those never count.
	internal static void OnKill(int prefab, Vector3 pos, bool ours)
	{
		try
		{
			if (defs.Count == 0 || ours || !RaidBossPlugin.WorldEncountersEnabled.Value || !RaidBossPlugin.Enabled.Value) return;
			foreach (Def def in defs)
			{
				if (!def.Victims.Contains(prefab)) continue;
				if (!NearestSite(def, pos, out Vector3 sitePos)) continue;

				if (!sites.TryGetValue(sitePos, out Site site)) sites[sitePos] = site = new Site();
				float now = Time.time;
				site.KillTimes.Enqueue(now);
				while (site.KillTimes.Count > 0 && now - site.KillTimes.Peek() > def.Window) site.KillTimes.Dequeue();
				if (site.KillTimes.Count < def.Kills || now < site.ReadyAt) continue;

				site.KillTimes.Clear();
				site.ReadyAt = now + def.Cooldown;
				pending.Add(new Pending { Def = def, Site = sitePos, At = pos });
				RaidBossPlugin.Log.LogInfo($"world encounter: {def.Kills} kills at the {def.Location} at {sitePos:0}; reinforcements are coming, next possible in {def.Cooldown:0} s");
			}
		}
		catch (Exception e) { RaidBossPlugin.Log.LogError("world encounter: " + e); }
	}

	static bool NearestSite(Def def, Vector3 pos, out Vector3 sitePos)
	{
		sitePos = default;
		float best = def.Radius * def.Radius;
		bool found = false;
		foreach (Vector3 s in def.Sites)
		{
			float dx = s.x - pos.x, dz = s.z - pos.z, d = dx * dx + dz * dz;
			if (d > best) continue;
			best = d; sitePos = s; found = true;
		}
		return found;
	}
}
