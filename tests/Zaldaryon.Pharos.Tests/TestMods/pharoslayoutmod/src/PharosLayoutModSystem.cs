using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace PharosLayoutMod;

/// <summary>
/// Registers <c>.pharoslayout open ok|wide|overlap</c>, which opens a dialog that fits, a dialog
/// 1000 pixels wide, or a dialog whose two buttons overlap.
/// </summary>
public sealed class PharosLayoutModSystem : ModSystem
{
    private PharosLayoutDialog? _dialog;

    public override bool ShouldLoad(EnumAppSide forSide) => forSide == EnumAppSide.Client;

    public override void StartClientSide(ICoreClientAPI api)
    {
        IChatCommand command = api.ChatCommands.Create("pharoslayout").WithDescription("Pharos GUI layout test");
        command.BeginSubCommand("open")
            .WithArgs(api.ChatCommands.Parsers.WordRange("mode", "ok", "wide", "overlap"))
            .HandleWith(args =>
            {
                _dialog?.TryClose();
                _dialog = new PharosLayoutDialog(api, (string)args[0]);
                _dialog.TryOpen();
                return TextCommandResult.Success();
            })
            .EndSubCommand();
    }
}

public sealed class PharosLayoutDialog : GuiDialog
{
    public PharosLayoutDialog(ICoreClientAPI capi, string mode) : base(capi)
    {
        double width = mode == "wide" ? 1000 : 400;
        double secondX = mode == "overlap" ? 60 : 20;
        double secondY = mode == "overlap" ? 40 : 80;

        ElementBounds dialogBounds = ElementBounds.Fixed(EnumDialogArea.CenterMiddle, 0, 0, width, 160);
        ElementBounds background = ElementBounds.Fixed(0, 0, width, 160);
        SingleComposer = capi.Gui.CreateCompo("pharoslayout-" + mode, dialogBounds)
            .AddShadedDialogBG(background, false)
            .AddButton("First", () => true, ElementBounds.Fixed(20, 20, 150, 30), EnumButtonStyle.Normal, "first")
            .AddButton("Second", () => true, ElementBounds.Fixed(secondX, secondY, 150, 30), EnumButtonStyle.Normal, "second")
            .Compose();
    }

    public override string ToggleKeyCombinationCode => null!;
}
