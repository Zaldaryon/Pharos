using System.Globalization;
using System.Reflection;
using Vintagestory.API.Client;
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

public class LanguageSweepTests
{
    [Fact]
    public void AllLanguages_ReadsShippedLanguagesWithoutDuplicates()
    {
        IReadOnlyList<string> codes = LanguageCatalog.AllLanguages();

        Assert.True(codes.Count >= 30, $"Expected at least 30 languages, found {codes.Count}.");
        Assert.Contains("en", codes);
        Assert.Contains("ar", codes);
        Assert.Contains("ja", codes);
        Assert.Contains("th", codes);
        Assert.Equal(codes.Count, codes.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void ParseLanguageCodes_SkipsEmptyAndDuplicateEntries()
    {
        IReadOnlyList<string> parsed = LanguageCatalog.ParseLanguageCodes("""
            [
              { "code": "en" },
              { "code": "de" },
              { "code": "DE" },
              { "code": "" }
            ]
            """);

        Assert.Equal(["en", "de"], parsed);
    }

    [Theory]
    [InlineData("ar", true)]
    [InlineData("ar-SA", true)]
    [InlineData("he", true)]
    [InlineData("fa", true)]
    [InlineData("ur", true)]
    [InlineData("en", false)]
    [InlineData("de", false)]
    [InlineData("ja", false)]
    [InlineData("th", false)]
    [InlineData(null, false)]
    public void IsRightToLeft_DetectsRtlBaseSubtags(string? code, bool expected) =>
        Assert.Equal(expected, LanguageCatalog.IsRightToLeft(code));

    [Fact]
    public void PseudoLongExpand_IncreasesLengthWhilePreservingPlaceholdersAndTags()
    {
        string source = "Found <strong>{0}</strong> items ({p0:# item|# items})\nNext";
        string expanded = PseudoLongTranslationService.Expand(source);

        Assert.Contains("{0}", expanded);
        Assert.Contains("{p0:# item|# items}", expanded);
        Assert.Contains("<strong>", expanded);
        Assert.Contains("</strong>", expanded);
        Assert.Contains("\n", expanded);
        Assert.True(expanded.Length > source.Length);
    }

    [Fact]
    public void AllLanguagesAttribute_SupportsIncludePseudoLongAndExclude()
    {
        MethodInfo target = typeof(LanguageSweepTests).GetMethod(nameof(SampleLanguageTheory), BindingFlags.NonPublic | BindingFlags.Static)!;
        AllLanguagesAttribute attr = new() { IncludePseudoLong = true, Exclude = ["de", "ar"] };

        string[] rows = [.. attr.GetData(target).Select(r => (string)r[0])];
        Assert.Contains("en", rows);
        Assert.Contains("pseudo-long", rows);
        Assert.DoesNotContain("de", rows);
        Assert.DoesNotContain("ar", rows);
    }

    [Fact]
    public void LanguagesAttribute_ValidatesCodesAndParameterSignature()
    {
        MethodInfo valid = typeof(LanguageSweepTests).GetMethod(nameof(SampleLanguageTheory), BindingFlags.NonPublic | BindingFlags.Static)!;
        MethodInfo invalid = typeof(LanguageSweepTests).GetMethod(nameof(InvalidLanguageTheory), BindingFlags.NonPublic | BindingFlags.Static)!;

        LanguagesAttribute attr = new("de", "ar", "ja", "th", "pseudo-long");
        Assert.Equal(["de", "ar", "ja", "th", "pseudo-long"], attr.GetData(valid).Select(r => (string)r[0]));

        Assert.Throws<ArgumentException>(() => new LanguagesAttribute());
        Assert.Throws<ArgumentException>(() => new LanguagesAttribute("en", " "));
        Assert.Throws<ArgumentException>(() => attr.GetData(invalid).ToList());
    }

    [Fact]
    public void CultureInvariant_PassesForInvariantCode_AndRestoresThreadCultures()
    {
        CultureInfo previous = CultureInfo.CurrentCulture;
        CultureInfo previousUi = CultureInfo.CurrentUICulture;

        double parsed = PharosAssert.CultureInvariant(() => double.Parse("1.5", CultureInfo.InvariantCulture));
        PharosAssert.CultureInvariant(() =>
        {
            _ = "FILE".ToLowerInvariant();
        });

        Assert.Equal(1.5, parsed);
        Assert.Equal(previous, CultureInfo.CurrentCulture);
        Assert.Equal(previousUi, CultureInfo.CurrentUICulture);
    }

    [Fact]
    public void CultureInvariant_FailsOnCultureDependentParsingOrTurkishCase()
    {
        PharosAssertException decimalError = Assert.Throws<PharosAssertException>(() =>
            PharosAssert.CultureInvariant(() => double.Parse("1.5", CultureInfo.CurrentCulture)));
        Assert.Contains("de-DE", decimalError.Message);

        PharosAssertException turkishError = Assert.Throws<PharosAssertException>(() =>
            PharosAssert.CultureInvariant(() => "FILE".ToLower()));
        Assert.Contains("tr-TR", turkishError.Message);
    }

    [Fact]
    public void TextLayoutInspector_EvaluateOverflow_MeasuresHorizontalAndVerticalBounds()
    {
        TextElementMetrics fits = new("D", "main", "ok", "GuiElementStaticText", "Save", 100, 30, 42, 18, EnumTextOrientation.Left, false);
        Assert.Null(TextLayoutInspector.EvaluateOverflow(in fits, "en", tolerance: 1));

        TextElementMetrics wide = fits with { Key = "wide", MeasuredWidth = 118.5, Text = "Donaudampfschifffahrt" };
        TextOverflowEntry? entry = TextLayoutInspector.EvaluateOverflow(in wide, "de", tolerance: 1);
        Assert.NotNull(entry);
        Assert.Equal(18.5, entry.HorizontalOverflowPx, 1);
        Assert.Equal(0, entry.VerticalOverflowPx);

        string message = TextLayoutInspector.FormatOverflowMessage("D", "de", [entry]);
        Assert.Contains("'wide'", message);
        Assert.Contains("(de)", message);
        Assert.Contains("18.5 px horizontally", message);
    }

    [Fact]
    public void TextLayoutInspector_EvaluateRtl_WarnsWithoutOptIn_AndFailsWhenOptedInOrStrict()
    {
        TextElementMetrics left = new("D", "main", "title", "GuiElementStaticText", "مرحبا", 100, 30, 40, 18, EnumTextOrientation.Left, false);
        TextElementMetrics right = left with { Key = "subtitle", Orientation = EnumTextOrientation.Right };

        Assert.Empty(TextLayoutInspector.EvaluateRtl([left], "en", strict: true).Failures);
        Assert.Empty(TextLayoutInspector.EvaluateRtl([left], "en", strict: false).Warnings);

        RtlEvaluationResult warningOnly = TextLayoutInspector.EvaluateRtl([left], "ar", strict: false);
        Assert.Empty(warningOnly.Failures);
        LanguageLayoutWarning warning = Assert.Single(warningOnly.Warnings);
        Assert.Equal("title", warning.ElementKey);

        RtlEvaluationResult optedIn = TextLayoutInspector.EvaluateRtl([right, left], "ar", strict: false);
        string failure = Assert.Single(optedIn.Failures);
        Assert.Contains("'title'", failure);

        RtlEvaluationResult strictResult = TextLayoutInspector.EvaluateRtl([left], "ar", strict: true);
        Assert.Single(strictResult.Failures);
    }

    [Fact]
    public void LanguageLayoutReport_SummarizesWorstOverflowsPerLanguageAndMetrics()
    {
        TextOverflowEntry deSmall = new("Codex", "main", "btn", "GuiElementTextButton", "de", 6, 0, "Speichern");
        TextOverflowEntry deBig = new("Codex", "main", "header", "GuiElementStaticText", "de", 24, 12, "Ueberschrift");
        TextOverflowEntry thBig = new("Codex", "main", "body", "GuiElementRichtext", "th", 0, 15, "ข้อความ");
        LanguageLayoutWarning arWarn = new("Codex", "main", "body", "GuiElementStaticText", "ar", "uses Left alignment");

        LanguageLayoutReport report = new(
            "mymod",
            [deSmall, deBig, thBig],
            [arWarn],
            new Dictionary<string, int> { ["en"] = 4, ["de"] = 4, ["th"] = 4, ["ar"] = 4 });

        Assert.True(report.HasOverflows);
        Assert.Equal(["de", "th", "ar", "en"], report.ByLanguage.Select(s => s.Language));
        Assert.Equal("header", report.ByLanguage[0].WorstElementKey);
        Assert.Equal(24, report.ByLanguage[0].WorstOverflowPx);

        string markdown = report.ToMarkdown();
        Assert.Contains("| de | 4 | 2 | 24 | header | 0 |", markdown);
        Assert.Contains("| ar | 4 | 0 | 0 | - | 1 |", markdown);
        Assert.Contains(report.Metrics(), m => m.Name == "lang.layout.worst_overflow_px" && Math.Abs(m.Value - 24) < 0.001);
    }

    private static void SampleLanguageTheory(string lang)
    {
    }

    private static void InvalidLanguageTheory(int wrongType)
    {
    }
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
