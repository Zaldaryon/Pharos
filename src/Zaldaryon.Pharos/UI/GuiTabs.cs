using System.Reflection;
using Vintagestory.API.Client;

namespace Zaldaryon.Pharos.UI;

/// <summary>A tab of a dialog's tab strip, vertical or horizontal.</summary>
/// <param name="Index">The tab's position in the strip, from the top or from the left.</param>
/// <param name="Name">The tab's label, as the game shows it.</param>
/// <param name="Active">
/// Whether the tab is selected. A toggle strip, such as the world map's layer tabs, can have
/// several active tabs; any other strip has one.
/// </param>
/// <param name="DataInt">The number the dialog gave the tab, which its handler reads.</param>
public sealed record GuiTabInfo(int Index, string Name, bool Active, int DataInt);

/// <summary>
/// Reads the game's tab strips and works out where a click selects a tab, from the same geometry
/// their own mouse handlers test.
/// </summary>
internal static class GuiTabs
{
    private const BindingFlags AnyInstance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    private static readonly FieldInfo? s_verticalTabs = typeof(GuiElementVerticalTabs).GetField("tabs", AnyInstance);
    private static readonly FieldInfo? s_verticalWidths = typeof(GuiElementVerticalTabs).GetField("tabWidths", AnyInstance);
    private static readonly FieldInfo? s_verticalHeight = typeof(GuiElementVerticalTabs).GetField("tabHeight", AnyInstance);
    private static readonly FieldInfo? s_verticalSpacing = typeof(GuiElementVerticalTabs).GetField("unscaledTabSpacing", AnyInstance);
    private static readonly FieldInfo? s_horizontalWidths = typeof(GuiElementHorizontalTabs).GetField("tabWidths", AnyInstance);
    private static readonly FieldInfo? s_horizontalScroll = typeof(GuiElementHorizontalTabs).GetField("currentScrollOffset", AnyInstance);
    private static readonly FieldInfo? s_horizontalArrows = typeof(GuiElementHorizontalTabs).GetField("arrowTextures", AnyInstance);
    private static readonly PropertyInfo? s_leftArrowShown = typeof(GuiElementHorizontalTabs).GetProperty("displayLeftArrow", AnyInstance);
    private static readonly PropertyInfo? s_rightArrowShown = typeof(GuiElementHorizontalTabs).GetProperty("displayRightArrow", AnyInstance);

    /// <summary>Whether <paramref name="element"/> is a tab strip Pharos can read.</summary>
    public static bool IsTabStrip(GuiElement element) => element is GuiElementVerticalTabs or GuiElementHorizontalTabs;

    // Client thread.
    public static List<GuiTabInfo> Read(GuiElement element)
    {
        switch (element)
        {
            case GuiElementVerticalTabs vertical:
                GuiTab[] tabs = s_verticalTabs?.GetValue(vertical) as GuiTab[] ?? throw GameChanged();
                return tabs.Select((t, i) => new GuiTabInfo(i, t.Name, t.Active, t.DataInt)).ToList();
            case GuiElementHorizontalTabs horizontal:
                return horizontal.tabs.Select((t, i) => new GuiTabInfo(i, t.Name, i == horizontal.activeElement, t.DataInt)).ToList();
            default:
                throw new InvalidOperationException($"Element {element.GetType().Name} is not a tab strip.");
        }
    }

    /// <summary>The screen point where a click selects tab <paramref name="index"/>. Client thread.</summary>
    /// <exception cref="InvalidOperationException">The tab is scrolled out of view, or the strip is not composed yet.</exception>
    public static (int X, int Y) ClickPoint(GuiElement element, int index)
    {
        ElementBounds bounds = element.Bounds;
        (int x, int y) = element switch
        {
            GuiElementVerticalTabs vertical => VerticalPoint(
                bounds.InnerWidth,
                s_verticalWidths?.GetValue(vertical) as int[] ?? throw GameChanged(),
                (s_verticalTabs?.GetValue(vertical) as GuiTab[] ?? throw GameChanged()).Select(t => t.PaddingTop).ToArray(),
                s_verticalHeight?.GetValue(vertical) as double? ?? throw GameChanged(),
                GuiElement.scaled(s_verticalSpacing?.GetValue(vertical) as double? ?? throw GameChanged()),
                index),
            GuiElementHorizontalTabs horizontal => HorizontalPoint(
                s_horizontalWidths?.GetValue(horizontal) as int[] ?? throw GameChanged(),
                GuiElement.scaled(horizontal.unscaledTabSpacing),
                s_horizontalScroll?.GetValue(horizontal) as double? ?? throw GameChanged(),
                bounds.InnerWidth, bounds.InnerHeight,
                index,
                ArrowWidth(horizontal, 0, s_leftArrowShown),
                ArrowWidth(horizontal, 1, s_rightArrowShown)),
            _ => throw new InvalidOperationException($"Element {element.GetType().Name} is not a tab strip."),
        };

        // The handlers measure from the bounds' whole-pixel corner.
        return ((int)bounds.absX + x, (int)bounds.absY + y);
    }

    /// <summary>
    /// Where tab <paramref name="index"/> of a vertical strip takes a click, relative to the strip's
    /// corner. The strip's handler tests each tab against the right edge, one tab height plus the
    /// spacing per tab, after each tab's own top padding.
    /// </summary>
    internal static (int X, int Y) VerticalPoint(double innerWidth, int[] widths, double[] paddingTops, double tabHeight, double spacing, int index)
    {
        RequireIndex(index, widths.Length);
        if (widths[index] <= 0 || tabHeight <= 0) throw new InvalidOperationException("The tab strip is not composed yet: its tabs have no size.");

        double top = 0;
        for (int i = 0; i < index; i++) top += paddingTops[i] + tabHeight + spacing;
        top += paddingTops[index];

        double right = innerWidth + 1;
        return ((int)(right - widths[index] / 2.0), (int)(top + tabHeight / 2));
    }

    /// <summary>
    /// Where tab <paramref name="index"/> of a horizontal strip takes a click, relative to the
    /// strip's corner. Tabs start one spacing in and follow one another with a spacing between,
    /// shifted left by the strip's scroll offset. A click on a scroll arrow, shown at either end
    /// of a strip too wide for its bounds, scrolls instead: <paramref name="leftArrow"/> and
    /// <paramref name="rightArrow"/> are the widths of the arrows shown, 0 for none.
    /// </summary>
    internal static (int X, int Y) HorizontalPoint(int[] widths, double spacing, double scroll, double innerWidth, double innerHeight, int index, double leftArrow = 0, double rightArrow = 0)
    {
        RequireIndex(index, widths.Length);
        if (widths[index] <= 0 || innerHeight <= 0) throw new InvalidOperationException("The tab strip is not composed yet: its tabs have no size.");

        double left = spacing;
        for (int i = 0; i < index; i++) left += widths[i] + spacing;

        int x = (int)(left + widths[index] / 2.0 - scroll);
        if (x <= leftArrow || x >= innerWidth - rightArrow)
        {
            throw new InvalidOperationException($"Tab {index} is scrolled out of view of its strip.");
        }

        return (x, (int)(innerHeight / 2));
    }

    // The width of a scroll arrow while the strip shows it, else 0.
    private static double ArrowWidth(GuiElementHorizontalTabs tabs, int arrow, PropertyInfo? shown)
    {
        if (shown?.GetValue(tabs) is not true) return 0;
        return s_horizontalArrows?.GetValue(tabs) is LoadedTexture[] arrows && arrows.Length > arrow ? arrows[arrow].Width : throw GameChanged();
    }

    private static void RequireIndex(int index, int count)
    {
        if (index < 0 || index >= count) throw new ArgumentOutOfRangeException(nameof(index), index, $"The strip has {count} tabs.");
    }

    private static InvalidOperationException GameChanged() =>
        new("Pharos cannot read the game's tab strip: the game changed.");
}
