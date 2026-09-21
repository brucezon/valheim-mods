using System;
using System.Collections.Generic;
using System.IO;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using ServerSync;
using UnityEngine;

namespace RaidBoss;

// Boss fights for groups. Installed on the server AND on every client (ServerSync refuses a client without it).
//
// The server still makes every decision, exactly as the server-only BossDirector it replaces did: it finds bosses
// among the ZDOs around the players, reads their health, creates adds and idols as bare ZDOs that the boss's owner
// brings to life (Director.cs), and answers raids on known places (WorldEncounters.cs). The client half is thin and
// exists for what a server cannot do (ClientSide.cs, Net.cs): scale the damage a player takes from a boss or an add,
// report kills exactly, and shorten the fireside wait after a death.
[BepInPlugin(GUID, Name, Version)]
public class RaidBossPlugin : BaseUnityPlugin
{
	public const string GUID = "bruceirons.RaidBoss";
	public const string Name = "RaidBoss";
	public const string Version = "0.1.8";
	// Oldest version still let in. Rule: this is the PREVIOUS release unless a release changes something both sides
	// must agree on (the three network messages in Net.cs, or the ZDO keys). Pinning it to Version locks out every
	// player who has not updated yet.
	const string MinimumVersion = "0.1.6";   // 0.1.6: the boss's owner wears its ward down, and the hunt button needs the server's new message

	static readonly ConfigSync configSync = new(Name) { DisplayName = Name, CurrentVersion = Version, MinimumRequiredVersion = MinimumVersion, ModRequired = true };

	internal enum Toggle { On = 1, Off = 0 }

	internal static ManualLogSource Log;

	static ConfigEntry<Toggle> serverConfigLocked;
	static ConfigEntry<Toggle> enabled;
	internal static bool IsOn => enabled != null && enabled.Value == Toggle.On;
	// The director code reads these as plain values.
	internal static EnabledView Enabled = new EnabledView();
	internal sealed class EnabledView { public bool Value => IsOn; }

	internal static ConfigEntry<float> Range;
	internal static ConfigEntry<bool> AddsHunt;
	internal static ConfigEntry<bool> RemoveAddsOnDeath;
	internal static ConfigEntry<float> SearchInterval;
	internal static ConfigEntry<float> CapBase;
	internal static ConfigEntry<float> CapPerPlayer;
	internal static ConfigEntry<float> RingMin;
	internal static ConfigEntry<float> RingMax;
	internal static ConfigEntry<float> AddDamage;
	internal static ConfigEntry<float> AddHealth;
	internal static ConfigEntry<float> MoreAdds;
	internal static ConfigEntry<float> BossDamage;
	internal static ConfigEntry<int> ForcePlayers;
	internal static ConfigEntry<bool> WorldEncountersEnabled;
	internal static ConfigEntry<string> WorldEncounterRules;
	internal static ConfigEntry<bool> HeroicEnabled;
	internal static ConfigEntry<bool> HeroicCostsTrophy;
	internal static ConfigEntry<float> HeroicAltarRadius;
	internal static ConfigEntry<float> HeroicTrophyRadius;
	internal static ConfigEntry<float> HeroicBossDamage;
	internal static ConfigEntry<string> HeroicBossDamageByBoss;
	internal static ConfigEntry<float> HeroicBossHealth;
	internal static ConfigEntry<float> HeroicMoreAdds;
	internal static ConfigEntry<float> HeroicStarChance;
	internal static ConfigEntry<bool> HeroicGuaranteedStar;
	internal static ConfigEntry<string> HeroicMessage;
	internal static ConfigEntry<float> IdolBase;
	internal static ConfigEntry<float> IdolPerPlayer;
	internal static ConfigEntry<float> IdolBattleShare;
	internal static ConfigEntry<string> IdolTiers;
	internal static ConfigEntry<float> RestingTimeAfterDeath;
	internal static ConfigEntry<float> JustDiedWindow;
	internal static ConfigEntry<float> ParryTaunt;
	internal static ConfigEntry<bool> AddsAvoidShields;
	internal static ConfigEntry<string> VanillaTargetedAdds;
	internal static ConfigEntry<float> BreakSize;
	internal static ConfigEntry<float> BreakDrain;
	internal static ConfigEntry<float> BreakParry;
	internal static ConfigEntry<float> BreakWaveChunk;
	internal static ConfigEntry<float> BreakSeconds;
	internal static ConfigEntry<float> BreakTaken;
	internal static ConfigEntry<float> BreakGrowth;
	internal static ConfigEntry<float> BreakHit;
	internal static ConfigEntry<float> BreakWeak;
	internal static ConfigEntry<bool> BreakInHeroic;
	internal static ConfigEntry<string> Traits;
	internal static ConfigEntry<string> StrikeFx;
	internal static ConfigEntry<string> WardLabel;
	internal static ConfigEntry<string> WardColour;
	internal static ConfigEntry<bool> WardBubble;
	internal static ConfigEntry<string> HuntOrder;
	internal static ConfigEntry<HuntBoss> HuntBossChoice;
	internal static ConfigEntry<bool> HuntHeroic;
	internal static ConfigEntry<bool> HuntButtons;
	internal enum HuntBoss { Eikthyr, Elder, Bonemass, Moder, Yagluth }
	static string HuntPrefab(HuntBoss b) => b switch { HuntBoss.Elder => "gd_king", HuntBoss.Bonemass => "Bonemass", HuntBoss.Moder => "Dragon", HuntBoss.Yagluth => "GoblinKing", _ => "Eikthyr" };

	// Two buttons in the in-game config menu (F1): the click goes to the server, which starts the hunt on whoever clicked.
	static void DrawHuntButtons(ConfigEntryBase entry)
	{
		GUILayout.BeginHorizontal();
		if (GUILayout.Button("Start hunt on me", GUILayout.ExpandWidth(true))) Net.RequestHunt(HuntPrefab(HuntBossChoice.Value), HuntHeroic.Value, false);
		if (GUILayout.Button("Stop hunt", GUILayout.ExpandWidth(true))) Net.RequestHunt("", false, true);
		GUILayout.EndHorizontal();
	}
	internal static ConfigEntry<string> HuntMessage;
	internal static ConfigEntry<string> HuntEndMessage;
	internal static ConfigEntry<float> HuntWaveGap;

	static readonly Dictionary<string, ConfigEntry<string>> Scripts = new Dictionary<string, ConfigEntry<string>>();

	const string ScriptHelp =
		"Rules separated by | . A rule is  TRIGGER \"optional message\": SPAWNS\n" +
		"TRIGGER  75%                          fires once when the boss drops to 75% health\n" +
		"         every 45s below 50% above 25%   repeats while the boss is hurt and inside that health band (both bands optional);\n" +
		"                                      repeating rules stop at the living-adds cap, threshold waves ignore it\n" +
		"SPAWNS   comma separated  Prefab base+perPlayer   count = base + perPlayer x players, rounded down.\n" +
		"         Hatchling 1+1 = 2 solo, 5 with four. Fenring 0.5+0.5 = 1,1,2,2. StoneGolem 0+0.25 = only with 4 or more.\n" +
		"         Wolf* is a one-star wolf, Wolf** two stars.\n" +
		"         Add @1-2, @3+ or @4 after an entry to use it only for that many players, so one creature can REPLACE another:\n" +
		"         GoblinBrute 1+0 @1-2, GoblinBrute* 1+0 @3+  sends a plain one to one or two players and a one-star instead from three.\n" +
		"         Start a rule with  heroic  or  normal  to use it only in that kind of fight: normal 40%: ... | heroic 40%: ...\n" +
		"         A heroic rule arrives exactly as written; every other threshold wave gains its star in a heroic fight.\n" +
		"Empty = this boss is left alone. Changes apply within a few seconds, also mid-fight (waves already passed do not fire late).\n" +
		"These lines live on the server only; players' copies of this file do not need them.";

	const string DragonDefault =
		"every 45s above 25%: Hatchling 1+0 @2+, Hatchling 1+0 @3+, Hatchling 1+0 @4+ | " +
		"75% \"Moder calls her brood\": Hatchling 0+1.34, Wolf 0.5+0.25 | " +
		"50% \"The pack answers her call\": Wolf 0+1.34, Wolf* 0+0.34 | " +
		"25% \"Her last guard descends\": StoneGolem 1+0, Hatchling 0+0.34, Hatchling 0+0.34 | " +
		"every 25s below 25%: Hatchling 1+0 @2+, Hatchling 1+0 @3+, Hatchling 1+0 @4+ | " +
		"heroic every 30s: Wolf 1+0 @2+ | " +
		"heroic every 20s below 75%: strike frost r4 d2.5 dmg150 x2";

	// Eikthyr has no adds: the first boss stays a duel, and the sky joins in. Telegraphed, dodgeable lightning under the
	// players - one at a time in a normal fight, two at a time and more often in a heroic one.
	const string EikthyrDefault =
		"normal every 20s below 90%: strike lightning r3.5 d2.5 dmg20 | " +
		"heroic every 12s below 90%: strike lightning r3.5 d2 dmg20 x2";

	// The Elder and Bonemass already summon in vanilla (roots; skeletons and blobs), so their scripts are lighter than
	// Moder's: the Elder keeps a slow trickle, Bonemass has none until the end because his own throw is one.
	const string ElderDefault =
		"every 50s above 25%: Greydwarf 1+0 @2+, Greydwarf 1+0 @3+, Greydwarf 1+0 @4+ | " +
		"75% \"The forest stirs\": Greydwarf 0+1.34 | " +
		"50% \"Shamans tend their king\": Greydwarf_Shaman 0.5+0.5, Greydwarf 1+0 | " +
		"25% \"The wrath of the forest\": Greydwarf_Elite 1+0, Troll 0+0.34 | " +
		"every 30s below 25%: Greydwarf 1+0 @2+, Greydwarf 1+0 @3+, Greydwarf 1+0 @4+";

	const string BonemassDefault =
		"75% \"The dead rise from the mire\": Draugr 0+1.34 | " +
		"50% \"Archers take aim from the murk\": Draugr_Ranged 0.5+0.5, Draugr* 0+0.34 | " +
		"normal 25% \"A champion of the drowned\": Draugr_Elite 1+0, Wraith 0+0.34 | " +
		"heroic 100%: guard taken0.3 broken3 melee0.5 size0.2 | " +
		"heroic 25% \"A champion of the drowned\": Draugr_Elite* 1+0, Wraith 0+0.34, ward 0.5 break | " +
		"every 30s below 25%: Draugr 1+0 @2+, Draugr 1+0 @3+, Draugr 1+0 @4+";

	const string GoblinKingDefault =
		"80% \"The last of his people answer\": Goblin 0+1 | " +
		"60% \"Shamans draw upon their king\": GoblinShaman 0.5+0.5, Goblin 0+0.5 | " +
		"normal 40% \"A champion of the fallen cities\": GoblinBrute 1+0, GoblinBrute* 0+0.25 | " +
		"heroic 40% \"A champion of the fallen cities\": GoblinBrute:Ironhide* 1+0 @1, GoblinBrute:Ironhide** 1+0 @2+, GoblinBrute:Ironhide 0+0.25 | " +
		"heroic every 25s below 60%: strike fire r4 d2.5 dmg120 x2 | " +
		"heroic every 45s below 80%: boss cycle Emberborn Stormcalled 20 | " +
		"heroic 100%: shield immune refresh25 range35 by:GoblinShaman | " +
		"heroic every 40s: GoblinShaman 1+0 | " +
		"20% \"They will not bend or break\": Goblin 1+0, Goblin* 0+0.5, Goblin* 0+0.5, GoblinArcher 0+0.34 | " +
		"every 20s below 20%: Goblin 1+0 @2+, Goblin 1+0 @3+, Goblin 1+0 @4+";

	float tick;
	float reloadTimer;
	bool bound;
	DateTime configStamp;

	// Synced = the server's value is pushed to every client. Only what a client actually reads is synced; the rest is
	// the server's business and stays out of the traffic.
	ConfigEntry<T> config<T>(string group, string name, T value, ConfigDescription description, bool synced)
	{
		ConfigEntry<T> entry = Config.Bind(group, name, value, description);
		configSync.AddConfigEntry(entry).SynchronizedConfig = synced;
		return entry;
	}

	ConfigEntry<T> config<T>(string group, string name, T value, string description, bool synced) => config(group, name, value, new ConfigDescription(description), synced);

	void Awake()
	{
		Log = Logger;
		serverConfigLocked = config("1 - General", "Config is locked", Toggle.On, "If on, only admins can change the synced settings on a server.", true);
		configSync.AddLockingConfigEntry(serverConfigLocked);
		enabled = config("1 - General", "Enabled", Toggle.On, "Master switch, from the server. Off = no adds, no encounters, no damage changes, vanilla fireside wait.", true);
		Range = config("1 - General", "Engaged range (m)", 100f, "A player within this distance of the boss counts towards the wave sizes and sees the wave messages. With nobody in range a fight pauses.", false);
		AddsHunt = config("1 - General", "Adds hunt players", true, "On = adds come straight for the players, like raid creatures. Off = they behave like a creature that was already there.", false);
		RemoveAddsOnDeath = config("1 - General", "Remove adds when the boss dies", true, "On = adds still alive vanish when the boss dies (no loot from those). Off = they stay and must be killed.", false);
		SearchInterval = config("1 - General", "Boss search interval (s)", 5f, "How often the server looks for a newly spawned boss around the players. A fight that is already known is followed every second regardless, and that costs nothing.", false);

		CapBase = config("2 - Scaling", "Living adds cap, base", 2f, "Repeating rules (every NNs) stop while this many adds are alive: (base + per player x players) x 'More adds'. Threshold waves always arrive in full.", false);
		CapPerPlayer = config("2 - Scaling", "Living adds cap, per player", 1f, "See above.", false);
		RingMin = config("2 - Scaling", "Spawn ring, inner (m)", 12f, "Adds appear at a random point between this and the outer distance from the boss.", false);
		RingMax = config("2 - Scaling", "Spawn ring, outer (m)", 20f, "See above.", false);
		AddDamage = config("2 - Scaling", "Add damage (x)", 0.9f, new ConfigDescription("Damage the adds deal to players, as a share of what that creature normally deals. 1 = normal. It is written on each add when the server creates it and applied on the player's own game; bosses and wild creatures are not affected. It multiplies with any other mod's enemy-damage setting.", new AcceptableValueRange<float>(0.05f, 3f)), false);
		AddHealth = config("2 - Scaling", "Add health (x)", 1f, new ConfigDescription("Adds arrive with this share of their health, so they die sooner. 1 = full health. The only visible sign is a health bar that starts part empty.", new AcceptableValueRange<float>(0.1f, 1f)), false);
		MoreAdds = config("2 - Scaling", "More adds (x)", 1.3f, new ConfigDescription("Every wave count and the living-adds cap are multiplied by this and rounded down, so at 1.3 a wave of 4 becomes 5, 5 becomes 6, 8 becomes 10, and 1 to 3 stay as they are. 1 = the scripts exactly as written.", new AcceptableValueRange<float>(1f, 4f)), false);
		BossDamage = config("2 - Scaling", "Boss damage during a fight (x)", 1f, new ConfigDescription("Damage a boss deals to players while its fight is on, as a share of normal. 1 = normal. The server tells every player's game, which applies it. It multiplies with any other mod's enemy-damage setting.", new AcceptableValueRange<float>(0.05f, 5f)), false);

		WorldEncountersEnabled = config("4 - World encounters", "Enabled", true, "Encounters away from bosses: kill enough of something at a known place and the place answers. No message is shown.", false);
		WorldEncounterRules = config("4 - World encounters", "Encounters", "GoblinCamp2 80m: 10 Goblin,GoblinArcher,GoblinShaman in 600s -> GoblinBrute 1+0 @1-2, GoblinBrute* 1+0 @3+, cooldown 1800s",
			"Rules separated by | . A rule is   Location 80m: 10 PrefabA,PrefabB in 600s -> Prefab 1+0, cooldown 1800s\n" +
			"  Location    the world location's prefab name (GoblinCamp2 = a Fuling village). The log says how many exist in this world,\n" +
			"              and suggests similar names when there are none.\n" +
			"  80m         a kill counts when it happens within this distance of the location.\n" +
			"  10 ... in 600s   this many of the listed creatures must be killed inside this many seconds. Kills are reported by the\n" +
			"              game of whoever was simulating the creature, so they are exact; creatures this mod spawned never count.\n" +
			"  -> ...      what arrives, same format as boss waves: Prefab base+perPlayer, * = one star, @1-2 or @3+ = only for that many\n" +
			"              players (counted within 80 m of the last kill). It appears 14-24 m from the last kill, hunting, with its\n" +
			"              normal loot.\n" +
			"  cooldown    seconds before the same location can answer again (real time while the server runs).", false);

		HeroicEnabled = config("5 - Heroic fights", "Enabled", true, "A harder version of a boss fight that the players choose, and the only one that pays idols. To ask for it, hold Shift and press Use on the boss's altar before summoning; the altar's hover text shows that the challenge is set. (The older way still works: drop the boss's own trophy on the ground within 'Trophy within (m)' of the boss before anyone hurts it.) From the server.", true);
		HeroicCostsTrophy = config("5 - Heroic fights", "The challenge costs a trophy", false, "Off (default) = Shift + Use on the altar is free, so a group can take the heroic fight the first time it meets a boss. On = it takes one of the boss's OWN trophies from your inventory, which means a boss must be beaten normally once before it can be fought heroically. From the server.", true);
		HeroicAltarRadius = config("5 - Heroic fights", "Altar within (m)", 80f, new ConfigDescription("How far from the altar a boss may appear and still pick up the altar's challenge.", new AcceptableValueRange<float>(10f, 150f)), false);
		HeroicTrophyRadius = config("5 - Heroic fights", "Trophy within (m)", 15f, new ConfigDescription("How close to the boss the dropped trophy has to lie.", new AcceptableValueRange<float>(2f, 60f)), false);
		HeroicBossDamage = config("5 - Heroic fights", "Boss damage (x)", 1.2f, new ConfigDescription("Multiplies 'Boss damage during a fight' for a heroic fight: 1.2 = the boss hits 20% harder.", new AcceptableValueRange<float>(1f, 3f)), false);
		HeroicBossDamageByBoss = config("5 - Heroic fights", "Boss damage by boss", "", "Bosses that use their own number instead of 'Boss damage (x)', as Prefab=number pairs separated by commas, for example GoblinKing=1.35. Empty = every boss uses the number above.", false);
		HeroicBossHealth = config("5 - Heroic fights", "Boss health (x)", 1.4f, new ConfigDescription("A heroic boss has this much more health, on top of whatever it spawned with - another mod's boss health multiplies with this one. Raised when the challenge is taken up, at full health. 1 = unchanged.", new AcceptableValueRange<float>(1f, 5f)), false);
		HeroicMoreAdds = config("5 - Heroic fights", "More adds (x)", 1.25f, new ConfigDescription("Multiplies the wave sizes and the living-adds cap again, on top of 'More adds' (1.3 x 1.25 = about 1.6).", new AcceptableValueRange<float>(1f, 3f)), false);
		HeroicGuaranteedStar = config("5 - Heroic fights", "Every wave carries a star", true, "On = in a heroic fight every threshold wave has a starred add. Where the script already stars something in that wave, one of those adds gains a star (a one-star becomes a two-star) and the rest arrive as written. Where it stars nothing, the first add of the wave arrives one-star - for a wave that is a single heavy creature, that is the heavy creature. Trickles are not affected.", false);
		HeroicStarChance = config("5 - Heroic fights", "Extra star chance for plain adds", 0f, new ConfigDescription("On top of the guaranteed star: chance that any other add written without a star arrives with one, trickles included. 0 = none.", new AcceptableValueRange<float>(0f, 1f)), false);
		HeroicMessage = config("5 - Heroic fights", "Message", "The challenge is accepted", "Centre-screen message when the trophy is taken. Empty = none.", false);
		IdolBase = config("5 - Heroic fights", "Idols on the kill, base", -1f, "Idols dropped by a heroic kill = base + per player x players, rounded down, never below 0. Players = the most that were in range at once during the fight. Default -1 + 1: none solo, 1 for two players, 2 for three, 3 for four.", false);
		IdolPerPlayer = config("5 - Heroic fights", "Idols on the kill, per player", 1f, "See above.", false);
		IdolBattleShare = config("5 - Heroic fights", "Battle idol share", 0.5f, new ConfigDescription("Each idol is a Battle (weapon) idol with this chance, otherwise a Protection (armour) idol. 0.5 = even. Vanilla chests hold weapon idols half as often as protection ones (0.33).", new AcceptableValueRange<float>(0f, 1f)), false);
		IdolTiers = config("5 - Heroic fights", "Idol tier by boss", "Eikthyr=0, gd_king=1, Bonemass=2, Dragon=3, GoblinKing=4, SeekerQueen=5, Fader=6, FrozenKing_p3=7", "Which idol tier each boss pays: the item is Upgrader<tier>Weapon or Upgrader<tier>Armor. 0 Wooden, 1 Bronze, 2 Iron, 3 Silver, 4 Black Metal, and so on up. A boss not listed pays nothing.", false);

		RestingTimeAfterDeath = config("6 - Recovery", "Resting time after a death (x)", 0.25f, new ConfigDescription("Multiplier on the fireside wait before the Rested buff arrives, while you have recently died. 1 = vanilla. 0.25 = a quarter of the wait. 0 = Rested the moment you are resting (by a fire, under a roof, unnoticed by enemies). Food, the tombstone and the length of the Rested buff are untouched.", new AcceptableValueRange<float>(0f, 1f)), true);
		JustDiedWindow = config("6 - Recovery", "Counts as just died for (seconds)", 120f, new ConfigDescription("How long after a death the shorter wait applies. The clock is the game's own time-since-death; it starts when you die and keeps running while you respawn and walk back.", new AcceptableValueRange<float>(0f, 3600f)), true);

		ParryTaunt = config("7 - Targeting", "A parry holds the boss for (seconds)", 12f, new ConfigDescription("A player who parries a hit from a boss (a perfect block, any boss attack that can be blocked) is that boss's target for this long. The first two or three seconds pass while the boss is staggered, if it can be. Vanilla bosses simply turn to whoever is closest. 0 = off. From the server.", new AcceptableValueRange<float>(0f, 60f)), true);
		AddsAvoidShields = config("7 - Targeting", "Melee adds rush players without a shield", true, "On = the opening rush of this mod's melee adds goes to the back line: an add's first target is a player without a shield in hand, the one with the fewest adds sent at them so far. The rush ends when the add comes within 4 m of any player (it arrived, or someone stepped in its way) or after 20 s; from then on the add is vanilla, so whoever intercepted it keeps it while they stay the nearest. With a shield in every hand there is no rush. Wild creatures are never affected. From the server.", true);
		VanillaTargetedAdds = config("7 - Targeting", "Adds that never rush", "GoblinArcher, GoblinShaman, Draugr_Ranged, Greydwarf_Shaman, Hatchling", "Archers, casters and flyers: prefab names, comma separated. These are vanilla from the start: closest player. From the server.", true);

		BreakSize = config("8 - Mechanics", "Break meter size", 0.4f, new ConfigDescription("Bosses cannot be staggered in vanilla. With this, every scripted boss has a break meter, filled by playing the fight: parries, cleared waves, melee hits and damage of a type the boss is weak to. Its size is this fraction of the boss's max health. Full = the boss is BROKEN: it stops acting and takes more damage for a few seconds, then the meter needs more. 0.4 means one or two breaks in a fight. 0 = no break meter.", new AcceptableValueRange<float>(0f, 5f)), false);
		BreakDrain = config("8 - Mechanics", "Break meter drain per second", 0.002f, new ConfigDescription("As a fraction of the boss's max health. 0.002 empties a full 0.4 meter in a little over three minutes of nobody hitting.", new AcceptableValueRange<float>(0f, 1f)), false);
		BreakParry = config("8 - Mechanics", "Break meter, a parry adds", 0.06f, new ConfigDescription("As a fraction of the boss's max health.", new AcceptableValueRange<float>(0f, 1f)), false);
		BreakWaveChunk = config("8 - Mechanics", "Break meter, a cleared wave adds", 0.25f, new ConfigDescription("As a fraction of the meter. Killing every add of a threshold wave pays this once.", new AcceptableValueRange<float>(0f, 1f)), false);
		BreakHit = config("8 - Mechanics", "Break meter, melee hits count (x)", 0.25f, new ConfigDescription("Share of a melee hit's stagger value (its blunt, slash and pierce) that goes into the meter - melee of every kind, so getting up close is how a break is earned. Arrows, bolts and magic add nothing here; they feed the meter only through the boss's weaknesses.", new AcceptableValueRange<float>(0f, 2f)), false);
		BreakWeak = config("8 - Mechanics", "Break meter, weakness damage counts (x)", 1f, new ConfigDescription("Damage of a type the boss is weak to - its own weaknesses, or its current trait's - goes into the meter at this share, on top.", new AcceptableValueRange<float>(0f, 5f)), false);
		BreakInHeroic = config("8 - Mechanics", "Break meter in heroic fights", true, "Off = heroic fights have no break meter and no breaks (a guard, which only a break ends, is then skipped too). Normal fights are unaffected.", false);
		BreakSeconds = config("8 - Mechanics", "Break length (s)", 8f, new ConfigDescription("How long a broken boss stays down.", new AcceptableValueRange<float>(1f, 60f)), false);
		BreakTaken = config("8 - Mechanics", "Break damage taken (x)", 2f, new ConfigDescription("A broken boss takes this much more damage. Vanilla's own stagger bonus is x2.", new AcceptableValueRange<float>(1f, 5f)), false);
		BreakGrowth = config("8 - Mechanics", "Break meter growth (x)", 1.5f, new ConfigDescription("After each break the meter needs this much more.", new AcceptableValueRange<float>(1f, 5f)), false);
		Traits = config("8 - Mechanics", "Traits", "Ironhide = resist pierce, resist slash | Brittle = weak blunt | Stonebound = resist blunt | Rimebound = infuse frost 0.3, resist frost, weak fire | Emberborn = infuse fire 0.3, resist fire, weak frost | Stormcalled = infuse lightning 0.3, resist lightning | Blighted = infuse poison 0.3, resist poison | Frenzied = frenzy 1.5 | Fleet = swift 1.25 | Renewing = mend 0.5 | Wrathful = frenzy 1.5, swift 1.15", "Named sets of changes to a creature, separated by | . Give one to an add in a script with Prefab:Trait (GoblinBrute:Ironhide 1+0) - its name gains the trait as a prefix - or to the boss with the action 'boss Trait', 'boss Trait 20' (for 20 seconds) or 'boss cycle TraitA TraitB 30' (the next one each time the rule fires), so a boss can shift between them during a fight; 'boss none' clears it. The boss shows its trait under its name. Words: resist, veryresist, slightresist, immune, weak, veryweak, slightweak, then blunt, slash, pierce, fire, frost, lightning, poison or spirit (they replace the creature's own value for that damage type; the game colours the damage numbers as always, yellow weak, grey resistant); infuse ELEMENT 0.3 = its hits on players carry 30% extra damage of that element; frenzy 1.5 = its attacks come round 1.5x as fast; swift 1.25 = movement speed; mend 0.5 = heals 0.5% of max health a second; hardened 0.7 = takes 70% damage.", false);
		StrikeFx = config("8 - Mechanics", "Strike effects", "fire: > fx_goblinking_meteor_hit | frost: > fx_iceshard_hit+fx_fenring_icenova | lightning: > fx_eikthyr_stomp+fx_chainlightning_hit", "Vanilla effects of a ground strike, by element: TELL > IMPACT, several joined with +. The warning ring is always drawn and is the warning; a TELL plays when it appears, so keep it subtle (or empty) - anything that looks like an impact reads as the strike landing early. Played by each player's own game, the IMPACT exactly when the ring fills.", false);
		WardLabel = config("8 - Mechanics", "Ward label", "Warded", "Shown under the boss's name while a ward is up. Pushed to the players.", true);
		WardColour = config("8 - Mechanics", "Ward colour", "#9fd8ff", "Colour of the ward's status under the boss's name (and of its bubble, if shown). HTML colour. Pushed to the players.", true);
		WardBubble = config("8 - Mechanics", "Ward bubble", false, "On = a warded boss also wears the Fuling shaman's bubble, in the ward colour. Off (default) = the status under its name only. Pushed to the players.", true);

		HuntOrder = config("9 - Debug", "Start a hunt", "", "A boss's add waves in the open world, with no boss: write the boss's prefab name, optionally 'heroic', optionally a player's name (default: the first player connected), and save - e.g. 'GoblinKing heroic Anthony'. The waves arrive around that player one after another: the next when the last is dead, or after 'Hunt, next wave after (s)'; the trickles run in between. It ends after the last wave. 'stop' ends one early. The server clears this line once it has read it.", false);
		HuntBossChoice = config("9 - Debug", "Hunt: waves of", HuntBoss.Yagluth, new ConfigDescription("Whose waves the hunt button sends.", null, new ConfigurationManagerAttributes { Order = 3 }), false);
		HuntHeroic = config("9 - Debug", "Hunt: heroic", false, new ConfigDescription("Send the heroic waves.", null, new ConfigurationManagerAttributes { Order = 2 }), false);
		HuntButtons = config("9 - Debug", "Hunt", false, new ConfigDescription("Start a hunt on yourself with the waves chosen above, or stop the one running. For admins (on a dedicated server, the server's admin list).", null, new ConfigurationManagerAttributes { CustomDrawer = DrawHuntButtons, HideDefaultButton = true, Order = 1 }), false);
		HuntWaveGap = config("9 - Debug", "Hunt, next wave after (s)", 90f, new ConfigDescription("The longest a hunt waits for a wave to be killed before sending the next.", new AcceptableValueRange<float>(10f, 600f)), false);
		HuntMessage = config("9 - Debug", "Hunt message", "You are being hunted", "Centre-screen message when a hunt starts. Empty = none.", true);
		HuntEndMessage = config("9 - Debug", "Hunt over message", "The hunt is over", "Centre-screen message when a hunt's last wave is dead. Empty = none.", true);
		ForcePlayers = config("9 - Debug", "Pretend this many players", 0, "0 = count real players. Anything else sizes every boss wave as if that many were fighting, so one person can see a four-player fight. Someone still has to be in range.", false);

		new Harmony(GUID).PatchAll();
		configStamp = Stamp();
	}

	DateTime Stamp()
	{
		try { return File.GetLastWriteTimeUtc(Config.ConfigFilePath); } catch { return DateTime.MinValue; }
	}

	internal static string ScriptFor(string bossPrefab) => Scripts.TryGetValue(bossPrefab, out ConfigEntry<string> e) ? e.Value : "";

	// "Ironhide = resist pierce, resist slash | Brittle = weak blunt" -> the resistances of one trait in the game's own
	// words ("pierce=Resistant,slash=Resistant"), which is what travels on a creature's ZDO. Empty = no such trait.
	// The whole mode string of one trait (Modes.cs): "name=Frostbound;res=frost=Resistant,fire=Weak;infuse=frost:0.3".
	internal static string TraitMode(string trait)
	{
		if (string.IsNullOrWhiteSpace(trait)) return "";
		foreach (string entry in (Traits.Value ?? "").Split('|'))
		{
			int eq = entry.IndexOf('=');
			if (eq < 0 || !entry.Substring(0, eq).Trim().Equals(trait.Trim(), StringComparison.OrdinalIgnoreCase)) continue;
			var res = new List<string>();
			var rest = new List<string>();
			foreach (string item in entry.Substring(eq + 1).Split(','))
			{
				string[] w = item.Trim().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
				if (w.Length < 2) continue;
				string word = w[0].ToLowerInvariant();
				string mod = word switch
				{
					"resist" => "Resistant", "veryresist" => "VeryResistant", "slightresist" => "SlightlyResistant", "immune" => "Immune",
					"weak" => "Weak", "veryweak" => "VeryWeak", "slightweak" => "SlightlyWeak", "normal" => "Normal", _ => null,
				};
				if (mod != null) { res.Add(w[1].ToLowerInvariant() + "=" + mod); continue; }
				switch (word)
				{
					case "frenzy": rest.Add("aggro=" + w[1]); break;
					case "swift": rest.Add("quick=" + w[1]); break;
					case "hardened": rest.Add("taken=" + w[1]); break;
					// written as per cent of max health a second
					case "mend": if (float.TryParse(w[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float pct)) rest.Add("regen=" + (pct / 100f).ToString(System.Globalization.CultureInfo.InvariantCulture)); break;
					case "infuse": rest.Add("infuse=" + w[1].ToLowerInvariant() + ":" + (w.Length > 2 ? w[2] : "0.3")); break;
				}
			}
			string mode = "name=" + entry.Substring(0, eq).Trim();
			if (res.Count > 0) mode += ";res=" + string.Join(",", res);
			if (rest.Count > 0) mode += ";" + string.Join(";", rest);
			return mode;
		}
		return "";
	}

	// "fire: tell > hit | frost: tell > hit": the vanilla effects of a ground strike, by element. Several joined with +.
	internal static void StrikeEffects(string element, out string tell, out string hit)
	{
		tell = hit = "";
		foreach (string entry in (StrikeFx.Value ?? "").Split('|'))
		{
			int colon = entry.IndexOf(':');
			if (colon < 0 || !entry.Substring(0, colon).Trim().Equals((element ?? "").Trim(), StringComparison.OrdinalIgnoreCase)) continue;
			string[] sides = entry.Substring(colon + 1).Split('>');
			tell = sides[0].Trim();
			hit = sides.Length > 1 ? sides[1].Trim() : "";
			return;
		}
	}

	// The heroic damage multiplier for a boss: its own entry in 'Boss damage by boss', else the general one.
	internal static float HeroicBossDamageFor(string bossPrefab)
	{
		foreach (string pair in (HeroicBossDamageByBoss.Value ?? "").Split(','))
		{
			string[] kv = pair.Split('=');
			if (kv.Length != 2 || !string.Equals(kv[0].Trim(), bossPrefab, StringComparison.OrdinalIgnoreCase)) continue;
			if (float.TryParse(kv[1].Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float mult) && mult > 0f) return Math.Min(mult, 3f);
		}
		return HeroicBossDamage.Value;
	}

	// "Bonemass=2, Dragon=3" -> tier for a boss prefab, or -1.
	internal static int IdolTierFor(string bossPrefab)
	{
		foreach (string pair in (IdolTiers.Value ?? "").Split(','))
		{
			string[] kv = pair.Split('=');
			if (kv.Length != 2 || !string.Equals(kv[0].Trim(), bossPrefab, StringComparison.OrdinalIgnoreCase)) continue;
			return int.TryParse(kv[1].Trim(), out int tier) && tier >= 0 ? tier : -1;
		}
		return -1;
	}

	// Boss prefabs are only known once the game has loaded them, so their config entries are bound late - and only on
	// the server, which is the only side that reads them.
	void BindScripts()
	{
		Director.CollectBossPrefabs();
		var names = new List<string>(Director.BossNames);
		names.Sort(StringComparer.Ordinal);
		bool first = true;
		foreach (string boss in names)
		{
			string def = boss == "Eikthyr" ? EikthyrDefault : boss == "Dragon" ? DragonDefault : boss == "GoblinKing" ? GoblinKingDefault : boss == "gd_king" ? ElderDefault : boss == "Bonemass" ? BonemassDefault : "";
			string help = (boss == "Dragon" ? "Moder. " : boss == "GoblinKing" ? "Yagluth. " : boss == "gd_king" ? "The Elder. " : "") + (first ? ScriptHelp : "Same format as the first entry in this section.");
			Scripts[boss] = Config.Bind("3 - Boss fights", boss, def, help);
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
		Log.LogInfo($"RaidBoss {Version} directing on this server: {names.Count} boss prefabs ({string.Join(", ", names)}). Adds deal x{AddDamage.Value:0.##}, bosses x{BossDamage.Value:0.##} in a fight, waves x{MoreAdds.Value:0.##}.");
		configStamp = Stamp();
	}

	void Update()
	{
		if (ZNet.instance == null || ZNetScene.instance == null || ZDOMan.instance == null || ZoneSystem.instance == null || WorldGenerator.instance == null)
		{
			if (bound) { bound = false; Scripts.Clear(); Director.Reset(); }
			return;
		}
		Mechanics.ClientTick(Time.deltaTime);
		// Everything below is the director, and the director runs on the server only.
		if (!ZNet.instance.IsServer()) return;
		Net.Register();
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
		if (!IsOn) { if (Director.ActiveFights > 0) Director.Reset(); return; }
		try { WorldEncounters.Tick(); } catch (Exception e) { Log.LogError(e); }
		try { Director.Tick(step); }
		catch (Exception e) { Log.LogError(e); }
		try { Hunts.Tick(step); } catch (Exception e) { Log.LogError(e); }
	}
}
