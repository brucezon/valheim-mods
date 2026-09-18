using System;
using System.Collections.Generic;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace BruceQoL;

// 19 - Stars: star chance grows with the world's progress.
//
// Vanilla rolls a chain of 10% checks per spawn (10% one star, 1% two stars), the same on day one as
// after every boss is dead. This scales that chance by progress: multiplier = 1 + PerStep x steps, capped.
// "steps" depends on the scope:
//   BiomeProgress (default): bosses defeated minus the tier of the biome the creature spawns in, never
//     below 0. Meadows (tier 0) with two bosses down is two steps; the Swamp (tier 2) is still vanilla until
//     Bonemass falls. The biome you are fighting through is never harder than vanilla; the ones behind you
//     stop being safe. Same idea as Creature Level and Loot Control's "Fluid" world level.
//   Global: bosses defeated, everywhere, the current biome included.
//
// Separate two-star chance (1.17.0). Vanilla rolls the SAME chance once per star, so two stars are always
// the square of one star (10% -> 1%, 20% -> 4%) and no single number can make two-stars common without
// making one-stars ubiquitous. The roll sits in three small loops - SpawnSystem.Spawn (world spawns, raids),
// SpawnArea.SpawnOne (spawner piles), CreatureSpawner.Spawn (fixed spawners, dungeons) - each of which ends in
// Character.SetLevel(i) when i > 1. Rather than transpile three loops, vanilla still decides "starred or not"
// and a SetLevel prefix, armed only while one of those three methods is running, re-decides the SECOND star
// with its own chance: P(two stars | starred) = twoStarTarget / oneStarChance, so the absolute two-star rate
// is exactly the configured target (never above the one-star chance). Breeding, the spawn console command
// and creatures loaded from a save never pass through those methods and are untouched. A spawn whose
// minimum level already forces stars, or whose maximum level cannot reach two stars, is left to vanilla.
//
// Hooks the two vanilla chance getters: SpawnSystem.GetLevelUpChance (world spawns, creature spawners,
// raids) and SpawnArea.GetLevelUpChance (spawner piles). Boss progress is read from the world's global
// keys, which every client has, and re-read every few seconds. The roll runs on whichever client owns the
// zone, so every entry is server-synced and the mod being required on clients is what keeps it consistent.
internal static class Stars
{
	internal enum Scope
	{
		BiomeProgress = 0,
		Global = 1,
	}

	internal static ConfigEntry<BruceQoLPlugin.Toggle> Enabled;
	internal static ConfigEntry<Scope> StepScope;
	internal static ConfigEntry<float> PerStep;
	internal static ConfigEntry<float> MaxMult;
	internal static ConfigEntry<string> BossKeys;
	internal static ConfigEntry<BruceQoLPlugin.Toggle> SeparateTwoStar;
	internal static ConfigEntry<float> TwoStarBase;
	internal static ConfigEntry<float> TwoStarPerStep;
	internal static ConfigEntry<float> TwoStarMax;

	private static int bossesDefeated;
	private static float nextKeyRead;

	// Set while one of the three vanilla spawn methods is on the stack.
	private static int rollDepth;
	private static int ctxMinLevel, ctxMaxLevel;
	private static float ctxChance;

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

	private static bool On => Enabled != null && Enabled.Value == BruceQoLPlugin.Toggle.On && WorldGenerator.instance != null;

	// How far progress has run ahead, for a creature spawning at this position.
	internal static int Steps(Vector3 position)
	{
		int bosses = CountBosses();
		if (StepScope != null && StepScope.Value == Scope.Global) return bosses;
		return Math.Max(0, bosses - Tier(WorldGenerator.instance.GetBiome(position)));
	}

	internal static float Multiplier(Vector3 position)
	{
		if (!On) return 1f;
		int over = Steps(position);
		if (over <= 0) return 1f;
		float m = 1f + Math.Max(0f, PerStep.Value) * over;
		return Mathf.Min(m, Math.Max(1f, MaxMult.Value));
	}

	// The absolute two-star chance (percent) this position should have, before it is limited by the one-star chance.
	internal static float TwoStarTarget(Vector3 position)
	{
		float t = Math.Max(0f, TwoStarBase.Value) + Math.Max(0f, TwoStarPerStep.Value) * Steps(position);
		return Mathf.Min(t, Math.Max(0f, TwoStarMax.Value));
	}

	private static float Apply(float chance, Vector3 position)
	{
		float m = Multiplier(position);
		float result = m > 1f ? Mathf.Min(chance * m, 95f) : chance;
		if (rollDepth > 0) ctxChance = result;
		return result;
	}

	// The second-star decision. level is what vanilla's chain of equal rolls produced (>= 2 here).
	internal static int Redecide(int level, float oneStarChance, float twoStarTarget, float roll01)
	{
		if (oneStarChance <= 0f) return level;
		float conditional = Mathf.Clamp01(twoStarTarget / oneStarChance);
		return roll01 < conditional ? Math.Max(level, 3) : 2;
	}

	private static void Begin(int minLevel, int maxLevel)
	{
		if (rollDepth++ == 0)
		{
			ctxMinLevel = minLevel;
			ctxMaxLevel = maxLevel;
			ctxChance = 0f;
		}
	}

	private static void End()
	{
		if (rollDepth > 0) rollDepth--;
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

	// The three methods that hold vanilla's roll. Finalizers, so the context always closes.
	[HarmonyPatch(typeof(SpawnSystem), "Spawn")]
	private static class SpawnSystemSpawnPatch
	{
		private static void Prefix(SpawnSystem.SpawnData critter) => Begin(critter != null ? critter.m_minLevel : 1, critter != null ? critter.m_maxLevel : 1);
		private static void Finalizer() => End();
	}

	[HarmonyPatch(typeof(SpawnArea), "SpawnOne")]
	private static class SpawnAreaSpawnOnePatch
	{
		// The entry is picked inside the method; use the widest range the pile can produce. Piles list
		// variants of one creature family with the same level range, so this is exact in practice.
		private static void Prefix(SpawnArea __instance)
		{
			int min = int.MaxValue, max = 1;
			if (__instance.m_prefabs != null)
			{
				foreach (SpawnArea.SpawnData d in __instance.m_prefabs)
				{
					if (d == null) continue;
					min = Math.Min(min, d.m_minLevel);
					max = Math.Max(max, d.m_maxLevel);
				}
			}
			Begin(min == int.MaxValue ? 1 : min, max);
		}

		private static void Finalizer() => End();
	}

	[HarmonyPatch(typeof(CreatureSpawner), "Spawn")]
	private static class CreatureSpawnerSpawnPatch
	{
		// Same location overrides vanilla applies before its loop.
		private static void Prefix(CreatureSpawner __instance)
		{
			int min = __instance.m_minLevel, max = __instance.m_maxLevel;
			Location loc = __instance.m_location;
			if (loc != null && (loc.m_excludeEnemyLevelOverrideGroups == null || !loc.m_excludeEnemyLevelOverrideGroups.Contains(__instance.m_spawnGroupID)))
			{
				if (loc.m_enemyMinLevelOverride >= 0) min = loc.m_enemyMinLevelOverride;
				if (loc.m_enemyMaxLevelOverride >= 0) max = loc.m_enemyMaxLevelOverride;
			}
			Begin(min, max);
		}

		private static void Finalizer() => End();
	}

	[HarmonyPatch(typeof(Character), nameof(Character.SetLevel))]
	private static class SetLevelPatch
	{
		private static void Prefix(Character __instance, ref int level)
		{
			if (rollDepth <= 0 || level < 2 || !On) return;
			if (SeparateTwoStar == null || SeparateTwoStar.Value != BruceQoLPlugin.Toggle.On) return;
			if (ctxMinLevel >= 2 || ctxMaxLevel < 3 || ctxChance <= 0f) return;
			level = Redecide(level, ctxChance, TwoStarTarget(__instance.transform.position), UnityEngine.Random.value);
		}
	}
}
