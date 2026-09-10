# Changelog

## 1.7.1 - 2026-09-10
- The skill's own `Skill gain factor` and `Skill loss` entries now live in `[7 - Hauling]` instead of
  a `[skill_2143584628]` section (SkillManager's name lookup has no localisation on a dedicated
  server). The unused `Skill effect factor` entry is gone. Delete the old section from existing
  config files; it is orphaned.

## 1.7.0 - 2026-09-10
- Hauling and carts: **Cart load weight at level 100 (x)** (1 = off; 0.5 = a full cart pulls like a
  half-full one at max skill), **Cart break force at level 100 (x)** (1 = off), and **Cart pulling
  trains Hauling** (On; needs **Cart training load** cargo, gives **Cart training experience** x2 a
  walking tick, same Training interval).

## 1.6.0 - 2026-09-10
- Units in key names for every absolute setting: `Wear check interval (seconds)`, `Training interval
  (seconds)`, `Area repair radius (metres)`, `Scale range (metres)`, `Station range (metres)`,
  `Honey time (seconds)`, `Extra carry weight at level 100 (weight units)`, and the raid multipliers
  now say what they multiply (`Raid interval (x vanilla 46 min)`, `Raid chance (x vanilla 20%)`).
  Old key names are orphaned in existing config files; re-enter any non-default values.

## 1.5.2 - 2026-09-10
- Hauling: **Extra carry weight at level 100** is now a flat amount (default 300, i.e. 300 -> 600 at
  max) instead of a fraction, and no longer multiplied by SkillManager's effect factor. One knob.

## 1.5.1 - 2026-09-10
- Weapon cross-training now has a **Weapon cross-training skills** list. Default is melee only
  (Swords, Knives, Clubs, Polearms, Spears, Axes, Unarmed); add Bows, Crossbows to include ranged.

## 1.5.0 - 2026-09-10
- New **14 - Raids**: Raids on/off, Raid interval, Raid chance, Raid duration multipliers, Raids
  anywhere, Disabled raids list. Applied to the raid system directly, no world keys. Edit live.

## 1.4.0 - 2026-09-10
- New **13 - Combat**: Enemy damage to players, Enemy damage to tames, Player damage to enemies,
  Enemy health, Boss health. Harmony multipliers in the same code path as vanilla's Combat slider,
  so they stack with the world setting, edit live, and never touch world keys (no cheat flag).

## 1.3.3 - 2026-09-10
- **Station range** is now a plain radius in metres, default 30 (vanilla 20), instead of "0 = vanilla".

## 1.3.2 - 2026-09-10
- **Stations need roof** now defaults to Off (stations work uncovered). README rewritten to cover every section.

## 1.3.1 - 2026-09-10
- Fix: 1.3.0 failed to patch (CraftingStation has no Awake in 1.0.7; the patch now targets Start), which
  aborted the plugin's own startup. Do not use 1.3.0.

## 1.3.0 - 2026-09-10 (broken, see 1.3.1)
- **10 - Containers**: per-prefab chest/cart/ship sizes (`Container sizes`, empty = vanilla; pick before the
  world starts), chest contents on hover, fermenter minutes-left on hover.
- **11 - Crafting stations**: station range override, stations work without a roof (toggle).
- **12 - Beehives**: honey per hive and seconds per honey (off by default = vanilla).
- **3 - Player**: re-equip after swimming (vanilla only re-shows swim-sheathed weapons on the toggle key).
- Ideas from OdinsQOL (AGPL), re-implemented; no code copied.

## 1.2.1 - 2026-09-10
- New section **9 - Multiplayer scaling**: vanilla's nearby-player difficulty scaling is hardcoded
  (+30% effective enemy health and +4% enemy damage per extra player within 100 m, capped at 5). Now
  configurable and server-synced: health per extra player, damage per extra player, range, max players.

## 1.2.0 - 2026-09-09
- New section **8 - Skill experience**, OdinsQOL-style percentage modifiers (own implementation): a
  master toggle, **All skills gain %**, one **<Skill> gain %** per vanilla skill (Swords ... Ride), and
  **Death penalty %** applied on top of the world's skillreductionrate. 0 = vanilla, 50 = +50%, -50 = half,
  -100 on the death penalty = no loss.

## 1.1.3 - 2026-09-09
- New: **Wear check throttle** (2 - Structures, on, 10 s). Pieces you own that are older than 30 s, at full health, dry, and outside the Ashlands / Deep North run their wear tick (including the support physics check) at most once per interval. Throttled, not skipped: a piece that loses its foundation still collapses within one interval. Big CPU saving on large bases for the player who owns the zone.

## 1.1.2 - 2026-09-09
- Infinite fires no longer run vanilla's 2-second fuel bookkeeping, which wrote a timestamp into the object every tick and sent a network update per fire every 2 s. Only the visual state is refreshed. A fire that stops being infinite burns its stored fuel down on the next tick.
- Weather damage = Off now also skips the per-piece roof raycast during rain on every client (kept in the Ashlands and Deep North for ash and snow).

## 1.1.1 - 2026-09-09
- Fix: on 1.0 clients the Hauling skill constructor threw (Steam not initialised during plugin Awake) and aborted the rest of config binding, so the client reported every Player/Items/Fires/Skills entry as unknown. The skill is now created after all entries and retried at the main menu.
- New: **Ignore cheated world keys** (1 - General, on). Launch-line keys such as `movestaminarate 85` no longer flag the world as cheated; achievements stay enabled without relying on Unshamed.

## 1.1.0 — 2026-09-09
- New **Hauling** skill: walking while carrying 90%+ of your limit trains it; each level raises carry
  weight (up to +100% at 100 by default). Replaces the PackHorse mod in our pack.
- Structures: **Weather damage** on/off toggle, and per-material **damage taken** multipliers
  (wood vs stone resistance) alongside the per-material health multipliers. Replaces
  AzuWearNTearPatches in our pack.

## 1.0.0 — 2026-09-09
- First release, built for Valheim 1.0.7.
- Structures: creature/boss/player/environment damage multipliers, natural wear, per-material health.
- Player: stamina cost multipliers, regen delay, pickup range, area repair.
- Items: stack size multiplier. Fires: infinite torches/fires/braziers.
- Skills: peak catch-up, weapon cross-training, swim XP and no swim loss, sneak backstab.
