using Vintagestory.API.Client;
using Vintagestory.Client;
using Zaldaryon.Pharos.Core;
using Zaldaryon.Pharos.Input;
using Zaldaryon.Pharos.UI;

namespace Zaldaryon.Pharos.Player;

/// <summary>
/// Inventory actions on an engine-mode client, performed through the GUI the way a player does.
/// </summary>
/// <remarks>
/// Selecting a hotbar slot presses its number key. Moving a stack opens the inventory with its
/// hotkey, clicks the stack in the hotbar to pick it up and clicks the target slot to put it
/// down, so the client sends the same slot packets as for a player and the server applies its own
/// rules to the move. The result reaches the server over the next ticks, which the caller steps.
/// </remarks>
internal sealed class LiveInventory
{
    private const string HotbarDialog = "HudHotbar";
    private const string HotbarGrid = "hotbargrid";

    private readonly HeadlessClient _client;
    private readonly GuiDriver _gui;

    public LiveInventory(HeadlessClient client, GuiDriver gui)
    {
        _client = client;
        _gui = gui;
    }

    public bool SelectHotbarSlot(int slotIndex)
    {
        if (slotIndex < 0 || slotIndex > 9) return false;

        // The number row selects slots 1 to 10, with 0 for the tenth.
        _client.Input.PressKey(slotIndex == 9 ? GlKeys.Number0 : GlKeys.Number1 + slotIndex);
        return true;
    }

    public bool MoveHotbarStack(int fromSlot, int toSlot)
    {
        if (fromSlot is < 0 or > 9 || toSlot is < 0 or > 9) return false;

        bool openedHere = !InventoryOpen();
        if (openedHere) ToggleInventory();

        try
        {
            _gui.ClickSlot(HotbarDialog, HotbarGrid, fromSlot);
            _gui.ClickSlot(HotbarDialog, HotbarGrid, toSlot);
            return true;
        }
        finally
        {
            if (openedHere) ToggleInventory();
        }
    }

    private bool InventoryOpen() =>
        _gui.OpenDialogs().Any(d => d.ToggleKey == "inventorydialog");

    private void ToggleInventory()
    {
        int keyCode = _client.RunOnClientThread(() => ScreenManager.hotkeyManager.HotKeys["inventorydialog"].CurrentMapping.KeyCode);
        _client.Input.PressKey((GlKeys)keyCode);
    }
}
