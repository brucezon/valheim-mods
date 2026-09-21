using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.CompilerServices;
using HarmonyLib;
using UnityEngine;

namespace RaidBoss;

// A mode is a named bundle of changes to one creature, carried as one string on its ZDO (raidboss_mode) with an
// optional end time (raidboss_mode_t), so a boss can shift between modes during a fight and a timed mode lapses by
// itself even if the server is gone. The server composes the string from its Traits setting; clients only read it:
//
//   name=Frostbound;res=frost=Resistant,fire=Weak;taken=0.8;aggro=1.5;quick=1.2;regen=0.005;infuse=frost:0.3
//
//   res     resistances laid over the creature's own        (read by whoever works out a hit on it: its owner)
//   taken   damage taken x                                  (its owner)
//   aggro   its attacks come round this much faster         (its owner)
//   quick   movement speed x                                (its owner)
//   regen   fraction of max health healed per second        (its owner)
//   infuse  its hits on players carry this much extra elemental damage, as a fraction of the hit (the player's game)
//   name    a prefix on an add's name; on a boss, a line under its name
internal sealed class Mode
{
	public string Name = "";
	public readonly List<KeyValuePair<HitData.DamageType, HitData.DamageModifier>> Res = new List<KeyValuePair<HitData.DamageType, HitData.DamageModifier>>();
	public float Taken = 1f, Aggro = 1f, Quick = 1f, Regen, Infuse;
	public string InfuseType = "";
}

internal static class Modes
{
	internal const string ModeName = "raidboss_mode";
	internal const string UntilName = "raidboss_mode_t";
	static readonly int ModeKey = ModeName.GetStableHashCode();
	static readonly int UntilKey = UntilName.GetStableHashCode();
	static readonly Dictionary<string, Mode> cache = new Dictionary<string, Mode>();

	static float F(string s, float fallback) => float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out float v) ? v : fallback;

	internal static Mode Get(ZDO zdo)
	{
		if (zdo == null) return null;
		string text = zdo.GetString(ModeKey, "");
		if (text.Length == 0) return null;
		long until = zdo.GetLong(UntilKey, 0L);
		if (until != 0L && ZNet.instance != null && until <= ZNet.instance.GetTime().Ticks) return null;
		if (cache.TryGetValue(text, out Mode mode)) return mode;
		if (cache.Count > 64) cache.Clear();
		cache[text] = mode = new Mode();
		foreach (string part in text.Split(';'))
		{
			int eq = part.IndexOf('=');
			if (eq <= 0) continue;
			string key = part.Substring(0, eq).Trim().ToLowerInvariant(), value = part.Substring(eq + 1).Trim();
			switch (key)
			{
				case "name": mode.Name = value; break;
				case "taken": mode.Taken = Mathf.Clamp(F(value, 1f), 0.01f, 10f); break;
				case "aggro": mode.Aggro = Mathf.Clamp(F(value, 1f), 0.25f, 5f); break;
				case "quick": mode.Quick = Mathf.Clamp(F(value, 1f), 0.25f, 3f); break;
				case "regen": mode.Regen = Mathf.Clamp(F(value, 0f), 0f, 0.1f); break;
				case "infuse":
					string[] iv = value.Split(':');
					mode.InfuseType = iv[0].Trim().ToLowerInvariant();
					mode.Infuse = Mathf.Clamp(iv.Length > 1 ? F(iv[1], 0.3f) : 0.3f, 0f, 3f);
					break;
				case "res":
					foreach (string pair in value.Split(','))
					{
						string[] kv = pair.Split('=');
						if (kv.Length == 2 && Enum.TryParse(kv[0].Trim(), true, out HitData.DamageType type) && Enum.TryParse(kv[1].Trim(), true, out HitData.DamageModifier mod))
							mode.Res.Add(new KeyValuePair<HitData.DamageType, HitData.DamageModifier>(type, mod));
					}
					break;
			}
		}
		return mode;
	}

	// ---- on the creature's owner: faster attacks, speed, regeneration. The ZDO is looked at twice a second per creature.

	sealed class State
	{
		public float Check, RegenTimer;
		public Mode Mode;
		public bool Sped;
		public float Speed, Run, Walk, FlySlow, FlyFast, Swim;
	}

	static readonly ConditionalWeakTable<MonsterAI, State> states = new ConditionalWeakTable<MonsterAI, State>();

	[HarmonyPatch(typeof(MonsterAI), nameof(MonsterAI.UpdateAI))]
	static class OwnerTickPatch
	{
		static void Prefix(MonsterAI __instance, float dt)
		{
			if (!RaidBossPlugin.IsOn) return;
			Character c = __instance.m_character;
			ZNetView view = __instance.m_nview;
			if (c == null || view == null || !view.IsValid() || !view.IsOwner()) return;
			State st = states.GetOrCreateValue(__instance);
			st.Check -= dt;
			if (st.Check <= 0f) { st.Check = 0.5f; st.Mode = Get(view.GetZDO()); }
			Mode mode = st.Mode;

			float quick = mode != null ? mode.Quick : 1f;
			if (!Mathf.Approximately(quick, 1f) && !st.Sped)
			{
				st.Sped = true;
				st.Speed = c.m_speed; st.Run = c.m_runSpeed; st.Walk = c.m_walkSpeed; st.FlySlow = c.m_flySlowSpeed; st.FlyFast = c.m_flyFastSpeed; st.Swim = c.m_swimSpeed;
				c.m_speed *= quick; c.m_runSpeed *= quick; c.m_walkSpeed *= quick; c.m_flySlowSpeed *= quick; c.m_flyFastSpeed *= quick; c.m_swimSpeed *= quick;
			}
			else if (Mathf.Approximately(quick, 1f) && st.Sped)
			{
				st.Sped = false;
				c.m_speed = st.Speed; c.m_runSpeed = st.Run; c.m_walkSpeed = st.Walk; c.m_flySlowSpeed = st.FlySlow; c.m_flyFastSpeed = st.FlyFast; c.m_swimSpeed = st.Swim;
			}
			if (mode == null) return;

			// Attack intervals are measured against each weapon's last use; moving that back makes time pass faster for them.
			if (mode.Aggro > 1.001f && c is Humanoid humanoid && humanoid.m_inventory != null)
				foreach (ItemDrop.ItemData item in humanoid.m_inventory.GetAllItems())
					item.m_lastAttackTime -= dt * (mode.Aggro - 1f);

			if (mode.Regen > 0f)
			{
				st.RegenTimer += dt;
				if (st.RegenTimer >= 1f)
				{
					st.RegenTimer = 0f;
					if (c.GetHealth() < c.GetMaxHealth()) c.Heal(c.GetMaxHealth() * mode.Regen, false);
				}
			}
		}
	}

	// ---- on the player's game: an infused creature's hits carry extra elemental damage

	[HarmonyPatch(typeof(Character), "RPC_Damage")]
	static class InfusionPatch
	{
		static void Prefix(Character __instance, HitData hit)
		{
			if (hit == null || !__instance.IsPlayer() || hit.m_attacker.IsNone() || !RaidBossPlugin.IsOn) return;
			if (__instance.m_nview == null || !__instance.m_nview.IsOwner() || ZDOMan.instance == null) return;
			Mode mode = Get(ZDOMan.instance.GetZDO(hit.m_attacker));
			if (mode == null || mode.Infuse <= 0f) return;
			// not GetTotalDamage: that counts chop and pickaxe, which a boss's bite has a thousand of
			HitData.DamageTypes d = hit.m_damage;
			float extra = (d.m_blunt + d.m_slash + d.m_pierce + d.m_fire + d.m_frost + d.m_lightning + d.m_poison + d.m_spirit) * mode.Infuse;
			switch (mode.InfuseType)
			{
				case "fire": hit.m_damage.m_fire += extra; break;
				case "frost": hit.m_damage.m_frost += extra; break;
				case "lightning": hit.m_damage.m_lightning += extra; break;
				case "poison": hit.m_damage.m_poison += extra; break;
				case "spirit": hit.m_damage.m_spirit += extra; break;
			}
		}
	}
}
