using System.Collections.Concurrent;

namespace Zaldaryon.Pharos.Metrics;

/// <summary>
/// Collects named metrics for one test. Thread-safe: the client thread, the server thread and the
/// test body may all record into it.
/// </summary>
public class MetricsCollector
{
    private readonly ConcurrentDictionary<string, SeriesBuilder> _series = new(StringComparer.Ordinal);

    /// <summary>Records one value.</summary>
    /// <param name="name">The metric's name. Names are compared exactly.</param>
    /// <param name="value">The value.</param>
    /// <param name="unit">What the value is measured in.</param>
    /// <param name="direction">Which way is better.</param>
    /// <param name="clock">Which clock produced the value; milliseconds default to the stopwatch, everything else to exact.</param>
    /// <param name="tags">Optional labels; the first reading of a name defines its labels.</param>
    /// <exception cref="ArgumentException"><paramref name="name"/> is empty or whitespace.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="value"/> is not a finite number.</exception>
    public virtual void Record(string name, double value, MetricUnit unit = MetricUnit.Count,
        MetricDirection direction = MetricDirection.Neutral, MetricClock? clock = null,
        IReadOnlyDictionary<string, string>? tags = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            throw new ArgumentOutOfRangeException(nameof(value), value, "A metric value must be a finite number.");
        }

        MetricClock actualClock = clock ?? (unit == MetricUnit.Milliseconds ? MetricClock.Stopwatch : MetricClock.Exact);
        string key = name.Trim();
        SeriesBuilder builder = _series.GetOrAdd(key, _ => new SeriesBuilder(key, unit, direction, actualClock, Copy(tags)));
        builder.Add(value);
    }

    /// <summary>Records a count.</summary>
    public void Count(string name, long count, MetricDirection direction = MetricDirection.Neutral,
        IReadOnlyDictionary<string, string>? tags = null) =>
        Record(name, count, MetricUnit.Count, direction, MetricClock.Exact, tags);

    /// <summary>Records a byte total.</summary>
    public void Bytes(string name, long bytes, MetricDirection direction = MetricDirection.LowerIsBetter,
        IReadOnlyDictionary<string, string>? tags = null) =>
        Record(name, bytes, MetricUnit.Bytes, direction, MetricClock.Exact, tags);

    /// <summary>Starts a stopwatch that records its elapsed milliseconds when disposed.</summary>
    /// <param name="name">The metric's name.</param>
    /// <param name="direction">Which way is better.</param>
    /// <param name="tags">Optional labels.</param>
    public MetricTimer Time(string name, MetricDirection direction = MetricDirection.LowerIsBetter,
        IReadOnlyDictionary<string, string>? tags = null) => new(this, name, direction, tags);

    /// <summary>The series collected so far, sorted by name.</summary>
    public IReadOnlyDictionary<string, MetricSeries> Snapshot()
    {
        SortedDictionary<string, MetricSeries> snapshot = new(StringComparer.Ordinal);
        foreach ((string name, SeriesBuilder builder) in _series)
        {
            snapshot[name] = builder.Build();
        }

        return snapshot;
    }

    /// <summary>The compact per-metric summaries written to <c>run.json</c>, sorted by name.</summary>
    public IReadOnlyDictionary<string, MetricSummary> Summaries()
    {
        SortedDictionary<string, MetricSummary> summaries = new(StringComparer.Ordinal);
        foreach ((string name, MetricSeries series) in Snapshot())
        {
            summaries[name] = series.Summary();
        }

        return summaries;
    }

    /// <summary>Whether anything was recorded.</summary>
    public bool IsEmpty => _series.IsEmpty;

    /// <summary>The number of distinct metric names.</summary>
    public int SeriesCount => _series.Count;

    /// <summary>Adds every reading to this collector.</summary>
    public void Publish(IEnumerable<MetricReading> readings)
    {
        ArgumentNullException.ThrowIfNull(readings);
        foreach (MetricReading reading in readings)
        {
            Record(reading.Name, reading.Value, reading.Unit, reading.Direction, reading.Clock, reading.Tags);
        }
    }

    /// <summary>Adds another collector's series, keeping this collector's labels when a name repeats.</summary>
    public void Merge(MetricsCollector other)
    {
        ArgumentNullException.ThrowIfNull(other);
        foreach (MetricSeries series in other.Snapshot().Values)
        {
            foreach (double sample in series.Samples)
            {
                Record(series.Name, sample, series.Unit, series.Direction, series.Clock, series.Tags);
            }
        }
    }

    private static IReadOnlyDictionary<string, string> Copy(IReadOnlyDictionary<string, string>? tags) =>
        tags is null || tags.Count == 0
            ? EmptyTags
            : new Dictionary<string, string>(tags, StringComparer.Ordinal);

    internal static readonly IReadOnlyDictionary<string, string> EmptyTags =
        new Dictionary<string, string>(0, StringComparer.Ordinal);

    private sealed class SeriesBuilder(string name, MetricUnit unit, MetricDirection direction, MetricClock clock,
        IReadOnlyDictionary<string, string> tags)
    {
        private readonly List<double> _samples = [];
        private readonly object _lock = new();

        public void Add(double value)
        {
            lock (_lock) _samples.Add(value);
        }

        public MetricSeries Build()
        {
            lock (_lock)
            {
                // The first reading wins for labels, units and clock; later readings of the same
                // name keep the series comparable instead of silently mixing units.
                return new MetricSeries(name, unit, direction, clock, [.. _samples], tags);
            }
        }
    }
}

/// <summary>Times one operation and records its milliseconds when disposed.</summary>
public sealed class MetricTimer : IDisposable
{
    private readonly MetricsCollector _collector;
    private readonly string _name;
    private readonly MetricDirection _direction;
    private readonly IReadOnlyDictionary<string, string>? _tags;
    private readonly long _started = System.Diagnostics.Stopwatch.GetTimestamp();
    private int _disposed;

    internal MetricTimer(MetricsCollector collector, string name, MetricDirection direction,
        IReadOnlyDictionary<string, string>? tags)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        _collector = collector ?? throw new ArgumentNullException(nameof(collector));
        _name = name;
        _direction = direction;
        _tags = tags;
    }

    /// <summary>How long has passed since the timer started.</summary>
    public TimeSpan Elapsed => System.Diagnostics.Stopwatch.GetElapsedTime(_started);

    /// <summary>Records the elapsed time, once.</summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _collector.Record(_name, Elapsed.TotalMilliseconds, MetricUnit.Milliseconds, _direction,
            MetricClock.Stopwatch, _tags);
    }
}
