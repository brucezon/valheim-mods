using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;

namespace SecondWind;

// Client-only, and deliberately not server-synced: no ServerSync, no version handshake, nothing a server or another
// player can see, so it can never stop anyone joining. One patch.
//
// Vanilla: standing by a fire under shelter gives the "Resting" status (SE_Cozy). It counts up from zero and grants
// Rested once m_time passes m_delay; any interruption (an enemy senses you, you leave the fire, you are wet or burning)
// removes Resting and the count starts over. Player.m_timeSinceDeath is the game's own clock for "died recently"
// (it drives the no-skill-drain grace period), is zeroed in OnDeath and is saved with the character.
//
// Here: while m_timeSinceDeath is inside the window, Rested is granted once m_time passes m_delay x multiplier.
// Food, the tombstone, the length of the Rested buff and everything else are untouched.
[BepInPlugin(GUID, Name, Version)]
public class SecondWindPlugin : BaseUnityPlugin
{
	public const string GUID = "bruceirons.SecondWind";
	public const string Name = "SecondWind";
	public const string Version = "0.1.0";

	internal static ManualLogSource Log;
	internal static ConfigEntry<bool> Enabled;
	internal static ConfigEntry<float> RestingTime;
	internal static ConfigEntry<float> Window;
	static bool loggedDelay;

	void Awake()
	{
		Log = Logger;
		Enabled = Config.Bind("1 - General", "Enabled", true, "Off = the fireside wait is always vanilla.");
		RestingTime = Config.Bind("1 - General", "Resting time after a death (x)", 0.25f,
			new ConfigDescription("Multiplier on the fireside wait before Rested arrives, while you have recently died. 1 = vanilla, 0.25 = a quarter of the wait, 0 = Rested the moment you are resting.", new AcceptableValueRange<float>(0f, 1f)));
		Window = Config.Bind("1 - General", "Counts as just died for (seconds)", 120f,
			new ConfigDescription("How long after a death the shorter wait applies. The clock is the game's own time-since-death, which keeps running while you walk back.", new AcceptableValueRange<float>(0f, 3600f)));
		new Harmony(GUID).PatchAll();
	}

	[HarmonyPatch(typeof(SE_Cozy), nameof(SE_Cozy.UpdateStatusEffect))]
	static class CozyPatch
	{
		static void Postfix(SE_Cozy __instance)
		{
			if (!Enabled.Value) return;
			Player player = __instance.m_character as Player;
			if (player == null || player != Player.m_localPlayer) return;
			if (!loggedDelay)
			{
				loggedDelay = true;
				Log.LogInfo($"vanilla fireside wait is {__instance.m_delay:0.#} s; after a death it becomes {__instance.m_delay * RestingTime.Value:0.#} s for {Window.Value:0} s");
			}
			if (player.m_timeSinceDeath > Window.Value) return;
			// Past m_delay vanilla grants Rested itself, every tick, so only the early part needs help.
			if (__instance.m_time > __instance.m_delay || __instance.m_time < __instance.m_delay * RestingTime.Value) return;
			player.GetSEMan().AddStatusEffect(__instance.m_statusEffectHash, resetTime: true, 0, 0f, -1);
		}
	}
}
