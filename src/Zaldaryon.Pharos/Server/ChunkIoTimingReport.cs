namespace Zaldaryon.Pharos.Server;

/// <summary>
/// Immutable result of chunk IO timing analysis, tracking load times and
/// calculating performance metrics for parallel vs serial comparison.
/// </summary>
/// <param name="ChunksLoaded">Total number of chunks loaded during tracking.</param>
/// <param name="TotalTimeMs">Total elapsed time for all chunk loads in milliseconds.</param>
/// <param name="BaselineTotalTimeMs">Optional baseline total time for speedup calculation.</param>
public sealed record ChunkIoTimingReport(
    int ChunksLoaded,
    long TotalTimeMs,
    long? BaselineTotalTimeMs = null)
{
    /// <summary>
    /// Average time per chunk load in milliseconds.
    /// Returns 0 if no chunks were loaded.
    /// </summary>
    public double AverageTimePerChunkMs => ChunksLoaded == 0 ? 0.0 : (double)TotalTimeMs / ChunksLoaded;

    /// <summary>
    /// Speedup factor relative to baseline. Returns 1.0 if no baseline is set.
    /// Values greater than 1.0 indicate faster than baseline.
    /// </summary>
    public double SpeedupFactor => BaselineTotalTimeMs.HasValue && TotalTimeMs > 0
        ? (double)BaselineTotalTimeMs.Value / TotalTimeMs
        : 1.0;

    /// <summary>
    /// True if tracking captured any chunk loads.
    /// </summary>
    public bool HasData => ChunksLoaded > 0;

    /// <summary>
    /// True if a baseline comparison is available.
    /// </summary>
    public bool HasBaseline => BaselineTotalTimeMs.HasValue;

    /// <summary>
    /// Returns the time saved compared to baseline in milliseconds.
    /// Positive values indicate time savings, negative values indicate slower performance.
    /// Returns 0 if no baseline is set.
    /// </summary>
    public long TimeSavedMs => BaselineTotalTimeMs.HasValue
        ? BaselineTotalTimeMs.Value - TotalTimeMs
        : 0;

    /// <summary>
    /// Returns the percentage improvement over baseline.
    /// Positive values indicate improvement, negative values indicate regression.
    /// Returns 0 if no baseline is set or baseline is zero.
    /// </summary>
    public double ImprovementPercent => BaselineTotalTimeMs.HasValue && BaselineTotalTimeMs.Value > 0
        ? (double)(BaselineTotalTimeMs.Value - TotalTimeMs) / BaselineTotalTimeMs.Value * 100.0
        : 0.0;

    /// <summary>
    /// Creates an empty report with no data.
    /// </summary>
    public static ChunkIoTimingReport Empty => new(0, 0);

    /// <summary>
    /// Creates a report with the specified chunk count and total time.
    /// </summary>
    public static ChunkIoTimingReport Create(int chunksLoaded, long totalTimeMs) =>
        new(chunksLoaded, totalTimeMs);

    /// <summary>
    /// Creates a report with baseline comparison data.
    /// </summary>
    /// <param name="chunksLoaded">Number of chunks loaded.</param>
    /// <param name="totalTimeMs">Total time for this run.</param>
    /// <param name="baselineTimeMs">Baseline total time for comparison.</param>
    public static ChunkIoTimingReport WithBaseline(int chunksLoaded, long totalTimeMs, long baselineTimeMs) =>
        new(chunksLoaded, totalTimeMs, baselineTimeMs);

    /// <summary>
    /// Returns a new report with the specified baseline time added.
    /// </summary>
    public ChunkIoTimingReport AddBaseline(long baselineTimeMs) =>
        new(ChunksLoaded, TotalTimeMs, baselineTimeMs);

    /// <summary>
    /// Returns a new report with the baseline from another report.
    /// </summary>
    public ChunkIoTimingReport CompareToBaseline(ChunkIoTimingReport baseline)
    {
        ArgumentNullException.ThrowIfNull(baseline);
        return AddBaseline(baseline.TotalTimeMs);
    }

    public override string ToString()
    {
        string baseline = HasBaseline
            ? FormattableString.Invariant($", Speedup={SpeedupFactor:F2}x, Saved={TimeSavedMs}ms")
            : "";
        return FormattableString.Invariant(
            $"ChunkIoTimingReport(Chunks={ChunksLoaded}, Total={TotalTimeMs}ms, Avg={AverageTimePerChunkMs:F2}ms/chunk{baseline})");
    }
}
