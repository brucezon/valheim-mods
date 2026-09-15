using System;
using BepInEx.Configuration;
using HarmonyLib;

namespace BruceQoL;

// 18 - Hoe: radius, raise step and stone cost of the hoe and cultivator operations.
//
// Every hoe / cultivator action is a TerrainOp piece (mud_road = level ground, raise, path, paved_road,
// cultivate, replant; the pickaxe's digg is one too and is deliberately left alone). Its numbers - the
// 2 m radius, the raise step - live in the piece prefab's Settings and are deep-copied into each placed
// instance, whose Awake applies the operation and destroys itself. Scaling the instance's settings in an
// Awake prefix therefore changes that one operation and nothing else. The placement ghost is created
// with TerrainOp.m_forceDisableTerrainOps set, so it is skipped the same way vanilla skips it.
//
// Levelling to the aim point instead of your feet already exists in vanilla: hold the alt-place key
// (Left Alt by default) while levelling. Not duplicated here.
internal static class Hoe
{
	internal static ConfigEntry<float> RadiusMult;
	internal static ConfigEntry<float> RaiseStepMult;
	internal static ConfigEntry<BruceQoLPlugin.Toggle> RaiseCostsStone;

	private static readonly string[] HoePrefixes = { "mud_road", "raise", "path", "paved_road", "cultivate", "replant" };

	private static bool IsHoeOp(string prefabName)
	{
		foreach (string p in HoePrefixes)
		{
			if (prefabName.StartsWith(p, StringComparison.OrdinalIgnoreCase)) return true;
		}
		return false;
	}

	private static bool IsRaise(string prefabName) => prefabName.StartsWith("raise", StringComparison.OrdinalIgnoreCase);

	[HarmonyPatch(typeof(TerrainOp), "Awake")]
	private static class TerrainOpPatch
	{
		private static void Prefix(TerrainOp __instance)
		{
			if (TerrainOp.m_forceDisableTerrainOps || __instance.m_settings == null) return;
			string name = Utils.GetPrefabName(__instance.gameObject);
			if (!IsHoeOp(name)) return;
			TerrainOp.Settings s = __instance.m_settings;
			float r = RadiusMult != null ? Math.Max(0.1f, RadiusMult.Value) : 1f;
			if (Math.Abs(r - 1f) > 0.001f)
			{
				s.m_levelRadius *= r;
				s.m_raiseRadius *= r;
				s.m_smoothRadius *= r;
				s.m_paintRadius *= r;
			}
			if (IsRaise(name) && RaiseStepMult != null && Math.Abs(RaiseStepMult.Value - 1f) > 0.001f)
			{
				s.m_raiseDelta *= Math.Max(0.05f, RaiseStepMult.Value);
			}
		}
	}

	private static bool FreeRaise(Piece piece) => piece != null && RaiseCostsStone != null && RaiseCostsStone.Value == BruceQoLPlugin.Toggle.Off && IsRaise(Utils.GetPrefabName(piece.gameObject));

	[HarmonyPatch(typeof(Player), nameof(Player.HaveRequirements), typeof(Piece), typeof(Player.RequirementMode))]
	private static class RaiseRequirementPatch
	{
		private static bool Prefix(Piece piece, ref bool __result)
		{
			if (!FreeRaise(piece)) return true;
			__result = true;
			return false;
		}
	}

	// Vanilla consumes the selected piece's requirement array right after placing it; the raise piece is
	// still the selected piece at that moment, so the array reference identifies it.
	[HarmonyPatch(typeof(Player), nameof(Player.ConsumeResources))]
	private static class RaiseConsumePatch
	{
		private static bool Prefix(Player __instance, Piece.Requirement[] requirements)
		{
			Piece selected = __instance.m_buildPieces != null ? __instance.m_buildPieces.GetSelectedPiece() : null;
			if (selected == null || !ReferenceEquals(requirements, selected.m_resources)) return true;
			return !FreeRaise(selected);
		}
	}
}
