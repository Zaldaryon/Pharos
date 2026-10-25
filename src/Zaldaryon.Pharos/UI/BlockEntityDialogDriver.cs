using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Zaldaryon.Pharos.Core;

namespace Zaldaryon.Pharos.UI;

/// <summary>A generic driver for a block-entity dialog opened on the client.</summary>
public sealed class BlockEntityDialogDriver
{
    private readonly HeadlessClient _client;
    private readonly BlockPos _pos;

    internal BlockEntityDialogDriver(HeadlessClient client, BlockPos pos)
    {
        _client = client;
        _pos = pos;
    }

    /// <summary>The block position this dialog belongs to.</summary>
    public BlockPos Position => _pos;

    /// <summary>Whether the dialog is open.</summary>
    public bool IsOpen => _client.RunOnClientThread(() =>
        _client.Client.api.Gui.OpenedGuis.Any(d => d.GetType().Name.StartsWith("GuiDialogBlockEntity", StringComparison.Ordinal)));

    /// <summary>Closes the dialog.</summary>
    public void Close() => _client.RunOnClientThread(() =>
    {
        var dialog = _client.Client.api.Gui.OpenedGuis.FirstOrDefault(d => d.GetType().Name.StartsWith("GuiDialogBlockEntity", StringComparison.Ordinal));
        dialog?.TryClose();
    });
}