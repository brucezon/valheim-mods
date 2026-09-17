using System.Collections.Generic;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace BruceQoL;

// 13 - Combat: make the Blocking skill (and, for equip speed, every weapon skill) worth levelling.
//
// Vanilla (1.0.7, verified in the decompile):
//  - ItemDrop.ItemData.GetBlockPower(quality, skillFactor) = base + base * skillFactor * 0.5, so
//    Blocking 100 is a flat +50% block armour and that is the skill's whole payoff.
//  - Humanoid.BlockAttack raises Blocking by 2 for a timed block and 1 for a held one, whether or
//    not the block held (the wiki's 1 / 0.5 in its own units). A held block costs
//    m_blockStaminaDrain scaled by how much of the block power the hit used; a perfect block costs a
//    flat m_perfectBlockStaminaDrain, or on some 1.0 shields refunds m_perfectBlockStaminaRegen.
//  - Player.QueueEquipAction / QueueUnequipAction queue a MinorActionData whose m_duration is the
//    item's m_equipDuration; no skill is involved.
//
// Every lever below is Off / 0 / 1 by default, which leaves vanilla untouched. All run on the local
// player's client (BlockAttack runs on the victim's owner, equipping on the equipping player) and
// every entry is server-synced. Same ideas as MidnightMods' ImpactfulSkills; independent code.
internal static class Blocking
{
	internal static ConfigEntry<BruceQoLPlugin.Toggle> BlockPowerEnabled;
	internal static ConfigEntry<float> BlockPowerAt100;
	internal static ConfigEntry<int> BlockPowerFromLevel;
	internal static ConfigEntry<float> ParryXpBonus;
	internal static ConfigEntry<float> HeldBlockXpBonus;
	internal static ConfigEntry<float> StaminaRefundAt100;
	internal static ConfigEntry<int> StaminaRefundFromLevel;
	internal static ConfigEntry<BruceQoLPlugin.Toggle> StaminaRefundParries;
	internal static ConfigEntry<float> EquipTimeAt100;
	internal static ConfigEntry<int> EquipFromLevel;

	private const float PerfectBlockInterval = 0.25f;      // Humanoid.m_perfectBlockInterval
	private const float VanillaBlockPowerPerSkill = 0.5f;  // the 0.5 in GetBlockPower

	// Block power. The postfix sees base * (1 + skill * 0.5); recover base and re-apply our curve.
	// GetBlockPowerTooltip goes through the same overload, so the item tooltip shows the new value.
	[HarmonyPatch(typeof(ItemDrop.ItemData), nameof(ItemDrop.ItemData.GetBlockPower), typeof(int), typeof(float))]
	private static class BlockPowerPatch
	{
		private static void Postfix(float skillFactor, ref float __result)
		{
			if (BlockPowerEnabled == null || BlockPowerEnabled.Value != BruceQoLPlugin.Toggle.On) return;
			if (skillFactor * 100f < BlockPowerFromLevel.Value) return;
			float baseBlockPower = __result / (1f + skillFactor * VanillaBlockPowerPerSkill);
			float bonusAt100 = Mathf.Max(0f, BlockPowerAt100.Value) - 1f;
			__result = baseBlockPower * (1f + skillFactor * bonusAt100);
		}
	}

	// Extra Blocking XP and the stamina refund. Vanilla returns false before touching skills or
	// stamina when there is no blocker or the hit came from behind, so __result gates both.
	[HarmonyPatch(typeof(Humanoid), "BlockAttack")]
	private static class BlockAttackPatch
	{
		internal struct State
		{
			public Player Player;
			public bool Timed;
			public float StaminaBefore;
		}

		private static void Prefix(Humanoid __instance, out State __state)
		{
			__state = default;
			if (__instance is not Player player) return;
			ItemDrop.ItemData blocker = player.GetCurrentBlocker();
			if (blocker == null) return;
			__state.Player = player;
			// Same test BlockAttack uses to decide whether this is a timed (parry) block.
			__state.Timed = blocker.m_shared.m_timedBlockBonus > 1f && player.m_blockTimer != -1f && player.m_blockTimer < PerfectBlockInterval;
			__state.StaminaBefore = player.GetStamina();
		}

		private static void Postfix(bool __result, State __state)
		{
			Player player = __state.Player;
			if (player == null || !__result) return;

			float xp = __state.Timed ? (ParryXpBonus?.Value ?? 0f) : (HeldBlockXpBonus?.Value ?? 0f);
			if (xp > 0f) player.RaiseSkill(Skills.SkillType.Blocking, xp);

			float refund = Mathf.Clamp01(StaminaRefundAt100?.Value ?? 0f);
			if (refund <= 0f) return;
			if (__state.Timed && StaminaRefundParries.Value != BruceQoLPlugin.Toggle.On) return;
			float skillFactor = player.GetSkillFactor(Skills.SkillType.Blocking);
			if (skillFactor * 100f < StaminaRefundFromLevel.Value) return;
			float spent = __state.StaminaBefore - player.GetStamina();
			if (spent > 0f) player.AddStamina(spent * skillFactor * refund);
		}
	}

	// Equip speed. Vanilla appends the action to m_actionQueue with the item's m_equipDuration;
	// scale that entry by the item's own skill. Armour and tools have no skill and stay vanilla.
	[HarmonyPatch(typeof(Player), "QueueEquipAction")]
	private static class QueueEquipPatch
	{
		private static void Postfix(Player __instance, ItemDrop.ItemData item) => ScaleQueuedEquip(__instance, item);
	}

	[HarmonyPatch(typeof(Player), "QueueUnequipAction")]
	private static class QueueUnequipPatch
	{
		private static void Postfix(Player __instance, ItemDrop.ItemData item) => ScaleQueuedEquip(__instance, item);
	}

	private static void ScaleQueuedEquip(Player player, ItemDrop.ItemData item)
	{
		if (player == null || item == null || EquipTimeAt100 == null) return;
		float at100 = EquipTimeAt100.Value;
		if (at100 <= 0f || Mathf.Abs(at100 - 1f) < 0.001f) return;
		Skills.SkillType skill = item.m_shared.m_skillType;
		if (skill == Skills.SkillType.None) return;
		float skillFactor = player.GetSkillFactor(skill);
		if (skillFactor * 100f < EquipFromLevel.Value) return;
		List<Player.MinorActionData> queue = player.m_actionQueue;
		for (int i = queue.Count - 1; i >= 0; i--)
		{
			Player.MinorActionData action = queue[i];
			if (action.m_item != item) continue;
			if (action.m_type != Player.MinorActionData.ActionType.Equip && action.m_type != Player.MinorActionData.ActionType.Unequip) continue;
			action.m_duration *= Mathf.Lerp(1f, at100, skillFactor);
			return;
		}
	}
}
