# PlantEasily_TEMP — temporary Valheim 1.0 build of PlantEasily

**This is not our mod.** PlantEasily is written by **Advize** and is licensed under the
**GNU GPLv3**. This package is a modified build of PlantEasily **2.1.1** that compiles against
Valheim 1.0.7, published only so a small private server can keep using it until Advize ships an
official 1.0 update. When that happens, use the official package instead and remove this one.

Upstream: https://github.com/AdvizeGH/Advize_ValheimMods (commit `f50a18f0`)
Official package: https://thunderstore.io/c/valheim/p/Advize/PlantEasily/

## What changed (2 lines)

1. `Patches/KeyHintPatches.cs` — the `ZInput.AddButton` Harmony patch now names the exact
   overload (1.0 added a second overload, so the unqualified patch failed to resolve).
2. `Core/PlacementController.cs` — `Piece.SetCreator` now takes a `PlatformUserID` on 1.0;
   the call passes the local user's ID from Splatform.

Nothing else was touched. Config, behaviour and BepInEx GUID (`advize.PlantEasily`) are
identical to upstream.

## Version stamping

The plugin reports version **2.1.1.99**: above 2.1.1 (so this build is what BepInEx loads when
both are present) but below any future **2.1.2** from Advize. When the official 1.0 update is
added to a profile, BepInEx logs `Skipping [PlantEasily 2.1.1.99] because a newer version exists`
and this package becomes inert.

## Source (GPLv3 obligation)

The complete modified source is inside this package under `source/`, together with
`CHANGES-vs-upstream-2.1.0.diff` and the upstream commit hash. Build with
`Advize_PlantEasily.TEMP.csproj` against a Valheim 1.0.7 install.

License: GPLv3 — see `LICENSE`. All credit to Advize.
