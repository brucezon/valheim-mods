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
// in paddle mode and reverse each rower adds a share of the paddle force and a share of the rudder push,
// and an oar appears through the hull beside their bench and strokes in time with the rudder paddle. The
// helmsman at the tiller is not a rower.
//
// Rowers amplify the helmsman's rudder instead of steering independently, which is what keeps the feature
// free of per-rower controls: the contribution is zero with the rudder centred, so there is nothing a
// rower could aim and no state to sync beyond who is sitting down. Sailing is untouched - vanilla only
// pushes the rudder in Slow and Back, and the crew respects that gating.
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
	public const string Version = "0.2.0";

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
	internal static ConfigEntry<string> ExcludedShips;
	internal static ConfigEntry<float> BonusPerRower;
	internal static ConfigEntry<float> SteerPerRower;
	internal static ConfigEntry<int> MaxRowers;
	internal static ConfigEntry<Toggle> RowUnderSail;
	internal static ConfigEntry<Toggle> ShowRowersOnTiller;
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
	internal static ConfigEntry<float> TurnStrokeBias;
	internal static ConfigEntry<float> BladeDip;
	internal static ConfigEntry<float> StowedAngle;
	internal static ConfigEntry<int> SimulatedRowers;
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
		Ships = config("2 - Rowing", "Only these ships", "", "Leave empty - the default - and every ship with benches rows, including boats added by other mods such as OdinShip. Nothing has to be named here for a new boat to work. Set a comma-separated list of prefab names to restrict rowing to just those (VikingShip = Longship, Karve). A ship with no benches never rows either way, because there is nowhere to sit.");
		ExcludedShips = config("2 - Rowing", "Excluded ships", "", "Comma-separated prefab names that never row, even though they have seats. For a boat whose only chair is a helm seat, or one whose handling you would rather leave alone. Checked before 'Only these ships'.");
		BonusPerRower = config("2 - Rowing", "Paddle force per rower (x)", 0.25f, "Extra paddle force per seated rower, as a fraction of the ship's paddle force. 0.25 with 4 rowers = twice the vanilla paddle force. Speed rises less than force because water drag grows with speed. Applies to reverse as well as forward.");
		SteerPerRower = config("2 - Rowing", "Steering force per rower (x)", 0.15f, "Extra turning force per seated rower, as a fraction of the ship's own rudder force. Rowers amplify whatever the helmsman is already asking for, so it is zero with the rudder centred and there is nothing for a rower to aim. Paddle and reverse only, which is exactly where vanilla gives the rudder a push - under sail the ship turns as it always did. 0 = no steering help. Deliberately lower than the paddle share: the game damps turning linearly but speed quadratically, so a given multiplier moves the turn rate far more than it moves the top speed. A full crew at 0.15 is roughly +40% turning force at paddle speed, because vanilla's other, speed-proportional turning force is untouched.");
		MaxRowers = config("2 - Rowing", "Max rowers", 0, "Rowers counted at most. 0 - the default - means every bench counts, so a big hull rewards a big crew: a warship with twelve benches is pulled by twelve. This costs vanilla boats nothing, since the Longship only has four seats anyway. Set a number to cap it if a modded ship turns out to be too quick with a full complement, and remember the per-rower shares above are the gentler way to tune that.");
		RowUnderSail = config("2 - Rowing", "Row under sail", Toggle.Off, "On = rowers also add their forward force while the sail is half or fully out. Off = rowing only counts in paddle mode and reverse. Steering help is never added under sail either way.");
		ShowRowersOnTiller = config("2 - Rowing", "Show rowers on the tiller", Toggle.On, "Add a 'Rowers 3/4' line to the tiller's hover text, so the helmsman can see the crew without the console.");

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
		TurnStrokeBias = config("3 - Oars", "Turn stroke bias (x)", 1.6f, "How strongly the rudder splits the two banks. The inside bank of a turn eases off and, past 1, drops into a back-water stroke while the outside bank keeps pulling - how a crew actually pivots a longship. 0 = both banks always stroke together whatever the rudder is doing. Purely visual: the turning force itself comes from 'Steering force per rower'.");
		BladeDip = config("3 - Oars", "Blade dip (degrees)", 22f, "How far the oar points down into the water while rowing.");
		StowedAngle = config("3 - Oars", "Stowed angle (degrees)", 12f, "Oars of seated rowers that are not rowing (sail out, or the ship stopped) are raised out of the water by this angle and held still.");

		SimulatedRowers = config("4 - Debug", "Simulate rowers", 0, "Testing aid: pretend at least this many benches are manned, so one player can see and feel a full crew in single player. Empty benches are filled bow to stern until the total is reached; real rowers always count first, so 4 on a Longship you are already rowing adds three phantoms. They row, draw oars and push the ship exactly as players would - the point is to tune oar placement and the force multipliers without four people. Only applies while somebody is actually aboard, so derelict boats stay still. 0 = off. 'oarsmen rowers' marks the fake ones. Leave this at 0 on a real server.");
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
			if (oars == null)
			{
				Say(args, "  oarsmen: no benches on this hull, so nothing to row with. Any ship with a Chair rows; 'oarsmen dump <prefab>' shows whether it has one.");
			}
			else if (!oars.Active)
			{
				Say(args, $"  oarsmen: {oars.BenchCount} benches, but this hull is switched off by the config (check 'Excluded ships' and 'Only these ships').");
			}
			else
			{
				Say(args, $"  oarsmen: {oars.RowerCount} rowing of {oars.BenchCount} benches");
			}
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
