using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Xunit;
using Zaldaryon.Pharos.Assertions;
using Zaldaryon.Pharos.Bootstrap;
using Zaldaryon.Pharos.Core;
using Zaldaryon.Pharos.Server;
using Zaldaryon.Pharos.XUnit;

namespace Zaldaryon.Pharos.Tests.World;

/// <summary>An engine-mode client through a save and reload: the client joins the saved world again and the chest is still full.</summary>
[Collection("Sequential")]
[Trait(PharosTraits.Category, PharosTraits.Live)]
[ServerWorld(seed: 4242, playStyle: "creativebuilding", worldType: "superflat")]
public class LiveClientSaveReloadTests : ClientServerScenarioBase
{
    protected override HeadlessClientOptions ClientOptions => new()
    {
        BootMode = ClientBootMode.Engine,
        Width = 1280,
        Height = 720,
    };

    protected override string PlayerName => "ReloadPlayer";

    [ClientServerScenario]
    public async Task ReloadWorldAsync_KeepsTheChest()
    {
        BlockPos chest = ServerPlayer.Entity!.Pos.AsBlockPos.AddCopy(4, 0, 0);
        await ServerHost!.EnsureChunksLoadedAsync(chest, chest);
        SetBlock(ServerHost, chest, "game:chest-east");
        FillSlot(ServerHost, chest, 3, "game:ingot-copper", 5);
        WorldState before = WorldState.Capture(ServerHost, chest, radius: 2);

        await ReloadWorldAsync();

        Assert.True(await StepUntilAsync(() => Client!.IsJoined, maxFrames: 600), "The client did not join the reloaded world.");
        await ServerHost!.EnsureChunksLoadedAsync(chest, chest);
        PharosAssert.SameWorldState(before, WorldState.Capture(ServerHost, chest, radius: 2));
        Assert.Equal("5x game:ingot-copper", SlotDescription(ServerHost, chest, 3));
    }

    private static void SetBlock(EmbeddedServerHost host, BlockPos pos, string code) => host.RunOnGameThread(() =>
    {
        ICoreServerAPI api = (ICoreServerAPI)host.Server.Api;
        Block block = api.World.GetBlock(new AssetLocation(code)) ?? throw new InvalidOperationException($"The game has no block {code}.");
        api.World.BlockAccessor.SetBlock(block.BlockId, pos);
    });

    private static void FillSlot(EmbeddedServerHost host, BlockPos pos, int slot, string code, int quantity) => host.RunOnGameThread(() =>
    {
        ICoreServerAPI api = (ICoreServerAPI)host.Server.Api;
        ItemSlot target = Inventory(api, pos)[slot] ?? throw new InvalidOperationException($"The block at {pos} has no slot {slot}.");
        CollectibleObject item = api.World.GetItem(new AssetLocation(code)) ?? throw new InvalidOperationException($"The game has no {code}.");
        target.Itemstack = new ItemStack(item, quantity);
        target.MarkDirty();
    });

    private static string SlotDescription(EmbeddedServerHost host, BlockPos pos, int slot) => host.RunOnGameThread(() =>
    {
        ItemStack? stack = Inventory((ICoreServerAPI)host.Server.Api, pos)[slot]?.Itemstack;
        return stack is null ? "empty" : $"{stack.StackSize}x {stack.Collectible.Code}";
    });

    private static IInventory Inventory(ICoreServerAPI api, BlockPos pos)
    {
        object be = api.World.BlockAccessor.GetBlockEntity(pos) ?? throw new InvalidOperationException($"There is no block entity at {pos}.");
        return (IInventory)(be.GetType().GetProperty("Inventory")?.GetValue(be) ?? be.GetType().GetField("Inventory")!.GetValue(be))!;
    }
}
