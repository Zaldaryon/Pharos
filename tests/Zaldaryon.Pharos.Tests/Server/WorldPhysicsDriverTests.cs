using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Xunit;
using Zaldaryon.Pharos.Server;
using Zaldaryon.Pharos.XUnit;

namespace Zaldaryon.Pharos.Tests.Server;

[Collection("Sequential")]
[ServerWorld(seed: 4242, playStyle: "creativebuilding", worldType: "superflat")]
public class WorldPhysicsDriverTests : ServerScenarioBase
{
    [ServerScenario]
    public async Task Light_RisesOnceTheTorchIsRelit()
    {
        ServerTestPlayer player = await CreateTestPlayerAsync("LightProbe");
        BlockPos torch = player.Entity!.Pos.AsBlockPos.AddCopy(4, 0, 0);
        BlockPos beside = torch.AddCopy(1, 0, 0);

        this.SetBlock(torch, "game:torch-basic-lit-up");
        await Host!.World.WaitForRelightAsync();

        LightReading lit = Host.World.Light(beside);
        Assert.True(lit.Block > 0, $"A block next to a lit torch should carry block light, got {lit.Block}.");
    }

    [ServerScenario]
    public async Task FallingBlocks_LandAndWaitReturns()
    {
        ServerTestPlayer player = await CreateTestPlayerAsync("FallProbe");
        BlockPos top = player.Entity!.Pos.AsBlockPos.AddCopy(4, 4, 0);
        BlockPos landing = player.Entity.Pos.AsBlockPos.AddCopy(4, 0, 0);

        // Sand falls only when its support goes, so stand it on a block and then remove that block.
        BlockPos support = top.DownCopy();
        this.SetBlock(support, "game:rock-granite");
        this.SetBlock(top, "game:sand-granite");
        Host!.RunOnGameThread(() =>
        {
            Api!.World.BlockAccessor.SetBlock(Api.World.GetBlock(new AssetLocation("game:air")).BlockId, support);
            Api.World.BlockAccessor.TriggerNeighbourBlockUpdate(support);
        });

        Assert.True(await Host!.TickUntilAsync(() => FallingBlockAlive(), maxTicks: 200), "Sand with no support should start falling.");
        await Host.World.WaitForFallingBlocksAsync(maxTicks: 600);

        Assert.Equal("game:air", BlockCode(top));
        Assert.Contains("sand", BlockCode(landing), StringComparison.Ordinal);
    }

    [ServerScenario]
    public async Task Liquids_SettleIntoTheBasinBelow()
    {
        ServerTestPlayer player = await CreateTestPlayerAsync("LiquidProbe");
        BlockPos source = player.Entity!.Pos.AsBlockPos.AddCopy(-4, 3, 0);
        BlockPos floor = player.Entity.Pos.AsBlockPos.AddCopy(-4, 0, 0);
        BlockPos min = source.AddCopy(-2, -3, -2);
        BlockPos max = source.AddCopy(2, 0, 2);

        this.SetBlock(source, "game:water-still-7");
        Host!.RunOnGameThread(() => Api!.World.BlockAccessor.TriggerNeighbourBlockUpdate(source));
        Host.Ticks(20);
        await Host.World.WaitForLiquidsSettledAsync(min, max, maxTicks: 600);

        Assert.StartsWith("game:water", BlockCode(floor), StringComparison.Ordinal);
    }

    private bool FallingBlockAlive() => Host!.RunOnGameThread(() =>
        Api!.World.LoadedEntities.Values.Any(e => e.Alive && e.GetType().Name == "EntityBlockFalling"));

    private string BlockCode(BlockPos pos) => Host!.RunOnGameThread(() => Api!.World.BlockAccessor.GetBlock(pos).Code.ToString());
}
