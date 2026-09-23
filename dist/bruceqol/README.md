# BruceQoL

Quality-of-life tuning for a private server, in one server-synced config. Every option defaults to
vanilla unless listed below. Required on the server and every client (mismatched clients are refused).
Built for Valheim 1.0.7.

Config file: `BepInEx/config/bruceirons.BruceQoL.cfg`. Locked to the server by default; admins can
change values live through the config file or Configuration Manager.

## 1 - General
- **Ignore cheated world keys** (On): keys set from the server launch line (e.g. `movestaminarate 85`)
  no longer flag the world as cheated, so achievements stay on.

## 2 - Structures
- **Creature damage to buildings** (0.5) and **Boss damage to buildings** (0.75): a troll still
  wrecks a wall, it just takes twice as long.
- **Player damage to other players buildings** (1): the builder always deals full damage to their own pieces.
- **Environmental hit damage** (1) and **Natural wear** (1). **Weather damage** (On): Off means
  unsheltered pieces never take rain wear, and every client also skips the roof raycasts during rain.
- **Wear check throttle** (On, 10 s): pieces you own that are intact, dry and settled run their wear and
  support check once per interval instead of every pass. Big CPU saving on large bases; a piece that
  loses its foundation still collapses within one interval.
- **<Material> health** (Stone 1.5, others 1) and **<Material> damage taken** (1) per material.

## 3 - Player
- Multipliers (all 1) for dodge, run, jump, sneak, swim and encumbered stamina, the stamina regen
  delay, and the auto pickup range.
- **Area repair** (On, 15 m): repairing one piece repairs every damaged piece around it.
- **Re-equip after swimming** (On): weapons sheathed by swimming come back out when you leave the water.

## 4 - Items
- **Stack size multiplier** (1).

## 5 - Fires
- **Infinite torches** (On), **Infinite fires** and **Infinite braziers** (Off). Infinite fires also skip
  the 2-second fuel bookkeeping that otherwise sends a network update per fire.

## 6 - Skills
- **Peak catch-up** (On, x2 XP): skills below their previous best level faster after a death.
- **Weapon cross-training** (On, x2 XP): a weapon skill lower than your best weapon skill levels faster.
  **Weapon cross-training skills** lists which count: melee only by default; add `Bows, Crossbows` for ranged.
- **Swim experience** (x2) and **Swim keeps level on death** (On).
- **Sneak boosts backstab** (On): up to +50% backstab at Sneak 100, and backstabs grant Sneak XP.

## 7 - Hauling (new skill)
Walk while carrying 90%+ of your limit and Hauling trains. **Extra carry weight at level 100** is a
flat amount, default 300 (so 300 base becomes 600 at max, 450 at level 50). Skill gain rate and
death loss (`Skill gain factor`, `Skill loss`) are in the same section.

Carts: **Cart load weight at level 100 (x)** scales how heavy a cart's cargo feels to the puller at max
skill (1 = vanilla, 0.5 = half), **Cart break force at level 100 (x)** scales how hard the cart is to
snap off (1 = vanilla), and **Cart pulling trains Hauling** ticks while you move a cart carrying at
least **Cart training load** at **Cart training experience** times a walking tick.

## 8 - Skill experience
- **All skills gain %** and one **<Skill> gain %** per vanilla skill (0 = vanilla, 50 = +50%, -50 = half).
- **Death penalty %** on top of the world's skill reduction rate (-100 = no loss).

## 9 - Multiplayer scaling
Vanilla hardcodes +30% enemy health and +4% enemy damage per extra nearby player (100 m, max 5).
All four numbers are configurable here; the toggle Off restores vanilla.

## 10 - Containers
- **Container sizes**: `prefab:WxH` list, e.g. `piece_chest_wood:6x3, piece_chest:8x4`. Empty = vanilla.
  Decide before the world starts; shrinking later strands items in the removed slots.
- **Chest contents on hover** (On, 12 entries) and **Fermenter progress on hover** (On).

## 11 - Crafting stations
- **Station range** (30 m, vanilla 20): how far from a station you can build and repair pieces that
  need it, and which station a piece belongs to. Not the monster-free area. Extensions still add range on top.
- **Stations need roof** (Off): stations work uncovered. On = vanilla.

## 12 - Beehives
- **Beehive tweaks** (Off). When On: **Honey per hive** (4) and **Seconds per honey** (1200).

## 13 - Combat
- **Enemy damage to players** (1), **Enemy damage to tames** (1), **Player damage to enemies** (1):
  multipliers applied where vanilla applies the Combat slider, so they stack with it. Edit live.
- **Enemy health** (1) and **Boss health** (1): max-health multipliers applied at spawn.
- **Parry difficulty compensation** (0) and **Held block difficulty compensation** (0): 0 = vanilla,
  1 = blocks are judged as on Combat Normal (damage that gets through is still scaled). Hard makes the
  same swing fill the stagger bar 2.25x faster; 1 undoes that for the block check only. Suggested on a
  Hard server: parry 1, held 0.5.
- **Bow draw tuning** (Off): when On, **Bow draw time at skill 0** (1) and **Bow draw time at skill 100**
  (0.2) set both ends of the skill curve as multipliers on each bow's draw time. Vanilla is 1 and 0.2, so
  a 2.5 s bow draws in 0.5 s at Bows 100. Try 0.8 and 0.4 for quicker early bows and less overpowering
  late ones. Levels between follow a straight line.
- **Blocking skill levers.** Vanilla gives the Blocking skill exactly one payoff, +50% block armour at
  100, and levels it 2 per parry and 1 per held block whether or not the block holds. These four make the
  skill worth the grind. Every value is a multiplier or an amount, never a percent, and every default is
  vanilla. "Scaled with level" always means a straight line from nothing at level 0 to the full value at 100.
  - **Block power by skill** (Off) is the master switch for **Block power at skill 100** (2) and **Block power
    bonus from level** (0). The multiplier is what a shield's block armour reaches at Blocking 100: 1.5 is
    vanilla, 2 doubles the shield at 100 (1.5x at 50), 3 triples it. The level delays the bonus: 0 = from the
    start, 25 = vanilla until Blocking 25. The shield tooltip shows the real number.
  - **Extra Blocking XP per parry** (0) and **per held block** (0) are added to vanilla's 2 and 1. Parry 2 =
    parries level Blocking twice as fast; held 1 = held blocks twice as fast. Keep held below parry.
  - **Block stamina refund at skill 100** (0) is the fraction of a held block's real stamina cost handed back
    right after the block: 0.5 = half back at 100, a quarter at 50. **Block stamina refund from level** (0)
    delays it. **Block stamina refund on parries** (Off) extends it to timed blocks, which vanilla already
    charges a flat amount for.
  - **Equip time at skill 100** (1) multiplies how long a weapon or shield takes to swap when its own skill
    (Swords, Blocking, Bows...) is at 100: 0.5 = half the time, 0.25 = a quarter. **Equip speed bonus from
    level** (0) delays it. Armour, torches and tools have no skill and are untouched.

  A reasonable first setup on a Hard server: block power On, 2 from level 25; parry XP 2, held 0; refund
  0.5 from level 40, parries Off; equip 0.5 from level 45. Each is live: change it in F1 and block something.
These replace world-key edits like `enemydamage`, which the game treats as cheating.

## 14 - Raids
- **Raids** (On). Off = no random raids.
- **Raid interval** (1): multiplier on the 46-minute roll timer. **Raid chance** (1): multiplier on the
  20% roll. **Raid duration** (1). All stack with the Raids world slider.
- **Raids anywhere** (Off): On lets raids start away from bases.
- **Disabled raids**: comma-separated names, e.g. `wolves, army_goblin`. The full list is logged at startup.

## 15 - Tames
- **Commandable tames** (`Boar`): comma-separated creature prefabs whose tamed animals follow or stay
  when you interact with them, like wolves and lox. Walk boars to a new pen. Tames never use portals.
  Empty = vanilla.
- **Passive taming and breeding** (Off; use the server-only ServerBasedRanch mod instead, which does this
  continuously on the server): when On, pens keep working while nobody is near. Drop a stack of food in
  the pen and leave; when the pen loads again the unloaded time is replayed by the vanilla rules: the
  animals eat when hungry, tame while fed, gain love points, conceive and give birth, and newborns grow
  up on the world clock. **Passive feed radius** (8 m) is how far from each animal food is taken from,
  **Passive catch-up limit** (12 h) caps one replay. Only tamed or once-fed animals are tracked.
- **Max animals per pen** (4 = vanilla): breeding stops at this many adults plus young within 10 m.

## 16 - Gathering
- **Skill based yield** (On): ore deposits, rocks, trees and logs drop extra items scaled by the
  Pickaxes or Wood cutting skill of whoever lands the finishing hit.
- **Extra ore and stone at level 100** (1) and **Extra wood at level 100** (1): extra drops as a
  fraction of the vanilla drop. 1 = double at level 100; at level 50 every item has a 50% chance of a
  second copy. Stacks with the Resources world slider. 0 = off.
The level travels with the hit, so it works whoever is simulating the rock or tree (the server, or
another player). Vanilla puts the Axes level in a tree hit; this mod corrects it to Wood cutting.
Pieces of an ore deposit that collapse because your hit took away their support count as yours too
(1.18.1; applies when the player simulating that area runs 1.18.1 or newer).

## 17 - Terrain
- **Dig and raise limit (metres)** (12, vanilla 8): how far a pickaxe or hoe may lower or raise the
  ground from its original height. Same limit both ways. Existing pits and mounds keep their shape
  until edited again. Every client must use the same value, which the sync guarantees: the terrain is
  re-clamped on each client when it is rebuilt, so a stray client on a lower limit would see a deep pit
  as a shallow one.

## 18 - Hoe
- **Radius multiplier** (1.5, vanilla 1 = a 2 m square): the area every hoe and cultivator action
  covers, so level ground, raise, path, paved road, cultivate and replant all sweep 3 m. The pickaxe is
  untouched.
- **Raise step multiplier** (1): how much ground one raise-ground click adds.
- **Raise ground costs stone** (On): Off makes raising free.
Levelling to the point you aim at instead of your own feet is vanilla: hold the alt-place key (Left Alt)
while levelling.

## 19 - Stars
- **Star chance by progress** (On): creatures in biomes the group has out-levelled spawn with stars
  more often. Vanilla rolls 10% per star; this multiplies that by
  `1 + Per boss over biome × (bosses defeated − biome tier)`, never below 1. Tiers: Meadows 0, Black
  Forest 1, Swamp 2, Mountain 3, Plains 4, Mistlands 5, Ashlands 6, Deep North 7. The biome you are
  fighting through stays vanilla; the ones behind you stop being safe.
- **Per boss over biome** (0.2): Meadows with two bosses down = ×1.4, so 14% one-star and 2% two-star;
  four down = ×1.8. **Max multiplier** (2): at most 20% / 4%. Vanilla's two-star cap is untouched.
- **Boss keys**: the world keys counted, in case a boss is added. Each listed key that is set counts once,
  so listing one twice makes that boss count double.
- **Star chance scope** (BiomeProgress): what a "step" is. BiomeProgress = bosses defeated minus the
  biome's tier, as above. **Global** = bosses defeated, everywhere, the biome you are fighting through
  included; "per boss over biome" then simply means per boss.
- **Separate two-star chance** (Off). Vanilla rolls the same chance once per star, so two-stars are always
  the one-star chance squared: 10% gives 1%, 20% gives 4%, and no single number makes two-stars common
  without making one-stars ubiquitous. On = the second star is rolled on its own and the one-star chance
  is left exactly as it is: two-star chance = **Two-star chance base** (1%) + **Two-star chance per step**
  (1%) × steps, up to **Two-star chance max** (10%). These are shares of all spawns, not of starred ones,
  and can never exceed the one-star chance at that spot. Only world spawns, raids, spawner piles and fixed
  spawners are affected; breeding, the spawn command and saved creatures are not.

  A global two-star chance that follows the bosses: scope Global, separate On, base 1, per step 1.
  That is 1% two-stars on day one, 4% after three bosses, 8% after all seven, with one-stars at 10%,
  16% and 20% (the cap). Measured over 3000 real spawns at five bosses down: 19.5% starred, 6.1%
  two-star, against 20.8% and 4.4% with the separate chance Off.

## 20 - Exploration
- **Map reveal radius** (1.5): multiplier on how far around you the map is uncovered on foot. 1 = vanilla;
  1.5 is a little over twice the area per step; 2 is four times the area.
- **Map reveal radius aboard a ship** (2): the same, used instead of the one above while you are on a
  ship's deck, so a coastline is charted from twice as far out. Rafts, karves, longships and modded hulls
  all count.
- Each player's own map; nothing is sent to anyone. The log prints the game's own radius in metres the
  first time the map updates. The idea is Smoothbrain's Exploration, without the skill.

## 21 - Refinement forge
- **Refinement odds** (On): apply the two chances below to every idol used at the Forge of Potential.
  Off = vanilla.
- **Success chance** (0.65): share of attempts that raise the item one level. Vanilla 0.65.
- **Break chance** (0.2): share of ALL attempts that destroy the item. Vanilla 1, which is why the game's
  third outcome never shows: whatever is left after success and break drops the item one level instead
  (0.65 + 0.2 leaves 15%). A break refunds materials the vanilla way (35% of the base cost plus the level
  you were at). 1 - success (0.35 by default) = no drops, vanilla odds.
- Level 4 to 7 needs three wins. Vanilla: 27.5% of items make it, 7.5 idols and 2.6 items lost on average.
  Default here: 41%, 7 idols, 1.4 items. Idols are the currency that hardly moves; the knob decides how
  often you rebuild the item. The roll happens on the crafting player's own game, so the odds are synced
  to every client.

Skill ideas follow Smoothbrain's SmartSkills and PackHorse; the container, station, beehive and
swim features follow OdinsQOL; skill-scaled gathering follows Smoothbrain's Mining and Lumberjacking.
All are independent implementations.

## License

MIT. Bundles ServerSync and SkillManager (MIT-0, blaxxun-boop). Written for the bruceirons LAN server.
