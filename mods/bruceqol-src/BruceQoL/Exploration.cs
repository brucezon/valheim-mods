using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace BruceQoL;

// 20 - Exploration: how much of the map is revealed around the player.
//
// Vanilla (1.0.15): Minimap.UpdateExplore runs every m_exploreInterval (2 s) and calls the private
// Minimap.Explore(Vector3 p, float radius) with the player's position and the Minimap's m_exploreRadius.
// That call is the method's only caller with a radius - ExploreAll, map loading and the cartography
// table all go through the per-pixel Explore(int, int) overload - so scaling the radius argument here
// changes the walking reveal and nothing else. The work is a square of (2r / pixel size)^2 pixel checks
// every two seconds: a few hundred at vanilla, a few thousand at x3, which is nothing.
//
// Two multipliers rather than one, because the sea is where a wider reveal earns its keep: a coastline
// slides past at the edge of vision while the land reveal is rarely what limits you. Aboard = inside a
// ship's player trigger, the same test vanilla uses for Ship.GetLocalShip. No new skill: Smoothbrain's
// Exploration ties the radius to one, and that is a different (and much larger) feature.
// Client-side effect (each player's own map); the entries are server-synced like the rest.
internal static class Exploration
{
	internal static ConfigEntry<float> RadiusMult;
	internal static ConfigEntry<float> ShipRadiusMult;

	private static bool logged;
	private static readonly BepInEx.Logging.ManualLogSource s_log = BepInEx.Logging.Logger.CreateLogSource("BruceQoL");

	[HarmonyPatch(typeof(Minimap), "Explore", typeof(Vector3), typeof(float))]
	private static class ExplorePatch
	{
		private static void Prefix(ref float radius)
		{
			if (RadiusMult == null) return;
			bool aboard = Ship.GetLocalShip() != null;
			float mult = Mathf.Clamp(aboard ? ShipRadiusMult.Value : RadiusMult.Value, 0.1f, 10f);
			if (!logged)
			{
				logged = true;
				s_log.LogInfo($"BruceQoL exploration: the game's map reveal radius is {radius:F0} m; on foot x{RadiusMult.Value:0.##}, aboard a ship x{ShipRadiusMult.Value:0.##}.");
			}
			if (Mathf.Abs(mult - 1f) > 0.001f) radius *= mult;
		}
	}
}
