using Xunit;
using Zaldaryon.Pharos.Platform;
using Zaldaryon.Pharos.Tests.XUnit;
using Zaldaryon.Pharos.XUnit;

namespace Zaldaryon.Pharos.Tests.Versions;

public class GameVersionTests
{
    [Theory]
    [InlineData(">=1.22.0", "1.22.0", true)]
    [InlineData(">=1.22.0", "1.22.7", true)]
    [InlineData(">=1.22.0", "1.21.6", false)]
    [InlineData(">=1.22.0", "1.22.0-rc.3", false)]
    [InlineData(">=1.22.0-rc.1", "1.22.0-rc.3", true)]
    [InlineData("<1.22.0", "1.22.0-rc.3", true)]
    [InlineData(">1.21.6, <=1.22.7", "1.22.7", true)]
    [InlineData(">1.21.6, <=1.22.7", "1.21.6", false)]
    [InlineData(">1.21.6,<=1.22.7", "1.23.0", false)]
    [InlineData("=1.22.7", "1.22.7", true)]
    [InlineData("1.22.7", "1.22.6", false)]
    [InlineData("1.22.x", "1.22.0-rc.1", true)]
    [InlineData("1.22.*", "1.22.9", true)]
    [InlineData("1.22", "1.22.9", true)]
    [InlineData("1.22", "1.23.0-pre.1", false)]
    [InlineData("<1.21.0 || >=1.22.0", "1.20.12", true)]
    [InlineData("<1.21.0 || >=1.22.0", "1.21.6", false)]
    public void Ranges_IncludeTheVersionsTheySay(string range, string version, bool included) =>
        Assert.Equal(included, GameVersionRange.Parse(range).Includes(PharosGameVersion.Parse(version)));

    [Theory]
    [InlineData("1.22.0-rc.10", "1.22.0-rc.9")]
    [InlineData("1.22.0", "1.22.0-rc.9")]
    [InlineData("1.22.0-rc.1", "1.22.0-pre.4")]
    [InlineData("1.22.0-rc.1.2", "1.22.0-rc.1")]
    [InlineData("1.10.0", "1.9.9")]
    public void Versions_Order_AsReleasesDo(string later, string earlier) =>
        Assert.True(PharosGameVersion.Parse(later) > PharosGameVersion.Parse(earlier));

    [Theory]
    [InlineData("")]
    [InlineData(">=")]
    [InlineData("1..2")]
    [InlineData("~1.2")]
    [InlineData(">=1.22.0,")]
    [InlineData("1.22.0-")]
    public void BadRanges_AreRefused_NamingThePart(string range) =>
        Assert.Throws<FormatException>(() => GameVersionRange.Parse(range));

    [Fact]
    public void TheInstallsVersion_IsReadWithoutLoadingIt_AndMatchesTheLoadedApi()
    {
        string install = HeadlessPlatformResolver.ResolveGamePath();
        string? read = InstalledGame.ReadVersion(install);

        Assert.NotNull(read);
        Assert.Equal(InstalledGame.Version, read);
        Assert.NotNull(InstalledGame.ParsedVersion);
        Assert.Null(InstalledGame.ReadVersion(Path.GetTempPath()));
    }
}

public class RequireGameVersionTests
{
#pragma warning disable xUnit1000 // Samples are private so the real test run does not discover them.
    private sealed class Future
    {
        [ClientScenario]
        [RequireGameVersion(">=99.0.0", Reason = "Needs the 99 API.")]
        public void Skipped()
        {
        }

        [ClientScenario]
        [RequireGameVersion(">=0.0.1")]
        public void Runs()
        {
        }

        [ClientScenario]
        [RequireGameVersion("~1.2")]
        public void Typo()
        {
        }
    }

    [RequireGameVersion("<1.0.0")]
    private sealed class OldOnly
    {
        [ClientScenario]
        public void Anything()
        {
        }
    }

#pragma warning restore xUnit1000

    [Fact]
    public void AScenarioOutsideTheRange_IsSkipped_SayingWhyAndWhatIsInstalled()
    {
        string reason = Assert.Single(ScenarioRunnerHarness.Discover(typeof(Future), nameof(Future.Skipped))).SkipReason;

        Assert.StartsWith("Requires Vintage Story >=99.0.0; this install is " + InstalledGame.Version, reason);
        Assert.EndsWith("Needs the 99 API.", reason);
    }

    [Fact]
    public void AScenarioInsideTheRange_Runs() =>
        Assert.Null(Assert.Single(ScenarioRunnerHarness.Discover(typeof(Future), nameof(Future.Runs))).SkipReason);

    [Fact]
    public void TheClassesRange_AppliesToItsScenarios() =>
        Assert.NotNull(Assert.Single(ScenarioRunnerHarness.Discover(typeof(OldOnly), nameof(OldOnly.Anything))).SkipReason);

    [Fact]
    public async Task ARangeThatCannotBeRead_FailsTheTest_RatherThanSkippingIt()
    {
        Xunit.Sdk.IXunitTestCase testCase = Assert.Single(ScenarioRunnerHarness.Discover(typeof(Future), nameof(Future.Typo)));
        Assert.Null(testCase.SkipReason);
        Assert.Contains("Invalid [RequireGameVersion] range", testCase.InitializationException?.Message);

        ScenarioRunnerHarness.RunResult result = await ScenarioRunnerHarness.RunAsync(typeof(Future), nameof(Future.Typo));
        Assert.Contains("Invalid [RequireGameVersion] range", result.FailureMessage);
    }

    [GameVersionFact(">=99.0.0")]
    public void APlainFact_CanBeGatedToo() => Assert.Fail("Skipped on every install there is.");

    [GameVersionTheory(">=0.0.1")]
    [InlineData(1)]
    public void APlainTheory_RunsInsideItsRange(int value) => Assert.Equal(1, value);
}

[Collection("Sequential")]
public class StagingRefreshTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "pharos-staging-" + Guid.NewGuid().ToString("N")[..8]);

    public StagingRefreshTests()
    {
        foreach (string install in new[] { "a", "b" })
        {
            Directory.CreateDirectory(Path.Combine(_root, install, "Lib"));
            File.WriteAllText(Path.Combine(_root, install, "Lib", "which.txt"), install);
        }

        Directory.CreateDirectory(Path.Combine(_root, "out"));
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private string Target => Path.Combine(_root, "out", "Lib");

    private string Staged() => File.ReadAllText(Path.Combine(Target, "which.txt"));

    [Fact]
    public void ALinkToAnotherInstall_IsReplaced()
    {
        HeadlessPlatformResolver.LinkOrCopyDirectory(Path.Combine(_root, "a", "Lib"), Target, "a");
        Assert.Equal("a", Staged());

        HeadlessPlatformResolver.LinkOrCopyDirectory(Path.Combine(_root, "b", "Lib"), Target, "b");
        Assert.Equal("b", Staged());

        // Relinking removes the link, never what it pointed at.
        Assert.Equal("a", File.ReadAllText(Path.Combine(_root, "a", "Lib", "which.txt")));
    }

    [Fact]
    public void WithNoInstallFound_TheOutputFolderIsLeftAsItIs()
    {
        HeadlessPlatformResolver.LinkOrCopyDirectory(Path.Combine(_root, "a", "Lib"), Target, "a");

        // The game path falls back to the output folder itself: staging it onto itself does nothing.
        HeadlessPlatformResolver.LinkOrCopyDirectory(Target, Target, "out");

        Assert.Equal("a", Staged());
    }

    [Fact]
    public void ALinkWhoseFolderIsGone_IsReplaced()
    {
        string gone = Path.Combine(_root, "gone");
        Directory.CreateDirectory(gone);
        Directory.CreateSymbolicLink(Target, gone);
        Directory.Delete(gone);

        HeadlessPlatformResolver.LinkOrCopyDirectory(Path.Combine(_root, "b", "Lib"), Target, "b");

        Assert.Equal("b", Staged());
    }

    [Fact]
    public void ACopyFromAnotherInstall_IsReplaced_ButAnUnmarkedOneIsLeft()
    {
        CopyTo(Path.Combine(_root, "a", "Lib"), Target);
        File.WriteAllText(Target + ".pharos-source", "a");

        Assert.True(HeadlessPlatformResolver.IsStale(Path.Combine(_root, "b", "Lib"), Target, "b"));
        HeadlessPlatformResolver.LinkOrCopyDirectory(Path.Combine(_root, "b", "Lib"), Target, "b");
        Assert.Equal("b", Staged());

        // A copy staged before Pharos marked its copies is left as it is.
        string unmarked = Path.Combine(_root, "out", "Mods");
        CopyTo(Path.Combine(_root, "a", "Lib"), unmarked);
        Assert.False(HeadlessPlatformResolver.IsStale(Path.Combine(_root, "b", "Lib"), unmarked, "b"));
    }

    [Fact]
    public void ALinkToTheSameInstall_IsLeftAlone()
    {
        HeadlessPlatformResolver.LinkOrCopyDirectory(Path.Combine(_root, "a", "Lib"), Target, "a");
        Assert.False(HeadlessPlatformResolver.IsStale(Path.Combine(_root, "a", "Lib") + Path.DirectorySeparatorChar, Target, "a"));
    }

    [Fact]
    public void TestsBuiltAgainstAnotherVersion_DoNotRun_UnlessAllowed()
    {
        string built = Directory.CreateDirectory(Path.Combine(_root, "built")).FullName;
        string other = Directory.CreateDirectory(Path.Combine(_root, "other")).FullName;
        string patched = Directory.CreateDirectory(Path.Combine(_root, "patched")).FullName;
        string none = Directory.CreateDirectory(Path.Combine(_root, "none")).FullName;
        WriteFakeApi(built, "1.22.7");
        WriteFakeApi(other, "1.21.6");
        WriteFakeApi(patched, "1.22.7", extra: true);

        InvalidOperationException error = Assert.Throws<InvalidOperationException>(() => HeadlessPlatformResolver.CheckGameMatchesBuild(other, built));
        Assert.Contains("built against Vintage Story 1.22.7, but VINTAGE_STORY is 1.21.6", error.Message);

        string? before = Environment.GetEnvironmentVariable("PHAROS_ALLOW_GAME_MISMATCH");
        try
        {
            Environment.SetEnvironmentVariable("PHAROS_ALLOW_GAME_MISMATCH", "1");
            HeadlessPlatformResolver.CheckGameMatchesBuild(other, built);
        }
        finally
        {
            Environment.SetEnvironmentVariable("PHAROS_ALLOW_GAME_MISMATCH", before);
        }

        // The same version with another API file only warns, and with no API there is nothing to compare.
        HeadlessPlatformResolver.CheckGameMatchesBuild(patched, built);
        HeadlessPlatformResolver.CheckGameMatchesBuild(none, built);
        Assert.Equal("1.21.6", InstalledGame.ReadVersion(other));
    }

    // A VintagestoryAPI.dll holding only GameVersion.ShortGameVersion.
    private static void WriteFakeApi(string directory, string version, bool extra = false)
    {
        System.Reflection.Emit.PersistedAssemblyBuilder assembly = new(new System.Reflection.AssemblyName("VintagestoryAPI"), typeof(object).Assembly);
        System.Reflection.Emit.ModuleBuilder module = assembly.DefineDynamicModule("VintagestoryAPI");
        System.Reflection.Emit.TypeBuilder type = module.DefineType("Vintagestory.API.Config.GameVersion",
            System.Reflection.TypeAttributes.Public | System.Reflection.TypeAttributes.Abstract | System.Reflection.TypeAttributes.Sealed);
        type.DefineField("ShortGameVersion", typeof(string),
            System.Reflection.FieldAttributes.Public | System.Reflection.FieldAttributes.Static | System.Reflection.FieldAttributes.Literal).SetConstant(version);
        type.CreateType();
        if (extra) module.DefineType("Vintagestory.API.Config.Patched", System.Reflection.TypeAttributes.Public).CreateType();
        assembly.Save(Path.Combine(directory, "VintagestoryAPI.dll"));
    }

    private static void CopyTo(string source, string target)
    {
        Directory.CreateDirectory(target);
        foreach (string file in Directory.GetFiles(source)) File.Copy(file, Path.Combine(target, Path.GetFileName(file)));
    }
}
