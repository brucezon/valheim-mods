using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using BepInEx.Configuration;
using HarmonyLib;

namespace BruceQoL;

// 17 - Terrain: how far a pickaxe or hoe may move the ground from its original height.
//
// Vanilla clamps every terrain edit to +/- 8 m of the world's generated height, as a literal 8f (and
// 8.0 in the legacy heightmap path) at four sites: TerrainComp.LevelTerrain and RaiseTerrain, where
// the per-vertex delta is stored, TerrainComp.ApplyToHeightmap, where the stored deltas are re-clamped
// when the heightmap is rebuilt, and Heightmap.LevelTerrain for pre-terrain-compiler worlds. The
// const Heightmap.c_LevelMaxDelta exists but nothing reads it. A transpiler swaps every 8 / -8 float
// constant in those four methods for the configured limit.
//
// The value is server-synced and the mod is required on every client, which matters here: the
// re-clamp in ApplyToHeightmap runs on every client that renders the terrain, so a client with a
// different limit would flatten a deep pit back to its own limit on screen.
internal static class DigDepth
{
	internal static ConfigEntry<float> Limit;

	private const float Vanilla = 8f;

	public static float Max() => Limit != null && Limit.Value > 0f ? Limit.Value : Vanilla;
	public static float Min() => -Max();
	public static double MaxD() => Max();

	[HarmonyPatch]
	private static class ClampPatch
	{
		private static IEnumerable<MethodBase> TargetMethods()
		{
			yield return AccessTools.Method(typeof(TerrainComp), "LevelTerrain");
			yield return AccessTools.Method(typeof(TerrainComp), "RaiseTerrain");
			yield return AccessTools.Method(typeof(TerrainComp), "ApplyToHeightmap");
			yield return AccessTools.Method(typeof(Heightmap), "LevelTerrain");
		}

		private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
		{
			MethodInfo max = AccessTools.Method(typeof(DigDepth), nameof(Max));
			MethodInfo min = AccessTools.Method(typeof(DigDepth), nameof(Min));
			MethodInfo maxD = AccessTools.Method(typeof(DigDepth), nameof(MaxD));
			foreach (CodeInstruction ci in instructions)
			{
				if (ci.opcode == OpCodes.Ldc_R4 && ci.operand is float f)
				{
					if (f == Vanilla) { yield return new CodeInstruction(OpCodes.Call, max).WithLabels(ci.labels).WithBlocks(ci.blocks); continue; }
					if (f == -Vanilla) { yield return new CodeInstruction(OpCodes.Call, min).WithLabels(ci.labels).WithBlocks(ci.blocks); continue; }
				}
				if (ci.opcode == OpCodes.Ldc_R8 && ci.operand is double d && d == Vanilla)
				{
					yield return new CodeInstruction(OpCodes.Call, maxD).WithLabels(ci.labels).WithBlocks(ci.blocks);
					continue;
				}
				yield return ci;
			}
		}
	}
}
