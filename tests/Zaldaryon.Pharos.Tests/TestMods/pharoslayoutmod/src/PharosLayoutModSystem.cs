using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;

namespace PharosLayoutMod;

/// <summary>
/// Registers <c>.pharoslayout open ok|wide|overlap|lang-ok|overflow|rtl|rtl-mixed</c>, which opens
/// dialogs to exercise layout, text overflow, and right-to-left checks.
/// </summary>
public sealed class PharosLayoutModSystem : ModSystem
{
    private PharosLayoutDialog? _dialog;

    public override bool ShouldLoad(EnumAppSide forSide) => forSide == EnumAppSide.Client;

    public override void StartClientSide(ICoreClientAPI api)
    {
        IChatCommand command = api.ChatCommands.Create("pharoslayout").WithDescription("Pharos GUI layout test");
        command.BeginSubCommand("open")
            .WithArgs(api.ChatCommands.Parsers.WordRange("mode", "ok", "wide", "overlap", "lang-ok", "overflow", "rtl", "rtl-mixed"))
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

        ElementBounds dialogBounds = ElementBounds.Fixed(EnumDialogArea.CenterMiddle, 0, 0, width, 180);
        ElementBounds background = ElementBounds.Fixed(0, 0, width, 180);

        GuiComposer composer = capi.Gui.CreateCompo("pharoslayout-" + mode, dialogBounds)
            .AddShadedDialogBG(background, false);

        switch (mode)
        {
            case "lang-ok":
                composer.AddStaticText(
                    Lang.Get("pharoslayoutmod:short-label"),
                    CairoFont.WhiteSmallText(),
                    EnumTextOrientation.Left,
                    ElementBounds.Fixed(20, 20, 320, 40),
                    "label");
                break;
            case "overflow":
                composer.AddStaticText(
                    Lang.Get("pharoslayoutmod:long-label"),
                    CairoFont.WhiteSmallText(),
                    EnumTextOrientation.Left,
                    ElementBounds.Fixed(20, 20, 70, 24),
                    "tight-label");
                break;
            case "rtl":
                composer.AddStaticText(
                    Lang.Get("pharoslayoutmod:rtl-label"),
                    CairoFont.WhiteSmallText().WithOrientation(EnumTextOrientation.Right),
                    EnumTextOrientation.Right,
                    ElementBounds.Fixed(20, 20, 320, 40),
                    "rtl-label");
                break;
            case "rtl-mixed":
                composer
                    .AddStaticText(
                        Lang.Get("pharoslayoutmod:rtl-label"),
                        CairoFont.WhiteSmallText().WithOrientation(EnumTextOrientation.Right),
                        EnumTextOrientation.Right,
                        ElementBounds.Fixed(20, 20, 320, 40),
                        "rtl-label")
                    .AddStaticText(
                        Lang.Get("pharoslayoutmod:short-label"),
                        CairoFont.WhiteSmallText(),
                        EnumTextOrientation.Left,
                        ElementBounds.Fixed(20, 70, 320, 40),
                        "left-label");
                break;
            default:
                composer
                    .AddButton("First", () => true, ElementBounds.Fixed(20, 20, 150, 30), EnumButtonStyle.Normal, "first")
                    .AddButton("Second", () => true, ElementBounds.Fixed(secondX, secondY, 150, 30), EnumButtonStyle.Normal, "second");
                break;
        }

        SingleComposer = composer.Compose();
    }

    public override string ToggleKeyCombinationCode => null!;
}
