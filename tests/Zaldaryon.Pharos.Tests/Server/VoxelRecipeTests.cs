using System.Collections;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Xunit;
using Zaldaryon.Pharos.Server;
using Zaldaryon.Pharos.Tests.Support;
using Zaldaryon.Pharos.XUnit;

namespace Zaldaryon.Pharos.Tests.Server;

/// <summary>Knapping and clay forming recipes on the server, completed through the block entities' own packets.</summary>
[ServerWorld(seed: 4242, playStyle: "creativebuilding", worldType: "superflat")]
public class VoxelRecipeTests : ServerScenarioBase
{
    [ServerScenario]
    public async Task Knapping_CompletesTheFlintKnifeBlade()
    {
        ServerTestPlayer player = await CreateTestPlayerAsync(PlayerNames.Next());
        player.GiveItem("game:flint");
        Host!.Ticks(2);
        BlockPos surface = player.Entity!.Pos.AsBlockPos.AddCopy(2, 0, 0);

        Host.Recipes.PlaceKnappingSurface(surface, "game:flint");
        await Host.Recipes.CompleteVoxelRecipeAsync(player, surface, "game:knifeblade-flint");

        Assert.Equal("game:air", BlockCode(surface));
        Assert.True(player.HasItem("game:knifeblade-flint"), "The finished knife blade should go to the player.");
    }

    [ServerScenario]
    public async Task Knapping_RefusesAMaterialTheRecipeDoesNotUse()
    {
        ServerTestPlayer player = await CreateTestPlayerAsync(PlayerNames.Next());
        player.GiveItem("game:flint");
        Host!.Ticks(2);
        BlockPos surface = player.Entity!.Pos.AsBlockPos.AddCopy(2, 0, 0);
        Host.Recipes.PlaceKnappingSurface(surface, "game:stone-granite");

        InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(() => Host.Recipes.CompleteVoxelRecipeAsync(player, surface, "game:knifeblade-flint"));
        Assert.Contains("knifeblade-flint", error.Message, StringComparison.Ordinal);
        Assert.Equal("game:knappingsurface", BlockCode(surface));
    }

    [ServerScenario]
    public async Task Knapping_NamesTheVoxelTheWorkItemLacks()
    {
        ServerTestPlayer player = await CreateTestPlayerAsync(PlayerNames.Next());
        player.GiveItem("game:flint");
        Host!.Ticks(2);
        BlockPos surface = player.Entity!.Pos.AsBlockPos.AddCopy(2, 0, 0);
        Host.Recipes.PlaceKnappingSurface(surface, "game:flint");
        Host.RunOnGameThread(() => Api!.World.BlockAccessor.GetBlockEntity(surface)!.GetType().GetField("Voxels")!.SetValue(Api.World.BlockAccessor.GetBlockEntity(surface), new bool[16, 16]));

        InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(() => Host.Recipes.CompleteVoxelRecipeAsync(player, surface, "game:knifeblade-flint"));
        Assert.Contains("lacks the voxel", error.Message, StringComparison.Ordinal);
    }

    [ServerScenario]
    public async Task Clayforming_CompletesABowl()
    {
        ServerTestPlayer player = await CreateTestPlayerAsync(PlayerNames.Next());
        player.GiveItem("game:clay-blue", 4);
        Host!.Ticks(2);
        BlockPos form = player.Entity!.Pos.AsBlockPos.AddCopy(2, 0, 0);

        Host.Recipes.PlaceClayForm(form, "game:clay-blue");
        await Host.Recipes.CompleteVoxelRecipeAsync(player, form, "game:bowl-blue-raw");

        // A bowl is ground-storable, so the game leaves it on the ground as an item rather than placing a block.
        Assert.Equal("game:groundstorage", BlockCode(form));
        Assert.Equal("game:bowl-blue-raw", GroundItem(form));
    }

    [ServerScenario]
    public async Task Clayforming_TakesClayFromTheHandWhenTheFormRunsOut()
    {
        ServerTestPlayer player = await CreateTestPlayerAsync(PlayerNames.Next());
        player.GiveItem("game:clay-blue", 4);
        Host!.Ticks(2);
        BlockPos form = player.Entity!.Pos.AsBlockPos.AddCopy(2, 0, 0);
        Host.Recipes.PlaceClayForm(form, "game:clay-blue");

        // An empty work item with no voxel budget: the bowl needs 41 voxels, so the form must take clay from the hand twice,
        // each time adding 25 voxels to the budget (ItemClay).
        Host.RunOnGameThread(() =>
        {
            BlockEntity be = Api!.World.BlockAccessor.GetBlockEntity(form)!;
            be.GetType().GetField("Voxels")!.SetValue(be, new bool[16, 16, 16]);
            be.GetType().GetField("AvailableVoxels")!.SetValue(be, 0);
        });

        await Host.Recipes.CompleteVoxelRecipeAsync(player, form, "game:bowl-blue-raw");

        Assert.Equal("game:groundstorage", BlockCode(form));
        Assert.Equal("game:bowl-blue-raw", GroundItem(form));
        Assert.Equal(2, HeldClay(player));
    }

    [ServerScenario]
    public async Task Clayforming_ChoosesTheRecipeByOutputQuantity()
    {
        ServerTestPlayer player = await CreateTestPlayerAsync(PlayerNames.Next());
        player.GiveItem("game:clay-blue", 8);
        Host!.Ticks(2);
        BlockPos form = player.Entity!.Pos.AsBlockPos.AddCopy(2, 0, 0);
        Host.Recipes.PlaceClayForm(form, "game:clay-blue");

        // The game makes one blue bowl, or four under the same output code. No recipe makes two, so asking for two finds none.
        InvalidOperationException none = await Assert.ThrowsAsync<InvalidOperationException>(() => Host.Recipes.CompleteVoxelRecipeAsync(player, form, "game:bowl-blue-raw", outputQuantity: 2));
        Assert.Contains("x2", none.Message, StringComparison.Ordinal);

        // Four is the recipe with four bowls on the ground, not the single bowl the default picks.
        await Host.Recipes.CompleteVoxelRecipeAsync(player, form, "game:bowl-blue-raw", outputQuantity: 4);
        Assert.Equal("game:bowl-blue-raw", GroundItem(form));
        Assert.Equal(4, GroundCount(form));
    }

    [ServerScenario]
    public async Task Clayforming_RefusesAMaterialTheRecipeDoesNotUse()
    {
        ServerTestPlayer player = await CreateTestPlayerAsync(PlayerNames.Next());
        player.GiveItem("game:clay-red", 4);
        Host!.Ticks(2);
        BlockPos form = player.Entity!.Pos.AsBlockPos.AddCopy(2, 0, 0);
        Host.Recipes.PlaceClayForm(form, "game:clay-red");

        // The red clay makes a red bowl, so asking for the blue one finds no recipe.
        InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(() => Host.Recipes.CompleteVoxelRecipeAsync(player, form, "game:bowl-blue-raw"));
        Assert.Contains("bowl-blue-raw", error.Message, StringComparison.Ordinal);
    }

    [ServerScenario]
    public async Task Clayforming_NeedsClayInHandToRefillTheBudget()
    {
        ServerTestPlayer player = await CreateTestPlayerAsync(PlayerNames.Next());
        Host!.Ticks(2);
        BlockPos form = player.Entity!.Pos.AsBlockPos.AddCopy(2, 0, 0);
        Host.Recipes.PlaceClayForm(form, "game:clay-blue");
        Host.RunOnGameThread(() =>
        {
            BlockEntity be = Api!.World.BlockAccessor.GetBlockEntity(form)!;
            be.GetType().GetField("Voxels")!.SetValue(be, new bool[16, 16, 16]);
            be.GetType().GetField("AvailableVoxels")!.SetValue(be, 0);
        });

        InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(() => Host.Recipes.CompleteVoxelRecipeAsync(player, form, "game:bowl-blue-raw"));
        Assert.Contains("clay", error.Message, StringComparison.Ordinal);
    }

    // The item a ground storage block holds in its first slot, read through the block entity's inventory.
    private string GroundItem(BlockPos pos) => Host!.RunOnGameThread(() =>
    {
        object be = Api!.World.BlockAccessor.GetBlockEntity(pos)!;
        object inventory = be.GetType().GetProperty("Inventory")?.GetValue(be) ?? be.GetType().GetField("Inventory")!.GetValue(be)!;
        var slot = (ItemSlot)((IEnumerable<ItemSlot>)inventory)?.First()!;
        return slot.Itemstack?.Collectible.Code.ToString() ?? "nothing";
    });

    // The items on the ground in all of the storage's slots: four bowls take four slots, one each.
    private int GroundCount(BlockPos pos) => Host!.RunOnGameThread(() =>
    {
        object be = Api!.World.BlockAccessor.GetBlockEntity(pos)!;
        object inventory = be.GetType().GetProperty("Inventory")?.GetValue(be) ?? be.GetType().GetField("Inventory")!.GetValue(be)!;
        return ((IEnumerable<ItemSlot>)inventory).Sum(slot => slot.Itemstack?.StackSize ?? 0);
    });

    private int HeldClay(ServerTestPlayer player) => Host!.RunOnGameThread(() => player.Player!.InventoryManager.ActiveHotbarSlot.StackSize);

    private string BlockCode(BlockPos pos) => Host!.RunOnGameThread(() => Api!.World.BlockAccessor.GetBlock(pos).Code.ToString());
}
