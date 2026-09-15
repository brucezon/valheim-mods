using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace Oarsmen;

internal static class Rowing
{
	private static readonly HashSet<string> ships = new(StringComparer.OrdinalIgnoreCase);
	private static string shipsParsed;
	private static readonly HashSet<string> excluded = new(StringComparer.OrdinalIgnoreCase);
	private static string excludedParsed;

	// What makes a ship rowable is having somewhere to sit, so a boat from another mod (OdinShip's canoes,
	// say) rows the moment it exists without anyone naming it in the config. The tiller is a ShipControlls,
	// not a Chair, so the helm is never mistaken for a bench; a boat whose only seat IS a chair at the helm
	// is what 'Excluded ships' is for.
	//
	// Only the bench scan happens at Awake, because a ship's seats cannot change. The two config lists are
	// re-read every frame instead, so editing them takes effect on boats that already exist - the rest of
	// the config is live and these should not be the exception.
	internal static bool IsRowingShip(GameObject go) => HasBenches(go);

	internal static bool AllowedByLists(string prefabName)
	{
		Parse(OarsmenPlugin.ExcludedShips.Value, excluded, ref excludedParsed);
		if (excluded.Contains(prefabName)) return false;
		Parse(OarsmenPlugin.Ships.Value, ships, ref shipsParsed);
		return ships.Count == 0 || ships.Contains(prefabName);
	}

	// Vanilla applies every one of its forces - buoyancy, damping, paddle, rudder - inside a single gate,
	// and that gate is the hull still being in the water (Ship.CustomFixedUpdate, `if (!(num2 >
	// m_disableLevel))`). Lift a boat off a wave crest and vanilla stops pushing it entirely, leaving
	// gravity and momentum to finish the jump. A postfix does not inherit that gate, so without this check
	// the crew would carry on rowing a boat through mid-air - and along transform.forward, which points at
	// the sky when the bow is pitched up, so they would be rowing it higher.
	//
	// Mirrored exactly rather than approximated with the centre point alone: vanilla averages five samples
	// across the float collider, and a pitching hull is precisely when one sample and five disagree. The
	// WaterVolume fields are lookup caches, not accumulated state, so re-reading them here is free and
	// cannot disturb vanilla's own call earlier in the same tick.
	private static bool InWater(Ship ship)
	{
		if (ship.m_floatCollider == null || ship.m_body == null) return false;
		Vector3 com = ship.m_body.worldCenterOfMass;
		Transform fc = ship.m_floatCollider.transform;
		Vector3 size = ship.m_floatCollider.size;
		Vector3 pos = fc.position, fwd = fc.forward, right = fc.right;
		float average = (Floating.GetWaterLevel(com, ref ship.m_previousCenter)
			+ Floating.GetWaterLevel(pos - right * (size.x / 2f), ref ship.m_previousLeft)
			+ Floating.GetWaterLevel(pos + right * (size.x / 2f), ref ship.m_previousRight)
			+ Floating.GetWaterLevel(pos + fwd * (size.z / 2f), ref ship.m_previousForward)
			+ Floating.GetWaterLevel(pos - fwd * (size.z / 2f), ref ship.m_previousBack)) / 5f;
		return !(com.y - average - ship.m_waterLevelOffset > ship.m_disableLevel);
	}

	// How much bite the oars still have under sail: 1 at rest, falling as the square of the speed the
	// blade has left and reaching 0 once the hull outruns it at 'Rowing cuts out above'. Shared by the
	// force patch and the animation on purpose - if the crew is not moving the ship it should not look
	// like it is, and two copies of this curve would drift apart the first time one was tuned.
	internal static float SailBite(Ship ship)
	{
		float cutout = OarsmenPlugin.RowCutoutSpeed.Value;
		if (cutout <= 0f || ship.m_body == null) return 1f;
		float alongHull = Math.Abs(Vector3.Dot(ship.m_body.linearVelocity, ship.transform.forward));
		float left = Mathf.Clamp01(1f - alongHull / cutout);
		return left * left;
	}

	private static bool HasBenches(GameObject go)
	{
		foreach (Chair chair in go.GetComponentsInChildren<Chair>(true))
		{
			if (chair != null) return true;
		}
		return false;
	}

	// ConfigEntry hands back the same string instance until the value changes, so a reference check is
	// enough to keep this off the per-ship path.
	private static void Parse(string list, HashSet<string> into, ref string cache)
	{
		list ??= "";
		if (ReferenceEquals(list, cache)) return;
		into.Clear();
		foreach (string part in list.Split(',', ';'))
		{
			if (part.Trim().Length > 0) into.Add(part.Trim());
		}
		cache = list;
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

	// Vanilla applies its paddle force and its rudder push at the end of CustomFixedUpdate, owner only,
	// both at the stern (transform.position + forward * m_stearForceOffset, where the offset is negative).
	// Add the rowers' share at the same point and in the same shape, so rowing reads as a stronger crew
	// on the same boat rather than a second set of physics.
	//
	// Steering deliberately follows vanilla's own gating: vanilla only gives the rudder a push in Slow and
	// Back, and under sail a ship turns purely on the velocity term. Rowers respect that, so the sailing
	// game is untouched - the crew helps the helmsman paddle and manoeuvre, not tack.
	[HarmonyPatch(typeof(Ship), nameof(Ship.CustomFixedUpdate))]
	private static class RowForcePatch
	{
		private static void Postfix(Ship __instance, float fixedDeltaTime)
		{
			if (OarsmenPlugin.Enabled.Value != OarsmenPlugin.Toggle.On) return;
			if (__instance.m_nview == null || !__instance.m_nview.IsValid() || !__instance.m_nview.IsOwner()) return;
			if (__instance.m_body == null || __instance.m_players.Count == 0) return;
			OarsBehaviour oars = __instance.GetComponent<OarsBehaviour>();
			if (oars == null || !oars.Active) return;
			// Oars in air move nothing. Vanilla stops pushing here too, so this keeps the crew inside the
			// same gate rather than rowing the hull through the top of a wave.
			if (!InWater(__instance)) return;
			Ship.Speed speed = __instance.m_speed;
			bool paddling = speed == Ship.Speed.Slow || speed == Ship.Speed.Back;
			bool sailing = speed == Ship.Speed.Half || speed == Ship.Speed.Full;
			if (!paddling && !(sailing && OarsmenPlugin.RowUnderSail.Value == OarsmenPlugin.Toggle.On)) return;
			// 0 means uncapped, so every bench on a big hull pulls. Guard the ordering here: a naive
			// Min(count, cap) would read 0 as "nobody rows" and silently disable the whole mod.
			int cap = OarsmenPlugin.MaxRowers.Value;
			int rowers = cap > 0 ? Math.Min(oars.RowerCount, cap) : oars.RowerCount;
			if (rowers <= 0) return;

			// Reverse pulls the same oars the other way: vanilla's Back term is its Slow term negated.
			float dir = speed == Ship.Speed.Back ? -1f : 1f;
			Vector3 add = Vector3.zero;

			float thrust = Math.Max(0f, OarsmenPlugin.BonusPerRower.Value) * rowers;

			// Under sail the oars are fighting a hull the wind is already driving. A blade only bites while
			// it is moving through the water faster than the hull is, so thrust goes as the square of the
			// speed the blade has left - (1 - v/V)^2, nothing at all at V. Squared rather than linear
			// because that is what a blade does, and because linear leaves a big crew still usefully
			// rowing at cruising speed, which is the thing this is meant to stop.
			//
			// Paddle and reverse are deliberately exempt: vanilla's own paddle force is flat and
			// speed-independent there, with quadratic hull drag doing the limiting, so matching it keeps
			// those modes pure augmentation. Under sail vanilla applies no paddle force at all, so this is
			// our model to choose rather than vanilla's to contradict.
			if (sailing && thrust > 0f) thrust *= SailBite(__instance);

			if (thrust > 0f)
			{
				add += __instance.transform.forward * (dir * __instance.m_backwardForce * (1f - Mathf.Abs(__instance.m_rudderValue)) * thrust);
			}

			// Rowers amplify the helmsman's rudder rather than choosing a direction, so this is zero with
			// the rudder centred and needs no per-rower control: sitting down is the whole opt-in.
			float steer = Math.Max(0f, OarsmenPlugin.SteerPerRower.Value) * rowers;
			// Ceiling on what the crew may ask for. Uncapped crews made this necessary: twelve rowers would
			// otherwise reach nearly three times the hull's own rudder force. Only ever trims force we are
			// adding, so a boat rowing vanilla cannot be affected.
			float steerCap = OarsmenPlugin.MaxSteerShare.Value;
			if (steerCap > 0f) steer = Math.Min(steer, steerCap);
			if (paddling && steer > 0f)
			{
				add += __instance.transform.right * (__instance.m_stearForce * (0f - __instance.m_rudderValue) * dir * steer);
			}

			if (add == Vector3.zero) return;
			Vector3 at = __instance.transform.position + __instance.transform.forward * __instance.m_stearForceOffset;
			__instance.m_body.AddForceAtPosition(add * (__instance.m_body.mass * fixedDeltaTime), at, ForceMode.Impulse);

			// Backstop on the result, for anything the share ceiling does not catch. Angular velocity is the
			// quantity that actually misbehaves: vanilla overwrites linear velocity every tick from a
			// quadratic drag model, but rotation is only damped, and gently, so torque accumulates.
			// Only the yaw about the ship's own up axis is limited, and only the excess is taken off, so
			// wave roll and pitch survive untouched - clamping the whole vector would flatten the sea.
			float maxTurn = OarsmenPlugin.MaxTurnRate.Value;
			if (maxTurn > 0f)
			{
				Vector3 spin = __instance.m_body.angularVelocity;
				Vector3 up = __instance.transform.up;
				float yaw = Vector3.Dot(spin, up);
				float limit = maxTurn * Mathf.Deg2Rad;
				if (Mathf.Abs(yaw) > limit)
				{
					__instance.m_body.angularVelocity = spin - up * (yaw - Mathf.Sign(yaw) * limit);
				}
			}
		}
	}

	// The tiller's hover text gains a rower count, so the helmsman can see the crew without the console.
	// Local and cosmetic; the count comes from the same synced bench state every client already reads.
	[HarmonyPatch(typeof(ShipControlls), nameof(ShipControlls.GetHoverText))]
	private static class HoverTextPatch
	{
		private static void Postfix(ShipControlls __instance, ref string __result)
		{
			if (OarsmenPlugin.Enabled.Value != OarsmenPlugin.Toggle.On) return;
			if (OarsmenPlugin.ShowRowersOnTiller.Value != OarsmenPlugin.Toggle.On) return;
			if (__instance.m_ship == null || string.IsNullOrEmpty(__result)) return;
			// Out of reach, vanilla returns only the greyed "too far" string; leave that one alone.
			if (!__instance.InUseDistance(Player.m_localPlayer)) return;
			OarsBehaviour oars = __instance.m_ship.GetComponent<OarsBehaviour>();
			if (oars == null || oars.BenchCount <= 0) return;
			__result += $"\nRowers {oars.RowerCount}/{oars.BenchCount}";
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
		public bool simulated;      // filled by the "Simulate rowers" testing aid, not a real player
		public float phase;
	}

	// Seconds of delay between one bench and the next, bow to stern. Small enough to read as a crew
	// pulling together rather than as rowers each doing their own thing.
	private const float StrokeStagger = 0.04f;

	private Ship ship;
	private string prefabName;
	private readonly List<Bench> benches = new();

	// Whether the config lists currently let this hull row. Re-evaluated every frame so the lists behave
	// like every other setting: edit them and boats already in the water follow.
	internal bool Active { get; private set; }
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
		prefabName = Utils.GetPrefabName(gameObject);
		Transform t = transform;
		foreach (Chair chair in GetComponentsInChildren<Chair>(true))
		{
			Transform seat = chair.m_attachPoint != null ? chair.m_attachPoint : chair.transform;
			float x = t.InverseTransformPoint(seat.position).x;
			benches.Add(new Bench { chair = chair, seat = seat, side = x >= 0f ? 1f : -1f });
		}
		// Bow to stern, so "first rower" is the front bench.
		benches.Sort((a, b) => t.InverseTransformPoint(b.seat.position).z.CompareTo(t.InverseTransformPoint(a.seat.position).z));
		// A crew rows in time - that is the whole point of calling the stroke - so the benches share one
		// clock and keep only a slight bow-to-stern ripple. Derived from the sorted order rather than
		// Random, which previously gave every client its own stroke pattern for the same ship.
		for (int i = 0; i < benches.Count; i++) benches[i].phase = i * StrokeStagger;
		OarsmenPlugin.Log.LogInfo($"Oarsmen: {Utils.GetPrefabName(gameObject)} has {benches.Count} benches");
	}

	private void Update()
	{
		bool allowed = Rowing.AllowedByLists(prefabName);
		if (allowed != Active)
		{
			Active = allowed;
			if (!Active)
			{
				// Excluded while afloat: drop the oars and the crew so the hull goes back to vanilla.
				foreach (Bench b in benches) { b.occupied = false; b.simulated = false; if (b.oar != null) b.oar.SetActive(false); }
				rowerCount = 0;
			}
		}
		if (!Active) return;

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
			b.simulated = false;
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

		// Testing aid: top the crew up with phantoms so one player can tune a full ship's worth of oars
		// and forces. Real rowers are counted first, so this is a floor on the crew rather than an extra.
		// Gated on somebody being aboard, both so derelict boats stay still and so it cannot quietly move
		// ships across a whole world if it is ever left switched on.
		int target = Math.Min(OarsmenPlugin.SimulatedRowers.Value, benches.Count);
		if (count < target && ship.m_players.Count > 0)
		{
			foreach (Bench b in benches)
			{
				if (count >= target) break;
				if (b.occupied) continue;
				b.occupied = true;
				b.simulated = true;
				count++;
			}
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
		bool rowing = speed == Ship.Speed.Slow || speed == Ship.Speed.Back
			|| ((speed == Ship.Speed.Half || speed == Ship.Speed.Full) && OarsmenPlugin.RowUnderSail.Value == OarsmenPlugin.Toggle.On);
		if (rowing) strokeTime += dt;

		// Which way each bank pulls. Ahead normally, astern when the ship is backing, and on a hard rudder
		// the inside bank eases off and drops through zero into a back-water stroke while the outside bank
		// keeps pulling - how a crew actually pivots a longship. Positive m_rudderValue turns the bow to
		// starboard (vanilla's steer force is right * m_stearForce * -rudder, applied at the stern), so the
		// starboard bank is the inside one there.
		// One shared frequency for both banks even when they pull opposite ways: differing rates would drift
		// the crew out of time, and rowing in time is the point. Vanilla reverses its own steering paddle the
		// same way, sin(t * -3) backing against sin(t * 6) ahead.
		float dirSign = speed == Ship.Speed.Back ? -1f : 1f;
		float turnBias = Mathf.Max(0f, OarsmenPlugin.TurnStrokeBias.Value) * ship.m_rudderValue;

		// Under sail the crew's bite runs out as the hull picks up speed, so the oars ease off and come up
		// out of the water rather than thrashing away achieving nothing. Same curve the force uses.
		float bite = (speed == Ship.Speed.Half || speed == Ship.Speed.Full) ? Rowing.SailBite(ship) : 1f;

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
			// Signed: + pulls the ship ahead, - backs water. Magnitude is how hard, so a bank the rudder has
			// cancelled out barely moves its oars. The whole split is multiplied by dirSign, not just the
			// base: vanilla negates its steer force when backing (num17 = -1), so the same rudder swings the
			// bow the other way and the banks have to swap with it, or the oars pivot against the boat.
			float power = Mathf.Clamp(dirSign * (1f - turnBias * b.side), -1f, 1f);
			float effort = Mathf.Abs(power) * bite;

			float sweep = 0f, dip;
			if (rowing && effort > 0.02f)
			{
				float s = Mathf.Sin((strokeTime + b.phase) * 6f);
				sweep = s * OarsmenPlugin.StrokeSweep.Value * 0.5f * effort;
				// The blade is in the water on the drive and lifted on the recovery. Pulling ahead the drive
				// is the aft half of the swing (s < 0); backing water it is the forward half, so the same
				// swing pushes the ship the other way.
				bool drive = power >= 0f ? s < 0f : s > 0f;
				// Blend towards the stowed angle as the effort falls away, so an oar that has stopped
				// earning its keep - outrun by the hull under sail, or on the bank a hard rudder has
				// cancelled - shortens its stroke and lifts clear instead of stopping dead at a threshold.
				dip = Mathf.Lerp(-OarsmenPlugin.StowedAngle.Value, OarsmenPlugin.BladeDip.Value * (drive ? 1f : 0.35f), effort);
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
			string state = b.simulated ? "SIMULATED" : b.occupied ? "rowing" : "empty";
			parts.Add($"bench{i + 1}({(b.side > 0 ? "R" : "L")}):{state}");
		}
		int fake = 0;
		foreach (Bench b in benches) if (b.simulated) fake++;
		string note = fake > 0 ? $" ({fake} simulated)" : "";
		// Turn rate is printed so the Max turn rate cap can be set from a measurement rather than a guess:
		// swing the rudder hard with no crew to read what the hull does on its own.
		float turn = ship.m_body != null ? Vector3.Dot(ship.m_body.angularVelocity, transform.up) * Mathf.Rad2Deg : 0f;
		return $"rowers {rowerCount}/{benches.Count}{note} setting {ship.m_speed} speed {ship.GetSpeed():F2} m/s rudder {ship.m_rudderValue:F2} turn {turn:F1} deg/s | {string.Join(" ", parts)}";
	}
}
