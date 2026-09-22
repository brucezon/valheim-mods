using System;
using System.Collections.Generic;
using System.Globalization;
using HarmonyLib;
using UnityEngine;

namespace RaidBoss;

// The general tools a fight script can use, client side. The server decides WHEN and WHAT (Director.RunActions); a
// player's game only knows HOW, so that new mechanics can be written on the server without another client update.
//
//   values on a creature   raidboss_taken (damage taken x), raidboss_res (resistances laid over the creature's own, in
//                          the game's own steps so the damage numbers colour themselves), raidboss_name (a name prefix),
//                          raidboss_label (a line under a boss's name). Read here by whoever needs them; written by
//                          the server on the adds it creates, and by a boss's owner when the server asks (Net.Op).
//   the break meter        hits on a boss are only seen by its owner, so the owner keeps the meter on the boss's ZDO:
//                          weakness damage in full, a share of each MELEE hit's stagger value (bosses normally throw it away), a chunk
//                          for a parry, a chunk when the server says a wave is cleared, draining over time. Full = a
//                          break: the boss stops acting, is slowed to a crawl and takes more damage for a few seconds;
//                          then the meter needs more. A flying boss breaks when it lands.
//   ground strikes         every player's game draws the warning ring and, when it lands, judges its OWN player.
//   effects, status, weather   vanilla things by name.
internal static class Mechanics
{
	static readonly int TakenKey = "raidboss_taken".GetStableHashCode();
	static readonly int ResKey = "raidboss_res".GetStableHashCode();
	static readonly int NameKey = "raidboss_name".GetStableHashCode();
	static readonly int LabelKey = "raidboss_label".GetStableHashCode();
	static readonly int BrkKey = "raidboss_brk".GetStableHashCode();
	static readonly int BrkMaxKey = "raidboss_brk_max".GetStableHashCode();
	static readonly int BrkDrainKey = "raidboss_brk_drain".GetStableHashCode();
	static readonly int BrkParryKey = "raidboss_brk_parry".GetStableHashCode();
	static readonly int BrkDurKey = "raidboss_brk_dur".GetStableHashCode();
	static readonly int BrkGrowKey = "raidboss_brk_grow".GetStableHashCode();
	static readonly int BrkTakenKey = "raidboss_brk_x".GetStableHashCode();
	static readonly int BrkHitKey = "raidboss_brk_hit".GetStableHashCode();
	static readonly int BrkWeakKey = "raidboss_brk_weak".GetStableHashCode();
	static readonly int BrkCapKey = "raidboss_brk_cap".GetStableHashCode();   // the most of the meter one parry may fill (0 = no cap)
	static readonly int GuardKey = "raidboss_guard".GetStableHashCode();
	static readonly int GuardBrokenKey = "raidboss_guard_x".GetStableHashCode();
	static readonly int BreakUntilKey = "raidboss_break_t".GetStableHashCode();
	static readonly int TauntUserKey = "raidboss_taunt_u".GetStableHashCode();
	static readonly int TauntIdKey = "raidboss_taunt_i".GetStableHashCode();
	static readonly int TauntUntilKey = "raidboss_taunt_t".GetStableHashCode();

	const float BrokenAnimSpeed = 0.15f;

	static long NowTicks => ZNet.instance != null ? ZNet.instance.GetTime().Ticks : 0L;
	static bool Broken(ZDO zdo) => zdo.GetLong(BreakUntilKey, 0L) > NowTicks;

	static float PrefabHealth(ZDO zdo)
	{
		GameObject prefab = ZNetScene.instance != null ? ZNetScene.instance.GetPrefab(zdo.GetPrefab()) : null;
		Character ch = prefab != null ? prefab.GetComponent<Character>() : null;
		return ch != null ? ch.m_health : 1f;
	}

	static float F(string s, float fallback) => float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out float v) ? v : fallback;

	// ---- operations the server asks a creature's owner to carry out (Net.RPC_Creature). Unknown ones are ignored.

	internal static void Apply(ZDO zdo, Net.Op op)
	{
		float max = zdo.GetFloat(ZDOVars.s_maxHealth, PrefabHealth(zdo));
		switch (op.Name)
		{
			case "set_f":
				if (op.Key.StartsWith("raidboss_", StringComparison.Ordinal)) zdo.Set(op.Key.GetStableHashCode(), op.Value);
				break;
			case "set_s":
				if (op.Key.StartsWith("raidboss_", StringComparison.Ordinal)) zdo.Set(op.Key.GetStableHashCode(), op.Text);
				break;
			case "set_until": // a moment this many seconds from now, as network time; 0 or less clears it
				if (op.Key.StartsWith("raidboss_", StringComparison.Ordinal)) zdo.Set(op.Key.GetStableHashCode(), op.Value > 0f ? NowTicks + (long)(op.Value * TimeSpan.TicksPerSecond) : 0L);
				break;
			case "hpmult":   // once per creature: the mark says it has been done. The health fraction is kept.
				if (zdo.GetFloat(Net.HealthKey, 0f) != 0f) break;
				float mult = Mathf.Clamp(op.Value, 0.1f, 10f);
				float health = zdo.GetFloat(ZDOVars.s_health, max);
				zdo.Set(ZDOVars.s_maxHealth, max * mult);
				zdo.Set(ZDOVars.s_health, health * mult);
				zdo.Set(Net.HealthKey, mult);
				RaidBossPlugin.Log.LogInfo($"health {max:0} -> {max * mult:0}");
				break;
			case "heal":     // a fraction of max health
				zdo.Set(ZDOVars.s_health, Mathf.Min(max, zdo.GetFloat(ZDOVars.s_health, max) + max * Mathf.Clamp01(op.Value)));
				break;
			case "meter":    // "size=0.4;drain=0.002;parry=0.06;dur=8;grow=1.5;x=2;hit=0.25;weak=1", sizes as fractions of max health. Once.
				if (zdo.GetFloat(BrkMaxKey, 0f) != 0f) break;
				var v = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
				foreach (string pair in op.Text.Split(';'))
				{
					string[] kv = pair.Split('=');
					if (kv.Length == 2) v[kv[0].Trim()] = kv[1].Trim();
				}
				string G(string k, string d) => v.TryGetValue(k, out string s) ? s : d;
				zdo.Set(BrkMaxKey, Mathf.Max(1f, F(G("size", "0.4"), 0.4f) * max));
				zdo.Set(BrkDrainKey, F(G("drain", "0.002"), 0.002f) * max);
				zdo.Set(BrkParryKey, F(G("parry", "0.03"), 0.03f) * max);
				zdo.Set(BrkDurKey, F(G("dur", "8"), 8f));
				zdo.Set(BrkGrowKey, F(G("grow", "1.5"), 1.5f));
				zdo.Set(BrkTakenKey, F(G("x", "2"), 2f));
				zdo.Set(BrkHitKey, F(G("hit", "1"), 1f));      // absent (a 0.1.0 server) = weapon stagger counts in full, as it did
				zdo.Set(BrkWeakKey, F(G("weak", "0"), 0f));
				zdo.Set(BrkCapKey, Mathf.Clamp01(F(G("cap", "0"), 0f)));
				break;
			case "brk_add":  // a fraction of the meter
				AddMeter(zdo, op.Value * zdo.GetFloat(BrkMaxKey, 0f), null);
				break;
			case "break":    // seconds; 0 = the meter's own length
				StartBreak(zdo, op.Value > 0f ? op.Value : zdo.GetFloat(BrkDurKey, 8f), false);
				break;
		}
	}

	// ---- the break meter (owner side)

	static void AddMeter(ZDO zdo, float amount, Character boss)
	{
		float max = zdo.GetFloat(BrkMaxKey, 0f);
		if (max <= 0f || amount <= 0f || Broken(zdo)) return;
		float value = Mathf.Min(max, zdo.GetFloat(BrkKey, 0f) + amount);
		zdo.Set(BrkKey, value);
		if (value >= max) TryBreak(zdo, boss);
	}

	// A full meter breaks the boss - unless it is in the air, where there is nothing to show; then it waits, full.
	static void TryBreak(ZDO zdo, Character boss)
	{
		if (boss == null && ZNetScene.instance != null)
		{
			GameObject go = ZNetScene.instance.FindInstance(zdo.m_uid);
			boss = go != null ? go.GetComponent<Character>() : null;
		}
		if (boss != null && boss.IsFlying()) return;
		StartBreak(zdo, zdo.GetFloat(BrkDurKey, 8f), true);
	}

	static void StartBreak(ZDO zdo, float seconds, bool fromMeter)
	{
		zdo.Set(BreakUntilKey, NowTicks + (long)(Mathf.Clamp(seconds, 1f, 60f) * TimeSpan.TicksPerSecond));
		zdo.Set(BrkKey, 0f);
		if (fromMeter)
		{
			zdo.Set(BrkMaxKey, zdo.GetFloat(BrkMaxKey, 0f) * Mathf.Max(1f, zdo.GetFloat(BrkGrowKey, 1.5f)));
			Net.SendEvent(zdo.m_uid, "break");
		}
	}

	sealed class OwnerState { public float Timer; public bool Slowed; public readonly Dictionary<long, long> Parries = new Dictionary<long, long>(); }
	static readonly Dictionary<ZDOID, OwnerState> owned = new Dictionary<ZDOID, OwnerState>();

	[HarmonyPatch(typeof(MonsterAI), nameof(MonsterAI.UpdateAI))]
	static class BossTickPatch
	{
		static bool Prefix(MonsterAI __instance, float dt, ref bool __result)
		{
			Character boss = __instance.m_character;
			if (boss == null || !boss.IsBoss() || !RaidBossPlugin.IsOn) return true;
			ZNetView view = __instance.m_nview;
			if (view == null || !view.IsValid() || !view.IsOwner()) return true;
			ZDO zdo = view.GetZDO();
			if (!owned.TryGetValue(zdo.m_uid, out OwnerState st)) { if (owned.Count > 32) owned.Clear(); owned[zdo.m_uid] = st = new OwnerState(); }

			if (Broken(zdo))
			{
				if (!st.Slowed) { st.Slowed = true; boss.m_zanim.SetSpeed(BrokenAnimSpeed); }
				__instance.StopMoving();
				__result = true;
				return false;
			}
			if (st.Slowed) { st.Slowed = false; boss.m_zanim.SetSpeed(1f); }

			float max = zdo.GetFloat(BrkMaxKey, 0f);
			if (max <= 0f) return true;
			st.Timer += dt;
			if (st.Timer < 1f) return true;
			float elapsed = st.Timer;
			st.Timer = 0f;
			// parries: each player's latest parry of this boss counts once
			float parry = zdo.GetFloat(BrkParryKey, 0f);
			if (parry > 0f)
				foreach (Player p in Player.GetAllPlayers())
				{
					if (p == null || p.m_nview == null || !p.m_nview.IsValid()) continue;
					ZDO pz = p.m_nview.GetZDO();
					long until = pz.GetLong(TauntUntilKey, 0L);
					if (until == 0L || pz.GetLong(TauntUserKey, 0L) != zdo.m_uid.UserID || pz.GetLong(TauntIdKey, 0L) != (long)zdo.m_uid.ID) continue;
					long id = p.GetPlayerID();
					st.Parries.TryGetValue(id, out long seen);
					if (until <= seen) continue;
					bool first = seen == 0L && until < NowTicks;   // an old parry from before we owned the boss
					st.Parries[id] = until;
					if (!first)
					{
						float cap = zdo.GetFloat(BrkCapKey, 0f);
						AddMeter(zdo, cap > 0f ? Mathf.Min(parry, cap * max) : parry, boss);
					}
				}
			float value = zdo.GetFloat(BrkKey, 0f);
			if (value >= max) TryBreak(zdo, boss);
			else if (value > 0f) zdo.Set(BrkKey, Mathf.Max(0f, value - zdo.GetFloat(BrkDrainKey, 0f) * elapsed));
			return true;
		}
	}

	// ---- damage a creature takes (owner side): the ward / break multiplier, and the meter

	// Melee hits feed the meter by their weight; arrows, bolts and magic only through the boss's weaknesses. Up close is
	// where a break is earned.
	static bool IsMelee(Skills.SkillType skill) => skill == Skills.SkillType.Swords || skill == Skills.SkillType.Knives || skill == Skills.SkillType.Clubs
		|| skill == Skills.SkillType.Polearms || skill == Skills.SkillType.Spears || skill == Skills.SkillType.Axes || skill == Skills.SkillType.Unarmed
		|| skill == Skills.SkillType.Pickaxes || skill == Skills.SkillType.WoodCutting;

	static bool IsWeak(HitData.DamageModifier m) => m == HitData.DamageModifier.Weak || m == HitData.DamageModifier.VeryWeak || m == HitData.DamageModifier.SlightlyWeak;

	static float WeakDamage(HitData.DamageModifiers mods, HitData.DamageTypes d)
	{
		float sum = 0f;
		if (d.m_blunt > 0f && IsWeak(mods.m_blunt)) sum += d.m_blunt;
		if (d.m_slash > 0f && IsWeak(mods.m_slash)) sum += d.m_slash;
		if (d.m_pierce > 0f && IsWeak(mods.m_pierce)) sum += d.m_pierce;
		if (d.m_fire > 0f && IsWeak(mods.m_fire)) sum += d.m_fire;
		if (d.m_frost > 0f && IsWeak(mods.m_frost)) sum += d.m_frost;
		if (d.m_lightning > 0f && IsWeak(mods.m_lightning)) sum += d.m_lightning;
		if (d.m_poison > 0f && IsWeak(mods.m_poison)) sum += d.m_poison;
		if (d.m_spirit > 0f && IsWeak(mods.m_spirit)) sum += d.m_spirit;
		return sum;
	}

	[HarmonyPatch(typeof(Character), "RPC_Damage")]
	static class CreatureDamagePatch
	{
		static void Prefix(Character __instance, HitData hit)
		{
			if (hit == null || __instance.IsPlayer() || !RaidBossPlugin.IsOn) return;
			ZNetView view = __instance.m_nview;
			if (view == null || !view.IsValid() || !view.IsOwner()) return;
			ZDO zdo = view.GetZDO();
			bool broken = Broken(zdo);
			if (!broken && zdo.GetFloat(BrkMaxKey, 0f) > 0f && Shield.Pool(zdo) <= 0f && hit.GetAttacker() is Player && Game.instance != null)
			{
				// Plain weapon stagger counts for little; damage of a type the boss is weak to (its own weaknesses, or a
				// trait's) counts in full. Worked out before resistances, so it measures what was swung, not what landed.
				float scale = Game.instance.GetDifficultyDamageScaleEnemy(__instance.transform.position);
				float fill = IsMelee(hit.m_skill) ? hit.m_damage.GetTotalStaggerDamage() * hit.m_staggerMultiplier * zdo.GetFloat(BrkHitKey, 1f) : 0f;
				float weakShare = zdo.GetFloat(BrkWeakKey, 0f);
				if (weakShare > 0f) fill += WeakDamage(__instance.GetDamageModifiers(), hit.m_damage) * weakShare;
				AddMeter(zdo, fill * scale, __instance);
			}
			if ((!broken || Shield.IsImmune(zdo)) && Shield.Absorb(__instance, zdo, hit, share => AddMeter(zdo, share * zdo.GetFloat(BrkMaxKey, 0f), __instance))) return;
			float mult = zdo.GetFloat(TakenKey, 1f);
			if (mult <= 0f) mult = 1f;
			Mode mode = Modes.Get(zdo);
			if (mode != null) mult *= mode.Taken;
			// Guarded: hard to hurt until the meter breaks it, and very easy while it is broken. Only with a meter, which ends it.
			// A guarded creature that CAN be staggered (a warband miniboss; the game's bosses cannot) has its guard down
			// while it is staggered: a landed parry pays out in full, at the game's own double damage for a staggered enemy.
			float guard = zdo.GetFloat(BrkMaxKey, 0f) > 0f ? zdo.GetFloat(GuardKey, 0f) : 0f;
			if (broken) mult *= Mathf.Max(1f, guard > 0f ? zdo.GetFloat(GuardBrokenKey, 3f) : zdo.GetFloat(BrkTakenKey, 2f));
			else if (guard > 0f && !__instance.IsStaggering()) mult *= Mathf.Clamp(guard, 0.01f, 1f);
			if (!Mathf.Approximately(mult, 1f)) hit.ApplyModifier(Mathf.Clamp(mult, 0.01f, 10f));
		}
	}

	// ---- resistances laid over the creature's own: "pierce=Resistant,blunt=Weak" in the game's own words

	static readonly Dictionary<string, List<KeyValuePair<HitData.DamageType, HitData.DamageModifier>>> parsedRes = new Dictionary<string, List<KeyValuePair<HitData.DamageType, HitData.DamageModifier>>>();

	[HarmonyPatch(typeof(Character), nameof(Character.GetDamageModifiers))]
	static class ResistancePatch
	{
		static void Postfix(Character __instance, ref HitData.DamageModifiers __result)
		{
			if (__instance.IsPlayer() || __instance.m_nview == null || !__instance.m_nview.IsValid()) return;
			// A staggered creature (or a broken boss) has its guard down: a trait's resistances do not apply then, so the
			// stagger pays out in full. Its weaknesses still do.
			bool open = __instance.IsStaggering() || Broken(__instance.m_nview.GetZDO());
			Mode mode = Modes.Get(__instance.m_nview.GetZDO());
			if (mode != null) Overlay(ref __result, mode.Res, open);
			string text = __instance.m_nview.GetZDO().GetString(ResKey, "");
			if (text.Length == 0) return;
			if (!parsedRes.TryGetValue(text, out List<KeyValuePair<HitData.DamageType, HitData.DamageModifier>> list))
			{
				if (parsedRes.Count > 64) parsedRes.Clear();
				parsedRes[text] = list = new List<KeyValuePair<HitData.DamageType, HitData.DamageModifier>>();
				foreach (string pair in text.Split(','))
				{
					string[] kv = pair.Split('=');
					if (kv.Length == 2 && Enum.TryParse(kv[0].Trim(), true, out HitData.DamageType type) && Enum.TryParse(kv[1].Trim(), true, out HitData.DamageModifier mod))
						list.Add(new KeyValuePair<HitData.DamageType, HitData.DamageModifier>(type, mod));
				}
			}
			Overlay(ref __result, list, open);
		}
	}

	static void Overlay(ref HitData.DamageModifiers mods, List<KeyValuePair<HitData.DamageType, HitData.DamageModifier>> list, bool open)
	{
		foreach (KeyValuePair<HitData.DamageType, HitData.DamageModifier> kv in list)
		{
			if (open && !IsWeak(kv.Value) && kv.Value != HitData.DamageModifier.Normal) continue;
			switch (kv.Key)
			{
				case HitData.DamageType.Blunt: mods.m_blunt = kv.Value; break;
				case HitData.DamageType.Slash: mods.m_slash = kv.Value; break;
				case HitData.DamageType.Pierce: mods.m_pierce = kv.Value; break;
				case HitData.DamageType.Fire: mods.m_fire = kv.Value; break;
				case HitData.DamageType.Frost: mods.m_frost = kv.Value; break;
				case HitData.DamageType.Lightning: mods.m_lightning = kv.Value; break;
				case HitData.DamageType.Poison: mods.m_poison = kv.Value; break;
				case HitData.DamageType.Spirit: mods.m_spirit = kv.Value; break;
			}
		}
	}

	// ---- names and the boss bar

	[HarmonyPatch(typeof(Character), nameof(Character.GetHoverName))]
	static class NamePatch
	{
		static void Postfix(Character __instance, ref string __result)
		{
			if (__instance.IsPlayer() || __instance.m_nview == null || !__instance.m_nview.IsValid()) return;
			if (__instance.IsBoss()) return;   // a boss shows its mode under its name instead
			string prefix = __instance.m_nview.GetZDO().GetString(NameKey, "");
			if (prefix.Length == 0) prefix = Modes.Get(__instance.m_nview.GetZDO())?.Name ?? "";
			if (prefix.Length > 0) __result = prefix + " " + __result;
		}
	}

	// EnemyHud rewrites a boss's name text every frame, so a label and the meter can simply ride along under it.
	[HarmonyPatch(typeof(EnemyHud), "UpdateHuds")]
	static class BossBarPatch
	{
		static void Postfix(EnemyHud __instance)
		{
			if (!RaidBossPlugin.IsOn) return;
			foreach (KeyValuePair<Character, EnemyHud.HudData> kv in __instance.m_huds)
			{
				Character c = kv.Key;
				if (c == null || !c.IsBoss() || kv.Value.m_name == null || c.m_nview == null || !c.m_nview.IsValid()) continue;
				ZDO zdo = c.m_nview.GetZDO();
				string extra = "";
				string label = zdo.GetString(LabelKey, "");
				Shield.UpdateBubble(c, zdo);
				float ward = Shield.Pool(zdo), wardMax = Shield.Max(zdo);
				if (ward > 0f && wardMax > 0f) label = (label.Length > 0 ? label + " - " : "") + "<color=" + RaidBossPlugin.WardColour.Value + ">" + RaidBossPlugin.WardLabel.Value + (Shield.IsImmune(zdo) ? "" : " (" + Mathf.CeilToInt(ward / wardMax * 100f) + "%)") + "</color>";
				string modeName = Modes.Get(zdo)?.Name ?? "";
				if (modeName.Length > 0) label = label.Length > 0 ? label + " - " + modeName : modeName;
				if (Broken(zdo)) extra += "\n<size=70%><color=#ffd24a>Broken</color></size>";
				else
				{
					if (zdo.GetFloat(GuardKey, 0f) > 0f && zdo.GetFloat(BrkMaxKey, 0f) > 0f) label = label.Length > 0 ? "Guarded - " + label : "Guarded";
					if (label.Length > 0) extra += "\n<size=70%>" + label + "</size>";
					float max = zdo.GetFloat(BrkMaxKey, 0f);
					if (max > 0f)
					{
						int lit = Mathf.Clamp(Mathf.RoundToInt(zdo.GetFloat(BrkKey, 0f) / max * 24f), 0, 24);
						if (lit > 0) extra += "\n<size=55%><color=#ffd24a>" + new string('|', lit) + "</color><color=#00000080>" + new string('|', 24 - lit) + "</color></size>";
					}
				}
				if (extra.Length > 0) kv.Value.m_name.text += extra;
			}
		}
	}

	// ---- ground strikes

	// ---- a storm: thunderstorm weather, and ground lightning around a place - the game's own Thunderstone strike
	// (lightningAOE) as a visual, its own 400-damage rod stripped out; ours is small and has no warning.
	internal static void Storm(Vector3 center, float radius, float seconds, float every, float damage, ZDOID attacker, string boltFx, string weather)
	{
		if (ZNet.instance == null || ZNet.instance.IsDedicated() || !RaidBossPlugin.IsOn) return;
		if (!string.IsNullOrEmpty(weather)) ForceWeather(weather, center, radius + 40f, seconds + 3f);
		var go = new GameObject("RaidBoss_Storm");
		StormRunner runner = go.AddComponent<StormRunner>();
		runner.Center = center; runner.Radius = Mathf.Clamp(radius, 5f, 80f); runner.Seconds = Mathf.Clamp(seconds, 1f, 120f);
		runner.Every = Mathf.Clamp(every, 0.3f, 10f); runner.Damage = damage; runner.Attacker = attacker;
		runner.BoltFx = string.IsNullOrEmpty(boltFx) ? "lightningAOE" : boltFx;
	}

	sealed class StormRunner : MonoBehaviour
	{
		public Vector3 Center;
		public float Radius, Seconds, Every, Damage;
		public ZDOID Attacker;
		public string BoltFx;
		float age, next;
		const float BoltReach = 2.5f;

		void Update()
		{
			age += Time.deltaTime;
			if (age > Seconds) { Destroy(gameObject); return; }
			next -= Time.deltaTime;
			if (next > 0f) return;
			next = UnityEngine.Random.Range(Every * 0.5f, Every * 1.5f);
			Player me = Player.m_localPlayer;
			Vector3 at;
			bool near = me != null && Utils.DistanceXZ(me.transform.position, Center) < Radius + 10f && UnityEngine.Random.value < 0.34f;
			Vector2 off = UnityEngine.Random.insideUnitCircle * (near ? 7f : Radius);
			at = (near ? me.transform.position : Center) + new Vector3(off.x, 0f, off.y);
			if (ZoneSystem.instance != null && ZoneSystem.instance.GetSolidHeight(at, out float h)) at.y = h;
			Bolt(at);
		}

		void Bolt(Vector3 at)
		{
			GameObject bolt = MakeLocal(BoltFx, at);
			if (bolt != null)
			{
				foreach (Aoe a in bolt.GetComponentsInChildren<Aoe>(true)) UnityEngine.Object.DestroyImmediate(a);   // the rod's own damage
				foreach (TimedDestruction td in bolt.GetComponentsInChildren<TimedDestruction>(true)) UnityEngine.Object.DestroyImmediate(td);
				UnityEngine.Object.Destroy(bolt, 10f);
			}
			Player p = Player.m_localPlayer;
			if (p == null || p.IsDead() || Damage <= 0f || Utils.DistanceXZ(p.transform.position, at) > BoltReach || Mathf.Abs(p.transform.position.y - at.y) > 4f) return;
			var hit = new HitData();
			hit.m_damage.m_lightning = Damage;
			hit.m_point = p.GetCenterPoint();
			Vector3 d = p.transform.position - at; d.y = 0f;
			hit.m_dir = d.sqrMagnitude > 0.01f ? d.normalized : Vector3.up;
			hit.m_attacker = Attacker;
			hit.m_blockable = false;
			hit.m_dodgeable = true;
			p.Damage(hit);
		}
	}

	// ---- a chase: a strike at the hunted player's feet every so often, each with its own warning ring, for a few seconds.
	// Standing still gets you hit; keep moving and each lands where you were.
	internal static void Chase(long playerId, string element, float radius, float delay, float damage, float every, float seconds, ZDOID attacker, string hitFx)
	{
		if (ZNet.instance == null || ZNet.instance.IsDedicated() || !RaidBossPlugin.IsOn) return;
		var go = new GameObject("RaidBoss_Chase");
		ChaseRunner runner = go.AddComponent<ChaseRunner>();
		runner.PlayerId = playerId; runner.Element = element; runner.Radius = radius; runner.Delay = delay; runner.Damage = damage;
		runner.Every = Mathf.Clamp(every, 0.3f, 10f); runner.Seconds = Mathf.Clamp(seconds, 0.5f, 30f); runner.Attacker = attacker; runner.HitFx = hitFx;
	}

	sealed class ChaseRunner : MonoBehaviour
	{
		public long PlayerId;
		public string Element, HitFx;
		public float Radius, Delay, Damage, Every, Seconds;
		public ZDOID Attacker;
		float age, next;

		void Update()
		{
			age += Time.deltaTime;
			if (age > Seconds) { Destroy(gameObject); return; }
			next -= Time.deltaTime;
			if (next > 0f) return;
			next = Every;
			Player target = null;
			foreach (Player p in Player.GetAllPlayers()) if (p != null && !p.IsDead() && p.GetPlayerID() == PlayerId) { target = p; break; }
			if (target == null) { Destroy(gameObject); return; }
			Strike(target.transform.position, Radius, Delay, Element, Damage, Attacker, "", HitFx);
		}
	}

	internal static void Strike(Vector3 pos, float radius, float delay, string element, float damage, ZDOID attacker, string tellFx, string hitFx)
	{
		if (ZNet.instance == null || ZNet.instance.IsDedicated() || !RaidBossPlugin.IsOn) return;
		var go = new GameObject("RaidBoss_Strike");
		go.transform.position = pos;
		StrikeRunner runner = go.AddComponent<StrikeRunner>();
		runner.Radius = Mathf.Clamp(radius, 0.5f, 30f);
		runner.Delay = Mathf.Clamp(delay, 0.3f, 15f);
		runner.Element = (element ?? "").ToLowerInvariant();
		runner.Damage = damage;
		runner.Attacker = attacker;
		runner.HitFx = hitFx;
		PlayEffect(tellFx, pos);
	}

	// A game projectile as a body only: its flight, network sync, fire-starting and colliders removed, so it cannot hurt,
	// burn or collide - it is moved by hand.
	static GameObject MakeLocalBody(string prefab, Vector3 pos)
	{
		GameObject go = MakeLocal(prefab, pos);
		if (go == null) return null;
		foreach (Projectile c in go.GetComponentsInChildren<Projectile>(true)) UnityEngine.Object.DestroyImmediate(c);
		foreach (ZSyncTransform c in go.GetComponentsInChildren<ZSyncTransform>(true)) UnityEngine.Object.DestroyImmediate(c);
		foreach (CinderSpawner c in go.GetComponentsInChildren<CinderSpawner>(true)) UnityEngine.Object.DestroyImmediate(c);
		foreach (Collider c in go.GetComponentsInChildren<Collider>(true)) UnityEngine.Object.DestroyImmediate(c);
		foreach (Rigidbody c in go.GetComponentsInChildren<Rigidbody>(true)) UnityEngine.Object.DestroyImmediate(c);
		UnityEngine.Object.Destroy(go, 10f);
		return go;
	}

	// An impact effect with its particles' built-in start delays squeezed to at most a tenth of a second.
	static void PlayImpact(string prefabName, Vector3 pos, float scale = 1f)
	{
		if (string.IsNullOrEmpty(prefabName)) return;
		foreach (string name in prefabName.Split('+'))
		{
			GameObject made = MakeLocal(name.Trim(), pos);
			if (made == null) continue;
			if (!Mathf.Approximately(scale, 1f)) made.transform.localScale *= scale;
			foreach (ParticleSystem ps in made.GetComponentsInChildren<ParticleSystem>(true))
			{
				ParticleSystem.MainModule main = ps.main;
				if (main.startDelay.constantMax > 0.1f) { ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear); main.startDelay = Mathf.Min(0.1f, main.startDelay.constantMax * 0.08f); ps.Play(true); }
			}
			UnityEngine.Object.Destroy(made, 12f);
		}
	}

	static Material ringMaterial;

	static Material RingMaterial()
	{
		if (ringMaterial != null) return ringMaterial;
		foreach (string name in new[] { "Sprites/Default", "Legacy Shaders/Particles/Alpha Blended", "Unlit/Color", "Standard" })
		{
			Shader shader = Shader.Find(name);
			if (shader == null) continue;
			ringMaterial = new Material(shader);
			break;
		}
		return ringMaterial;
	}

	sealed class StrikeRunner : MonoBehaviour
	{
		public float Radius, Delay, Damage;
		public string Element, HitFx;
		public ZDOID Attacker;
		const int Points = 48;
		float time;
		LineRenderer outer, inner;
		readonly float[] heights = new float[Points];

		Color Colour => Element == "fire" ? new Color(1f, 0.45f, 0.1f) : Element == "frost" || Element == "ice" ? new Color(0.55f, 0.85f, 1f) : Element == "lightning" ? new Color(0.9f, 0.9f, 1f) : Element == "poison" ? new Color(0.5f, 0.9f, 0.3f) : new Color(1f, 0.2f, 0.2f);

		LineRenderer Ring(float width, float alpha)
		{
			var child = new GameObject("ring");
			child.transform.SetParent(transform, false);
			LineRenderer line = child.AddComponent<LineRenderer>();
			line.useWorldSpace = true;
			line.loop = true;
			line.positionCount = Points;
			line.startWidth = line.endWidth = width;
			line.material = RingMaterial();
			Color c = Colour; c.a = alpha;
			line.startColor = line.endColor = c;
			line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
			line.receiveShadows = false;
			return line;
		}

		void Start()
		{
			Vector3 centre = transform.position;
			for (int i = 0; i < Points; i++)
			{
				float a = i * Mathf.PI * 2f / Points;
				Vector3 p = centre + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * Radius;
				heights[i] = ZoneSystem.instance != null && ZoneSystem.instance.GetSolidHeight(p, out float h) && Mathf.Abs(h - centre.y) < 8f ? h : centre.y;
			}
			outer = Ring(0.18f, 0.9f);
			inner = Ring(0.1f, 0.6f);
			Draw(outer, Radius);
			Draw(inner, 0.05f);
		}

		void Draw(LineRenderer line, float r)
		{
			Vector3 centre = transform.position;
			for (int i = 0; i < Points; i++)
			{
				float a = i * Mathf.PI * 2f / Points;
				line.SetPosition(i, new Vector3(centre.x + Mathf.Cos(a) * r, heights[i] + 0.15f, centre.z + Mathf.Sin(a) * r));
			}
		}

		// Fire comes down as one of Yagluth's own meteors: it starts falling so that it touches the ground on the very frame
		// the ring fills and the damage lands, never before.
		GameObject falling;
		bool fell;
		Vector3 fallFrom;
		float fallStart, fallTime;

		void Update()
		{
			time += Time.deltaTime;
			if (time < Delay)
			{
				Draw(inner, Mathf.Max(0.05f, Radius * time / Delay));
				Fall();
				return;
			}
			if (falling != null) Destroy(falling);
			Land();
			Destroy(gameObject);
		}

		float Size => Mathf.Clamp(Radius / 4f, 0.4f, 1.5f);

		void Fall()
		{
			if (!fell && Element == "fire")
			{
				fallTime = Mathf.Min(0.6f, Delay * 0.8f);
				if (time < Delay - fallTime) return;
				fell = true;
				Vector2 side = UnityEngine.Random.insideUnitCircle.normalized * 12f;
				fallFrom = transform.position + new Vector3(side.x, 26f, side.y);
				fallStart = time;
				falling = MakeLocalBody("projectile_meteor", fallFrom);
				// sized to its ring: a full meteor for a 4 m strike, smaller ones for chases and rain
				if (falling != null) falling.transform.localScale *= Size;
				if (falling != null) falling.transform.rotation = Quaternion.LookRotation(transform.position - fallFrom);
			}
			if (falling == null) return;
			float k = Mathf.Clamp01((time - fallStart) / Mathf.Max(0.01f, Delay - fallStart));
			falling.transform.position = Vector3.Lerp(fallFrom, transform.position, k);
		}

		void Land()
		{
			PlayImpact(HitFx, transform.position, Size);
			Player p = Player.m_localPlayer;
			if (p == null || p.IsDead() || Damage <= 0f) return;
			Vector3 d = p.transform.position - transform.position;
			if (Mathf.Abs(d.y) > 8f) return;
			d.y = 0f;
			if (d.magnitude > Radius) return;
			var hit = new HitData();
			switch (Element)
			{
				case "fire": hit.m_damage.m_fire = Damage; break;
				case "frost": case "ice": hit.m_damage.m_frost = Damage; break;
				case "lightning": hit.m_damage.m_lightning = Damage; break;
				case "poison": hit.m_damage.m_poison = Damage; break;
				default: hit.m_damage.m_blunt = Damage; break;
			}
			hit.m_point = p.GetCenterPoint();
			hit.m_dir = d.sqrMagnitude > 0.01f ? d.normalized : Vector3.up;
			hit.m_attacker = Attacker;
			hit.m_blockable = false;
			hit.m_dodgeable = true;
			p.Damage(hit);
		}
	}

	// ---- vanilla effects, status effects, weather

	// Only prefabs WITHOUT a ZNetView: those are local visuals. One with a ZNetView would become a networked object from
	// every player's game at once; the server creates those itself instead.
	internal static void PlayEffect(string prefabName, Vector3 pos)
	{
		if (string.IsNullOrEmpty(prefabName)) return;
		foreach (string name in prefabName.Split('+'))
		{
			GameObject made = MakeLocal(name.Trim(), pos);
			if (made != null) UnityEngine.Object.Destroy(made, 12f);
		}
	}

	// Every vanilla effect prefab carries a ZNetView. Made with ZNetView.m_forceDisableInit (as the game does for a build
	// ghost), the view removes itself and the copy stays a purely local visual: no network object, and it plays exactly
	// when this game says - so a strike's impact lands the moment its ring fills. The caller decides when it goes.
	internal static GameObject MakeLocal(string prefabName, Vector3 pos)
	{
		if (string.IsNullOrEmpty(prefabName) || ZNetScene.instance == null || ZNet.instance == null || ZNet.instance.IsDedicated()) return null;
		GameObject prefab = ZNetScene.instance.GetPrefab(prefabName);
		if (prefab == null || prefab.GetComponent<Character>() != null) return null;
		bool was = ZNetView.m_forceDisableInit;
		ZNetView.m_forceDisableInit = true;
		try { return UnityEngine.Object.Instantiate(prefab, pos, Quaternion.identity); }
		catch (Exception e) { RaidBossPlugin.Log.LogWarning($"effect '{prefabName}' failed: {e.Message}"); return null; }
		finally { ZNetView.m_forceDisableInit = was; }
	}

	internal static void PlayerStatus(string effect, Vector3 pos, float radius, bool remove)
	{
		Player p = Player.m_localPlayer;
		if (p == null || p.IsDead() || string.IsNullOrEmpty(effect) || !RaidBossPlugin.IsOn) return;
		if (radius > 0f && Vector3.Distance(p.transform.position, pos) > radius) return;
		int hash = effect.GetStableHashCode();
		if (remove) p.GetSEMan().RemoveStatusEffect(hash);
		else p.GetSEMan().AddStatusEffect(hash, resetTime: true);
	}

	static string weather = "";
	static Vector3 weatherPos;
	static float weatherRadius, weatherUntil, weatherTimer;
	static bool weatherForced;

	internal static void ForceWeather(string env, Vector3 pos, float radius, float seconds)
	{
		weather = env ?? "";
		weatherPos = pos;
		weatherRadius = radius;
		weatherUntil = Time.time + Mathf.Clamp(seconds, 0f, 3600f);
		weatherTimer = 99f;
	}

	// Called every frame from the plugin; does its work once a second.
	internal static void ClientTick(float dt)
	{
		Auras.Tick(dt);
		weatherTimer += dt;
		if (weatherTimer < 1f) return;
		weatherTimer = 0f;
		if (EnvMan.instance == null) return;
		Player p = Player.m_localPlayer;
		bool want = weather.Length > 0 && Time.time < weatherUntil && p != null && RaidBossPlugin.IsOn && Vector3.Distance(p.transform.position, weatherPos) <= weatherRadius;
		if (want && !weatherForced) { EnvMan.instance.SetForceEnvironment(weather); weatherForced = true; }
		else if (!want && weatherForced) { EnvMan.instance.SetForceEnvironment(""); weatherForced = false; }
	}
}
