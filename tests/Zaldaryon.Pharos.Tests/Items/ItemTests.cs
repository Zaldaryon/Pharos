using Vintagestory.API.Common;
using Xunit;
using Zaldaryon.Pharos.Bootstrap;
using Zaldaryon.Pharos.Core;
using Zaldaryon.Pharos.Visual;
using Zaldaryon.Pharos.XUnit;

namespace Zaldaryon.Pharos.Tests.Items;

public class ItemTooltipTests
{
    [Fact]
    public void PlainText_DropsTheMarkup_AndLines_DropTrailingBlankLines()
    {
        ItemTooltip tooltip = new("Flint axe", "Durability: 60 / 60\n<font color=\"#aaa\">Code: game:axe-flint</font>\n\n");

        Assert.Equal("Durability: 60 / 60\nCode: game:axe-flint\n\n", tooltip.PlainText);
        Assert.Equal(["Durability: 60 / 60", "Code: game:axe-flint"], tooltip.Lines);
        Assert.True(tooltip.Contains("Code: game:axe-flint"));
        Assert.True(tooltip.Contains("Flint axe"));
    }

    [Fact]
    public void PlainText_KeepsComparisons_BreaksLines_AndDecodesEntities()
    {
        ItemTooltip tooltip = new("Spear", "Damage < 5, range > 3<br>Mode: <a href=\"handbook://x\">thrust</a> &amp; parry");

        Assert.Equal("Damage < 5, range > 3\nMode: thrust & parry", tooltip.PlainText);
        Assert.Equal("Damage < 5, range > 3", (tooltip with { Text = "Damage < 5, range > 3" }).PlainText);
    }

    [Fact]
    public void ASnapshot_SavedWithHalfAlpha_ReadsBackUnchanged()
    {
        string path = Path.Combine(Path.GetTempPath(), "pharos-alpha-" + Guid.NewGuid().ToString("N")[..8] + ".png");
        try
        {
            byte[] pixels = [200, 100, 50, 128, 10, 20, 30, 255, 0, 0, 0, 0, 255, 255, 255, 64];
            new FramebufferSnapshot(pixels, 2, 2).SaveToPng(path);

            Assert.Equal(pixels, FramebufferSnapshot.FromFile(path).RawRgba);
        }
        catch (Exception ex) when (ex is DllNotFoundException or TypeInitializationException)
        {
            // SkiaSharp's native library is missing on this machine.
        }
        finally
        {
            File.Delete(path);
        }
    }
}

/// <summary>
/// Tooltips and icons on an engine-mode client, with vanilla items and <c>TestMods/pharositemmod</c>:
/// a sword with 500 durability and a crate block.
/// </summary>
[Collection("Sequential")]
[Trait(PharosTraits.Category, PharosTraits.Live)]
[ServerMods("TestMods/pharositemmod")]
public class LiveItemTests : ClientServerScenarioBase
{
    protected override HeadlessClientOptions ClientOptions => new()
    {
        BootMode = ClientBootMode.Engine,
        Width = 640,
        Height = 360,
    };

    [ClientServerScenario]
    public void Tooltip_IsTheGamesOwn_TitleAndAttributeLines()
    {
        ItemStack axe = Client!.Items.Stack("game:axe-flint");
        ItemTooltip tooltip = Client.Items.Tooltip(axe);

        Assert.Equal("Flint axe", tooltip.Title);
        Assert.Contains("Durability: 60 / 60", tooltip.Lines);
        Assert.Contains("Code: game:axe-flint", Client.Items.Tooltip(axe, extendedInfo: true).PlainText);

        Assert.Contains("When eaten:", Client.Items.Tooltip(Client.Items.Stack("game:fruit-cranberry")).PlainText);

        ItemTooltip sword = Client.Items.Tooltip(Client.Items.Stack("pharositemmod:sword-iron"));
        Assert.Equal("Pharos iron sword", sword.Title);
        Assert.Contains("Durability: 500 / 500", sword.Lines);
    }

    [ClientServerScenario]
    public void Stack_ResolvesItemsAndBlocks_AndRefusesUnknownCodes()
    {
        Assert.Equal(EnumItemClass.Block, Client!.Items.Stack("game:rock-granite").Class);
        Assert.Equal(EnumItemClass.Item, Client.Items.Stack("stick", 5).Class);
        Assert.Equal(5, Client.Items.Stack("stick", 5).StackSize);
        Assert.Throws<ArgumentException>(() => Client.Items.Stack("game:pharos-no-such-thing"));
        Assert.Throws<ArgumentException>(() => Client.Items.Stack("game:stick", type: EnumItemClass.Block));
    }

    [ClientServerScenario]
    public void RenderIcon_DrawsTheStack_TheSameEachTime_AndLeavesTheFrameAsItWas()
    {
        ItemStack granite = Client!.Items.Stack("game:rock-granite");
        (int projection, int modelView) before = Client.RunOnClientThread(() => (Client.Client.PMatrix.Count, Client.Client.MvMatrix.Count));

        FramebufferSnapshot icon = Client.Items.RenderIcon(granite, 64);
        FramebufferSnapshot again = Client.Items.RenderIcon(granite, 64);

        Assert.Equal((64, 64), (icon.Width, icon.Height));
        Assert.Equal(255, icon.GetPixel(32, 32).A);
        Assert.Equal(0, icon.GetPixel(0, 0).A);
        Assert.Equal(0, icon.GetPixel(63, 63).A);
        Assert.True(icon.RawRgba.Distinct().Count() > 4, "The icon is a single colour");
        Assert.Equal(icon.RawRgba, again.RawRgba);
        Assert.Equal(before, Client.RunOnClientThread(() => (Client.Client.PMatrix.Count, Client.Client.MvMatrix.Count)));

        FramebufferSnapshot stick = Client.Items.RenderIcon(Client.Items.Stack("game:stick"), 48);
        Assert.Equal(48, stick.Width);
        Assert.Contains(Enumerable.Range(0, 48 * 48), i => stick.RawRgba[i * 4 + 3] == 255);
    }

    [ClientServerScenario]
    public async Task ManyIcons_DoNotGrowTheMatrixStacks_AndTheClientKeepsRendering()
    {
        ItemStack granite = Client!.Items.Stack("game:rock-granite");
        (int, int) before = Client.RunOnClientThread(() => (Client.Client.PMatrix.Count, Client.Client.MvMatrix.Count));

        for (int i = 0; i < 40; i++) Client.Items.RenderIcon(granite, 16);

        Assert.Equal(before, Client.RunOnClientThread(() => (Client.Client.PMatrix.Count, Client.Client.MvMatrix.Count)));
        await Session!.StepFramesAsync(5);
        Assert.True(Client.IsJoined);
    }

    [ClientServerScenario]
    public void RenderIcons_SweepsADomain_AndAGoldenRoundTrips()
    {
        IReadOnlyDictionary<IconKey, FramebufferSnapshot> icons = Client!.Items.RenderIcons("pharositemmod", 64);

        Assert.Equal(
            ["block pharositemmod:crate", "item pharositemmod:sword-iron"],
            icons.Keys.Select(k => k.ToString()).Order());
        Assert.All(icons.Values, icon => Assert.Contains(Enumerable.Range(0, 64 * 64), i => icon.RawRgba[i * 4 + 3] == 255));

        string goldens = Path.Combine(Path.GetTempPath(), "pharos-goldens-" + Guid.NewGuid().ToString("N")[..8]);
        try
        {
            Assert.Equal(2, Client.Items.SaveIcons("pharositemmod", goldens, 64));
            FramebufferSnapshot sword = Client.Items.RenderIcon(Client.Items.Stack("pharositemmod:sword-iron"), 64);
            PerceptualDiffResult result = GoldenImageAssertion.Assert(sword, Path.Combine(goldens, "item", "sword-iron.png"));
            Assert.True(result.IsSimilar, $"The icon differs from the one saved: {result}");
            Assert.Equal(sword.RawRgba, FramebufferSnapshot.FromFile(Path.Combine(goldens, "item", "sword-iron.png")).RawRgba);
        }
        finally
        {
            if (Directory.Exists(goldens)) Directory.Delete(goldens, recursive: true);
        }

        Assert.Throws<ArgumentException>(() => Client.Items.RenderIcons("pharos-no-such-domain"));
        Assert.Throws<ArgumentOutOfRangeException>(() => Client.Items.RenderIcon(Client.Items.Stack("game:stick"), 4));
    }
}
