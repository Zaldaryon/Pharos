using Vintagestory.API.Common;
using Vintagestory.Server;
using Xunit;
using Zaldaryon.Pharos.Reporting;
using Zaldaryon.Pharos.XUnit;

namespace Zaldaryon.Pharos.Tests.XUnit;

public sealed class ThreadPoolWarningScenarioBaseTests
{
    [Fact]
    public void WarningGateSelectsThreadPoolFailuresAndIgnoresUnrelatedWarnings()
    {
        LogEntry[] warnings =
        [
            new(EnumAppSide.Server, EnumLogType.Warning, "QueueTask: callback threw an exception"),
            new(EnumAppSide.Server, EnumLogType.Warning, "Pharos thread pool did not become idle within 12000ms after Stop"),
            new(EnumAppSide.Server, EnumLogType.Warning, "Pharos thread pool drain could not attach to server system disposal"),
            new(EnumAppSide.Server, EnumLogType.Warning, "Unrelated server warning"),
        ];

        string[] selected = ThreadPoolWarningScenarioBase.CollectThreadPoolWarnings(warnings);

        Assert.Equal(3, selected.Length);
        Assert.Contains(selected, message => message.StartsWith("QueueTask:", StringComparison.Ordinal));
        Assert.Contains(selected, message => message.StartsWith("Pharos thread pool did not become idle", StringComparison.Ordinal));
        Assert.Contains(selected, message => message.StartsWith("Pharos thread pool drain", StringComparison.Ordinal));
    }
}

[Collection("Sequential")]
[Trait(PharosTraits.Category, PharosTraits.Live)]
public sealed class ThreadPoolWarningScenarioBaseLiveTests
{
    [Fact]
    public async Task CapturedThreadPoolWarningFailsTheScenarioDuringTeardown()
    {
        string root = Path.Combine(Path.GetTempPath(), "pharos-thread-pool-warning-" + Guid.NewGuid().ToString("N")[..8]);
        FailureArtifactWriter.RootOverride = root;
        try
        {
            ScenarioRunnerHarness.RunResult result = await ScenarioRunnerHarness.RunAsync(
                typeof(WarningScenario),
                nameof(WarningScenario.LogsThreadPoolWarning));

            Assert.Equal(typeof(Xunit.Sdk.TrueException).FullName, result.FailureType);
            Assert.Contains("Unexpected thread-pool warnings during server teardown", result.FailureMessage);
            Assert.Contains("QueueTask: expected teardown-gate failure", result.FailureMessage);
        }
        finally
        {
            FailureArtifactWriter.RootOverride = null;
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [ServerWorld(seed: 4242, Isolation = Isolation.Restart)]
#pragma warning disable xUnit1000
    private sealed class WarningScenario : ThreadPoolWarningScenarioBase
    {
        [ServerScenario]
        public void LogsThreadPoolWarning()
        {
            ServerMain.Logger!.Warning("QueueTask: expected teardown-gate failure");
            ServerMain.Logger.Warning("Pharos thread pool drain could not attach to server system disposal");
        }
    }
#pragma warning restore xUnit1000
}
