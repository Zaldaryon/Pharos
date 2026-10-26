using System.Reflection;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.Client.NoObf;
using Zaldaryon.Pharos.Core;
using Zaldaryon.Pharos.Input;
using Zaldaryon.Pharos.Player;

namespace Zaldaryon.Pharos.UI;

/// <summary>Captures an inventory slot's state when the dialog was read.</summary>
public sealed record DialogSlotSnapshot(int Index, string? ItemCode, int StackSize, bool IsEmpty);

/// <summary>A generic driver for a block-entity dialog opened on the client.</summary>
public class BlockEntityDialogDriver
{
    private readonly HeadlessClient _client;
    private readonly BlockPos _pos;

    internal BlockEntityDialogDriver(HeadlessClient client, BlockPos pos)
    {
        _client = client;
        _pos = pos.Copy();
    }

    /// <summary>A defensive copy of the block position this dialog belongs to.</summary>
    public BlockPos Position => _pos.Copy();

    /// <summary>The dialog's inventory, or null if not available.</summary>
    protected InventoryBase? Inventory => _client.RunOnClientThread(() => FindDialog()?.Inventory);

    /// <summary>Whether the dialog is open.</summary>
    public bool IsOpen => _client.RunOnClientThread(() => FindDialog() != null);

    /// <summary>The slot grids visible in this dialog by their element keys.</summary>
    public IReadOnlyList<string> SlotGrids => _client.RunOnClientThread(() =>
    {
        GuiDialogBlockEntity? dialog = FindDialog();
        if (dialog == null) return [];
        var grids = new List<string>();
        foreach (var c in (IEnumerable<KeyValuePair<string, GuiComposer>>)dialog.Composers)
        {
            var interactive = typeof(GuiComposer).GetField("interactiveElements", BindingFlags.Instance | BindingFlags.NonPublic)
                ?.GetValue(c.Value) as Dictionary<string, GuiElement>;
            if (interactive == null) continue;
            foreach (var e in interactive.Where(kv => kv.Value is GuiElementItemSlotGridBase))
                grids.Add(e.Key);
        }
        return grids;
    });

    /// <summary>Captures all slots visible in the dialog's first slot grid.</summary>
    public IReadOnlyList<DialogSlotSnapshot> Slots => _client.RunOnClientThread(() =>
    {
        GuiDialogBlockEntity? dialog = FindDialog();
        if (dialog?.Inventory == null) return [];
        return dialog.Inventory.Select((slot, i) => new DialogSlotSnapshot(
            i, slot?.Itemstack?.Item?.Code?.ToString() ?? slot?.Itemstack?.Block?.Code?.ToString(),
            slot?.Itemstack?.StackSize ?? 0, slot?.Empty ?? true)).ToList();
    });

    /// <summary>Clicks a slot in the dialog's inventory through real mouse input.</summary>
    public async Task ClickSlotAsync(int slotIndex, VirtualMouseButton button = VirtualMouseButton.Left,
        bool shift = false, bool ctrl = false, bool alt = false, int maxFrames = 60)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(slotIndex);
        _client.RunOnClientThread(() =>
        {
            GuiDialogBlockEntity? dialog = FindDialog();
            if (dialog == null)
                throw new InvalidOperationException($"No block entity dialog is open at {_pos}. Open the dialog first.");
            var grid = FindSlotGrid(dialog)
                ?? throw new InvalidOperationException("No slot grid found in the dialog.");
            var slots = grid.availableSlots;
            if (slotIndex >= slots.Count)
                throw new ArgumentOutOfRangeException(nameof(slotIndex), $"Slot index {slotIndex} exceeds available slots ({slots.Count}).");
            var bounds = grid.SlotBounds[slotIndex];
            int x = (int)(bounds.absX + bounds.InnerWidth / 2);
            int y = (int)(bounds.absY + bounds.InnerHeight / 2);
            if (shift) _client.Input.InjectKey(GlKeys.LShift, true);
            if (ctrl) _client.Input.InjectKey(GlKeys.LControl, true);
            if (alt) _client.Input.InjectKey(GlKeys.LAlt, true);
            try { _client.Input.Click(x, y, button); }
            finally
            {
                if (shift) _client.Input.InjectKey(GlKeys.LShift, false);
                if (ctrl) _client.Input.InjectKey(GlKeys.LControl, false);
                if (alt) _client.Input.InjectKey(GlKeys.LAlt, false);
            }
        });
        await _client.StepAsync(default).ConfigureAwait(false);
    }

    /// <summary>Shift-clicks a slot to move its contents to the player's inventory.</summary>
    public Task ShiftClickAsync(int slotIndex, int maxFrames = 60) =>
        ClickSlotAsync(slotIndex, VirtualMouseButton.Left, shift: true, maxFrames: maxFrames);

    /// <summary>Closes the dialog.</summary>
    public void Close() => _client.RunOnClientThread(() => FindDialog()?.TryClose());

    /// <summary>Finds the open dialog for this position.</summary>
    protected GuiDialogBlockEntity? FindDialog() =>
        _client.Client.api.Gui.OpenedGuis.OfType<GuiDialogBlockEntity>()
            .FirstOrDefault(d => d.BlockEntityPosition?.Equals(_pos) == true);

    /// <summary>Finds the first slot grid in the dialog.</summary>
    protected static GuiElementItemSlotGridBase? FindSlotGrid(GuiDialogBlockEntity dialog)
    {
        foreach (var c in (IEnumerable<KeyValuePair<string, GuiComposer>>)dialog.Composers)
        {
            var interactive = typeof(GuiComposer).GetField("interactiveElements", BindingFlags.Instance | BindingFlags.NonPublic)
                ?.GetValue(c.Value) as Dictionary<string, GuiElement>;
            if (interactive == null) continue;
            foreach (var e in interactive.Values)
            {
                if (e is GuiElementItemSlotGridBase grid && grid.availableSlots.Count > 0)
                    return grid;
            }
        }
        return null;
    }
}

/// <summary>Driver for firepit-like dialogs with fuel, input, output and temperature.</summary>
public sealed class FirepitDialogDriver : BlockEntityDialogDriver
{
    internal FirepitDialogDriver(HeadlessClient client, BlockPos pos) : base(client, pos) { }

    /// <summary>The shown temperature text from the dialog.</summary>
    public string? ShownTemperature => Client.RunOnClientThread(() =>
    {
        var dialog = FindDialog();
        if (dialog == null) return null;
        foreach (var c in (IEnumerable<KeyValuePair<string, GuiComposer>>)dialog.Composers)
        {
            var statics = typeof(GuiComposer).GetField("staticElements", BindingFlags.Instance | BindingFlags.NonPublic)
                ?.GetValue(c.Value) as Dictionary<string, GuiElement>;
            if (statics == null) continue;
            foreach (var e in statics.Values.OfType<GuiElementDynamicText>())
            {
                string text = e.GetText();
                if (text.Contains("°") || text.ToLowerInvariant().Contains("temp"))
                    return text;
            }
        }
        return null;
    });

    private HeadlessClient Client => (HeadlessClient)typeof(BlockEntityDialogDriver)
        .GetField("_client", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(this)!;
}

/// <summary>Driver for barrel dialogs with seal and liquid.</summary>
public sealed class BarrelDialogDriver : BlockEntityDialogDriver
{
    internal BarrelDialogDriver(HeadlessClient client, BlockPos pos) : base(client, pos) { }

    /// <summary>Whether the barrel can be sealed (seal button exists and is enabled).</summary>
    public bool CanSeal => Client.RunOnClientThread(() =>
    {
        var dialog = FindDialog();
        if (dialog == null) return false;
        foreach (var c in (IEnumerable<KeyValuePair<string, GuiComposer>>)dialog.Composers)
        {
            if (c.Value.GetToggleButton("seal") is { } btn)
                return btn.Enabled;
        }
        return false;
    });

    /// <summary>Clicks the seal button if it exists.</summary>
    public async Task SealAsync(int maxFrames = 60)
    {
        Client.RunOnClientThread(() =>
        {
            var dialog = FindDialog()
                ?? throw new InvalidOperationException("No barrel dialog is open.");
            foreach (var c in (IEnumerable<KeyValuePair<string, GuiComposer>>)dialog.Composers)
            {
                if (c.Value.GetToggleButton("seal") is { Enabled: true } btn)
                {
                    var bounds = btn.Bounds;
                    int x = (int)(bounds.absX + bounds.InnerWidth / 2);
                    int y = (int)(bounds.absY + bounds.InnerHeight / 2);
                    Client.Input.Click(x, y, VirtualMouseButton.Left);
                    return;
                }
            }
            throw new InvalidOperationException("No seal button found or it is disabled.");
        });
        await Client.StepAsync(default).ConfigureAwait(false);
    }

    private HeadlessClient Client => (HeadlessClient)typeof(BlockEntityDialogDriver)
        .GetField("_client", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(this)!;
}