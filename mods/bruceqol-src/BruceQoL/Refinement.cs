using System.Collections.Generic;
using System.Globalization;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace BruceQoL;

// 21 - Refinement forge: the odds at the Forge of Potential (the 1.0 idols).
//
// Vanilla (1.0.15, InventoryGui.DoCrafting): one roll r in [0, 1] is checked against the IDOL's numbers:
//   success if idol.m_upgradeChance >= r; else destroyed if idol.m_breakChance >= 1 - r; else the item
//   drops one level (the "$msg_upgrader_failed" branch). Every vanilla idol ships with m_upgradeChance 0.65
//   and m_breakChance 1, so the drop never happens: 65% up, 35% destroyed, 0% down. The prefab default the
//   game never uses is 0.65 / 0.1 (65 / 10 / 25).
// The two entries here are shares of ALL attempts: success first, then break; whatever is left is the drop.
// Break chance 0.2 with success 0.65 = 65% up, 20% destroyed, 15% down a level. A break still refunds
// materials the vanilla way (35% of the base cost plus the cost of the level you were at).
// The roll runs on the crafting player's own client against its own ObjectDB, so the values are written
// into the idol items on every peer (server-synced entries), when the ObjectDB loads and on every change.
// Idols are found through the recipes (any item a recipe lists as its upgrader resource) with the prefab
// name as a fallback, so a modded idol that follows the vanilla recipe rule is covered too.
internal static class Refinement
{
	internal static ConfigEntry<BruceQoLPlugin.Toggle> Enabled;
	internal static ConfigEntry<float> SuccessChance;
	internal static ConfigEntry<float> BreakChance;

	private static readonly Dictionary<ItemDrop.ItemData.SharedData, (float up, float brk)> vanilla = new();
	private static readonly BepInEx.Logging.ManualLogSource s_log = BepInEx.Logging.Logger.CreateLogSource("BruceQoL");

	internal static void Apply()
	{
		if (Enabled == null || !ObjectDB.instance || ObjectDB.instance.m_items == null) return;
		HashSet<ItemDrop.ItemData.SharedData> idols = new();
		if (ObjectDB.instance.m_recipes != null)
		{
			foreach (Recipe recipe in ObjectDB.instance.m_recipes)
			{
				if (recipe == null || recipe.m_resources == null) continue;
				foreach (Piece.Requirement req in recipe.m_resources)
				{
					if (req.m_upgraderResource && req.m_resItem && req.m_resItem.m_itemData?.m_shared != null) idols.Add(req.m_resItem.m_itemData.m_shared);
				}
			}
		}
		foreach (GameObject go in ObjectDB.instance.m_items)
		{
			if (!go || !go.name.StartsWith("Upgrader")) continue;
			ItemDrop drop = go.GetComponent<ItemDrop>();
			if (drop && drop.m_itemData?.m_shared != null) idols.Add(drop.m_itemData.m_shared);
		}
		if (idols.Count == 0) return;

		bool on = Enabled.Value == BruceQoLPlugin.Toggle.On;
		float up = Mathf.Clamp01(SuccessChance.Value);
		float brk = Mathf.Clamp01(BreakChance.Value);
		foreach (ItemDrop.ItemData.SharedData shared in idols)
		{
			if (!vanilla.TryGetValue(shared, out (float up, float brk) v))
			{
				v = (shared.m_upgradeChance, shared.m_breakChance);
				vanilla[shared] = v;
			}
			shared.m_upgradeChance = on ? up : v.up;
			shared.m_breakChance = on ? brk : v.brk;
		}
		if (on)
		{
			float breakShare = Mathf.Min(brk, 1f - up);
			float drop = Mathf.Max(0f, 1f - up - breakShare);
			s_log.LogInfo(string.Format(CultureInfo.InvariantCulture, "BruceQoL refinement: {0} idols set to {1:0}% success, {2:0}% destroyed, {3:0}% down a level.", idols.Count, up * 100f, breakShare * 100f, drop * 100f));
		}
		else
		{
			s_log.LogInfo("BruceQoL refinement: Off, " + idols.Count + " idols at their vanilla odds.");
		}
	}

	[HarmonyPatch(typeof(ObjectDB), "Awake")]
	private static class ObjectDBAwakePatch
	{
		[HarmonyPriority(Priority.Last)]
		private static void Postfix() => Apply();
	}

	[HarmonyPatch(typeof(ObjectDB), nameof(ObjectDB.CopyOtherDB))]
	private static class ObjectDBCopyPatch
	{
		[HarmonyPriority(Priority.Last)]
		private static void Postfix() => Apply();
	}
}
