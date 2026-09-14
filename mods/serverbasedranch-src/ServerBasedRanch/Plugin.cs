using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using UnityEngine;

namespace ServerBasedRanch;

// Server-only. Nothing to install on clients; vanilla clients see the results when they load the pen.
//
// A dedicated server holds the saved state of every object in the world (its ZDO) whether or not any
// player has that zone loaded, but vanilla only advances taming and breeding through the animal's
// components, which exist only in loaded zones. This plugin ticks the unloaded ones directly on their
// saved fields, with the vanilla rules and numbers: hungry animals eat food lying in the pen (real
// item stacks are decremented), fed wild animals tame, fed tamed animals gain love points, conceive and
// give birth. A birth creates the offspring's saved object with the fields the game expects (prefab,
// position, tamed, level, spawn time); vanilla Growup then raises it on the world clock. Zones a player
// has loaded are left alone: the player's client is running the real thing there.
[BepInPlugin(GUID, Name, Version)]
public class ServerBasedRanchPlugin : BaseUnityPlugin
{
	public const string GUID = "bruceirons.ServerBasedRanch";
	public const string Name = "ServerBasedRanch";
	public const string Version = "1.0.2";

	internal static ManualLogSource Log;

	internal static ConfigEntry<bool> Enabled;
	internal static ConfigEntry<float> TickInterval;
	internal static ConfigEntry<float> FeedRadius;
	internal static ConfigEntry<float> MaxHours;
	internal static ConfigEntry<float> UnloadedSpeed;
	internal static ConfigEntry<int> MaxPerPen;
	internal static ConfigEntry<float> PartnerRadius;
	internal static ConfigEntry<bool> LogActivity;
	internal static ConfigEntry<bool> LogDetails;

	private float m_timer;

	private void Awake()
	{
		Log = Logger;
		Enabled = Config.Bind("1 - General", "Enabled", true,
			"Tick unloaded pens on the server: eat from the pen, tame, breed, give birth. Off = vanilla (nothing happens while unloaded).");
		TickInterval = Config.Bind("1 - General", "Tick interval (seconds)", 30f,
			"How often the server sweeps the world for tracked animals. Each sweep advances every unloaded tracked animal by the time since its last tick, in 10 s steps.");
		FeedRadius = Config.Bind("1 - General", "Feed radius (metres)", 8f,
			"How far from where the animal stands food is taken from. Vanilla animals walk up to 5 m to eat while loaded.");
		MaxHours = Config.Bind("1 - General", "Catch-up limit (hours)", 12f,
			"At most this much time is advanced per animal per tick. Bounds what a long server downtime does when the server comes back.");
		UnloadedSpeed = Config.Bind("1 - General", "Unloaded speed (%)", 50f,
			new ConfigDescription("How fast an unloaded pen progresses compared to a loaded one. 100 = real time, 50 = an hour away counts as half an hour of eating, taming and breeding. Applies to the server-side ticks only; grown-up timers on newborns are the game's own world clock.", new AcceptableValueRange<float>(0f, 400f)));
		MaxPerPen = Config.Bind("1 - General", "Max animals per pen", 4,
			"Breeding stops when this many adults plus young of the same kind are within 10 m. Vanilla 4. Only affects the server-side ticks; loaded pens use the game's own value (or BruceQoL's).");
		PartnerRadius = Config.Bind("1 - General", "Partner radius while unloaded (metres)", 10f,
			"A tame partner of the same kind within this distance counts for breeding. Vanilla uses 3 m but re-checks every 10 s while the animals wander; unloaded animals stand still, so a pen-sized radius stands in for that.");
		LogActivity = Config.Bind("1 - General", "Log activity", true,
			"Log one line per sweep that fed, tamed, gained love points, conceived or bred something, with the reasons anything was blocked.");
		LogDetails = Config.Bind("1 - General", "Log every sweep", false,
			"Also log the line on sweeps where nothing changed. Useful while checking that a pen is being ticked at all.");
	}

	private void Update()
	{
		if (!Enabled.Value) return;
		m_timer += Time.deltaTime;
		if (m_timer < Mathf.Max(5f, TickInterval.Value)) return;
		m_timer = 0f;
		if (ZNet.instance == null || !ZNet.instance.IsServer() || ZDOMan.instance == null || ZNetScene.instance == null) return;
		try
		{
			Ranch.Tick();
		}
		catch (System.Exception e)
		{
			Log.LogWarning($"ServerBasedRanch tick failed: {e.GetType().Name}: {e.Message}");
		}
	}
}
