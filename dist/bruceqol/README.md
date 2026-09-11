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
These replace world-key edits like `enemydamage`, which the game treats as cheating.

## 14 - Raids
- **Raids** (On). Off = no random raids.
- **Raid interval** (1): multiplier on the 46-minute roll timer. **Raid chance** (1): multiplier on the
  20% roll. **Raid duration** (1). All stack with the Raids world slider.
- **Raids anywhere** (Off): On lets raids start away from bases.
- **Disabled raids**: comma-separated names, e.g. `wolves, army_goblin`. The full list is logged at startup.

Skill ideas follow Smoothbrain's SmartSkills and PackHorse; the container, station, beehive and
swim features follow OdinsQOL. All are independent implementations.

## License

MIT. Bundles ServerSync and SkillManager (MIT-0, blaxxun-boop). Written for the bruceirons LAN server.
