using System.Reflection;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Zaldaryon.Pharos.Input;
using Vintagestory.API.MathTools;
using Vintagestory.Client.NoObf;
using Zaldaryon.Pharos.Core;

namespace Zaldaryon.Pharos.UI;

/// <summary>A diff of one slot before and after a gesture.</summary>
public sealed record SlotDiffEntry(int SlotIndex, string BeforeCode, int BeforeQuantity, string AfterCode, int AfterQuantity);

/// <summary>A diff of all affected slots from a gesture.</summary>
public sealed record SlotDiff(IReadOnlyList<SlotDiffEntry> Entries)
{
    /// <summary>The entries that changed quantity or item code.</summary>
    public IReadOnlyList<SlotDiffEntry> Changed => Entries.Where(e =>
        e.BeforeQuantity != e.AfterQuantity || e.BeforeCode != e.AfterCode).ToList();
}

/// <summary>Drives inventory gestures through real mouse input.</summary>
public sealed class SlotsDriver
{
    private readonly HeadlessClient _client;
    private static readonly FieldInfo? s_interactive = typeof(GuiComposer).GetField("interactiveElements", BindingFlags.Instance | BindingFlags.NonPublic);

    internal SlotsDriver(HeadlessClient client) => _client = client;

    /// <summary>Clicks a slot with the given button and modifiers through real mouse input.</summary>
    /// <remarks>Modifier keys are restored in a finally block even if the click fails.</remarks>
    public async Task<SlotDiff> ClickAsync(int slotIndex, VirtualMouseButton button = VirtualMouseButton.Left,
        bool shift = false, bool ctrl = false, bool alt = false, int maxFrames = 60)
    {
        var before = CaptureAll();
        var bounds = FindSlotBounds(slotIndex)
            ?? throw new InvalidOperationException($"Slot {slotIndex} not found in any open inventory dialog.");

        if (shift) _client.Input.InjectKey(GlKeys.LShift, true);
        if (ctrl) _client.Input.InjectKey(GlKeys.LControl, true);
        if (alt) _client.Input.InjectKey(GlKeys.LAlt, true);
        try
        {
            int x = (int)(bounds.absX + bounds.InnerWidth / 2);
            int y = (int)(bounds.absY + bounds.InnerHeight / 2);
            _client.Input.Click(x, y, button);
        }
        finally
        {
            if (shift) _client.Input.InjectKey(GlKeys.LShift, false);
            if (ctrl) _client.Input.InjectKey(GlKeys.LControl, false);
            if (alt) _client.Input.InjectKey(GlKeys.LAlt, false);
        }
        await _client.StepAsync(default).ConfigureAwait(false);
        var after = CaptureAll();
        return Diff(before, after);
    }

    /// <summary>Scrolls the wheel on a slot, moving items one at a time.</summary>
    public async Task<SlotDiff> WheelAsync(int slotIndex, int steps, int maxFrames = 60)
    {
        var before = CaptureAll();
        var bounds = FindSlotBounds(slotIndex)
            ?? throw new InvalidOperationException($"Slot {slotIndex} not found in any open inventory dialog.");
        int x = (int)(bounds.absX + bounds.InnerWidth / 2);
        int y = (int)(bounds.absY + bounds.InnerHeight / 2);
        _client.Input.InjectMouseMove(x, y);
        _client.Input.InjectScroll(steps * 120);
        await _client.StepAsync(default).ConfigureAwait(false);
        var after = CaptureAll();
        return Diff(before, after);
    }

    /// <summary>Double-clicks a slot to collect matching stacks.</summary>
    public async Task<SlotDiff> DoubleClickAsync(int slotIndex, int maxFrames = 60)
    {
        var before = CaptureAll();
        var bounds = FindSlotBounds(slotIndex)
            ?? throw new InvalidOperationException($"Slot {slotIndex} not found in any open inventory dialog.");
        int x = (int)(bounds.absX + bounds.InnerWidth / 2);
        int y = (int)(bounds.absY + bounds.InnerHeight / 2);
        _client.Input.Click(x, y, VirtualMouseButton.Left);
        await _client.StepAsync(default).ConfigureAwait(false);
        _client.Input.Click(x, y, VirtualMouseButton.Left);
        await _client.StepAsync(default).ConfigureAwait(false);
        var after = CaptureAll();
        return Diff(before, after);
    }

    /// <summary>Presses a number key to swap a slot with a hotbar slot.</summary>
    public async Task<SlotDiff> HotbarSwapAsync(int slotIndex, int hotbarIndex, int maxFrames = 60)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(hotbarIndex);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(hotbarIndex, 9);
        var before = CaptureAll();
        var bounds = FindSlotBounds(slotIndex)
            ?? throw new InvalidOperationException($"Slot {slotIndex} not found in any open inventory dialog.");
        int x = (int)(bounds.absX + bounds.InnerWidth / 2);
        int y = (int)(bounds.absY + bounds.InnerHeight / 2);
        _client.Input.InjectMouseMove(x, y);
        var key = hotbarIndex switch
        {
            0 => GlKeys.Number1, 1 => GlKeys.Number2, 2 => GlKeys.Number3,
            3 => GlKeys.Number4, 4 => GlKeys.Number5, 5 => GlKeys.Number6,
            6 => GlKeys.Number7, 7 => GlKeys.Number8, 8 => GlKeys.Number9,
            _ => GlKeys.Number0
        };
        _client.Input.PressKey(key);
        await _client.StepAsync(default).ConfigureAwait(false);
        var after = CaptureAll();
        return Diff(before, after);
    }

    /// <summary>Drops one or all items from a slot (Q or Ctrl+Q).</summary>
    public async Task<SlotDiff> DropAsync(int slotIndex, bool all = false, int maxFrames = 60)
    {
        var before = CaptureAll();
        var bounds = FindSlotBounds(slotIndex)
            ?? throw new InvalidOperationException($"Slot {slotIndex} not found in any open inventory dialog.");
        int x = (int)(bounds.absX + bounds.InnerWidth / 2);
        int y = (int)(bounds.absY + bounds.InnerHeight / 2);
        _client.Input.InjectMouseMove(x, y);
        if (all) _client.Input.InjectKey(GlKeys.LControl, true);
        try { _client.Input.PressKey(GlKeys.Q); }
        finally { if (all) _client.Input.InjectKey(GlKeys.LControl, false); }
        await _client.StepAsync(default).ConfigureAwait(false);
        var after = CaptureAll();
        return Diff(before, after);
    }

    /// <summary>Captures the player's hotbar inventory slot at the given index.</summary>
    public (string? Code, int Quantity) Backpack(int index) => _client.RunOnClientThread(() =>
    {
        var inv = _client.Client.player?.InventoryManager?.GetHotbarInventory();
        if (inv == null || index >= inv.Count) return (null, 0);
        var slot = inv[index];
        return (slot?.Itemstack?.Item?.Code?.ToString() ?? slot?.Itemstack?.Block?.Code?.ToString(), slot?.StackSize ?? 0);
    });

    private ElementBounds? FindSlotBounds(int globalIndex) => _client.RunOnClientThread(() =>
    {
        int seen = 0;
        foreach (var dialog in _client.Client.api.Gui.OpenedGuis)
        {
            foreach (var c in (IEnumerable<KeyValuePair<string, GuiComposer>>)dialog.Composers)
            {
                var interactive = s_interactive?.GetValue(c.Value) as Dictionary<string, GuiElement>;
                if (interactive == null) continue;
                foreach (var e in interactive.Values)
                {
                    if (e is GuiElementItemSlotGridBase grid)
                    {
                        for (int i = 0; i < grid.SlotBounds.Length; i++)
                        {
                            if (seen == globalIndex)
                                return grid.SlotBounds[i];
                            seen++;
                        }
                    }
                }
            }
        }
        return null;
    });

    private List<(int Index, string? Code, int Qty)> CaptureAll() => _client.RunOnClientThread(() =>
    {
        var result = new List<(int, string?, int)>();
        int seen = 0;
        foreach (var dialog in _client.Client.api.Gui.OpenedGuis)
        {
            foreach (var c in (IEnumerable<KeyValuePair<string, GuiComposer>>)dialog.Composers)
            {
                var interactive = s_interactive?.GetValue(c.Value) as Dictionary<string, GuiElement>;
                if (interactive == null) continue;
                foreach (var e in interactive.Values)
                {
                    if (e is GuiElementItemSlotGridBase grid)
                    {
                        foreach (var slot in grid.availableSlots.Values)
                        {
                            result.Add((seen++, slot?.Itemstack?.Item?.Code?.ToString() ?? slot?.Itemstack?.Block?.Code?.ToString(),
                                slot?.StackSize ?? 0));
                        }
                    }
                }
            }
        }
        return result;
    });

    private static SlotDiff Diff(List<(int Index, string? Code, int Qty)> before, List<(int Index, string? Code, int Qty)> after)
    {
        var entries = new List<SlotDiffEntry>();
        var afterDict = after.ToDictionary(x => x.Index);
        foreach (var b in before)
        {
            afterDict.TryGetValue(b.Index, out var a);
            entries.Add(new SlotDiffEntry(b.Index, b.Code ?? "", b.Qty, a.Code ?? "", a.Qty));
        }
        return new SlotDiff(entries);
    }
}