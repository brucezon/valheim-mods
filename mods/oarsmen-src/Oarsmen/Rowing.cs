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
	private static readonly HashSet<string> rowAnims = new(StringComparer.OrdinalIgnoreCase);
	private static string rowAnimsParsed = null;
	private static readonly HashSet<string> holdAnims = new(StringComparer.OrdinalIgnoreCase);
	private static string holdAnimsParsed = null;
	private static float[] holes = Array.Empty<float>();
	private static string holesParsed = null;

	// A dedicated server (or any -nographics process) has nobody to draw for. The rowing force is owner-only
	// and a dedicated server never owns a ship while anyone is aboard, so it only ever needs the bench scan;
	// building primitive oars there would be wasted work every frame per ship.
	internal static bool Headless =>
		SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null
		|| (ZNet.instance != null && ZNet.instance.IsDedicated());

	// Bumped whenever either seat list changes, so hulls already afloat rebuild the set of benches they
	// found at Awake instead of keeping it. Every other setting is live; these should not be the exception.
	internal static int SeatRules { get; private set; }

	// What makes a ship rowable is having somewhere to sit, so a boat from another mod (OdinShip's canoes,
	// say) rows the moment it exists without anyone naming it in the config.
	//
	// A seat is not the same thing as a rowing bench, though. The Longship carries seven Chairs: the four
	// benches, the helm seat, and two places a passenger holds fast - the mast and the figurehead. Vanilla
	// tells them apart by the animation it puts the player into: 'attach_sitship' on the benches,
	// 'attach_chair' at the helm, 'attach_mast' and 'attach_dragon' where you are hanging on rather than
	// sitting down. 0.2.0 took every Chair, so a Longship rowed with seven oars on a four-oared hull, one
	// of them out at the figurehead above head height, and a crew of seven pulling a boat built for four.
	//
	// The helm is caught by position rather than by its animation, because 'attach_chair' is an ordinary
	// seat animation a modded hull may well use for a real bench. Whatever it is called, the chair sharing
	// the tiller's attach point is the helmsman's - on the Longship the two are 8 cm apart - and someone
	// has to steer.
	//
	// Only the bench scan happens at Awake, because a ship's seats cannot change. The config lists are
	// re-read every frame instead, so editing them takes effect on boats that already exist.
	internal static bool IsRowingShip(GameObject go) => CountBenches(go) > 0;

	// Distance within which a chair is taken to be the tiller's own seat. The Longship's helm chair sits
	// 8 cm from the tiller attach point and its nearest real bench is 4 m away, so there is no hull where
	// this is a close call.
	private const float HelmRadius = 0.75f;

	internal static bool IsRowingBench(GameObject shipGo, Chair chair)
	{
		if (chair == null) return false;
		RefreshSeatRules();
		string anim = chair.m_attachAnimation ?? "";
		if (holdAnims.Contains(anim)) return false;
		if (rowAnims.Count > 0 && !rowAnims.Contains(anim)) return false;
		return !IsHelm(shipGo, chair);
	}

	// Vanilla's tiller is a ShipControlls rather than a Chair, but it parks the helmsman on a Chair at the
	// same attach point, so the helm is found by asking where the tiller puts you rather than by name.
	// m_shipControlls may not be filled in yet when this runs from Ship.Awake, hence the direct search.
	private static bool IsHelm(GameObject shipGo, Chair chair)
	{
		if (shipGo == null) return false;
		Ship ship = shipGo.GetComponent<Ship>();
		ShipControlls ctrl = ship != null ? ship.m_shipControlls : null;
		if (ctrl == null) ctrl = shipGo.GetComponentInChildren<ShipControlls>(true);
		if (ctrl == null) return false;
		Transform tiller = ctrl.m_attachPoint != null ? ctrl.m_attachPoint : ctrl.transform;
		Transform seat = chair.m_attachPoint != null ? chair.m_attachPoint : chair.transform;
		return Vector3.Distance(tiller.position, seat.position) <= HelmRadius;
	}

	private static int CountBenches(GameObject go)
	{
		int n = 0;
		foreach (Chair chair in go.GetComponentsInChildren<Chair>(true))
		{
			if (IsRowingBench(go, chair)) n++;
		}
		return n;
	}

	// Cheap enough to call every frame: two reference checks unless somebody edited the lists.
	internal static void RefreshSeatRules()
	{
		string row = OarsmenPlugin.RowingSeatAnims.Value ?? "";
		string hold = OarsmenPlugin.HoldFastSeatAnims.Value ?? "";
		if (ReferenceEquals(row, rowAnimsParsed) && ReferenceEquals(hold, holdAnimsParsed)) return;
		Parse(row, rowAnims, ref rowAnimsParsed);
		Parse(hold, holdAnims, ref holdAnimsParsed);
		SeatRules++;
	}

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
	// blade has left and reaching 0 once the hull outruns it at 'Speed where oars stop helping'. Shared by the
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

	// Oar hole positions, parsed once per config value rather than once per ship per frame (same identity
	// check as Parse below).
	internal static float[] Holes()
	{
		string list = OarsmenPlugin.HolePositions.Value ?? "";
		if (ReferenceEquals(list, holesParsed)) return holes;
		List<float> vals = new();
		foreach (string part in list.Split(',', ';'))
		{
			if (float.TryParse(part.Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float v)) vals.Add(v);
		}
		holes = vals.ToArray();
		holesParsed = list;
		return holes;
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
			OarsBehaviour oars = OarsBehaviour.For(__instance);
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
			OarsBehaviour oars = OarsBehaviour.For(__instance.m_ship);
			// A hull the config has excluded is vanilla again, so it gets no crew line either.
			if (oars == null || !oars.Active || oars.BenchCount <= 0) return;
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
		public float stow = 1f;     // 0 pulling, 1 shipped; eased, never set outright
		public float dir = 1f;      // +1 pulling ahead, -1 backing water; eased through zero
		public float wantDir = 1f;  // what dir is easing towards, held through the deadband
	}

	// How far behind the bench ahead each bench pulls, bow to stern, as a fraction of a stroke. Small
	// enough to read as a crew pulling together rather than as rowers each doing their own thing. Held
	// as a fraction rather than in seconds so the ripple keeps its shape whatever the rate is set to.
	private const float StrokeStagger = 0.038f;

	// How long the simulated crew keeps its benches after the last player is reported off the boat.
	// Long enough to ride out the onboard trigger dropping in and out, short enough that stepping ashore
	// stops the phantoms before the boat can row away on its own.
	private const float AboardGrace = 1f;

	// One sample of the hull's side: how far out the planking stands (x) at a point along the ship (z),
	// both ship-local.
	private struct HullPoint
	{
		public float z;
		public float x;
	}

	// One lookup per ship for the physics postfix instead of a GetComponent walk every fixed update.
	private static readonly Dictionary<Ship, OarsBehaviour> byShip = new();
	internal static OarsBehaviour For(Ship ship) => ship != null && byShip.TryGetValue(ship, out OarsBehaviour o) ? o : null;

	private Ship ship;
	private string prefabName;
	private readonly List<Bench> benches = new();
	private readonly List<HullPoint> hullPort = new();
	private readonly List<HullPoint> hullStarboard = new();
	private int seatRules = -1;

	// Whether the config lists currently let this hull row. Re-evaluated every frame so the lists behave
	// like every other setting: edit them and boats already in the water follow.
	internal bool Active { get; private set; }
	private Material oarMaterial;
	private float scanTimer;
	private float lastAboard = -999f;
	private int rowerCount;
	private float strokeCycle;
	private bool stroking;
	private float builtLength = -1f, builtInboard, builtThickness, builtBladeLength, builtBladeWidth;

	internal int RowerCount => rowerCount;
	internal int BenchCount => benches.Count;

	private void Awake()
	{
		ship = GetComponent<Ship>();
		prefabName = Utils.GetPrefabName(gameObject);
		if (ship != null) byShip[ship] = this;
		BuildBenches();
	}

	private void OnDestroy()
	{
		if (ship != null && byShip.TryGetValue(ship, out OarsBehaviour o) && o == this) byShip.Remove(ship);
	}

	// Rebuilt rather than done once at Awake so that editing the seat lists takes effect on hulls already
	// in the water. A hull that had no rowing bench at all never got this component, so it needs a reload
	// to pick one up - the only part of the config that is not live.
	private void BuildBenches()
	{
		foreach (Bench b in benches) if (b.oar != null) Destroy(b.oar);
		benches.Clear();
		rowerCount = 0;
		seatRules = Rowing.SeatRules;
		builtLength = -1f;   // force BuildOar for the new set on the next frame
		Transform t = transform;
		int skipped = 0;
		foreach (Chair chair in GetComponentsInChildren<Chair>(true))
		{
			if (!Rowing.IsRowingBench(gameObject, chair)) { skipped++; continue; }
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
		MapHull();
		OarsmenPlugin.Log.LogInfo($"Oarsmen: {prefabName} has {benches.Count} rowing benches ({skipped} seats skipped: helm and hold-fast points)");
	}

	// Where the hull's side is at any point along the ship, read off the row of box colliders vanilla
	// builds the sides from - seven a side on the Longship, plus the ladders, which sit on the same line.
	// Only used when 'Snap oars to the hull' is on, and only worth it on a hull where one offset from the
	// bench cannot fit both ends: the Longship's side stands at |x| 2.4 amidships and 1.6 at the forward
	// benches, while its benches are inset 1.5 and 0.8.
	//
	// The bench boxes are excluded or they would be read as the hull and pull every oar inboard, and the
	// keel-centred mesh colliders have no side to read.
	private void MapHull()
	{
		hullPort.Clear();
		hullStarboard.Clear();
		Transform t = transform;
		foreach (Collider c in GetComponentsInChildren<Collider>(true))
		{
			if (c == null || c.isTrigger || c is MeshCollider) continue;
			if (ship != null && c == ship.m_floatCollider) continue;
			if (c.GetComponentInParent<Chair>() != null || c.GetComponentInParent<ShipControlls>() != null) continue;
			Vector3 lc = t.InverseTransformPoint(c.bounds.center);
			if (Mathf.Abs(lc.x) < 0.5f) continue;   // mast and anything else on the centreline
			(lc.x < 0f ? hullPort : hullStarboard).Add(new HullPoint { z = lc.z, x = lc.x });
		}
		hullPort.Sort((a, b) => a.z.CompareTo(b.z));
		hullStarboard.Sort((a, b) => a.z.CompareTo(b.z));
	}

	// The hull line at z on one side, interpolated between the two samples that bracket it.
	private bool TryHullSideAt(float side, float z, out float x)
	{
		List<HullPoint> line = side < 0f ? hullPort : hullStarboard;
		x = 0f;
		if (line.Count == 0) return false;
		if (z <= line[0].z) { x = line[0].x; return true; }
		if (z >= line[line.Count - 1].z) { x = line[line.Count - 1].x; return true; }
		for (int i = 1; i < line.Count; i++)
		{
			if (z > line[i].z) continue;
			float span = line[i].z - line[i - 1].z;
			x = span > 0.0001f ? Mathf.Lerp(line[i - 1].x, line[i].x, (z - line[i - 1].z) / span) : line[i].x;
			return true;
		}
		x = line[line.Count - 1].x;
		return true;
	}

	private void Update()
	{
		Rowing.RefreshSeatRules();
		if (seatRules != Rowing.SeatRules) BuildBenches();

		bool allowed = Rowing.AllowedByLists(prefabName);
		if (allowed != Active)
		{
			Active = allowed;
			if (!Active)
			{
				// Excluded while afloat: drop the oars and the crew so the hull goes back to vanilla.
				foreach (Bench b in benches) { b.occupied = false; b.simulated = false; b.stow = 1f; if (b.oar != null) b.oar.SetActive(false); }
				rowerCount = 0;
				stroking = false;
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
		//
		// Held for a moment after the last player leaves, because vanilla's onboard trigger flickers: walk
		// the deck near the rail and it reports nobody aboard for a frame or two at a time. Without the
		// grace period the whole phantom crew - oars and force - drops out and comes back with it, which
		// is what made the simulated oars stutter while the real ones beside them looked fine.
		if (ship.m_players.Count > 0) lastAboard = Time.time;
		int target = Math.Min(OarsmenPlugin.SimulatedRowers.Value, benches.Count);
		if (count < target && Time.time - lastAboard < AboardGrace)
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
		if (Rowing.Headless) return;
		bool show = OarsmenPlugin.ShowOars.Value == OarsmenPlugin.Toggle.On && OarsmenPlugin.Enabled.Value == OarsmenPlugin.Toggle.On;
		if (!show)
		{
			// Switched off outright rather than eased: reset the stow so that turning oars back on starts
			// them shipped and swings them out, instead of resuming mid-stroke.
			foreach (Bench b in benches) { b.stow = 1f; if (b.oar != null) b.oar.SetActive(false); }
			stroking = false;
			return;
		}
		if (NeedsRebuild()) foreach (Bench b in benches) BuildOar(b);

		Ship.Speed speed = ship.m_speed;
		bool rowing = speed == Ship.Speed.Slow || speed == Ship.Speed.Back
			|| ((speed == Ship.Speed.Half || speed == Ship.Speed.Full) && OarsmenPlugin.RowUnderSail.Value == OarsmenPlugin.Toggle.On);
		// Strokes per second, and the clock kept as a position within one stroke rather than as elapsed
		// seconds multiplied up. Two reasons: changing the rate then changes how fast the clock runs
		// instead of jumping the crew to a different point in the stroke, which matters while somebody is
		// dragging the slider; and the clock cannot drift into the part of the float range where a stroke
		// stops being smooth, however long the session runs.
		float strokeRate = Mathf.Max(1f, OarsmenPlugin.StrokeRate.Value) / 60f;
		if (rowing || stroking) strokeCycle = Mathf.Repeat(strokeCycle + dt * strokeRate, 1f);

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

		// The rate used to be hard-coded to the rudder paddle vanilla animates at sin(t * 6), which works
		// out at 57 strokes a minute - a racing sprint, not a crew moving a loaded longship. Nothing
		// depends on the two agreeing: the paddle is vanilla's own animation on a different part of the
		// boat, and the force the crew adds is flat rather than stroke-timed.
		float driveShare = Mathf.Clamp(OarsmenPlugin.DriveShare.Value, 0.15f, 0.85f);

		float[] holes = Rowing.Holes();
		Transform t = transform;
		bool anyPulling = false;
		foreach (Bench b in benches)
		{
			if (b.oar == null) continue;
			// An oar stays drawn until it has finished shipping itself, so standing up sends it back along
			// the hull rather than deleting it out of the air mid-stroke.
			bool draw = b.occupied || b.stow < 0.999f;
			b.oar.SetActive(draw);
			if (!draw) continue;

			// Pivot: beside the bench at the hull, or snapped to the nearest oar hole along the ship.
			Vector3 seat = t.InverseTransformPoint(b.seat.position);
			Vector3 pivot = seat + new Vector3(b.side * OarsmenPlugin.PivotOutward.Value, OarsmenPlugin.PivotUp.Value, OarsmenPlugin.PivotForward.Value);
			if (holes.Length > 0)
			{
				float best = holes[0];
				foreach (float h in holes) if (Mathf.Abs(h - pivot.z) < Mathf.Abs(best - pivot.z)) best = h;
				pivot.z = best;
			}
			// A hull that tapers cannot be fitted by a single offset from the bench, so this reads the
			// side off the hull's own colliders at the oar's station instead. Off by default: the vanilla
			// offsets are measured, and they fit the Longship and Karve.
			if (OarsmenPlugin.SnapToHull.Value == OarsmenPlugin.Toggle.On && TryHullSideAt(b.side, pivot.z, out float hullX))
			{
				pivot.x = hullX - b.side * OarsmenPlugin.HullInset.Value;
			}
			b.oar.transform.localPosition = pivot;

			// Oar's +Z points outboard from the pivot. Rowing: swing fore-aft at the stroke rate, blade
			// dipped. Not rowing: shipped along the hull, or held out to the side, and still.
			// Signed: + pulls the ship ahead, - backs water. Magnitude is how hard, so a bank the rudder has
			// cancelled out barely moves its oars. The whole split is multiplied by dirSign, not just the
			// base: vanilla negates its steer force when backing (num17 = -1), so the same rudder swings the
			// bow the other way and the banks have to swap with it, or the oars pivot against the boat.
			// Which way this bank is pulling. Positive rudder swings the bow to starboard (vanilla's steer
			// force is right * m_stearForce * -rudder, applied at the stern), so starboard is the inside of
			// that turn and it is the inside bank that backs water while the outside keeps pulling - which
			// is how a crew pivots a longship on the spot. Past 'Turn stroke bias' worth of rudder the
			// inside bank goes negative and reverses; below that both banks pull ahead.
			//
			// Only the direction comes from the rudder. How hard the bank pulls does not, which is the
			// change: the amplitude used to be |power|, and because power crosses zero on its way to
			// negative, feeding in rudder took the inside bank's stroke to nothing - and nothing is what
			// ships an oar, so the inside bank stowed itself along the hull mid-turn and then came back out
			// backing. Both banks now row a full stroke throughout; one of them simply rows it backwards.
			float bankPower = dirSign * (1f - turnBias * b.side);
			// A deadband so a rudder held near the reversal point cannot flutter the bank between ahead and
			// astern; outside it the bank commits, and b.dir eases it across.
			if (bankPower > 0.05f) b.wantDir = 1f;
			else if (bankPower < -0.05f) b.wantDir = -1f;
			float effort = b.occupied ? bite : 0f;

			// How far this oar is from pulling: 0 pulling flat out, 1 fully shipped. Eased towards its
			// target over 'Stow time' rather than set outright, because every reason an oar stops rowing
			// arrives as a step change - the helmsman drops to Stop, the sail goes up, a rower stands - and
			// stepping this would teleport the oar into the stowed pose. Everything the oar does hangs off
			// this one number, so the swing, the blade and the shipping cannot disagree with each other.
			float targetStow = rowing && effort > 0.02f ? 1f - effort : 1f;
			b.stow = Mathf.MoveTowards(b.stow, targetStow, dt / Mathf.Max(0.05f, OarsmenPlugin.StowTime.Value));
			// The blend itself moves at a constant rate; what the oar does with it is eased, so it leaves the
			// stroke and settles into the stowed pose without a kink at either end.
			float stowEased = Mathf.SmoothStep(0f, 1f, b.stow);
			float pull = 1f - stowEased;
			if (pull > 0.001f) anyPulling = true;

			// One turn of u is one stroke: the drive occupies the first 'Drive share' of it, the recovery
			// the rest. The two are eased separately rather than being read off one sine, and that is what
			// makes this read as rowing:
			//
			//  - The oar is momentarily still at the catch and at the finish, because a smoothstep has no
			//    slope at its ends. A sine is never still except at its extremes and never dwells there, so
			//    a sine-driven oar waves rather than rows.
			//  - The drive can be quicker than the recovery, as a real stroke is - the crew pulls hard and
			//    comes forward at leisure. A sine forces the two to be mirror images.
			//  - The blade being buried and the oar driving are now the same interval by construction. Every
			//    version up to 0.3.0 kept them as two curves that had to be held a quarter-cycle apart, and
			//    getting that relationship wrong is exactly what made the oars stir instead of row.
			float u = Mathf.Repeat(strokeCycle + b.phase, 1f);
			bool driving = u < driveShare;
			float p = driving ? u / driveShare : (u - driveShare) / (1f - driveShare);
			float eased = Mathf.SmoothStep(0f, 1f, p);
			// +1 is forward at the catch, -1 aft at the finish; the recovery carries it back the other way.
			float swing = driving ? Mathf.Lerp(1f, -1f, eased) : Mathf.Lerp(-1f, 1f, eased);
			// Backing water is the same stroke with the loaded half swung the other way. Eased across rather
			// than flipped, because negating the arc outright mirrors the oar in a single frame: running it
			// through zero shortens the stroke, turns it over and lengthens it again, which is what a rower
			// taking up a backing stroke actually looks like.
			b.dir = Mathf.MoveTowards(b.dir, b.wantDir, dt / Mathf.Max(0.05f, OarsmenPlugin.StowTime.Value));
			// Eased over the whole -1..1 run, so the turn-over starts and finishes gently and moves fastest
			// through zero, where the stroke is shortest and a change of pace shows least.
			float dirEased = Mathf.Lerp(-1f, 1f, Mathf.SmoothStep(0f, 1f, (b.dir + 1f) * 0.5f));
			float sweep = swing * dirEased * OarsmenPlugin.StrokeSweep.Value * 0.5f * pull;

			// Blade buried through the drive and clear of the water on the recovery. It squares up over the
			// first part of the drive and feathers out over the last - at the catch and the finish, where
			// the oar is slowest, so the turn has time to read - rather than being switched at a crossing.
			// The entry is centred on the catch (u = 0) and the extraction on the finish (u = driveShare), each
			// spread over 'Catch blend' of the whole stroke, so the blade drops in over the last of the recovery
			// and the first of the drive, and comes out over the last of the drive and the first of the recovery.
			// 0.3.1 ran both inside the drive alone, over a fifth of it - 9% of a stroke, a fifth of a second at
			// 26 a minute - which is a blade slapped into the water rather than dropped in.
			float half = Mathf.Clamp(OarsmenPlugin.CatchBlend.Value, 0.02f, Mathf.Min(driveShare, 1f - driveShare)) * 0.5f;
			float toCatch = u <= 0.5f ? u : u - 1f;   // signed distance from the catch, in strokes
			float toFinish = u - driveShare;           // signed distance from the finish
			float square = Mathf.Abs(toCatch) <= Mathf.Abs(toFinish)
				? Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-half, half, toCatch))
				: 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-half, half, toFinish));
			float clearDip = OarsmenPlugin.BladeDip.Value - Mathf.Max(0f, OarsmenPlugin.RecoveryLift.Value);
			float dip = Mathf.Lerp(-OarsmenPlugin.StowedAngle.Value, Mathf.Lerp(clearDip, OarsmenPlugin.BladeDip.Value, square), pull);
			// Feathering: the blade turns flat as it leaves the water and squares up again at the catch.
			// Rolled about the oar's own axis - the root's Z, which Unity applies before the pitch and yaw,
			// so it stays a roll of the shaft rather than a twist of the whole swing.
			float feather = Mathf.Lerp(OarsmenPlugin.FeatherAngle.Value, 0f, square) * pull * b.side;

			// yaw: outboard (+90 right / -90 left) plus the fore-aft sweep; pitch: blade down into the water.
			float yaw = b.side * 90f + (-b.side * sweep);
			// Shipped oars lie fore and aft along the hull, blades aft, the way a crew boats them - rather
			// than standing straight out to the side doing nothing. Turned on the same eased blend, so the
			// oar swings in from wherever the stroke had it. The target is side * 180 rather than a flat 180
			// so each bank comes aft the short way instead of sweeping across the boat.
			if (OarsmenPlugin.StowedOars.Value == OarsmenPlugin.StowStyle.AlongHull && b.stow > 0f)
			{
				yaw = Mathf.Lerp(yaw, b.side * 180f, stowEased);
			}
			b.oar.transform.localRotation = Quaternion.Euler(dip, yaw, feather);
		}
		// Keep the clock running while anyone is still easing off, so a crew that has been told to stop
		// finishes the stroke it is in rather than freezing mid-swing and then rotating.
		stroking = anyPulling;
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
