using System.Collections.Generic;
using HarmonyLib;

namespace BruceNetworking;

// Throttles WearNTear.UpdateWear for pieces the SERVER owns and that cannot change state right now:
// older than 30 s, full health, dry (no rain wear pending), not Ashlands, not Deep North (snow buildup
// ticks there). Vanilla runs the full body, including the physics overlap in UpdateSupport, every time
// the round-robin updater reaches the piece. We let it through once per `Wear interval` instead of
// skipping outright (Fires' approach), so a piece that loses its foundation still collapses, just up
// to one interval later. Only affects server-owned pieces: the spawn area under our ownership rules,
// or everything when Serverside Simulations is on.
[HarmonyPatch(typeof(WearNTear), nameof(WearNTear.UpdateWear))]
internal static class UpdateWear_Patch
{
	internal static long Skipped;
	private static readonly Dictionary<int, float> s_lastFull = new();
	private static float s_lastPrune;

	private static bool Prefix(WearNTear __instance, float time)
	{
		if (!BruceNetworkingPlugin.WearThrottleEnabled.Value || !BruceNetworkingPlugin.IsServer)
		{
			return true;
		}
		ZNetView nview = __instance.m_nview;
		if (nview == null || !nview.IsValid() || !nview.IsOwner())
		{
			return true;
		}
		if (__instance.m_createTime < 0f || time - __instance.m_createTime <= 30f)
		{
			return true;
		}
		if (__instance.m_inAshlands || __instance.m_rainWet || __instance.m_addPreSnow || EnvMan.IsWet())
		{
			return true;
		}
		if (__instance.m_biome == Heightmap.Biome.DeepNorth)
		{
			return true;
		}
		float health = __instance.m_health;
		if (nview.GetZDO().GetFloat(ZDOVars.s_health, health) < health)
		{
			return true;
		}

		float interval = BruceNetworkingPlugin.WearInterval.Value;
		int id = __instance.GetInstanceID();
		if (s_lastFull.TryGetValue(id, out float last) && time - last < interval)
		{
			Skipped++;
			return false;
		}
		s_lastFull[id] = time;

		if (time - s_lastPrune > 120f)
		{
			s_lastPrune = time;
			List<int> stale = new();
			foreach (KeyValuePair<int, float> kv in s_lastFull)
			{
				if (time - kv.Value > interval * 2f) stale.Add(kv.Key);
			}
			foreach (int k in stale) s_lastFull.Remove(k);
		}
		return true;
	}
}
