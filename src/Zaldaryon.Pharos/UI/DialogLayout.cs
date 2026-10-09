namespace Zaldaryon.Pharos.UI;

/// <summary>The on-screen bounds of one of a dialog's composers.</summary>
public sealed record ComposerBounds(string Composer, double X, double Y, double Width, double Height);

/// <summary>
/// An open dialog as it is laid out now: the window it is laid out in, its composers' bounds and
/// its elements' bounds, all in screen pixels.
/// </summary>
/// <param name="Dialog">The dialog's class name.</param>
/// <param name="ScreenWidth">The window's width.</param>
/// <param name="ScreenHeight">The window's height.</param>
/// <param name="GuiScale">The GUI scale it was laid out at.</param>
/// <param name="Composers">Its composers' bounds.</param>
/// <param name="Elements">Its elements, with their bounds.</param>
public sealed record DialogLayout(
    string Dialog,
    int ScreenWidth,
    int ScreenHeight,
    float GuiScale,
    IReadOnlyList<ComposerBounds> Composers,
    IReadOnlyList<GuiElementInfo> Elements)
{
    /// <summary>The element types whose overlap is a layout bug: the ones a player reads or uses.</summary>
    internal static readonly string[] OverlapTypes =
    [
        "GuiElementTextButton", "GuiElementToggleButton", "GuiElementTextInput", "GuiElementNumberInput",
        "GuiElementSwitch", "GuiElementDropDown", "GuiElementStaticText", "GuiElementDynamicText",
    ];

    /// <summary>Whether an element is checked for overlaps by default: buttons, inputs, slot grids and text.</summary>
    public static bool ChecksOverlap(GuiElementInfo element)
    {
        if (element.Width <= 0 || element.Height <= 0) return false;
        if (element.Type.StartsWith("GuiElementItemSlotGrid", StringComparison.Ordinal)) return true;
        if (!OverlapTypes.Contains(element.Type)) return false;
        return !element.Type.EndsWith("Text", StringComparison.Ordinal) || !string.IsNullOrWhiteSpace(element.Text);
    }

    public override string ToString() => $"{Dialog} at {ScreenWidth}x{ScreenHeight}, GUI scale {GuiScale:0.##}";
}
