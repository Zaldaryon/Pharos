# Content Sweep

The server-side content sweep exercises a mod's blocks, items and entity types using a repeatable
script. Each code runs through the steps that apply to its type, and the report keeps failures
separate by code and step.

```csharp
using Zaldaryon.Pharos.Content;

[ServerWorld(Preset = WorldPreset.SurvivalFlat)]
[ServerMods("mods/MyMod")]
public class MyModContentTests : ServerScenarioBase
{
    [ServerTheory]
    [ContentSweep("mymod", Steps = SweepSteps.All, SkipCodes = ["mymod:debugblock-*"])]
    public async Task ContentRunsThroughTheServer(ContentCode code)
    {
        SweepReport report = await ContentSweep.RunAsync(Server!, code);
        Assert.Empty(report.Failures);
        Assert.Empty(report.LeakedTickListeners);
    }
}
```

`[ContentSweep]` supplies one theory row per code found in the staged mod's `blocktypes`, `itemtypes`
and `entities` JSON assets. It is a data attribute, so pair it with `[ServerTheory]`. The attribute
property is named `SkipCodes` because xUnit reserves `Skip` for a test's skip reason. The attribute
expands `variantgroups`, and `Variants` can select named values such as `metal=iron`. `*` and `?`
match variant names and values.

The code row carries `Kind`, the fully-qualified `Code`, `Domain`, `Path`, `Variants` and `Steps`.
`Steps` is the set of steps this row will run. A row for a mod that registers content only from code
cannot be derived from JSON assets. Use the domain overload for that content:

```csharp
using Zaldaryon.Pharos.Content;

SweepReport report = await ContentSweep.RunAsync(Server!, "mymod");
Assert.Empty(report.Failures);
Assert.Empty(report.LeakedTickListeners);
Assert.Equal(
    report.Blocks.Count,
    report.Passed(SweepStep.BlockEntityRoundTrip).Count + report.WithoutBlockEntity.Count);
```

## Steps

| Content kind | Steps |
| --- | --- |
| Block | `BlockPlace`, `BlockTick`, `BlockNeighbourChange`, `BlockEntityRoundTrip`, `BlockDrops`, `BlockBreak` |
| Item | `ItemGive`, `ItemHeldIdle`, `ItemGroundIdle`, `ItemCollect`, `ItemHeldItemInfo`, `ItemTransitions`, `ItemDurability` |
| Entity | `EntitySpawn`, `EntityTick`, `EntityDamage`, `EntityRoundTrip`, `EntityDie`, `EntityDespawn` |

`SweepSteps.All` runs all steps that apply to the code's kind. The default tick count is three; set
`ContentSweepOptions.Ticks` to use a different count for held-idle, world-tick and post-placement
checks.

Blocks are placed and broken through a joined server test player's packet handlers. The sweep finds
a loaded support block, places the test player in creative mode, and picks the highest-tier loaded
tool that meets `Block.RequiredMiningTier`. It calls the neighbour-change handler and round-trips a
tool that meets `Block.RequiredMiningTier`. It resolves every configured `BlockDropItemStack` before
calling `Block.GetDrops`, so a missing drop code is reported even when its random chance would not
produce a stack. It calls the neighbour-change handler and round-trips a block entity's tree
attributes when the placed block has one. A block with no block entity is listed in
`WithoutBlockEntity`, not treated as a failure.

Items are held in the test player's hotbar. The sweep calls held-idle, ground-idle, tooltip and
transition handlers. It drops an `EntityItem` near the player and advances server ticks to test
pickup. For durable items, it damages a clone to zero durability so the player's inventory remains
usable for the other steps.

Entity types are created through the server's class registry and spawned near the test player. The
sweep ticks them, calls `ReceiveDamage` and `Die`, round-trips their binary save format, and removes
them from the world.

## Report

`SweepReport` contains the `Blocks`, `Items` and `Entities` included in the run, the steps that
passed, `Failures`, `LeakedTickListeners` and `WithoutBlockEntity`. `Passed(step)` returns the
distinct codes that completed that step. A `SweepFailure` gives the `Code`, `Step`, `Message`, thrown
`Exception` if one occurred, and the server `LoggedErrors` recorded during the step. A logged error
fails the step even when the handler returns normally.

`ContentSweepOptions` controls the domain overload:

```csharp
using Zaldaryon.Pharos.Content;

var options = new ContentSweepOptions
{
    Steps = SweepSteps.BlockPlace | SweepSteps.BlockBreak,
    Skip = ["mymod:debug-*"],
    Variants = ["*=oak", "*=iron"],
    Ticks = 5,
};
SweepReport report = await ContentSweep.RunAsync(Server!, "mymod", options);
```

The asset-backed theory attribute knows the names from `variantgroups`. The live-domain overload
enumerates registered codes, which carry only a terminal variant suffix; use `*=value` selectors
there when a variant filter is needed.

The sweep creates and removes temporary world content. Run it in a server scenario with rollback
isolation to restore the world and the test player's state. `LeakedTickListeners` compares the
server's registered listener IDs before and after the code is swept; it does not include delayed
callbacks.

Some operations need setup. A block is installed directly when a later block step is selected but
`BlockPlace` is not; the test only reports `BlockPlace` when that step is selected. Held-item checks
give the item to the player as setup when `ItemGive` is not selected. Entity checks spawn the entity
as setup when `EntitySpawn` is not selected. Failures in setup appear under `Resolve`.

The asset-backed theory attribute discovers only codes declared in JSON. Code that generates content
at runtime must use the domain overload, which enumerates the server's live `World.Blocks`,
`World.Items` and `World.EntityTypes`. The attribute reads named variant groups from JSON. The live
world exposes only the code's terminal variant suffix, so use a wildcard key such as `*=oak` in the
domain overload. Boot-time errors are not attributed to a sweep step; inspect `Server.BootDiagnostics`
for those.
