using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Server;
using Vintagestory.Client.NoObf;
using Xunit;
using Zaldaryon.Pharos.Bootstrap;
using Zaldaryon.Pharos.UI;
using Zaldaryon.Pharos.XUnit;

namespace Zaldaryon.Pharos.Tests.UI;

[Collection("Sequential")]
[ServerWorld(seed: 4242, playStyle: "surviveandbuild")]
public class HudTests : ClientServerScenarioBase
{
    protected override HeadlessClientOptions ClientOptions => new()
    {
        BootMode = ClientBootMode.Engine,
        Width = 1280,
        Height = 900,
    };

    private HudDriver Hud => Client!.Ui!.Hud;

    [ClientServerScenario]
    public async Task Statbars_ReadTheComposedValues_AfterServerSync_AndHideFullOxygen()
    {
        await Hud.WaitForAsync(h => h.Health.Max > 0, 120);
        HudStatbarSnapshot before = Hud.Health;
        IServerPlayer player = Server!.GetClientByPlayername(PlayerName)!.Player;
        Server!.RunOnGameThread(() => player.Entity.ReceiveDamage(new DamageSource
        {
            Source = EnumDamageSource.Internal,
            Type = EnumDamageType.Injury,
        }, 6));
        await Hud.WaitForAsync(h => h.Health.Value <= before.Value - 5.9f, 120);
        Assert.True(Hud.Health.Visible);
        Assert.Equal(before.Max, Hud.Health.Max);
        Assert.Equal(before.Value, before.Max);
        Assert.True(Hud.Satiety.Value > 0);
        Assert.Equal(1500, Hud.Satiety.Max);
        Assert.True(Hud.Satiety.Visible);
        Assert.Equal(Hud.Oxygen.Max, Hud.Oxygen.Value);
        Assert.False(Hud.Oxygen.Visible);

        // Prove the reader inspects the composed bar, not the player's watched attributes.
        try
        {
            Client!.RunOnClientThread(() =>
            {
                var bar = Client.Client.api.Gui.LoadedGuis.OfType<HudStatbar>().Single().Composers["statbar"].GetStatbar("healthstatbar");
                bar.SetValues(3, 0, 24);
                bar.SetFutureValues(8, 1);
                bar.ShouldFlash = true;
            });
            Assert.Equal(3, Hud.Health.Value);
            Assert.Equal(24, Hud.Health.Max);
            Assert.Equal(8, Hud.Health.Future);
            Assert.True(Hud.Health.Flashing);
            Assert.NotEqual(before.Value, Hud.Health.Value);
            Assert.Equal(before.Max, before.Value); // Earlier snapshots remain detached.
        }
        finally
        {
            Client!.RunOnClientThread(() =>
            {
                var bar = Client.Client.api.Gui.LoadedGuis.OfType<HudStatbar>().Single().Composers["statbar"].GetStatbar("healthstatbar");
                bar.SetValues(before.Value, 0, before.Max);
                bar.SetFutureValues(before.Future, 0);
                bar.ShouldFlash = before.Flashing;
            });
        }
    }

    [ClientServerScenario]
    public async Task Hotbar_ReadsTheDisplayedSelection_Name_AndOffhand()
    {
        IServerPlayer player = Server!.GetClientByPlayername(PlayerName)!.Player;
        Server!.RunOnGameThread(() =>
        {
            var inventory = player.InventoryManager.GetHotbarInventory()!;
            inventory[2]!.Itemstack = new ItemStack(((ICoreServerAPI)Server.Api).World.GetItem(new AssetLocation("game:stick")), 7);
            inventory[2]!.MarkDirty();
            inventory[11]!.Itemstack = new ItemStack(((ICoreServerAPI)Server.Api).World.GetItem(new AssetLocation("game:flint")), 2);
            inventory[11]!.MarkDirty();
        });
        await Hud.WaitForAsync(h => h.Hotbar.Slots[2].StackSize == 7 && !h.Hotbar.OffhandSlot.IsEmpty, 120);
        Client!.Inventory.SelectSlot(2);
        await Hud.WaitForAsync(h => h.Hotbar.ActiveSlot == 2 && h.Hotbar.ShownItemName.Length > 0, 60);
        var selected = Hud.Hotbar;
        Assert.Equal("game:stick", selected.Slots[2].ItemCode);
        string expectedName = Client.RunOnClientThread(() => Client.Client.player.InventoryManager.GetHotbarInventory()![2]!.Itemstack!.GetName());
        Assert.Equal(expectedName, selected.ShownItemName);
        Assert.Equal(selected.ActiveSlot, (await Task.Run(() => Hud.Hotbar)).ActiveSlot);
        Assert.True(Client.RunOnClientThread(() => ReferenceEquals(Client.Client.player.Entity.LeftHandItemSlot,
            Client.Client.player.InventoryManager.GetHotbarInventory()![11])));
        Assert.Equal("game:flint", selected.OffhandSlot.ItemCode);
        Assert.NotEmpty(selected.Layout.Elements);
        Client.Inventory.SelectSlot(3);
        await Hud.WaitForAsync(h => h.Hotbar.ActiveSlot == 3 && h.Hotbar.ShownItemName == "", 60);
        Assert.Equal(2, selected.ActiveSlot);
    }

    [ClientServerScenario]
    public async Task SkillAndBackpackHighlights_AndGamemodeVisibility_AreReadFromTheHud()
    {
        IServerPlayer player = Server!.GetClientByPlayername(PlayerName)!.Player;
        Server.RunOnGameThread(() =>
        {
            var inventory = player.InventoryManager.GetHotbarInventory()!;
            inventory[10]!.Itemstack = new ItemStack(((ICoreServerAPI)Server.Api).World.GetItem(new AssetLocation("game:stick")));
            inventory[10]!.MarkDirty();
        });
        await Hud.WaitForAsync(h => !h.Hotbar.Slots[10].IsEmpty, 120);
        Client!.RunOnClientThread(() => Client.Client.player.InventoryManager.ActiveHotbarSlotNumber = 10);
        Assert.Equal(10, Hud.Hotbar.ActiveSlot);
        Client.RunOnClientThread(() => Client.Client.player.InventoryManager.ActiveHotbarSlotNumber = 12);
        Assert.Equal(12, Hud.Hotbar.ActiveSlot);
        Server.RunOnGameThread(() =>
        {
            var inventory = player.InventoryManager.GetHotbarInventory()!;
            inventory[10]!.Itemstack = null;
            inventory[10]!.MarkDirty();
        });
        await Hud.WaitForAsync(h => h.Hotbar.Slots[10].IsEmpty, 120);
        Client.RunOnClientThread(() =>
        {
            Client.Client.player.InventoryManager.ActiveHotbarSlotNumber = 0;
            Client.Client.player.InventoryManager.ActiveHotbarSlotNumber = 12;
        });
        Assert.Equal(12, Hud.Hotbar.ActiveSlot);
        var mode = Client.RunOnClientThread(() => Client.Client.player.WorldData.CurrentGameMode);
        try
        {
            Client.RunOnClientThread(() => Client.Client.player.WorldData.CurrentGameMode = EnumGameMode.Creative);
            Assert.True(Hud.Satiety.Max > 0);
            Assert.True(Hud.Satiety.Visible);
            Client.RunOnClientThread(() => Client.Client.player.WorldData.CurrentGameMode = EnumGameMode.Spectator);
            Assert.False(Hud.Health.Visible);
            Assert.False(Hud.Hotbar.Visible);
            Assert.False(Hud.Element<HudStatbar>().Visible);
        }
        finally
        {
            Client.RunOnClientThread(() =>
            {
                Client.Client.player.WorldData.CurrentGameMode = mode;
                Client.Client.player.InventoryManager.ActiveHotbarSlotNumber = 0;
            });
        }
    }

    [ClientServerScenario]
    public async Task GenericLayouts_IncludeHudElements_AndCoordinatesRespectTheSetting()
    {
        bool original = Client!.RunOnClientThread(() => ClientSettings.ShowCoordinateHud);
        try
        {
            Client!.RunOnClientThread(() => ClientSettings.ShowCoordinateHud = true);
            await Hud.WaitForAsync(h => h.Coordinates.Visible && h.Coordinates.Text.Length > 0, 120);
            var coordinates = Hud.Coordinates;
            Assert.False(coordinates.HiddenBySetting);
            Assert.Contains(",", coordinates.Text);
            var generic = Hud.Element<HudElementCoordinates>();
            Assert.True(generic.Visible);
            Assert.Equal("HudElementCoordinates", generic.Layout.Dialog);
            Assert.NotEmpty(generic.Layout.Composers);
            Assert.Contains(Hud.Elements(), e => e.Type == typeof(HudHotbar).FullName);
            Client.RunOnClientThread(() => ClientSettings.ShowCoordinateHud = false);
            await Hud.WaitForAsync(h => !h.Coordinates.Visible, 20);
            Assert.True(Hud.Coordinates.HiddenBySetting);
            Assert.Equal("", Hud.Coordinates.Text);
            Assert.True(coordinates.Visible);
        }
        finally
        {
            Client.RunOnClientThread(() => ClientSettings.ShowCoordinateHud = original);
        }
    }

    [ClientServerScenario]
    public async Task CustomHudWithoutHudPrefix_HasADetachedLayout_AndClosedState()
    {
        Assert.Throws<InvalidOperationException>(() => Hud.Element<CompassForTest>());
        CompassForTest compass = Client!.RunOnClientThread(() =>
        {
            var element = new CompassForTest(Client.Client.api);
            element.TryOpen();
            return element;
        });
        try
        {
            await Session!.StepFramesAsync(1);
            var snapshot = Hud.Element<CompassForTest>();
            Assert.True(snapshot.Visible);
            Assert.Contains(snapshot.Layout.Elements, e => e.Key == "bearing" && e.Text == "North");
            Assert.Contains(Hud.Elements(), e => e.Type == typeof(CompassForTest).FullName);
            Client.RunOnClientThread(() => compass.SingleComposer.GetDynamicText("bearing").SetNewText("East"));
            Assert.Contains(Hud.Element<CompassForTest>().Layout.Elements, e => e.Text == "East");
            Assert.Contains(snapshot.Layout.Elements, e => e.Text == "North");
            Client.RunOnClientThread(() => compass.TryClose());
            Assert.False(Hud.Element<CompassForTest>().Visible);
        }
        finally
        {
            Client.RunOnClientThread(() => { compass.TryClose(); Client.Client.api.Gui.LoadedGuis.Remove(compass); compass.Dispose(); });
        }
    }

    private sealed class CompassForTest : HudElement
    {
        public CompassForTest(ICoreClientAPI api) : base(api)
        {
            SingleComposer = api.Gui.CreateCompo("pharos-compass", ElementBounds.Fixed(20, 20, 180, 40))
                .AddDynamicText("North", CairoFont.WhiteSmallText(), ElementBounds.Fixed(0, 0, 180, 40), "bearing")
                .Compose();
        }
    }

    [ClientServerScenario]
    public async Task Waits_RespectFrameBudget_Arguments_AndCancellation()
    {
        long before = Client!.FrameController.TotalFrames;
        await Hud.WaitForAsync(_ => true, 0);
        await Assert.ThrowsAsync<TimeoutException>(() => Hud.WaitForAsync(_ => false, 0));
        Assert.Equal(before, Client.FrameController.TotalFrames);
        await Assert.ThrowsAsync<TimeoutException>(() => Hud.WaitForAsync(_ => false, 3));
        Assert.Equal(before + 3, Client.FrameController.TotalFrames);
        await Assert.ThrowsAsync<ArgumentNullException>(() => Hud.WaitForAsync(null!, 1));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => Hud.WaitForAsync(_ => false, -1));
        using var cancel = new CancellationTokenSource();
        cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Hud.WaitForAsync(_ => true, 0, cancel.Token));
    }
}
