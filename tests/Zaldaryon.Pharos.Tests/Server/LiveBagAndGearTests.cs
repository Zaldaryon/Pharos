using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Xunit;
using Zaldaryon.Pharos.Assertions;
using Zaldaryon.Pharos.Core;
using Zaldaryon.Pharos.Items;
using Zaldaryon.Pharos.Server;
using Zaldaryon.Pharos.Tests.Support;
using Zaldaryon.Pharos.XUnit;

namespace Zaldaryon.Pharos.Tests.Server;

/// <summary>The bags, the gear and the liquid containers of the server.</summary>
[Collection("Sequential")]
[Trait(PharosTraits.Category, PharosTraits.Live)]
public class LiveBagAndGearTests : ServerScenarioBase
{
    [ServerScenario]
    public async Task TheBag_HoldsWhatWasPutInIt()
    {
        ServerTestPlayer player = await CreateTestPlayerAsync(PlayerNames.Next());
        player.GiveItem("game:backpack-normal", 1);

        BagContents bag = Server.Inventories.Bag(BagSlotOf(player));
        Assert.True(bag.Put(0, "game:flint", 12));
        Assert.Equal(12, bag.Count("game:flint"));
        Assert.Single(bag.Contents);
    }

    [ServerScenario]
    public async Task TheBagContents_AreOnTheGroundAfterTheDeath()
    {
        ServerTestPlayer player = await CreateTestPlayerAsync(PlayerNames.Next());
        player.GiveItem("game:backpack-normal", 1);

        BagContents bag = Server.Inventories.Bag(BagSlotOf(player));
        Assert.True(bag.Put(0, "game:flint", 12));

        Vec3d death = player.Entity!.Pos.XYZ;
        await player.KillAsync();

        PharosAssert.BagContentsPreserved(Server, 12, death.AsBlockPos, "game:flint");
    }

    [ServerScenario]
    public async Task TheGear_HoldsWhatWasPutInIt()
    {
        ServerTestPlayer player = await CreateTestPlayerAsync(PlayerNames.Next());

        Assert.True(await player.Gear.EquipAsync("armorbody", "game:armor-body-jerkin-leather"));
        Assert.NotNull(player.Gear.Stats.Protection);
        Assert.True(player.Gear.Stats.Protection!.ProtectionTier >= 0);
    }

    [ServerScenario]
    public async Task TheBucket_HoldsWhatWasPouredInIt()
    {
        ServerTestPlayer player = await CreateTestPlayerAsync(PlayerNames.Next());
        player.GiveItem("game:woodbucket", 1);
        player.GiveItem("game:waterportion", 300);

        ItemStack bucket = Server.RunOnGameThread(() => FilledBucket());
        ItemStack jug = Server.RunOnGameThread(() => EmptyBucket());

        LiquidContents before = Server.Liquids.In(bucket);
        float moved = Server.Liquids.Pour(bucket, jug, 3);

        Assert.True(before.Litres > 0, $"The bucket holds {before.Litres} litres, not {before.CapacityLitres} of {before.LiquidCode}.");
        Assert.Equal(3, moved, 2);
        Assert.Equal(7, Server.Liquids.In(bucket).Litres, 1);
    }

    /// <summary>An empty bucket.</summary>
    private ItemStack EmptyBucket() => Server.RunOnGameThread(() =>
    {
        Block bucket = Server.Api.World.GetBlock(new AssetLocation("game:woodbucket"))!;
        return new ItemStack(bucket, 1);
    });

    /// <summary>A bucket with water in it, the way the game's own filled bucket is.</summary>
    private ItemStack FilledBucket() => Server.RunOnGameThread(() =>
    {
        Block bucket = Server.Api.World.GetBlock(new AssetLocation("game:woodbucket"))!;
        ItemStack stack = new(bucket, 1);
        stack.Attributes["ucontents"] = TreeAttribute.FromJson(
            "[{\"type\":\"item\",\"code\":\"waterportion\",\"makefull\":true}]");
        return stack;
    });

    private ItemSlot BagSlotOf(ServerTestPlayer player) => Server.RunOnGameThread(() =>
    {
        IPlayer serverPlayer = player.Player!;
        foreach (string inventory in new[] { "hotbar", "backpack" })
        {
            IInventory? slots = serverPlayer.InventoryManager.GetOwnInventory(inventory);
            if (slots == null) continue;
            foreach (ItemSlot slot in slots)
            {
                if (slot?.Itemstack?.Collectible?.Code.ToString() == "game:backpack-normal") return slot;
            }
        }

        throw new InvalidOperationException("The player has no backpack.");
    });
}
