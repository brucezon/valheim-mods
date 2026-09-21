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

	// ---- 3. Asking for a heroic fight at the altar: Shift + Use.
	//
	// Every boss altar is an OfferingBowl, and Interact is called for it with alt = true when Shift is held - for the
	// offer-an-item altars too, where vanilla's own Interact does nothing. The challenge is stored ON THE ALTAR (a bool
	// on its ZDO, written by this client after claiming the object), so it survives a server restart, every player sees
	// it in the hover text, and the server reads it when the boss appears (Director.TakeAltarChallenge) and clears it.
	// Free by default, so a group can take the heroic fight the first time it meets a boss; a server can make it cost one
	// of the boss's own trophies from the player's inventory (then a boss has to be beaten normally once first).
	internal static readonly int AltarKey = "raidboss_heroic".GetStableHashCode();

	static ItemDrop.ItemData.SharedData TrophyOf(OfferingBowl altar)
	{
		CharacterDrop drops = altar.m_bossPrefab != null ? altar.m_bossPrefab.GetComponent<CharacterDrop>() : null;
		if (drops == null) return null;
		foreach (CharacterDrop.Drop d in drops.m_drops)
			if (d.m_prefab != null && d.m_prefab.name.StartsWith("Trophy", System.StringComparison.Ordinal))
				return d.m_prefab.GetComponent<ItemDrop>()?.m_itemData.m_shared;
		return null;
	}

	static bool Armed(OfferingBowl altar) => altar.m_nview != null && altar.m_nview.IsValid() && altar.m_nview.GetZDO().GetBool(AltarKey);

	[HarmonyPatch(typeof(OfferingBowl), nameof(OfferingBowl.Interact))]
	static class AltarInteractPatch
	{
		static bool Prefix(OfferingBowl __instance, Humanoid user, bool hold, bool alt, ref bool __result)
		{
			if (!alt || hold || !RaidBossPlugin.IsOn || !RaidBossPlugin.HeroicEnabled.Value) return true;
			__result = false;
			if (__instance.m_nview == null || !__instance.m_nview.IsValid() || user == null) return false;
			if (Armed(__instance))
			{
				user.Message(MessageHud.MessageType.Center, "The challenge is already set");
				return false;
			}
			if (RaidBossPlugin.HeroicCostsTrophy.Value)
			{
				ItemDrop.ItemData.SharedData trophy = TrophyOf(__instance);
				if (trophy != null)
				{
					if (user.GetInventory().CountItems(trophy.m_name) < 1)
					{
						user.Message(MessageHud.MessageType.Center, "$msg_donthaveany " + trophy.m_name);
						return false;
					}
					user.GetInventory().RemoveItem(trophy.m_name, 1);
				}
			}
			__instance.m_nview.ClaimOwnership();
			__instance.m_nview.GetZDO().Set(AltarKey, true);
			user.Message(MessageHud.MessageType.Center, "The challenge is set");
			__result = true;
			return false;
		}
	}

	[HarmonyPatch(typeof(OfferingBowl), nameof(OfferingBowl.GetHoverText))]
	static class AltarHoverPatch
	{
		static void Postfix(OfferingBowl __instance, ref string __result)
		{
			if (!RaidBossPlugin.IsOn || !RaidBossPlugin.HeroicEnabled.Value || string.IsNullOrEmpty(__result)) return;
			string line;
			if (Armed(__instance)) line = "<color=orange>Heroic fight: the challenge is set</color>";
			else
			{
				ItemDrop.ItemData.SharedData trophy = RaidBossPlugin.HeroicCostsTrophy.Value ? TrophyOf(__instance) : null;
				line = "[<color=yellow><b>$KEY_AltPlace + $KEY_Use</b></color>] Heroic fight" + (trophy != null ? " (1 " + trophy.m_name + ")" : "");
			}
			__result += "\n" + Localization.instance.Localize(line);
		}
	}

	// ---- 4. A shorter fireside wait for Rested after a death.
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
	// ---- 5. Idols fall.
	//
	// The server drops a heroic kill's idols where the boss fell (Director.SpawnItem), flagged "raidboss_loot". An item
	// that lands on the boss's body settles and the physics puts it to sleep; when the body is gone a sleeping item does
	// not notice and is left hanging in the air. So for its first half minute the game simulating a flagged item keeps
	// waking it up, and it drops to the ground when there is nothing under it any more.
	static readonly int LootKey = "raidboss_loot".GetStableHashCode();

	[HarmonyPatch(typeof(ItemDrop), "Awake")]
	static class LootPatch
	{
		static void Postfix(ItemDrop __instance)
		{
			ZNetView view = __instance.m_nview;
			if (view == null || !view.IsValid() || !view.GetZDO().GetBool(LootKey)) return;
			if (__instance.GetComponent<Rigidbody>() == null || __instance.gameObject.GetComponent<LootWaker>() != null) return;
			__instance.gameObject.AddComponent<LootWaker>();
		}
	}

	sealed class LootWaker : MonoBehaviour
	{
		float age, next;
		Rigidbody body;
		ZNetView view;

		void Awake() { body = GetComponent<Rigidbody>(); view = GetComponent<ZNetView>(); }

		void Update()
		{
			age += Time.deltaTime;
			if (age > 30f || body == null) { Destroy(this); return; }
			if (age < next) return;
			next = age + 0.25f;
			if (view != null && view.IsValid() && view.IsOwner() && !body.isKinematic && body.IsSleeping()) body.WakeUp();
		}
	}
}
