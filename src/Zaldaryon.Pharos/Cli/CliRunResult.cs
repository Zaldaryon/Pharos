using System.Text;

namespace Zaldaryon.Pharos.Cli;

/// <summary>
/// Aggregate result of a CLI test run.
/// </summary>
/// <param name="PassCount">Number of tests that passed.</param>
/// <param name="FailCount">Number of tests that failed.</param>
/// <param name="SkippedCount">Number of tests that were skipped.</param>
/// <param name="DurationMs">Total execution time in milliseconds.</param>
/// <param name="Failures">Detailed information about each failure.</param>
public sealed record CliRunResult(
    int PassCount,
    int FailCount,
    int SkippedCount,
    long DurationMs,
    IReadOnlyList<CliTestFailure> Failures)
{
    /// <summary>
    /// Total number of tests executed (pass + fail + skipped).
    /// </summary>
    public int TotalCount => PassCount + FailCount + SkippedCount;

    /// <summary>
    /// Whether all executed tests passed (no failures).
    /// </summary>
    public bool IsSuccess => FailCount == 0;

    /// <summary>
    /// Gets the appropriate exit code for this result.
    /// </summary>
    public int ExitCode => FailCount > 0 ? 1 : 0;

    /// <summary>
    /// Pass rate as a percentage (0-100).
    /// Returns 100 if no tests were executed.
    /// </summary>
    public float PassRate
    {
        get
        {
            int executed = PassCount + FailCount;
            return executed > 0 ? (float)PassCount / executed * 100f : 100f;
        }
    }

    /// <summary>
    /// Creates an empty result for when no tests are found.
    /// </summary>
    public static CliRunResult Empty(long durationMs = 0) =>
        new(0, 0, 0, durationMs, []);

    /// <summary>
    /// Creates an error result with a single failure.
    /// </summary>
    public static CliRunResult Error(string message, long durationMs = 0) =>
        new(0, 1, 0, durationMs, [new CliTestFailure("CLI", message, string.Empty)]);

    /// <summary>
    /// Formats a summary string for console output.
    /// </summary>
    /// <param name="verbose">Include detailed failure information.</param>
    /// <returns>Formatted summary string.</returns>
    public string FormatSummary(bool verbose = false)
    {
        var sb = new StringBuilder();

        sb.AppendLine();
        sb.AppendLine("═══════════════════════════════════════════════════════════");
        sb.AppendLine("                    TEST RESULTS SUMMARY                    ");
        sb.AppendLine("═══════════════════════════════════════════════════════════");
        sb.AppendLine();

        if (IsSuccess)
        {
            sb.AppendLine($"  ✓ All tests passed!");
        }
        else
        {
            sb.AppendLine($"  ✗ {FailCount} test(s) failed");
        }

        sb.AppendLine();
        sb.AppendLine($"  Total:    {TotalCount}");
        sb.AppendLine($"  Passed:   {PassCount}");
        sb.AppendLine($"  Failed:   {FailCount}");
        sb.AppendLine($"  Skipped:  {SkippedCount}");
        sb.AppendLine($"  Duration: {DurationMs} ms");
        sb.AppendLine($"  Pass Rate: {PassRate:F1}%");
        sb.AppendLine();

        if (Failures.Count > 0)
        {
            sb.AppendLine("───────────────────────────────────────────────────────────");
            sb.AppendLine("                       FAILURES                            ");
            sb.AppendLine("───────────────────────────────────────────────────────────");
            sb.AppendLine();

            foreach (var failure in Failures)
            {
                sb.AppendLine(failure.Format(verbose));
                sb.AppendLine();
            }
        }

        sb.AppendLine("═══════════════════════════════════════════════════════════");

        return sb.ToString();
    }
}
