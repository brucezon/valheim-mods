using System.Collections.Generic;
using System.Text;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using ServerSync;
using UnityEngine;

namespace Oarsmen;

// Oarsmen: seated players row the ship.
//
// Vanilla ships paddle with one fixed force whoever is aboard, and the Longship's benches and oar holes
// are decoration. Here every player sitting on one of the ship's benches (a Chair on the ship) is a rower:
// in paddle mode each rower adds a share of the paddle force, and an oar appears through the hull beside
// their bench and strokes in time with the rudder paddle. The helmsman at the tiller is not a rower.
//
// Rowers are counted on every client from synced state (the bench's attach animation flag and the
// player's position at the bench), so everyone sees the same oars; only the ship's owner applies the
// force, in the same place vanilla applies its paddle force. Config is server-synced; the mod is not
// required on clients (a client without it simply sees no oars and, if it owns the ship, rows at vanilla speed).
[BepInPlugin(GUID, Name, Version)]
public class OarsmenPlugin : BaseUnityPlugin
{
	public const string GUID = "bruceirons.Oarsmen";
	public const string Name = "Oarsmen";
	public const string Version = "0.1.0";

	internal static ManualLogSource Log;
	private static readonly ConfigSync configSync = new(Name) { DisplayName = Name, CurrentVersion = Version, MinimumRequiredVersion = Version, ModRequired = false };

	internal enum Toggle
	{
		On = 1,
		Off = 0,
	}

	internal static ConfigEntry<Toggle> serverConfigLocked;
	internal static ConfigEntry<Toggle> Enabled;
	internal static ConfigEntry<string> Ships;
	internal static ConfigEntry<float> BonusPerRower;
	internal static ConfigEntry<int> MaxRowers;
	internal static ConfigEntry<Toggle> RowUnderSail;
	internal static ConfigEntry<Toggle> ShowOars;
	internal static ConfigEntry<float> OarLength;
	internal static ConfigEntry<float> OarInboard;
	internal static ConfigEntry<float> OarThickness;
	internal static ConfigEntry<float> BladeLength;
	internal static ConfigEntry<float> BladeWidth;
	internal static ConfigEntry<float> PivotOutward;
	internal static ConfigEntry<float> PivotUp;
	internal static ConfigEntry<float> PivotForward;
	internal static ConfigEntry<string> HolePositions;
	internal static ConfigEntry<float> StrokeSweep;
	internal static ConfigEntry<float> BladeDip;
	internal static ConfigEntry<float> StowedAngle;
	internal static ConfigEntry<Toggle> LogRowers;

	private ConfigEntry<T> config<T>(string group, string name, T value, string description, bool synced = true)
	{
		ConfigEntry<T> entry = Config.Bind(group, name, value, description);
		configSync.AddConfigEntry(entry).SynchronizedConfig = synced;
		return entry;
	}

	private void Awake()
	{
		Log = Logger;
		serverConfigLocked = config("1 - General", "Config is locked", Toggle.On, "If on, only admins can change the configuration on a server.");
		configSync.AddLockingConfigEntry(serverConfigLocked);

		Enabled = config("2 - Rowing", "Rowing", Toggle.On, "Seated players row: each player sitting on one of the ship's benches adds paddle force in paddle mode. Off = vanilla.");
		Ships = config("2 - Rowing", "Ships", "VikingShip, Karve", "Comma-separated ship prefab names that row. VikingShip = Longship (4 benches), Karve (2 benches). Ships without benches never row.");
		BonusPerRower = config("2 - Rowing", "Paddle force per rower (x)", 0.25f, "Extra paddle force per seated rower, as a fraction of the ship's paddle force. 0.25 with 4 rowers = twice the vanilla paddle force. Speed rises less than force because water drag grows with speed.");
		MaxRowers = config("2 - Rowing", "Max rowers", 4, "Rowers counted at most, whatever the number of benches.");
		RowUnderSail = config("2 - Rowing", "Row under sail", Toggle.Off, "On = rowers also add their force while the sail is half or fully out. Off = rowing only counts in paddle mode (vanilla's slow setting).");

		ShowOars = config("3 - Oars", "Show oars", Toggle.On, "Show an oar through the hull beside every occupied bench. Purely visual; each client draws them from the same seat state.");
		OarLength = config("3 - Oars", "Oar length (metres)", 3.6f, "Total oar length, handle to blade tip.");
		OarInboard = config("3 - Oars", "Oar inboard length (metres)", 1.0f, "How much of the oar is inside the hull, from the pivot to the handle end.");
		OarThickness = config("3 - Oars", "Oar shaft thickness (metres)", 0.07f, "Shaft diameter.");
		BladeLength = config("3 - Oars", "Blade length (metres)", 0.8f, "Length of the flat blade at the outboard end.");
		BladeWidth = config("3 - Oars", "Blade width (metres)", 0.2f, "Width of the blade.");
		PivotOutward = config("3 - Oars", "Pivot outward (metres)", 0.75f, "Pivot (oar hole) distance sideways from the bench seat toward the hull, positive = toward the side the bench is on.");
		PivotUp = config("3 - Oars", "Pivot up (metres)", 0.55f, "Pivot height above the bench seat.");
		PivotForward = config("3 - Oars", "Pivot forward (metres)", 0.0f, "Pivot offset along the ship from the bench seat, positive = toward the bow.");
		HolePositions = config("3 - Oars", "Oar hole positions", "", "Comma-separated positions of the hull's oar holes along the ship (ship-local Z, metres, bow positive). When set, each oar snaps to the closest hole instead of sitting beside its bench. Empty = no snapping. Find them with the 'oarsmen ship' console command and a bit of trial.");
		StrokeSweep = config("3 - Oars", "Stroke sweep (degrees)", 40f, "Total fore-aft swing of an oar per stroke while rowing.");
		BladeDip = config("3 - Oars", "Blade dip (degrees)", 22f, "How far the oar points down into the water while rowing.");
		StowedAngle = config("3 - Oars", "Stowed angle (degrees)", 12f, "Oars of seated rowers that are not rowing (sail out, or the ship stopped) are raised out of the water by this angle and held still.");

		LogRowers = config("4 - Debug", "Log rower changes", Toggle.Off, "Log a line whenever the number of rowers on a ship changes.", false);

		Harmony harmony = new(GUID);
		harmony.PatchAll();
	}

	// ---------------------------------------------------------------- console commands (client-side, local only)

	[HarmonyPatch(typeof(Terminal), "InitTerminal")]
	private static class ConsoleCommandsPatch
	{
		private static bool registered;

		private static void Postfix()
		{
			if (registered) return;
			registered = true;
			new Terminal.ConsoleCommand("oarsmen", "oarsmen ship | oarsmen dump <prefab> [depth] | oarsmen rowers - inspect ships and benches (logs to BepInEx/LogOutput.log)", args =>
			{
				string sub = args.Length > 1 ? args[1].ToLowerInvariant() : "";
				switch (sub)
				{
					case "ship": DescribeShip(args); break;
					case "dump": DumpPrefab(args); break;
					case "rowers": ListRowers(args); break;
					default: args.Context.AddString("oarsmen ship | oarsmen dump <prefab> [depth] | oarsmen rowers"); break;
				}
			});
		}

		private static void Say(Terminal.ConsoleEventArgs args, string line)
		{
			args.Context.AddString(line);
			Log.LogInfo(line);
		}

		// The ship the local player stands on: benches, tiller, hull collider, renderers. Positions are ship-local.
		private static void DescribeShip(Terminal.ConsoleEventArgs args)
		{
			Player p = Player.m_localPlayer;
			Ship ship = p != null ? p.GetStandingOnShip() : null;
			if (ship == null) ship = p != null ? p.GetControlledShip() : null;
			if (ship == null) { Say(args, "oarsmen: stand on a ship first."); return; }
			Transform t = ship.transform;
			Say(args, $"oarsmen ship: {Utils.GetPrefabName(ship.gameObject)} speed {ship.GetSpeed():F2} m/s setting {ship.GetSpeedSetting()} paddleForce {ship.m_backwardForce} sailForceFactor {ship.m_sailForceFactor} stearForce {ship.m_stearForce} rudderSpeed {ship.m_rudderSpeed} mass {ship.m_body.mass}");
			if (ship.m_floatCollider != null)
			{
				Vector3 c = t.InverseTransformPoint(ship.m_floatCollider.transform.TransformPoint(ship.m_floatCollider.center));
				Say(args, $"  float collider centre {Fmt(c)} size {Fmt(ship.m_floatCollider.size)}");
			}
			foreach (Chair chair in ship.GetComponentsInChildren<Chair>(true))
			{
				Transform a = chair.m_attachPoint != null ? chair.m_attachPoint : chair.transform;
				Vector3 lp = t.InverseTransformPoint(a.position);
				Vector3 lf = t.InverseTransformDirection(a.forward);
				Say(args, $"  bench '{chair.name}' seat {Fmt(lp)} facing {Fmt(lf)} anim {chair.m_attachAnimation} inShip {chair.m_inShip}");
			}
			if (ship.m_shipControlls != null && ship.m_shipControlls.m_attachPoint != null)
			{
				Say(args, $"  tiller seat {Fmt(t.InverseTransformPoint(ship.m_shipControlls.m_attachPoint.position))}");
			}
			foreach (MeshRenderer r in ship.GetComponentsInChildren<MeshRenderer>(true))
			{
				Bounds b = r.bounds;
				Say(args, $"  renderer '{r.name}' material '{(r.sharedMaterial != null ? r.sharedMaterial.name : "none")}' shader '{(r.sharedMaterial != null && r.sharedMaterial.shader != null ? r.sharedMaterial.shader.name : "")}' bounds centre {Fmt(t.InverseTransformPoint(b.center))} size {Fmt(b.size)}");
			}
			OarsBehaviour oars = ship.GetComponent<OarsBehaviour>();
			Say(args, oars != null ? $"  oarsmen: {oars.RowerCount} rowing of {oars.BenchCount} benches" : "  oarsmen: not a rowing ship (see 'Ships' in the config)");
		}

		private static void DumpPrefab(Terminal.ConsoleEventArgs args)
		{
			if (args.Length < 3) { Say(args, "oarsmen dump <prefab> [depth]"); return; }
			int depth = args.Length > 3 && int.TryParse(args[3], out int d) ? d : 4;
			GameObject prefab = ZNetScene.instance != null ? ZNetScene.instance.GetPrefab(args[2]) : null;
			if (prefab == null) { Say(args, $"oarsmen: no prefab '{args[2]}'"); return; }
			StringBuilder sb = new();
			Dump(prefab.transform, prefab.transform, "", depth, sb);
			Log.LogInfo($"oarsmen dump {args[2]}:\n{sb}");
			args.Context.AddString($"oarsmen: {args[2]} hierarchy written to LogOutput.log ({sb.Length} chars)");
		}

		private static void Dump(Transform root, Transform t, string indent, int depth, StringBuilder sb)
		{
			Vector3 lp = root.InverseTransformPoint(t.position);
			List<string> comps = new();
			foreach (Component c in t.GetComponents<Component>())
			{
				if (c == null || c is Transform) continue;
				comps.Add(c.GetType().Name);
			}
			sb.Append(indent).Append(t.name).Append(" @").Append(Fmt(lp)).Append(" [").Append(string.Join(", ", comps)).Append("]\n");
			if (depth <= 0) return;
			for (int i = 0; i < t.childCount; i++) Dump(root, t.GetChild(i), indent + "  ", depth - 1, sb);
		}

		private static void ListRowers(Terminal.ConsoleEventArgs args)
		{
			foreach (OarsBehaviour oars in Object.FindObjectsOfType<OarsBehaviour>())
			{
				Say(args, $"oarsmen: {Utils.GetPrefabName(oars.gameObject)} {oars.Describe()}");
			}
		}

		private static string Fmt(Vector3 v) => $"({v.x:F2}, {v.y:F2}, {v.z:F2})";
	}
}
