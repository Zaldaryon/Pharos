using System.Diagnostics;
using Zaldaryon.Pharos.Reporting;

namespace Zaldaryon.Pharos.Benchmarks;

/// <summary>
/// Runs a collection of benchmarks and aggregates results.
/// Integrates with TestReportBuilder for comprehensive reporting.
/// </summary>
public sealed class BenchmarkSuite
{
    /// <summary>
    /// Runs all provided benchmarks and returns results.
    /// </summary>
    /// <param name="benchmarks">Benchmarks to run.</param>
    /// <returns>Ordered list of results matching input benchmark order.</returns>
    public IReadOnlyList<BenchmarkResult> Run(IEnumerable<PerformanceBenchmark> benchmarks)
    {
        ArgumentNullException.ThrowIfNull(benchmarks);

        var results = new List<BenchmarkResult>();
        foreach (var benchmark in benchmarks)
        {
            var result = benchmark.RunBenchmark();
            results.Add(result);
        }
        return results;
    }

    /// <summary>
    /// Runs benchmarks and integrates results into a TestReportBuilder.
    /// </summary>
    /// <param name="benchmarks">Benchmarks to run.</param>
    /// <param name="reportBuilder">Report builder to add results to.</param>
    /// <returns>List of benchmark results.</returns>
    public IReadOnlyList<BenchmarkResult> RunWithReport(
        IEnumerable<PerformanceBenchmark> benchmarks,
        TestReportBuilder reportBuilder)
    {
        ArgumentNullException.ThrowIfNull(benchmarks);
        ArgumentNullException.ThrowIfNull(reportBuilder);

        var results = Run(benchmarks);

        foreach (var result in results)
        {
            if (result.IsPassing)
            {
                reportBuilder.AddPassResult($"Benchmark:{result.Name}");
            }
            else
            {
                reportBuilder.AddFailResult($"Benchmark:{result.Name}", result.FormatSummary());
            }
        }

        return results;
    }

    /// <summary>
    /// Creates a summary of benchmark results.
    /// </summary>
    /// <param name="results">Results to summarize.</param>
    /// <returns>Summary object with aggregated statistics.</returns>
    public static BenchmarkSummary Summarize(IReadOnlyList<BenchmarkResult> results)
    {
        ArgumentNullException.ThrowIfNull(results);

        int passed = 0;
        int failed = 0;
        int improved = 0;
        int regressed = 0;
        float totalElapsed = 0;
        float totalBaseline = 0;

        foreach (var result in results)
        {
            if (result.IsPassing) passed++;
            else failed++;

            if (result.IsImprovement) improved++;
            if (result.IsSignificantRegression) regressed++;

            totalElapsed += result.ElapsedMs;
            totalBaseline += result.BaselineMs;
        }

        float avgDelta = results.Count > 0
            ? results.Average(r => r.DeltaPercent)
            : 0;

        return new BenchmarkSummary(
            TotalCount: results.Count,
            PassCount: passed,
            FailCount: failed,
            ImprovedCount: improved,
            RegressedCount: regressed,
            TotalElapsedMs: totalElapsed,
            TotalBaselineMs: totalBaseline,
            AverageDeltaPercent: avgDelta);
    }

    /// <summary>
    /// Formats all results as a multi-line summary string.
    /// </summary>
    /// <param name="results">Results to format.</param>
    /// <returns>Formatted string with one line per result plus summary.</returns>
    public static string FormatResults(IReadOnlyList<BenchmarkResult> results)
    {
        ArgumentNullException.ThrowIfNull(results);

        if (results.Count == 0)
        {
            return "No benchmarks run.";
        }

        var lines = new List<string>(results.Count + 3);
        lines.Add("=== Benchmark Results ===");
        
        foreach (var result in results)
        {
            lines.Add(result.FormatSummary());
        }

        var summary = Summarize(results);
        lines.Add("---");
        lines.Add(summary.FormatSummary());

        return string.Join(Environment.NewLine, lines);
    }
}

/// <summary>
/// Aggregated statistics for a benchmark suite run.
/// </summary>
/// <param name="TotalCount">Total number of benchmarks run.</param>
/// <param name="PassCount">Number of passing benchmarks.</param>
/// <param name="FailCount">Number of failing benchmarks.</param>
/// <param name="ImprovedCount">Number of benchmarks showing improvement.</param>
/// <param name="RegressedCount">Number of benchmarks with significant regression.</param>
/// <param name="TotalElapsedMs">Sum of all elapsed times.</param>
/// <param name="TotalBaselineMs">Sum of all baselines.</param>
/// <param name="AverageDeltaPercent">Average delta percentage across all benchmarks.</param>
public sealed record BenchmarkSummary(
    int TotalCount,
    int PassCount,
    int FailCount,
    int ImprovedCount,
    int RegressedCount,
    float TotalElapsedMs,
    float TotalBaselineMs,
    float AverageDeltaPercent)
{
    /// <summary>
    /// True if all benchmarks passed.
    /// </summary>
    public bool AllPassed => FailCount == 0;

    /// <summary>
    /// Pass rate as a percentage (0-100).
    /// </summary>
    public float PassRate => TotalCount > 0 ? PassCount * 100f / TotalCount : 100f;

    /// <summary>
    /// Formats the summary as a single line.
    /// </summary>
    public string FormatSummary()
    {
        string status = AllPassed ? "ALL PASSED" : $"{FailCount} FAILED";
        string deltaSign = AverageDeltaPercent >= 0 ? "+" : "";
        return $"Summary: {status} ({PassCount}/{TotalCount}), Total: {TotalElapsedMs:F2}ms, Avg delta: {deltaSign}{AverageDeltaPercent:F1}%";
    }
}
