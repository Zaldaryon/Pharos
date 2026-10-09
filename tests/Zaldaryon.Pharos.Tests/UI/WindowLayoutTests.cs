using System.Reflection;
using Xunit;
using Zaldaryon.Pharos.Assertions;
using Zaldaryon.Pharos.Bootstrap;
using Zaldaryon.Pharos.Core;
using Zaldaryon.Pharos.UI;
using Zaldaryon.Pharos.XUnit;

namespace Zaldaryon.Pharos.Tests.UI;

public class WindowLayoutTests
{
    [Theory]
    [InlineData("1280x720", 1280, 720, 1f)]
    [InlineData("1920X1080@1.5", 1920, 1080, 1.5f)]
    public void Parse_ReadsSizeAndScale(string text, int width, int height, float scale) =>
        Assert.Equal(new WindowLayout(width, height, scale), WindowLayout.Parse(text));

    [Theory]
    [InlineData("1280")]
    [InlineData("axb")]
    [InlineData("1280x720@")]
    public void Parse_RefusesWhatIsNotASize(string text) => Assert.Throws<FormatException>(() => WindowLayout.Parse(text));

    [Fact]
    public void Parse_RefusesWindowsBelowTheGamesMinimum() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => WindowLayout.Parse("500x300"));

    [Fact]
    public void WindowSizes_GivesEverySizeAtEveryScale()
    {
        WindowSizesAttribute sizes = new("1280x720", "800x600") { GuiScales = [1f, 1.5f] };
        string[] rows = [.. sizes.GetData(typeof(WindowLayoutTests).GetMethod(nameof(WindowSizes_GivesEverySizeAtEveryScale))!).Select(r => r[0].ToString()!)];

        Assert.Equal(["1280x720@1", "1280x720@1.5", "800x600@1", "800x600@1.5"], rows);
    }

    [Fact]
    public void WindowSizes_KeepsEachSizesOwnScale_WhenNoScalesAreGiven()
    {
        WindowSizesAttribute sizes = new("1280x720@1.5", "800x600");
        string[] rows = [.. sizes.GetData(typeof(WindowLayoutTests).GetMethod(nameof(WindowSizes_KeepsEachSizesOwnScale_WhenNoScalesAreGiven))!).Select(r => r[0].ToString()!)];

        Assert.Equal(["1280x720@1.5", "800x600@1"], rows);
    }

    [WindowSizes("1280x720")]
    [ClientSettingsMatrix("guiScale:1.5")]
    private static void Combined()
    {
    }

    [Fact]
    public void WindowSizes_RefusesToBeCombinedWithASettingsMatrix()
    {
        MethodInfo combined = typeof(WindowLayoutTests).GetMethod(nameof(Combined), BindingFlags.NonPublic | BindingFlags.Static)!;
        Assert.Throws<ArgumentException>(() => new WindowSizesAttribute("1280x720").GetData(combined).ToList());
    }

    [Fact]
    public void SettingsMatrix_TakesKeyValueSettingsBesidePresets()
    {
        ClientSettingsProfile profile = ClientSettingsMatrixAttribute.Profile("guiScale:1.5");
        Assert.Equal("1.5", profile.Values["guiScale"]);
        Assert.Equal("guiScale:1.5", profile.Name);
        Assert.Equal("high", ClientSettingsMatrixAttribute.Profile("high").Name);
    }

    private static GuiElementInfo Button(string key, double x, double y, double w = 100, double h = 30, string composer = "main") =>
        new("D", composer, key, "GuiElementTextButton", x, y, w, h, key, true);

    [Fact]
    public void DialogOnScreen_NamesWhatIsOff_AndSkipsClippedContent()
    {
        DialogLayout fits = new("D", 800, 600, 1, [new ComposerBounds("main", 100, 100, 300, 200)], [Button("a", 120, 120), Button("scrolled", 120, 900) with { IsClipped = true }]);
        PharosAssert.DialogOnScreen(fits);

        DialogLayout spills = fits with { Elements = [Button("a", 120, 120), Button("below", 120, 900)] };
        Assert.Contains("'below'", Assert.Throws<PharosAssertException>(() => PharosAssert.DialogOnScreen(spills)).Message);

        DialogLayout wide = new("D", 800, 600, 1, [new ComposerBounds("main", -100, 100, 1000, 200)], [Button("a", 750, 120)]);
        PharosAssertException error = Assert.Throws<PharosAssertException>(() => PharosAssert.DialogOnScreen(wide));
        Assert.Contains("100 px past the left edge", error.Message);
        Assert.Contains("'a'", error.Message);
    }

    [Fact]
    public void NoOverlappingElements_ComparesButtonsAndText_NotContainers()
    {
        GuiElementInfo background = new("D", "main", "bg", "GuiElementDialogBackground", 0, 0, 500, 500, null, false);
        DialogLayout clean = new("D", 800, 600, 1, [], [background, Button("a", 10, 10), Button("b", 10, 50), Button("c", 10, 10, composer: "other")]);
        PharosAssert.NoOverlappingElements(clean);

        DialogLayout overlap = new("D", 800, 600, 1, [], [Button("a", 10, 10), Button("b", 60, 30)]);
        PharosAssertException error = Assert.Throws<PharosAssertException>(() => PharosAssert.NoOverlappingElements(overlap));
        Assert.Contains("'a' (GuiElementTextButton) and 'b'", error.Message);
        Assert.Contains("50x10 px", error.Message);
    }
}

/// <summary>Window sizes, GUI scales and dialog layout on an engine-mode client booted at 640x360.</summary>
[Collection("Sequential")]
[Trait(PharosTraits.Category, PharosTraits.Live)]
[ServerMods("TestMods/pharoslayoutmod")]
public class LiveWindowLayoutTests : ClientServerScenarioBase
{
    protected override HeadlessClientOptions ClientOptions => new()
    {
        BootMode = ClientBootMode.Engine,
        Width = 640,
        Height = 360,
    };

    [ClientServerScenario]
    public async Task Resize_ChangesWhatTheGameRenders_EvenPastTheScreen()
    {
        Assert.Equal((640, 360), (Client!.Window.CurrentWidth, Client.Window.CurrentHeight));

        await Client.Window.ResizeAsync(1920, 1080);
        FramebufferSnapshot big = Client.CaptureFrame();
        Assert.Equal((1920, 1080), (big.Width, big.Height));
        Assert.True(big.RawRgba.Distinct().Count() > 4, "The resized frame is blank");

        await Client.Window.ResizeAsync(800, 600);
        Assert.Equal((800, 600), (Client.Window.CurrentWidth, Client.Window.CurrentHeight));
        Assert.Equal(800, Client.CaptureFrame().Width);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => Client.Window.ResizeAsync(500, 300));
    }

    [ClientServerScenario]
    public async Task ASizeLeftChanged_IsPutBack_ForTheNextTest_Part1()
    {
        Assert.Equal((640, 360), (Client!.Window.CurrentWidth, Client.Window.CurrentHeight));
        await Client.Window.ResizeAsync(1024, 768);
    }

    [ClientServerScenario]
    public async Task ASizeLeftChanged_IsPutBack_ForTheNextTest_Part2()
    {
        Assert.Equal((640, 360), (Client!.Window.CurrentWidth, Client.Window.CurrentHeight));
        Assert.Equal(640, Client.CaptureFrame().Width);
        await Client.Window.ResizeAsync(1024, 768);
    }

    [ClientTheory]
    [WindowSizes("1280x720", "800x600", GuiScales = [1f, 1.5f])]
    public async Task ADialogThatFits_FitsAtEverySizeAndScale(WindowLayout layout)
    {
        await using IAsyncDisposable _ = await Client!.Window.UseAsync(layout);
        DialogLayout dialog = await OpenAsync("ok");

        Assert.Equal((layout.Width, layout.Height), (dialog.ScreenWidth, dialog.ScreenHeight));
        Assert.Equal(layout.GuiScale, dialog.GuiScale);
        PharosAssert.DialogOnScreen(dialog);
        PharosAssert.NoOverlappingElements(dialog);
    }

    [ClientServerScenario]
    public async Task AWideDialog_FitsAt1280_AndIsOffScreenAt800()
    {
        await Client!.Window.ResizeAsync(1280, 720);
        PharosAssert.DialogOnScreen(await OpenAsync("wide"));

        await Client.Window.ResizeAsync(800, 600);
        PharosAssertException error = Assert.Throws<PharosAssertException>(() => PharosAssert.DialogOnScreen(Client.Ui!.Layout("PharosLayoutDialog")));
        Assert.Contains("past the left edge", error.Message);
        Assert.Contains("past the right edge", error.Message);
    }

    [ClientServerScenario]
    public async Task OverlappingButtons_AreNamed()
    {
        await Client!.Window.ResizeAsync(1280, 720);
        await OpenAsync("overlap");

        PharosAssertException error = Assert.Throws<PharosAssertException>(() => PharosAssert.NoOverlappingElements(Client, "PharosLayoutDialog"));
        Assert.Contains("'first'", error.Message);
        Assert.Contains("'second'", error.Message);
        PharosAssert.DialogOnScreen(Client, "PharosLayoutDialog");
    }

    [ClientServerScenario]
    public async Task TheEscapeMenu_FitsWithoutOverlaps()
    {
        await Client!.Window.ResizeAsync(800, 600);
        Client.Input.PressKey(Vintagestory.API.Client.GlKeys.Escape);
        Assert.True(await StepUntilAsync(() => Client.Ui!.IsOpen("GuiDialogEscapeMenu"), maxFrames: 60), "Escape did not open the menu");
        await Session!.StepFramesAsync(2);

        DialogLayout menu = Client.Ui!.Layout("GuiDialogEscapeMenu");
        Assert.Contains(menu.Elements, e => e.Type == "GuiElementTextButton");
        PharosAssert.DialogOnScreen(menu);
        PharosAssert.NoOverlappingElements(menu);
    }

    private async Task<DialogLayout> OpenAsync(string mode)
    {
        await Client!.Commands.ExecuteSuccessAsync(".pharoslayout open " + mode);
        await Session!.StepFramesAsync(2);
        return Client.Ui!.Layout("PharosLayoutDialog");
    }
}
