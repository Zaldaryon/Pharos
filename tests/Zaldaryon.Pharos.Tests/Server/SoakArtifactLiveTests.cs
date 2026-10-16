using System.Text.Json;
using Xunit;
using Zaldaryon.Pharos.Reporting;
using Zaldaryon.Pharos.Tests.XUnit;
using Zaldaryon.Pharos.XUnit;

namespace Zaldaryon.Pharos.Tests.Server;

/// <summary>A failed soak run, through the real scenario pipeline, keeps its CSV and summary with the failure.</summary>
[Collection("Sequential")]
[Trait(PharosTraits.Category, PharosTraits.Live)]
public sealed class SoakArtifactLiveTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "pharos-soak-live-" + Guid.NewGuid().ToString("N")[..8]);

    public SoakArtifactLiveTests()
    {
        FailureArtifactWriter.RootOverride = _root;
    }

    public void Dispose()
    {
        FailureArtifactWriter.RootOverride = null;
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public async Task AFailedSoakRun_SavesItsCsvAndSummaryWithTheFailure()
    {
        ScenarioRunnerHarness.RunResult result = await ScenarioRunnerHarness.RunAsync(typeof(SoakingClass), nameof(SoakingClass.BreaksABudget));
        Assert.Single(result.Failed);

        string directory = Path.Combine(_root, nameof(SoakingClass), nameof(SoakingClass.BreaksABudget));
        Assert.True(File.Exists(Path.Combine(directory, "soak.csv")), "soak.csv was not written");
        using JsonDocument run = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "run.json")));
        JsonElement summary = run.RootElement.GetProperty("soak");
        Assert.False(summary.GetProperty("completed").GetBoolean());
        Assert.Equal("tick listeners", summary.GetProperty("breaches")[0].GetProperty("budget").GetString());
    }

#pragma warning disable xUnit1000 // Samples are private so the real test run does not discover them.
    [ServerWorld(seed: 4242, playStyle: "creativebuilding", worldType: "superflat", Isolation = WorldIsolation.Restart)]
    private sealed class SoakingClass : ServerScenarioBase
    {
        [ServerScenario]
        public async Task BreaksABudget()
        {
            // The server has more than one tick listener, so the first sample breaks the budget.
            await Host!.Simulate.DaysAsync(0.25, speed: 1440, monitor: m => m.TickListenersBelow(1));
        }
    }
#pragma warning restore xUnit1000
}
