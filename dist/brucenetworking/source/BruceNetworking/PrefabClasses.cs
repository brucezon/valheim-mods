using System.Collections.Generic;
using UnityEngine;

namespace BruceNetworking;

internal enum PrefabClass : byte
{
	Default,
	Player,
	Creature,
	Interactive,
	Dynamic,
	Structure,
	Nature,
}

// Maps a ZDO's prefab hash to a coarse class by inspecting the prefab's components once, then caches it.
internal static class PrefabClasses
{
	private struct Entry
	{
		public PrefabClass Class;
		public bool Vehicle;
	}

	private static readonly Dictionary<int, Entry> s_cache = new();

	internal static void ClearCache() => s_cache.Clear();

	internal static PrefabClass Classify(int prefabHash) => Resolve(prefabHash).Class;

	internal static bool IsVehicle(int prefabHash) => Resolve(prefabHash).Vehicle;

	internal static float Bias(int prefabHash)
	{
		return Classify(prefabHash) switch
		{
			PrefabClass.Player => BruceNetworkingPlugin.BiasPlayer.Value,
			PrefabClass.Creature => BruceNetworkingPlugin.BiasCreature.Value,
			PrefabClass.Interactive => BruceNetworkingPlugin.BiasInteractive.Value,
			PrefabClass.Dynamic => BruceNetworkingPlugin.BiasDynamic.Value,
			PrefabClass.Structure => BruceNetworkingPlugin.BiasStructure.Value,
			PrefabClass.Nature => BruceNetworkingPlugin.BiasNature.Value,
			_ => 0f,
		};
	}

	private static Entry Resolve(int prefabHash)
	{
		if (s_cache.TryGetValue(prefabHash, out Entry entry)) return entry;
		entry = default;
		GameObject prefab = ZNetScene.instance != null ? ZNetScene.instance.GetPrefab(prefabHash) : null;
		if (prefab != null)
		{
			entry.Vehicle = prefab.GetComponent<Ship>() != null || prefab.GetComponent<Vagon>() != null;
			if (prefab.GetComponent<Player>() != null) entry.Class = PrefabClass.Player;
			else if (prefab.GetComponent<Character>() != null || prefab.GetComponent<BaseAI>() != null) entry.Class = PrefabClass.Creature;
			else if (prefab.GetComponent<Door>() != null || prefab.GetComponent<Container>() != null
				|| prefab.GetComponent<CookingStation>() != null || prefab.GetComponent<Smelter>() != null
				|| prefab.GetComponent<Fermenter>() != null || prefab.GetComponent<Fireplace>() != null
				|| prefab.GetComponent<Beehive>() != null || prefab.GetComponent<Bed>() != null
				|| prefab.GetComponent<CraftingStation>() != null || prefab.GetComponent<ItemStand>() != null
				|| prefab.GetComponent<Sign>() != null || prefab.GetComponent<Turret>() != null
				|| prefab.GetComponent<Windmill>() != null) entry.Class = PrefabClass.Interactive;
			else if (entry.Vehicle || prefab.GetComponent<Projectile>() != null
				|| prefab.GetComponent<ItemDrop>() != null || prefab.GetComponent<Fish>() != null) entry.Class = PrefabClass.Dynamic;
			else if (prefab.GetComponent<Plant>() != null || prefab.GetComponent<Pickable>() != null
				|| prefab.GetComponent<TreeBase>() != null || prefab.GetComponent<TreeLog>() != null
				|| prefab.GetComponent<Destructible>() != null || prefab.GetComponent<MineRock>() != null
				|| prefab.GetComponent<MineRock5>() != null) entry.Class = PrefabClass.Nature;
			else if (prefab.GetComponent<WearNTear>() != null || prefab.GetComponent<Piece>() != null) entry.Class = PrefabClass.Structure;
			s_cache[prefabHash] = entry; // only cache real lookups; an unknown hash may resolve later
		}
		return entry;
	}
}
