using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace RaidBoss;

// A boss's ward, raised by its casters (the "shield" rule action; Director.TickShield decides when). It works like the
// Fuling shaman's own shield, only sized for a boss: it swallows every hit whole while it holds, and each hit's damage
// wears it down; when the damage passes what it can take, it breaks. The shaman's bubble shows it, tinted so it reads
// as the boss's and not a shaman's.
//
//   raidboss_shield       what the ward can still take (the boss's owner wears it down)
//   raidboss_shield_max   what it could take when raised
//   raidboss_shield_feed  share of the break meter a broken ward pays
internal static class Shield
{
	internal const string PoolName = "raidboss_shield";
	internal const string MaxName = "raidboss_shield_max";
	internal const string FeedName = "raidboss_shield_feed";
	static readonly int PoolKey = PoolName.GetStableHashCode();
	static readonly int MaxKey = MaxName.GetStableHashCode();
	static readonly int FeedKey = FeedName.GetStableHashCode();

	const string BubbleFx = "vfx_GoblinShield";
	const string HitFx = "fx_GoblinShieldHit";
	const string BreakFx = "fx_GoblinShieldBreak";

	internal static float Pool(ZDO zdo) => zdo == null ? 0f : zdo.GetFloat(PoolKey, 0f);
	internal static float Max(ZDO zdo) => zdo == null ? 0f : zdo.GetFloat(MaxKey, 0f);

	// On the boss's owner, before the game works the hit out: a held ward takes the whole hit. Returns true if it did.
	internal static bool Absorb(Character boss, ZDO zdo, HitData hit, Action<float> feedMeter)
	{
		float pool = Pool(zdo);
		if (pool <= 0f) return false;
		HitData.DamageTypes d = hit.m_damage;
		float damage = d.m_blunt + d.m_slash + d.m_pierce + d.m_fire + d.m_frost + d.m_lightning + d.m_poison + d.m_spirit;
		pool -= damage;
		hit.ApplyModifier(0f);
		Mechanics.PlayEffect(HitFx, hit.m_point);
		if (pool <= 0f)
		{
			zdo.Set(PoolKey, 0f);
			feedMeter(zdo.GetFloat(FeedKey, 0f));
			RaidBossPlugin.Log.LogInfo($"{boss.m_name}: the ward is broken");
		}
		else zdo.Set(PoolKey, pool);
		return true;
	}

	// ---- the bubble, on every player's game: shown while the boss's ward holds, and a break effect when it goes

	static readonly Dictionary<Character, GameObject> bubbles = new Dictionary<Character, GameObject>();
	static readonly List<Character> gone = new List<Character>();

	internal static void UpdateBubble(Character boss, ZDO zdo)
	{
		bool held = Pool(zdo) > 0f;
		bubbles.TryGetValue(boss, out GameObject bubble);
		if (held && bubble == null)
		{
			bubble = Mechanics.MakeLocal(BubbleFx, boss.GetCenterPoint());
			if (bubble == null) return;
			bubble.transform.SetParent(boss.transform, true);
			bubble.transform.localRotation = Quaternion.identity;
			// the shaman's effect is made for a Fuling; the game scales status effects by twice the creature's radius
			bubble.transform.localScale = Vector3.one * Mathf.Max(1f, boss.GetRadius() * 2f) / Mathf.Max(0.01f, boss.transform.lossyScale.x);
			Tint(bubble, RaidBossPlugin.WardColour.Value);
			bubbles[boss] = bubble;
		}
		else if (!held && bubble != null)
		{
			UnityEngine.Object.Destroy(bubble);
			bubbles.Remove(boss);
			GameObject burst = Mechanics.MakeLocal(BreakFx, boss.GetCenterPoint());
			if (burst != null) burst.transform.localScale = Vector3.one * Mathf.Max(1f, boss.GetRadius());
		}
		if (bubbles.Count > 0)
		{
			gone.Clear();
			foreach (KeyValuePair<Character, GameObject> kv in bubbles) if (kv.Key == null || kv.Value == null) gone.Add(kv.Key);
			foreach (Character c in gone) bubbles.Remove(c);
		}
	}

	static void Tint(GameObject go, string hex)
	{
		if (!ColorUtility.TryParseHtmlString(hex ?? "", out Color tint)) return;
		foreach (ParticleSystem ps in go.GetComponentsInChildren<ParticleSystem>(true))
		{
			ParticleSystem.MainModule main = ps.main;
			Color c = main.startColor.color;
			main.startColor = new Color(tint.r, tint.g, tint.b, c.a);
		}
		foreach (Renderer r in go.GetComponentsInChildren<Renderer>(true))
			foreach (Material m in r.materials)
			{
				if (m.HasProperty("_Color")) { Color c = m.GetColor("_Color"); m.SetColor("_Color", new Color(tint.r, tint.g, tint.b, c.a)); }
				if (m.HasProperty("_TintColor")) { Color c = m.GetColor("_TintColor"); m.SetColor("_TintColor", new Color(tint.r, tint.g, tint.b, c.a)); }
				if (m.HasProperty("_EmissionColor")) m.SetColor("_EmissionColor", tint * 1.5f);
			}
		foreach (Light l in go.GetComponentsInChildren<Light>(true)) l.color = tint;
	}
}
