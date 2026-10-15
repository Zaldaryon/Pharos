using Vintagestory.API.MathTools;
using Xunit;
using Zaldaryon.Pharos.Assertions;
using Zaldaryon.Pharos.Server;
using Zaldaryon.Pharos.XUnit;

namespace Zaldaryon.Pharos.Tests.Assertions;

/// <summary>The comparison of two world states, without a server: what differs, and how the failure reads.</summary>
public class WorldStateDiffTests
{
    private static readonly BlockPos Center = new(512, 4, 512);

    [Fact]
    public void Differences_NameTheChangedEntryWithItsValuesBeforeAndAfter()
    {
        WorldState before = WorldState.FromEntries(Center, 2, new Dictionary<string, string> { ["block 513,4,512"] = "game:air" });
        WorldState after = WorldState.FromEntries(Center, 2, new Dictionary<string, string> { ["block 513,4,512"] = "game:chest-east" });

        Assert.Equal(["block 513,4,512: game:air -> game:chest-east"], WorldState.Differences(before, after));
    }

    [Fact]
    public void Differences_MarkAnEntryThatIsOnlyOnOneSide()
    {
        WorldState before = WorldState.FromEntries(Center, 2, new Dictionary<string, string> { ["block entity 513,4,512/inventory/3"] = "5x game:ingot-copper" });
        WorldState after = WorldState.FromEntries(Center, 2, new Dictionary<string, string>());

        Assert.Equal(["block entity 513,4,512/inventory/3: 5x game:ingot-copper -> (missing)"], WorldState.Differences(before, after));
        Assert.Equal(["block entity 513,4,512/inventory/3: (missing) -> 5x game:ingot-copper"], WorldState.Differences(after, before));
    }

    [Fact]
    public void Differences_LeaveOutTheIgnoredPaths()
    {
        WorldState before = WorldState.FromEntries(Center, 2, new Dictionary<string, string> { ["savegame mod data mymod:counter"] = "01000000", ["block 1,1,1"] = "game:air" });
        WorldState after = WorldState.FromEntries(Center, 2, new Dictionary<string, string> { ["savegame mod data mymod:counter"] = "03000000", ["block 1,1,1"] = "game:air" });

        Assert.Empty(WorldState.Differences(before, after, "savegame mod data"));
        Assert.Single(WorldState.Differences(before, after));
    }

    [Fact]
    public void SameWorldState_PassesForEqualStates()
    {
        var entries = new Dictionary<string, string> { ["block 1,1,1"] = "game:air" };

        PharosAssert.SameWorldState(WorldState.FromEntries(Center, 2, entries), WorldState.FromEntries(Center, 2, entries));
    }

    [Fact]
    public void SameWorldState_NamesEachDifferenceAndItsPlace()
    {
        WorldState before = WorldState.FromEntries(Center, 2, new Dictionary<string, string> { ["block 513,4,512"] = "game:chest-east" });
        WorldState after = WorldState.FromEntries(Center, 2, new Dictionary<string, string> { ["block 513,4,512"] = "game:air" });

        PharosAssertException error = Assert.Throws<PharosAssertException>(() => PharosAssert.SameWorldState(before, after));

        Assert.Contains("World state differs in 1 place(s) around 512,4,512 r=2:", error.Message, StringComparison.Ordinal);
        Assert.Contains("block 513,4,512: game:chest-east -> game:air", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void SameWorldState_ShowsAtMostFiftyDifferences()
    {
        var before = new Dictionary<string, string>();
        var after = new Dictionary<string, string>();
        for (int i = 0; i < 60; i++)
        {
            before[$"block {i},0,0"] = "game:air";
            after[$"block {i},0,0"] = "game:stone";
        }

        PharosAssertException error = Assert.Throws<PharosAssertException>(() =>
            PharosAssert.SameWorldState(WorldState.FromEntries(Center, 2, before), WorldState.FromEntries(Center, 2, after)));

        Assert.Contains("World state differs in 60 place(s)", error.Message, StringComparison.Ordinal);
        Assert.Contains("... 10 more", error.Message, StringComparison.Ordinal);
        Assert.Equal(51, error.Message.Split(Environment.NewLine).Length - 1);
    }
}
