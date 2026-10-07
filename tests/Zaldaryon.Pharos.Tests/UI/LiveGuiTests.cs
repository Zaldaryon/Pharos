using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Server;
using Xunit;
using Zaldaryon.Pharos.Bootstrap;
using Zaldaryon.Pharos.UI;
using Zaldaryon.Pharos.XUnit;

namespace Zaldaryon.Pharos.Tests.UI;

[Collection("Sequential")]
public class LiveGuiTests : ClientServerScenarioBase
{
    protected override HeadlessClientOptions ClientOptions => new()
    {
        BootMode = ClientBootMode.Engine,
        Width = 1024,
        Height = 768,
    };

    private GuiDriver Ui => Client!.Ui!;

    [ClientServerScenario]
    public async Task EscapeMenu_ListsItsButtonsAndClosesOnBackToGame()
    {
        await Session!.StepFramesAsync(20);

        Client!.Input.PressKey(GlKeys.Escape);
        Assert.True(await StepUntilAsync(() => Ui.IsOpen("GuiDialogEscapeMenu"), maxFrames: 60), "Escape did not open the menu");
        Assert.Contains("GuiDialogEscapeMenu", Client.Gui.GetOpenDialogs());

        string backLabel = Client.RunOnClientThread(() => Lang.Get("pause-back2game"));
        GuiElementInfo back = Assert.Single(Ui.Elements("GuiDialogEscapeMenu"), e => e.Text == backLabel && e.IsInteractive && e.Type == "GuiElementTextButton");
        Assert.True(back.Width > 0 && back.Height > 0);

        Ui.Click("GuiDialogEscapeMenu", back.Key);
        Assert.True(await StepUntilAsync(() => !Ui.IsOpen("GuiDialogEscapeMenu"), maxFrames: 60), "Clicking the button did not close the menu");
    }

    [ClientServerScenario]
    public async Task TypeInto_FillsTheCreativeInventorySearchBox()
    {
        await Session!.StepFramesAsync(20);

        Client!.Input.PressKey(GlKeys.E);
        Assert.True(await StepUntilAsync(() => Ui.IsOpen("GuiDialogInventory"), maxFrames: 60), "E did not open the inventory");
        await Session.StepFramesAsync(5);

        Ui.TypeInto("GuiDialogInventory", "searchbox", "granite");
        await Session.StepFramesAsync(2);

        Assert.Equal("granite", Ui.Find("GuiDialogInventory", "searchbox")!.Text);
    }

    [ClientServerScenario]
    public async Task Inventory_SelectsAndMovesHotbarStacksThroughTheGui()
    {
        await Session!.StepFramesAsync(20);
        IServerPlayer serverPlayer = Server!.GetClientByPlayername(PlayerName).Player;
        ServerHost!.RunOnGameThread(() =>
        {
            ItemSlot slot = serverPlayer.InventoryManager.GetHotbarInventory()[0];
            slot.Itemstack = new ItemStack(((ICoreServerAPI)Server.Api).World.GetItem(new AssetLocation("game:stick")), 7);
            slot.MarkDirty();
        });
        await StepUntilAsync(() => Client!.Inventory.GetSlotContents(0).StackSize == 7, maxFrames: 120);

        Client!.Inventory.SelectSlot(4);
        Assert.True(await StepUntilAsync(() => serverPlayer.InventoryManager.ActiveHotbarSlotNumber == 4, maxFrames: 120),
            "The server never saw the selected hotbar slot");
        Assert.Equal(4, Client.Inventory.SelectedSlotIndex);

        Assert.True(Client.Inventory.DragSlot(0, 3));
        bool moved = await StepUntilAsync(
            () => ServerHost.RunOnGameThread(() => serverPlayer.InventoryManager.GetHotbarInventory()[3].StackSize == 7
                && serverPlayer.InventoryManager.GetHotbarInventory()[0].Empty),
            maxFrames: 120);

        Assert.True(moved, "The server never applied the stack move");
        Assert.Equal("game:stick", Client.Inventory.GetSlotContents(3).ItemCode);
    }
}
