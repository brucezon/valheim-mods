using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using ServerSync;
using SkillManager;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace BruceQoL;

// Server-synced quality-of-life tuning for a private server. Every section is independent and
// defaults to vanilla behaviour except where noted, so the config file is the whole design.
[BepInPlugin(ModGUID, ModName, ModVersion)]
public class BruceQoLPlugin : BaseUnityPlugin
{
	private const string ModName = "BruceQoL";
	private const string ModVersion = "1.19.2";
	private const string ModGUID = "bruceirons.BruceQoL";
	// Oldest client/server version still let in. RULE (host, 18 Sep 2026): this is the PREVIOUS release unless a
	// release is truly breaking (changes data both sides must agree on, or makes an old client misbehave rather
	// than merely lack the new feature). Pinning it to ModVersion, as every release up to 1.16.0 did, kicked
	// every player who had not pulled yet, several times a day. ServerSync only warns about config keys the
	// other side does not know. 1.17.0: new section-19 options whose defaults reproduce 1.16.0, so 1.16.0 stays in.
	private const string MinimumVersion = "1.16.0";

	private static readonly ConfigSync configSync = new(ModName) { DisplayName = ModName, CurrentVersion = ModVersion, MinimumRequiredVersion = MinimumVersion, ModRequired = true };
	internal static BruceQoLPlugin mod;

	internal enum Toggle
	{
		On = 1,
		Off = 0,
	}

	// 1 - General
	private static ConfigEntry<Toggle> serverConfigLocked;

	// 2 - Structures
	private static ConfigEntry<float> creatureDamageMult;
	private static ConfigEntry<float> bossDamageMult;
	private static ConfigEntry<float> playerOtherDamageMult;
	private static ConfigEntry<float> environmentalHitMult;
	private static ConfigEntry<float> naturalWearMult;
	private static ConfigEntry<Toggle> weatherDamage;
	private static ConfigEntry<Toggle> wearThrottle;
	private static ConfigEntry<float> wearInterval;
	private static readonly Dictionary<WearNTear.MaterialType, ConfigEntry<float>> materialHealth = new();
	private static readonly Dictionary<WearNTear.MaterialType, ConfigEntry<float>> materialDamageTaken = new();

	// 7 - Hauling
	private static Skill hauling;
	private static bool haulingReady;

	// 1 - General
	private static ConfigEntry<Toggle> ignoreCheatedKeys;
	private static ConfigEntry<float> haulingBonus;
	private static ConfigEntry<float> haulingThreshold;
	private static ConfigEntry<float> haulingInterval;

	// 3 - Player
	private static ConfigEntry<float> dodgeStaminaMult;
	private static ConfigEntry<float> runStaminaMult;
	private static ConfigEntry<float> jumpStaminaMult;
	private static ConfigEntry<float> sneakStaminaMult;
	private static ConfigEntry<float> swimStaminaMult;
	private static ConfigEntry<float> encumberedStaminaMult;
	private static ConfigEntry<float> regenDelayMult;
	private static ConfigEntry<float> pickupRangeMult;
	private static ConfigEntry<Toggle> areaRepair;
	private static ConfigEntry<float> areaRepairRadius;

	// 4 - Items
	private static ConfigEntry<float> stackMult;

	// 5 - Fires
	private static ConfigEntry<Toggle> infiniteTorches;
	private static ConfigEntry<Toggle> infiniteFires;
	private static ConfigEntry<Toggle> infiniteBraziers;

	// 6 - Skills
	private static ConfigEntry<Toggle> peakCatchUp;
	private static ConfigEntry<float> peakBonus;
	private static ConfigEntry<Toggle> weaponCross;
	private static ConfigEntry<float> weaponCrossBonus;
	private static ConfigEntry<string> weaponCrossSkills;
	private static ConfigEntry<float> swimXpMult;
	private static ConfigEntry<Toggle> swimNoLoss;
	private static ConfigEntry<Toggle> sneakBackstab;
	private static ConfigEntry<float> sneakBackstabBonus;
	private static ConfigEntry<float> sneakBackstabXp;

	// 8 - Skill experience (OdinsQOL-style percentage modifiers; 0 = vanilla, 50 = +50%, -50 = half)
	private static ConfigEntry<Toggle> skillXpModifiers;
	private static ConfigEntry<float> allSkillsGain;
	private static ConfigEntry<float> deathPenaltyModifier;
	private static readonly Dictionary<Skills.SkillType, ConfigEntry<float>> skillGain = new();

	private static float Pct(float modifier) => Math.Max(0f, (100f + modifier) / 100f);

	// 9 - Multiplayer scaling (vanilla: enemies gain +30% effective health and +4% damage per extra player
	// within 100 m, capped at 5 players; hardcoded on Game, no world key)
	private static ConfigEntry<Toggle> mpScaling;
	private static ConfigEntry<float> mpHealthPerPlayer;
	private static ConfigEntry<float> mpDamagePerPlayer;
	private static ConfigEntry<float> mpRange;
	private static ConfigEntry<int> mpMaxPlayers;

	// 10 - Containers
	private static ConfigEntry<string> containerSizes;
	private static readonly Dictionary<string, (int w, int h)> containerSizeMap = new(StringComparer.OrdinalIgnoreCase);
	private static ConfigEntry<Toggle> chestHover;
	private static ConfigEntry<int> chestHoverMax;
	private static ConfigEntry<Toggle> fermenterHover;

	// 11 - Crafting stations
	private static ConfigEntry<float> stationRange;
	private static ConfigEntry<Toggle> stationsNeedRoof;

	// 7 - Hauling: carts
	private static ConfigEntry<float> cartLoadAt100;
	private static ConfigEntry<float> cartBreakAt100;
	private static ConfigEntry<Toggle> cartTraining;
	private static ConfigEntry<float> cartTrainingLoad;
	private static ConfigEntry<float> cartTrainingXp;

	// 14 - Raids
	private static ConfigEntry<Toggle> raidsEnabled;
	private static ConfigEntry<float> raidIntervalMult;
	private static ConfigEntry<float> raidChanceMult;
	private static ConfigEntry<float> raidDurationMult;
	private static ConfigEntry<Toggle> raidsAnywhere;
	private static ConfigEntry<string> disabledRaids;

	// 15 - Tames
	private static ConfigEntry<string> commandableTames;

	// 13 - Combat
	private static ConfigEntry<float> enemyDamageToPlayers;
	private static ConfigEntry<float> bossDamageToPlayers;
	private static ConfigEntry<float> enemyDamageToTames;
	private static ConfigEntry<float> playerDamageToEnemies;
	private static ConfigEntry<float> enemyHealthMult;
	private static ConfigEntry<float> bossHealthMult;

	// 12 - Beehives
	private static ConfigEntry<Toggle> beehiveTweaks;
	private static ConfigEntry<int> honeyPerHive;
	private static ConfigEntry<float> secondsPerHoney;

	// 3 - Player (added 1.3.0)
	private static ConfigEntry<Toggle> reequipAfterSwim;

	private static void ParseContainerSizes()
	{
		containerSizeMap.Clear();
		foreach (string part in (containerSizes.Value ?? "").Split(',', ';'))
		{
			string[] kv = part.Split(':');
			if (kv.Length != 2) continue;
			string[] wh = kv[1].Trim().ToLowerInvariant().Split('x');
			if (wh.Length == 2 && int.TryParse(wh[0], out int w) && int.TryParse(wh[1], out int h) && w > 0 && h > 0 && w <= 16 && h <= 12)
			{
				containerSizeMap[kv[0].Trim()] = (w, h);
			}
			else
			{
				mod.Logger.LogWarning($"Container sizes: ignoring '{part.Trim()}' (want prefab:WxH, e.g. piece_chest_wood:6x3)");
			}
		}
	}

	private ConfigEntry<T> config<T>(string group, string name, T value, ConfigDescription description, bool synchronizedSetting = true)
	{
		ConfigEntry<T> configEntry = Config.Bind(group, name, value, description);
		SyncedConfigEntry<T> syncedConfigEntry = configSync.AddConfigEntry(configEntry);
		syncedConfigEntry.SynchronizedConfig = synchronizedSetting;
		return configEntry;
	}

	private ConfigEntry<T> config<T>(string group, string name, T value, string description, bool synchronizedSetting = true) => config(group, name, value, new ConfigDescription(description), synchronizedSetting);

	private void Awake()
	{
		mod = this;

		serverConfigLocked = config("1 - General", "Config is locked", Toggle.On, "If on, only admins can change the configuration on a server.");
		configSync.AddLockingConfigEntry(serverConfigLocked);
		ignoreCheatedKeys = config("1 - General", "Ignore cheated world keys", Toggle.On, "World keys set from the server launch line (for example 'movestaminarate 85') use values the in-game modifier GUI cannot pick, so vanilla flags the world as cheated and disables achievements for everyone. On = report the world as clean. Independent of Unshamed, which does the same on the client only.");

		creatureDamageMult = config("2 - Structures", "Creature damage to buildings", 0.5f, "Multiplier on damage that creatures (trolls, greydwarves, raids...) deal to player-built pieces. 1 = vanilla.");
		bossDamageMult = config("2 - Structures", "Boss damage to buildings", 0.75f, "Multiplier on damage that bosses deal to player-built pieces. 1 = vanilla.");
		playerOtherDamageMult = config("2 - Structures", "Player damage to other players buildings", 1f, "Multiplier on damage a player deals to pieces built by someone else. The builder always deals full damage to their own pieces. 1 = vanilla.");
		environmentalHitMult = config("2 - Structures", "Environmental hit damage", 1f, "Multiplier on hits with no attacker (fire spread, ashlands ocean, falling). 1 = vanilla.");
		weatherDamage = config("2 - Structures", "Weather damage", Toggle.On, "Off = unsheltered pieces never take rain / water wear, and every client skips the per-piece roof raycasts during rain (CPU saving on big bases; Ashlands and Deep North keep them for ash and snow). On = vanilla behaviour scaled by Natural wear below.");
		wearThrottle = config("2 - Structures", "Wear check throttle", Toggle.On, "For pieces you own that are older than 30 s, at full health, dry, and outside the Ashlands and Deep North, run the wear tick (which includes the support physics check) at most once per Wear check interval instead of on every updater pass. Big CPU saving on large bases. A piece that loses its foundation still collapses, at most one interval later.");
		wearInterval = config("2 - Structures", "Wear check interval (seconds)", 10f, "Seconds between wear ticks for throttled pieces.");
		naturalWearMult = config("2 - Structures", "Natural wear", 1f, "Multiplier on weather / water wear that ticks on unsheltered pieces. 1 = vanilla, 0 = none.");
		foreach (WearNTear.MaterialType material in Enum.GetValues(typeof(WearNTear.MaterialType)))
		{
			float def = material == WearNTear.MaterialType.Stone ? 1.5f : 1f;
			materialHealth[material] = config("2 - Structures", material + " health", def, "Max health multiplier for " + material + " pieces. Applied when a piece is created or loaded; existing pieces show as damaged after raising it until repaired.");
		}
		foreach (WearNTear.MaterialType material in Enum.GetValues(typeof(WearNTear.MaterialType)))
		{
			materialDamageTaken[material] = config("2 - Structures", material + " damage taken", 1f, "Multiplier on every hit " + material + " pieces take, on top of the attacker multipliers above. 0.5 = twice as tough. 1 = vanilla.");
		}

		haulingBonus = config("7 - Hauling", "Extra carry weight at level 100 (weight units)", 300f, "Flat carry weight added at Hauling 100, in the game's weight units (vanilla base limit is 300). Scales linearly with level: at 50 you get half. This is the only knob; the SkillManager 'Skill effect factor' below is ignored.");
		haulingThreshold = config("7 - Hauling", "Training load", 0.9f, "Fraction of your carry limit you must be carrying while moving to gain Hauling experience.");
		haulingInterval = config("7 - Hauling", "Training interval (seconds)", 1f, "Seconds of loaded walking per experience tick.");
		// The Hauling skill is created AFTER every config entry is bound (see EnsureHaulingSkill): on a 1.0
		// client the skill constructor touches Localization.instance, which calls into Steam before Steam is
		// initialised and throws. Creating it last means a failure can no longer abort the config binding.

		dodgeStaminaMult = config("3 - Player", "Dodge stamina", 1f, "Multiplier on dodge stamina cost. 1 = vanilla.");
		runStaminaMult = config("3 - Player", "Run stamina drain", 1f, "Multiplier on running stamina drain. 1 = vanilla.");
		jumpStaminaMult = config("3 - Player", "Jump stamina", 1f, "Multiplier on jump stamina cost. 1 = vanilla.");
		sneakStaminaMult = config("3 - Player", "Sneak stamina drain", 1f, "Multiplier on sneaking stamina drain. 1 = vanilla.");
		swimStaminaMult = config("3 - Player", "Swim stamina drain", 1f, "Multiplier on swimming stamina drain. 1 = vanilla.");
		encumberedStaminaMult = config("3 - Player", "Encumbered stamina drain", 1f, "Multiplier on stamina drain while over the weight limit. 1 = vanilla.");
		regenDelayMult = config("3 - Player", "Stamina regen delay", 1f, "Multiplier on the pause before stamina starts regenerating after use. 1 = vanilla (1 s).");
		pickupRangeMult = config("3 - Player", "Auto pickup range", 1f, "Multiplier on the auto pickup radius. 1 = vanilla (2 m).");
		areaRepair = config("3 - Player", "Area repair", Toggle.On, "Repairing a piece with the hammer also repairs every damaged piece within the radius below.");
		areaRepairRadius = config("3 - Player", "Area repair radius (metres)", 15f, "Radius in metres for area repair.");
		reequipAfterSwim = config("3 - Player", "Re-equip after swimming", Toggle.On, "Weapons and shields the game sheathes while you swim come back out on their own when you leave the water (vanilla waits for the toggle key).");

		stackMult = config("4 - Items", "Stack size multiplier", 1f, "Multiplier on the max stack size of every stackable item (rounded). Items that stack to 1 are untouched. 1 = vanilla.");

		infiniteTorches = config("5 - Fires", "Infinite torches", Toggle.On, "Placed torches (wall, standing, hanging, coloured) never run out of fuel. Infinite fires also skip vanilla's 2-second fuel bookkeeping, which otherwise sends a network update per fire every 2 s. If you turn this off later, those fires burn their stored fuel down on the next tick.");
		infiniteFires = config("5 - Fires", "Infinite fires", Toggle.Off, "Campfires, hearths and bonfires never run out of fuel.");
		infiniteBraziers = config("5 - Fires", "Infinite braziers", Toggle.Off, "Braziers never run out of fuel.");

		peakCatchUp = config("6 - Skills", "Peak catch-up", Toggle.On, "Remembers the highest level each skill has reached. While a skill is below its peak (after a death), it gains bonus experience until it is back.");
		peakBonus = config("6 - Skills", "Peak catch-up bonus", 2f, "Experience multiplier while a skill is below its peak.");
		weaponCross = config("6 - Skills", "Weapon cross-training", Toggle.On, "A weapon skill gains bonus experience while any other weapon skill is higher, so switching weapon types is less painful.");
		weaponCrossBonus = config("6 - Skills", "Weapon cross-training bonus", 2f, "Experience multiplier for a weapon skill that is below your best weapon skill.");
		weaponCrossSkills = config("6 - Skills", "Weapon cross-training skills", "Swords, Knives, Clubs, Polearms, Spears, Axes, Unarmed", "Which skills count as weapon skills for cross-training, comma-separated. Default = melee only; add Bows, Crossbows to include ranged.");
		weaponCrossSkills.SettingChanged += (_, _) => ParseWeaponSkills();
		ParseWeaponSkills();
		swimXpMult = config("6 - Skills", "Swim experience", 2f, "Multiplier on swimming experience. 1 = vanilla.");
		swimNoLoss = config("6 - Skills", "Swim keeps level on death", Toggle.On, "Swimming is not lowered by the death penalty.");
		sneakBackstab = config("6 - Skills", "Sneak boosts backstab", Toggle.On, "Backstab damage scales with your Sneak skill, and a backstab gives Sneak experience.");
		sneakBackstabBonus = config("6 - Skills", "Sneak backstab bonus", 0.5f, "Extra backstab multiplier at Sneak 100 (0.5 = +50%, scaled linearly with skill).");
		sneakBackstabXp = config("6 - Skills", "Sneak experience per backstab", 3f, "Sneak experience granted on a successful backstab (a normal sneak tick is 1).");

		foreach (ConfigEntry<float> e in new[] { dodgeStaminaMult, runStaminaMult, jumpStaminaMult, sneakStaminaMult, swimStaminaMult, encumberedStaminaMult, regenDelayMult, pickupRangeMult })
		{
			e.SettingChanged += (_, _) => ReapplyAllPlayers();
		}
		stackMult.SettingChanged += (_, _) => ApplyStacks();

		skillXpModifiers = config("8 - Skill experience", "Skill experience modifiers", Toggle.On, "Apply the percentage modifiers below to skill experience gain. All at 0 = vanilla. Stacks with the Skills section (peak catch-up, cross-training, swim) and with the world's skill gain rate.");
		allSkillsGain = config("8 - Skill experience", "All skills gain %", 0f, "Modifier on experience gain for every skill. 50 = +50%, -50 = half. 0 = vanilla.");
		foreach (Skills.SkillType type in Enum.GetValues(typeof(Skills.SkillType)))
		{
			if (type == Skills.SkillType.None || type == Skills.SkillType.All) continue;
			skillGain[type] = config("8 - Skill experience", type + " gain %", 0f, $"Modifier on {type} experience gain, on top of All skills gain %. 50 = +50%, -50 = half. 0 = vanilla.");
		}
		deathPenaltyModifier = config("8 - Skill experience", "Death penalty %", 0f, "Modifier on the skill loss at death, on top of the world's skillreductionrate key. 50 = lose 50% more, -50 = lose half, -100 = lose nothing. 0 = vanilla.");

		mpScaling = config("9 - Multiplayer scaling", "Multiplayer scaling", Toggle.On, "Apply the values below to vanilla's nearby-player difficulty scaling. Off = leave vanilla's hardcoded 0.3 / 0.04 / 100 m / 5 untouched.");
		mpHealthPerPlayer = config("9 - Multiplayer scaling", "Health per extra player", 0.3f, "Extra effective enemy health per additional nearby player, as a fraction. Vanilla 0.3 = +30% each (5 players = 2.2x). 0 disables health scaling.");
		mpDamagePerPlayer = config("9 - Multiplayer scaling", "Damage per extra player", 0.04f, "Extra enemy damage to players per additional nearby player, as a fraction. Vanilla 0.04 = +4% each. 0 disables damage scaling.");
		mpRange = config("9 - Multiplayer scaling", "Scale range (metres)", 100f, "Metres around a creature within which players are counted. Vanilla 100.");
		mpMaxPlayers = config("9 - Multiplayer scaling", "Max players counted", 5, "Cap on the player count used for scaling. Vanilla 5.");
		foreach (ConfigEntry<float> e in new[] { mpHealthPerPlayer, mpDamagePerPlayer, mpRange })
		{
			e.SettingChanged += (_, _) => ApplyDifficultyScaling();
		}
		mpMaxPlayers.SettingChanged += (_, _) => ApplyDifficultyScaling();
		mpScaling.SettingChanged += (_, _) => ApplyDifficultyScaling();

		containerSizes = config("10 - Containers", "Container sizes", "", "Comma-separated prefab:WIDTHxHEIGHT overrides. Empty = vanilla. Vanilla sizes: piece_chest_wood:5x2, piece_chest:6x4 (reinforced), piece_chest_blackmetal:8x4, piece_chest_private:3x2, Cart:6x3, Karve:2x2, VikingShip:6x3. Example: piece_chest_wood:6x3, piece_chest:8x4. Decide before the world starts: shrinking later strands items in the removed slots.");
		chestHover = config("10 - Containers", "Chest contents on hover", Toggle.On, "Show what a chest holds in its hover text, most numerous first.");
		chestHoverMax = config("10 - Containers", "Hover max entries", 12, "Maximum item lines in the chest hover text.");
		fermenterHover = config("10 - Containers", "Fermenter progress on hover", Toggle.On, "Show minutes remaining while a fermenter is fermenting.");
		containerSizes.SettingChanged += (_, _) => ParseContainerSizes();
		ParseContainerSizes();

		stationRange = config("11 - Crafting stations", "Station range (metres)", 30f, "Radius in metres around every crafting station (workbench, forge, stonecutter...) within which you can place and repair pieces that need it. Vanilla is 20. Does not change the monster-free area, which is a separate effect. Extensions still add their range on top.");
		stationsNeedRoof = config("11 - Crafting stations", "Stations need roof", Toggle.Off, "Off = crafting stations work without a roof. On = vanilla.");

		beehiveTweaks = config("12 - Beehives", "Beehive tweaks", Toggle.Off, "Apply the two values below to every beehive. Off = vanilla.");
		honeyPerHive = config("12 - Beehives", "Honey per hive", 4, "Maximum honey a hive stores. Vanilla 4.");
		secondsPerHoney = config("12 - Beehives", "Honey time (seconds)", 1200f, "Seconds to produce one honey. Vanilla 1200 (20 min).");

		enemyDamageToPlayers = config("13 - Combat", "Enemy damage to players", 1f, "Multiplier on damage creatures and bosses deal to players. Stacks with the Combat world slider. 1 = as the world is set.");
		bossDamageToPlayers = config("13 - Combat", "Boss damage to players", 0f, new ConfigDescription("Multiplier on damage BOSSES deal to players, used instead of 'Enemy damage to players' for them. 0 = no separate number: bosses follow 'Enemy damage to players', as they did before 1.19.2. Stacks with the Combat world slider. The server-side BossDirector mod can set this and the entry above for the length of a boss fight (adds softer, boss at full strength) and puts them back afterwards.", new AcceptableValueRange<float>(0f, 5f)));
		BlockCompensation.ParryCompensation = config("13 - Combat", "Parry difficulty compensation", 0f,
			new ConfigDescription("0 = vanilla: a timed block is judged against Combat-scaled enemy damage (Hard = 1.5x; the stagger bar fills 2.25x faster than Normal). 1 = parry timing, stagger fill and stamina cost are exactly what they are on Combat Normal; damage that still gets through is scaled as usual. Values between are partial. No effect when Combat is Normal.",
				new AcceptableValueRange<float>(0f, 1f)));
		BlockCompensation.HeldBlockCompensation = config("13 - Combat", "Held block difficulty compensation", 0f,
			new ConfigDescription("Same as above for held (non-timed) blocks. Keep lower than the parry value if held blocks should stay punishing.",
				new AcceptableValueRange<float>(0f, 1f)));
		enemyDamageToTames = config("13 - Combat", "Enemy damage to tames", 1f, "Multiplier on damage creatures deal to tamed animals. 1 = vanilla.");
		playerDamageToEnemies = config("13 - Combat", "Player damage to enemies", 1f, "Multiplier on damage players deal to creatures and bosses. Stacks with the Combat world slider. 1 = as the world is set.");
		enemyHealthMult = config("13 - Combat", "Enemy health", 1f, "Max health multiplier for creatures, applied when they spawn. 1 = vanilla.");
		bossHealthMult = config("13 - Combat", "Boss health", 1f, "Max health multiplier for bosses, applied when they spawn. 1 = vanilla.");
		BowDraw.Enabled = config("13 - Combat", "Bow draw tuning", Toggle.Off, "Apply the two bow draw multipliers below. Off = vanilla: most bows take 2.5 s to full draw at Bows 0, 1.5 s at Bows 50 and 0.5 s at Bows 100.");
		BowDraw.AtSkill0 = config("13 - Combat", "Bow draw time at skill 0 (x)", 1f, "Multiplier on the bow's draw time at Bows 0. Vanilla 1 = 2.5 s for most bows. Examples: 0.8 = 2 s, 0.6 = 1.5 s (easier early game), 1.2 = 3 s.");
		BowDraw.AtSkill100 = config("13 - Combat", "Bow draw time at skill 100 (x)", 0.2f, "Multiplier on the bow's draw time at Bows 100. Vanilla 0.2 = 0.5 s for most bows. Examples: 0.3 = 0.75 s, 0.4 = 1 s (tones down a maxed archer), 0.6 = 1.5 s. Levels between follow a straight line from the skill-0 value: with 1 and 0.4, Bows 50 draws in 1.75 s (vanilla 1.5 s).");
		// Blocking skill levers. Each one is a multiplier or an amount, never a percent; every one at its default = vanilla.
		Blocking.BlockPowerEnabled = config("13 - Combat", "Block power by skill", Toggle.Off,
			"Master switch for the two block power entries below. Off = vanilla: a shield blocks its listed block armour +50% at Blocking 100 (+25% at 50, +0% at 0). On = use the multiplier and start level below instead.");
		Blocking.BlockPowerAt100 = config("13 - Combat", "Block power at skill 100 (x)", 2f,
			new ConfigDescription("The multiplier a shield's block armour reaches at Blocking 100. 1 = the skill adds nothing. 1.5 = vanilla. 2 = a maxed blocker blocks double the listed armour; a level-50 blocker gets half the bonus (1.5x). 3 = triple at 100. A 40-armour bronze buckler at Blocking 60 with 2 here blocks 40 x (1 + 0.6 x 1) = 64. The item tooltip shows the result.",
				new AcceptableValueRange<float>(1f, 4f)));
		Blocking.BlockPowerFromLevel = config("13 - Combat", "Block power bonus from level", 0,
			new ConfigDescription("Blocking level at which the multiplier above starts applying; below it the vanilla curve is used. 0 = from the first level. 25 = vanilla until Blocking 25, then the multiplier kicks in (a milestone you can feel). Values between 0 and 100.",
				new AcceptableValueRange<int>(0, 100)));
		Blocking.ParryXpBonus = config("13 - Combat", "Extra Blocking XP per parry", 0f,
			new ConfigDescription("Extra Blocking skill experience added to every timed (perfect) block, in the same units vanilla uses. Vanilla gives 2 per parry and 1 per held block, whether or not the block holds. 0 = vanilla. 2 = a parry is worth 4, so parries level Blocking twice as fast. 6 = four times as fast.",
				new AcceptableValueRange<float>(0f, 10f)));
		Blocking.HeldBlockXpBonus = config("13 - Combat", "Extra Blocking XP per held block", 0f,
			new ConfigDescription("Same for held (non-timed) blocks, which vanilla rewards with 1. 0 = vanilla. 1 = held blocks level twice as fast. Keep this below the parry value so parrying stays the faster way up.",
				new AcceptableValueRange<float>(0f, 10f)));
		Blocking.StaminaRefundAt100 = config("13 - Combat", "Block stamina refund at skill 100", 0f,
			new ConfigDescription("Fraction of the stamina a held block actually cost that is handed back straight after the block, at Blocking 100; lower levels get proportionally less. 0 = nothing back (vanilla). 0.5 = a maxed blocker gets half back, a level-50 blocker a quarter, a level-10 blocker 5%. 1 = held blocks are free at Blocking 100. Timed blocks are only included if the parries switch below is On.",
				new AcceptableValueRange<float>(0f, 1f)));
		Blocking.StaminaRefundFromLevel = config("13 - Combat", "Block stamina refund from level", 0,
			new ConfigDescription("Blocking level at which refunds start; below it nothing is refunded. 0 = from the first level. 40 = nothing until Blocking 40, then the refund at that level's fraction.",
				new AcceptableValueRange<int>(0, 100)));
		Blocking.StaminaRefundParries = config("13 - Combat", "Block stamina refund on parries", Toggle.Off,
			"Off = the refund applies to held blocks only. On = timed (perfect) blocks are refunded the same way. Parries already cost a flat amount in vanilla and some 1.0 shields give it back themselves, so Off is the safer choice.");
		Blocking.EquipTimeAt100 = config("13 - Combat", "Equip time at skill 100 (x)", 1f,
			new ConfigDescription("Multiplier on how long a weapon or shield takes to equip and unequip when its own skill is at 100 (Swords for a sword, Blocking for a shield, Bows for a bow); lower levels scale between 1 and this. 1 = vanilla, no bonus. 0.5 = a maxed skill swaps in half the time, level 50 in three quarters. 0.25 = four times faster at 100. Armour, torches and tools have no skill and are untouched.",
				new AcceptableValueRange<float>(0.1f, 1f)));
		Blocking.EquipFromLevel = config("13 - Combat", "Equip speed bonus from level", 0,
			new ConfigDescription("Skill level at which the equip bonus starts; below it swaps take vanilla time. 0 = from the first level. 45 = a milestone: vanilla until 45, then faster.",
				new AcceptableValueRange<int>(0, 100)));

		cartLoadAt100 = config("7 - Hauling", "Cart load weight at level 100 (x)", 1f, "How much a cart's cargo weighs to the puller at Hauling 100, as a multiplier (scaled linearly with level). 1 = vanilla, no effect. 0.5 = a full cart pulls like a half-full one. Applies on attach and every 5 s while attached.");
		cartBreakAt100 = config("7 - Hauling", "Cart break force at level 100 (x)", 1f, "Multiplier on the force needed to snap the cart off the player at Hauling 100 (scaled with level). 1 = vanilla. 2 = a skilled hauler keeps the cart on steeper slopes.");
		cartTraining = config("7 - Hauling", "Cart pulling trains Hauling", Toggle.On, "Moving while attached to a loaded cart trains Hauling.");
		cartTrainingLoad = config("7 - Hauling", "Cart training load (weight units)", 100f, "Minimum cargo weight in the cart for pulling it to count as training.");
		cartTrainingXp = config("7 - Hauling", "Cart training experience (x walking)", 2f, "Experience per cart tick relative to a loaded-walking tick. Uses the same Training interval.");

		raidsEnabled = config("14 - Raids", "Raids", Toggle.On, "Off = no random raids at all. Boss-triggered and scripted events are unaffected.");
		raidIntervalMult = config("14 - Raids", "Raid interval (x vanilla 46 min)", 1f, "Multiplier on the time between raid rolls (vanilla rolls every 46 min). 2 = half as often, 0.5 = twice as often. Stacks with the Raids world slider.");
		raidChanceMult = config("14 - Raids", "Raid chance (x vanilla 20%)", 1f, "Multiplier on the chance that a roll starts a raid (vanilla 20%). Stacks with the Raids world slider.");
		raidDurationMult = config("14 - Raids", "Raid duration (x vanilla)", 1f, "Multiplier on how long a raid lasts (most vanilla raids are 60-150 s).");
		raidsAnywhere = config("14 - Raids", "Raids anywhere", Toggle.Off, "On = raids can also start away from player bases. Off = vanilla (near a base only).");
		disabledRaids = config("14 - Raids", "Disabled raids", "", "Comma-separated raid names that never happen, e.g. 'army_eikthyr, wolves, army_goblin'. Names are logged at startup.");

		commandableTames = config("15 - Tames", "Commandable tames", "Boar", "Comma-separated creature prefab names whose tamed animals can be told to follow or stay by interacting with them, like wolves and lox. Vanilla boars cannot. Applies to animals as they load; tames never use portals. Empty = vanilla.");
		PassiveTames.Enabled = config("15 - Tames", "Passive taming and breeding", Toggle.Off, "Off by default: the server-only ServerBasedRanch mod does this continuously on the server and is the preferred way. Turn this on only on a server without ServerBasedRanch. Pens keep working while nobody is near. When an animal loads again, the time it was unloaded is replayed with the vanilla rules: it eats food lying in the pen when hungry, tames while fed, gains love points, conceives and gives birth; newborns then grow up on the world clock. Only animals that are tamed or have been fed once are tracked. Off = vanilla (nothing happens while unloaded).");
		PassiveTames.FeedRadius = config("15 - Tames", "Passive feed radius (metres)", 8f, "How far from where the animal stands food is taken from during the replay. Vanilla animals walk up to 5 m to eat while loaded.");
		PassiveTames.MaxHours = config("15 - Tames", "Passive catch-up limit (hours)", 12f, "At most this much unloaded time is replayed per load. Bounds how much food a long absence eats and how many animals appear at once.");
		PassiveTames.MaxPerPen = config("15 - Tames", "Max animals per pen", 4, "Breeding stops when this many adults plus young of the same kind are within 10 m. Vanilla 4. Applies loaded and in the replay.");

		Gathering.Enabled = config("16 - Gathering", "Skill based yield", Toggle.On, "Ore deposits, rocks, trees and logs drop extra items scaled by the Pickaxes or Wood cutting skill of whoever lands the finishing hit. Off = vanilla.");
		Gathering.OreBonusAt100 = config("16 - Gathering", "Extra ore and stone at level 100 (x)", 1f, "Extra drops from ore deposits and rocks at Pickaxes 100, as a fraction of the vanilla drop (1 = +100% = double). Scales linearly with level: at 50 each item has a 50% chance of a second copy. Stacks with the Resources world slider. Pieces of a deposit that collapse because of your hit count too. 0 = off.");
		Gathering.WoodBonusAt100 = config("16 - Gathering", "Extra wood at level 100 (x)", 1f, "Extra drops from trees, logs and stumps at Wood cutting 100, as a fraction of the vanilla drop (1 = +100% = double). Scales linearly with level. Stacks with the Resources world slider. 0 = off.");

		DigDepth.Limit = config("17 - Terrain", "Dig and raise limit (metres)", 12f, "How far a pickaxe or hoe may lower or raise the ground from its original height, in metres. Vanilla 8. Applies to digging down and building up alike. Existing pits and mounds are unaffected until edited again; lowering this below a pit's current depth makes the pit display shallower on every client until it is dug again.");

		Hoe.RadiusMult = config("18 - Hoe", "Radius multiplier (x)", 1.5f, "Multiplier on the area every hoe and cultivator action covers: level ground, raise ground, path, paved road, cultivate, replant. Vanilla 1 = a 2 m square; 1.5 = 3 m, 2 = 4 m. The pickaxe is not affected. To level to the point you aim at instead of your feet, hold the alt-place key (Left Alt) - that is vanilla.");
		Hoe.RaiseStepMult = config("18 - Hoe", "Raise step multiplier (x)", 1f, "Multiplier on how much ground one raise-ground click adds. 1 = vanilla.");
		Hoe.RaiseCostsStone = config("18 - Hoe", "Raise ground costs stone", Toggle.On, "Off = raising ground needs no stone. On = vanilla (1 stone per click).");

		Stars.Enabled = config("19 - Stars", "Star chance by progress", Toggle.On, "Creatures in biomes the group has out-levelled spawn with stars more often. Multiplier on vanilla's level-up chance (10% per star) = 1 + Per boss over biome x (bosses defeated - biome tier), never below 1. Biome tiers: Meadows 0, Black Forest 1, Swamp 2, Mountain 3, Plains 4, Mistlands 5, Ashlands 6, Deep North 7. The biome you are currently fighting through stays vanilla; the ones behind you get spicier. Off = vanilla everywhere.");
		Stars.PerStep = config("19 - Stars", "Per boss over biome (x)", 0.2f, "Added to the multiplier for each defeated boss beyond the biome's tier. 0.2: Meadows with two bosses down = x1.4 (14% one star, 2% two stars); with four down = x1.8 (18% / 3.2%).");
		Stars.MaxMult = config("19 - Stars", "Max multiplier (x)", 2f, "Cap on the multiplier. 2 = at most 20% one star and 4% two stars, however far ahead the group is.");
		Stars.BossKeys = config("19 - Stars", "Boss keys", "defeated_eikthyr, defeated_gdking, defeated_bonemass, defeated_dragon, defeated_goblinking, defeated_queen, defeated_fader", "World keys counted as defeated bosses, comma-separated. Add the Deep North boss key when known. Each entry that is set in the world counts once, so listing a key twice makes that boss count double.");
		Stars.StepScope = config("19 - Stars", "Star chance scope", Stars.Scope.BiomeProgress, "What a 'step' is for every setting in this section. BiomeProgress = bosses defeated minus the biome's tier, never below 0: only biomes the group has out-levelled get more stars, and the one you are fighting through stays vanilla. Global = bosses defeated, everywhere: every biome, the current one included, gets more stars with each boss killed. With Global, 'Per boss over biome' simply means per boss.");
		Stars.SeparateTwoStar = config("19 - Stars", "Separate two-star chance", Toggle.Off, "Off = vanilla's rule: the same chance is rolled once per star, so two stars are always the one-star chance squared (10% gives 1%, 20% gives 4%). On = the second star gets its own chance, set by the three entries below, and the one-star chance above is left exactly as it is. Only creatures spawned by the world, by spawner piles and by fixed spawners are affected; breeding, the spawn command and saved creatures are not. Needs 'Star chance by progress' On.");
		Stars.TwoStarBase = config("19 - Stars", "Two-star chance base (%)", 1f, new ConfigDescription("Chance in percent that a spawn is two-star with zero steps. 1 = vanilla's 1%. This is the share of ALL spawns, not of starred ones.", new AcceptableValueRange<float>(0f, 50f)));
		Stars.TwoStarPerStep = config("19 - Stars", "Two-star chance per step (%)", 1f, new ConfigDescription("Added to the two-star chance for every step (see 'Star chance scope'). With Global scope and 1 here: 1% with no bosses down, 4% after three, 8% after all seven. 0 = a flat two-star chance.", new AcceptableValueRange<float>(0f, 20f)));
		Stars.TwoStarMax = config("19 - Stars", "Two-star chance max (%)", 10f, new ConfigDescription("Ceiling on the two-star chance. It can also never exceed the one-star chance at that spot, because a two-star creature is a starred creature: with a 14% one-star chance, asking for 20% two-stars gives 14%, all of them two-star.", new AcceptableValueRange<float>(0f, 50f)));
		Exploration.RadiusMult = config("20 - Exploration", "Map reveal radius (x)", 1.5f, new ConfigDescription("Multiplier on how far around you the map is uncovered while on foot. 1 = vanilla. 1.5 = half as far again, which is a little over twice the area per step. 2 = twice as far, four times the area. The log prints the game's own radius in metres the first time the map updates. Each player's own map; nothing is sent to anyone.", new AcceptableValueRange<float>(0.1f, 10f)));
		Exploration.ShipRadiusMult = config("20 - Exploration", "Map reveal radius aboard a ship (x)", 2f, new ConfigDescription("The same multiplier while you are aboard a ship, used INSTEAD of the one above, not on top of it. 1 = vanilla. 2 = a coastline is charted from twice as far out. Aboard means inside the ship's deck area, the same test the game uses for its own ship checks, so rafts, karves, longships and modded hulls all count.", new AcceptableValueRange<float>(0.1f, 10f)));
		Recovery.RestingTimeAfterDeath = config("21 - Recovery", "Resting time after a death (x)", 1f, new ConfigDescription("Multiplier on the fireside wait before the Rested buff arrives, while you have recently died. 1 = vanilla. 0.25 = a quarter of the wait. 0 = Rested the moment you are resting (by a fire, under a roof, unnoticed by enemies). Food, the tombstone and the length of the Rested buff are untouched. The log prints the game's own wait in seconds the first time you rest.", new AcceptableValueRange<float>(0f, 1f)));
		Recovery.JustDiedWindow = config("21 - Recovery", "Counts as just died for (seconds)", 120f, new ConfigDescription("How long after a death the shorter wait above applies. The clock is the game's own time-since-death (the one behind the no-skill-drain grace period); it starts when you die and keeps running while you respawn and walk back.", new AcceptableValueRange<float>(0f, 3600f)));
		BossAdds.HonourScaling = config("22 - Boss adds", "Scaled damage from boss adds", Toggle.On, "Creatures spawned by the server-side BossDirector mod carry a damage multiplier chosen in ITS config (for example 0.65). On = a hit from such a creature on a player is scaled by that number. Off = they hit at full strength. Without BossDirector on the server no creature carries the number and this does nothing. Wild creatures and the bosses themselves are never affected.");
		BossAdds.HonourScaling.SettingChanged += (_, _) => BossAdds.WriteMarker();
		foreach (ConfigEntry<float> e in new[] { raidIntervalMult, raidChanceMult, raidDurationMult })
		{
			e.SettingChanged += (_, _) => ApplyRaids();
		}
		raidsEnabled.SettingChanged += (_, _) => ApplyRaids();
		raidsAnywhere.SettingChanged += (_, _) => ApplyRaids();
		disabledRaids.SettingChanged += (_, _) => ApplyRaids();

		EnsureHaulingSkill();

		Harmony harmony = new(ModGUID);
		harmony.PatchAll();
	}

	// ---------------------------------------------------------------- 2 - Structures

	[HarmonyPatch(typeof(WearNTear), "Awake")]
	private static class MaterialHealthPatch
	{
		[HarmonyPriority(Priority.First)]
		private static void Prefix(WearNTear __instance)
		{
			if (materialHealth.TryGetValue(__instance.m_materialType, out ConfigEntry<float> entry) && entry.Value > 0f && Math.Abs(entry.Value - 1f) > 0.001f)
			{
				__instance.m_health *= entry.Value;
			}
		}
	}

	[HarmonyPatch(typeof(WearNTear), "RPC_Damage")]
	private static class StructureHitPatch
	{
		private static void Prefix(WearNTear __instance, HitData hit)
		{
			if (hit == null) return;
			Character attacker = hit.GetAttacker();
			float mult;
			if (attacker == null)
			{
				mult = environmentalHitMult.Value;
			}
			else if (attacker.IsPlayer())
			{
				Piece piece = __instance.m_piece;
				long creator = piece != null ? piece.GetCreator() : 0L;
				bool isCreator = creator != 0L && attacker is Player player && player.GetPlayerID() == creator;
				mult = isCreator ? 1f : playerOtherDamageMult.Value;
			}
			else
			{
				mult = attacker.IsBoss() ? bossDamageMult.Value : creatureDamageMult.Value;
			}
			if (materialDamageTaken.TryGetValue(__instance.m_materialType, out ConfigEntry<float> taken))
			{
				mult *= Math.Max(0f, taken.Value);
			}
			if (Math.Abs(mult - 1f) > 0.001f)
			{
				hit.ApplyModifier(Math.Max(0f, mult));
			}
		}
	}

	[HarmonyPatch(typeof(WearNTear), nameof(WearNTear.ApplyDamage))]
	private static class NaturalWearPatch
	{
		private static void Prefix(ref float damage, HitData hitData)
		{
			if (hitData == null)
			{
				damage *= weatherDamage.Value == Toggle.Off ? 0f : Math.Max(0f, naturalWearMult.Value);
			}
		}
	}

	// ---------------------------------------------------------------- 3 - Player

	private class PlayerBase
	{
		public float dodge, run, jump, sneak, swimMin, swimMax, encumbered, regenDelay, pickup;
	}

	private static readonly ConditionalWeakTable<Player, PlayerBase> playerBases = new();

	private static void ApplyPlayer(Player p)
	{
		PlayerBase b = playerBases.GetValue(p, x => new PlayerBase
		{
			dodge = x.m_dodgeStaminaUsage, run = x.m_runStaminaDrain, jump = x.m_jumpStaminaUsage, sneak = x.m_sneakStaminaDrain,
			swimMin = x.m_swimStaminaDrainMinSkill, swimMax = x.m_swimStaminaDrainMaxSkill, encumbered = x.m_encumberedStaminaDrain,
			regenDelay = x.m_staminaRegenDelay, pickup = x.m_autoPickupRange,
		});
		p.m_dodgeStaminaUsage = b.dodge * dodgeStaminaMult.Value;
		p.m_runStaminaDrain = b.run * runStaminaMult.Value;
		p.m_jumpStaminaUsage = b.jump * jumpStaminaMult.Value;
		p.m_sneakStaminaDrain = b.sneak * sneakStaminaMult.Value;
		p.m_swimStaminaDrainMinSkill = b.swimMin * swimStaminaMult.Value;
		p.m_swimStaminaDrainMaxSkill = b.swimMax * swimStaminaMult.Value;
		p.m_encumberedStaminaDrain = b.encumbered * encumberedStaminaMult.Value;
		p.m_staminaRegenDelay = b.regenDelay * regenDelayMult.Value;
		p.m_autoPickupRange = b.pickup * pickupRangeMult.Value;
	}

	private static void ReapplyAllPlayers()
	{
		foreach (Player p in Player.GetAllPlayers())
		{
			if (p) ApplyPlayer(p);
		}
	}

	[HarmonyPatch(typeof(Player), "Awake")]
	private static class PlayerAwakePatch
	{
		private static void Postfix(Player __instance) => ApplyPlayer(__instance);
	}

	private static bool inAreaRepair;

	[HarmonyPatch(typeof(WearNTear), nameof(WearNTear.Repair))]
	private static class AreaRepairPatch
	{
		private static void Postfix(WearNTear __instance, bool __result)
		{
			if (inAreaRepair || !__result || areaRepair.Value == Toggle.Off || areaRepairRadius.Value <= 0f) return;
			inAreaRepair = true;
			try
			{
				Vector3 center = __instance.transform.position;
				float radiusSq = areaRepairRadius.Value * areaRepairRadius.Value;
				int repaired = 0;
				foreach (WearNTear other in WearNTear.GetAllInstances())
				{
					if (other == null || other == __instance) continue;
					if ((other.transform.position - center).sqrMagnitude > radiusSq) continue;
					if (other.Repair()) repaired++;
				}
				if (repaired > 0 && Player.m_localPlayer != null)
				{
					Player.m_localPlayer.Message(MessageHud.MessageType.TopLeft, "Repaired " + (repaired + 1) + " pieces");
				}
			}
			finally
			{
				inAreaRepair = false;
			}
		}
	}

	// ---------------------------------------------------------------- 4 - Items

	private static readonly Dictionary<ItemDrop.ItemData.SharedData, int> baseStacks = new();

	private static void ApplyStacks()
	{
		if (!ObjectDB.instance || ObjectDB.instance.m_items == null) return;
		float mult = Math.Max(0.1f, stackMult.Value);
		int changed = 0;
		foreach (GameObject go in ObjectDB.instance.m_items)
		{
			if (!go) continue;
			ItemDrop drop = go.GetComponent<ItemDrop>();
			if (!drop || drop.m_itemData?.m_shared == null) continue;
			ItemDrop.ItemData.SharedData shared = drop.m_itemData.m_shared;
			if (!baseStacks.TryGetValue(shared, out int baseStack))
			{
				baseStack = shared.m_maxStackSize;
				baseStacks[shared] = baseStack;
			}
			if (baseStack <= 1) continue;
			shared.m_maxStackSize = Math.Max(1, (int)Math.Round(baseStack * mult));
			changed++;
		}
		mod.Logger.LogInfo("BruceQoL: stack size x" + mult.ToString("0.##", CultureInfo.InvariantCulture) + " applied to " + changed + " items.");
	}

	[HarmonyPatch(typeof(ObjectDB), "Awake")]
	private static class ObjectDBAwakePatch
	{
		[HarmonyPriority(Priority.Last)]
		private static void Postfix() => ApplyStacks();
	}

	[HarmonyPatch(typeof(ObjectDB), nameof(ObjectDB.CopyOtherDB))]
	private static class ObjectDBCopyPatch
	{
		[HarmonyPriority(Priority.Last)]
		private static void Postfix() => ApplyStacks();
	}

	// ---------------------------------------------------------------- 5 - Fires

	private static readonly ConditionalWeakTable<Fireplace, StrongBox<bool>> fireplaceOriginal = new();

	private static bool WantsInfiniteFuel(Fireplace fire)
	{
		string name = fire.name.ToLowerInvariant();
		if (name.Contains("torch")) return infiniteTorches.Value == Toggle.On;
		if (name.Contains("brazier")) return infiniteBraziers.Value == Toggle.On;
		return infiniteFires.Value == Toggle.On;
	}

	[HarmonyPatch(typeof(Fireplace), "Awake")]
	private static class FireplaceAwakePatch
	{
		[HarmonyPriority(Priority.First)]
		private static void Prefix(Fireplace __instance)
		{
			fireplaceOriginal.GetValue(__instance, f => new StrongBox<bool>(f.m_infiniteFuel));
			if (WantsInfiniteFuel(__instance)) __instance.m_infiniteFuel = true;
		}
	}

	// Vanilla's 2 s tick writes ZDOVars.s_lastTime on every pass (inside GetTimeSinceLastUpdate) even when
	// the fuel is infinite. Each write bumps the ZDO revision, so every torch costs a network update every
	// 2 s for everyone nearby. For infinite fires we skip the owner block entirely and only refresh the
	// visual state, which reads the ZDO and toggles objects. Side effect, documented in the config text:
	// a fire that stops being infinite (config flipped off) burns its stored fuel down on the next tick,
	// because its last-update stamp is stale.
	[HarmonyPatch(typeof(Fireplace), "UpdateFireplace")]
	private static class FireplaceUpdatePatch
	{
		private static bool Prefix(Fireplace __instance)
		{
			StrongBox<bool> original = fireplaceOriginal.GetValue(__instance, f => new StrongBox<bool>(f.m_infiniteFuel));
			bool infinite = original.Value || WantsInfiniteFuel(__instance);
			__instance.m_infiniteFuel = infinite;
			if (!infinite || original.Value) return true; // vanilla for finite fires and for prefabs that are infinite by design
			if (__instance.m_nview == null || !__instance.m_nview.IsValid()) return false;
			__instance.UpdateState();
			return false;
		}
	}

	// ---------------------------------------------------------------- 2 - Structures: wear throttle

	// WearNTear.UpdateWear runs the full body, including the UpdateSupport physics overlap, every time the
	// round-robin updater reaches a piece the local instance owns (after the first 30 s). For pieces that
	// cannot change state right now we let it through once per interval instead. Throttled, not skipped:
	// support loss is still detected within one interval. Same logic as BruceNetworking's server-side
	// throttle, here for every owner (players own the bases they stand in).
	[HarmonyPatch(typeof(WearNTear), nameof(WearNTear.UpdateWear))]
	private static class WearThrottlePatch
	{
		private static readonly Dictionary<int, float> lastFull = new();
		private static float lastPrune;

		private static bool Prefix(WearNTear __instance, float time)
		{
			if (wearThrottle.Value != Toggle.On) return true;
			ZNetView nview = __instance.m_nview;
			if (nview == null || !nview.IsValid() || !nview.IsOwner()) return true;
			if (__instance.m_createTime < 0f || time - __instance.m_createTime <= 30f) return true;
			if (__instance.m_inAshlands || __instance.m_rainWet || __instance.m_addPreSnow || EnvMan.IsWet()) return true;
			if (__instance.m_biome == Heightmap.Biome.DeepNorth) return true;
			float health = __instance.m_health;
			if (nview.GetZDO().GetFloat(ZDOVars.s_health, health) < health) return true;

			float interval = Math.Max(1f, wearInterval.Value);
			int id = __instance.GetInstanceID();
			if (lastFull.TryGetValue(id, out float last) && time - last < interval) return false;
			lastFull[id] = time;

			if (time - lastPrune > 120f)
			{
				lastPrune = time;
				List<int> stale = new();
				foreach (KeyValuePair<int, float> kv in lastFull)
				{
					if (time - kv.Value > interval * 2f) stale.Add(kv.Key);
				}
				foreach (int k in stale) lastFull.Remove(k);
			}
			return true;
		}
	}

	// ---------------------------------------------------------------- 2 - Structures: roof raycasts

	// WearNTear.UpdateCover runs for every loaded piece on every client and, while it rains, sphere-casts
	// 100 m upward every 4 s from each piece that has no cached roof. Its only consumers are the rain
	// wear and wet visual (owner-side UpdateWear), snow (Deep North) and ash (Ashlands) logic. With weather
	// damage off, the rain result is never used, so skip the cast outside those two biomes.
	[HarmonyPatch(typeof(WearNTear), nameof(WearNTear.UpdateCover))]
	private static class CoverRaycastPatch
	{
		private static bool Prefix(WearNTear __instance)
		{
			if (weatherDamage.Value != Toggle.Off) return true;
			if (__instance.m_inAshlands || __instance.m_biome == Heightmap.Biome.DeepNorth) return true;
			return false;
		}
	}

	// ---------------------------------------------------------------- 10 - Containers

	// Container.Awake builds the Inventory from m_width/m_height; set them first. The container may sit
	// on a child of a ship or cart, so the prefab name is taken from the root object.
	[HarmonyPatch(typeof(Container), "Awake")]
	private static class ContainerSizePatch
	{
		[HarmonyPriority(Priority.First)]
		private static void Prefix(Container __instance)
		{
			if (containerSizeMap.Count == 0) return;
			string prefab = Utils.GetPrefabName(__instance.transform.root.gameObject);
			if (!containerSizeMap.TryGetValue(prefab, out (int w, int h) size))
			{
				prefab = Utils.GetPrefabName(__instance.gameObject);
				if (!containerSizeMap.TryGetValue(prefab, out size)) return;
			}
			__instance.m_width = size.w;
			__instance.m_height = size.h;
		}
	}

	[HarmonyPatch(typeof(Container), nameof(Container.GetHoverText))]
	private static class ContainerHoverPatch
	{
		private static void Postfix(Container __instance, ref string __result)
		{
			if (chestHover.Value != Toggle.On || __instance.m_inventory == null) return;
			if (__instance.m_checkGuardStone && !PrivateArea.CheckAccess(__instance.transform.position, 0f, flash: false)) return;
			List<ItemDrop.ItemData> items = __instance.m_inventory.GetAllItems();
			if (items.Count == 0) return;
			Dictionary<string, int> counts = new();
			foreach (ItemDrop.ItemData item in items)
			{
				counts[item.m_shared.m_name] = counts.TryGetValue(item.m_shared.m_name, out int c) ? c + item.m_stack : item.m_stack;
			}
			List<KeyValuePair<string, int>> sorted = new(counts);
			sorted.Sort((a, b) => b.Value.CompareTo(a.Value));
			System.Text.StringBuilder sb = new(__result);
			int max = Math.Max(1, chestHoverMax.Value);
			for (int i = 0; i < sorted.Count && i < max; i++)
			{
				sb.Append("\n<color=#cccccc>").Append(Localization.instance.Localize(sorted[i].Key)).Append(" x").Append(sorted[i].Value).Append("</color>");
			}
			if (sorted.Count > max) sb.Append("\n<color=#888888>+").Append(sorted.Count - max).Append(" more</color>");
			__result = sb.ToString();
		}
	}

	[HarmonyPatch(typeof(Fermenter), nameof(Fermenter.GetHoverText))]
	private static class FermenterHoverPatch
	{
		private static void Postfix(Fermenter __instance, ref string __result)
		{
			if (fermenterHover.Value != Toggle.On || __instance.m_nview == null || !__instance.m_nview.IsValid()) return;
			if (__instance.GetContent() == 0) return;
			double elapsed = __instance.GetFermentationTime();
			double remaining = __instance.m_fermentationDuration - elapsed;
			if (elapsed <= 0.0 || remaining <= 0.0 || elapsed > __instance.m_fermentationDuration) return;
			__result += $"\n<color=#cccccc>{Math.Ceiling(remaining / 60.0):0} min left</color>";
		}
	}

	// ---------------------------------------------------------------- 11 - Crafting stations

	[HarmonyPatch(typeof(CraftingStation), "Start")] // CraftingStation has no Awake in 1.0.7
	private static class CraftingStationPatch
	{
		private static void Postfix(CraftingStation __instance)
		{
			if (stationRange.Value >= 1f) __instance.m_rangeBuild = stationRange.Value;
			if (stationsNeedRoof.Value == Toggle.Off) __instance.m_craftRequireRoof = false;
		}
	}

	// ---------------------------------------------------------------- 12 - Beehives

	[HarmonyPatch(typeof(Beehive), "Awake")]
	private static class BeehivePatch
	{
		private static void Postfix(Beehive __instance)
		{
			if (beehiveTweaks.Value != Toggle.On) return;
			__instance.m_maxHoney = Math.Max(1, honeyPerHive.Value);
			__instance.m_secPerUnit = Math.Max(1f, secondsPerHoney.Value);
		}
	}

	// ---------------------------------------------------------------- 3 - Player: re-equip after swimming

	// Humanoid.UpdateEquipment hides hand items while swimming. Vanilla only shows them again on the
	// toggle key. We remember that the swim did the hiding and show them once the player is out.
	[HarmonyPatch(typeof(Humanoid), "UpdateEquipment")]
	private static class ReequipAfterSwimPatch
	{
		private static bool hiddenBySwim;

		private static void Prefix(Humanoid __instance, out bool __state)
		{
			__state = __instance.IsPlayer() && __instance == Player.m_localPlayer && __instance.IsSwimming() && !__instance.IsOnGround()
				&& (__instance.m_rightItem != null || __instance.m_leftItem != null);
		}

		private static void Postfix(Humanoid __instance, bool __state)
		{
			if (reequipAfterSwim.Value != Toggle.On || !__instance.IsPlayer() || __instance != Player.m_localPlayer) return;
			if (__state) { hiddenBySwim = true; return; }
			if (!hiddenBySwim) return;
			if (__instance.IsSwimming() && !__instance.IsOnGround()) return;
			hiddenBySwim = false;
			if (__instance.m_rightItem == null && __instance.m_leftItem == null && !__instance.InAttack() && !__instance.InDodge())
			{
				__instance.ShowHandItems();
			}
		}
	}

	// ---------------------------------------------------------------- 9 - Multiplayer scaling

	// Game.GetDifficultyDamageScalePlayer / GetDifficultyDamageScaleEnemy read these public fields on the
	// Game instance, on whichever machine resolves a hit (the owner of the damaged character). Both sides
	// run this mod, and the values are server-synced, so every machine computes the same scale.
	private static readonly float[] vanillaScaling = { 0.3f, 0.04f, 100f, 5f };

	private static void ApplyDifficultyScaling()
	{
		Game game = Game.instance;
		if (game == null) return;
		bool on = mpScaling.Value == Toggle.On;
		game.m_healthScalePerPlayer = on ? Math.Max(0f, mpHealthPerPlayer.Value) : vanillaScaling[0];
		game.m_damageScalePerPlayer = on ? Math.Max(0f, mpDamagePerPlayer.Value) : vanillaScaling[1];
		game.m_difficultyScaleRange = on ? Math.Max(0f, mpRange.Value) : vanillaScaling[2];
		game.m_difficultyScaleMaxPlayers = on ? Math.Max(1, mpMaxPlayers.Value) : (int)vanillaScaling[3];
	}

	[HarmonyPatch(typeof(Game), "Awake")]
	private static class GameAwakeScalingPatch
	{
		private static void Postfix() => ApplyDifficultyScaling();
	}

	// ---------------------------------------------------------------- 1 - General: cheated-world flag

	// Achievements.IsWorldCheated -> ServerOptionsGUI.WorldContainsCheatedModifiers compares the world's
	// starting keys against the values the modifier GUI can produce; a launch-line key like
	// "movestaminarate 85" is not among them, so the world (and every player on it) counts as cheated.
	[HarmonyPatch(typeof(ServerOptionsGUI), nameof(ServerOptionsGUI.WorldContainsCheatedModifiers))]
	private static class WorldCheatedPatch
	{
		private static bool Prefix(ref bool __result)
		{
			if (ignoreCheatedKeys.Value != Toggle.On) return true;
			__result = false;
			return false;
		}
	}

	[HarmonyPatch(typeof(ServerOptionsGUI), nameof(ServerOptionsGUI.SetKeyAndValueIsCheat))]
	private static class SetKeyCheatedPatch
	{
		private static bool Prefix(ref bool __result)
		{
			if (ignoreCheatedKeys.Value != Toggle.On) return true;
			__result = false;
			return false;
		}
	}

	// ---------------------------------------------------------------- 7 - Hauling

	// SkillManager's Skill constructor registers the skill, then resolves Localization.instance for its
	// name. On a 1.0 client that throws (Steam not initialised yet) while a plugin Awake runs. Retry at
	// the main menu, well before any Player exists; the registry uses indexers, so a retry just overwrites.
	private static void EnsureHaulingSkill()
	{
		if (haulingReady) return;
		try
		{
			hauling = new Skill("Hauling", "hauling.png") { Configurable = true, ConfigGroup = "7 - Hauling", HideEffectFactor = true };
			hauling.Description.English("Carrying heavy loads trains this skill. Each level raises how much you can carry.");
			haulingReady = true;
		}
		catch (Exception e)
		{
			mod.Logger.LogWarning($"Hauling skill deferred to the main menu: {e.GetType().Name}: {e.Message}");
		}
	}

	[HarmonyPatch(typeof(FejdStartup), "Start")]
	private static class HaulingRetryPatch
	{
		private static void Postfix() => EnsureHaulingSkill();
	}

	[HarmonyPatch(typeof(Character), "UpdateWalking")]
	private static class HaulingTrainingPatch
	{
		private static float counter;

		private static void Postfix(Character __instance, float dt)
		{
			if (!haulingReady) return;
			if (__instance is not Player player || player != Player.m_localPlayer) return;
			if (player.m_currentVel.magnitude <= 0.1f) return;
			if (player.m_inventory.m_totalWeight < player.GetMaxCarryWeight() * haulingThreshold.Value) return;
			counter += dt;
			if (counter >= Math.Max(0.1f, haulingInterval.Value))
			{
				counter = 0f;
				player.RaiseSkill("Hauling");
			}
		}
	}

	[HarmonyPatch(typeof(Player), nameof(Player.GetMaxCarryWeight))]
	private static class HaulingCarryPatch
	{
		private static void Postfix(Player __instance, ref float __result)
		{
			if (!haulingReady) return;
			// Vanilla skill factor (0..1) times a flat amount; deliberately not SkillManager's effect-factor-scaled version.
			__result += __instance.GetSkillFactor(Skill.fromName("Hauling")) * Math.Max(0f, haulingBonus.Value);
		}
	}

	// ---------------------------------------------------------------- 6 - Skills

	private static HashSet<Skills.SkillType> weaponSkills = new();

	private static void ParseWeaponSkills()
	{
		HashSet<Skills.SkillType> set = new();
		foreach (string part in weaponCrossSkills.Value.Split(',', ';'))
		{
			string name = part.Trim();
			if (name.Length > 0 && Enum.TryParse(name, true, out Skills.SkillType t)) set.Add(t);
		}
		weaponSkills = set;
	}

	private static string PeakKey(Skills.SkillType type) => "bruceqol.peak." + type;

	private static float PeakOf(Player player, Skills.SkillType type)
	{
		return player.m_customData.TryGetValue(PeakKey(type), out string s) && float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out float v) ? v : 0f;
	}

	[HarmonyPatch(typeof(Skills.Skill), nameof(Skills.Skill.Raise))]
	private static class SkillRaisePatch
	{
		private static void Prefix(Skills.Skill __instance, ref float factor)
		{
			Player player = Player.m_localPlayer;
			if (player == null || __instance.m_info == null) return;
			Skills.SkillType type = __instance.m_info.m_skill;
			float level = __instance.m_level;

			if (peakCatchUp.Value == Toggle.On && PeakOf(player, type) > level + 0.01f)
			{
				factor *= Math.Max(1f, peakBonus.Value);
			}
			if (weaponCross.Value == Toggle.On && weaponSkills.Contains(type))
			{
				float best = 0f;
				foreach (Skills.Skill other in player.GetSkills().GetSkillList())
				{
					if (other != __instance && other.m_info != null && weaponSkills.Contains(other.m_info.m_skill))
					{
						best = Math.Max(best, other.m_level);
					}
				}
				if (best > level + 0.01f) factor *= Math.Max(1f, weaponCrossBonus.Value);
			}
			if (type == Skills.SkillType.Swim)
			{
				factor *= Math.Max(0f, swimXpMult.Value);
			}
			if (skillXpModifiers.Value == Toggle.On)
			{
				factor *= Pct(allSkillsGain.Value);
				if (skillGain.TryGetValue(type, out ConfigEntry<float> perSkill)) factor *= Pct(perSkill.Value);
			}
		}

		private static void Postfix(Skills.Skill __instance)
		{
			Player player = Player.m_localPlayer;
			if (player == null || __instance.m_info == null || peakCatchUp.Value == Toggle.Off) return;
			Skills.SkillType type = __instance.m_info.m_skill;
			if (__instance.m_level > PeakOf(player, type))
			{
				player.m_customData[PeakKey(type)] = __instance.m_level.ToString("0.##", CultureInfo.InvariantCulture);
			}
		}
	}

	[HarmonyPatch(typeof(Skills), nameof(Skills.LowerAllSkills))]
	private static class SwimNoLossPatch
	{
		private static void Prefix(Skills __instance, ref float factor, out float __state)
		{
			__state = swimNoLoss.Value == Toggle.On ? __instance.GetSkillLevel(Skills.SkillType.Swim) : -1f;
			if (skillXpModifiers.Value == Toggle.On)
			{
				factor *= Pct(deathPenaltyModifier.Value); // Skills.OnDeath passes m_DeathLowerFactor * Game.m_skillReductionRate
			}
		}

		private static void Postfix(Skills __instance, float __state)
		{
			if (__state < 0f) return;
			Skills.Skill swim = __instance.GetSkill(Skills.SkillType.Swim);
			if (swim != null) swim.m_level = __state;
		}
	}

	[HarmonyPatch(typeof(Character), nameof(Character.Damage))]
	private static class SneakBackstabPatch
	{
		private static void Prefix(Character __instance, HitData hit)
		{
			if (hit == null || sneakBackstab.Value == Toggle.Off) return;
			Player local = Player.m_localPlayer;
			if (local == null || __instance.IsPlayer() || hit.m_backstabBonus <= 1f) return;
			if (hit.GetAttacker() != local) return;
			BaseAI ai = __instance.m_baseAI;
			if (ai == null || ai.IsAlerted()) return;

			float sneak = local.GetSkillFactor(Skills.SkillType.Sneak);
			hit.m_backstabBonus *= 1f + sneak * Math.Max(0f, sneakBackstabBonus.Value);
			if (sneakBackstabXp.Value > 0f)
			{
				local.RaiseSkill(Skills.SkillType.Sneak, sneakBackstabXp.Value);
			}
		}
	}

	// ---------------------------------------------------------------- 7 - Hauling: carts

	private static float HaulingFactorOf(Player player) => player == null ? 0f : player.GetSkillFactor(Skill.fromName("Hauling"));

	private static bool PulledByLocalPlayer(Vagon cart)
	{
		Player local = Player.m_localPlayer;
		return local != null && cart.m_attachedObject != null && cart.m_attachedObject == local.gameObject;
	}

	// Vanilla: mass = base + cargo weight * factor, recomputed every 5 s on the owner (the puller).
	[HarmonyPatch(typeof(Vagon), "UpdateMass")]
	private static class CartMassPatch
	{
		private static void Postfix(Vagon __instance)
		{
			if (!PulledByLocalPlayer(__instance) || __instance.m_container == null || !__instance.m_nview.IsOwner()) return;
			float mult = 1f + (Math.Max(0f, cartLoadAt100.Value) - 1f) * HaulingFactorOf(Player.m_localPlayer);
			if (Math.Abs(mult - 1f) < 0.001f) return;
			float weight = __instance.m_container.GetInventory().GetTotalWeight();
			__instance.SetMass(__instance.m_baseMass + weight * __instance.m_itemWeightMassFactor * mult);
		}
	}

	[HarmonyPatch(typeof(Vagon), "AttachTo")]
	private static class CartAttachPatch
	{
		private static void Postfix(Vagon __instance)
		{
			if (!PulledByLocalPlayer(__instance)) return;
			float skill = HaulingFactorOf(Player.m_localPlayer);
			if (__instance.m_attachJoin != null)
			{
				__instance.m_attachJoin.breakForce = __instance.m_breakForce * (1f + (Math.Max(0f, cartBreakAt100.Value) - 1f) * skill);
			}
			__instance.UpdateMass();
		}
	}

	[HarmonyPatch(typeof(Vagon), "Update")]
	private static class CartTrainingPatch
	{
		private static float counter;

		private static void Postfix(Vagon __instance)
		{
			if (cartTraining.Value != Toggle.On || !PulledByLocalPlayer(__instance) || __instance.m_container == null) return;
			Player local = Player.m_localPlayer;
			if (local.m_currentVel.magnitude <= 0.1f) return;
			if (__instance.m_container.GetInventory().GetTotalWeight() < cartTrainingLoad.Value) return;
			counter += Time.deltaTime;
			if (counter >= Math.Max(0.1f, haulingInterval.Value))
			{
				counter = 0f;
				local.RaiseSkill("Hauling", Math.Max(0f, cartTrainingXp.Value));
			}
		}
	}

	// ---------------------------------------------------------------- 14 - Raids

	private static float raidBaseInterval = -1f, raidBaseChance = -1f;
	private static readonly Dictionary<RandomEvent, (float duration, bool nearBase, bool enabled)> raidBase = new();

	private static void ApplyRaids()
	{
		RandEventSystem sys = RandEventSystem.instance;
		if (sys == null) return;
		if (raidBaseInterval < 0f) { raidBaseInterval = sys.m_eventIntervalMin; raidBaseChance = sys.m_eventChance; }
		sys.m_eventIntervalMin = raidBaseInterval * Math.Max(0.01f, raidIntervalMult.Value);
		sys.m_eventChance = raidsEnabled.Value == Toggle.On ? raidBaseChance * Math.Max(0f, raidChanceMult.Value) : 0f;

		HashSet<string> off = new(StringComparer.OrdinalIgnoreCase);
		foreach (string part in disabledRaids.Value.Split(',', ';'))
		{
			if (part.Trim().Length > 0) off.Add(part.Trim());
		}
		List<string> names = new();
		foreach (RandomEvent ev in sys.m_events)
		{
			if (ev == null) continue;
			if (!raidBase.TryGetValue(ev, out var b)) { b = (ev.m_duration, ev.m_nearBaseOnly, ev.m_enabled); raidBase[ev] = b; }
			ev.m_duration = b.duration * Math.Max(0.05f, raidDurationMult.Value);
			ev.m_nearBaseOnly = raidsAnywhere.Value == Toggle.On ? false : b.nearBase;
			ev.m_enabled = b.enabled && !off.Contains(ev.m_name);
			if (b.enabled && ev.m_random) names.Add(ev.m_name);
		}
		mod.Logger.LogInfo("BruceQoL raids: interval x" + raidIntervalMult.Value + " chance x" + raidChanceMult.Value + " duration x" + raidDurationMult.Value + (off.Count > 0 ? " disabled=[" + string.Join(", ", off) + "]" : "") + ". Random raids: " + string.Join(", ", names));
	}

	[HarmonyPatch(typeof(RandEventSystem), "Awake")]
	private static class RaidsAwakePatch
	{
		private static void Postfix() => ApplyRaids();
	}

	// ---------------------------------------------------------------- 13 - Combat

	// Runs on the victim's owner, the same place vanilla applies its enemy/player damage rates,
	// so it stacks with (and does not touch) the world keys. No cheat flag involved.
	[HarmonyPatch(typeof(Character), "RPC_Damage")]
	private static class CombatDamagePatch
	{
		private static void Prefix(Character __instance, HitData hit)
		{
			if (hit == null) return;
			Character attacker = hit.GetAttacker();
			if (attacker == null) return;
			float mult = 1f;
			if (!attacker.IsPlayer())
			{
				if (__instance.IsPlayer()) mult = attacker.IsBoss() && bossDamageToPlayers.Value > 0f ? bossDamageToPlayers.Value : enemyDamageToPlayers.Value;
				else if (__instance.IsTamed()) mult = enemyDamageToTames.Value;
			}
			else if (!__instance.IsPlayer() && !__instance.IsTamed())
			{
				mult = playerDamageToEnemies.Value;
			}
			if (Math.Abs(mult - 1f) > 0.001f)
			{
				hit.ApplyModifier(Math.Max(0f, mult));
			}
		}
	}

	[HarmonyPatch(typeof(Character), "SetupMaxHealth")]
	private static class EnemyHealthPatch
	{
		private static void Postfix(Character __instance)
		{
			if (__instance.IsPlayer() || __instance.IsTamed()) return;
			if (__instance.m_nview == null || !__instance.m_nview.IsValid() || !__instance.m_nview.IsOwner()) return;
			float mult = Math.Max(0.01f, __instance.IsBoss() ? bossHealthMult.Value : enemyHealthMult.Value);
			if (Math.Abs(mult - 1f) < 0.001f) return;
			float oldMax = __instance.GetMaxHealth();
			bool wasFull = __instance.GetHealth() >= oldMax - 0.01f;
			__instance.SetMaxHealth(oldMax * mult);
			if (wasFull) __instance.SetHealth(__instance.GetMaxHealth());
		}
	}

	// ---------------------------------------------------------------- 15 - Tames

	// m_commandable is prefab data (true on Wolf and Lox, false on Boar). Tameable.Interact only offers
	// follow/stay when it is set, and the follow logic itself lives in MonsterAI, which every tame has.
	// Flipping the flag on each instance as it wakes covers both freshly spawned and already-saved animals.
	[HarmonyPatch(typeof(Tameable), "Awake")]
	private static class CommandableTamePatch
	{
		private static void Postfix(Tameable __instance)
		{
			if (__instance.m_commandable) return;
			string list = commandableTames.Value;
			if (string.IsNullOrWhiteSpace(list)) return;
			string prefab = Utils.GetPrefabName(__instance.gameObject);
			foreach (string entry in list.Split(','))
			{
				if (entry.Trim().Equals(prefab, StringComparison.OrdinalIgnoreCase))
				{
					__instance.m_commandable = true;
					return;
				}
			}
		}
	}
}


