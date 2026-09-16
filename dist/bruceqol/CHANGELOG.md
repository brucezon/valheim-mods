# Changelog

## 1.15.1 - 2026-09-16
- Section 19 defaults lowered: **Per boss over biome** 0.5 → **0.2**, **Max multiplier** 4 → **2**.
  Meadows with two bosses down now rolls 14% one-star / 2% two-star (was 20% / 4%), and the ceiling
  anywhere is 20% / 4% (was 40% / 16%). A config file written by 1.15.0 keeps the old values; set the
  two lines by hand or delete them to take the new defaults.

## 1.15.0 - 2026-09-16
- New **19 - Stars**: **Star chance by progress** (On). Vanilla's 10%-per-star roll is multiplied by
  `1 + Per boss over biome (0.5) × (bosses defeated − biome tier)`, capped at **Max multiplier** (4), and
  never below 1, so the biome you are fighting through stays vanilla while the ones behind you get
  starrier: Meadows with two bosses down rolls 20% one-star and 4% two-star. Vanilla's star cap is
  untouched. Hooks the two vanilla chance getters (world spawns, spawners, raids, spawner piles). Boss
  progress comes from the world keys in **Boss keys**. Same idea as CLLC's Fluid world level, on
  vanilla's own roll.

## 1.14.0 - 2026-09-15
- New **18 - Hoe**: **Radius multiplier** (1.5: level ground, raise, path, paved road, cultivate and
  replant cover a 3 m square instead of 2 m; the pickaxe is untouched), **Raise step multiplier** (1),
  **Raise ground costs stone** (On). Applied to each placed terrain operation as it fires, so it is live
  and touches nothing but that one operation. Levelling to the aim point is vanilla's alt-place key.

## 1.13.0 - 2026-09-15
- New **17 - Terrain**: **Dig and raise limit (metres)** (12, vanilla 8). How far a pickaxe or hoe may
  lower or raise the ground from its original height. Vanilla hard-codes 8 at four places (the two
  terrain-compiler edit routines, the heightmap re-clamp when terrain is rebuilt, and the legacy
  heightmap path); a transpiler swaps the constant for the setting in all four, so a deeper pit both
  digs and displays. Server-synced; the re-clamp runs on every client, which is why the mod being
  required on clients matters for this one.

## 1.12.0 - 2026-09-13
- New in [13 - Combat]: **Bow draw tuning** (Off), **Bow draw time at skill 0 (x)** (1) and **Bow draw
  time at skill 100 (x)** (0.2). Both ends of vanilla's draw-time-versus-skill line as multipliers on
  each bow's draw time; vanilla's 0.2 at skill 100 is what makes high Bows fire five times faster than a
  novice. Off leaves the vanilla code untouched. Same idea as WackyMole's Tone Down the Twang, with the
  skill-0 end added; independent code.

## 1.11.1 - 2026-09-12
- **Passive taming and breeding** now defaults to Off. The server-only ServerBasedRanch mod does the same
  job continuously on the server and is the preferred way; turn this on only where that mod is absent.
  Existing config files keep their value.

## 1.11.0 - 2026-09-12
- **15 - Tames**: **Passive taming and breeding** (On). Pens keep working while nobody is near: when an
  animal loads again, its unloaded time is replayed with the vanilla rules in 10 s steps. It eats food
  lying in the pen when hungry (real items are removed), tames while fed, gains love points, conceives
  and gives birth; newborns get a backdated spawn time so they grow up on the world clock. Only tamed or
  once-fed animals are tracked; a wild herd costs nothing. **Passive feed radius** (8 m), **Passive
  catch-up limit** (12 h), **Max animals per pen** (4 = vanilla; applies loaded and in the replay).
  Each replay that did something is logged.

## 1.10.0 - 2026-09-12
- New **16 - Gathering**: **Skill based yield** (On), **Extra ore and stone at level 100** (1) and
  **Extra wood at level 100** (1). Ore deposits, rocks, trees and logs drop extra copies of what they
  roll, scaled by the finishing hit's Pickaxes or Wood cutting level: double at 100, a 50% chance of a
  second copy at 50. Works whoever simulates the object because the level rides on the hit; the hit's
  level is corrected to Wood cutting for axes on trees (vanilla sends the Axes level).

## 1.9.0 - 2026-09-12
- New **15 - Tames**: **Commandable tames** (default `Boar`). Tamed animals of the listed prefabs can be
  told to follow or stay by interacting with them, the same way wolves and lox already can. Lets you
  walk boars to a new pen instead of shoving them onto a boat. Tames still cannot use portals.

## 1.8.0 - 2026-09-11
- New in [13 - Combat]: **Parry difficulty compensation** and **Held block difficulty compensation** (0-1,
  default 0 = vanilla). The block is judged against enemy damage divided by R^c (R = Combat enemy damage
  rate, 1.5 on Hard) and whatever leaks is multiplied back, so at 1 the parry window, stagger fill and
  stamina cost equal Combat Normal while damage taken stays Hard. Reason: 1.0's armor-style block puts
  the square of the damage into the stagger bar, so Combat Hard cut the largest parryable hit by 1.5x.

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
