using System;
using System.Collections.Generic;
using System.IO;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;

namespace BossDirector;

// Server-only. Adds waves of creatures to boss fights at health thresholds, scaled by the players present.
// No Harmony patches and nothing on the client: the server reads the boss's health from its ZDO and creates the adds
// as ZDOs that the boss's owner instantiates (see Director.cs). Every fight is one config line, reloaded live.
[BepInPlugin(GUID, Name, Version)]
public class BossDirectorPlugin : BaseUnityPlugin
{
	public const string GUID = "bruceirons.BossDirector";
	public const string Name = "BossDirector";
	public const string Version = "0.3.0";

	internal static ManualLogSource Log;
	internal static BossDirectorPlugin Instance;

	internal static ConfigEntry<bool> Enabled;
	internal static ConfigEntry<float> Range;
	internal static ConfigEntry<bool> AddsHunt;
	internal static ConfigEntry<bool> RemoveAddsOnDeath;
	internal static ConfigEntry<float> CapBase;
	internal static ConfigEntry<float> CapPerPlayer;
	internal static ConfigEntry<float> RingMin;
	internal static ConfigEntry<float> RingMax;
	internal static ConfigEntry<int> ForcePlayers;
	internal static ConfigEntry<float> SearchInterval;
	internal static ConfigEntry<float> AddDamage;
	internal static ConfigEntry<float> AddHealth;
	internal static ConfigEntry<float> MoreAdds;
	internal static ConfigEntry<bool> FightModeEnabled;
	internal static ConfigEntry<float> FightEnemyDamage;
	internal static ConfigEntry<float> FightBossDamage;
	internal static ConfigEntry<float> FightModeLinger;

	static readonly Dictionary<string, ConfigEntry<string>> Scripts = new Dictionary<string, ConfigEntry<string>>();

	const string ScriptHelp =
		"Rules separated by | . A rule is  TRIGGER \"optional message\": SPAWNS\n" +
		"TRIGGER  75%                          fires once when the boss drops to 75% health\n" +
		"         every 45s below 50% above 25%   repeats while the boss is hurt and inside that health band (both bands optional);\n" +
		"                                      repeating rules stop at the living-adds cap, threshold waves ignore it\n" +
		"SPAWNS   comma separated  Prefab base+perPlayer   count = base + perPlayer x players, rounded down.\n" +
		"         Hatchling 1+1 = 2 solo, 5 with four. Fenring 0.5+0.5 = 1,1,2,2. StoneGolem 0+0.25 = only with 4 or more.\n" +
		"         Wolf* is a one-star wolf, Wolf** two stars.\n" +
		"Empty = this boss is left alone. Changes apply within a few seconds, also mid-fight (waves already passed do not fire late).";

	const string DragonDefault =
		"every 45s above 25%: Hatchling 0+0.34 | " +
		"75% \"Moder calls her brood\": Hatchling 0+1.34, Wolf 0.5+0.25 | " +
		"50% \"The pack answers her call\": Wolf 0+1.34, Wolf* 0+0.34 | " +
		"25% \"Her last guard descends\": StoneGolem 1+0, Hatchling 0+0.34, Hatchling 0+0.34 | " +
		"every 25s below 25%: Hatchling 0+0.34";

	// The Elder and Bonemass already summon in vanilla (roots; skeletons and blobs), so their scripts are lighter than
	// Moder's: the Elder keeps a slow trickle, Bonemass has none until the end because his own throw is one.
	const string ElderDefault =
		"every 50s above 25%: Greydwarf 0+0.34 | " +
		"75% \"The forest stirs\": Greydwarf 0+1.34 | " +
		"50% \"Shamans tend their king\": Greydwarf_Shaman 0.5+0.5, Greydwarf 1+0 | " +
		"25% \"The wrath of the forest\": Greydwarf_Elite 1+0, Troll 0+0.34 | " +
		"every 30s below 25%: Greydwarf 0+0.34";

	const string BonemassDefault =
		"75% \"The dead rise from the mire\": Draugr 0+1.34 | " +
		"50% \"Archers take aim from the murk\": Draugr_Ranged 0.5+0.5, Draugr* 0+0.34 | " +
		"25% \"A champion of the drowned\": Draugr_Elite 1+0, Wraith 0+0.34 | " +
		"every 30s below 25%: Draugr 0+0.34";

	const string GoblinKingDefault =
		"80% \"The last of his people answer\": Goblin 0+1 | " +
		"60% \"Shamans draw upon their king\": GoblinShaman 0.5+0.5, Goblin 0+0.5 | " +
		"40% \"A champion of the fallen cities\": GoblinBrute 1+0, GoblinBrute* 0+0.25 | " +
		"20% \"They will not bend or break\": Goblin 1+0, Goblin* 0+0.5, Goblin* 0+0.5, GoblinArcher 0+0.34 | " +
		"every 20s below 20%: Goblin 0+0.34";

	float tick;
	float reloadTimer;
	bool bound;
	DateTime configStamp;

	void Awake()
	{
		Log = Logger;
		Instance = this;
		Enabled = Config.Bind("1 - General", "Enabled", true, "Master switch. Off = no adds are spawned; fights in progress are forgotten.");
		Range = Config.Bind("1 - General", "Engaged range (m)", 100f, "A player within this distance of the boss counts towards the wave sizes and sees the wave messages. With nobody in range a fight pauses.");
		AddsHunt = Config.Bind("1 - General", "Adds hunt players", true, "On = adds come straight for the players, like raid creatures. Off = they behave like a creature that was already there.");
		RemoveAddsOnDeath = Config.Bind("1 - General", "Remove adds when the boss dies", true, "On = adds still alive vanish when the boss dies (no loot from those). Off = they stay and must be killed.");
		CapBase = Config.Bind("2 - Scaling", "Living adds cap, base", 2f, "Repeating rules (every NNs) stop while this many adds are alive: base + per player x players. Default 2 + 1 = 3 solo, 6 with four. Threshold waves always arrive in full.");
		CapPerPlayer = Config.Bind("2 - Scaling", "Living adds cap, per player", 1f, "See above.");
		RingMin = Config.Bind("2 - Scaling", "Spawn ring, inner (m)", 12f, "Adds appear at a random point between this and the outer distance from the boss.");
		RingMax = Config.Bind("2 - Scaling", "Spawn ring, outer (m)", 20f, "See above.");
		SearchInterval = Config.Bind("1 - General", "Boss search interval (s)", 5f, "How often the server looks for a newly spawned boss around the players. A fight that is already known is followed every second regardless, and that costs nothing.");
		FightModeEnabled = Config.Bind("5 - Boss fight mode", "Enabled", true, "Needs BruceQoL on this server (any version since 1.5.0). While players are in range of a boss, BruceQoL's damage settings are changed on the server and BruceQoL pushes them to every player at once; when the fight is over they are put back. Held in memory only: BruceQoL's config file is never written, so a crash mid-fight cannot leave the server on the fight values. The settings are server-wide, so players elsewhere in the world are affected too while a fight is on. Off, or no BruceQoL: damage is never touched and waves stay as written.");
		FightEnemyDamage = Config.Bind("5 - Boss fight mode", "Enemy damage to players during a fight (x)", 0.6f, new ConfigDescription("BruceQoL's 'Enemy damage to players' while a boss fight is on: adds and everything else that is not a boss. Works for every player, whatever BruceQoL version they run.", new AcceptableValueRange<float>(0.05f, 3f)));
		FightBossDamage = Config.Bind("5 - Boss fight mode", "Boss damage to players during a fight (x)", 1f, new ConfigDescription("BruceQoL's 'Boss damage to players' while a boss fight is on. Needs BruceQoL 1.19.2+ on the server to exist at all, and on a player's game to apply to that player; a player on an older BruceQoL takes the enemy number above from the boss too, which errs on the easy side.", new AcceptableValueRange<float>(0.05f, 5f)));
		FightModeLinger = Config.Bind("5 - Boss fight mode", "Stays on after the last player leaves (seconds)", 15f, new ConfigDescription("So deaths and respawns do not switch it off and on.", new AcceptableValueRange<float>(0f, 300f)));
		AddDamage = Config.Bind("2 - Scaling", "Add damage (x)", 1f, new ConfigDescription("Extra, per-add damage multiplier, on top of anything boss fight mode does. 1 = none (the default since 0.3.0: boss fight mode does this job for everyone at once). Below 1 it is written on each add and applied only by players running BruceQoL 1.19.0+.", new AcceptableValueRange<float>(0.05f, 3f)));
		AddHealth = Config.Bind("2 - Scaling", "Add health (x)", 1f, new ConfigDescription("Adds arrive with this share of their health, so they die sooner. 1 = full health. Needs nothing on the players' side; the only visible sign is a health bar that starts part empty.", new AcceptableValueRange<float>(0.1f, 1f)));
		MoreAdds = Config.Bind("2 - Scaling", "More adds while their damage is scaled (x)", 1.3f, new ConfigDescription("Used exactly while boss fight mode (section 5) is on, because that is when every player takes reduced damage from adds: every wave count and the living-adds cap are multiplied by this and rounded down, so at 1.3 a wave of 4 becomes 5, 5 becomes 6, 8 becomes 10, and 1 to 3 stay as they are. 1 = never more adds. No BruceQoL on the server, or boss fight mode off: the scripts run exactly as written.", new AcceptableValueRange<float>(1f, 4f)));
		ForcePlayers = Config.Bind("4 - Debug", "Pretend this many players", 0, "0 = count real players. Anything else sizes every wave as if that many were fighting, so one person can see a four-player fight. Someone still has to be in range.");
		configStamp = Stamp();
	}

	DateTime Stamp()
	{
		try { return File.GetLastWriteTimeUtc(Config.ConfigFilePath); } catch { return DateTime.MinValue; }
	}

	internal static string ScriptFor(string bossPrefab) => Scripts.TryGetValue(bossPrefab, out ConfigEntry<string> e) ? e.Value : "";

	// Boss prefabs are only known once the game has loaded them, so their config entries are bound late.
	void BindScripts()
	{
		FightMode.Find();
		Director.CollectBossPrefabs();
		var names = new List<string>(Director.BossNames);
		names.Sort(StringComparer.Ordinal);
		bool first = true;
		foreach (string boss in names)
		{
			string def = boss == "Dragon" ? DragonDefault : boss == "GoblinKing" ? GoblinKingDefault : boss == "gd_king" ? ElderDefault : boss == "Bonemass" ? BonemassDefault : "";
			string help = (boss == "Dragon" ? "Moder. " : boss == "GoblinKing" ? "Yagluth. " : boss == "gd_king" ? "The Elder. " : "") + (first ? ScriptHelp : "Same format as the first entry in this section.");
			Scripts[boss] = Config.Bind("3 - Encounters", boss, def, help);
			first = false;
			Encounter parsed = Encounter.Parse(Scripts[boss].Value);
			foreach (string err in parsed.Errors) Log.LogWarning($"{boss} script: {err}");
			foreach (Encounter.Rule rule in parsed.Rules)
				foreach (Encounter.Spawn s in rule.Spawns)
				{
					UnityEngine.GameObject p = ZNetScene.instance.GetPrefab(s.Prefab);
					if (p == null || p.GetComponent<Character>() == null) Log.LogWarning($"{boss} script: '{s.Prefab}' is not a creature prefab in this game");
				}
			if (parsed.Rules.Count > 0) Log.LogInfo($"{boss}: solo {parsed.Describe(1)}  ||  four players {parsed.Describe(4)}");
		}
		Log.LogInfo($"BossDirector {Version}: {names.Count} boss prefabs ({string.Join(", ", names)})");
		configStamp = Stamp();
	}

	void Update()
	{
		if (ZNet.instance == null || ZNetScene.instance == null || ZDOMan.instance == null || ZoneSystem.instance == null || WorldGenerator.instance == null)
		{
			if (bound) { bound = false; Scripts.Clear(); FightMode.Leave("the world closed"); Director.Reset(); }
			return;
		}
		if (!ZNet.instance.IsServer()) return;
		if (!bound) { BindScripts(); bound = true; }

		float dt = UnityEngine.Time.unscaledDeltaTime;
		reloadTimer += dt;
		if (reloadTimer >= 3f)
		{
			reloadTimer = 0f;
			DateTime now = Stamp();
			if (now != configStamp)
			{
				configStamp = now;
				try { Config.Reload(); Log.LogInfo("config reloaded"); } catch (Exception e) { Log.LogWarning("config reload failed: " + e.Message); }
			}
		}

		tick += dt;
		if (tick < 1f) return;
		float step = tick;
		tick = 0f;
		if (!Enabled.Value) { FightMode.Leave("BossDirector was switched off"); if (Director.ActiveFights > 0) Director.Reset(); return; }
		try { Director.Tick(step); }
		catch (Exception e) { Log.LogError(e); }
	}
}
