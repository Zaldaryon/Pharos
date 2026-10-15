using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.Client;
using Zaldaryon.Pharos.Core;
using Zaldaryon.Pharos.Input;

namespace Zaldaryon.Pharos.UI;

/// <summary>
/// Fills and empties the crafting grid of the survival inventory on an engine-mode client, with the clicks a player makes.
/// </summary>
/// <remarks>
/// <para>
/// The grid is the inventory's 3 by 3 <c>craftinggrid</c>, and its output is the <c>outputslot</c>. The client sends the same slot
/// packets a player's clicks send, so the server applies its own rules: the output is handed over only when the grid matches a recipe,
/// and the ingredients are used up when the output is taken.
/// </para>
/// <para>
/// A click on a hotbar slot picks up its stack, a right click on a grid cell puts one item into it, and a click on the hotbar again
/// puts the rest back. The crafting grid exists in survival and guest mode only, and closing the inventory returns what is in the grid.
/// </para>
/// </remarks>
public sealed class CraftingDriver
{
    private const string InventoryDialog = "GuiDialogInventory";
    private const string GridKey = "craftinggrid";
    private const string OutputKey = "outputslot";
    private const string HotbarDialog = "HudHotbar";
    private const string HotbarGrid = "hotbargrid";
    private const int GridSize = 3;
    private const int HotbarSize = 10;

    private readonly HeadlessClient _client;
    private readonly GuiDriver _gui;

    internal CraftingDriver(HeadlessClient client, GuiDriver gui)
    {
        _client = client;
        _gui = gui;
    }

    /// <summary>Whether the survival inventory, with its crafting grid, is open.</summary>
    public bool IsOpen => _gui.OpenDialogs().Any(d => d.ToggleKey == "inventorydialog");

    /// <summary>The code and quantity of the item on the cursor, or null when the cursor is empty.</summary>
    public (string Code, int Quantity)? Cursor => _client.RunOnClientThread(() =>
    {
        ItemStack? stack = _client.Client.player.InventoryManager.MouseItemSlot.Itemstack;
        if (stack is null) return ((string Code, int Quantity)?)null;
        return ((string Code, int Quantity)?)(stack.Collectible.Code.ToString(), stack.StackSize);
    });

    /// <summary>
    /// Opens the survival inventory with its hotkey and waits until it is open. Does nothing when it is open.
    /// </summary>
    /// <exception cref="InvalidOperationException">The player is not in survival or guest mode, or the hotkey did not open the inventory.</exception>
    public async Task OpenAsync(CancellationToken ct = default)
    {
        if (IsOpen) return;

        EnumGameMode mode = _client.RunOnClientThread(() => _client.Client.player.WorldData.CurrentGameMode);
        if (mode is not (EnumGameMode.Survival or EnumGameMode.Guest))
        {
            throw new InvalidOperationException($"The player is in {mode} mode: the crafting grid is in survival or guest mode only.");
        }

        await _client.Hotkeys.TriggerAsync("inventorydialog", ct).ConfigureAwait(false);
        if (!await _gui.StepUntilAsync(() => IsOpen, 30, ct).ConfigureAwait(false))
        {
            throw new InvalidOperationException("The inventory hotkey did not open the inventory.");
        }

        await _client.StepAsync(ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Puts one item of <paramref name="itemCode"/> from the hotbar into a grid cell. Call it once per item: a cell takes one.
    /// Waits until the cell shows the item and the cursor is empty again.
    /// </summary>
    /// <param name="row">The grid row, from the top: 0 to 2.</param>
    /// <param name="column">The grid column, from the left: 0 to 2.</param>
    /// <param name="itemCode">The item to put in, such as <c>game:stick</c>. The hotbar must hold it.</param>
    /// <exception cref="ArgumentOutOfRangeException">The row or column is outside the 3 by 3 grid.</exception>
    /// <exception cref="InvalidOperationException">The hotbar holds no such item, the cursor is not empty, or the cell did not take it.</exception>
    public async Task PlaceAsync(int row, int column, string itemCode, CancellationToken ct = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(row);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(row, GridSize);
        ArgumentOutOfRangeException.ThrowIfNegative(column);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(column, GridSize);
        ArgumentNullException.ThrowIfNull(itemCode);

        await OpenAsync(ct).ConfigureAwait(false);
        if (Cursor is not null) throw new InvalidOperationException($"The cursor holds {Cursor.Value.Code}: empty it before placing.");
        int hotbar = HotbarSlotOf(itemCode);
        if (hotbar < 0) throw new InvalidOperationException($"The hotbar holds no {itemCode}.");

        // Each click is repeated until its effect shows on the client. A click the client has not finished with is ignored, and the
        // game takes two clicks close together as one gesture, so the steps between clicks matter.
        bool picked = await ClickUntilAsync(() => Cursor is not null, () => _gui.ClickSlot(HotbarDialog, HotbarGrid, hotbar), ct).ConfigureAwait(false);
        if (!picked) throw new InvalidOperationException($"Hotbar slot {hotbar} did not give up {itemCode}.");

        bool placed = await ClickUntilAsync(
            () => GridCell(row, column) == itemCode,
            () => _gui.ClickSlot(InventoryDialog, GridKey, row * GridSize + column, VirtualMouseButton.Right),
            ct).ConfigureAwait(false);
        if (!placed)
        {
            throw new InvalidOperationException($"The grid cell ({row}, {column}) did not take {itemCode}.");
        }

        // What is left on the cursor goes back into its hotbar slot. An empty cursor needs no click, and a click on an empty
        // cursor would pick up the slot's stack instead.
        if (Cursor is not null)
        {
            bool returned = await ClickUntilAsync(() => Cursor is null, () => _gui.ClickSlot(HotbarDialog, HotbarGrid, hotbar), ct).ConfigureAwait(false);
            if (!returned) throw new InvalidOperationException($"Hotbar slot {hotbar} did not take back the rest of {itemCode}.");
        }
    }

    // Clicks, then steps frames until the condition holds, clicking again up to a few times when it does not.
    private async Task<bool> ClickUntilAsync(Func<bool> done, Action click, CancellationToken ct)
    {
        for (int attempt = 0; attempt < 3; attempt++)
        {
            click();
            if (await _gui.StepUntilAsync(done, 30, ct).ConfigureAwait(false)) return true;
        }

        return done();
    }

    /// <summary>
    /// Takes the output of the grid's recipe onto the cursor and puts it into an empty hotbar slot. The ingredients are used up
    /// when the output is taken.
    /// </summary>
    /// <returns>The code and quantity of the output.</returns>
    /// <exception cref="InvalidOperationException">The grid makes no output, the cursor is not empty, or the hotbar has no empty slot.</exception>
    public async Task<(string Code, int Quantity)> TakeOutputAsync(CancellationToken ct = default)
    {
        await OpenAsync(ct).ConfigureAwait(false);
        if (Cursor is not null) throw new InvalidOperationException($"The cursor holds {Cursor.Value.Code}: empty it before taking the output.");
        int empty = EmptyHotbarSlot();
        if (empty < 0) throw new InvalidOperationException("The hotbar has no empty slot for the output.");

        _gui.ClickSlot(InventoryDialog, OutputKey, 0);
        bool taken = await _gui.StepUntilAsync(() => Cursor is not null, 60, ct).ConfigureAwait(false);
        if (!taken) throw new InvalidOperationException("The output slot gave nothing: the grid makes no recipe.");

        (string Code, int Quantity) output = Cursor!.Value;
        _gui.ClickSlot(HotbarDialog, HotbarGrid, empty);
        if (!await _gui.StepUntilAsync(() => Cursor is null, 60, ct).ConfigureAwait(false))
        {
            throw new InvalidOperationException($"Hotbar slot {empty} did not take the output {output.Code}.");
        }

        return output;
    }

    /// <summary>Closes the survival inventory, as its close button does, and steps a frame. The grid's items go back to the player.</summary>
    public async Task CloseAsync(CancellationToken ct = default)
    {
        if (!IsOpen) return;

        _client.Input.PressKey(GlKeys.Escape);
        await _gui.StepUntilAsync(() => !IsOpen, 30, ct).ConfigureAwait(false);
    }

    /// <summary>The code of the item in a grid cell on the client, or null when the cell is empty.</summary>
    public string? GridCell(int row, int column) => _client.RunOnClientThread(() =>
        _client.Client.player.InventoryManager.GetOwnInventory(GridKey)?[row * GridSize + column]?.Itemstack?.Collectible.Code.ToString());

    private int HotbarSlotOf(string itemCode) => _client.RunOnClientThread(() =>
    {
        IInventory hotbar = _client.Client.player.InventoryManager.GetHotbarInventory();
        for (int i = 0; i < HotbarSize; i++)
        {
            if (hotbar[i]?.Itemstack?.Collectible.Code.ToString() == itemCode) return i;
        }

        return -1;
    });

    private int EmptyHotbarSlot() => _client.RunOnClientThread(() =>
    {
        IInventory hotbar = _client.Client.player.InventoryManager.GetHotbarInventory();
        for (int i = 0; i < HotbarSize; i++)
        {
            if (hotbar[i]?.Empty ?? false) return i;
        }

        return -1;
    });
}
