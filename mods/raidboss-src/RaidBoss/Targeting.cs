using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace RaidBoss;

// Who a boss and its adds go after. Vanilla has no threat at all: every two seconds a creature turns to the CLOSEST
// enemy it can sense (BaseAI.FindEnemy, called from MonsterAI.UpdateTarget). That runs on the creature's owner, a
// player's game, which is why this lives in the client half. Two rules, both only a second opinion on FindEnemy's
// answer; everything else about the AI is vanilla.
//
//   A parry is a taunt   a player who parries a boss's hit holds that boss for a few seconds. The player's own game
//                        sees the parry and writes "which boss, until when" on the player's ZDO (its own object, so
//                        it may); the boss's owner reads it from there.
//   The opening rush     a melee add's FIRST target is a player without a shield in hand - the one with the fewest adds
//                        sent at them so far, so a wave spreads over the back line. It keeps that target only until it
//                        comes within 4 m of any player (it arrived, or someone stepped in its way), its target is
//                        gone, or 20 s have passed. From then on the add is vanilla for good: closest player, so
//                        whoever intercepted it keeps it for as long as they stay the nearest. Archers, casters and
//                        flyers never rush (a server list).
internal static class Targeting
{
	static readonly int AddTag = "raidboss_add".GetStableHashCode();
	static readonly int RushTargetKey = "raidboss_tgt".GetStableHashCode();
	static readonly int RushStartKey = "raidboss_rush_t".GetStableHashCode();
	static readonly int RushOverKey = "raidboss_rushed".GetStableHashCode();
	static readonly int TauntUserKey = "raidboss_taunt_u".GetStableHashCode();
	static readonly int TauntIdKey = "raidboss_taunt_i".GetStableHashCode();
	static readonly int TauntUntilKey = "raidboss_taunt_t".GetStableHashCode();

	const float Reach = 60f;          // players further than this from the creature are not considered
	const float InYourFace = 4f;      // the rush ends when the add comes this near any player
	const float RushSeconds = 20f;    // and after this long at the latest

	static long NowTicks => ZNet.instance != null ? ZNet.instance.GetTime().Ticks : 0L;

	// ---- the parry, on the parrying player's own game

	[HarmonyPatch(typeof(Humanoid), "BlockAttack")]
	static class ParryPatch
	{
		// Vanilla's own test for a perfect block, read before BlockAttack runs.
		static void Prefix(Humanoid __instance, out bool __state)
		{
			__state = false;
			if (__instance != Player.m_localPlayer || !RaidBossPlugin.IsOn || RaidBossPlugin.ParryTaunt.Value <= 0f) return;
			ItemDrop.ItemData blocker = __instance.GetCurrentBlocker();
			__state = blocker != null && blocker.m_shared.m_timedBlockBonus > 1f && __instance.m_blockTimer != -1f && __instance.m_blockTimer < 0.25f;
		}

		static void Postfix(Humanoid __instance, Character attacker, bool __result, bool __state)
		{
			if (!__state || !__result || attacker == null || !attacker.IsBoss()) return;
			ZNetView mine = __instance.m_nview, theirs = attacker.m_nview;
			if (mine == null || !mine.IsValid() || !mine.IsOwner() || theirs == null || !theirs.IsValid()) return;
			ZDOID boss = theirs.GetZDO().m_uid;
			ZDO zdo = mine.GetZDO();
			zdo.Set(TauntUserKey, boss.UserID);
			zdo.Set(TauntIdKey, (long)boss.ID);
			zdo.Set(TauntUntilKey, NowTicks + (long)(RaidBossPlugin.ParryTaunt.Value * TimeSpan.TicksPerSecond));
		}
	}

	// ---- parry difficulty compensation for starred spawns, on the parrying player's own game
	//
	// In 1.0 a block is armour-style: what survives the block power feeds the player's stagger bar, and a parry fails
	// when the bar overflows. A three-star miniboss hits for 2.5x, so a parry that would hold against the plain creature
	// is broken by the stars alone. Here a TIMED block against a RaidBoss spawn is judged as if the creature were
	// unstarred (and, for a warband miniboss, unmultiplied): the hit is divided before BlockAttack and multiplied back
	// after, on the same HitData, so whatever leaks through - and every held block - still takes the full hit. The same
	// trick BruceQoL's Combat-slider compensation uses; the two compose.
	[HarmonyPatch(typeof(Humanoid), "BlockAttack")]
	static class StarParryPatch
	{
		static readonly int WarbossKey = "raidboss_warboss".GetStableHashCode();
		static readonly int AddTag = "raidboss_add".GetStableHashCode();

		static void Prefix(Humanoid __instance, HitData hit, Character attacker, out float __state)
		{
			__state = 1f;
			if (hit == null || attacker == null || attacker.IsPlayer() || __instance != Player.m_localPlayer || !RaidBossPlugin.IsOn) return;
			float c = Mathf.Clamp01(RaidBossPlugin.StarParryCompensation.Value);
			if (c <= 0f) return;
			ZNetView theirs = attacker.m_nview;
			if (theirs == null || !theirs.IsValid()) return;
			ZDO zdo = theirs.GetZDO();
			bool warboss = zdo.GetBool(WarbossKey);
			if (!warboss && !zdo.GetBool(AddTag)) return;
			ItemDrop.ItemData blocker = __instance.GetCurrentBlocker();
			if (blocker == null) return;
			bool timed = blocker.m_shared.m_timedBlockBonus > 1f && __instance.m_blockTimer != -1f && __instance.m_blockTimer < 0.25f;
			if (!timed) return;
			float factor = 1f + Mathf.Max(0, attacker.GetLevel() - 1) * 0.5f;   // the game's own level damage factor
			if (warboss && Net.TryGetBossDamage(zdo.m_uid, out float mult) && mult > 0f) factor *= mult;
			if (factor <= 1f) return;
			factor = Mathf.Pow(factor, c);
			hit.ApplyModifier(1f / factor);
			__state = factor;
		}

		static void Postfix(HitData hit, float __state)
		{
			if (hit != null && __state != 1f) hit.ApplyModifier(__state);
		}
	}

	// ---- the second opinion, on the creature's owner

	[HarmonyPatch(typeof(BaseAI), "FindEnemy")]
	static class FindEnemyPatch
	{
		static void Postfix(BaseAI __instance, ref Character __result)
		{
			if (!RaidBossPlugin.IsOn) return;
			Character me = __instance.m_character;
			ZNetView view = __instance.m_nview;
			if (me == null || view == null || !view.IsValid() || !view.IsOwner()) return;
			ZDO zdo = view.GetZDO();
			if (me.IsBoss())
			{
				if (RaidBossPlugin.ParryTaunt.Value <= 0f) return;
				Player taunter = Taunter(zdo.m_uid, me.transform.position);
				if (taunter != null) __result = taunter;
				return;
			}
			if (!zdo.GetBool(AddTag) || zdo.GetBool(RushOverKey) || !RaidBossPlugin.AddsAvoidShields.Value) return;
			if (!(__result is Player closest)) return;   // nothing sensed yet, or it is after a tame: look again next time
			Player target = null;
			bool over = KeepsVanillaTargeting(Utils.GetPrefabName(me.gameObject))
				|| Vector3.Distance(closest.transform.position, me.transform.position) <= InYourFace;
			if (!over)
			{
				long id = zdo.GetLong(RushTargetKey, 0L);
				if (id == 0L)
				{
					target = FewestAdds(me);
					if (target != null)
					{
						zdo.Set(RushTargetKey, target.GetPlayerID());
						zdo.Set(RushStartKey, NowTicks);
					}
				}
				else if (NowTicks - zdo.GetLong(RushStartKey, 0L) <= (long)(RushSeconds * TimeSpan.TicksPerSecond))
					target = Find(id, me.transform.position);
				over = target == null;
			}
			if (over) { zdo.Set(RushOverKey, true); return; }
			__result = target;
		}
	}

	static bool Alive(Player p) => p != null && !p.IsDead() && p.m_nview != null && p.m_nview.IsValid();

	static Player Find(long playerId, Vector3 from)
	{
		foreach (Player p in Player.GetAllPlayers())
			if (Alive(p) && p.GetPlayerID() == playerId)
				return Vector3.Distance(p.transform.position, from) <= Reach ? p : null;
		return null;
	}

	static Player Taunter(ZDOID boss, Vector3 bossPos)
	{
		long now = NowTicks, best = 0L;
		Player found = null;
		foreach (Player p in Player.GetAllPlayers())
		{
			if (!Alive(p)) continue;
			ZDO pz = p.m_nview.GetZDO();
			long until = pz.GetLong(TauntUntilKey, 0L);
			if (until <= now || until <= best) continue;
			if (pz.GetLong(TauntUserKey, 0L) != boss.UserID || pz.GetLong(TauntIdKey, 0L) != (long)boss.ID) continue;
			if (Vector3.Distance(p.transform.position, bossPos) > Reach) continue;
			best = until;
			found = p;
		}
		return found;
	}

	static bool HoldsShield(Player p)
	{
		int hash = p.m_nview.GetZDO().GetInt(ZDOVars.s_leftItem);
		if (hash == 0 || ObjectDB.instance == null) return false;
		GameObject item = ObjectDB.instance.GetItemPrefab(hash);
		ItemDrop drop = item != null ? item.GetComponent<ItemDrop>() : null;
		return drop != null && drop.m_itemData.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Shield;
	}

	static readonly Dictionary<long, int> counts = new Dictionary<long, int>();

	// Among the players without a shield in hand: the one the fewest adds have been sent at so far (every add keeps
	// its rush target on its ZDO, so adds simulated by another player's game count too). A tie goes to the nearest.
	// Null when everyone in reach carries a shield: then there is no rush and the add is vanilla from the start.
	static Player FewestAdds(Character me)
	{
		counts.Clear();
		foreach (Character c in Character.GetAllCharacters())
		{
			if (c == null || c == me || c.IsPlayer() || c.IsDead() || c.m_nview == null || !c.m_nview.IsValid()) continue;
			ZDO cz = c.m_nview.GetZDO();
			if (!cz.GetBool(AddTag)) continue;
			long t = cz.GetLong(RushTargetKey, 0L);
			if (t == 0L) continue;
			counts.TryGetValue(t, out int n);
			counts[t] = n + 1;
		}
		Player best = null;
		int bestCount = int.MaxValue;
		float bestDist = float.MaxValue;
		foreach (Player p in Player.GetAllPlayers())
		{
			if (!Alive(p)) continue;
			float dist = Vector3.Distance(p.transform.position, me.transform.position);
			if (dist > Reach || HoldsShield(p)) continue;
			counts.TryGetValue(p.GetPlayerID(), out int n);
			if (n > bestCount || (n == bestCount && dist >= bestDist)) continue;
			best = p;
			bestCount = n;
			bestDist = dist;
		}
		return best;
	}

	static string cachedList;
	static readonly HashSet<string> vanillaTargeted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

	static bool KeepsVanillaTargeting(string prefab)
	{
		string list = RaidBossPlugin.VanillaTargetedAdds.Value ?? "";
		if (list != cachedList)
		{
			cachedList = list;
			vanillaTargeted.Clear();
			foreach (string name in list.Split(','))
				if (name.Trim().Length > 0) vanillaTargeted.Add(name.Trim());
		}
		return vanillaTargeted.Contains(prefab);
	}
}
