using Xunit;
using Zaldaryon.Pharos.Reporting;
using Zaldaryon.Pharos.XUnit;

namespace Zaldaryon.Pharos.Tests.XUnit;

/// <summary><see cref="ModSmokeTest"/> on real mods: one that plays cleanly and one that breaks during play.</summary>
[Collection("Sequential")]
[Trait(PharosTraits.Category, PharosTraits.Live)]
public sealed class ModSmokeTestLiveTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "pharos-smoke-live-" + Guid.NewGuid().ToString("N")[..8]);

    public ModSmokeTestLiveTests()
    {
        FailureArtifactWriter.RootOverride = _root;
    }

    public void Dispose()
    {
        FailureArtifactWriter.RootOverride = null;
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public async Task ACleanMod_PassesTheSmokeTest_WithItsCommands()
    {
        ScenarioRunnerHarness.RunResult result = await ScenarioRunnerHarness.RunAsync(typeof(CleanSmoke), nameof(ModSmokeTest.ModBootsJoinsAndPlays));

        Assert.True(result.Passed.Count == 1, result.Failed.Count > 0 ? result.FailureMessage : "The smoke test did not run");
    }

    [Fact]
    public async Task AModThatLogsAnErrorDuringPlay_FailsTheSmokeTest_WithArtifacts()
    {
        ScenarioRunnerHarness.RunResult result = await ScenarioRunnerHarness.RunAsync(typeof(BrokenSmoke), nameof(ModSmokeTest.ModBootsJoinsAndPlays));

        Assert.True(result.FailureMessage.Contains("Pharos error mod broke during play"), result.FailureMessage);
        Assert.True(File.Exists(Path.Combine(_root, nameof(BrokenSmoke), nameof(ModSmokeTest.ModBootsJoinsAndPlays), "screenshot.png")), "No screenshot of the failed smoke test");
    }

    [Fact]
    public async Task AFailingCommand_FailsTheSmokeTest()
    {
        ScenarioRunnerHarness.RunResult result = await ScenarioRunnerHarness.RunAsync(typeof(BadCommandSmoke), nameof(ModSmokeTest.ModBootsJoinsAndPlays));

        Assert.Contains("pharosnosuchcommand", result.FailureMessage);
    }

    [Fact]
    public async Task ASmokeTestWithoutMods_Fails()
    {
        ScenarioRunnerHarness.RunResult result = await ScenarioRunnerHarness.RunAsync(typeof(NoModSmoke), nameof(ModSmokeTest.ModBootsJoinsAndPlays));

        Assert.Contains("names no mod", result.FailureMessage);
    }

    [Fact]
    public void TheSmokeCommand_PassesACleanMod_AndFailsABrokenOneStrictly()
    {
        StringWriter stdout = new(), stderr = new();
        string artifacts = Path.Combine(_root, "cli");

        int clean = Zaldaryon.Pharos.Cli.SmokeCommand.Run(
            ["--mod", "TestMods/pharostestmod", "--frames", "120", "--command", "/time set day", "--artifacts", artifacts], stdout, stderr);
        Assert.True(clean == 0, stdout + Environment.NewLine + stderr);
        Assert.Contains("Smoke test passed.", stdout.ToString());
        Assert.True(File.Exists(Path.Combine(artifacts, "smoke.log")), "The game's output was not written to smoke.log");

        int broken = Zaldaryon.Pharos.Cli.SmokeCommand.Run(
            ["--mod", "TestMods/pharosbrokenmod", "--strict", "--artifacts", artifacts], stdout, stderr);
        Assert.Equal(1, broken);
        Assert.Contains("BootDiagnosticsException", stdout.ToString());
        Assert.NotEmpty(Directory.GetFiles(artifacts, "boot-diagnostics.txt", SearchOption.AllDirectories));
    }

#pragma warning disable xUnit1000 // Samples are private so the real test run does not discover them.
    [ServerMods("TestMods/pharostestmod")]
    [StrictBoot]
    private sealed class CleanSmoke : ModSmokeTest
    {
        protected override int SmokeFrames => 240;

        protected override IReadOnlyList<string> SmokeCommands => ["/time set day"];
    }

    [ServerMods("TestMods/pharoserrormod")]
    private sealed class BrokenSmoke : ModSmokeTest
    {
        // The mod logs its error a second after the join; the walk around takes longer than that.
        protected override int SmokeFrames => 600;
    }

    [ServerMods("TestMods/pharostestmod")]
    private sealed class BadCommandSmoke : ModSmokeTest
    {
        protected override IReadOnlyList<string> SmokeCommands => ["/pharosnosuchcommand"];
    }

    private sealed class NoModSmoke : ModSmokeTest;
#pragma warning restore xUnit1000
}
