using System;
using System.Runtime.CompilerServices;
using BepInEx.Configuration;
using HarmonyLib;
using MagicaCloth2;
using UnityEngine;

namespace Oarsmen;

// 5 - Sails: give pre-1.0 hulls a real Valheim 1.0 sail.
//
// 1.0's sail is one self-contained rig under the mast, called Karve_Sail on every vanilla ship (Raft,
// Karve, Longship and Drakkar all carry the same 63-vertex mesh, only scaled): a MagicaCloth MeshCloth, its
// SkinnedMeshRenderer, a two-bone skeleton (Sail_Top / Sail_Bottom), a MagicaCapsuleCollider for the mast and
// three markers (FurledPosition, MidFurledPosition, UnfurledPosition). Ship.UpdateSailSize slides Sail_Bottom
// between the markers and sets the cloth's blend weight from a curve, all behind the prefab's m_hasSail flag.
// m_hasSail is read nowhere else, so it is purely visual.
//
// A hull that has not migrated (OdinShip 0.8.1: every one of its nine boats) still carries the pre-1.0 layout
// instead - Mast/Sail scaled 0.1..1 in Y, holding sail_full (SkinnedMeshRenderer + Unity Cloth + GlobalWind)
// and two rope LineRenderers - and none of the new fields. LegacySails.cs fakes the old routine for those.
// This goes the other way: clone vanilla's rig under the hull's own mast, size it from the old sail's measured
// width, top and foot, point the Ship's 1.0 fields at the clone and set m_hasSail, so vanilla's own code drives
// a real 1.0 sail. The old mesh and cloth are switched off; the old Sail object stays and is still scaled in
// step, because the two rope lines are its children and take their length from it.
//
// Two things that bite: (1) UpdateSailSize logs m_changeSailPosEffect.m_effectPrefabs[0] unguarded, and an
// unmigrated hull's list is empty, so the vanilla list MUST be copied or the first sail change throws;
// (2) MagicaCloth captures its transform when it initialises, so the clone is instantiated inactive (the
// source object is switched off for the duration of Instantiate) and only activated once placed and scaled.
//
// Visual only and per client, like the oars; a dedicated server never runs it. If anything here fails the
// hull is left to LegacySails. Both follow their toggles live.
internal static class ModernSails
{
	internal static ConfigEntry<OarsmenPlugin.Toggle> Enabled;
	internal static ConfigEntry<OarsmenPlugin.Toggle> KeepHullCanvas;
	internal static ConfigEntry<float> SizeFactor;

	private static readonly string[] SourcePrefabs = { "VikingShip", "Karve", "Raft" };
	private const string RigName = "Oarsmen_Sail_1_0";

	private sealed class State
	{
		public bool failed;
		public bool skipped;
		public string note = "";
		public GameObject rig;
		public Transform oldSail;
		public float oldSailScaleY = 0.1f;
		public Renderer[] oldRenderers = Array.Empty<Renderer>();
		public Behaviour[] oldBehaviours = Array.Empty<Behaviour>();
		public Cloth oldCloth;
		public EffectList savedEffect;
		public AnimationCurve savedCurve;
		public Material ownMaterial;
		public Renderer oldCanvas;
		public SkinnedMeshRenderer newCanvas;
		public Material vanillaMaterial;
		public Material dressedFrom;   // the old canvas's material the new one was last dressed from
		public bool dressedKeep;       // the 'keep the hull canvas' setting it was dressed under
	}

	private static readonly ConditionalWeakTable<Ship, State> states = new();

	internal static bool IsGrafted(Ship ship) => ship != null && states.TryGetValue(ship, out State st) && st.rig != null;

	internal static string Describe(Ship ship)
	{
		if (ship == null) return "";
		if (!states.TryGetValue(ship, out State st)) return "1.0 sail graft: not attempted";
		if (st.rig != null) return "1.0 sail graft: active (" + st.note + ", canvas " + (st.ownMaterial != null && st.dressedFrom != null ? "hull's own '" + st.dressedFrom.name.Replace(" (Instance)", "") + "'" : "vanilla") + ")";
		if (st.skipped) return "1.0 sail graft: not needed (" + st.note + ")";
		return "1.0 sail graft: " + (st.failed ? "FAILED, legacy shim in charge (" + st.note + ")" : "off");
	}

	// Runs before vanilla's body on every client each fixed update. Cheap when nothing changes: one table
	// lookup and two comparisons. Grafting happens here rather than in Ship.Awake so the ZDO is valid (placement
	// ghosts never reach CustomFixedUpdate) and so the toggle is live for ships already afloat.
	[HarmonyPatch(typeof(Ship), "UpdateSailSize")]
	private static class UpdateSailSizePatch
	{
		[HarmonyPriority(Priority.High)]
		private static void Prefix(Ship __instance)
		{
			if (Enabled == null || Rowing.Headless) return;
			bool want = Enabled.Value == OarsmenPlugin.Toggle.On;
			State st = states.GetOrCreateValue(__instance);
			if (st.rig != null)
			{
				if (!want) { Ungraft(__instance, st); return; }
				// Vanilla's sail-change branch dereferences Player.m_localPlayer, which is null while joining
				// and between death and respawn, and because the branch throws before it clears
				// m_sailWasInPosition it would throw every tick until the sail stopped moving. With no local
				// player there is nobody to play the effect for; clearing the flag skips the branch.
				if (Player.m_localPlayer == null) __instance.m_sailWasInPosition = false;
				return;
			}
			if (want && !st.failed && LegacySails.IsLegacy(__instance)) TryGraft(__instance, st);
		}

		// After vanilla has moved the sail: keep the old Sail object's height in step so its rope lines follow.
		private static void Postfix(Ship __instance)
		{
			if (!states.TryGetValue(__instance, out State st) || st.rig == null || st.oldSail == null) return;
			SyncCanvas(st);
			Vector3 s = st.oldSail.localScale;
			float y = Mathf.Lerp(0.1f, 1f, __instance.m_sailPosition);
			if (Mathf.Abs(s.y - y) > 0.0005f)
			{
				s.y = y;
				st.oldSail.localScale = s;
			}
		}
	}

	// Test hook: the smoke test on a headless scratch server calls this by reflection.
	internal static string ForceGraftForTest(Ship ship)
	{
		State st = states.GetOrCreateValue(ship);
		if (st.rig == null) TryGraft(ship, st);
		return Describe(ship);
	}

	private static Ship FindSource()
	{
		if (ZNetScene.instance == null) return null;
		foreach (string name in SourcePrefabs)
		{
			Ship s = ZNetScene.instance.GetPrefab(name)?.GetComponent<Ship>();
			if (IsUsableSource(s)) return s;
		}
		foreach (GameObject prefab in ZNetScene.instance.m_prefabs)
		{
			Ship s = prefab != null ? prefab.GetComponent<Ship>() : null;
			if (IsUsableSource(s)) return s;
		}
		return null;
	}

	private static bool IsUsableSource(Ship s)
	{
		if (s == null || !s.m_hasSail || s.m_sailCloth == null || s.m_sailBottomTransform == null) return false;
		if (s.m_sailFurledPosition == null || s.m_sailMidfurledPosition == null || s.m_sailUnfurledPosition == null) return false;
		Transform rig = s.m_sailCloth.transform;
		return s.m_sailBottomTransform.IsChildOf(rig) && s.m_sailFurledPosition.IsChildOf(rig)
			&& s.m_sailMidfurledPosition.IsChildOf(rig) && s.m_sailUnfurledPosition.IsChildOf(rig)
			&& rig.GetComponentInChildren<SkinnedMeshRenderer>(true) != null;
	}

	private static string RelativePath(Transform child, Transform root)
	{
		string path = "";
		for (Transform t = child; t != null && t != root; t = t.parent) path = path.Length == 0 ? t.name : t.name + "/" + path;
		return path;
	}

	// Axis-aligned bounds of a renderer's mesh expressed in another transform's local space.
	private static bool MeshBoundsIn(Renderer r, Transform space, out Bounds result)
	{
		result = default;
		Mesh mesh = r is SkinnedMeshRenderer smr ? smr.sharedMesh : r.GetComponent<MeshFilter>()?.sharedMesh;
		if (mesh == null) return false;
		Matrix4x4 m = space.worldToLocalMatrix * r.transform.localToWorldMatrix;
		Bounds b = mesh.bounds;
		bool first = true;
		for (int i = 0; i < 8; i++)
		{
			Vector3 corner = b.center + Vector3.Scale(b.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
			Vector3 p = m.MultiplyPoint3x4(corner);
			if (first) { result = new Bounds(p, Vector3.zero); first = false; }
			else result.Encapsulate(p);
		}
		return true;
	}

	private static void TryGraft(Ship ship, State st)
	{
		GameObject clone = null;
		try
		{
			Ship src = FindSource();
			if (src == null) { Fail(st, "no vanilla ship with a 1.0 sail rig found"); return; }

			Transform oldSail = ship.m_sailObject.transform;
			Transform mast = ship.m_mastObject != null ? ship.m_mastObject.transform : oldSail.parent;
			if (mast == null) { Fail(st, "hull has no mast transform"); return; }
			// OdinShip's two canoes carry a mast and sail that are switched off in the prefab: nothing is drawn,
			// so there is nothing to replace.
			if (!oldSail.gameObject.activeInHierarchy) { Fail(st, "old sail is hidden on this hull, nothing to replace"); st.skipped = true; return; }

			// The old canvas: the largest skinned or static mesh under the old Sail object.
			Renderer oldCanvas = null; float best = 0f;
			foreach (Renderer r in oldSail.GetComponentsInChildren<Renderer>(true))
			{
				if (r is LineRenderer) continue;
				Mesh mesh = r is SkinnedMeshRenderer s0 ? s0.sharedMesh : r.GetComponent<MeshFilter>()?.sharedMesh;
				if (mesh == null) continue;
				float area = mesh.bounds.size.sqrMagnitude;
				if (area > best) { best = area; oldCanvas = r; }
			}
			if (oldCanvas == null) { Fail(st, "old sail object holds no mesh to measure"); return; }

			// Measure it fully set: the prefab rests at scale Y 0.1 (furled).
			Vector3 restScale = oldSail.localScale;
			oldSail.localScale = new Vector3(restScale.x, 1f, restScale.z);
			bool measured = MeshBoundsIn(oldCanvas, mast, out Bounds oldB);
			oldSail.localScale = restScale;
			if (!measured || oldB.size.y < 0.05f) { Fail(st, "old sail could not be measured"); return; }

			GameObject srcRig = src.m_sailCloth.gameObject;
			string bottomPath = RelativePath(src.m_sailBottomTransform, srcRig.transform);
			string furledPath = RelativePath(src.m_sailFurledPosition, srcRig.transform);
			string midPath = RelativePath(src.m_sailMidfurledPosition, srcRig.transform);
			string unfurledPath = RelativePath(src.m_sailUnfurledPosition, srcRig.transform);

			// Instantiate INACTIVE so MagicaCloth initialises only after the rig is placed and scaled.
			bool wasActive = srcRig.activeSelf;
			try
			{
				srcRig.SetActive(false);
				clone = UnityEngine.Object.Instantiate(srcRig, mast, false);
			}
			finally
			{
				srcRig.SetActive(wasActive);
			}
			clone.name = RigName;

			// The vanilla rig's sheet ropes (Sail_Bottom/left_bottom, right_bottom) are LineAttach lines tied to anchor
			// points on the vanilla HULL, outside this subtree. A clone keeps pointing at the source prefab, which sits
			// at the world origin, so the rope would run from the sail to (0,0,0). The hull keeps its own rope lines, so
			// any line with an end outside the rig is simply switched off.
			foreach (LineAttach line in clone.GetComponentsInChildren<LineAttach>(true))
			{
				bool outside = false;
				foreach (Transform end in line.m_attachments)
					if (end == null || !end.IsChildOf(clone.transform)) { outside = true; break; }
				if (!outside) continue;
				line.enabled = false;
				LineRenderer drawn = line.GetComponent<LineRenderer>();
				if (drawn != null) drawn.enabled = false;
			}

			SkinnedMeshRenderer newCanvas = clone.GetComponentInChildren<SkinnedMeshRenderer>(true);
			MagicaCloth cloth = clone.GetComponent<MagicaCloth>();
			Transform bottom = clone.transform.Find(bottomPath);
			Transform furled = clone.transform.Find(furledPath);
			Transform mid = clone.transform.Find(midPath);
			Transform unfurled = clone.transform.Find(unfurledPath);
			if (newCanvas == null || cloth == null || bottom == null || furled == null || mid == null || unfurled == null)
			{
				Fail(st, "cloned rig is missing parts"); UnityEngine.Object.Destroy(clone); return;
			}

			// Same orientation relative to the mast as the canvas it replaces. Old and new canvases are both
			// thin in local X, tall in Y, wide in Z, and vanilla turns both (0,90,0) against the mast.
			clone.transform.localRotation = Quaternion.Inverse(mast.rotation) * oldCanvas.transform.rotation;
			clone.transform.localPosition = Vector3.zero;
			clone.transform.localScale = Vector3.one;
			if (!MeshBoundsIn(newCanvas, mast, out Bounds unit)) { Fail(st, "rig canvas has no mesh"); UnityEngine.Object.Destroy(clone); return; }

			// Width axis in mast space = the larger horizontal extent of the old canvas.
			bool wideOnX = oldB.size.x >= oldB.size.z;
			float oldWidth = wideOnX ? oldB.size.x : oldB.size.z;
			float unitWidth = wideOnX ? unit.size.x : unit.size.z;
			if (unitWidth < 0.01f || oldWidth < 0.05f) { Fail(st, "degenerate sail width"); UnityEngine.Object.Destroy(clone); return; }
			float factor = Mathf.Clamp(SizeFactor?.Value ?? 1f, 0.25f, 4f);
			float scale = oldWidth / unitWidth * factor;
			// The mast may be non-uniformly scaled (LittleBoat 0.55/0.60/0.55) and MagicaCloth wants a uniform
			// world scale, so each rig axis is divided by the mast's scale along the mast axis it points down.
			// The rig is turned 90 degrees against the mast, so its X and Z map to the mast's Z and X.
			Vector3 ms = mast.lossyScale;
			float widthScale = Mathf.Abs(wideOnX ? ms.x : ms.z);
			Quaternion lr = clone.transform.localRotation;
			clone.transform.localScale = new Vector3(
				scale * SafeDiv(widthScale, MastScaleAlong(lr * Vector3.right, ms)),
				scale * SafeDiv(widthScale, MastScaleAlong(lr * Vector3.up, ms)),
				scale * SafeDiv(widthScale, MastScaleAlong(lr * Vector3.forward, ms)));

			// Align: centre on the old canvas horizontally, tops level.
			MeshBoundsIn(newCanvas, mast, out Bounds placed);
			clone.transform.localPosition = new Vector3(oldB.center.x - placed.center.x, oldB.max.y - placed.max.y, oldB.center.z - placed.center.z);

			// Foot: the canvas's bottom edge rides Sail_Bottom one-for-one, and in the bind pose Sail_Bottom rests
			// on the unfurled marker with the canvas foot a fixed distance above it (measured: 0.35-0.40 rig
			// units). So lowering the marker by d lowers the foot by d. Move it by exactly the gap between the
			// bind-pose foot and the old sail's foot, limited to 40% of the canvas height either way. Vanilla
			// already sets this marker differently per hull (Y 0.51 Longship, 0.68 Raft, 0.90 Karve).
			MeshBoundsIn(newCanvas, mast, out placed);
			float wantFoot = oldB.max.y - oldB.size.y * factor;
			float drop = placed.min.y - wantFoot;                       // + = foot must come down
			float limit = placed.size.y * 0.4f;
			float clampedDrop = Mathf.Clamp(drop, -limit, limit);
			float stretch = placed.size.y > 0.01f ? (placed.size.y + drop) / placed.size.y : 1f;
			float clamped = placed.size.y > 0.01f ? (placed.size.y + clampedDrop) / placed.size.y : 1f;
			Vector3 u = mast.InverseTransformPoint(unfurled.position);
			u.y -= clampedDrop;
			unfurled.position = mast.TransformPoint(u);
			Vector3 srcF = src.m_sailFurledPosition.localPosition, srcM = src.m_sailMidfurledPosition.localPosition, srcU = src.m_sailUnfurledPosition.localPosition;
			float midT = Mathf.Abs(srcF.y - srcU.y) > 0.001f ? Mathf.Clamp01((srcF.y - srcM.y) / (srcF.y - srcU.y)) : 0.5f;
			mid.position = Vector3.Lerp(furled.position, unfurled.position, midT);

			st.oldCanvas = oldCanvas;
			st.newCanvas = newCanvas;
			st.vanillaMaterial = newCanvas.sharedMaterial;
			st.dressedFrom = null;
			st.dressedKeep = false;
			SyncCanvas(st);

			// Hand the ship to vanilla's 1.0 routine.
			st.savedEffect = ship.m_changeSailPosEffect;
			st.savedCurve = ship.m_sailBlendWeightCurve;
			ship.m_sailCloth = cloth;
			ship.m_sailBottomTransform = bottom;
			ship.m_sailFurledPosition = furled;
			ship.m_sailMidfurledPosition = mid;
			ship.m_sailUnfurledPosition = unfurled;
			ship.m_sailBlendWeightCurve = src.m_sailBlendWeightCurve;
			ship.m_changeSailPosEffect = src.m_changeSailPosEffect; // never leave this empty: vanilla indexes [0]
			ship.m_sailPosition = 0f;
			// false, like a freshly loaded vanilla ship. With true, a hull that loads with its sail already up
			// enters vanilla's change-effect branch on its first tick; that branch reads Player.m_localPlayer,
			// which is null while joining, and the flag is only cleared at the end of the block, so it would
			// throw every frame until the player spawned.
			ship.m_sailWasInPosition = false;

			// Retire the old canvas but keep its object (rope lines hang off it).
			st.oldSail = oldSail;
			st.oldSailScaleY = restScale.y;
			st.oldRenderers = Array.FindAll(oldSail.GetComponentsInChildren<Renderer>(true), r => !(r is LineRenderer) && r.enabled);
			foreach (Renderer r in st.oldRenderers) r.enabled = false;
			st.oldCloth = oldSail.GetComponentInChildren<Cloth>(true);
			if (st.oldCloth != null) st.oldCloth.enabled = false;
			st.oldBehaviours = Array.FindAll(oldCanvas.GetComponents<Behaviour>(), b => b.enabled && b.GetType().Name == "GlobalWind");
			foreach (Behaviour b in st.oldBehaviours) b.enabled = false;

			st.rig = clone;
			st.note = $"rig from {Utils.GetPrefabName(src.gameObject)}, scale {scale:F2}, foot stretch {clamped:F2}{(Mathf.Abs(clamped - stretch) > 0.01f ? " (wanted " + stretch.ToString("F2") + ")" : "")}";
			ship.m_hasSail = true;
			clone.SetActive(true);
			clone = null;
			OarsmenPlugin.Log.LogInfo($"1.0 sail grafted onto {Utils.GetPrefabName(ship.gameObject)}: {st.note}");
		}
		catch (Exception e)
		{
			Fail(st, e.GetType().Name + ": " + e.Message);
			OarsmenPlugin.Log.LogWarning($"1.0 sail graft failed on {Utils.GetPrefabName(ship.gameObject)}, leaving it to the legacy shim: {e}");
			try { ship.m_hasSail = false; } catch { }
		}
		finally
		{
			if (clone != null) UnityEngine.Object.Destroy(clone);
		}
	}

	// Dress the new canvas, and keep it dressed. The hull's own look lives on the OLD renderer, and mods keep
	// changing it after the graft: OdinShip's ShipCustomization cycles sail designs with H, stores the index
	// in the ZDO ('odinship_sail_index') and on every client sets SailRenderer.material to the chosen entry -
	// on the old, now hidden, mesh. 0.5.0 copied that material once, so a design picked afterwards never
	// showed. Called every tick for a grafted hull; it does nothing unless the old renderer's material object
	// or the setting has changed, which is one reference comparison. Knows nothing about OdinShip: any mod
	// that re-materials the old sail is followed.
	//
	// Measured on the meshes: both run V up the sail, but U runs +Z on vanilla's Karve_Sail and -Z on the
	// pre-1.0 'sail' mesh OdinShip reuses, so the old material would come out mirrored left to right. Wear a
	// per-ship copy with U reversed instead. (The V range differs slightly, 0.15..0.85 against 0.09..0.89,
	// which crops a few percent top and bottom.)
	private static void SyncCanvas(State st)
	{
		if (st.newCanvas == null) return;
		bool keep = KeepHullCanvas != null && KeepHullCanvas.Value == OarsmenPlugin.Toggle.On;
		Material source = keep && st.oldCanvas != null ? st.oldCanvas.sharedMaterial : null;
		if (keep == st.dressedKeep && ReferenceEquals(source, st.dressedFrom) && (st.ownMaterial != null) == (source != null)) return;

		if (st.ownMaterial != null) UnityEngine.Object.Destroy(st.ownMaterial);
		st.ownMaterial = null;
		if (source != null)
		{
			Material own = new(source) { name = source.name + " (Oarsmen 1.0 sail)" };
			if (own.HasProperty("_MainTex"))
			{
				Vector2 tile = own.mainTextureScale, off = own.mainTextureOffset;
				own.mainTextureScale = new Vector2(-tile.x, tile.y);
				own.mainTextureOffset = new Vector2(off.x + tile.x, off.y);
			}
			st.newCanvas.sharedMaterial = own;
			st.ownMaterial = own;
		}
		else if (st.vanillaMaterial != null)
		{
			st.newCanvas.sharedMaterial = st.vanillaMaterial;
		}
		st.dressedFrom = source;
		st.dressedKeep = keep;
	}

	private static float SafeDiv(float a, float b) => Mathf.Abs(b) > 0.0001f ? a / b : 1f;

	// The mast's scale along a direction given in mast-local space (exact for axis-aligned directions).
	private static float MastScaleAlong(Vector3 dir, Vector3 mastScale)
	{
		dir.Normalize();
		return Mathf.Abs(dir.x * mastScale.x) + Mathf.Abs(dir.y * mastScale.y) + Mathf.Abs(dir.z * mastScale.z);
	}

	private static void Fail(State st, string why)
	{
		st.failed = true;
		st.note = why;
	}

	private static void Ungraft(Ship ship, State st)
	{
		ship.m_hasSail = false;
		ship.m_sailCloth = null;
		ship.m_sailBottomTransform = null;
		ship.m_sailFurledPosition = null;
		ship.m_sailMidfurledPosition = null;
		ship.m_sailUnfurledPosition = null;
		if (st.savedEffect != null) ship.m_changeSailPosEffect = st.savedEffect;
		if (st.savedCurve != null) ship.m_sailBlendWeightCurve = st.savedCurve;
		foreach (Renderer r in st.oldRenderers) if (r != null) r.enabled = true;
		foreach (Behaviour b in st.oldBehaviours) if (b != null) b.enabled = true;
		// The legacy shim decides the old cloth's state from here on; start it furled.
		if (st.oldSail != null)
		{
			Vector3 s = st.oldSail.localScale; s.y = st.oldSailScaleY; st.oldSail.localScale = s;
		}
		if (st.rig != null) UnityEngine.Object.Destroy(st.rig);
		if (st.ownMaterial != null) UnityEngine.Object.Destroy(st.ownMaterial);
		st.ownMaterial = null;
		st.newCanvas = null;
		st.oldCanvas = null;
		st.vanillaMaterial = null;
		st.dressedFrom = null;
		st.rig = null;
		st.note = "";
		OarsmenPlugin.Log.LogInfo($"1.0 sail removed from {Utils.GetPrefabName(ship.gameObject)}; legacy shim takes over.");
	}
}
