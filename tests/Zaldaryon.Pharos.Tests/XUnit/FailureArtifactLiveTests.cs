using System.Text.Json;
using Xunit;
using Zaldaryon.Pharos.Bootstrap;
using Zaldaryon.Pharos.Core;
using Zaldaryon.Pharos.Reporting;
using Zaldaryon.Pharos.Server;
using Zaldaryon.Pharos.XUnit;

namespace Zaldaryon.Pharos.Tests.XUnit;

/// <summary>
/// Failure artifacts from real scenarios: sample scenario classes that fail on purpose are run
/// through the scenario pipeline, and what they leave behind is checked.
/// </summary>
[Collection("Sequential")]
[Trait(PharosTraits.Category, PharosTraits.Live)]
public sealed class FailureArtifactLiveTests : IDisposable
{
    private static readonly byte[] s_pngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
    private readonly string _root = Path.Combine(Path.GetTempPath(), "pharos-artifacts-live-" + Guid.NewGuid().ToString("N")[..8]);

    public FailureArtifactLiveTests()
    {
        FailureArtifactWriter.RootOverride = _root;
    }

    public void Dispose()
    {
        FailureArtifactWriter.RootOverride = null;
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public async Task FailingClientServerScenario_SavesScreenshotLogsAndRunInfo()
    {
        ScenarioRunnerHarness.RunResult result = await ScenarioRunnerHarness.RunAsync(typeof(FailingPair), nameof(FailingPair.Fails));

        Assert.Equal(typeof(ScenarioFailedException).FullName, result.FailureType);
        Assert.StartsWith("Xunit.Sdk.EqualException", result.FailureMessage);
        Assert.Contains("Pharos test error", result.FailureMessage);

        string dir = ArtifactDirectory(nameof(FailingPair), nameof(FailingPair.Fails));
        Assert.Contains(dir, result.FailureMessage);
        AssertPng(Path.Combine(dir, "screenshot.png"));
        Assert.Contains("Pharos test error", File.ReadAllText(Path.Combine(dir, "server.log")));
        Assert.True(File.Exists(Path.Combine(dir, "client.log")), "No client.log");
        Assert.True(File.Exists(Path.Combine(dir, "packets.json")), "No packets.json although the scenario asked for them");

        using JsonDocument run = JsonDocument.Parse(File.ReadAllText(Path.Combine(dir, "run.json")));
        Assert.Equal("failed", run.RootElement.GetProperty("outcome").GetString());
        Assert.Equal("Engine", run.RootElement.GetProperty("clientBootMode").GetString());
        Assert.True(run.RootElement.GetProperty("frames").GetInt64() >= 20, "run.json does not count the frames the test stepped");
        Assert.True(run.RootElement.GetProperty("serverTicks").GetInt64() > 0, "run.json does not count the server ticks");
    }

    [Fact]
    public async Task TimedOutScenario_IsStopped_Captured_AndTheNextScenarioRuns()
    {
        ScenarioRunnerHarness.RunResult timedOut = await ScenarioRunnerHarness.RunAsync(typeof(SteppingPair), nameof(SteppingPair.StepsForever));

        Assert.StartsWith("Xunit.Sdk.TestTimeoutException", timedOut.FailureMessage);
        string dir = ArtifactDirectory(nameof(SteppingPair), nameof(SteppingPair.StepsForever));
        AssertPng(Path.Combine(dir, "screenshot.png"));
        using (JsonDocument run = JsonDocument.Parse(File.ReadAllText(Path.Combine(dir, "run.json"))))
        {
            Assert.Equal("timedOut", run.RootElement.GetProperty("outcome").GetString());
        }

        // The stopped body let go of the game: the next scenario boots and passes.
        ScenarioRunnerHarness.RunResult next = await ScenarioRunnerHarness.RunAsync(typeof(SteppingPair), nameof(SteppingPair.Passes));
        Assert.Single(next.Passed);
    }

    [Fact]
    public async Task ScenarioThatCannotStart_SavesWhatItHas()
    {
        ScenarioRunnerHarness.RunResult result = await ScenarioRunnerHarness.RunAsync(typeof(NeverJoins), nameof(NeverJoins.Never));

        Assert.Equal(typeof(ScenarioFailedException).FullName, result.FailureType);
        Assert.StartsWith("System.TimeoutException", result.FailureMessage);
        Assert.Contains("failed before its test body ran", result.FailureMessage);

        string dir = ArtifactDirectory(nameof(NeverJoins), nameof(NeverJoins.Never));
        Assert.True(File.Exists(Path.Combine(dir, "server.log")), "No server.log");
        Assert.True(File.Exists(Path.Combine(dir, "run.json")), "No run.json");
    }

    [Fact]
    public async Task FailingServerScenario_SavesTheServerLog()
    {
        ScenarioRunnerHarness.RunResult result = await ScenarioRunnerHarness.RunAsync(typeof(FailingServer), nameof(FailingServer.Fails));

        Assert.Equal(typeof(ScenarioFailedException).FullName, result.FailureType);
        string dir = ArtifactDirectory(nameof(FailingServer), nameof(FailingServer.Fails));
        Assert.True(File.Exists(Path.Combine(dir, "server.log")), "No server.log");
        Assert.True(Directory.Exists(Path.Combine(dir, "server-logs")), "The game's own server logs were not copied");
        Assert.False(File.Exists(Path.Combine(dir, "screenshot.png")), "A server scenario has no client to take a screenshot of");
    }

    [Fact]
    public async Task LoggedErrors_FailTheScenarioOnce_WithArtifacts()
    {
        ScenarioRunnerHarness.RunResult result = await ScenarioRunnerHarness.RunAsync(typeof(LogsAnError), nameof(LogsAnError.Passes));

        Assert.Single(result.SingleFailure.ExceptionTypes);
        Assert.StartsWith("Zaldaryon.Pharos.Assertions.PharosAssertException", result.FailureMessage);
        Assert.True(File.Exists(Path.Combine(ArtifactDirectory(nameof(LogsAnError), nameof(LogsAnError.Passes)), "server.log")));
    }

    private string ArtifactDirectory(string sampleClass, string method)
    {
        string dir = Path.Combine(_root, sampleClass, method);
        Assert.True(Directory.Exists(dir), $"No artifacts in {dir}; found: {string.Join(", ", Directory.Exists(_root) ? Directory.GetDirectories(_root, "*", SearchOption.AllDirectories) : [])}");
        return dir;
    }

    private static void AssertPng(string path)
    {
        Assert.True(File.Exists(path), $"No {Path.GetFileName(path)}");
        byte[] bytes = File.ReadAllBytes(path);
        Assert.True(bytes.Length > 1000, $"The screenshot is only {bytes.Length} bytes");
        Assert.Equal(s_pngSignature, bytes[..8]);
    }

#pragma warning disable xUnit1000 // Samples are private so the real test run does not discover them.
    private sealed class FailingPair : ClientServerScenarioBase
    {
        protected override HeadlessClientOptions ClientOptions => new() { BootMode = ClientBootMode.Engine, Width = 640, Height = 360 };

        protected override WorldIsolation WorldIsolation => WorldIsolation.Restart;

        protected override FailureArtifacts Artifacts => FailureArtifacts.All;

        [ClientServerScenario]
        public async Task Fails()
        {
            await Session!.StepFramesAsync(20);
            ServerHost!.RunOnGameThread(() => Server!.Api.Logger.Error("Pharos test error"));
            Assert.Equal(1, 2);
        }
    }

    private sealed class SteppingPair : ClientServerScenarioBase
    {
        protected override HeadlessClientOptions ClientOptions => new() { BootMode = ClientBootMode.Engine, Width = 640, Height = 360 };

        [ClientServerScenario(TimeoutMs = 5000)]
        public async Task StepsForever()
        {
            while (true)
            {
                await Session!.StepFramesAsync(1);
            }
        }

        [ClientServerScenario]
        public async Task Passes() => await Session!.StepFramesAsync(5);
    }

    private sealed class NeverJoins : ClientServerScenarioBase
    {
        protected override HeadlessClientOptions ClientOptions => new() { BootMode = ClientBootMode.Engine, Width = 640, Height = 360 };

        protected override TimeSpan PlayerJoinTimeout => TimeSpan.FromMilliseconds(1);

        [ClientServerScenario]
        public void Never() => throw new InvalidOperationException("The body ran although the player never joined.");
    }

    private sealed class FailingServer : ServerScenarioBase
    {
        [ServerScenario]
        public void Fails()
        {
            Host!.Ticks(5);
            Assert.Fail("server scenario failed on purpose");
        }
    }

    private sealed class LogsAnError : ServerScenarioBase
    {
        protected override bool FailOnLoggedErrors => true;

        [ServerScenario]
        public void Passes() => Host!.RunOnGameThread(() => Api!.Logger.Error("Pharos gate error"));
    }
#pragma warning restore xUnit1000
}
