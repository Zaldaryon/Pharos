using Xunit;
using Zaldaryon.Pharos.Reporting;
using Zaldaryon.Pharos.Server;
using Zaldaryon.Pharos.Tests.XUnit;
using Zaldaryon.Pharos.XUnit;

namespace Zaldaryon.Pharos.Tests.Reporting;

/// <summary>Boot diagnostics of real hosts, and strict boots of sample scenario classes.</summary>
[Collection("Sequential")]
[Trait(PharosTraits.Category, PharosTraits.Live)]
public sealed class BootDiagnosticsLiveTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "pharos-boot-live-" + Guid.NewGuid().ToString("N")[..8]);

    public BootDiagnosticsLiveTests()
    {
        FailureArtifactWriter.RootOverride = _root;
    }

    public void Dispose()
    {
        FailureArtifactWriter.RootOverride = null;
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public void AVanillaServer_BootsClean_AndStopsCollectingOnceBooted()
    {
        using ServerSandbox sandbox = new();
        using EmbeddedServerHost host = EmbeddedServerHost.Boot(sandbox, new ServerWorldOptions { WorldName = "pharos-boot-clean" });

        BootDiagnostics boot = host.BootDiagnostics;
        Assert.True(boot.IsComplete);
        BootDiagnosticsResult result = boot.Check([]);
        Assert.True(result.Passed, "A vanilla server logged at boot:" + Environment.NewLine + result.Describe());

        host.RunOnGameThread(() => host.Server.Api.Logger.Warning("after the boot"));
        Assert.Empty(host.BootDiagnostics);
        Assert.Contains(host.Logs.Warnings, w => w.Message == "after the boot");
    }

    [Fact]
    public async Task AStrictClassWithABrokenMod_FailsItsBootOnce_WithArtifacts()
    {
        ScenarioRunnerHarness.RunResult first = await ScenarioRunnerHarness.RunAsync(typeof(StrictBroken), nameof(StrictBroken.First));

        Assert.Equal(typeof(ScenarioFailedException).FullName, first.FailureType);
        Assert.StartsWith(typeof(BootDiagnosticsException).FullName, first.FailureMessage);
        Assert.Contains("pharosbrokenmod", first.FailureMessage);
        string dir = Path.Combine(_root, nameof(StrictBroken), nameof(StrictBroken.First));
        Assert.Contains("pharosbrokenmod", File.ReadAllText(Path.Combine(dir, "boot-diagnostics.txt")));
        Assert.True(File.Exists(Path.Combine(dir, "server.log")));

        // The class is not booted again: the next test fails before it reaches the host, with
        // the same list, and has no artifacts of its own.
        ScenarioRunnerHarness.RunResult second = await ScenarioRunnerHarness.RunAsync(typeof(StrictBroken), nameof(StrictBroken.Second));
        Assert.Contains("is not booted again", second.FailureMessage);
        Assert.Contains("pharosbrokenmod", second.FailureMessage);
        Assert.False(Directory.Exists(Path.Combine(_root, nameof(StrictBroken), nameof(StrictBroken.Second))), "The second test booted and failed on its own");
    }

    [Fact]
    public async Task AllowingTheBrokenModsWarnings_LetsTheStrictBootPass()
    {
        ScenarioRunnerHarness.RunResult result = await ScenarioRunnerHarness.RunAsync(typeof(StrictBrokenAllowed), nameof(StrictBrokenAllowed.Boots));

        Assert.Single(result.Passed);
    }

    [Fact]
    public async Task AStrictVanillaPair_Boots_AndTheClientsBootEndsAtTheJoin()
    {
        ScenarioRunnerHarness.RunResult result = await ScenarioRunnerHarness.RunAsync(typeof(StrictPair), nameof(StrictPair.Joins));

        Assert.True(result.Passed.Count == 1, result.Failed.Count > 0 ? result.FailureMessage : "The test did not run");
    }

#pragma warning disable xUnit1000 // Samples are private so the real test run does not discover them.
    [StrictBoot]
    private sealed class StrictPair : ClientServerScenarioBase
    {
        protected override Zaldaryon.Pharos.Bootstrap.HeadlessClientOptions ClientOptions =>
            new() { BootMode = Zaldaryon.Pharos.Bootstrap.ClientBootMode.Engine, Width = 640, Height = 360 };

        [ClientServerScenario]
        public void Joins()
        {
            Assert.True(Client!.BootDiagnostics.IsComplete, "The joined client is still booting");
            Assert.True(ServerHost!.BootDiagnostics.IsComplete);
            Assert.True(UnexpectedBootDiagnostics.Passed, UnexpectedBootDiagnostics.Describe());
        }
    }

    [StrictBoot]
    [ServerMods("TestMods/pharosbrokenmod")]
    private sealed class StrictBroken : ServerScenarioBase
    {
        [ServerScenario]
        public void First() => throw new InvalidOperationException("The body ran although the boot failed.");

        [ServerScenario]
        public void Second() => throw new InvalidOperationException("The body ran although the boot failed.");
    }

    [StrictBoot]
    [ServerMods("TestMods/pharosbrokenmod")]
    [AllowBootDiagnostic("pharosbrokenmod", Required = true)]
    private sealed class StrictBrokenAllowed : ServerScenarioBase
    {
        [ServerScenario]
        public void Boots() => Assert.True(UnexpectedBootDiagnostics.Passed, UnexpectedBootDiagnostics.Describe());
    }
#pragma warning restore xUnit1000
}
