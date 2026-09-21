using System;
using BepInEx;
using BepInEx.Bootstrap;
using BepInEx.Configuration;

namespace BossDirector;

// "Boss fight mode": while players are engaged with a boss, BruceQoL's own damage settings are changed ON THE SERVER,
// and BruceQoL's ServerSync pushes every change of a synced setting to all clients at once (ConfigSync.AddConfigEntry
// broadcasts on SettingChanged). So the whole group gets softer adds for the length of the fight with no client code of
// ours at all: "Enemy damage to players" has been in BruceQoL since 1.5.0. "Boss damage to players" is newer (1.19.2);
// a client without it applies the enemy number to the boss too, which errs on the easy side.
//
// The override lives in memory only. BruceQoL's config file is never written while it is active (SaveOnConfigSet is
// switched off for the duration), so a crash mid-fight cannot leave the server stuck on the fight values: a restart
// reads the normal ones. The settings are server-wide, so a player elsewhere in the world is softened too while a
// fight is on.
internal static class FightMode
{
	const string BruceQoLGuid = "bruceirons.BruceQoL";
	const string Section = "13 - Combat";

	static ConfigFile file;
	static ConfigEntry<float> enemy, boss;
	static float enemyNormal, bossNormal;
	static bool savedSaveFlag;
	static float quiet;

	internal static bool Active { get; private set; }
	internal static bool Available => enemy != null;

	internal static void Find()
	{
		file = null; enemy = null; boss = null;
		if (!Chainloader.PluginInfos.TryGetValue(BruceQoLGuid, out PluginInfo info) || info.Instance == null)
		{
			BossDirectorPlugin.Log.LogInfo("boss fight mode: BruceQoL is not on this server, so damage is never changed and waves stay as written");
			return;
		}
		file = info.Instance.Config;
		file.TryGetEntry(new ConfigDefinition(Section, "Enemy damage to players"), out enemy);
		file.TryGetEntry(new ConfigDefinition(Section, "Boss damage to players"), out boss);
		BossDirectorPlugin.Log.LogInfo(enemy == null
			? $"boss fight mode: BruceQoL {info.Metadata.Version} has no 'Enemy damage to players' setting; nothing will be changed"
			: $"boss fight mode: BruceQoL {info.Metadata.Version} found. Normal enemy damage x{enemy.Value:0.##}" +
			  (boss != null ? $", boss damage {(boss.Value > 0f ? "x" + boss.Value.ToString("0.##") : "follows enemy damage")}" : " (this BruceQoL has no separate boss number; 1.19.2+ does)"));
	}

	// Called once a second with whether anybody is in range of a known boss.
	static bool heroic;

	internal static void Tick(bool engaged, bool heroicFight, float dt)
	{
		if (!Available) return;
		if (heroicFight != heroic)
		{
			heroic = heroicFight;
			if (Active) BossDirectorPlugin.Log.LogInfo($"boss fight mode: heroic {(heroic ? "ON" : "off")}, boss damage x{BossValue():0.##}");
		}
		bool want = engaged && BossDirectorPlugin.FightModeEnabled.Value;
		if (want) quiet = 0f; else quiet += dt;

		if (!Active)
		{
			if (want) Enter();
			return;
		}
		// Deaths and respawns must not flick it off and on.
		if (!want && (quiet >= BossDirectorPlugin.FightModeLinger.Value || !BossDirectorPlugin.FightModeEnabled.Value)) { Leave("the fight is over"); return; }
		Hold();
	}

	static void Enter()
	{
		enemyNormal = enemy.Value;
		bossNormal = boss != null ? boss.Value : 0f;
		savedSaveFlag = file.SaveOnConfigSet;
		file.SaveOnConfigSet = false;
		Active = true;
		Hold();
		BossDirectorPlugin.Log.LogInfo($"boss fight mode ON: enemy damage to players x{enemyNormal:0.##} -> x{enemy.Value:0.##}" +
			(boss != null ? $", boss damage -> x{boss.Value:0.##}" : "") + $"; waves x{BossDirectorPlugin.MoreAdds.Value:0.##}");
	}

	// Keeps the fight values in place. If something else changed a setting meanwhile (an admin, a config reload), that
	// new value is what gets restored afterwards.
	static void Hold()
	{
		float e = BossDirectorPlugin.FightEnemyDamage.Value;
		if (Math.Abs(enemy.Value - e) > 0.0001f)
		{
			if (lastSetEnemy >= 0f && Math.Abs(enemy.Value - lastSetEnemy) > 0.0001f) enemyNormal = enemy.Value;
			enemy.Value = e;
		}
		lastSetEnemy = e;
		if (boss == null) return;
		float b = BossValue();
		if (Math.Abs(boss.Value - b) > 0.0001f)
		{
			if (lastSetBoss >= 0f && Math.Abs(boss.Value - lastSetBoss) > 0.0001f) bossNormal = boss.Value;
			boss.Value = b;
		}
		lastSetBoss = b;
	}

	static float lastSetEnemy = -1f, lastSetBoss = -1f;

	// The boss number for the fight in progress: harder while a heroic fight is engaged.
	static float BossValue() => BossDirectorPlugin.FightBossDamage.Value * (heroic && BossDirectorPlugin.HeroicEnabled.Value ? BossDirectorPlugin.HeroicBossDamage.Value : 1f);

	internal static void Leave(string why)
	{
		if (!Active) return;
		Active = false;
		heroic = false;
		lastSetEnemy = lastSetBoss = -1f;
		try
		{
			enemy.Value = enemyNormal;
			if (boss != null) boss.Value = bossNormal;
		}
		finally
		{
			file.SaveOnConfigSet = savedSaveFlag;
		}
		BossDirectorPlugin.Log.LogInfo($"boss fight mode OFF ({why}): enemy damage to players back to x{enemyNormal:0.##}" +
			(boss != null ? $", boss damage back to {(bossNormal > 0f ? "x" + bossNormal.ToString("0.##") : "following enemy damage")}" : ""));
	}
}
