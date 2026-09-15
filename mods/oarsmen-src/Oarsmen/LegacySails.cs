using System.Runtime.CompilerServices;
using BepInEx.Configuration;
using HarmonyLib;
using UnityEngine;

namespace Oarsmen;

// 5 - Legacy sails: make pre-1.0 modded sails deploy on Valheim 1.0.
//
// Before 1.0 a ship's sail was a Unity Cloth under m_sailObject and Ship.UpdateSailSize scaled that
// object's height every frame: 0.1 furled (Stop, Slow, Back), 0.5 at Half, 1.0 at Full. 1.0 replaced the
// whole mechanism - MagicaCloth, a sail-bottom transform sliding between furled / mid / unfurled points,
// a blend curve - and gates it behind a new m_hasSail flag on the prefab. A boat built for the old
// system (OdinShip 0.7.9's hulls, for one) still has m_sailObject but none of the new fields, so on 1.0
// m_hasSail is false and nothing ever moves its sail: it accepts Half and Full and sits there furled.
//
// This runs the old routine for exactly those ships: m_hasSail false and an m_sailObject present. Ships
// with the new setup are untouched. Purely visual and per client, like the oars. Turn it off the day the
// boat's author migrates the prefabs; the shim then has nothing to act on anyway.
internal static class LegacySails
{
	internal static ConfigEntry<OarsmenPlugin.Toggle> Enabled;

	private sealed class State
	{
		public bool looked;
		public Cloth cloth;
		public bool wasInPosition;
	}

	private static readonly ConditionalWeakTable<Ship, State> states = new();

	internal static bool IsLegacy(Ship ship) => ship != null && !ship.m_hasSail && ship.m_sailObject != null;

	internal static string Describe(Ship ship)
	{
		if (ship == null) return "";
		string sailObj = ship.m_sailObject != null ? ship.m_sailObject.name : "none";
		string cloth = "n/a";
		if (ship.m_sailObject != null)
		{
			Cloth c = ship.m_sailObject.GetComponentInChildren<Cloth>(true);
			cloth = c != null ? $"legacy Cloth on '{c.name}' ({(c.enabled ? "enabled" : "disabled")})" : "no legacy Cloth";
		}
		string scale = ship.m_sailObject != null ? ship.m_sailObject.transform.localScale.y.ToString("F2") : "-";
		return $"hasSail(1.0) {ship.m_hasSail} sailObject '{sailObj}' {cloth} sailScaleY {scale} legacyShim {(IsLegacy(ship) ? (Enabled.Value == OarsmenPlugin.Toggle.On ? "active" : "needed but Off") : "not needed")}";
	}

	// UpdateSailSize runs on every client each fixed update, before the owner check, which is what the
	// visuals need. With m_hasSail false vanilla's body is skipped entirely, so this postfix is the whole
	// sail update for a legacy ship.
	[HarmonyPatch(typeof(Ship), "UpdateSailSize")]
	private static class UpdateSailSizePatch
	{
		private static void Postfix(Ship __instance, float dt)
		{
			if (Enabled == null || Enabled.Value != OarsmenPlugin.Toggle.On || !IsLegacy(__instance)) return;
			GameObject sail = __instance.m_sailObject;
			State st = states.GetOrCreateValue(__instance);
			if (!st.looked)
			{
				st.looked = true;
				st.cloth = sail.GetComponentInChildren<Cloth>(true);
			}

			Ship.Speed speed = __instance.m_speed;
			float target = speed switch
			{
				Ship.Speed.Half => 0.5f,
				Ship.Speed.Full => 1f,
				_ => 0.1f,
			};
			Vector3 scale = sail.transform.localScale;
			bool inPosition = Mathf.Abs(scale.y - target) < 0.01f;
			if (!inPosition)
			{
				scale.y = Mathf.MoveTowards(scale.y, target, dt);
				sail.transform.localScale = scale;
			}
			if (st.cloth != null)
			{
				// Pre-1.0 behaviour: the cloth simulates only while the sail is set and has finished moving;
				// furled, it is switched off so a bunched sail does not flap.
				bool furled = speed == Ship.Speed.Stop || speed == Ship.Speed.Slow || speed == Ship.Speed.Back;
				if (furled)
				{
					if (inPosition && st.cloth.enabled) st.cloth.enabled = false;
				}
				else if (inPosition)
				{
					if (!st.wasInPosition) { st.cloth.enabled = false; st.cloth.enabled = true; } // reset the simulation at the new size
					else if (!st.cloth.enabled) st.cloth.enabled = true;
				}
			}
			st.wasInPosition = inPosition;
		}
	}
}
