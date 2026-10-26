using System.Text.Json;
using System.Text.RegularExpressions;
using Xunit;

namespace Zaldaryon.Pharos.Tests.Cli;

/// <summary>
/// The <c>dotnet new</c> templates of Zaldaryon.Pharos.Templates. CI generates them and runs the
/// result (the templates job of ci.yml); these check what a generated project points at.
/// </summary>
public sealed class TemplatesTests
{
    private static readonly string Root = FindRoot();
    private static readonly string Content = Path.Combine(Root, "src", "Zaldaryon.Pharos.Templates", "content");

    [Theory]
    [InlineData("pharos-mod-tests", "Zaldaryon.Pharos.Templates.ModTests")]
    [InlineData("pharos-ci", "Zaldaryon.Pharos.Templates.Ci")]
    public void EachTemplate_HasItsShortNameAndIdentity(string shortName, string identity)
    {
        using JsonDocument template = ReadTemplate(shortName);

        Assert.Equal(shortName, template.RootElement.GetProperty("shortName").GetString());
        Assert.Equal(identity, template.RootElement.GetProperty("identity").GetString());
    }

    [Fact]
    public void TheModTestsTemplate_TakesTheModProjectAFrameworkAndAClient()
    {
        using JsonDocument template = ReadTemplate("pharos-mod-tests");
        JsonElement symbols = template.RootElement.GetProperty("symbols");

        Assert.True(symbols.GetProperty("mod-project").GetProperty("isRequired").GetBoolean());
        Assert.Equal(
            ["xunit2", "xunit3"],
            symbols.GetProperty("framework").GetProperty("choices").EnumerateArray().Select(c => c.GetProperty("choice").ToString()).ToArray());
        Assert.Equal("bool", symbols.GetProperty("client").GetProperty("datatype").GetString());
    }

    /// <summary>
    /// A path is written into XML and into YAML, where a <c>&amp;</c> or a <c>'</c> in it would break the
    /// file: each goes through a derived symbol that escapes it.
    /// </summary>
    [Theory]
    [InlineData("pharos-mod-tests", "mod-project", "MOD_PROJECT_PATH", "xmlEncode")]
    [InlineData("pharos-ci", "test-project", "TEST_PROJECT_PATH", "yamlSingleQuoted")]
    public void APath_IsEscapedForTheFileItIsWrittenTo(string shortName, string symbol, string placeholder, string transform)
    {
        using JsonDocument template = ReadTemplate(shortName);
        JsonElement symbols = template.RootElement.GetProperty("symbols");

        Assert.False(symbols.GetProperty(symbol).TryGetProperty("replaces", out _));
        JsonElement derived = symbols.EnumerateObject().Single(s => s.Value.TryGetProperty("replaces", out JsonElement r) && r.GetString() == placeholder).Value;
        Assert.Equal("derived", derived.GetProperty("type").GetString());
        Assert.Equal(symbol, derived.GetProperty("valueSource").GetString());
        Assert.Equal(transform, derived.GetProperty("valueTransform").GetString());
        Assert.True(template.RootElement.GetProperty("forms").TryGetProperty(transform, out _));
    }

    [Fact]
    public void TheCiTemplate_DefaultsToTheGameCiTestsAgainst()
    {
        using JsonDocument template = ReadTemplate("pharos-ci");
        string ciGame = Regex.Match(File.ReadAllText(Path.Combine(Root, ".github", "workflows", "ci.yml")), "VS_VERSION: \"([^\"]+)\"").Groups[1].Value;

        Assert.Equal(ciGame, template.RootElement.GetProperty("symbols").GetProperty("game-version").GetProperty("defaultValue").GetString());
    }

    /// <summary>
    /// A generated project restores the Pharos release the templates ship with, and a generated
    /// workflow calls main during development, or the matching tag for a release.
    /// </summary>
    [Fact]
    public void TheTemplates_PointAtTheReleaseTheyShipWith()
    {
        string version = PackageVersion("Zaldaryon.Pharos.Templates");
        string project = File.ReadAllText(Path.Combine(Content, "pharos-mod-tests", "PharosModTests.csproj"));
        string workflow = File.ReadAllText(Path.Combine(Content, "pharos-ci", ".github", "workflows", "pharos.yml"));

        Assert.Equal(PackageVersion("Zaldaryon.Pharos.XUnit"), version);
        Assert.Equal(
            [PackageVersion("Zaldaryon.Pharos.XUnit.V3"), version],
            Regex.Matches(project, "<PharosVersion [^>]*>([^<]+)</PharosVersion>").Select(m => m.Groups[1].Value).ToArray());
        string reference = Regex.Match(workflow, @"mod-ci.yml@([^\s]+)").Groups[1].Value;
        Assert.Contains(reference, new[] { "main", $"v{version}" });
        Assert.Contains($"mod-ci.yml@{reference}", File.ReadAllText(Path.Combine(Root, ".github", "workflows", "mod-ci.yml")), StringComparison.Ordinal);
    }

    [Fact]
    public void TheReusableWorkflow_TakesTheInputsTheCiTemplatePasses()
    {
        string workflow = File.ReadAllText(Path.Combine(Content, "pharos-ci", ".github", "workflows", "pharos.yml"));
        string modCi = File.ReadAllText(Path.Combine(Root, ".github", "workflows", "mod-ci.yml"));
        string[] passed = Regex.Matches(workflow, @"^      ([a-z-]+):", RegexOptions.Multiline).Select(m => m.Groups[1].Value).Distinct().ToArray();

        Assert.Equal(["test-project", "game-version", "os"], passed);
        Assert.Contains("  workflow_call:", modCi, StringComparison.Ordinal);
        foreach (string input in passed.Concat(["parallel", "smoke", "upload-artifacts"]))
        {
            // The $ anchor doesn't match before \r\n on Windows checkouts, so use \r?$ explicitly.
            Assert.Matches($"(?m)^      {input}:\\r?$", modCi);
        }
    }

    [Fact]
    public void TheGeneratedSamples_AreInTheRepository()
    {
        // CI regenerates these with scripts/update-template-samples.sh and fails on a difference.
        string samples = Path.Combine(Root, "tests", "TemplateSample");

        Assert.True(File.Exists(Path.Combine(samples, "ExampleMod", "modinfo.json")));
        Assert.True(File.Exists(Path.Combine(samples, "ExampleMod.Tests", "ExampleMod.Tests.csproj")));
        Assert.True(File.Exists(Path.Combine(samples, "ExampleMod.Tests", "ClientServerTests.cs")));
        Assert.True(File.Exists(Path.Combine(samples, "ExampleMod.Tests.V3", "ExampleMod.Tests.V3.csproj")));
        Assert.True(File.Exists(Path.Combine(samples, ".github", "workflows", "pharos.yml")));
    }

    private static JsonDocument ReadTemplate(string shortName) =>
        JsonDocument.Parse(File.ReadAllText(Path.Combine(Content, shortName, ".template.config", "template.json")));

    private static string PackageVersion(string package)
    {
        string project = File.ReadAllText(Path.Combine(Root, "src", package, package + ".csproj"));
        Match version = Regex.Match(project, "<Version>([^<]+)</Version>");
        Assert.True(version.Success, $"{package} has no version.");
        return version.Groups[1].Value;
    }

    private static string FindRoot()
    {
        for (DirectoryInfo? dir = new(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Pharos.sln")))
            {
                return dir.FullName;
            }
        }

        throw new InvalidOperationException("Found no Pharos.sln above " + AppContext.BaseDirectory);
    }
}
