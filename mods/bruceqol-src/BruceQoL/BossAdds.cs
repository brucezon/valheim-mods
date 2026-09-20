using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace BruceQoL;

// 22 - Boss adds: honour the damage multiplier BossDirector writes on the creatures it spawns.
//
// BossDirector is server-only and cannot change what a creature's hit does to a player: Character.RPC_Damage runs
// on the machine that owns the VICTIM, which for a player is that player's own game. What the server can do is
// write a float on the add's ZDO when it creates it ("bossdirector_dmg", next to its "bossdirector_add" tag), and
// ZDO data reaches every client. HitData.m_attacker is the attacker's ZDOID - set for melee, projectiles and area
// attacks alike - so the victim's game looks the attacker up and scales the hit before vanilla's armour and block
// maths see it. The number lives in BossDirector's config, per server, tunable live; nothing here needs a release
// to retune. No BossDirector, or an older one: no creature carries the float and nothing changes.
//
// Only hits on players are scaled (not on tames, structures or boats), and only by creatures carrying the float,
// so bosses and everything the world spawns are untouched. A player without this patch takes full damage.
internal static class BossAdds
{
	internal static ConfigEntry<BruceQoLPlugin.Toggle> HonourScaling;

	private static readonly int DamageKey = "bossdirector_dmg".GetStableHashCode();
	// 1.19.1: our side of the handshake. Written on the local player's own character while the scaling is on, so a
	// BossDirector can see, fight by fight, that every engaged player takes reduced damage before it sends bigger waves.
	// A player's ZDO is recreated on every spawn, so it is written in OnSpawned, and again whenever the toggle changes
	// (which includes the moment the server's synced value arrives).
	private static readonly int MarkerKey = "bruceqol_adddmg".GetStableHashCode();

	internal static void WriteMarker()
	{
		Player player = Player.m_localPlayer;
		if (player == null || player.m_nview == null || !player.m_nview.IsValid() || !player.m_nview.IsOwner()) return;
		bool on = HonourScaling != null && HonourScaling.Value == BruceQoLPlugin.Toggle.On;
		player.m_nview.GetZDO().Set(MarkerKey, on ? 1 : 0);
	}

	[HarmonyPatch(typeof(Player), nameof(Player.OnSpawned))]
	private static class SpawnedPatch
	{
		private static void Postfix() => WriteMarker();
	}

	[HarmonyPatch(typeof(Character), "RPC_Damage")]
	private static class DamagePatch
	{
		private static void Prefix(Character __instance, HitData hit)
		{
			if (HonourScaling == null || HonourScaling.Value != BruceQoLPlugin.Toggle.On) return;
			if (hit == null || !__instance.IsPlayer() || hit.m_attacker.IsNone()) return;
			if (__instance.m_nview == null || !__instance.m_nview.IsOwner() || ZDOMan.instance == null) return;
			ZDO attacker = ZDOMan.instance.GetZDO(hit.m_attacker);
			if (attacker == null) return;
			float mult = attacker.GetFloat(DamageKey, 1f);
			if (mult <= 0f || Mathf.Approximately(mult, 1f)) return;
			hit.ApplyModifier(Mathf.Clamp(mult, 0.05f, 3f));
		}
	}
}
