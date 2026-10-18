using Zaldaryon.Pharos.Bootstrap;
using Zaldaryon.Pharos.Core;

namespace Zaldaryon.Pharos.Scenarios;

/// <summary>
/// Formats standard scenario outputs, including the version prefix, random seeds, and attempt logs.
/// </summary>
public static class ScenarioOutput
{
    /// <summary>The version prefix banner printed at the start of every scenario output.</summary>
    public static string VersionBanner =>
        $"Vintage Story {InstalledGame.Version}{(OptimumInstall.Loaded is { } optimum ? $" ({OptimumInstall.Describe(optimum)})" : "")}, Pharos {Reporting.FailureArtifactWriter.PharosVersion}{Environment.NewLine}";

    /// <summary>Formats the history summary line for multiple attempts.</summary>
    public static string FormatHistory(int attemptCount, int maxAttempts, int requiredSuccesses, int successes, bool flaky, IReadOnlyList<ScenarioAttemptEntry> history)
    {
        string text = $"Pharos attempts: {attemptCount}/{maxAttempts}; required successes: {requiredSuccesses}; successes: {successes}; flaky={flaky.ToString().ToLowerInvariant()}.";
        if (history.Count > 1)
        {
            text += Environment.NewLine + string.Join(Environment.NewLine, history.Select(a => $"  attempt {a.Number}: {a.Outcome}{(string.IsNullOrWhiteSpace(a.Failure) ? "" : ": " + FirstLine(a.Failure))}"));
        }
        return text;
    }

    private static string FirstLine(string? text) => text?.Trim().Split('\n')[0] ?? "";
}
