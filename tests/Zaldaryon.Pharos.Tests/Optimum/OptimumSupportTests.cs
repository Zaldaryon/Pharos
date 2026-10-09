using System.Reflection;
using System.Reflection.Emit;
using Xunit;
using Zaldaryon.Pharos.Bootstrap;
using Zaldaryon.Pharos.Core;
using Zaldaryon.Pharos.Platform;
using Zaldaryon.Pharos.XUnit;

namespace Zaldaryon.Pharos.Tests.Optimum;

// Shaped like Optimum's diagnostics: counters as static fields and a summary method. Each test
// class reads its own fake, so the statics are not shared between parallel classes.
internal static class FakeDiagnostics
{
    public static long ChunksUploaded = 7;
    public static int GreedyFacesMerged = 3;
    public static readonly double Ratio = 0.5;
    public const int Threshold = 9;

    public static string GetTessellationSummary() => "chunks=4, uploads=2, ms=1.5ms";

    public static string Helper() => "not a summary";
}

internal static class FakeDiagnosticsWithReset
{
    public static long Passes = 11;
    public static long Untouched = 4;

    public static void Reset() => Passes = 0;
}

// Its own statics, so the test that moves them races no other test.
internal static class FakeMovingDiagnostics
{
    public static long ChunksUploaded = 7;
}

internal static class FakeOddDiagnostics
{
    public static readonly string Version = "0.9.2";
    public static short Small = 2;
    public static decimal Exact = 1.25m;

    public static string GetUploadSummary() => "batches = 3; bytes=4KB queued=5\tratio =40%";

    public static string GetBrokenSummary() => throw new InvalidOperationException("broken");
}

internal static class FakePropertyVersion
{
    public static string OptimumVersion => "1.0.0-beta";
}

internal static class FakeThrowingVersion
{
    public static string Version => throw new InvalidOperationException("no version");

    public const string BuildVersion = "0.8.0";
}

public class OptimumDiagnosticsTests
{
    [Fact]
    public void Counters_AreTheNumericStatics_AndTheSummaryPairs()
    {
        OptimumDiagnostics diagnostics = new(typeof(FakeDiagnostics));

        IReadOnlyDictionary<string, double> counters = diagnostics.Counters;

        Assert.Equal(7, counters["ChunksUploaded"]);
        Assert.Equal(3, counters["GreedyFacesMerged"]);
        Assert.Equal(0.5, counters["Ratio"]);
        Assert.DoesNotContain("Threshold", counters.Keys);
        Assert.Equal((4, 2, 1.5), (counters["tessellation.chunks"], counters["tessellation.uploads"], counters["tessellation.ms"]));
        Assert.Equal("tessellation: chunks=4, uploads=2, ms=1.5ms", diagnostics.Summary());
    }

    [Fact]
    public void AMissingCounter_ListsTheOnesThereAre()
    {
        KeyNotFoundException error = Assert.Throws<KeyNotFoundException>(() => new OptimumDiagnostics(typeof(FakeDiagnostics)).Counter("greedy.facesMerged"));
        Assert.Contains("GreedyFacesMerged", error.Message);
        Assert.Contains("tessellation.chunks", error.Message);
    }

    [Fact]
    public void Reset_WithoutOptimumsOwn_CountsFromNow_AndChangesNothingInOptimum()
    {
        OptimumDiagnostics diagnostics = new(typeof(FakeMovingDiagnostics));
        long before = FakeMovingDiagnostics.ChunksUploaded;

        Assert.False(diagnostics.Reset());
        Assert.Equal(0, diagnostics.Counter("ChunksUploaded"));
        Assert.Equal(before, FakeMovingDiagnostics.ChunksUploaded);

        using OptimumWindow window = diagnostics.Measure();
        FakeMovingDiagnostics.ChunksUploaded += 5;
        Assert.Equal(5, diagnostics.Counter("ChunksUploaded"));
        Assert.Equal(5, window.Delta("ChunksUploaded"));
        Assert.Throws<KeyNotFoundException>(() => window.Delta("nope"));
    }

    [Fact]
    public void Reset_UsesOptimumsOwn_WhenThereIsOne()
    {
        OptimumDiagnostics diagnostics = new(typeof(FakeDiagnosticsWithReset));

        Assert.True(diagnostics.Reset());

        Assert.Equal(0, FakeDiagnosticsWithReset.Passes);
        Assert.Equal(0, diagnostics.Counter("Passes"));
        Assert.Equal(4, FakeDiagnosticsWithReset.Untouched);
        Assert.Equal(0, diagnostics.Counter("Untouched"));
    }

    [Fact]
    public void OtherNumberTypes_Separators_AndUnits_AreRead_AndABrokenSummaryIsLeftOut()
    {
        IReadOnlyDictionary<string, double> counters = new OptimumDiagnostics(typeof(FakeOddDiagnostics)).Counters;

        Assert.Equal((2, 1.25), (counters["Small"], counters["Exact"]));
        Assert.Equal((3, 4, 5, 40), (counters["upload.batches"], counters["upload.bytes"], counters["upload.queued"], counters["upload.ratio"]));
        Assert.DoesNotContain(counters.Keys, k => k.StartsWith("broken.", StringComparison.Ordinal));
    }

    [Fact]
    public void TheVersion_IsReadFromAConstant_AField_OrAProperty_AndNeverThrows()
    {
        Assert.Equal("0.9.2", OptimumInstall.VersionOf(typeof(FakeOddDiagnostics)));
        Assert.Equal("1.0.0-beta", OptimumInstall.VersionOf(typeof(FakePropertyVersion)));
        Assert.Equal("0.8.0", OptimumInstall.VersionOf(typeof(FakeThrowingVersion)));
        Assert.Equal("unknown", OptimumInstall.VersionOf(typeof(FakeDiagnostics)));
    }
}

public class OptimumInstallTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "pharos-optimum-" + Guid.NewGuid().ToString("N")[..8]);

    public OptimumInstallTests() => Directory.CreateDirectory(_root);

    public void Dispose() => Directory.Delete(_root, recursive: true);

    [Fact]
    public void AnOptimumApi_IsDetected_WithItsVersion_AndWhetherTheLibraryIsPatched()
    {
        string optimum = Install("optimum", optimumVersion: "0.9.1", patchedLib: true);
        string halfPatched = Install("half", optimumVersion: null, patchedLib: false);
        string vanilla = Install("vanilla", optimumVersion: null, patchedLib: false, optimumType: false);

        Assert.Equal(new OptimumInfo("0.9.1", true), OptimumInstall.Detect(optimum));
        Assert.Equal(new OptimumInfo("unknown", false), OptimumInstall.Detect(halfPatched));
        Assert.Null(OptimumInstall.Detect(vanilla));
        Assert.Null(OptimumInstall.Detect(Path.Combine(_root, "missing")));
        File.WriteAllText(Path.Combine(vanilla, "VintagestoryAPI.dll"), "not an assembly");
        Assert.Null(OptimumInstall.Detect(vanilla));
    }

    [NotOptimumFact]
    public void ThisInstall_IsVanilla()
    {
        Assert.Null(OptimumInstall.Loaded);
        Assert.Null(OptimumInstall.Detect(HeadlessPlatformResolver.ResolveGamePath()));
    }

    [Fact]
    public void TestsBuiltForVanilla_DoNotRunOnOptimum_OrTheReverse()
    {
        string optimum = Install("optimum", optimumVersion: "0.9.1", patchedLib: true);
        string vanilla = Install("vanilla", optimumVersion: null, patchedLib: false, optimumType: false);

        Assert.Contains("is an Optimum build", Assert.Throws<InvalidOperationException>(() => HeadlessPlatformResolver.CheckGameMatchesBuild(optimum, vanilla)).Message);
        Assert.Contains("built against an Optimum build", Assert.Throws<InvalidOperationException>(() => HeadlessPlatformResolver.CheckGameMatchesBuild(vanilla, optimum)).Message);
        HeadlessPlatformResolver.CheckGameMatchesBuild(optimum, optimum);
    }

    // An install folder with a VintagestoryAPI.dll of version 1.22.7, with or without Optimum's
    // diagnostics type, and a VintagestoryLib.dll with or without Optimum's tesselator member.
    private string Install(string name, string? optimumVersion, bool patchedLib, bool optimumType = true)
    {
        string dir = Directory.CreateDirectory(Path.Combine(_root, name)).FullName;
        Emit(Path.Combine(dir, "VintagestoryAPI.dll"), "VintagestoryAPI", module =>
        {
            TypeBuilder version = module.DefineType("Vintagestory.API.Config.GameVersion", TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Sealed);
            version.DefineField("ShortGameVersion", typeof(string), FieldAttributes.Public | FieldAttributes.Static | FieldAttributes.Literal).SetConstant("1.22.7");
            version.CreateType();
            if (!optimumType) return;
            TypeBuilder diagnostics = module.DefineType("Vintagestory.API.Config.OptimumDiagnostics", TypeAttributes.Public | TypeAttributes.Abstract | TypeAttributes.Sealed);
            if (optimumVersion != null) diagnostics.DefineField("Version", typeof(string), FieldAttributes.Public | FieldAttributes.Static | FieldAttributes.Literal).SetConstant(optimumVersion);
            diagnostics.CreateType();
        });
        Emit(Path.Combine(dir, "VintagestoryLib.dll"), "VintagestoryLib", module =>
        {
            TypeBuilder manager = module.DefineType("Vintagestory.Client.NoObf.ChunkTesselatorManager", TypeAttributes.Public);
            if (patchedLib) manager.DefineField("PrimaryTesselator", typeof(object), FieldAttributes.Public);
            manager.CreateType();
        });
        return dir;
    }

    private static void Emit(string path, string assemblyName, Action<ModuleBuilder> define)
    {
        PersistedAssemblyBuilder assembly = new(new AssemblyName(assemblyName), typeof(object).Assembly);
        define(assembly.DefineDynamicModule(assemblyName));
        assembly.Save(path);
    }
}

public class RequireOptimumTests
{
    private static readonly OptimumInfo Present = new("0.9.1", true);

#pragma warning disable xUnit1000 // Samples are private so the real test run does not discover them.
    private sealed class Samples
    {
        [ClientScenario]
        [RequireOptimum(Reason = "Reads Optimum's counters.")]
        public void Requires()
        {
        }

        [ClientScenario]
        [SkipOnOptimum]
        public void Skips()
        {
        }

        [ClientScenario]
        public void Either()
        {
        }
    }

    [SkipOnOptimum]
    private sealed class VanillaOnly
    {
        [ClientScenario]
        [RequireOptimum]
        public void Contradiction()
        {
        }
    }
#pragma warning restore xUnit1000

    private static MethodInfo Method(Type type, string name) => type.GetMethod(name)!;

    [Fact]
    public void RequireOptimum_SkipsOnVanilla_NamingTheInstall()
    {
        string reason = OptimumGate.SkipReasonFor(typeof(Samples), Method(typeof(Samples), nameof(Samples.Requires)), null)!;
        Assert.StartsWith("Requires an Optimum build; this install is vanilla Vintage Story", reason);
        Assert.EndsWith("Reads Optimum's counters.", reason);
        Assert.Null(OptimumGate.SkipReasonFor(typeof(Samples), Method(typeof(Samples), nameof(Samples.Requires)), Present));
    }

    [Fact]
    public void SkipOnOptimum_SkipsOnlyOnOptimum()
    {
        Assert.Null(OptimumGate.SkipReasonFor(typeof(Samples), Method(typeof(Samples), nameof(Samples.Skips)), null));
        Assert.StartsWith("Skipped on Optimum 0.9.1", OptimumGate.SkipReasonFor(typeof(Samples), Method(typeof(Samples), nameof(Samples.Skips)), Present));
        Assert.Null(OptimumGate.SkipReasonFor(typeof(Samples), Method(typeof(Samples), nameof(Samples.Either)), Present));
    }

    [Fact]
    public async Task BothAttributes_FailTheTest_EvenWhenOneIsOnTheClass()
    {
        Assert.Throws<OptimumGateConflictException>(() => OptimumGate.SkipReasonFor(typeof(VanillaOnly), Method(typeof(VanillaOnly), nameof(VanillaOnly.Contradiction)), null));

        XUnit.ScenarioRunnerHarness.RunResult result = await XUnit.ScenarioRunnerHarness.RunAsync(typeof(VanillaOnly), nameof(VanillaOnly.Contradiction));
        Assert.Contains("[RequireOptimum] and [SkipOnOptimum]", string.Join("\n", Assert.Single(result.Failed).Messages));
    }

    [NotOptimumFact]
    public void OnThisVanillaInstall_TheScenarioGateSkips()
    {
        Assert.NotNull(Assert.Single(XUnit.ScenarioRunnerHarness.Discover(typeof(Samples), nameof(Samples.Requires))).SkipReason);
        Assert.Null(Assert.Single(XUnit.ScenarioRunnerHarness.Discover(typeof(Samples), nameof(Samples.Skips))).SkipReason);
    }

    [NotOptimumFact]
    public void APlainFact_CanBeKeptOffOptimum() => Assert.Null(OptimumInstall.Loaded);

    [Fact]
    public void ClientOptimum_SaysToGateTheTest_OnVanilla()
    {
        Assert.Contains("[RequireOptimum]", Assert.Throws<InvalidOperationException>(() => OptimumDriverMessage()).Message);
    }

    private static string OptimumDriverMessage() => throw new InvalidOperationException(typeof(OptimumDriver).GetMethod("NotOptimum", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, ["/opt/vs"]) as string);
}
