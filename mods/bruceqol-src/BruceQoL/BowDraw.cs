using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace BruceQoL;

// 13 - Combat: bow draw time versus skill.
//
// Vanilla (Humanoid.GetAttackDrawPercentage): a bow is fully drawn after
//   Lerp(drawDurationMin, drawDurationMin * 0.2, skillFactor)
// seconds, so a 2.5 s bow takes 2.5 s at Bows 0 and 0.5 s at Bows 100. The 0.2 is a literal; it is what
// makes a high Bows skill fire five times faster than a novice. This exposes both ends of that curve:
// the multiplier at skill 0 (vanilla 1) and at skill 100 (vanilla 0.2). Off = vanilla untouched.
// Same idea as WackyMole's Tone Down the Twang (which exposes the 0.2 only); independent code.
// Runs on the drawing player's client; the entries are server-synced so everyone uses the same curve.
internal static class BowDraw
{
	internal static ConfigEntry<BruceQoLPlugin.Toggle> Enabled;
	internal static ConfigEntry<float> AtSkill0;
	internal static ConfigEntry<float> AtSkill100;

	[HarmonyPatch(typeof(Humanoid), nameof(Humanoid.GetAttackDrawPercentage))]
	private static class DrawPercentagePatch
	{
		private static bool Prefix(Humanoid __instance, ref float __result)
		{
			if (Enabled == null || Enabled.Value != BruceQoLPlugin.Toggle.On) return true;
			ItemDrop.ItemData weapon = __instance.GetCurrentWeapon();
			if (weapon == null || !weapon.m_shared.m_attack.m_bowDraw || __instance.m_attackDrawTime <= 0f)
			{
				__result = 0f;
				return false;
			}
			float skill = __instance.GetSkillFactor(weapon.m_shared.m_skillType);
			float min = weapon.m_shared.m_attack.m_drawDurationMin;
			float full = Mathf.Lerp(min * Mathf.Max(0f, AtSkill0.Value), min * Mathf.Max(0f, AtSkill100.Value), skill);
			__result = full > 0f ? Mathf.Clamp01(__instance.m_attackDrawTime / full) : 1f;
			return false;
		}
	}
}
