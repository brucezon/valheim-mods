using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Rendering;

namespace BruceNetworking;

// Server-only. Two features, each behind its own toggle:
//   1. Send priority  - the server's per-peer ZDO send order gets a per-prefab-class bias, so players,
//                       creatures, doors, chests and other things you interact with go out before plain
//                       build pieces, plants and rocks when a peer's send budget is saturated.
//   2. Ownership      - 1.0 arbitrates zone ownership on the server (ZDOMan.ReleaseZDOS runs only there).
//                       We claim in preference order (LAN peers first, then by ping) and periodically move
//                       ownership from a worse peer to a better one that is well inside the object's zone,
//                       with a per-object cooldown so it never flaps.
[BepInPlugin(GUID, Name, Version)]
public class BruceNetworkingPlugin : BaseUnityPlugin
{
	public const string GUID = "bruceirons.BruceNetworking";
	public const string Name = "BruceNetworking";
	public const string Version = "0.2.1";

	internal static ManualLogSource Log;

	// --- 1 General
	internal static ConfigEntry<bool> PriorityEnabled;
	internal static ConfigEntry<bool> OwnershipEnabled;
	internal static ConfigEntry<bool> LogStats;
	internal static ConfigEntry<float> StatsInterval;

	// --- 2 Send priority (bias in metres; lower sends sooner; vanilla staleness is worth up to 150)
	internal static ConfigEntry<float> BiasPlayer;
	internal static ConfigEntry<float> BiasCreature;
	internal static ConfigEntry<float> BiasInteractive;
	internal static ConfigEntry<float> BiasDynamic;
	internal static ConfigEntry<float> BiasStructure;
	internal static ConfigEntry<float> BiasNature;
	internal static ConfigEntry<bool> LogSendOrder;

	// --- 3 Ownership
	internal static ConfigEntry<string> LanSubnets;
	internal static ConfigEntry<string> ForceLanPlayers;
	internal static ConfigEntry<string> ForceRemotePlayers;
	internal static ConfigEntry<bool> PingTiebreak;
	internal static ConfigEntry<int> PingDelta;
	internal static ConfigEntry<float> InnerMargin;
	internal static ConfigEntry<float> TransferCooldown;
	internal static ConfigEntry<int> MaxTransfersPerPass;
	internal static ConfigEntry<bool> SteerVehicles;
	internal static ConfigEntry<bool> LogTransfers;

	// --- 4 Send loop
	internal static ConfigEntry<bool> SendLoopEnabled;
	internal static ConfigEntry<float> SendInterval;

	// --- 5 RPC area of interest
	internal static ConfigEntry<bool> RpcAoiEnabled;
	internal static ConfigEntry<float> RpcRadius;

	// --- 6 Wear throttle
	internal static ConfigEntry<bool> WearThrottleEnabled;
	internal static ConfigEntry<float> WearInterval;

	internal static bool IsServer => ZNet.instance != null && ZNet.instance.IsServer();

	private void Awake()
	{
		Log = Logger;

		PriorityEnabled = Config.Bind("1 - General", "Send priority", true,
			"Bias the server's ZDO send order by prefab class (players, creatures, doors, chests first; build pieces, plants, rocks last).");
		OwnershipEnabled = Config.Bind("1 - General", "Ownership steering", true,
			"Prefer LAN peers (then lowest ping) as zone owners. Claims go to the best peer in range, and ownership held by a worse peer is moved to a better one that is well inside the zone.");
		LogStats = Config.Bind("1 - General", "Log peer stats", true,
			"Periodically log every peer's address, LAN/remote class, ping and owned-object count.");
		StatsInterval = Config.Bind("1 - General", "Stats interval", 60f,
			"Seconds between peer stat lines.");

		BiasPlayer = Config.Bind("2 - Send priority", "Player", -300f, "Bias for player characters.");
		BiasCreature = Config.Bind("2 - Send priority", "Creature", -120f, "Bias for creatures, tames and anything with AI.");
		BiasInteractive = Config.Bind("2 - Send priority", "Interactive", -90f, "Bias for doors, chests, cooking stations, smelters, fires, beds, crafting stations, item stands, signs, turrets, windmills.");
		BiasDynamic = Config.Bind("2 - Send priority", "Dynamic", -60f, "Bias for ships, carts, projectiles, dropped items, fish.");
		BiasStructure = Config.Bind("2 - Send priority", "Structure", 0f, "Bias for plain build pieces (WearNTear only). 0 = vanilla order.");
		BiasNature = Config.Bind("2 - Send priority", "Nature", 40f, "Bias for trees, logs, rocks, ore, plants, pickables. Positive = sent later.");
		LogSendOrder = Config.Bind("2 - Send priority", "Log send order sample", false,
			"Debug: every few seconds log the first entries of one peer's sorted send list with their class.");

		LanSubnets = Config.Bind("3 - Ownership", "LAN subnets", "10.0.0.0/8, 172.16.0.0/12, 192.168.0.0/16, 127.0.0.0/8",
			"Comma-separated IPv4 CIDR ranges that count as LAN. Tailscale (100.64.0.0/10) is deliberately NOT here: those players are remote.");
		ForceLanPlayers = Config.Bind("3 - Ownership", "Force LAN players", "",
			"Comma-separated player names always treated as LAN regardless of address.");
		ForceRemotePlayers = Config.Bind("3 - Ownership", "Force remote players", "",
			"Comma-separated player names always treated as remote regardless of address.");
		PingTiebreak = Config.Bind("3 - Ownership", "Ping tiebreak", true,
			"Within the same class (LAN vs LAN, remote vs remote) move ownership to a peer whose ping is lower by at least Ping delta.");
		PingDelta = Config.Bind("3 - Ownership", "Ping delta", 60,
			"Milliseconds of ping advantage required before the tiebreak moves ownership.");
		InnerMargin = Config.Bind("3 - Ownership", "Inner margin", 12f,
			"Metres. Only move ownership to a peer when the object is at least this far inside the peer's active area (vanilla's 1.5-zone box / 1.75-zone circle), so objects near the edge never bounce.");
		TransferCooldown = Config.Bind("3 - Ownership", "Transfer cooldown", 10f,
			"Seconds before the same object may be moved again. Also bounds how fast a move that raced with an in-flight update from the old owner gets corrected.");
		MaxTransfersPerPass = Config.Bind("3 - Ownership", "Max transfers per pass", 300,
			"Cap on ownership moves per 2-second pass, to spread the sync cost.");
		SteerVehicles = Config.Bind("3 - Ownership", "Steer ships and carts", false,
			"Also move ships and carts. Off by default: their physics authority is sensitive to owner changes mid-motion.");
		LogTransfers = Config.Bind("3 - Ownership", "Log transfers", true,
			"Log a one-line summary whenever a pass moves ownership.");

		SendLoopEnabled = Config.Bind("4 - Send loop", "Multi-peer send loop", true,
			"Service every connected peer on each send tick. Vanilla services one peer per frame after a 0.05 s wait, so with N players each peer waits 0.05 s plus N frames.");
		SendInterval = Config.Bind("4 - Send loop", "Send interval", 0.05f,
			"Seconds between send ticks. 0.05 = vanilla cadence (20/s). Lower = more responsive and more bandwidth; 0.02 is what VBNetTweaks uses.");

		RpcAoiEnabled = Config.Bind("5 - RPC area of interest", "RPC area of interest", true,
			"Forward broadcast RPCs that target an object only to peers within RPC radius of that object. Peers farther away could not have the object loaded and would discard the RPC anyway.");
		RpcRadius = Config.Bind("5 - RPC area of interest", "RPC radius", 300f,
			"Metres. Floor for the filter radius. The mod always uses at least the loaded radius implied by the server's simulation distance (about 190 m at the default), so a larger -simulationdistance is handled automatically. Objects flagged Distant are always forwarded.");

		WearThrottleEnabled = Config.Bind("6 - Wear throttle", "Wear throttle", true,
			"For SERVER-owned build pieces that are older than 30 s, at full health, dry, and not in the Ashlands or Deep North, run the wear tick (which includes the support physics check) at most once per Wear interval instead of every updater pass.");
		WearInterval = Config.Bind("6 - Wear throttle", "Wear interval", 10f,
			"Seconds. Upper bound on how late a server-owned piece notices it lost support.");

		PeerClassifier.Reload();
		LanSubnets.SettingChanged += (_, _) => PeerClassifier.Reload();
		ForceLanPlayers.SettingChanged += (_, _) => PeerClassifier.Reload();
		ForceRemotePlayers.SettingChanged += (_, _) => PeerClassifier.Reload();
		foreach (ConfigEntry<float> e in new[] { BiasPlayer, BiasCreature, BiasInteractive, BiasDynamic, BiasStructure, BiasNature })
		{
			e.SettingChanged += (_, _) => PrefabClasses.ClearCache();
		}

		bool headless = SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null;
		Log.LogInfo($"{Name} {Version} loaded ({(headless ? "dedicated server" : "client/host")}). Patches are no-ops unless this process is the server.");

		Harmony harmony = new(GUID);
		harmony.PatchAll();
	}
}
