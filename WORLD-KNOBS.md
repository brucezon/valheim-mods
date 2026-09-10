# World knobs (Valheim 1.0.7) — verified from decompiled source

Every knob below is a **global key** on the world. On *every* server start the game wipes the
modifier keys and re-applies the world's *starting keys* (`ZoneSystem.SetStartingGlobalKeys`), and
the dedicated-server launch line edits those starting keys — so all of this can be set at creation
**or changed later** by restarting with different arguments. The result is saved into the world.

## Launch-line syntax (dedicated server)

```
-preset <Normal|Casual|Easy|Hard|Hardcore|Immersive|Hammer>     # applies a whole bundle first
-modifier <Combat|DeathPenalty|Resources|Raids|Portals> <option>  # one slider at a time
-setkey "<key> <value>"                                          # any raw key; scalars are PERCENT (150 = 1.5x)
```
Order matters: put `-preset` before `-modifier`/`-setkey` (a preset clears the list).

Slider options (same as the in-game World Modifiers screen):
- Combat: `VeryEasy` `Easy` `Default` `Hard` `VeryHard`
- DeathPenalty: `Casual` `VeryEasy` `Easy` `Default` `Hard` `Hardcore`
- Resources: `MuchLess` `Less` `Default` `More` `MuchMore` `Most`
- Raids: `None` `MuchLess` `Less` `Default` `More` `MuchMore`
- Portals: `Casual` `Default` `Hard` `VeryHard`

## Scalar keys (`-setkey "name value"`, value in percent, default 100)

| Key | Affects |
|---|---|
| `PlayerDamage` / `EnemyDamage` | what the Combat slider sets |
| `ResourceRate` | all drops (what the Resources slider sets) |
| `EventRate` | raid frequency (Raids slider) |
| `SkillReductionRate` | skill loss on death (DeathPenalty slider). Base 100 = 5% of each skill per death. **Ignored while FortifySkillsRedux is installed** — its OnDeath finalizer overwrites skills to their fortified level after vanilla's loss runs |
| `SkillGainRate` | how fast skills level |
| `StaminaRate` | stamina cost of all actions |
| `MoveStaminaRate` | stamina cost of running/jumping/swimming only |
| `StaminaRegenRate` | all stamina regeneration |
| `EitrRate` | eitr costs |
| `FoodRate` | food duration/values |
| `DurabilityRate` | gear durability loss |
| `CarryWeightRate` | max carry weight |
| `AdrenalineRate` | adrenaline (new 1.0 system) |
| `EnemySpeedSize` | enemy speed/size scaling |
| `EnemyLevelUpRate` | star-creature chance |
| `WorldLevel` | world level (NG+-style scaling; leave alone) |

## Toggle keys (`-setkey "name"`, presence = on)

Death: `DeathKeepEquip` `DeathKeepInventory` `DeathDeleteItems` `DeathDeleteUnequipped` `DeathSkillsReset`
Building: `NoBuildCost` `NoCraftCost` `AllPiecesUnlocked` `AllRecipesUnlocked` `NoWorkbench` `DungeonBuild` `NoBuildingFall`
World: `PassiveMobs` `NoMap` `NoPortals` `NoBossPortals` `TeleportAll` `PlayerEvents` `Fire` `NoPseudoDrops`
`WorldLevelLockedTools` `NoHeavySnow` `AllHeavySnow` (the last two are new in 1.0 — Deep North snow)

## Achievements caveat (1.0)

Setting keys from the **in-game console** (`setkey …`) counts as cheating unless the value matches
a UI slider option — that flags the character and disables achievements (Unshamed does NOT cover it).
The launch-line route above sets the keys without going through that check.

## FINAL launch line (9 Sep 2026 night) — in `tools/start-lan-server.bat`

```
-preset Hard -modifier DeathPenalty Default -setkey "movestaminarate 85" -setkey "skillreductionrate 60" -setkey "playerevents"
```
- `playerevents` — "player-based raids": raids are picked from each player's own boss kills, not
  the world's. A newcomer next to a Yagluth-slayer gets greyling raids, not fulings. Boolean key, no value.
- `-preset Hard` — Combat Hard + Death Penalty Hard bundle (everything else default)
- `-modifier DeathPenalty Default` — pulls the death slider back to Default, keeps Combat Hard
- `movestaminarate 85` — cheaper sprint/jump, combat stamina cost unchanged
- `skillreductionrate 60` — 60% of the default skill loss per death (BruceQoL's peak catch-up then
  doubles XP until the skill is back)
- No `staminaregenrate` / `carryweightrate`: Endurance (food curve) and BruceQoL's Hauling skill
  (carry weight) replace them. Adding those keys again would stack on top of the mods.
Order matters: preset first, then modifiers, then keys. Re-applied on every server start, so
editing the .bat and restarting changes an existing world.
```
```
