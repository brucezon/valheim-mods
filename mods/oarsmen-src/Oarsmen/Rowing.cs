using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace Oarsmen;

internal static class Rowing
{
	private static readonly HashSet<string> ships = new(StringComparer.OrdinalIgnoreCase);
	private static string shipsParsed;

	internal static bool IsRowingShip(GameObject go)
	{
		string list = OarsmenPlugin.Ships.Value ?? "";
		if (!ReferenceEquals(list, shipsParsed))
		{
			ships.Clear();
			foreach (string part in list.Split(',', ';'))
			{
				if (part.Trim().Length > 0) ships.Add(part.Trim());
			}
			shipsParsed = list;
		}
		return ships.Contains(Utils.GetPrefabName(go));
	}

	[HarmonyPatch(typeof(Ship), "Awake")]
	private static class ShipAwakePatch
	{
		private static void Postfix(Ship __instance)
		{
			if (__instance.GetComponent<OarsBehaviour>() == null && IsRowingShip(__instance.gameObject))
			{
				__instance.gameObject.AddComponent<OarsBehaviour>();
			}
		}
	}

	// Vanilla applies its paddle force at the end of CustomFixedUpdate, owner only. Add the rowers' share
	// at the same point, same direction, same steering falloff, so rowing feels like a stronger paddle.
	[HarmonyPatch(typeof(Ship), nameof(Ship.CustomFixedUpdate))]
	private static class RowForcePatch
	{
		private static void Postfix(Ship __instance, float fixedDeltaTime)
		{
			if (OarsmenPlugin.Enabled.Value != OarsmenPlugin.Toggle.On) return;
			if (__instance.m_nview == null || !__instance.m_nview.IsValid() || !__instance.m_nview.IsOwner()) return;
			if (__instance.m_body == null || __instance.m_players.Count == 0) return;
			OarsBehaviour oars = __instance.GetComponent<OarsBehaviour>();
			if (oars == null) return;
			Ship.Speed speed = __instance.m_speed;
			bool paddling = speed == Ship.Speed.Slow;
			bool sailing = speed == Ship.Speed.Half || speed == Ship.Speed.Full;
			if (!paddling && !(sailing && OarsmenPlugin.RowUnderSail.Value == OarsmenPlugin.Toggle.On)) return;
			int rowers = Math.Min(oars.RowerCount, Math.Max(0, OarsmenPlugin.MaxRowers.Value));
			if (rowers <= 0) return;
			float share = Math.Max(0f, OarsmenPlugin.BonusPerRower.Value) * rowers;
			if (share <= 0f) return;
			Vector3 force = __instance.transform.forward * (__instance.m_backwardForce * (1f - Mathf.Abs(__instance.m_rudderValue)) * share);
			Vector3 at = __instance.transform.position + __instance.transform.forward * __instance.m_stearForceOffset;
			__instance.m_body.AddForceAtPosition(force * (__instance.m_body.mass * fixedDeltaTime), at, ForceMode.Impulse);
		}
	}
}

// One per rowing ship. Knows the benches, decides who is rowing, and draws the oars.
internal sealed class OarsBehaviour : MonoBehaviour
{
	private sealed class Bench
	{
		public Chair chair;
		public Transform seat;
		public float side;          // +1 right, -1 left (ship-local X of the seat)
		public GameObject oar;      // pivot object, child of the ship
		public bool occupied;
		public float phase;
	}

	private Ship ship;
	private readonly List<Bench> benches = new();
	private Material oarMaterial;
	private float scanTimer;
	private int rowerCount;
	private float strokeTime;
	private float builtLength = -1f, builtInboard, builtThickness, builtBladeLength, builtBladeWidth;

	internal int RowerCount => rowerCount;
	internal int BenchCount => benches.Count;

	private void Awake()
	{
		ship = GetComponent<Ship>();
		Transform t = transform;
		foreach (Chair chair in GetComponentsInChildren<Chair>(true))
		{
			Transform seat = chair.m_attachPoint != null ? chair.m_attachPoint : chair.transform;
			float x = t.InverseTransformPoint(seat.position).x;
			benches.Add(new Bench { chair = chair, seat = seat, side = x >= 0f ? 1f : -1f, phase = UnityEngine.Random.Range(0f, 0.3f) });
		}
		// Bow to stern, so "first rower" is the front bench.
		benches.Sort((a, b) => t.InverseTransformPoint(b.seat.position).z.CompareTo(t.InverseTransformPoint(a.seat.position).z));
		OarsmenPlugin.Log.LogInfo($"Oarsmen: {Utils.GetPrefabName(gameObject)} has {benches.Count} benches");
	}

	private void Update()
	{
		scanTimer += Time.deltaTime;
		if (scanTimer >= 0.25f)
		{
			scanTimer = 0f;
			Scan();
		}
		UpdateOars(Time.deltaTime);
	}

	// A bench is occupied when a player aboard is at its seat and in the bench's attach animation.
	// Both are synced: the position is the player's, the animation flag goes through ZSyncAnimation.
	private void Scan()
	{
		int count = 0;
		foreach (Bench b in benches)
		{
			b.occupied = false;
			foreach (Player p in ship.m_players)
			{
				if (p == null) continue;
				if (Vector3.Distance(p.transform.position, b.seat.position) > 1.2f) continue;
				bool attached = p == Player.m_localPlayer ? p.IsAttached() : (p.m_animator != null && p.m_animator.GetBool(b.chair.m_attachAnimation));
				if (!attached) continue;
				b.occupied = true;
				break;
			}
			if (b.occupied) count++;
		}
		if (count != rowerCount)
		{
			rowerCount = count;
			if (OarsmenPlugin.LogRowers.Value == OarsmenPlugin.Toggle.On)
			{
				OarsmenPlugin.Log.LogInfo($"Oarsmen: {Utils.GetPrefabName(gameObject)} rowers {count}/{benches.Count}");
			}
		}
	}

	private void UpdateOars(float dt)
	{
		bool show = OarsmenPlugin.ShowOars.Value == OarsmenPlugin.Toggle.On && OarsmenPlugin.Enabled.Value == OarsmenPlugin.Toggle.On;
		if (!show)
		{
			foreach (Bench b in benches) if (b.oar != null) b.oar.SetActive(false);
			return;
		}
		if (NeedsRebuild()) foreach (Bench b in benches) BuildOar(b);

		Ship.Speed speed = ship.m_speed;
		bool rowing = speed == Ship.Speed.Slow || ((speed == Ship.Speed.Half || speed == Ship.Speed.Full) && OarsmenPlugin.RowUnderSail.Value == OarsmenPlugin.Toggle.On);
		if (rowing) strokeTime += dt;

		float[] holes = ParseHoles(OarsmenPlugin.HolePositions.Value);
		Transform t = transform;
		foreach (Bench b in benches)
		{
			if (b.oar == null) continue;
			b.oar.SetActive(b.occupied);
			if (!b.occupied) continue;

			// Pivot: beside the bench at the hull, or snapped to the nearest oar hole along the ship.
			Vector3 seat = t.InverseTransformPoint(b.seat.position);
			Vector3 pivot = seat + new Vector3(b.side * OarsmenPlugin.PivotOutward.Value, OarsmenPlugin.PivotUp.Value, OarsmenPlugin.PivotForward.Value);
			if (holes.Length > 0)
			{
				float best = holes[0];
				foreach (float h in holes) if (Mathf.Abs(h - pivot.z) < Mathf.Abs(best - pivot.z)) best = h;
				pivot.z = best;
			}
			b.oar.transform.localPosition = pivot;

			// Oar's +Z points outboard from the pivot. Rowing: swing fore-aft in time with the rudder paddle
			// (vanilla wiggles the rudder at sin(t * 6)), blade dipped. Not rowing: held level and still.
			float sweep = 0f, dip;
			if (rowing)
			{
				float s = Mathf.Sin((strokeTime + b.phase) * 6f);
				sweep = s * OarsmenPlugin.StrokeSweep.Value * 0.5f;
				// Blade in the water on the pull (s < 0 -> moving aft), lifted on the recovery.
				dip = OarsmenPlugin.BladeDip.Value * (s < 0f ? 1f : 0.35f);
			}
			else
			{
				dip = -OarsmenPlugin.StowedAngle.Value;
			}
			// yaw: outboard (+90 right / -90 left) plus the fore-aft sweep; pitch: blade down into the water.
			float yaw = b.side * 90f + (-b.side * sweep);
			b.oar.transform.localRotation = Quaternion.Euler(dip, yaw, 0f);
		}
	}

	private bool NeedsRebuild()
	{
		bool changed = Math.Abs(builtLength - OarsmenPlugin.OarLength.Value) > 0.001f || Math.Abs(builtInboard - OarsmenPlugin.OarInboard.Value) > 0.001f
			|| Math.Abs(builtThickness - OarsmenPlugin.OarThickness.Value) > 0.001f || Math.Abs(builtBladeLength - OarsmenPlugin.BladeLength.Value) > 0.001f
			|| Math.Abs(builtBladeWidth - OarsmenPlugin.BladeWidth.Value) > 0.001f;
		if (changed)
		{
			builtLength = OarsmenPlugin.OarLength.Value; builtInboard = OarsmenPlugin.OarInboard.Value; builtThickness = OarsmenPlugin.OarThickness.Value;
			builtBladeLength = OarsmenPlugin.BladeLength.Value; builtBladeWidth = OarsmenPlugin.BladeWidth.Value;
		}
		return changed;
	}

	// Shaft: a cylinder along the pivot's +Z from -inboard to (length - inboard). Blade: a flat box at the outboard end.
	private void BuildOar(Bench b)
	{
		if (b.oar != null) Destroy(b.oar);
		float length = Mathf.Max(1f, builtLength), inboard = Mathf.Clamp(builtInboard, 0f, length - 0.5f);
		float thick = Mathf.Max(0.02f, builtThickness), bladeLen = Mathf.Clamp(builtBladeLength, 0.1f, length - inboard), bladeW = Mathf.Max(0.05f, builtBladeWidth);

		GameObject root = new GameObject("Oarsmen_oar");
		root.transform.SetParent(transform, false);
		root.layer = gameObject.layer;

		GameObject shaft = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
		Destroy(shaft.GetComponent<Collider>());
		shaft.name = "shaft";
		shaft.transform.SetParent(root.transform, false);
		shaft.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);         // cylinder Y -> root Z
		shaft.transform.localScale = new Vector3(thick, length * 0.5f, thick);  // cylinder height is 2 units
		shaft.transform.localPosition = new Vector3(0f, 0f, length * 0.5f - inboard);
		shaft.layer = gameObject.layer;

		GameObject blade = GameObject.CreatePrimitive(PrimitiveType.Cube);
		Destroy(blade.GetComponent<Collider>());
		blade.name = "blade";
		blade.transform.SetParent(root.transform, false);
		blade.transform.localScale = new Vector3(bladeW, thick * 0.5f, bladeLen);
		blade.transform.localPosition = new Vector3(0f, 0f, length - inboard - bladeLen * 0.5f);
		blade.layer = gameObject.layer;

		Material m = GetOarMaterial();
		if (m != null)
		{
			shaft.GetComponent<Renderer>().sharedMaterial = m;
			blade.GetComponent<Renderer>().sharedMaterial = m;
		}
		b.oar = root;
		root.SetActive(false);
	}

	// Borrow the hull's wood material so the oars match the ship in every light; fall back to plain brown.
	private Material GetOarMaterial()
	{
		if (oarMaterial != null) return oarMaterial;
		Material first = null;
		foreach (MeshRenderer r in GetComponentsInChildren<MeshRenderer>(true))
		{
			if (r.sharedMaterial == null || r.name.StartsWith("Oarsmen")) continue;
			first ??= r.sharedMaterial;
			string n = r.sharedMaterial.name.ToLowerInvariant();
			if (n.Contains("wood") || n.Contains("hull") || n.Contains("ship") || n.Contains("boat")) { oarMaterial = r.sharedMaterial; return oarMaterial; }
		}
		if (first != null) { oarMaterial = first; return oarMaterial; }
		Shader s = Shader.Find("Standard");
		if (s != null)
		{
			oarMaterial = new Material(s) { color = new Color(0.45f, 0.30f, 0.16f) };
		}
		return oarMaterial;
	}

	private static float[] ParseHoles(string list)
	{
		if (string.IsNullOrWhiteSpace(list)) return Array.Empty<float>();
		List<float> vals = new();
		foreach (string part in list.Split(',', ';'))
		{
			if (float.TryParse(part.Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float v)) vals.Add(v);
		}
		return vals.ToArray();
	}

	internal string Describe()
	{
		List<string> parts = new();
		for (int i = 0; i < benches.Count; i++)
		{
			Bench b = benches[i];
			parts.Add($"bench{i + 1}({(b.side > 0 ? "R" : "L")}):{(b.occupied ? "rowing" : "empty")}");
		}
		return $"rowers {rowerCount}/{benches.Count} setting {ship.m_speed} speed {ship.GetSpeed():F2} m/s | {string.Join(" ", parts)}";
	}
}
