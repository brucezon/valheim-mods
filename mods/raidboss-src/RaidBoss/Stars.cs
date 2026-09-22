using HarmonyLib;
using UnityEngine;

namespace RaidBoss;

// A third star. The game stops at two (level 3): a creature at level 4 has four times the health and two and a half
// times the damage, as the level formula says, but it wears no star tint, no size and no star icons, because its
// level visuals have only two setups and the hud only two icons. Here a level-4 creature gets the two-star look one
// size bigger, and "★★★" before its name in the hud, so a three-star miniboss stands out.
internal static class Stars
{
	internal const int Max = 4;   // level 4 = three stars
	const float ThirdStarScale = 1.2f;

	static bool IsThreeStar(Character c) => c != null && c.GetLevel() >= Max;

	// Level visuals: use the top setup for anything above it, then grow it.
	[HarmonyPatch(typeof(LevelEffects), nameof(LevelEffects.SetupLevelVisualization))]
	static class VisualsPatch
	{
		static void Prefix(LevelEffects __instance, ref int level, out bool __state)
		{
			__state = false;
			if (__instance.m_levelSetups == null || __instance.m_levelSetups.Count == 0) return;
			int top = __instance.m_levelSetups.Count + 1;
			if (level > top) { level = top; __state = true; }
		}

		static void Postfix(LevelEffects __instance, bool __state)
		{
			if (__state) __instance.transform.localScale *= ThirdStarScale;
		}
	}

	// The hud: the two star icons off, three stars in the name instead. Runs before RaidBoss's own boss-bar text.
	[HarmonyPatch(typeof(EnemyHud), "UpdateHuds")]
	[HarmonyPriority(Priority.High)]
	static class HudPatch
	{
		static void Postfix(EnemyHud __instance)
		{
			foreach (var kv in __instance.m_huds)
			{
				EnemyHud.HudData hud = kv.Value;
				if (hud == null || hud.m_name == null || !IsThreeStar(kv.Key)) continue;
				if (hud.m_level2 != null) hud.m_level2.gameObject.SetActive(false);
				if (hud.m_level3 != null) hud.m_level3.gameObject.SetActive(false);
				hud.m_name.text = "<color=#ffcc44>★★★</color> " + hud.m_name.text;
			}
		}
	}
}
