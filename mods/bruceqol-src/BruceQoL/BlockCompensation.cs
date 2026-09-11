using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace BruceQoL;

// 13 - Combat: block / parry difficulty compensation.
//
// Vanilla multiplies an enemy hit by Game.m_enemyDamageRate (the Combat world slider; 1.5 on Hard)
// before Humanoid.BlockAttack judges it. In 1.0 the block is armour-style: the part of the hit that
// survives the block power feeds the stagger bar and the stamina cost, and for hits under half the
// block power that residual grows with the square of the incoming damage. So the same swing fills the
// bar 2.25x faster on Hard than on Normal, and the largest hit a shield can parry shrinks by 1.5x.
//
// This divides the hit by R^c before BlockAttack and multiplies it back afterwards, on the same
// HitData instance. At c = 1 the parry window, stagger fill and stamina drain are exactly what they
// are on Combat Normal, while whatever leaks through the block (or the whole hit when the block
// fails) is still scaled by the Combat rate. Runs on the blocking player's client, because
// Character.RPC_Damage only proceeds on the victim's owner; both entries are server-synced so every
// client uses the server's value.
internal static class BlockCompensation
{
	internal static ConfigEntry<float> ParryCompensation;
	internal static ConfigEntry<float> HeldBlockCompensation;

	private const float PerfectBlockInterval = 0.25f; // Humanoid.m_perfectBlockInterval

	[HarmonyPatch(typeof(Humanoid), "BlockAttack")]
	private static class BlockAttackPatch
	{
		private static void Prefix(Humanoid __instance, HitData hit, Character attacker, out float __state)
		{
			__state = 1f;
			if (hit == null || attacker == null || attacker.IsPlayer() || !__instance.IsPlayer()) return;
			float rate = Game.m_enemyDamageRate;
			if (rate <= 0f || Mathf.Abs(rate - 1f) < 0.001f) return; // Combat Normal: nothing to undo
			ItemDrop.ItemData blocker = __instance.GetCurrentBlocker();
			if (blocker == null) return;
			// Same test BlockAttack uses to decide whether this is a timed (parry) block.
			bool timed = blocker.m_shared.m_timedBlockBonus > 1f && __instance.m_blockTimer != -1f && __instance.m_blockTimer < PerfectBlockInterval;
			ConfigEntry<float> entry = timed ? ParryCompensation : HeldBlockCompensation;
			float c = Mathf.Clamp01(entry?.Value ?? 0f);
			if (c <= 0f) return;
			float factor = Mathf.Pow(rate, c);
			hit.ApplyModifier(1f / factor);
			__state = factor;
		}

		private static void Postfix(HitData hit, float __state)
		{
			if (hit != null && __state != 1f) hit.ApplyModifier(__state);
		}
	}
}
