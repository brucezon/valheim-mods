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
// and an oar appears through the hull beside their bench and strokes in time with the rest of the crew. The
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
	public const string Version = "0.3.2";

	internal static ManualLogSource Log;
	private static readonly ConfigSync configSync = new(Name) { DisplayName = Name, CurrentVersion = Version, MinimumRequiredVersion = Version, ModRequired = false };

	internal enum Toggle
	{
		On = 1,
		Off = 0,
	}

	// What an oar does when its rower is not pulling.
	internal enum StowStyle
	{
		AlongHull = 0,
		Outboard = 1,
	}

	internal static ConfigEntry<Toggle> serverConfigLocked;
	internal static ConfigEntry<Toggle> Enabled;
	internal static ConfigEntry<string> Ships;
	internal static ConfigEntry<string> ExcludedShips;
	internal static ConfigEntry<string> RowingSeatAnims;
	internal static ConfigEntry<string> HoldFastSeatAnims;
	internal static ConfigEntry<float> BonusPerRower;
	internal static ConfigEntry<float> SteerPerRower;
	internal static ConfigEntry<float> MaxSteerShare;
	internal static ConfigEntry<float> MaxTurnRate;
	internal static ConfigEntry<int> MaxRowers;
	internal static ConfigEntry<Toggle> RowUnderSail;
	internal static ConfigEntry<float> RowCutoutSpeed;
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
	internal static ConfigEntry<Toggle> SnapToHull;
	internal static ConfigEntry<float> HullInset;
	internal static ConfigEntry<string> HolePositions;
	internal static ConfigEntry<float> StrokeSweep;
	internal static ConfigEntry<float> StrokeRate;
	internal static ConfigEntry<float> TurnStrokeBias;
	internal static ConfigEntry<float> BladeDip;
	internal static ConfigEntry<float> DriveShare;
	internal static ConfigEntry<float> RecoveryLift;
	internal static ConfigEntry<float> FeatherAngle;
	internal static ConfigEntry<float> CatchBlend;
	internal static ConfigEntry<StowStyle> StowedOars;
	internal static ConfigEntry<float> StowedAngle;
	internal static ConfigEntry<float> StowTime;
	internal static ConfigEntry<int> SimulatedRowers;
	internal static ConfigEntry<Toggle> LogRowers;

	private ConfigEntry<T> config<T>(string group, string name, T value, ConfigDescription description, bool synced = true)
	{
		ConfigEntry<T> entry = Config.Bind(group, name, value, description);
		configSync.AddConfigEntry(entry).SynchronizedConfig = synced;
		return entry;
	}

	private ConfigEntry<T> config<T>(string group, string name, T value, string description, bool synced = true) =>
		config(group, name, value, new ConfigDescription(description), synced);

	private void Awake()
	{
		Log = Logger;
		serverConfigLocked = config("1 - General", "Config is locked", Toggle.On, "If on, only admins can change the configuration on a server.");
		configSync.AddLockingConfigEntry(serverConfigLocked);

		Enabled = config("2 - Rowing", "Rowing", Toggle.On, "Seated players row: each player sitting on one of the ship's benches adds paddle force in paddle mode. Off = vanilla.");
		Ships = config("2 - Rowing", "Only these ships", "", "Leave empty - the default - and every ship with benches rows, including boats added by other mods such as OdinShip. Nothing has to be named here for a new boat to work. Set a comma-separated list of prefab names to restrict rowing to just those (VikingShip = Longship, Karve). A ship with no benches never rows either way, because there is nowhere to sit.");
		ExcludedShips = config("2 - Rowing", "Excluded ships", "", "Comma-separated prefab names that never row, even though they have seats. For a boat whose only chair is a helm seat, or one whose handling you would rather leave alone. Checked before 'Only these ships'.");
		HoldFastSeatAnims = config("2 - Rowing", "Hold-fast seat animations", "attach_mast,attach_dragon,attach_bed", "Comma-separated attach animations that are places to hold on rather than benches to row from, and so get no oar and pull nothing. Not every seat on a hull is a rowing station: the Longship has seven, of which four are benches - the other three are the helm, the mast and the figurehead. Vanilla names them by the animation it puts the player into, which is what this matches. The helm is excluded separately, by sitting at the tiller's own attach point, because 'attach_chair' is an ordinary seat animation that a modded hull may use for a real bench. 'oarsmen ship' prints the animation of every seat on the boat you are standing on.");
		RowingSeatAnims = config("2 - Rowing", "Rowing seat animations", "", "Leave empty - the default - and every seat rows except the helm and the hold-fast points above, so a boat from another mod needs nothing named here. Set a comma-separated list of attach animations to go the other way and let only those seats row: 'attach_sitship' restricts rowing to vanilla ship benches exactly. Both lists apply live, and a hull in the water rebuilds its benches when you edit them; a hull that had no rowing bench at all needs a reload to pick one up.");
		BonusPerRower = config("2 - Rowing", "Paddle force per rower (x)", 0.25f, "Extra paddle force per seated rower, as a fraction of the ship's paddle force. 0.25 with 4 rowers = twice the vanilla paddle force. Speed rises less than force because water drag grows with speed. Applies to reverse as well as forward.");
		SteerPerRower = config("2 - Rowing", "Steering force per rower (x)", 0.15f, "Extra turning force per seated rower, as a fraction of the ship's own rudder force. Rowers amplify whatever the helmsman is already asking for, so it is zero with the rudder centred and there is nothing for a rower to aim. Paddle and reverse only, which is exactly where vanilla gives the rudder a push - under sail the ship turns as it always did. 0 = no steering help. Deliberately lower than the paddle share: the game damps turning linearly but speed quadratically, so a given multiplier moves the turn rate far more than it moves the top speed. A full crew at 0.15 is roughly +40% turning force at paddle speed, because vanilla's other, speed-proportional turning force is untouched.");
		MaxSteerShare = config("2 - Rowing", "Max steering share (x)", 1f, "Ceiling on the crew's total steering contribution, however many are aboard. 1 means the crew can at most double the ship's own rudder force; a twelve-bench warship is held to the same limit as a Longship instead of reaching nearly three times it. This bounds what the crew can ask for, which is the safe half of the guard - it only ever removes force we added, so an uncrewed boat is untouched. 0 = no ceiling.");
		MaxTurnRate = config("2 - Rowing", "Max turn rate (degrees per second)", 45f, "Backstop on how fast a crewed ship may swing its bow. Only the yaw is limited, and only the excess is removed, so roll and pitch from waves are left alone - and it is applied solely on ticks where the crew actually added force, so a ship rowing vanilla can never be slowed by it. This catches anything the share ceiling above does not. 45 is set well clear of vanilla so it acts as a guard rail rather than a handling change: run 'oarsmen ship' while turning hard to read your hull's real turn rate before lowering it. 0 = off.");
		MaxRowers = config("2 - Rowing", "Max rowers", 0, "Rowers counted at most. 0 - the default - means every bench counts, so a big hull rewards a big crew: a warship with twelve benches is pulled by twelve. This costs vanilla boats nothing, since the Longship only has four seats anyway. Set a number to cap it if a modded ship turns out to be too quick with a full complement, and remember the per-rower shares above are the gentler way to tune that.");
		RowUnderSail = config("2 - Rowing", "Row under sail", Toggle.Off, "Off (default) = the crew only rows in paddle mode and reverse, so raising the sail is what ends their shift. On = they also help while the sail is out, but only until the ship outruns them - their push fades away with speed and stops entirely at 'Speed where oars stop helping' below, and the oars lift out of the water as it fades. On makes rowing a way to get under way rather than a flat speed bonus; a crew hauls you off the line and off a lee shore, then ships their oars as the sail takes over. Steering help is never added under sail either way, whichever this is set to.");
		RowCutoutSpeed = config("2 - Rowing", "Speed where oars stop helping (m/s)", 5f, "ONLY USED WHEN 'Row under sail' IS ON - ignore this entirely if it is off. An oar pushes the ship only while the blade is moving through the water faster than the hull is; once the ship outruns the blade the crew is just dragging wood. This is the speed where that happens. Below it the crew's help fades smoothly rather than switching off, and the oars visibly lift out of the water as it fades. For scale, since the game never shows you a speed: a Longship paddles at about 3.2 m/s and sails between 3.6 into the wind and 9.4 with a full tailwind; a Karve paddles at 3.1 and sails between 2.8 and 7.0. So the default 5 means the crew hauls hard getting under way and has bowed out by the time any sail is drawing properly. Raise it toward 7 if you want rowers to still count while sailing; drop it toward 4 to make them purely a way to get moving. 0 turns the fade off entirely and gives a flat share at any speed, which is not recommended. Paddle mode and reverse ignore this and keep vanilla's own flat paddle force. 'oarsmen ship' prints your live speed if you want to measure your own hull.");
		ShowRowersOnTiller = config("2 - Rowing", "Show rowers on the tiller", Toggle.On, "Add a 'Rowers 3/4' line to the tiller's hover text, so the helmsman can see the crew without the console.");

		ShowOars = config("3 - Oars", "Show oars", Toggle.On, "Show an oar through the hull beside every occupied bench. Purely visual; each client draws them from the same seat state.");
		OarLength = config("3 - Oars", "Oar length (metres)", 3.6f, "Total oar length, handle to blade tip.");
		OarInboard = config("3 - Oars", "Oar inboard length (metres)", 1.0f, "How much of the oar is inside the hull, from the pivot to the handle end.");
		OarThickness = config("3 - Oars", "Oar shaft thickness (metres)", 0.07f, "Shaft diameter.");
		BladeLength = config("3 - Oars", "Blade length (metres)", 0.8f, "Length of the flat blade at the outboard end.");
		BladeWidth = config("3 - Oars", "Blade width (metres)", 0.2f, "Width of the blade.");
		PivotOutward = config("3 - Oars", "Pivot outward (metres)", 0.6f, "Pivot (oar hole) distance sideways from the bench seat toward the hull, positive = toward the side the bench is on. Measured in game against the Longship and Karve, which is why it is not a round number.");
		PivotUp = config("3 - Oars", "Pivot up (metres)", 0.26f, "Pivot height above the bench seat.");
		PivotForward = config("3 - Oars", "Pivot forward (metres)", 0.19f, "Pivot offset along the ship from the bench seat, positive = toward the bow.");
		SnapToHull = config("3 - Oars", "Snap oars to the hull", Toggle.Off, "Off (default) = the pivot sits at the three offsets above, which are measured against the vanilla hulls. On = the pivot is placed on the hull's own side instead, read off the row of box colliders vanilla builds the sides from, at whatever point along the ship the oar sits. That matters on a hull that tapers, where no single offset fits both ends: the Longship's side stands at 2.4 m from the keel amidships and 1.6 m at the forward benches, while its benches are inset 1.5 and 0.8. Worth trying on a modded boat whose oars come out in the wrong place; 'Pivot outward' is ignored while it is on, and the mod falls back to it on a hull whose sides it cannot read.");
		HullInset = config("3 - Oars", "Oar hole inset from the hull (metres)", 0f, "ONLY USED WHEN 'Snap oars to the hull' IS ON. How far inboard of the hull's side to put the pivot. Positive moves it into the boat, negative out past the planking.");
		HolePositions = config("3 - Oars", "Oar hole positions", "", "Comma-separated positions of the hull's oar holes along the ship (ship-local Z, metres, bow positive). When set, each oar snaps to the closest hole instead of sitting beside its bench. Empty = no snapping. Find them with the 'oarsmen ship' console command and a bit of trial.");
		StrokeSweep = config("3 - Oars", "Stroke sweep (degrees)", 40f, "Total fore-aft swing of an oar per stroke while rowing.");
		StrokeRate = config("3 - Oars", "Stroke rate (strokes per minute)", 26f, new ConfigDescription("How fast the crew pulls. 26 is a working crew moving a loaded hull; a racing eight sprints at about 40, and much above that the oars look like they are being stirred. Before 0.3.0 this was fixed at the rate vanilla wiggles the steering paddle, which works out at 57. Nothing depends on the two matching - the paddle is vanilla's own animation on another part of the boat, and the force the crew adds is flat rather than stroke-timed, so this is purely how it looks.", new AcceptableValueRange<float>(4f, 90f)));
		TurnStrokeBias = config("3 - Oars", "Turn stroke bias (x)", 1.6f, "How much rudder it takes before the inside bank of the turn stops pulling ahead and backs water instead, while the outside bank keeps pulling - how a crew pivots a longship on the spot. The bank turns over at 1 divided by this, so 1.6 means a little over three fifths of full rudder; higher reverses sooner, and 0 keeps both banks pulling ahead whatever the rudder is doing. Only the direction changes: a backing bank rows the same full stroke the other way round, easing across rather than flipping. Purely visual - the turning force itself comes from 'Steering force per rower'.");
		DriveShare = config("3 - Oars", "Drive share of the stroke", 0.45f, new ConfigDescription("How much of each stroke is the drive - the loaded half, blade in the water, sweeping aft - with the rest being the recovery that carries the oar forward again. A real crew pulls hard and comes forward at more leisure, so it sits below half. The oar is momentarily still at the catch and at the finish either way, which is what stops the stroke looking like an oar being waved about.", new AcceptableValueRange<float>(0.15f, 0.85f)));
		RecoveryLift = config("3 - Oars", "Recovery lift (degrees)", 22f, new ConfigDescription("How far the blade rises above the drive angle for the swing forward. It only has to clear the water: 'Blade dip' puts the blade in, this takes it back out. Too little and the crew drags its blades through the water on the recovery, too much and the oars wave in the air.", new AcceptableValueRange<float>(0f, 60f)));
		FeatherAngle = config("3 - Oars", "Feather angle (degrees)", 90f, new ConfigDescription("How far the blade turns flat on the recovery. A crew feathers the blade out of the water on the way forward and squares it up again at the catch, which is what makes a stroke read as rowing rather than as poles being waved. 90 is fully flat, 0 turns feathering off and leaves the blade square all the way round.", new AcceptableValueRange<float>(0f, 90f)));
		CatchBlend = config("3 - Oars", "Catch blend (share of stroke)", 0.16f, new ConfigDescription("How much of each stroke the blade spends going into the water at the catch, and again coming out at the finish. Centred on the catch, so half of it is the end of the recovery and half the start of the drive. 0.16 at 26 strokes a minute is about a third of a second each way; 0.3.1 had the equivalent of 0.09, which slapped the blade in. Capped at the shorter of the drive and the recovery.", new AcceptableValueRange<float>(0.02f, 0.5f)));
		BladeDip = config("3 - Oars", "Blade dip (degrees)", 35f, "How far the oar points down into the water while rowing. 35 rather than a shallower angle because the oar holes sit well above the waterline - the blades have to reach down past the freeboard before they are in the water at all.");
		StowedOars = config("3 - Oars", "Stowed oars", StowStyle.AlongHull, "What an oar does when its rower is not pulling - sail out, ship stopped, or the bank a hard rudder has cancelled. AlongHull (default) = shipped fore and aft along the side of the boat, blades aft, the way a crew boats their oars. Outboard = left standing straight out to the side, lifted clear of the water by the angle below. Either way the oar turns and lifts on the same blend the stroke fades on, so it swings in as the crew eases off rather than flicking round.");
		StowedAngle = config("3 - Oars", "Stowed angle (degrees)", 12f, "How far a stowed oar is lifted clear of the water. Applies to both stow styles.");
		StowTime = config("3 - Oars", "Stow time (seconds)", 0.8f, new ConfigDescription("How long an oar takes to ship itself, and to come back out. Every reason an oar stops rowing arrives as a step change - the helmsman drops to Stop, the sail goes up, a rower stands - so without this the oar would jump into the stowed pose rather than swinging there from wherever the stroke had it. Lower is brisker; 0 is as near instant as makes no difference.", new AcceptableValueRange<float>(0f, 5f)));

		SimulatedRowers = config("4 - Debug", "Simulate rowers", 0, new ConfigDescription("Testing aid: pretend at least this many benches are manned, so one player can see and feel a full crew in single player. Empty benches are filled bow to stern until the total is reached; real rowers always count first, so 4 on a Longship you are already rowing adds three phantoms. They row, draw oars and push the ship exactly as players would - the point is to tune oar placement and the force multipliers without four people. Only applies while somebody is actually aboard, so derelict boats stay still. 0 = off. 'oarsmen rowers' marks the fake ones. Leave this at 0 on a real server.", new AcceptableValueRange<int>(0, 32)));
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
