using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace RaidBoss;

// "You are being hunted": a boss's add waves, played around a player in the open world, with no boss. A way to try the
// waves themselves - their size, their stars, how they arrive - without summoning anyone. Server side, like the rest
// of the director; started from the config ("Start a hunt" = "GoblinKing heroic Anthony").
//
// A hunt has no health to fall, so it walks down the boss's thresholds by itself: the next wave comes when the last one
// is dead, or after "Hunt, next wave after (s)" at the latest. The trickles of the band it is in run in between, up to
// the usual living-adds cap. It ends when the last threshold wave is dead. A rule's actions (ground strikes, ward,
// guard, traits on the boss, breaks) are boss mechanics and are left out: a hunt is only the waves.
//
// It looks like a raid: a random event of its own ("raidboss_hunt", added to every game's event list, never started by
// chance) gives the red circle on the map - kept on the hunted player as they move - the raid music, and the start and
// end messages, all through the game's own event code.
internal static class Hunts
{
	static readonly int AddTag = "raidboss_add".GetStableHashCode();
	static readonly int AddDamageKey = "raidboss_dmg".GetStableHashCode();
	static readonly int ModeKey = Modes.ModeName.GetStableHashCode();

	sealed class Hunt
	{
		public string Boss, Player;
		public Encounter Script;
		public bool Heroic;
		public bool[] Fired;
		public float[] Timers;
		public float Fraction = 1f, SinceWave, Age;
		public readonly List<ZDOID> LastWave = new List<ZDOID>();
		public readonly List<ZDOID> All = new List<ZDOID>();
		public bool FinalFired;
	}

	static Hunt hunt;
	internal const string EventName = "raidboss_hunt";

	// Every game (server and players) learns the hunt event, so the event the server announces can be shown.
	[HarmonyPatch(typeof(RandEventSystem), "Awake")]
	static class AddEventPatch
	{
		static void Postfix(RandEventSystem __instance)
		{
			AddEvent(__instance, EventName);
			AddEvent(__instance, Warbands.EventName);   // the same mood at a warband's site
		}

		static void AddEvent(RandEventSystem __instance, string name)
		{
			if (__instance.m_events == null || __instance.m_events.Exists(e => e != null && e.m_name == name)) return;
			RandomEvent model = __instance.m_events.Find(e => e != null && e.m_name == "army_goblin") ?? __instance.m_events.Find(e => e != null);
			if (model == null) return;
			RandomEvent ev = model.Clone();
			ev.m_name = name;
			ev.m_enabled = true;
			ev.m_random = false;               // never picked by chance
			ev.m_standaloneInterval = 0f;
			ev.m_spawn = new List<SpawnSystem.SpawnData>();   // the waves are the director's, not the event's
			ev.m_duration = 3600f;             // ended by the hunt
			ev.m_pauseIfNoPlayerInArea = false;
			ev.m_nearBaseOnly = false;
			ev.m_forceEnvironment = "";
			ev.m_requiredGlobalKeys = new List<string>();
			ev.m_notRequiredGlobalKeys = new List<string>();
			__instance.m_events.Add(ev);
		}
	}

	// The messages come from the server's config (pushed to the players), read when the event starts on each game.
	[HarmonyPatch(typeof(RandomEvent), nameof(RandomEvent.OnActivate))]
	static class MessagesPatch
	{
		static void Prefix(RandomEvent __instance)
		{
			if (__instance.m_name == Warbands.EventName)
			{
				__instance.m_startMessage = RaidBossPlugin.WarbandFightMessage.Value ?? "";
				__instance.m_endMessage = "";
				return;
			}
			if (__instance.m_name != EventName) return;
			__instance.m_startMessage = RaidBossPlugin.HuntMessage.Value ?? "";
			__instance.m_endMessage = RaidBossPlugin.HuntEndMessage.Value ?? "";
		}
	}

	static RandomEvent Current => RandEventSystem.instance != null ? RandEventSystem.instance.GetCurrentRandomEvent() : null;

	static void EndEvent()
	{
		if (Current != null && Current.m_name == EventName) RandEventSystem.instance.ResetRandomEvent();
	}
	const float Grace = 8f;   // between "you are being hunted" and the first wave

	internal static void Tick(float dt)
	{
		string order = (RaidBossPlugin.HuntOrder.Value ?? "").Trim();
		if (order.Length > 0)
		{
			RaidBossPlugin.HuntOrder.Value = "";   // one order, one hunt
			try { Start(order); } catch (Exception e) { RaidBossPlugin.Log.LogWarning($"hunt: {e.Message}"); }
		}
		if (hunt == null) return;
		try { Step(hunt, dt); }
		catch (Exception e) { RaidBossPlugin.Log.LogError($"hunt: {e}"); EndEvent(); hunt = null; }
	}

	static void Start(string order)
	{
		string[] words = order.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
		if (words[0].Equals("stop", StringComparison.OrdinalIgnoreCase)) { Log(Stop()); return; }
		bool heroic = false;
		string who = "";
		for (int i = 1; i < words.Length; i++)
		{
			if (words[i].Equals("heroic", StringComparison.OrdinalIgnoreCase)) heroic = true;
			else who = who.Length == 0 ? words[i] : who + " " + words[i];
		}
		Log(Begin(words[0], heroic, who));
	}

	internal static string Stop()
	{
		if (hunt == null) return "no hunt is running";
		string said = $"stopped ({hunt.Boss} on {hunt.Player})";
		EndEvent();
		hunt = null;
		return said;
	}

	internal static string Begin(string boss, bool heroic, string who)
	{
		if (hunt != null) return $"one is already running ({hunt.Boss} on {hunt.Player}); stop it first";
		Encounter script = Encounter.Parse(RaidBossPlugin.ScriptFor(boss));
		if (script.Rules.Count == 0) return $"'{boss}' has no script (use the boss's prefab name: Eikthyr, gd_king, Bonemass, Dragon, GoblinKing)";
		if (!Find(who, out Target peer)) return who.Length > 0 ? $"no player called '{who}' is connected" : "nobody is connected";
		if (Current != null && Current.m_name != EventName) return $"a raid ({Current.m_name}) is on; try again when it is over";
		hunt = new Hunt
		{
			Boss = boss, Player = peer.Name, Script = script, Heroic = heroic,
			Fired = new bool[script.Rules.Count], Timers = new float[script.Rules.Count],
		};
		// Rules that send no adds (boss mechanics only) are not waves: marked done from the start, so they never stand in
		// for one.
		for (int i = 0; i < script.Rules.Count; i++) if (script.Rules[i].Spawns.Count == 0) hunt.Fired[i] = true;
		RandEventSystem.instance?.SetRandomEventByName(EventName, peer.Pos);
		return $"{(heroic ? "HEROIC " : "")}{boss} waves on {peer.Name}";
	}

	// The hunted player: a connected peer by name (or the first one), or the test harness's first fake player.
	struct Target { public string Name; public Vector3 Pos; }

	static bool Find(string who, out Target target)
	{
		target = default;
		if (Director.DebugPlayers != null && Director.DebugPlayers.Count > 0)
		{
			target = new Target { Name = "test", Pos = Director.DebugPlayers[0].Value };
			return true;
		}
		foreach (ZNetPeer p in ZNet.instance.GetPeers())
			if (p.IsReady() && (who.Length == 0 || who == "test" || p.m_playerName.Equals(who, StringComparison.OrdinalIgnoreCase)))
			{
				target = new Target { Name = p.m_playerName, Pos = p.m_refPos };
				return true;
			}
		return false;
	}

	static void Log(string text) => RaidBossPlugin.Log.LogInfo("hunt: " + text);

	static bool Alive(ZDOID id)
	{
		ZDO zdo = ZDOMan.instance.GetZDO(id);
		return zdo != null && zdo.IsValid() && zdo.GetBool(AddTag);
	}

	static void Step(Hunt h, float dt)
	{
		if (!Find(h.Player, out Target peer)) { Log($"{h.Player} left; the hunt is off"); EndEvent(); hunt = null; return; }
		h.Age += dt;
		if (h.Age < Grace) return;
		Vector3 center = peer.Pos;
		if (Current != null && Current.m_name == EventName) Current.m_pos = center;   // the circle follows; sent to the players every 2 s
		int players = Math.Max(1, Director.CountPlayers(center, RaidBossPlugin.Range.Value, out long nearest));
		if (RaidBossPlugin.ForcePlayers.Value > 0) players = RaidBossPlugin.ForcePlayers.Value;
		float saved = Encounter.CountMultiplier;
		Encounter.CountMultiplier = RaidBossPlugin.MoreAdds.Value * (h.Heroic ? RaidBossPlugin.HeroicMoreAdds.Value : 1f);
		try
		{
			h.All.RemoveAll(id => !Alive(id));
			bool waveAlive = h.LastWave.Exists(Alive);
			h.SinceWave += dt;

			// the next threshold, if the last wave is dead or has had its time
			if (!waveAlive || h.SinceWave >= RaidBossPlugin.HuntWaveGap.Value)
			{
				float next = -1f;
				for (int i = 0; i < h.Script.Rules.Count; i++)
				{
					Encounter.Rule r = h.Script.Rules[i];
					if (r.Repeating || h.Fired[i] || !Applies(h, r) || r.Threshold >= h.Fraction) continue;
					next = Mathf.Max(next, r.Threshold);
				}
				if (next >= 0f) h.Fraction = Mathf.Max(0f, next - 0.0001f);
				else if (h.FinalFired && !waveAlive) { Log($"over: {h.Boss} waves done"); EndEvent(); hunt = null; return; }
			}

			for (int i = 0; i < h.Script.Rules.Count; i++)
			{
				Encounter.Rule rule = h.Script.Rules[i];
				if (!Applies(h, rule)) continue;
				if (!rule.Repeating)
				{
					if (h.Fired[i] || h.Fraction > rule.Threshold) continue;
					h.Fired[i] = true;
					h.LastWave.Clear();
					h.SinceWave = 0f;
					Log($"{rule.Threshold * 100f:0}% wave, {players} player(s)");
					Spawn(h, rule, center, nearest, players, int.MaxValue, h.LastWave);
					if (rule.Message.Length > 0) Director.Message(center, rule.Message);
					h.FinalFired = !HasLaterThreshold(h);
					continue;
				}
				if (h.Fraction >= rule.Below || h.Fraction < rule.Above || h.Fraction >= 0.999f) { h.Timers[i] = 0f; continue; }
				h.Timers[i] += dt;
				if (h.Timers[i] < rule.Interval) continue;
				h.Timers[i] = 0f;
				int cap = Mathf.FloorToInt((RaidBossPlugin.CapBase.Value + RaidBossPlugin.CapPerPlayer.Value * players) * Encounter.CountMultiplier + 0.001f);
				int room = cap - h.All.Count;
				if (room > 0) Spawn(h, rule, center, nearest, players, room, null);
			}
		}
		finally { Encounter.CountMultiplier = saved; }
	}

	static bool Applies(Hunt h, Encounter.Rule r) => !(r.HeroicOnly && !h.Heroic) && !(r.NormalOnly && h.Heroic);

	static bool HasLaterThreshold(Hunt h)
	{
		for (int i = 0; i < h.Script.Rules.Count; i++)
			if (!h.Script.Rules[i].Repeating && !h.Fired[i] && Applies(h, h.Script.Rules[i])) return true;
		return false;
	}

	// The same wave as the boss fight sends, stars included (Director.SpawnRule), around the hunted player.
	static void Spawn(Hunt h, Encounter.Rule rule, Vector3 center, long owner, int players, int room, List<ZDOID> wave)
	{
		bool heroicWave = h.Heroic && !rule.Repeating && !rule.HeroicOnly && RaidBossPlugin.HeroicGuaranteedStar.Value;
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
				if (h.Heroic && level == 1 && UnityEngine.Random.value < RaidBossPlugin.HeroicStarChance.Value) level = 2;
				ZDOID id = Director.SpawnCreature(spawn.Prefab, level, center, RaidBossPlugin.RingMin.Value, RaidBossPlugin.RingMax.Value, owner, AddTag, "hunt " + h.Boss);
				if (id.IsNone()) continue;
				ZDO zdo = ZDOMan.instance.GetZDO(id);
				float dmg = RaidBossPlugin.AddDamage.Value;
				if (dmg > 0f && !Mathf.Approximately(dmg, 1f)) zdo.Set(AddDamageKey, dmg);
				if (spawn.Trait.Length > 0)
				{
					string mode = RaidBossPlugin.TraitMode(spawn.Trait);
					if (mode.Length > 0) zdo.Set(ModeKey, mode);
				}
				h.All.Add(id);
				wave?.Add(id);
				made++;
			}
		}
		// A rule's actions (strikes, wards, traits on the boss...) are boss mechanics: a hunt is only the waves.
	}
}
