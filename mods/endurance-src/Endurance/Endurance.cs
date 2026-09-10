using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using LocalizationManager;
using ServerSync;

namespace Endurance;

[BepInPlugin(ModGUID, ModName, ModVersion)]
[BepInIncompatibility("org.bepinex.plugins.valheim_plus")]
public class EndurancePlugin : BaseUnityPlugin
{
	private const string ModName = "Endurance";
	private const string ModVersion = "1.0.0";
	private const string ModGUID = "bruceirons.Endurance";

	private static readonly ConfigSync configSync = new(ModName) { DisplayName = ModName, CurrentVersion = ModVersion, MinimumRequiredVersion = ModVersion };

	private enum Toggle
	{
		On = 1,
		Off = 0,
	}

	// Endurance per food is Curve(foodStamina) * global multiplier * per-food scale.
	//   Linear:     A * S                       (original mod behaviour, A = 0.03)
	//   Declining:  max(C, A - B * S)           early food gives the most, late food tapers to the floor C
	//   Saturating: A * S / (S + B)             rises then plateaus at A
	//   SquareRoot: A * sqrt(S)                 compressed spread
	public enum Curve
	{
		Linear,
		Declining,
		Saturating,
		SquareRoot,
	}

	private static ConfigEntry<Toggle> serverConfigLocked = null!;
	private static ConfigEntry<Toggle> isEnabled = null!;
	private static ConfigEntry<float> regMultiplier = null!;
	private static ConfigEntry<Curve> curve = null!;
	private static ConfigEntry<float> curveA = null!;
	private static ConfigEntry<float> curveB = null!;
	private static ConfigEntry<float> curveC = null!;
	private static ConfigEntry<float> totalCap = null!;
	private static ConfigEntry<string> stacking = null!;
	private static float[] stackingWeights = { 1f, 0.5f, 0.25f };

	private static void ParseStacking()
	{
		List<float> weights = new();
		foreach (string part in stacking.Value.Split(',', ';', ' '))
		{
			if (float.TryParse(part.Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float w))
			{
				weights.Add(Math.Max(0f, w));
			}
		}
		stackingWeights = weights.Count > 0 ? weights.ToArray() : new[] { 1f, 0.5f, 0.25f };
	}

	private ConfigEntry<T> config<T>(string group, string name, T value, ConfigDescription description, bool synchronizedSetting = true)
	{
		ConfigEntry<T> configEntry = Config.Bind(group, name, value, description);

		SyncedConfigEntry<T> syncedConfigEntry = configSync.AddConfigEntry(configEntry);
		syncedConfigEntry.SynchronizedConfig = synchronizedSetting;

		return configEntry;
	}

	private static bool localizerLoaded;

	// Localizer.Load() forces Localization to initialise; on 1.0 clients that happens before Steam
	// is up and throws, which used to abort Awake (no config, no patches). Keep going and retry later.
	private static void TryLoadLocalizer()
	{
		if (localizerLoaded) return;
		try
		{
			Localizer.Load();
			localizerLoaded = true;
		}
		catch (Exception e)
		{
			mod.Logger.LogWarning($"Localizer.Load deferred: {e.GetType().Name}: {e.Message}");
		}
	}

	private void Awake()
	{
		mod = this;
		TryLoadLocalizer();

		serverConfigLocked = config("1 - General", "Config is locked", Toggle.On, new ConfigDescription("If on, only admins can change the configuration on a server."));
		configSync.AddLockingConfigEntry(serverConfigLocked);
		isEnabled = config("1 - General", "Enabled", Toggle.On, new ConfigDescription("If the mod is enabled."));
		regMultiplier = config("1 - General", "Multiplier for all foods", 1f, new ConfigDescription("Multiplies the Endurance of every food after the curve is applied."));
		curve = config("1 - General", "Curve", Curve.SquareRoot, new ConfigDescription("How a food's stamina value S maps to its Endurance (stamina regen per second added while the food is active). SquareRoot: A*sqrt(S) (default A=0.196: 10 stam -> +10%, 20 -> +15%, 40 -> +21%, 60 -> +25%, 80 -> +29% of the 1.0 base regen of 6/s). Linear: A*S (A=0.03 is the original mod). Declining: max(C, A - B*S). Saturating: A*S/(S+B)."));
		curveA = config("1 - General", "Curve A", 0.196f, new ConfigDescription("Curve parameter A. SquareRoot: scale (0.196 default). Linear: slope. Declining: Endurance at zero stamina. Saturating: the plateau."));
		curveB = config("1 - General", "Curve B", 0.01f, new ConfigDescription("Curve parameter B. Declining: loss per point of food stamina. Saturating: half-saturation stamina value. Unused by SquareRoot and Linear."));
		curveC = config("1 - General", "Curve C", 0.3f, new ConfigDescription("Curve parameter C. Declining: minimum Endurance (floor). Unused by other curves."));
		stacking = config("1 - General", "Stacking weights", "1, 0.5, 0.25", new ConfigDescription("How multiple active foods combine: the food with the highest Endurance counts at the first weight, the next-highest at the second, and so on. '1, 1, 1' is plain addition. Default '1, 0.5, 0.25' keeps a full belly from doubling vanilla regen while still rewarding better food."));
		totalCap = config("1 - General", "Total Endurance cap", 3.5f, new ConfigDescription("Maximum combined Endurance from all active foods, in regen per second (3.5 = about +60% of the 1.0 base regen of 6/s). 0 disables the cap."));
		stacking.SettingChanged += (_, _) => ParseStacking();
		ParseStacking();

		mod = this;

		Harmony harmony = new(ModGUID);
		harmony.PatchAll();
	}

	private static EndurancePlugin mod = null!;
	private static Dictionary<string, float> foodStamina = new();
	private static Dictionary<string, ConfigEntry<float>> foodScale = new();

	private static float CurveValue(float s)
	{
		float value = curve.Value switch
		{
			Curve.Declining => Math.Max(curveC.Value, curveA.Value - curveB.Value * s),
			Curve.Saturating => curveA.Value * s / (s + Math.Max(0.001f, curveB.Value)),
			Curve.SquareRoot => curveA.Value * (float)Math.Sqrt(s),
			_ => curveA.Value * s,
		};
		return Math.Max(0f, value);
	}

	private static bool TryGetEndurance(string itemName, out float endurance)
	{
		endurance = 0f;
		if (!foodStamina.TryGetValue(itemName, out float s))
		{
			return false;
		}
		float scale = foodScale.TryGetValue(itemName, out ConfigEntry<float> entry) ? entry.Value : 1f;
		endurance = CurveValue(s) * regMultiplier.Value * scale;
		return true;
	}

	[HarmonyPatch(typeof(ObjectDB), "Awake")]
	private class ReadFoodConfigs
	{
		[HarmonyPriority(Priority.Last)]
		private static void Postfix()
		{
			TryLoadLocalizer();
			try
			{
				Localization english = new();
				english.SetupLanguage("English");

				Regex regex = new(@"[=\n\t\\""'\[\]]*");

				List<ItemDrop.ItemData.SharedData> items = ObjectDB.instance.m_items
					.Where(i => i != null).Select(i => i.GetComponent<ItemDrop>())
					.Where(d => d != null && d.m_itemData?.m_shared != null).Select(d => d.m_itemData.m_shared).ToList();
				foodStamina = items.Where(item => item.m_itemType == ItemDrop.ItemData.ItemType.Consumable && item.m_foodStamina > 0)
					.GroupBy(item => item.m_name).ToDictionary(g => g.Key, g => g.First().m_foodStamina);
				foodScale = foodStamina.Keys.ToDictionary(name => name, name => mod.config("2 - Food (scale)", regex.Replace(english.Localize(name), ""), 1f,
					new ConfigDescription($"Per-food multiplier on top of the curve. Food stamina {foodStamina[name]} -> curve gives {CurveValue(foodStamina[name]):0.00} regen/s at scale 1.", null,
						new ConfigurationManagerAttributes { DispName = regex.Replace(Localization.instance.Localize(name), "") })));
				mod.Logger.LogInfo($"Endurance: {foodStamina.Count} stamina foods registered (curve {curve.Value}, A={curveA.Value}).");
			}
			catch (Exception e)
			{
				mod.Logger.LogError($"Endurance: failed to read food configs: {e}");
			}
		}
	}

	[HarmonyPatch(typeof(Player), nameof(Player.UpdateFood))]
	private class FoodUpdatePatch
	{
		internal static float? basisStaminaRegen;

		private static void Prefix(Player __instance)
		{
			if (isEnabled.Value == Toggle.On)
			{
				basisStaminaRegen ??= __instance.m_staminaRegen;

				List<float> endurances = new();
				foreach (Player.Food food in __instance.m_foods)
				{
					if (TryGetEndurance(food.m_item.m_shared.m_name, out float endurance))
					{
						endurances.Add(endurance);
					}
				}
				endurances.Sort((a, b) => b.CompareTo(a));
				float bonus = 0f;
				for (int i = 0; i < endurances.Count; i++)
				{
					float weight = i < stackingWeights.Length ? stackingWeights[i] : stackingWeights[stackingWeights.Length - 1];
					bonus += endurances[i] * weight;
				}
				if (totalCap.Value > 0f)
				{
					bonus = Math.Min(bonus, totalCap.Value);
				}

				__instance.m_staminaRegen = (float)basisStaminaRegen + bonus;
			}
		}
	}

	[HarmonyPatch(typeof(ItemDrop.ItemData), nameof(ItemDrop.ItemData.GetTooltip), typeof(ItemDrop.ItemData), typeof(int), typeof(bool), typeof(float), typeof(int), typeof(bool))]
	private class FoodDescPatch
	{
		private static void Postfix(ItemDrop.ItemData item, ref string __result)
		{
			if (isEnabled.Value == Toggle.On && TryGetEndurance(item.m_shared.m_name, out float endurance))
			{
				float basis = FoodUpdatePatch.basisStaminaRegen ?? 5f;
				int percent = (int)Math.Round(endurance / basis * 100f);
				__result += "\n$endurance_stat_name: <color=orange>+" + percent + "%</color>";
			}
		}
	}
}
