using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Xunit;
using Zaldaryon.Pharos.Assertions;
using Zaldaryon.Pharos.Server;
using Zaldaryon.Pharos.Tests.Support;
using Zaldaryon.Pharos.XUnit;

namespace Zaldaryon.Pharos.Tests.World;

/// <summary>Block entities of a code mod through a save and reload: a clean one keeps its state, a lossy one shows the loss, and a save that fails is reported.</summary>
[ServerMods("TestMods/pharossavemod")]
[ServerWorld(seed: 4242, playStyle: "creativebuilding", worldType: "superflat")]
public class LiveBlockEntitySaveTests : ServerScenarioBase
{
    protected override bool FailOnLoggedErrors => true;

    [ServerScenario]
    public async Task ACleanBlockEntity_IsTheSameAfterReloadingTheWorld()
    {
        ServerTestPlayer player = await CreateTestPlayerAsync(PlayerNames.Next());
        BlockPos pos = player.Entity!.Pos.AsBlockPos.AddCopy(4, 0, 0);
        this.SetBlock(pos, "pharossavemod:savecase-clean");
        SetCount(pos, 5);
        await Host!.SaveWorldAsync();
        WorldState before = WorldState.Capture(Host!, pos, radius: 1);

        await ReloadWorldAsync();

        PharosAssert.SameWorldState(before, WorldState.Capture(Host!, pos, radius: 1));
        Assert.Equal(5, GetCount(pos));
    }

    [ServerScenario]
    public async Task ALossyBlockEntity_ShowsTheCountItLost()
    {
        ServerTestPlayer player = await CreateTestPlayerAsync(PlayerNames.Next());
        BlockPos pos = player.Entity!.Pos.AsBlockPos.AddCopy(4, 0, 0);
        this.SetBlock(pos, "pharossavemod:savecase-lossy");
        SetCount(pos, 5);
        await Host!.SaveWorldAsync();
        WorldState before = WorldState.Capture(Host!, pos, radius: 1);

        await ReloadWorldAsync();

        IReadOnlyList<string> differences = WorldState.Differences(before, WorldState.Capture(Host!, pos, radius: 1));
        Assert.Contains(differences, line => line.EndsWith("/count: 5 -> 0", StringComparison.Ordinal));
    }

    [ServerScenario]
    public async Task ASaveTimeSerializationError_FailsTheReload()
    {
        ServerTestPlayer player = await CreateTestPlayerAsync(PlayerNames.Next());
        BlockPos pos = player.Entity!.Pos.AsBlockPos.AddCopy(4, 0, 0);
        this.SetBlock(pos, "pharossavemod:savecase-throwing");
        SetCount(pos, -1);

        // The reload saves the chunk, and the game logs a block entity that fails to serialize instead of stopping the save. The
        // reload checks the log of the server it stopped, so the error fails the reload.
        PharosAssertException error = await Assert.ThrowsAsync<PharosAssertException>(() => ReloadWorldAsync());
        Assert.Contains("serialize block entity", error.Message, StringComparison.Ordinal);
    }

    private void SetCount(BlockPos pos, int count) => Host!.RunOnGameThread(() =>
    {
        object be = Api!.World.BlockAccessor.GetBlockEntity(pos) ?? throw new InvalidOperationException($"There is no block entity at {pos}.");
        be.GetType().GetField("Count")!.SetValue(be, count);
        ((BlockEntity)be).MarkDirty();
    });

    private int GetCount(BlockPos pos) => Host!.RunOnGameThread(() =>
    {
        object be = Api!.World.BlockAccessor.GetBlockEntity(pos) ?? throw new InvalidOperationException($"There is no block entity at {pos}.");
        return (int)be.GetType().GetField("Count")!.GetValue(be)!;
    });
}
