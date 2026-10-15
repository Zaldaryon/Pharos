using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Xunit;
using Zaldaryon.Pharos.Assertions;
using Zaldaryon.Pharos.Bootstrap;
using Zaldaryon.Pharos.Server;
using Zaldaryon.Pharos.Tests.Support;
using Zaldaryon.Pharos.Timing;
using Zaldaryon.Pharos.XUnit;

namespace Zaldaryon.Pharos.Tests.Server;

/// <summary>Chunks unloaded and loaded again from the save: new objects with what the save held.</summary>
[ServerWorld(seed: 4242, playStyle: "creativebuilding", worldType: "superflat")]
public class ChunkReloadTests : ServerScenarioBase
{
    [ServerScenario]
    public async Task UnloadAndReload_GivesNewChunks_AndTheSameChest()
    {
        ServerTestPlayer player = await CreateTestPlayerAsync(PlayerNames.Next());
        BlockPos chest = player.Entity!.Pos.AsBlockPos.AddCopy(4, 0, 0);
        this.SetBlock(chest, "game:chest-east");
        FillSlot(chest, 3, "game:ingot-copper", 5);
        WorldState before = WorldState.Capture(Host!, chest, radius: 2);
        object loaded = ChunkAt(chest) ?? throw new InvalidOperationException("The chest's chunk is not loaded.");

        await Host!.World.UnloadChunksAsync(chest, chest);
        Assert.Null(ChunkAt(chest));

        await Host.World.ReloadChunksAsync(chest, chest);
        object reloaded = ChunkAt(chest) ?? throw new InvalidOperationException("The chest's chunk did not load again.");
        Assert.NotSame(loaded, reloaded);
        PharosAssert.SameWorldState(before, WorldState.Capture(Host, chest, radius: 2));
    }

    [ServerScenario]
    public async Task Unload_RefusesThePlayersColumn()
    {
        ServerTestPlayer player = await CreateTestPlayerAsync(PlayerNames.Next());
        BlockPos standing = player.Entity!.Pos.AsBlockPos;

        InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(() => Host!.World.UnloadChunksAsync(standing, standing));

        Assert.Contains("stands in the chunk column", error.Message, StringComparison.Ordinal);
        Assert.NotNull(ChunkAt(standing));
    }

    [Fact]
    public async Task Unload_WritesTheSave_SoACrashKeepsTheChest()
    {
        using ServerSandbox sandbox = new();
        using EmbeddedServerHost first = EmbeddedServerHost.Boot(sandbox, new ServerWorldOptions
        {
            WorldName = "pharos-unload",
            WorldType = "superflat",
            PlayStyle = "creativebuilding",
            Seed = "4242",
        });
        BlockPos chest = new(5, 60, 5);
        await first.EnsureChunksLoadedAsync(chest, chest);
        first.RunOnGameThread(() =>
        {
            ICoreServerAPI api = (ICoreServerAPI)first.Server.Api;
            Block chestBlock = api.World.GetBlock(new AssetLocation("game:chest-east")) ?? throw new InvalidOperationException("The game has no chest.");
            api.World.BlockAccessor.SetBlock(chestBlock.BlockId, chest);
        });

        // The chunk leaves memory with the chest in it, and the crash that follows writes nothing: only the unload wrote it.
        await first.World.UnloadChunksAsync(chest, chest);
        EmbeddedServerHost second = await first.RestartAsync(RestartMode.Crash);
        try
        {
            await second.EnsureChunksLoadedAsync(chest, chest);
            string code = second.RunOnGameThread(() => ((ICoreServerAPI)second.Server.Api).World.BlockAccessor.GetBlock(chest).Code.ToString());
            Assert.Equal("game:chest-east", code);
        }
        finally
        {
            second.Dispose();
        }
    }

    private object? ChunkAt(BlockPos pos) => Host!.RunOnGameThread(() =>
    {
        long index = Host.Server.WorldMap.ChunkIndex3D(pos.X / 32, pos.Y / 32, pos.Z / 32);
        return Host.Server.GetLoadedChunk(index);
    });

    private void FillSlot(BlockPos pos, int slot, string code, int quantity) => Host!.RunOnGameThread(() =>
    {
        ItemSlot target = Inventory(pos)[slot] ?? throw new InvalidOperationException($"The block at {pos} has no slot {slot}.");
        CollectibleObject item = Api!.World.GetItem(new AssetLocation(code)) ?? throw new InvalidOperationException($"The game has no {code}.");
        target.Itemstack = new ItemStack(item, quantity);
        target.MarkDirty();
    });

    private IInventory Inventory(BlockPos pos)
    {
        object be = Api!.World.BlockAccessor.GetBlockEntity(pos) ?? throw new InvalidOperationException($"There is no block entity at {pos}.");
        return (IInventory)(be.GetType().GetProperty("Inventory")?.GetValue(be) ?? be.GetType().GetField("Inventory")!.GetValue(be))!;
    }
}
