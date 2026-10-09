using Vintagestory.API.Common;
using Xunit;
using Zaldaryon.Pharos.Assertions;
using Zaldaryon.Pharos.Bootstrap;
using Zaldaryon.Pharos.Core;
using Zaldaryon.Pharos.Translations;
using Zaldaryon.Pharos.XUnit;
using GameLang = Vintagestory.API.Config.Lang;

namespace Zaldaryon.Pharos.Tests.Translations;

public class MissingTranslationTests
{
    [Theory]
    [InlineData("mymod:item-foo", "mymod")]
    [InlineData("item-foo", "game")]
    [InlineData("Error: something broke", "game")]
    [InlineData(":odd", "game")]
    public void DomainOf_ReadsTheDomainOnlyWhenItLooksLikeOne(string key, string domain) =>
        Assert.Equal(domain, MissingTranslation.DomainOf(key));
}

/// <summary>
/// Reloads and translations on an engine-mode client joined to a server, with
/// <c>TestMods/pharoslangmod</c>: an item with a name in English and German, a block with none,
/// and a client mod that counts the reload events.
/// </summary>
[Collection("Sequential")]
[Trait(PharosTraits.Category, PharosTraits.Live)]
[ServerMods("TestMods/pharoslangmod")]
public class LiveTranslationTests : ClientServerScenarioBase
{
    protected override HeadlessClientOptions ClientOptions => new()
    {
        BootMode = ClientBootMode.Engine,
        Width = 640,
        Height = 360,
    };

    [ClientServerScenario]
    public async Task ReloadShaders_RunsTheModsHandler_AndTheClientKeepsRendering()
    {
        string before = await Counts();

        ReloadResult result = await Client!.ReloadAsync(AssetCategory.shaders);

        Assert.True(result.Succeeded);
        Assert.Equal(Bump(before, "shaders"), await Counts());
        await Session!.StepFramesAsync(5);
    }

    [ClientServerScenario]
    public async Task ReloadShapesAndTextures_RaiseTheModsEvents()
    {
        string before = await Counts();

        await Client!.ReloadAsync(AssetCategory.shapes);
        await Client.ReloadAsync(AssetCategory.textures);

        Assert.Equal(Bump(Bump(before, "shapes"), "textures"), await Counts());
        await Session!.StepFramesAsync(5);
    }

    [ClientServerScenario]
    public async Task AFailedShaderReload_Throws_UnlessErrorsAreAllowed()
    {
        await Client!.Commands.ExecuteSuccessAsync(".pharoslang failshaders");
        await Assert.ThrowsAsync<InvalidOperationException>(() => Client.ReloadAsync(AssetCategory.shaders));

        await Client.Commands.ExecuteSuccessAsync(".pharoslang failshaders");
        ReloadResult result = await Client.ReloadAsync("shaders", allowErrors: true);
        Assert.False(result.Succeeded);

        await Assert.ThrowsAsync<ArgumentException>(() => Client.ReloadAsync("nosuchcategory"));
        await Assert.ThrowsAsync<NotSupportedException>(() => Client.ReloadAsync(AssetCategory.sounds));
    }

    [ClientServerScenario]
    public async Task ReloadAll_ReloadsLangShapesTexturesAndShaders_WithoutMissingKeysOfItsOwn()
    {
        IReadOnlyList<ReloadResult> results = await Client!.ReloadAllAsync();

        Assert.Equal(["lang", "shapes", "textures", "shaders"], results.Select(r => r.Category.Code));
        Assert.Empty(Client.Lang.MissingKeysOf("pharoslangmod"));
        Assert.Equal("Hello from Pharos", Client.RunOnClientThread(() => GameLang.Get("pharoslangmod:hello")));
    }

    [ClientServerScenario]
    public async Task MissingKeys_ListLookupsWithNoEntry_FromTheGamesOwnCallSites()
    {
        Assert.Empty(Client!.Lang.MissingKeysOf("pharoslangmod"));

        string gadgetName = Client.RunOnClientThread(() => new ItemStack(Client.Client.World.GetBlock(new AssetLocation("pharoslangmod:pharosgadget"))).GetName());
        Client.RunOnClientThread(() => GameLang.Get("pharoslangmod:no-such-key"));
        Client.RunOnClientThread(() => GameLang.Get("pharoslangmod:hello"));
        Client.RunOnClientThread(() => GameLang.Get("pharoslangmod:no-such-key"));
        await Client.Commands.ExecuteSuccessAsync(".pharoslang counts");

        Assert.Equal("pharoslangmod:block-pharosgadget", gadgetName);
        IReadOnlyList<MissingTranslation> missing = Client.Lang.MissingKeysOf("pharoslangmod");
        Assert.Contains(missing, m => m.Key == "pharoslangmod:block-pharosgadget" && m.Language == "en");
        MissingTranslation noKey = Assert.Single(missing, m => m.Key == "pharoslangmod:no-such-key");
        Assert.Equal(2, noKey.Count);
        Assert.False(noKey.FellBackToDefault);
        Assert.DoesNotContain(missing, m => m.Key == "pharoslangmod:hello");
        Assert.DoesNotContain(Client.Lang.MissingKeys, m => m.Key.StartsWith("shaders=", StringComparison.Ordinal));
        Assert.Throws<PharosAssertException>(() => PharosAssert.NoMissingTranslations(Client, "pharoslangmod"));

        Client.Lang.Reset();
        Assert.Empty(Client.Lang.MissingKeys);
        PharosAssert.NoMissingTranslations(Client, "pharoslangmod");
    }

    [ClientServerScenario]
    public void Use_SwitchesTheLanguage_ReportsWhatFellBackToEnglish_AndRestores()
    {
        Assert.Equal("en", Client!.Lang.Current);

        using (Client.Lang.Use("de"))
        {
            Assert.Equal("de", Client.Lang.Current);
            Assert.Equal("Pharos-Widget", Client.RunOnClientThread(() => GameLang.Get("pharoslangmod:item-pharoswidget")));
            Assert.Equal("Hello from Pharos", Client.RunOnClientThread(() => GameLang.Get("pharoslangmod:hello")));
        }

        Assert.Equal("en", Client.Lang.Current);
        Assert.Empty(Client.Lang.MissingKeysOf("pharoslangmod"));
        MissingTranslation hello = Assert.Single(Client.Lang.MissingKeysOf("pharoslangmod", includeFallbacks: true));
        Assert.Equal(("pharoslangmod:hello", "de", true), (hello.Key, hello.Language, hello.FellBackToDefault));
        Assert.Throws<ArgumentException>(() => Client.Lang.Use("xx-nope"));
    }

    [ClientServerScenario]
    public void ALanguageLeftOn_IsRestoredForTheNextTest_Part1()
    {
        Assert.Equal("en", Client!.Lang.Current);
        Client.Lang.Use("de");
    }

    [ClientServerScenario]
    public void ALanguageLeftOn_IsRestoredForTheNextTest_Part2()
    {
        // Whichever part runs first, each starts in the language the client booted with.
        Assert.Equal("en", Client!.Lang.Current);
        Client.Lang.Use("de");
    }

    [ClientServerScenario]
    public void AllTranslated_NamesTheItemsAndBlocksWithoutAName()
    {
        PharosAssertException error = Assert.Throws<PharosAssertException>(() => PharosAssert.AllTranslated(Client!, "pharoslangmod", "en"));

        Assert.Contains("pharoslangmod:block-pharosgadget", error.Message);
        Assert.DoesNotContain("pharoslangmod:item-pharoswidget", error.Message);
        Assert.Contains("1 of 2", error.Message);
        Assert.Throws<ArgumentException>(() => PharosAssert.AllTranslated(Client!, "pharoslangmod", "xx-nope"));
        Assert.Throws<PharosAssertException>(() => PharosAssert.AllTranslated(Client!, "nosuchdomain"));
    }

    private async Task<string> Counts() => (await Client!.Commands.ExecuteSuccessAsync(".pharoslang counts")).Message!;

    private static string Bump(string counts, string name) =>
        string.Join(' ', counts.Split(' ').Select(part =>
        {
            string[] pair = part.Split('=');
            return pair[0] == name ? $"{name}={int.Parse(pair[1]) + 1}" : part;
        }));
}
