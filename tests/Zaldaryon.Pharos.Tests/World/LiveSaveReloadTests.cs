using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Xunit;
using Zaldaryon.Pharos.Assertions;
using Zaldaryon.Pharos.Server;
using Zaldaryon.Pharos.Tests.Support;
using Zaldaryon.Pharos.XUnit;

namespace Zaldaryon.Pharos.Tests.World;

/// <summary>A world saved and booted again on its save: what the save gives back is what was there.</summary>
[ServerWorld(seed: 4242, playStyle: "creativebuilding", worldType: "superflat")]
public class LiveSaveReloadTests : ServerScenarioBase
{
    [ServerScenario]
    public async Task AChestWithItems_IsTheSameAfterReloadingTheWorld()
    {
        ServerTestPlayer player = await CreateTestPlayerAsync(PlayerNames.Next());
        BlockPos chest = player.Entity!.Pos.AsBlockPos.AddCopy(4, 0, 0);
        this.SetBlock(chest, "game:chest-east");
        FillSlot(chest, 3, "game:ingot-copper", 5);
        await Host!.SaveWorldAsync();
        WorldState before = WorldState.Capture(Host!, chest, radius: 2);
        EmbeddedServerHost booted = Host!;

        await ReloadWorldAsync();

        Assert.NotSame(booted, Host);
        PharosAssert.SameWorldState(before, WorldState.Capture(Host!, chest, radius: 2));
        Assert.Equal("5x game:ingot-copper", SlotDescription(chest, 3));
    }

    private void FillSlot(BlockPos pos, int slot, string code, int quantity) => Host!.RunOnGameThread(() =>
    {
        ItemSlot target = Inventory(pos)[slot] ?? throw new InvalidOperationException($"The block at {pos} has no slot {slot}.");
        CollectibleObject item = Api!.World.GetItem(new AssetLocation(code)) ?? throw new InvalidOperationException($"The game has no {code}.");
        target.Itemstack = new ItemStack(item, quantity);
        target.MarkDirty();
    });

    private string SlotDescription(BlockPos pos, int slot) => Host!.RunOnGameThread(() =>
    {
        ItemStack? stack = Inventory(pos)[slot]?.Itemstack;
        return stack is null ? "empty" : $"{stack.StackSize}x {stack.Collectible.Code}";
    });

    private IInventory Inventory(BlockPos pos)
    {
        object be = Api!.World.BlockAccessor.GetBlockEntity(pos) ?? throw new InvalidOperationException($"There is no block entity at {pos}.");
        return (IInventory)(be.GetType().GetProperty("Inventory")?.GetValue(be) ?? be.GetType().GetField("Inventory")!.GetValue(be))!;
    }
}
