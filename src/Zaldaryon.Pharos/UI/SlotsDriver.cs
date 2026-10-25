using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Zaldaryon.Pharos.Input;
using Vintagestory.API.MathTools;
using Zaldaryon.Pharos.Core;

namespace Zaldaryon.Pharos.UI;

/// <summary>A diff of one slot before and after a gesture.</summary>
public sealed record SlotDiffEntry(int SlotIndex, string BeforeCode, int BeforeQuantity, string AfterCode, int AfterQuantity);

/// <summary>Drives inventory gestures through real mouse input.</summary>
public sealed class SlotsDriver
{
    private readonly HeadlessClient _client;

    internal SlotsDriver(HeadlessClient client) => _client = client;

    /// <summary>Clicks a slot with the given button and modifiers.</summary>
    public async Task<SlotDiffEntry?> ClickAsync(int slotIndex, VirtualMouseButton button = VirtualMouseButton.Left, bool shift = false, bool ctrl = false, bool alt = false, int maxFrames = 60)
    {
        var before = Capture(slotIndex);
        if (shift) _client.Input.InjectKey(GlKeys.LShift, true);
        if (ctrl) _client.Input.InjectKey(GlKeys.LControl, true);
        if (alt) _client.Input.InjectKey(GlKeys.LAlt, true);
        _client.Input.Click(0, 0, button); // placeholder: real position from slot bounds needed
        if (shift) _client.Input.InjectKey(GlKeys.LShift, false);
        if (ctrl) _client.Input.InjectKey(GlKeys.LControl, false);
        if (alt) _client.Input.InjectKey(GlKeys.LAlt, false);
        await _client.StepAsync(default).ConfigureAwait(false);
        var after = Capture(slotIndex);
        return new SlotDiffEntry(slotIndex, before.Code, before.Qty, after.Code, after.Qty);
    }

    private (string Code, int Qty) Capture(int slotIndex) => _client.RunOnClientThread(() =>
    {
        // Placeholder: read from first open inventory
        return ("", 0);
    });
}