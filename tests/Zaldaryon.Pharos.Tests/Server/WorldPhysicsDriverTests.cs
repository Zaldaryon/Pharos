using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Xunit;
using Zaldaryon.Pharos.Server;
using Zaldaryon.Pharos.Tests.Support;
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

    [ServerScenario]
    public async Task Room_ASealedThreeCubedCellarHasNoExitAndIsACellar()
    {
        ServerTestPlayer player = await CreateTestPlayerAsync(PlayerNames.Next());
        BlockPos inside = BuildSealedCellar(player, out _);
        await Host!.World.WaitForRelightAsync();

        RoomReading room = Host.World.Room(inside);

        Assert.True(room.Enclosed, $"A sealed cellar should have no exit, got {room.ExitCount}.");
        Assert.True(room.IsCellar, "A 3x3x3 sealed room should count as a cellar.");
        Assert.True(room.Min.X <= inside.X && inside.X <= room.Max.X, "The room's bounds should contain the position inside it.");
        Assert.True(room.SkylightCount < room.NonSkylightCount, $"A sealed room should see less sky than it does not, got {room.SkylightCount} and {room.NonSkylightCount}.");
    }

    [ServerScenario]
    public async Task Room_OpeningAWallGivesTheRoomAnExit()
    {
        ServerTestPlayer player = await CreateTestPlayerAsync(PlayerNames.Next());
        BlockPos inside = BuildSealedCellar(player, out BlockPos hole);
        this.SetBlock(hole, "game:air");
        await Host!.World.WaitForRelightAsync();

        RoomReading room = Host.World.Room(inside);

        Assert.False(room.Enclosed, "A room with an open wall should have an exit.");
        Assert.True(room.ExitCount > 0);
    }

    [ServerScenario]
    public async Task Light_RisesWhenTheTorchChunkWasRolledBack()
    {
        ServerTestPlayer player = await CreateTestPlayerAsync(PlayerNames.Next());

        // The chunk centre keeps the torch's light inside the chunk the map's one-chunk lookup cache holds.
        BlockPos origin = player.Entity!.Pos.AsBlockPos;
        int chunk = GlobalConstants.ChunkSize;
        BlockPos torch = new((origin.X / chunk) * chunk + chunk / 2, origin.Y, (origin.Z / chunk) * chunk + chunk / 2);
        BlockPos beside = torch.AddCopy(1, 0, 0);

        WorldSnapshot snapshot = Host!.TakeSnapshot();
        this.SetBlock(torch, "game:torch-basic-lit-up");
        await Host.World.WaitForRelightAsync();
        Assert.True(Host.RestoreSnapshot(snapshot) > 0, "The rollback should restore the chunk the torch changed.");

        this.SetBlock(torch, "game:torch-basic-lit-up");
        await Host.World.WaitForRelightAsync();

        Assert.True(Host.World.Light(beside).Block > 0, "After a rollback, a torch placed again should still light its neighbour.");
    }

    [ServerScenario]
    public async Task Light_OfATorchRestoredByARollbackStillLights()
    {
        ServerTestPlayer player = await CreateTestPlayerAsync(PlayerNames.Next());
        BlockPos torch = player.Entity!.Pos.AsBlockPos.AddCopy(4, 0, 0);
        BlockPos beside = torch.AddCopy(1, 0, 0);
        this.SetBlock(torch, "game:torch-basic-lit-up");
        await Host!.World.WaitForRelightAsync();
        Assert.True(Host.World.Light(beside).Block > 0, "The torch should light its neighbour before the snapshot.");

        WorldSnapshot snapshot = Host.TakeSnapshot();
        using (Host.World.PauseLighting())
        {
            // Removing the torch queues the removal of its light. The rollback brings the torch back while that removal waits.
            this.SetBlock(torch, "game:air");
            Assert.True(Host.World.LightingQueued > 0, "Removing the torch should queue lighting work while the lighting thread is held.");
            Host.RestoreSnapshot(snapshot);
        }

        await Host.World.WaitForRelightAsync();
        Assert.Equal("game:torch-basic-lit-up", BlockCode(torch));
        Assert.True(Host.World.Light(beside).Block > 0, "A torch restored by a rollback should still light its neighbour.");
    }

    // A 3x3x3 air room in a stone shell, one block from the player. Returns a position inside and the middle of the west wall.
    private BlockPos BuildSealedCellar(ServerTestPlayer player, out BlockPos wall)
    {
        BlockPos origin = player.Entity!.Pos.AsBlockPos.AddCopy(8, 1, 8);
        for (int x = -1; x <= 3; x++)
        for (int y = -1; y <= 3; y++)
        for (int z = -1; z <= 3; z++)
        {
            bool interior = x >= 0 && x <= 2 && y >= 0 && y <= 2 && z >= 0 && z <= 2;
            BlockPos pos = origin.AddCopy(x, y, z);
            this.SetBlock(pos, interior ? "game:air" : "game:rock-granite");
        }

        wall = origin.AddCopy(-1, 1, 1);
        return origin.AddCopy(1, 1, 1);
    }

    private bool FallingBlockAlive() => Host!.RunOnGameThread(() =>
        Api!.World.LoadedEntities.Values.Any(e => e.Alive && e.GetType().Name == "EntityBlockFalling"));

    private string BlockCode(BlockPos pos) => Host!.RunOnGameThread(() => Api!.World.BlockAccessor.GetBlock(pos).Code.ToString());
}
