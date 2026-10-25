using Xunit;
using Zaldaryon.Pharos.Assertions;
using Zaldaryon.Pharos.UI;

namespace Zaldaryon.Pharos.Tests.UI;

public class HudLayoutTests
{
    [Fact]
    public void CrossLayoutOverlap_ReportsBothHuds_RegardlessOfComposerName()
    {
        var compass = Layout("Compass", Element("heading", "GuiElementDynamicText", 10, 10, text: "North"));
        var hotbar = Layout("HudHotbar", Element("slots", "GuiElementItemSlotGrid", 50, 10));
        var error = Assert.Throws<PharosAssertException>(() => PharosAssert.NoOverlappingElements(compass, hotbar));
        Assert.Contains("Compass", error.Message);
        Assert.Contains("HudHotbar", error.Message);
        Assert.Contains("heading", error.Message);
        Assert.Contains("slots", error.Message);
        Assert.Contains("60x20", error.Message);
        PharosAssert.NoOverlappingElements(compass, hotbar, tolerance: 60);
        PharosAssert.NoOverlappingElements(compass, hotbar, include: e => e.Key != "heading");
    }

    [Fact]
    public void CrossLayoutOverlap_ChecksStatbars_ButIgnoresBackgroundsAndZeroBounds()
    {
        var compass = Layout("Compass", Element("heading", "GuiElementDynamicText", 10, 10, text: "North"));
        var stats = Layout("HudStatbar", Element("health", "GuiElementStatbar", 10, 10));
        Assert.Throws<PharosAssertException>(() => PharosAssert.NoOverlappingElements(compass, stats));
        var ignored = Layout("Hud", Element("background", "GuiElementDialogBackground", 10, 10),
            Element("empty", "GuiElementDynamicText", 10, 10),
            Element("zero", "GuiElementItemSlotGrid", 10, 10, width: 0));
        PharosAssert.NoOverlappingElements(compass, ignored);
        PharosAssert.NoOverlappingElements(compass, Layout("Hud", Element("other", "GuiElementStatbar", 400, 400)));
    }

    [Fact]
    public void CrossLayoutOverlap_RejectsInvalidToleranceAndCoordinateSpaces()
    {
        var layout = Layout("Hud");
        Assert.Throws<ArgumentNullException>(() => PharosAssert.NoOverlappingElements((DialogLayout)null!, layout));
        Assert.Throws<ArgumentNullException>(() => PharosAssert.NoOverlappingElements(layout, (DialogLayout)null!));
        foreach (double tolerance in new[] { -1d, double.NaN, double.PositiveInfinity })
            Assert.Throws<ArgumentOutOfRangeException>(() => PharosAssert.NoOverlappingElements(layout, layout, tolerance));
        Assert.Throws<ArgumentException>(() => PharosAssert.NoOverlappingElements(layout, layout with { ScreenWidth = 900 }));
        Assert.Throws<ArgumentException>(() => PharosAssert.NoOverlappingElements(layout, layout with { GuiScale = 2 }));
    }

    private static DialogLayout Layout(string name, params GuiElementInfo[] elements) => new(name, 800, 600, 1, [], elements);
    private static GuiElementInfo Element(string key, string type, double x, double y, double width = 100, string? text = null) =>
        new("Hud", key == "slots" ? "other" : "main", key, type, x, y, width, 20, text, false);
}
