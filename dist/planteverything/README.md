# PlantEverything_TEMP — temporary Valheim 1.0 build of PlantEverything

**This is not our mod.** PlantEverything is written by **Advize** and licensed under the
**GNU GPLv3**. This package is Advize's PlantEverything **1.20.0** recompiled against Valheim 1.0.7,
published only so a small private server can keep using it until Advize ships an official 1.0
update. When that happens, use the official package and remove this one.

Upstream: https://github.com/AdvizeGH/Advize_ValheimMods (commit `f50a18f0`)
Official package: https://thunderstore.io/c/valheim/p/Advize/PlantEverything/

## What changed

**One cosmetic source change (1.20.1 package):** every config description in
`Configuration/ModConfig.cs` is prefixed with its unit, `[MINUTES]`, `[SECONDS]` or `[ITEM COUNT]`,
so the .cfg file and Configuration Manager tooltips say what a number means. Key names, defaults and
behaviour are untouched. Otherwise **no source code changes.** The official 1.20.0 dll ships a pre-1.0 ServerSync that reads a field
Valheim 1.0 turned into a constant, so it fails on load. This build compiles ServerSync from its
current source instead, against the 1.0.7 game assemblies. Behaviour, config and BepInEx GUID
(`advize.PlantEverything`) are identical to upstream.

## Version stamping

The plugin reports **1.20.0.99**: above 1.20.0, below any future 1.20.1 from Advize, so the official
1.0 update takes over automatically when added to a profile.

## Source (GPLv3 obligation)

The complete source used for this build is under `source/` in this package, with the build project
(`Advize_PlantEverything.TEMP.csproj`) and the upstream commit hash. License: GPLv3, see `LICENSE`.
All credit to Advize.
