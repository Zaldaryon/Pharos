using Zaldaryon.Pharos.Assertions;
using Zaldaryon.Pharos.Reporting;

namespace Zaldaryon.Pharos.XUnit;

/// <summary>
/// Fails a scenario whose client or server logged an error it did not expect, when the scenario
/// opts in with <c>FailOnLoggedErrors</c>.
/// </summary>
internal static class LoggedErrorGate
{
    /// <summary>The most errors listed in the failure message.</summary>
    private const int Listed = 20;

    /// <summary>
    /// The errors in <paramref name="logs"/> that no fragment of <paramref name="allowed"/>
    /// matches. Taken before teardown, so what the game logs while it shuts down does not count.
    /// </summary>
    public static IReadOnlyList<LogEntry> Collect(bool enabled, IEnumerable<string> allowed, params LogCapture?[] logs)
    {
        if (!enabled) return [];

        string[] fragments = [.. allowed];
        return logs.Where(l => l != null).SelectMany(l => l!.UnexpectedErrors(fragments)).ToList();
    }

    /// <summary>Throws when <paramref name="errors"/> is not empty.</summary>
    /// <exception cref="PharosAssertException">Errors were logged.</exception>
    public static void ThrowIfAny(IReadOnlyList<LogEntry> errors)
    {
        if (errors.Count == 0) return;

        IEnumerable<string> lines = errors.Take(Listed).Select(e => "  " + e);
        string more = errors.Count > Listed ? $"{Environment.NewLine}  ... and {errors.Count - Listed} more" : "";
        throw new PharosAssertException(
            $"The scenario logged {errors.Count} unexpected error(s). Allow known ones with AllowedLoggedErrors:{Environment.NewLine}" +
            string.Join(Environment.NewLine, lines) + more);
    }
}
