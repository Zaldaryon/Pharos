namespace Zaldaryon.Pharos.Benchmarks;

/// <summary>
/// Raw per-iteration timings and descriptive statistics for one benchmark run.
/// Percentiles use the nearest-rank definition; standard deviation is population standard deviation.
/// </summary>
public sealed record BenchmarkTimingSummary
{
    private BenchmarkTimingSummary(IReadOnlyList<double> sampleTimingsMs)
    {
        SampleTimingsMs = sampleTimingsMs;
        SampleCount = sampleTimingsMs.Count;

        double[] sorted = sampleTimingsMs.Order().ToArray();
        MinMs = sorted[0];
        MedianMs = sorted.Length % 2 == 0
            ? (sorted[sorted.Length / 2 - 1] + sorted[sorted.Length / 2]) / 2.0
            : sorted[sorted.Length / 2];
        P95Ms = NearestRank(sorted, 0.95);
        P99Ms = NearestRank(sorted, 0.99);
        MeanMs = sorted.Average();
        StandardDeviationMs = Math.Sqrt(sorted.Sum(value => Math.Pow(value - MeanMs, 2)) / sorted.Length);
    }

    /// <summary>Raw timings in execution order, in milliseconds.</summary>
    public IReadOnlyList<double> SampleTimingsMs { get; }

    public int SampleCount { get; }
    public double MinMs { get; }
    public double MedianMs { get; }
    public double P95Ms { get; }
    public double P99Ms { get; }
    public double MeanMs { get; }
    public double StandardDeviationMs { get; }

    internal static BenchmarkTimingSummary Create(IEnumerable<double> sampleTimingsMs)
    {
        ArgumentNullException.ThrowIfNull(sampleTimingsMs);

        double[] samples = sampleTimingsMs.ToArray();
        if (samples.Length == 0)
        {
            throw new ArgumentException("At least one timing sample is required.", nameof(sampleTimingsMs));
        }

        if (samples.Any(sample => !double.IsFinite(sample) || sample < 0))
        {
            throw new ArgumentOutOfRangeException(nameof(sampleTimingsMs), "Timing samples must be finite and non-negative.");
        }

        return new BenchmarkTimingSummary(Array.AsReadOnly(samples));
    }

    private static double NearestRank(IReadOnlyList<double> sortedSamples, double percentile)
    {
        int rank = (int)Math.Ceiling(percentile * sortedSamples.Count);
        return sortedSamples[Math.Max(0, rank - 1)];
    }
}
