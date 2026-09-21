using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace RaidBoss;

// What has to run on a player's own game. Three small patches; all decisions are still the server's.
internal static class ClientSide
{
	static readonly int AddTag = "raidboss_add".GetStableHashCode();
	static readonly int EncTag = "raidboss_enc".GetStableHashCode();
	static readonly int DamageKey = "raidboss_dmg".GetStableHashCode();

	// ---- 1. Damage a player takes from a boss in a fight, or from an add.
	//
	// Character.RPC_Damage runs on the machine that owns the VICTIM, which for a player is that player's own game.
	// HitData.m_attacker is the attacker's ZDOID - set for melee, projectiles and area attacks alike. A boss in a fight
	// the server announced hits for the announced multiplier (Net.cs); an add carries its multiplier on its own ZDO,
	// written by the server when it created it. Applied before vanilla's armour and block maths. Everything else -
	// wild creatures, tames, structures, other mods' multipliers - is untouched, and the two never apply to one hit.
	[HarmonyPatch(typeof(Character), "RPC_Damage")]
	static class DamagePatch
	{
		static void Prefix(Character __instance, HitData hit)
		{
			if (hit == null || !__instance.IsPlayer() || hit.m_attacker.IsNone()) return;
			if (!RaidBossPlugin.IsOn || __instance.m_nview == null || !__instance.m_nview.IsOwner() || ZDOMan.instance == null) return;
			if (Net.TryGetBossDamage(hit.m_attacker, out float mult))
			{
				if (!Mathf.Approximately(mult, 1f)) hit.ApplyModifier(mult);
				return;
			}
			ZDO attacker = ZDOMan.instance.GetZDO(hit.m_attacker);
			if (attacker == null) return;
			mult = attacker.GetFloat(DamageKey, 1f);
			if (mult > 0f && !Mathf.Approximately(mult, 1f)) hit.ApplyModifier(Mathf.Clamp(mult, 0.05f, 3f));
		}
	}

	// ---- 2. Exact kills.
	//
	// Character.OnDeath runs once, on the creature's owner. (Player overrides it, so players never come through here.)
	[HarmonyPatch(typeof(Character), "OnDeath")]
	static class DeathPatch
	{
		static void Prefix(Character __instance)
		{
			if (__instance.IsPlayer() || __instance.IsTamed()) return;
			ZNetView view = __instance.m_nview;
			if (view == null || !view.IsValid() || !view.IsOwner()) return;
			ZDO zdo = view.GetZDO();
			Net.ReportKill(zdo.m_uid, zdo.GetPrefab(), __instance.transform.position, zdo.GetBool(AddTag) || zdo.GetBool(EncTag));
		}
	}

	// ---- 3. A shorter fireside wait for Rested after a death.
	//
	// By a fire, under shelter and unnoticed, the player has the "Resting" status: an SE_Cozy that counts m_time up from
	// zero and, past m_delay, adds Rested. Anything that breaks the conditions removes Resting and the count starts
	// over. Player.m_timeSinceDeath is the game's own "died recently" clock (it drives the no-skill-drain grace period):
	// zeroed in OnDeath, advanced while alive, saved with the character. While it is inside the window, Rested arrives
	// at m_delay x multiplier. Food, the tombstone and the length of Rested are untouched.
	static bool loggedWait;

	[HarmonyPatch(typeof(SE_Cozy), nameof(SE_Cozy.UpdateStatusEffect))]
	static class CozyPatch
	{
		static void Postfix(SE_Cozy __instance)
		{
			if (!RaidBossPlugin.IsOn) return;
			Player player = __instance.m_character as Player;
			if (player == null || player != Player.m_localPlayer) return;
			float mult = Mathf.Clamp01(RaidBossPlugin.RestingTimeAfterDeath.Value);
			if (!loggedWait)
			{
				loggedWait = true;
				RaidBossPlugin.Log.LogInfo($"the game's fireside wait is {__instance.m_delay:0.#} s; for {RaidBossPlugin.JustDiedWindow.Value:0} s after a death it is {__instance.m_delay * mult:0.#} s");
			}
			if (mult >= 1f || player.m_timeSinceDeath > RaidBossPlugin.JustDiedWindow.Value) return;
			if (__instance.m_time > __instance.m_delay || __instance.m_time < __instance.m_delay * mult) return;
			player.GetSEMan().AddStatusEffect(__instance.m_statusEffectHash, resetTime: true, 0, 0f, -1);
		}
	}
}
