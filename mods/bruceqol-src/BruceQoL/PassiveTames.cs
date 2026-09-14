using BepInEx.Configuration;
using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace BruceQoL;

// 15 - Tames: passive taming and breeding.
//
// Vanilla taming and breeding only tick while the pen is loaded (a player within the active area), and
// the animals only eat while their AI runs. The dedicated server loads nothing on its own, so a pen at
// an outpost does nothing until someone stands there. Two vanilla timers already use the world clock and
// catch up on their own: a started pregnancy comes due, and offspring grow up.
//
// This fills the gap by replaying the vanilla loop for the time the animal was unloaded, the first time
// its owner ticks it after a load: in 10 s steps it eats from the pen when hungry (real items removed from
// the pen), progresses taming while fed, gains love points, conceives and gives birth exactly as
// Tameable.TamingUpdate / Procreation.Procreate would have, using the same fields, chances and caps.
// Offspring born in the replay get their spawn time backdated, so Growup turns them into adults on its
// next tick when enough world time has passed. Each animal stamps "last simulated" once a minute while
// loaded (only tamed or fed animals, so wild herds cost nothing).
internal static class PassiveTames
{
	internal static ConfigEntry<BruceQoLPlugin.Toggle> Enabled;
	internal static ConfigEntry<float> FeedRadius;
	internal static ConfigEntry<float> MaxHours;
	internal static ConfigEntry<int> MaxPerPen;

	private static readonly int KeyLastSim = "BQ_lastSim".GetStableHashCode();
	private const float StampInterval = 60f;
	private const float Step = 10f;         // Procreation.m_updateInterval; taming is linear so it does not care
	private const int MaxDeferTicks = 10;   // 30 s of waiting for item ownership before replaying without food

	private sealed class State
	{
		public bool Done;
		public int Tries;
		public float LastStamp = -1f;
	}

	private static readonly ConditionalWeakTable<Tameable, State> s_state = new();
	private static readonly BepInEx.Logging.ManualLogSource s_log = BepInEx.Logging.Logger.CreateLogSource("BruceQoL");

	[HarmonyPatch(typeof(Procreation), "Awake")]
	private static class PenCapPatch
	{
		private static void Postfix(Procreation __instance)
		{
			int cap = MaxPerPen?.Value ?? 0;
			if (cap > 0) __instance.m_maxCreatures = cap;
		}
	}

	// Runs every 3 s on every loaded Tameable; vanilla's own body follows.
	[HarmonyPatch(typeof(Tameable), "TamingUpdate")]
	private static class TamingTickPatch
	{
		private static void Prefix(Tameable __instance)
		{
			if (Enabled == null || Enabled.Value != BruceQoLPlugin.Toggle.On) return;
			ZNetView nview = __instance.m_nview;
			if (nview == null || !nview.IsValid() || !nview.IsOwner()) return;
			ZDO zdo = nview.GetZDO();
			// Only animals someone has started on: tamed, or fed at least once.
			if (!__instance.IsTamed() && zdo.GetLong(ZDOVars.s_tameLastFeeding, 0L) == 0L) return;

			State st = s_state.GetValue(__instance, _ => new State());
			if (!st.Done)
			{
				if (!TryCatchUp(__instance, zdo, st)) return;
				st.Done = true;
				st.LastStamp = Time.time;
				zdo.Set(KeyLastSim, ZNet.instance.GetTime().Ticks);
				return;
			}
			if (Time.time - st.LastStamp >= StampInterval)
			{
				st.LastStamp = Time.time;
				zdo.Set(KeyLastSim, ZNet.instance.GetTime().Ticks);
			}
		}
	}

	// Returns false to try again next tick (waiting for ownership of food items).
	private static bool TryCatchUp(Tameable tame, ZDO zdo, State st)
	{
		long lastSim = zdo.GetLong(KeyLastSim, 0L);
		DateTime now = ZNet.instance.GetTime();
		if (lastSim == 0L) return true; // first time we see it: just start stamping
		double elapsed = (now - new DateTime(lastSim)).TotalSeconds;
		double cap = Math.Max(0f, MaxHours.Value) * 3600.0;
		if (elapsed > cap) elapsed = cap;
		if (elapsed < 30.0) return true;
		DateTime t0 = now.AddSeconds(-elapsed);

		Character ch = tame.m_character;
		MonsterAI ai = tame.m_monsterAI;
		Procreation proc = tame.GetComponent<Procreation>();
		if (ch == null || ai == null) return true;

		// Food in the pen. Every item must be ours to remove; otherwise ask and wait a tick.
		List<ItemDrop> food = FindFood(tame.transform.position, ai);
		foreach (ItemDrop f in food)
		{
			if (!f.CanPickup())
			{
				f.RequestOwn();
				if (++st.Tries < MaxDeferTicks) return false;
			}
		}

		// Starting state.
		DateTime lastFeed = new DateTime(zdo.GetLong(ZDOVars.s_tameLastFeeding, 0L));
		bool tamed = tame.IsTamed();
		float timeLeft = zdo.GetFloat(ZDOVars.s_tameTimeLeft, tame.m_tamingTime);
		int love = proc != null ? zdo.GetInt(ZDOVars.s_lovePoints) : 0;
		long pregnant = proc != null ? zdo.GetLong(ZDOVars.s_pregnant, 0L) : 0L;
		bool tameNow = false;
		int fed = 0;
		List<DateTime> births = new();

		// Pen census, from what is loaded now (animals do not move while unloaded).
		int penCount = 0, partners = 0;
		string myName = Utils.GetPrefabName(tame.gameObject);
		string offspringName = proc != null && proc.m_offspring != null ? Utils.GetPrefabName(proc.m_offspring) : null;
		string partnerName = proc != null && proc.m_seperatePartner != null ? Utils.GetPrefabName(proc.m_seperatePartner) : myName;
		if (proc != null)
		{
			Vector3 pos = tame.transform.position;
			foreach (Character c in Character.GetAllCharacters())
			{
				if (c == null || c.IsPlayer()) continue;
				string n = Utils.GetPrefabName(c.gameObject);
				float d = Vector3.Distance(c.transform.position, pos);
				if ((n == myName || n == offspringName) && d < proc.m_totalCheckRange) penCount++;
				if (n == partnerName && c.IsTamed() && d < proc.m_partnerCheckRange) partners++;
			}
		}
		bool partnerOk = proc != null && (proc.m_noPartnerOffspring != null
			|| (proc.m_seperatePartner != null ? partners >= 1 : partners >= 2));

		int foodIdx = 0;
		for (DateTime t = t0.AddSeconds(Step); t <= now; t = t.AddSeconds(Step))
		{
			bool hungry = (t - lastFeed).TotalSeconds > tame.m_fedDuration;
			if (hungry && foodIdx < food.Count)
			{
				ItemDrop item = food[foodIdx];
				if (item != null && item.RemoveOne())
				{
					fed++;
					lastFeed = t;
					hungry = false;
					if (item == null || item.m_itemData.m_stack <= 0 || !item.m_nview.IsValid()) foodIdx++;
				}
				else
				{
					foodIdx++;
				}
			}
			if (!tamed)
			{
				if (!hungry)
				{
					timeLeft -= Step;
					if (timeLeft <= 0f) { timeLeft = 0f; tamed = true; tameNow = true; }
				}
				continue;
			}
			if (proc == null) continue;
			if (pregnant != 0L)
			{
				if ((t - new DateTime(pregnant)).TotalSeconds > proc.m_pregnancyDuration)
				{
					pregnant = 0L;
					births.Add(t);
					penCount++;
				}
				continue;
			}
			if (hungry || UnityEngine.Random.value <= proc.m_pregnancyChance) continue;
			if (penCount >= proc.m_maxCreatures || !partnerOk) continue;
			love++;
			if (love >= proc.m_requiredLovePoints)
			{
				love = 0;
				pregnant = t.Ticks;
			}
		}

		// Write back and spawn.
		zdo.Set(ZDOVars.s_tameLastFeeding, lastFeed.Ticks);
		if (!tame.IsTamed()) zdo.Set(ZDOVars.s_tameTimeLeft, timeLeft);
		if (tameNow) tame.Tame();
		if (proc != null)
		{
			zdo.Set(ZDOVars.s_lovePoints, love);
			zdo.Set(ZDOVars.s_pregnant, pregnant);
			foreach (DateTime birth in births) SpawnOffspring(tame, proc, partners, birth);
		}
		if (fed > 0 || tameNow || births.Count > 0)
		{
			s_log.LogInfo($"BruceQoL passive tames: {myName} caught up {elapsed / 3600.0:F1} h: ate {fed}{(tameNow ? ", tamed" : "")}{(births.Count > 0 ? $", {births.Count} born" : "")}");
		}
		return true;
	}

	private static List<ItemDrop> FindFood(Vector3 pos, MonsterAI ai)
	{
		List<ItemDrop> result = new();
		if (ai.m_consumeItems == null || ai.m_consumeItems.Count == 0) return result;
		float radius = Math.Max(1f, FeedRadius.Value);
		Collider[] hits = Physics.OverlapSphere(pos, radius, LayerMask.GetMask("item"));
		foreach (Collider col in hits)
		{
			if (col.attachedRigidbody == null) continue;
			ItemDrop item = col.attachedRigidbody.GetComponent<ItemDrop>();
			if (item == null || item.m_nview == null || !item.m_nview.IsValid()) continue;
			bool ok = false;
			foreach (ItemDrop c in ai.m_consumeItems)
			{
				if (c != null && c.m_itemData.m_shared.m_name == item.m_itemData.m_shared.m_name) { ok = true; break; }
			}
			if (ok && !result.Contains(item)) result.Add(item);
		}
		result.Sort((a, b) => Vector3.Distance(a.transform.position, pos).CompareTo(Vector3.Distance(b.transform.position, pos)));
		return result;
	}

	// Mirrors the birth half of Procreation.Procreate, then backdates the spawn time so Growup catches up.
	private static void SpawnOffspring(Tameable tame, Procreation proc, int partners, DateTime birth)
	{
		if (proc.m_offspring == null) return;
		GameObject prefab = ZNetScene.instance.GetPrefab(Utils.GetPrefabName(proc.m_offspring));
		if (prefab == null) return;
		if (proc.m_noPartnerOffspring != null)
		{
			bool noPartner = proc.m_seperatePartner != null ? partners < 1 : partners < 2;
			if (noPartner) prefab = ZNetScene.instance.GetPrefab(Utils.GetPrefabName(proc.m_noPartnerOffspring)) ?? prefab;
		}
		Vector3 dir = tame.transform.forward;
		if (proc.m_spawnRandomDirection)
		{
			float a = UnityEngine.Random.Range(0f, Mathf.PI * 2f);
			dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
		}
		float off = proc.m_spawnOffsetMax > 0f ? UnityEngine.Random.Range(proc.m_spawnOffset, proc.m_spawnOffsetMax) : proc.m_spawnOffset;
		GameObject go = UnityEngine.Object.Instantiate(prefab, tame.transform.position - dir * off, Quaternion.LookRotation(-tame.transform.forward, Vector3.up));
		int level = Mathf.Max(proc.m_minOffspringLevel, tame.m_character != null ? tame.m_character.GetLevel() : proc.m_minOffspringLevel);
		Character child = go.GetComponent<Character>();
		if (child != null)
		{
			child.SetTamed(tame.IsTamed());
			child.SetLevel(level);
		}
		else
		{
			go.GetComponent<ItemDrop>()?.SetQuality(level);
		}
		ZNetView view = go.GetComponent<ZNetView>();
		if (view != null && view.IsValid())
		{
			view.GetZDO().Set(ZDOVars.s_spawnTime, birth.Ticks);
			view.GetZDO().Set(KeyLastSim, birth.Ticks);
		}
	}
}
