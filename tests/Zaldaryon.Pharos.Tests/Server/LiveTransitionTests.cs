using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Xunit;
using Zaldaryon.Pharos.Assertions;
using Zaldaryon.Pharos.Core;
using Zaldaryon.Pharos.Server;
using Zaldaryon.Pharos.Tests.Support;
using Zaldaryon.Pharos.XUnit;

namespace Zaldaryon.Pharos.Tests.Server;

/// <summary>The transitions of a stack, and its temperature.</summary>
[Collection("Sequential")]
[Trait(PharosTraits.Category, PharosTraits.Live)]
public class LiveTransitionTests : ServerScenarioBase
{
    private const string Bread = "game:bread-spelt-partbaked";

    [ServerScenario]
    public async Task TheStackSpoils_WhenTheTimePasses()
    {
        ServerTestPlayer player = await CreateTestPlayerAsync(PlayerNames.Next());
        player.GiveItem(Bread, 1);
        ItemSlot slot = SlotOf(player, Bread);
        await Server.Items.AgeAsync(TimeSpan.Zero);

        TransitionState? fresh = Server.Items.Transition(slot, EnumTransitionType.Perish);
        Assert.NotNull(fresh);
        Assert.Equal(0, fresh!.TransitionLevel, 3);
        Assert.True(fresh.FreshHours > 0, $"The bread stays fresh for {fresh.FreshHours} hours.");

        // The fresh time is over and the transition is under way: the stack is half spoiled.
        await Server.Items.AgeAsync(TimeSpan.FromHours(fresh.FreshHours + 12));

        TransitionState? half = Server.Items.Transition(slot, EnumTransitionType.Perish);
        Assert.NotNull(half);
        Assert.InRange(half!.TransitionLevel, 0.2f, 0.8f);

        // The transition is over: the stack is what it turns into. The game swaps the stack when
        // the transitions are read, so the read comes first.
        await Server.Items.AgeAsync(TimeSpan.FromHours(24));
        Server.Items.Transitions(slot);

        Assert.Equal("game:rot", Server.RunOnGameThread(() => slot.Itemstack?.Collectible.Code.ToString()));
    }

    [ServerScenario]
    public async Task TheStackSpoilsWithinTheDaysTheItemSays()
    {
        ServerTestPlayer player = await CreateTestPlayerAsync(PlayerNames.Next());
        player.GiveItem(Bread, 1);
        ItemSlot slot = SlotOf(player, Bread);
        await Server.Items.AgeAsync(TimeSpan.Zero);

        TransitionState? perish = Server.Items.Transition(slot, EnumTransitionType.Perish);
        Assert.NotNull(perish);

        PharosAssert.SpoilsWithin(Server, slot, perish!.FreshHours / 24f, tolerance: 0.01);
    }

    [ServerScenario]
    public async Task TheHotIngot_CoolsWithTheTime()
    {
        ServerTestPlayer player = await CreateTestPlayerAsync(PlayerNames.Next());
        player.GiveItem("game:ingot-copper", 1);
        ItemSlot slot = SlotOf(player, "game:ingot-copper");
        await Server.Items.AgeAsync(TimeSpan.Zero);

        Server.Items.SetTemperature(slot, 1100);
        Assert.Equal(1100, Server.Items.Temperature(slot), 1);

        await Server.Items.AgeAsync(TimeSpan.FromHours(2));

        Assert.True(Server.Items.Temperature(slot) < 450, $"The ingot cooled to {Server.Items.Temperature(slot)}.");
    }

    [ServerScenario]
    public async Task TheRate_IsTheContainerTimesTheItemsOwn()
    {
        ServerTestPlayer player = await CreateTestPlayerAsync(PlayerNames.Next());
        player.GiveItem(Bread, 1);
        ItemSlot slot = SlotOf(player, Bread);
        await Server.Items.AgeAsync(TimeSpan.Zero);

        float rate = Server.Items.TransitionRate(slot, EnumTransitionType.Perish);
        float container = Server.Items.ContainerTransitionRate(slot, EnumTransitionType.Perish);

        Assert.True(rate > 0, "The bread does not spoil at all.");
        Assert.Equal(container, container, 3);
    }

    private ItemSlot SlotOf(ServerTestPlayer player, string code) => Server.RunOnGameThread(() =>
    {
        IPlayer serverPlayer = player.Player!;
        foreach (string inventory in new[] { "hotbar", "backpack" })
        {
            IInventory? slots = serverPlayer.InventoryManager.GetOwnInventory(inventory);
            if (slots == null) continue;
            foreach (ItemSlot slot in slots)
            {
                if (slot?.Itemstack?.Collectible?.Code.ToString() == code) return slot;
            }
        }

        throw new InvalidOperationException($"The player has no {code}.");
    });
}
