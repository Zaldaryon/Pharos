namespace Zaldaryon.Pharos.Bridge;

/// <summary>
/// Records named metrics from mod code. The bridge forwards them to the Pharos test running in
/// the same process; outside Pharos no one listens, so every call is a no-op.
/// </summary>
/// <remarks>
/// This type lives in the bridge assembly, which a mod can reference without referencing the
/// Pharos engine. Units are <c>count</c>, <c>ms</c>, <c>bytes</c> or <c>ratio</c>; directions are
/// <c>lowerIsBetter</c>, <c>higherIsBetter</c> or <c>neutral</c>.
/// </remarks>
public static class PharosMetrics
{
    /// <summary>Receives every reading: name, value, unit, direction and optional tags.</summary>
    public static event Action<string, double, string, string, IReadOnlyDictionary<string, string>?>? Recorded;

    /// <summary>Records one value.</summary>
    /// <param name="name">The metric's name.</param>
    /// <param name="value">The value.</param>
    /// <param name="unit">Its unit; <c>count</c> by default.</param>
    /// <param name="direction">Which way is better; <c>neutral</c> by default.</param>
    /// <param name="tags">Optional labels.</param>
    public static void Record(string name, double value, string unit = "count",
        string direction = "neutral", IReadOnlyDictionary<string, string>? tags = null)
    {
        if (string.IsNullOrWhiteSpace(name) || double.IsNaN(value) || double.IsInfinity(value)) return;
        Recorded?.Invoke(name, value, unit, direction, tags);
    }

    /// <summary>Records a count.</summary>
    public static void Count(string name, long count, string direction = "neutral",
        IReadOnlyDictionary<string, string>? tags = null) =>
        Record(name, count, "count", direction, tags);

    /// <summary>Records a byte total.</summary>
    public static void Bytes(string name, long bytes, string direction = "lowerIsBetter",
        IReadOnlyDictionary<string, string>? tags = null) =>
        Record(name, bytes, "bytes", direction, tags);

    /// <summary>Starts a stopwatch that records milliseconds when disposed.</summary>
    public static IDisposable Time(string name, string direction = "lowerIsBetter",
        IReadOnlyDictionary<string, string>? tags = null) => new Timer(name, direction, tags);

    private sealed class Timer(string name, string direction, IReadOnlyDictionary<string, string>? tags) : IDisposable
    {
        private readonly long _started = System.Diagnostics.Stopwatch.GetTimestamp();
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            Record(name, System.Diagnostics.Stopwatch.GetElapsedTime(_started).TotalMilliseconds, "ms", direction, tags);
        }
    }
}
