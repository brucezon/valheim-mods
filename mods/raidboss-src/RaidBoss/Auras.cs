using System.Collections.Generic;
using UnityEngine;

namespace RaidBoss;

// A creature whose trait carries an element (infuse fire / frost / lightning / poison / spirit) wears that element's
// aura, on every player's game: the game's own status-effect visual for it (vfx_Burning, vfx_Frost, fx_Lightning,
// vfx_Poison, vfx_UndeadBurn), made local and toned down.
//
// Those visuals are made for a person, and the game scales them by twice the creature's radius - on Yagluth that is a
// bonfire. So the particles are never made more than twice their size; the area they are emitted from is stretched
// over the body instead (and the emission raised with it, within limits), the way CLLC fits its own infusion effect
// to a creature's bounds. Brightness and speed are turned down so it reads as an aura, not as the creature burning.
internal static class Auras
{
	sealed class Aura { public string Fx; public GameObject Go; }
	static readonly Dictionary<Character, Aura> auras = new Dictionary<Character, Aura>();
	static readonly List<Character> gone = new List<Character>();
	static float timer;

	static string FxFor(string element) => element switch
	{
		"fire" => "vfx_Burning",
		"frost" => "vfx_Frost",
		"lightning" => "fx_Lightning",
		"poison" => "vfx_Poison",
		"spirit" => "vfx_UndeadBurn",
		_ => "",
	};

	// Called every frame from the plugin (Mechanics.ClientTick); looks around twice a second.
	internal static void Tick(float dt)
	{
		timer += dt;
		if (timer < 0.5f) return;
		timer = 0f;
		if (ZNet.instance == null || ZNet.instance.IsDedicated()) return;
		bool on = RaidBossPlugin.IsOn && RaidBossPlugin.AuraBrightness.Value > 0f;
		foreach (Character c in Character.GetAllCharacters())
		{
			if (c == null || c.IsPlayer() || c.m_nview == null || !c.m_nview.IsValid()) continue;
			Mode mode = on ? Modes.Get(c.m_nview.GetZDO()) : null;
			string fx = mode != null && mode.Infuse > 0f ? FxFor(mode.InfuseType) : "";
			auras.TryGetValue(c, out Aura aura);
			if (aura != null && aura.Fx == fx && aura.Go != null) continue;
			if (aura != null) { if (aura.Go != null) Object.Destroy(aura.Go); auras.Remove(c); }
			if (fx.Length == 0) continue;
			GameObject go = Make(c, fx);
			if (go != null) auras[c] = new Aura { Fx = fx, Go = go };
		}
		gone.Clear();
		foreach (KeyValuePair<Character, Aura> kv in auras) if (kv.Key == null || kv.Value.Go == null) gone.Add(kv.Key);
		foreach (Character c in gone) { if (auras.TryGetValue(c, out Aura a) && a.Go != null) Object.Destroy(a.Go); auras.Remove(c); }
	}

	static GameObject Make(Character c, string fx)
	{
		GameObject go = Mechanics.MakeLocal(fx, c.GetCenterPoint());
		if (go == null) return null;
		// it lives as long as the trait does, not as long as its own timer says
		foreach (TimedDestruction td in go.GetComponentsInChildren<TimedDestruction>(true)) Object.DestroyImmediate(td);
		foreach (AudioSource a in go.GetComponentsInChildren<AudioSource>(true)) a.enabled = false;
		go.transform.SetParent(c.transform, true);
		go.transform.localRotation = Quaternion.identity;

		float body = Mathf.Max(0.5f, c.GetRadius()) * 2f;                  // what the game would scale it by
		float scale = Mathf.Min(body, 2f) * Mathf.Clamp(RaidBossPlugin.AuraSize.Value, 0.25f, 3f);
		float spread = Mathf.Max(1f, body / Mathf.Max(0.01f, scale));      // how much more ground the emission must cover
		float lossy = Mathf.Max(0.01f, c.transform.lossyScale.x);
		go.transform.localScale = Vector3.one * scale / lossy;

		float bright = Mathf.Clamp01(RaidBossPlugin.AuraBrightness.Value / 100f);
		foreach (ParticleSystem ps in go.GetComponentsInChildren<ParticleSystem>(true))
		{
			ParticleSystem.MainModule main = ps.main;
			main.simulationSpeed *= 0.6f;
			main.startColor = Fade(main.startColor, bright);
			ParticleSystem.ShapeModule shape = ps.shape;
			if (shape.enabled)
			{
				shape.radius *= spread;
				shape.scale = new Vector3(shape.scale.x * spread, shape.scale.y * Mathf.Sqrt(spread), shape.scale.z * spread);
			}
			ParticleSystem.EmissionModule em = ps.emission;
			em.rateOverTimeMultiplier *= Mathf.Min(spread * spread, 6f) * Mathf.Lerp(0.5f, 1f, bright);
		}
		foreach (Light l in go.GetComponentsInChildren<Light>(true))
		{
			l.intensity *= bright;
			l.range *= Mathf.Min(spread, 3f);
		}
		return go;
	}

	static ParticleSystem.MinMaxGradient Fade(ParticleSystem.MinMaxGradient g, float k)
	{
		switch (g.mode)
		{
			case ParticleSystemGradientMode.Color:
				Color c = g.color; c.a *= k; return new ParticleSystem.MinMaxGradient(c);
			case ParticleSystemGradientMode.TwoColors:
				Color a = g.colorMin, b = g.colorMax; a.a *= k; b.a *= k; return new ParticleSystem.MinMaxGradient(a, b);
			default:
				return g;   // gradients keep their own fade; the emission cut still thins them
		}
	}
}
