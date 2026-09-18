namespace Zaldaryon.Pharos.Benchmarks;

/// <summary>
/// Result of a single benchmark run, including timing and regression detection.
/// </summary>
/// <param name="Name">Benchmark name.</param>
/// <param name="ElapsedMs">Actual elapsed time in milliseconds.</param>
/// <param name="BaselineMs">Expected baseline time in milliseconds.</param>
/// <param name="IsPassing">True if elapsed is within acceptable threshold (≤ 1.2× baseline).</param>
/// <param name="DeltaPercent">Percentage difference from baseline ((elapsed - baseline) / baseline × 100).</param>
public sealed record BenchmarkResult(
    string Name,
    float ElapsedMs,
    float BaselineMs,
    bool IsPassing,
    float DeltaPercent)
{
    /// <summary>
    /// Default regression threshold: 20% slower than baseline.
    /// </summary>
    public const float DefaultThreshold = 1.2f;

    /// <summary>
    /// Creates a result with automatic passing/delta calculation.
    /// </summary>
    /// <param name="name">Benchmark name.</param>
    /// <param name="elapsedMs">Measured elapsed time.</param>
    /// <param name="baselineMs">Expected baseline time.</param>
    /// <param name="threshold">Regression threshold multiplier (default: 1.2).</param>
    /// <returns>A new BenchmarkResult with computed IsPassing and DeltaPercent.</returns>
    public static BenchmarkResult Create(string name, float elapsedMs, float baselineMs, float threshold = DefaultThreshold)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        if (baselineMs <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(baselineMs), baselineMs, "Baseline must be positive.");
        }

        float deltaPercent = (elapsedMs - baselineMs) / baselineMs * 100f;
        bool isPassing = elapsedMs <= baselineMs * threshold;

        return new BenchmarkResult(name, elapsedMs, baselineMs, isPassing, deltaPercent);
    }

    /// <summary>
    /// Formats the result as a human-readable summary line.
    /// </summary>
    public string FormatSummary()
    {
        string status = IsPassing ? "PASS" : "FAIL";
        string deltaSign = DeltaPercent >= 0 ? "+" : "";
        return FormattableString.Invariant($"[{status}] {Name}: {ElapsedMs:F2}ms (baseline: {BaselineMs:F2}ms, {deltaSign}{DeltaPercent:F1}%)");
    }

    /// <summary>
    /// True if this result represents a significant regression (elapsed > baseline × 1.5).
    /// </summary>
    public bool IsSignificantRegression => ElapsedMs > BaselineMs * 1.5f;

    /// <summary>
    /// True if this result represents a performance improvement (elapsed < baseline × 0.9).
    /// </summary>
    public bool IsImprovement => ElapsedMs < BaselineMs * 0.9f;
}
