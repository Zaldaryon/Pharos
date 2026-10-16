using Vintagestory.API.Common;
using Vintagestory.Client;
using Xunit;
using Zaldaryon.Pharos.Bootstrap;
using Zaldaryon.Pharos.Core;
using Zaldaryon.Pharos.XUnit;

namespace Zaldaryon.Pharos.Tests.UI;

/// <summary>The survival inventory's crafting grid, filled with clicks on an engine-mode client and checked on the server.</summary>
[Collection("Sequential")]
[Trait(PharosTraits.Category, PharosTraits.Live)]
[ServerWorld(seed: 4242, playStyle: "surviveandbuild")]
public class CraftingGridTests : ClientServerScenarioBase
{
    protected override HeadlessClientOptions ClientOptions => new()
    {
        BootMode = ClientBootMode.Engine,
        Width = 1280,
        Height = 720,
    };

    protected override string PlayerName => "CraftingPlayer";

    [ClientServerScenario]
    public async Task PlacesGrassAndSticks_TakesFirestarter()
    {
        ServerPlayer.GiveItem("game:drygrass");
        ServerPlayer.GiveItem("game:stick", 2);
        Assert.True(await StepUntilAsync(() => HotbarHolds("game:drygrass", 1) && HotbarHolds("game:stick", 2), maxFrames: 600), "The client never received the grass and sticks.");

        await Client!.Ui!.Crafting.PlaceAsync(0, 0, "game:drygrass");
        await Client.Ui.Crafting.PlaceAsync(0, 1, "game:stick");
        await Client.Ui.Crafting.PlaceAsync(1, 0, "game:stick");
        Assert.True(await StepUntilAsync(() => ServerCell(0, 0) == "game:drygrass" && ServerCell(1, 0) == "game:stick" && ServerCell(0, 1) == "game:stick", maxFrames: 600), "The server's grid never took the three items.");

        // The server's last cursor update can reach the client after the client emptied its cursor, so wait until both agree it is empty.
        Assert.True(await StepUntilAsync(() => ServerPlayer.SlotItem("mouse", 0) is null && Client.Ui.Crafting.Cursor is null, maxFrames: 600), "The cursor never emptied on the server and the client.");

        (string Code, int Quantity) output = await Client.Ui.Crafting.TakeOutputAsync();

        Assert.Equal(("game:firestarter", 1), output);
        Assert.True(await StepUntilAsync(() => ServerPlayer.HasItem("game:firestarter") && ServerCell(0, 0) is null && ServerCell(0, 1) is null && ServerCell(1, 0) is null, maxFrames: 600), "The ingredients were not used up.");
    }

    [ClientServerScenario]
    public async Task BandageRecipe_DamagesTheKnifeAndGivesFourBandages()
    {
        ServerPlayer.GiveItem("game:knife-generic-flint");
        ServerPlayer.GiveItem("game:linen-normal-up");
        Assert.True(await StepUntilAsync(() => HotbarHolds("game:knife-generic-flint", 1) && HotbarHolds("game:linen-normal-up", 1), maxFrames: 600), "The client never received the knife and linen.");
        int? before = ServerPlayer.ItemDurability("game:knife-generic-flint");
        Assert.NotNull(before);

        await Client!.Ui!.Crafting.PlaceAsync(0, 0, "game:knife-generic-flint");
        await Client.Ui.Crafting.PlaceAsync(0, 1, "game:linen-normal-up");
        (string Code, int Quantity) output = await Client.Ui.Crafting.TakeOutputAsync();

        Assert.Equal(("game:bandage-clean", 4), output);

        // The knife is a tool the recipe uses, not an ingredient it consumes: it stays, and loses durability.
        Assert.True(await StepUntilAsync(() => ServerPlayer.ItemDurability("game:knife-generic-flint") < before, maxFrames: 600), "The knife lost no durability.");
    }

    [ClientServerScenario]
    public async Task ClosingTheInventory_ReturnsTheIngredients()
    {
        ServerPlayer.GiveItem("game:drygrass");
        Assert.True(await StepUntilAsync(() => HotbarHolds("game:drygrass", 1), maxFrames: 600), "The client never received the grass.");

        await Client!.Ui!.Crafting.PlaceAsync(0, 0, "game:drygrass");
        Assert.True(await StepUntilAsync(() => ServerCell(0, 0) == "game:drygrass", maxFrames: 600), "The server's grid never took the grass.");

        await Client.Ui.Crafting.CloseAsync();

        Assert.True(await StepUntilAsync(() => ServerCell(0, 0) is null && ServerPlayer.HasItem("game:drygrass"), maxFrames: 600), "Closing the inventory did not return the grass.");
    }

    [ClientServerScenario]
    public async Task PlaceAsync_RefusesAnItemTheHotbarDoesNotHold()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => Client!.Ui!.Crafting.PlaceAsync(0, 0, "game:flint"));
    }

    private bool HotbarHolds(string itemCode, int quantity) => Client!.RunOnClientThread(() =>
    {
        int total = 0;
        IInventory hotbar = Client.Client.player.InventoryManager.GetHotbarInventory();
        for (int i = 0; i < 10; i++)
        {
            ItemStack? stack = hotbar[i]?.Itemstack;
            if (stack?.Collectible.Code.ToString() == itemCode) total += stack.StackSize;
        }

        return total >= quantity;
    });

    private string? ServerCell(int row, int column) => ServerPlayer.SlotItem("craftinggrid", row * 3 + column);
}

/// <summary>The crafting grid does not exist in creative mode, and the driver says so instead of clicking.</summary>
[Collection("Sequential")]
[Trait(PharosTraits.Category, PharosTraits.Live)]
[ServerWorld(seed: 4242, playStyle: "creativebuilding")]
public class CraftingGridCreativeTests : ClientServerScenarioBase
{
    protected override HeadlessClientOptions ClientOptions => new()
    {
        BootMode = ClientBootMode.Engine,
        Width = 1280,
        Height = 720,
    };

    protected override string PlayerName => "CreativeCrafter";

    [ClientServerScenario]
    public async Task CreativeMode_RefusesTheCraftingGrid()
    {
        InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(() => Client!.Ui!.Crafting.OpenAsync());

        Assert.Contains("creative", error.Message, StringComparison.OrdinalIgnoreCase);
    }
}
