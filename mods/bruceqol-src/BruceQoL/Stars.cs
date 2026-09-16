using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace BruceQoL;

// 19 - Stars: star chance grows in biomes the group has out-levelled.
//
// Vanilla rolls a chain of 10% checks per spawn (10% one star, 1% two stars), the same on day one as
// after every boss is dead. This scales that chance by how far the world's progress has run past the
// biome the creature spawns in: multiplier = 1 + PerStep x max(0, bossesDefeated - biomeTier), capped.
// Meadows (tier 0) with two bosses down gets x2 at the default 0.5 - 20% one star, 4% two stars -
// while the Swamp (tier 2) is still vanilla until Bonemass falls. The biome you are currently fighting
// through is never made harder than vanilla; the ones behind you stop being safe. Same idea as
// Creature Level and Loot Control's "Fluid" world level, on vanilla's own roll and star cap.
//
// Hooks the two vanilla chance getters: SpawnSystem.GetLevelUpChance (world spawns, creature spawners,
// raids) and SpawnArea.GetLevelUpChance (spawner piles). Boss progress is read from the world's global
// keys, which every client has, and re-read every few seconds.
internal static class Stars
{
	internal static ConfigEntry<BruceQoLPlugin.Toggle> Enabled;
	internal static ConfigEntry<float> PerStep;
	internal static ConfigEntry<float> MaxMult;
	internal static ConfigEntry<string> BossKeys;

	private static int bossesDefeated;
	private static float nextKeyRead;

	private static int Tier(Heightmap.Biome biome) => biome switch
	{
		Heightmap.Biome.Meadows => 0,
		Heightmap.Biome.BlackForest => 1,
		Heightmap.Biome.Ocean => 1,
		Heightmap.Biome.Swamp => 2,
		Heightmap.Biome.Mountain => 3,
		Heightmap.Biome.Plains => 4,
		Heightmap.Biome.Mistlands => 5,
		Heightmap.Biome.AshLands => 6,
		Heightmap.Biome.DeepNorth => 7,
		_ => 0,
	};

	private static int CountBosses()
	{
		if (Time.time < nextKeyRead) return bossesDefeated;
		nextKeyRead = Time.time + 5f;
		int n = 0;
		if (ZoneSystem.instance != null)
		{
			foreach (string part in (BossKeys.Value ?? "").Split(',', ';'))
			{
				string key = part.Trim();
				if (key.Length > 0 && ZoneSystem.instance.GetGlobalKey(key)) n++;
			}
		}
		bossesDefeated = n;
		return n;
	}

	internal static float Multiplier(Vector3 position)
	{
		if (Enabled == null || Enabled.Value != BruceQoLPlugin.Toggle.On || WorldGenerator.instance == null) return 1f;
		int over = CountBosses() - Tier(WorldGenerator.instance.GetBiome(position));
		if (over <= 0) return 1f;
		float m = 1f + Math.Max(0f, PerStep.Value) * over;
		return Mathf.Min(m, Math.Max(1f, MaxMult.Value));
	}

	private static float Apply(float chance, Vector3 position)
	{
		float m = Multiplier(position);
		return m > 1f ? Mathf.Min(chance * m, 95f) : chance;
	}

	[HarmonyPatch(typeof(SpawnSystem), nameof(SpawnSystem.GetLevelUpChance), typeof(Vector3), typeof(float))]
	private static class SpawnSystemChancePatch
	{
		private static void Postfix(Vector3 position, ref float __result) => __result = Apply(__result, position);
	}

	[HarmonyPatch(typeof(SpawnArea), nameof(SpawnArea.GetLevelUpChance))]
	private static class SpawnAreaChancePatch
	{
		private static void Postfix(SpawnArea __instance, ref float __result) => __result = Apply(__result, __instance.transform.position);
	}
}
