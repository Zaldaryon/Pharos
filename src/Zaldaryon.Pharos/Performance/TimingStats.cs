using System.Globalization;

namespace Zaldaryon.Pharos.Performance;

/// <summary>Durations measured once per frame or tick, summarised.</summary>
/// <remarks>
/// Percentiles use the nearest-rank method: P95 of 60 samples is the 57th smallest, and P99 of
/// fewer than 100 samples is the largest. <see cref="Samples"/> keeps every value, in the order measured.
/// </remarks>
public sealed class TimingStats
{
    private TimingStats(IReadOnlyList<TimeSpan> samples)
    {
        Samples = samples;
        if (samples.Count == 0) return;

        TimeSpan[] sorted = [.. samples];
        Array.Sort(sorted);
        Min = sorted[0];
        Max = sorted[^1];
        Median = Rank(sorted, 0.5);
        P95 = Rank(sorted, 0.95);
        P99 = Rank(sorted, 0.99);
        Total = sorted.Aggregate(TimeSpan.Zero, (sum, t) => sum + t);
        Mean = Total / sorted.Length;
    }

    /// <summary>The values, one per frame or tick, in the order measured.</summary>
    public IReadOnlyList<TimeSpan> Samples { get; }

    /// <summary>How many values there are.</summary>
    public int Count => Samples.Count;

    /// <summary>The smallest value.</summary>
    public TimeSpan Min { get; }

    /// <summary>The middle value (nearest rank).</summary>
    public TimeSpan Median { get; }

    /// <summary>The 95th percentile (nearest rank).</summary>
    public TimeSpan P95 { get; }

    /// <summary>The 99th percentile (nearest rank).</summary>
    public TimeSpan P99 { get; }

    /// <summary>The largest value.</summary>
    public TimeSpan Max { get; }

    /// <summary>The average.</summary>
    public TimeSpan Mean { get; }

    /// <summary>All the values added up.</summary>
    public TimeSpan Total { get; }

    /// <summary>The stats of <paramref name="samples"/>.</summary>
    public static TimingStats From(IEnumerable<TimeSpan> samples) => new([.. samples]);

    internal static TimingStats FromTicks(long[] stopwatchTicks, int count) =>
        new(stopwatchTicks.Take(count).Select(t => TimeSpan.FromSeconds(t / (double)System.Diagnostics.Stopwatch.Frequency)).ToArray());

    /// <inheritdoc/>
    public override string ToString() =>
        Count == 0 ? "no samples" : string.Create(CultureInfo.InvariantCulture,
            $"median {Ms(Median)}, p95 {Ms(P95)}, p99 {Ms(P99)}, min {Ms(Min)}, max {Ms(Max)}, mean {Ms(Mean)}, total {Ms(Total)} over {Count}");

    internal static string Ms(TimeSpan time) => string.Create(CultureInfo.InvariantCulture, $"{time.TotalMilliseconds:0.###} ms");

    private static TimeSpan Rank(TimeSpan[] sorted, double percentile) =>
        sorted[Math.Clamp((int)Math.Ceiling(percentile * sorted.Length) - 1, 0, sorted.Length - 1)];
}
