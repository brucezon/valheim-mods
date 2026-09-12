using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace BruceQoL;

// 16 - Gathering: extra ore / stone / wood scaled by the vanilla Pickaxes and Wood cutting skills.
//
// Drops are rolled by the object's OWNER (the peer simulating the rock or tree), which is usually not
// the player swinging the tool, and skills are never synced. But every HitData carries the attacker's
// skill level (m_skillLevel, serialised with the damage RPC), so the owner already knows it. One
// catch: for an axe on a tree Attack.DoMeleeAttack sets hit.m_skill = WoodCutting (m_specialHitSkill)
// but fills m_skillLevel from the weapon's own skill (Axes). GatherHitSkillPatch corrects that on the
// attacker's side before the hit is routed. Pickaxes report their own skill, so it is a no-op there.
//
// On the owner, the damage handlers of rocks, ore deposits, trees, logs and destructibles open a
// window in which DropTable.GetDropList appends extra copies of everything it rolled: floor(bonus)
// guaranteed copies plus one more with probability frac(bonus). Bonus = at-100 value x level / 100,
// so the default 1 doubles the yield at 100 and gives a 50% chance of a second copy at 50.
// The extra copies sit on top of the vanilla list, which the Resources world slider already scaled.
internal static class Gathering
{
	internal static ConfigEntry<BruceQoLPlugin.Toggle> Enabled;
	internal static ConfigEntry<float> OreBonusAt100;
	internal static ConfigEntry<float> WoodBonusAt100;

	// Extra copies per dropped item while a gathering drop window is open; 0 = closed.
	private static float bonus;

	private static float BonusFor(HitData hit)
	{
		if (hit == null || Enabled == null || Enabled.Value != BruceQoLPlugin.Toggle.On || hit.m_skillLevel <= 0f) return 0f;
		float at100 = hit.m_skill switch
		{
			Skills.SkillType.Pickaxes => OreBonusAt100.Value,
			Skills.SkillType.WoodCutting => WoodBonusAt100.Value,
			_ => 0f,
		};
		return at100 > 0f ? at100 * Mathf.Clamp01(hit.m_skillLevel / 100f) : 0f;
	}

	// Attacker side: make the hit carry the level of the skill it says it used.
	[HarmonyPatch]
	private static class GatherHitSkillPatch
	{
		private static IEnumerable<MethodBase> TargetMethods()
		{
			yield return AccessTools.Method(typeof(TreeBase), "Damage");
			yield return AccessTools.Method(typeof(TreeLog), "Damage");
			yield return AccessTools.Method(typeof(MineRock), "Damage");
			yield return AccessTools.Method(typeof(MineRock5), "Damage");
			yield return AccessTools.Method(typeof(Destructible), "Damage");
		}

		private static void Prefix(HitData hit)
		{
			if (hit == null || (hit.m_skill != Skills.SkillType.WoodCutting && hit.m_skill != Skills.SkillType.Pickaxes)) return;
			if (hit.GetAttacker() is Player p && p == Player.m_localPlayer)
			{
				hit.m_skillLevel = p.GetSkillLevel(hit.m_skill);
			}
		}
	}

	// Owner side: open the drop window for the duration of the damage handler. Saved/restored rather
	// than cleared, because Destructible.Destroy can re-enter MineRock5 damage on the same call stack.
	[HarmonyPatch]
	private static class GatherDropWindowPatch
	{
		private static IEnumerable<MethodBase> TargetMethods()
		{
			yield return AccessTools.Method(typeof(TreeBase), "RPC_Damage");
			yield return AccessTools.Method(typeof(TreeLog), "RPC_Damage");
			yield return AccessTools.Method(typeof(MineRock), "RPC_Hit");
			yield return AccessTools.Method(typeof(MineRock5), "DamageArea");
			yield return AccessTools.Method(typeof(Destructible), "RPC_Damage");
		}

		private static void Prefix(HitData hit, out float __state)
		{
			__state = bonus;
			bonus = BonusFor(hit);
		}

		private static void Postfix(float __state) => bonus = __state;
	}

	[HarmonyPatch(typeof(DropTable), nameof(DropTable.GetDropList), new Type[0])]
	private static class GatherDropListPatch
	{
		private static void Postfix(List<GameObject> __result)
		{
			if (bonus <= 0f || __result == null || __result.Count == 0) return;
			int count = __result.Count;
			int whole = (int)Math.Floor(bonus);
			float frac = bonus - whole;
			for (int i = 0; i < count; i++)
			{
				GameObject item = __result[i];
				for (int k = 0; k < whole; k++) __result.Add(item);
				if (frac > 0f && UnityEngine.Random.value < frac) __result.Add(item);
			}
		}
	}
}
