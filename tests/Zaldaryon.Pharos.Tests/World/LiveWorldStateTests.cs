using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Xunit;
using Zaldaryon.Pharos.Assertions;
using Zaldaryon.Pharos.Server;
using Zaldaryon.Pharos.Tests.Support;
using Zaldaryon.Pharos.XUnit;

namespace Zaldaryon.Pharos.Tests.World;

/// <summary>Capturing a region of a live server: an unchanged region compares equal, and a changed chest slot shows up.</summary>
[ServerWorld(seed: 4242, playStyle: "creativebuilding", worldType: "superflat")]
public class LiveWorldStateTests : ServerScenarioBase
{
    [ServerScenario]
    public async Task Capture_TwiceWithoutTicks_IsTheSame()
    {
        ServerTestPlayer player = await CreateTestPlayerAsync(PlayerNames.Next());
        BlockPos chest = player.Entity!.Pos.AsBlockPos.AddCopy(4, 0, 0);
        this.SetBlock(chest, "game:chest-east");

        WorldState first = WorldState.Capture(Host!, chest, radius: 2);
        WorldState second = WorldState.Capture(Host!, chest, radius: 2);

        PharosAssert.SameWorldState(first, second);
    }

    [ServerScenario]
    public async Task Capture_SeesAChangedChestSlot()
    {
        ServerTestPlayer player = await CreateTestPlayerAsync(PlayerNames.Next());
        BlockPos chest = player.Entity!.Pos.AsBlockPos.AddCopy(4, 0, 0);
        this.SetBlock(chest, "game:chest-east");
        WorldState before = WorldState.Capture(Host!, chest, radius: 2);

        Host!.RunOnGameThread(() =>
        {
            object be = Api!.World.BlockAccessor.GetBlockEntity(chest)!;
            var inventory = (IInventory)(be.GetType().GetProperty("Inventory")?.GetValue(be) ?? be.GetType().GetField("Inventory")!.GetValue(be))!;
            ItemSlot slot = inventory[3] ?? throw new InvalidOperationException("The chest has no slot 3.");
            CollectibleObject copper = Api.World.GetItem(new AssetLocation("game:ingot-copper")) ?? throw new InvalidOperationException("The game has no copper ingot.");
            slot.Itemstack = new ItemStack(copper, 5);
            slot.MarkDirty();
        });
        WorldState after = WorldState.Capture(Host, chest, radius: 2);

        IReadOnlyList<string> differences = WorldState.Differences(before, after);
        Assert.Contains(differences, line => line.Contains("/inventory/", StringComparison.Ordinal) && line.EndsWith("-> 5x game:ingot-copper", StringComparison.Ordinal));
        Assert.Throws<PharosAssertException>(() => PharosAssert.SameWorldState(before, after));
    }
}
