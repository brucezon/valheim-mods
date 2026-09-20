using BepInEx.Configuration;
using HarmonyLib;

namespace BruceQoL;

// 21 - Recovery: a shorter fireside wait for the Rested buff after a death.
//
// Vanilla (1.0.15): by a fire, under shelter and unnoticed, Player.UpdateEnvStatusEffects keeps the "Resting"
// status on the player. Resting is an SE_Cozy: it counts m_time up from zero and, once past m_delay, adds its
// m_statusEffect (Rested) every tick. Anything that breaks the conditions removes Resting, and the count starts
// over. Player.m_timeSinceDeath is the game's own "died recently" clock (it drives the no-skill-drain grace
// period): zeroed in OnDeath, advanced while alive, saved with the character.
//
// While m_timeSinceDeath is inside the window, Rested is granted once m_time passes m_delay x multiplier. Past
// m_delay vanilla does the granting itself, so only the early stretch is touched. Food, the tombstone and the
// length of Rested (comfort level) are untouched. Runs for the local player only; the entries are server-synced.
internal static class Recovery
{
	internal static ConfigEntry<float> RestingTimeAfterDeath;
	internal static ConfigEntry<float> JustDiedWindow;

	private static bool logged;
	private static readonly BepInEx.Logging.ManualLogSource s_log = BepInEx.Logging.Logger.CreateLogSource("BruceQoL");

	[HarmonyPatch(typeof(SE_Cozy), nameof(SE_Cozy.UpdateStatusEffect))]
	private static class CozyPatch
	{
		private static void Postfix(SE_Cozy __instance)
		{
			if (RestingTimeAfterDeath == null) return;
			Player player = __instance.m_character as Player;
			if (player == null || player != Player.m_localPlayer) return;
			float mult = UnityEngine.Mathf.Clamp01(RestingTimeAfterDeath.Value);
			if (!logged)
			{
				logged = true;
				s_log.LogInfo($"BruceQoL recovery: the game's fireside wait is {__instance.m_delay:0.#} s; for {JustDiedWindow.Value:0} s after a death it is {__instance.m_delay * mult:0.#} s.");
			}
			if (mult >= 1f || player.m_timeSinceDeath > JustDiedWindow.Value) return;
			if (__instance.m_time > __instance.m_delay || __instance.m_time < __instance.m_delay * mult) return;
			player.GetSEMan().AddStatusEffect(__instance.m_statusEffectHash, resetTime: true, 0, 0f, -1);
		}
	}
}
