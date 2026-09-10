using System.Reflection;
using HarmonyLib;

namespace LocalizationManager
{
    // Localization.AddWord is private in assembly_guiutils (1.0). Calling it through reflection
    // sidesteps the runtime accessibility check that a direct call trips in this build.
    internal static class LocalizationAccess
    {
        private static readonly MethodInfo addWord =
            AccessTools.Method(typeof(Localization), "AddWord", new[] { typeof(string), typeof(string) });

        public static void AddWord(Localization localization, string key, string text)
        {
            addWord.Invoke(localization, new object[] { key, text });
        }
    }
}
