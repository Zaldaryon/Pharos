# Mod Loading

Staging a mod proves the game started, not that it loaded the mod the way the mod expects. A
scenario can ask what the loader did: which mods it enabled or refused, which systems ran and in
what order, what every JSON patch did, and which compatibility assets replaced another mod's.

```csharp
using Zaldaryon.Pharos.Mods;

ModLoadReport mods = Server.Mods;
Assert.Equal(ModStatus.Enabled, mods.Loaded["mymod"].Status);
Assert.Contains(mods.Systems, system => system.Key == "mymod:MySystem" && system.Ran);
PharosAssert.AllPatchesApplied(Server, "mymod");
PharosAssert.LoadsBefore(Server, "mymod:MySystem", "game:RecipeLoader", ModRunPhase.AssetsFinalize);
PharosAssert.NoModErrors(Server);
```

`Server.Mods` is captured on the game thread at the end of boot; `Client.Mods` is captured on the
client's thread after it joins. Both are snapshots: they do not change while a test ticks the game.

## Mods and their state

`Loaded` maps a mod ID to a `LoadedMod`: `Name`, `Version`, `NetworkVersion`, `Side`, `Type`,
`Status`, `Error`, `Dependencies`, `MissingDependencies` and `LoadOrder`. It includes disabled and
errored mods, not only the enabled ones. `MissingDependencies` lists the loader's unresolved
dependency IDs, such as `pharosmissingdependency@1.0.0`.

```csharp
Assert.All(Server.Mods.Loaded.Values.Where(mod => mod.Status == ModStatus.Enabled), mod =>
    Assert.Null(mod.Error));
Assert.Empty(Server.Mods.MissingDependencies);
```

## Systems, sides and order

`Systems` has one `LoadedSystem` per system the loader instantiated, ordered by its first
`Normal` run. `Order` is that run's position, or -1 when it never ran. `ExecuteOrder` is the
system's own value. `LoadedOn` says which sides its `ShouldLoad(EnumAppSide)` accepts; `Ran` says
whether it actually ran on this host. A client-only system on a server is present with `Ran`
false.

`RunLog` is the exact order of `TryRunModPhase` calls: `Order`, `ModId`, `Type`, `Phase`,
`ElapsedMs` and `Succeeded`. `Phases` gives the wall time of each run phase, from `Pre` through
`Normal`.

```csharp
using Zaldaryon.Pharos.Mods;
LoadedSystem late = Assert.Single(Server.Mods.Systems, system => system.Key == "mymod:LateSystem");
Assert.True(late.Ran);
Assert.True(late.PhaseMs.ContainsKey(ModRunPhase.Start));
```

A selector for the assertions is `mod:TypeName`, a full type name, or a bare type name. A duplicate
type name needs the `mod:` prefix.

## JSON patches

`Patches` holds one result per patch target plus one for a patch the loader skipped before applying
it. Each `PatchResult` gives the source asset and mod, the patch index in its file, `Op`, `Target`,
`Path`, `Side`, `Enabled`, `Outcome` and a `Detail` for skips and errors.

| Outcome | Meaning |
| --- | --- |
| `Applied` | The loader changed that target. |
| `NotFound` | The target asset does not exist. |
| `ConditionUnmet` | The patch's `Condition` on the world config is not met. |
| `DependencyMissing` | The patch's `DependsOn` requirement is not met. |
| `Error` | The loader reported an error, or recorded no result for an eligible patch. |
| `Disabled` | The patch is disabled, or is for another side. |

`PatchesOf(modId)` filters by the patch asset's domain. `AllPatchesApplied` fails on `NotFound` and
`Error`; a disabled, side-mismatched, condition-unmet or dependency-missing patch is skipped on
purpose and passes.

```csharp
using Zaldaryon.Pharos.Mods;
IReadOnlyList<PatchResult> mine = Server.Mods.PatchesOf("mymod");
Assert.Contains(mine, patch => patch.Outcome == PatchOutcome.NotFound && patch.Target!.Contains("old/path"));
```

A wildcard patch produces one result per matched target. A patch file that does not parse as a JSON
array produces one `Error` with index -1. If the loader cannot read the patch assets at all, Pharos
reports one `Error` with source `(patches)` and no target.

## Compatibility assets

`CompatibilityAssets` lists each asset a mod's `compatibility/<targetmod>/` folder copied into the
target mod's domain: `Path` (`targetmod:path`), `TargetMod`, `ProviderMod` and `Replaced`.
`Replaced` is true when the target asset existed before the copy; false when the copy added it.

The count is taken just before the game remaps the folder. Two providers that copy the same missing
target both count as added, as they do in the game's own counter. Assets for the other side are
left out by the game and are not listed here.

## Asset origin

`Server.Assets.Origin(path)` and `Client.Assets.Origin(path)` return an `AssetSource`: the requested
`Path`, its target `Domain`, the `ModId` whose folder supplied the bytes, `OriginPath`, `Patched`
and `IsCompatibility`. `TryOrigin` returns null for a missing asset; `Origin` throws.

```csharp
using Zaldaryon.Pharos.Mods;
AssetSource source = Server.Assets.Origin("game:blocktypes/stone/rock.json");
Assert.True(source.Patched);
Assert.Equal("mymod", source.ModId);
```

The provider is the mod whose folder holds the bytes. For an asset changed by a JSON patch, `ModId`
names the patching mod instead of the mod that supplied the pre-patch file; `Patched` is the game's
own `IAsset.IsPatched` flag. A compatibility copy still reports the providing mod while `Domain`
stays the target mod.

## Measurement

`ModLoadReport` implements `IMetricSource`, so `Metrics.Publish(Server.Mods)` records:

| Metric | Unit |
| --- | --- |
| `mods.loaded`, `mods.enabled`, `mods.disabled`, `mods.errored` | count |
| `mods.missingdependencies`, `mods.systems`, `mods.runlog` | count |
| `mods.patches.applied`, `.notfound`, `.errors`, `.conditionunmet`, `.dependencymissing`, `.disabled` | count |
| `mods.patches.<mod>.applied`, `.notfound`, `.errors` | count |
| `mods.compatibility.added`, `.replaced` | count |
| `mods.phase.<phase>.ms` | wall-clock milliseconds |

Phase times are wall-clock time and are not comparable across machines without repeated samples.
The counts are exact.

## What is observed, and what is not

- **Hooks.** The observer patches `ModLoader.LoadModInfos` and `LoadMods`, `TryRunModPhase` and
  `RunModPhase`, then the game's `ModJsonPatchLoader.ProcessSinglePatch` (the path `ApplyPatches`
  uses) and its public `ApplyPatch`, and `ModCompatiblityUtil.AssetsLoaded`. The patch loader and
  compatibility class live in `VSEssentials`; when a game build does not have them, `Notes` says so
  and the rest of the report is still available.
- **Skipped patches.** The loader filters disabled, side-mismatched, condition-unmet and
  dependency-missing patches before it applies anything. Pharos re-reads each `patches/*.json` after
  boot to classify those. A patch that specifies no side is treated as universal; the engine also
  derives a side from the target asset's category, which Pharos does not reproduce.
- **Systems.** `LoadedOn` comes from `ShouldLoad(EnumAppSide)`. A system that overrides only
  `ShouldLoad(ICoreAPI)` may report both sides even when the API's configuration makes it load on
  one. `Ran` is always what the loader actually did on this host.
- **Mods.** A mod that fails before instantiation has no systems; it still appears in `Loaded` with
  its `Status` and `Error`.
- **Removal.** `ModCompatibilitySimulator` and its fake snapshot API are gone in 0.8. Stage the real
  mods with `[ServerMods]` or `[PharosMods]` and inspect them with this profile instead.
