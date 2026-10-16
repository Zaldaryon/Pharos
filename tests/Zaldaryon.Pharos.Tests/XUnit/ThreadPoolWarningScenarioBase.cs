using Xunit;
using Vintagestory.API.Common;
using Zaldaryon.Pharos.Reporting;
using Zaldaryon.Pharos.XUnit;

namespace Zaldaryon.Pharos.Tests.XUnit;

/// <summary>Fails a live server scenario when its background callback logs an exception.</summary>
public abstract class ThreadPoolWarningScenarioBase : ServerScenarioBase
{
    public override async Task DisposeAsync()
    {
        LogCapture? logs = Host?.Logs;
        await base.DisposeAsync();

        string[] warnings = CollectThreadPoolWarnings(logs?.Warnings);
        Assert.True(
            warnings.Length == 0,
            $"Unexpected thread-pool warnings during server teardown:\n{string.Join(Environment.NewLine, warnings)}");
    }

    internal static string[] CollectThreadPoolWarnings(IEnumerable<LogEntry>? warnings) => warnings?
            .Where(entry => entry.Message.StartsWith("QueueTask:", StringComparison.Ordinal)
                || entry.Message.StartsWith("Pharos thread pool", StringComparison.Ordinal))
            .Select(entry => entry.Message)
            .ToArray() ?? [];
}
