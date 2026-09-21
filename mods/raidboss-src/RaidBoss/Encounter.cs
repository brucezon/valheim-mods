using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

namespace RaidBoss;

// One boss's script, parsed from a single config line:
//
//   75% "Moder calls her brood": Hatchling 1+1 | every 45s above 25%: Hatchling 1+0
//
// Rules are separated by |. A rule is  <trigger> ["message"] : <spawns>.
//   trigger  NN%                          fires once when the boss drops to NN% health
//            every NNs [below NN%] [above NN%]   repeats while the boss is hurt and inside that health band
//   spawns   comma separated  Prefab[*|**] base+perPlayer   (count = floor(base + perPlayer x players), * = one star)
internal sealed class Encounter
{
	// Set by the plugin every tick: 1, or the "more adds" factor while a damage-scaling BruceQoL is on this server.
	internal static float CountMultiplier = 1f;

	internal sealed class Spawn
	{
		public string Prefab;
		public string Trait = "";       // "GoblinBrute:Ironhide" - a named set of resistances from the Traits setting
		public int Level = 1;
		public float Base;
		public float PerPlayer;
		// Optional "@1-2" / "@3+" / "@2": this entry only exists for that many players. It is what lets one creature
		// REPLACE another as the group grows (GoblinBrute 1+0 @1-2, GoblinBrute* 1+0 @3+), which base+perPlayer cannot say.
		public int MinPlayers = 1;
		public int MaxPlayers = int.MaxValue;

		// The count as written, before any "more adds" multiplier.
		public int PlainCount(int players)
		{
			if (players < MinPlayers || players > MaxPlayers) return 0;
			return Math.Max(0, (int)Math.Floor(Base + PerPlayer * players + 0.001f));
		}

		public string Suffix => MinPlayers <= 1 && MaxPlayers == int.MaxValue ? "" : MaxPlayers == int.MaxValue ? $" @{MinPlayers}+" : MinPlayers == MaxPlayers ? $" @{MinPlayers}" : $" @{MinPlayers}-{MaxPlayers}";

		public int Count(int players)
		{
			int plain = PlainCount(players);
			// Multiplied and rounded down: at 1.3, 4 -> 5, 5 -> 6, 8 -> 10, and 1 to 3 are unchanged. Applied after the plain count so a single
			// heavy add never doubles.
			return Math.Max(0, (int)Math.Floor(plain * CountMultiplier + 0.001f));
		}
	}

	// Something a rule does besides spawning: "ward 0.5 break", "strike ice r4 d2 dmg90 x2", "boss Ironhide", "break 8",
	// "heal 5", "weather Twilight_SnowStorm 60", "effect fx_name", "status Wet". Carried out by Director.RunActions.
	internal sealed class Act
	{
		public string Verb;
		public string[] Args;
		public override string ToString() => Verb + (Args.Length > 0 ? " " + string.Join(" ", Args) : "");
	}

	internal static readonly HashSet<string> Verbs = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "ward", "guard", "shield", "boss", "strike", "chase", "rain", "storm", "break", "heal", "weather", "effect", "status" };

	internal sealed class Rule
	{
		public bool Repeating;
		public bool HeroicOnly;         // "heroic 60%: ..." - this rule exists only in a heroic fight
		public bool NormalOnly;         // "normal 40%: ..." - this rule is left out of a heroic fight (a "heroic 40%" rule replaces it)
		public float Threshold;          // 0..1, threshold rules
		public float Interval;           // seconds, repeating rules
		public float Below = 1f;         // repeating rules run while Above <= health < Below
		public float Above;
		public string Message = "";
		public readonly List<Spawn> Spawns = new List<Spawn>();
		public readonly List<Act> Actions = new List<Act>();
	}

	public readonly List<Rule> Rules = new List<Rule>();
	public readonly List<string> Errors = new List<string>();

	static readonly Regex MessageRx = new Regex("\"([^\"]*)\"", RegexOptions.Compiled);
	static readonly Regex ThresholdRx = new Regex(@"^(\d+(?:\.\d+)?)\s*%$", RegexOptions.Compiled);
	static readonly Regex EveryRx = new Regex(@"^every\s+(\d+(?:\.\d+)?)\s*s?((?:\s+(?:below|above)\s+\d+(?:\.\d+)?\s*%)*)$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
	static readonly Regex BandRx = new Regex(@"(below|above)\s+(\d+(?:\.\d+)?)\s*%", RegexOptions.Compiled | RegexOptions.IgnoreCase);
	static readonly Regex SpawnRx = new Regex(@"^([^\s*]+)(\*{0,2})\s+(\d+(?:\.\d+)?)(?:\s*\+\s*(\d+(?:\.\d+)?))?(?:\s*@\s*(\d+)\s*(?:(\+)|-\s*(\d+))?)?$", RegexOptions.Compiled);

	static float F(string s) => float.Parse(s, CultureInfo.InvariantCulture);

	// "Prefab[*|**] base[+perPlayer]" -> Spawn. Shared with WorldEncounters.
	internal static bool TryParseSpawn(string text, out Spawn spawn)
	{
		spawn = null;
		Match sm = SpawnRx.Match(text.Trim());
		if (!sm.Success) return false;
		spawn = new Spawn
		{
			Prefab = sm.Groups[1].Value.Split(':')[0],
			Trait = sm.Groups[1].Value.Contains(":") ? sm.Groups[1].Value.Substring(sm.Groups[1].Value.IndexOf(':') + 1) : "",
			Level = 1 + sm.Groups[2].Value.Length,
			Base = F(sm.Groups[3].Value),
			PerPlayer = sm.Groups[4].Success ? F(sm.Groups[4].Value) : 0f,
		};
		if (sm.Groups[5].Success)
		{
			spawn.MinPlayers = Math.Max(1, int.Parse(sm.Groups[5].Value, CultureInfo.InvariantCulture));
			spawn.MaxPlayers = sm.Groups[6].Success ? int.MaxValue
				: sm.Groups[7].Success ? Math.Max(spawn.MinPlayers, int.Parse(sm.Groups[7].Value, CultureInfo.InvariantCulture))
				: spawn.MinPlayers;
		}
		return true;
	}

	public static Encounter Parse(string text)
	{
		var enc = new Encounter();
		if (string.IsNullOrWhiteSpace(text)) return enc;
		foreach (string rawRule in text.Split('|'))
		{
			string s = rawRule.Trim();
			if (s.Length == 0) continue;
			var rule = new Rule();
			Match msg = MessageRx.Match(s);
			if (msg.Success)
			{
				rule.Message = msg.Groups[1].Value;
				s = s.Remove(msg.Index, msg.Length);
			}
			int colon = s.IndexOf(':');
			if (colon < 0) { enc.Errors.Add($"no ':' in rule '{rawRule.Trim()}'"); continue; }
			string trigger = s.Substring(0, colon).Trim();
			if (trigger.StartsWith("heroic ", StringComparison.OrdinalIgnoreCase)) { rule.HeroicOnly = true; trigger = trigger.Substring(7).Trim(); }
			else if (trigger.StartsWith("normal ", StringComparison.OrdinalIgnoreCase)) { rule.NormalOnly = true; trigger = trigger.Substring(7).Trim(); }
			string spawns = s.Substring(colon + 1).Trim();

			Match m;
			if ((m = ThresholdRx.Match(trigger)).Success)
			{
				rule.Threshold = F(m.Groups[1].Value) / 100f;
			}
			else if ((m = EveryRx.Match(trigger)).Success)
			{
				rule.Repeating = true;
				rule.Interval = Math.Max(1f, F(m.Groups[1].Value));
				foreach (Match b in BandRx.Matches(m.Groups[2].Value))
				{
					float v = F(b.Groups[2].Value) / 100f;
					if (b.Groups[1].Value.Equals("below", StringComparison.OrdinalIgnoreCase)) rule.Below = v; else rule.Above = v;
				}
			}
			else { enc.Errors.Add($"trigger '{trigger}' is neither 'NN%' nor 'every NNs [below NN%] [above NN%]'"); continue; }

			foreach (string rawSpawn in spawns.Split(','))
			{
				string sp = rawSpawn.Trim();
				if (sp.Length == 0) continue;
				string[] words = sp.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
				if (Verbs.Contains(words[0]))
				{
					var args = new string[words.Length - 1];
					Array.Copy(words, 1, args, 0, args.Length);
					rule.Actions.Add(new Act { Verb = words[0].ToLowerInvariant(), Args = args });
					continue;
				}
				if (!TryParseSpawn(sp, out Spawn parsed)) { enc.Errors.Add($"spawn '{sp}' is not 'Prefab[*] base+perPlayer [@players]'"); continue; }
				rule.Spawns.Add(parsed);
			}
			if (rule.Spawns.Count == 0 && rule.Actions.Count == 0) { enc.Errors.Add($"rule '{trigger}' does nothing"); continue; }
			enc.Rules.Add(rule);
		}
		return enc;
	}

	public string Describe(int players)
	{
		var parts = new List<string>();
		foreach (Rule r in Rules)
		{
			var sp = new List<string>();
			foreach (Spawn s in r.Spawns) sp.Add($"{s.Count(players)}x {s.Prefab}{(s.Trait.Length > 0 ? ":" + s.Trait : "")}{new string('*', s.Level - 1)}{s.Suffix}");
			foreach (Act a in r.Actions) sp.Add("[" + a + "]");
			string when = (r.HeroicOnly ? "heroic " : r.NormalOnly ? "normal " : "") + (r.Repeating ? $"every {r.Interval:0}s [{r.Above * 100:0}-{r.Below * 100:0}%)" : $"{r.Threshold * 100:0}%");
			parts.Add($"{when}: {string.Join(" + ", sp)}");
		}
		return string.Join(" | ", parts);
	}
}
