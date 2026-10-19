using System.Text.Json.Serialization;

namespace Zaldaryon.Pharos.Metrics;

/// <summary>What a metric's values are measured in.</summary>
public enum MetricUnit
{
    /// <summary>A number of things, such as ticks, calls or entities.</summary>
    Count,

    /// <summary>Milliseconds.</summary>
    Milliseconds,

    /// <summary>A number of bytes.</summary>
    Bytes,

    /// <summary>A ratio, such as bytes per tick or a percentage written as a fraction.</summary>
    Ratio,
}

/// <summary>Which way a metric should move to be an improvement.</summary>
public enum MetricDirection
{
    /// <summary>A smaller value is better.</summary>
    LowerIsBetter,

    /// <summary>A larger value is better.</summary>
    HigherIsBetter,

    /// <summary>Neither direction is better, such as a count of features in use.</summary>
    Neutral,
}

/// <summary>Which clock produced a metric's values, so comparisons know how much to trust them.</summary>
public enum MetricClock
{
    /// <summary>A count, a byte total or a number of ticks: exact and comparable between runs.</summary>
    Exact,

    /// <summary>Simulated frame time.</summary>
    Frame,

    /// <summary>Simulated server tick time.</summary>
    Tick,

    /// <summary>Wall-clock time: noisy, and only comparable with repeated samples and statistics.</summary>
    Stopwatch,
}

/// <summary>Which automatic collectors run for a scenario.</summary>
[Flags]
public enum Metric
{
    /// <summary>No automatic collector.</summary>
    None = 0,

    /// <summary>Frame time, frame count and render-stage timings.</summary>
    Frame = 1 << 0,

    /// <summary>Server tick time and per-system tick cost.</summary>
    Tick = 1 << 1,

    /// <summary>Allocations, managed heap and garbage collections.</summary>
    Memory = 1 << 2,

    /// <summary>Draw calls and OpenGL object counts.</summary>
    Gl = 1 << 3,

    /// <summary>Packets and bytes per direction and channel.</summary>
    Network = 1 << 4,

    /// <summary>Loaded chunks, entities, block entities and tick listeners.</summary>
    World = 1 << 5,

    /// <summary>Boot and rollback durations.</summary>
    Boot = 1 << 6,

    /// <summary>Every automatic collector.</summary>
    All = Frame | Tick | Memory | Gl | Network | World | Boot,
}

/// <summary>How a metric's values are summarised in <c>result.json</c> and <c>run.json</c>.</summary>
public sealed record MetricSummary
{
    /// <summary>The unit, as <c>count</c>, <c>ms</c>, <c>bytes</c> or <c>ratio</c>.</summary>
    public required string Unit { get; init; }

    /// <summary>The preferred direction, as <c>lowerIsBetter</c>, <c>higherIsBetter</c> or <c>neutral</c>.</summary>
    public required string Direction { get; init; }

    /// <summary>The clock, as <c>exact</c>, <c>frame</c>, <c>tick</c> or <c>stopwatch</c>.</summary>
    public required string Clock { get; init; }

    /// <summary>The number of samples.</summary>
    public int Count { get; init; }

    /// <summary>The sum of the samples.</summary>
    public double Sum { get; init; }

    /// <summary>The smallest sample.</summary>
    public double Min { get; init; }

    /// <summary>The largest sample.</summary>
    public double Max { get; init; }

    /// <summary>The arithmetic mean.</summary>
    public double Mean { get; init; }

    /// <summary>The 50th percentile (the lower median for an even sample count).</summary>
    public double P50 { get; init; }

    /// <summary>The 95th percentile.</summary>
    public double P95 { get; init; }

    /// <summary>The 99th percentile.</summary>
    public double P99 { get; init; }

    /// <summary>
    /// The representative value: the sum for counts and bytes, the mean for times and ratios.
    /// A series with no samples has a value of 0.
    /// </summary>
    public double Value { get; init; }
}

/// <summary>One named metric and every value recorded for it during a test.</summary>
public sealed class MetricSeries
{
    internal MetricSeries(string name, MetricUnit unit, MetricDirection direction, MetricClock clock,
        IReadOnlyList<double> samples, IReadOnlyDictionary<string, string> tags)
    {
        Name = name;
        Unit = unit;
        Direction = direction;
        Clock = clock;
        Samples = samples;
        Tags = tags;
    }

    /// <summary>The metric's name, such as <c>tick.modsystem.mymod:ForgeSystem.ms</c>.</summary>
    public string Name { get; }

    /// <summary>What the values are measured in.</summary>
    public MetricUnit Unit { get; }

    /// <summary>Which way is better.</summary>
    public MetricDirection Direction { get; }

    /// <summary>Which clock produced the values.</summary>
    public MetricClock Clock { get; }

    /// <summary>Every recorded sample, oldest first.</summary>
    public IReadOnlyList<double> Samples { get; }

    /// <summary>Optional labels, such as the mod, side or channel the metric belongs to.</summary>
    public IReadOnlyDictionary<string, string> Tags { get; }

    /// <summary>The number of samples.</summary>
    public int Count => Samples.Count;

    /// <summary>The sum of the samples.</summary>
    public double Sum => Samples.Sum();

    /// <summary>The smallest sample, or 0 when there are none.</summary>
    public double Min => Samples.Count == 0 ? 0 : Samples.Min();

    /// <summary>The largest sample, or 0 when there are none.</summary>
    public double Max => Samples.Count == 0 ? 0 : Samples.Max();

    /// <summary>The arithmetic mean, or 0 when there are none.</summary>
    public double Mean => Samples.Count == 0 ? 0 : Sum / Samples.Count;

    /// <summary>
    /// The 50th percentile, or 0 when there are none. With nearest-rank this is the lower of the
    /// two middle samples for an even sample count.
    /// </summary>
    public double P50 => Percentile(50);

    /// <summary>The 95th percentile, or 0 when there are none.</summary>
    public double P95 => Percentile(95);

    /// <summary>The 99th percentile, or 0 when there are none.</summary>
    public double P99 => Percentile(99);

    /// <summary>
    /// The representative value: the sum for counts and bytes, the mean for times and ratios.
    /// </summary>
    public double Value => Unit is MetricUnit.Count or MetricUnit.Bytes ? Sum : Mean;

    /// <summary>The metric's unit as it is written to JSON.</summary>
    public string UnitName => MetricFormat.Unit(Unit);

    /// <summary>The metric's direction as it is written to JSON.</summary>
    public string DirectionName => MetricFormat.Direction(Direction);

    /// <summary>The metric's clock as it is written to JSON.</summary>
    public string ClockName => MetricFormat.Clock(Clock);

    /// <summary>This series as the compact summary written to <c>run.json</c>.</summary>
    public MetricSummary Summary() => new()
    {
        Unit = UnitName,
        Direction = DirectionName,
        Clock = ClockName,
        Count = Count,
        Sum = Sum,
        Min = Min,
        Max = Max,
        Mean = Mean,
        P50 = P50,
        P95 = P95,
        P99 = P99,
        Value = Value,
    };

    /// <summary>
    /// The nearest-rank percentile: P95 of 20 samples is the 19th smallest. A sample set of one
    /// returns that sample; an empty one returns 0.
    /// </summary>
    /// <param name="percent">The percentile, between 0 and 100.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="percent"/> is outside 0 to 100.</exception>
    public double Percentile(double percent)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(percent, 0);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(percent, 100);
        if (Samples.Count == 0) return 0;

        double[] sorted = [.. Samples];
        Array.Sort(sorted);
        int rank = (int)Math.Ceiling(percent / 100.0 * sorted.Length);
        return sorted[Math.Clamp(rank - 1, 0, sorted.Length - 1)];
    }
}

/// <summary>How metric names are written in JSON and on the command line.</summary>
public static class MetricFormat
{
    /// <summary>The unit's name: <c>count</c>, <c>ms</c>, <c>bytes</c> or <c>ratio</c>.</summary>
    public static string Unit(MetricUnit unit) => unit switch
    {
        MetricUnit.Count => "count",
        MetricUnit.Milliseconds => "ms",
        MetricUnit.Bytes => "bytes",
        MetricUnit.Ratio => "ratio",
        _ => throw new ArgumentOutOfRangeException(nameof(unit), unit, null),
    };

    /// <summary>The direction's name: <c>lowerIsBetter</c>, <c>higherIsBetter</c> or <c>neutral</c>.</summary>
    public static string Direction(MetricDirection direction) => direction switch
    {
        MetricDirection.LowerIsBetter => "lowerIsBetter",
        MetricDirection.HigherIsBetter => "higherIsBetter",
        MetricDirection.Neutral => "neutral",
        _ => throw new ArgumentOutOfRangeException(nameof(direction), direction, null),
    };

    /// <summary>The clock's name: <c>exact</c>, <c>frame</c>, <c>tick</c> or <c>stopwatch</c>.</summary>
    public static string Clock(MetricClock clock) => clock switch
    {
        MetricClock.Exact => "exact",
        MetricClock.Frame => "frame",
        MetricClock.Tick => "tick",
        MetricClock.Stopwatch => "stopwatch",
        _ => throw new ArgumentOutOfRangeException(nameof(clock), clock, null),
    };

    /// <summary>Parses a unit name written by <see cref="Unit(MetricUnit)"/>.</summary>
    public static bool TryParseUnit(string? text, out MetricUnit unit)
    {
        switch (text?.Trim().ToLowerInvariant())
        {
            case "count": unit = MetricUnit.Count; return true;
            case "ms" or "milliseconds": unit = MetricUnit.Milliseconds; return true;
            case "bytes" or "b": unit = MetricUnit.Bytes; return true;
            case "ratio" or "%" or "percent": unit = MetricUnit.Ratio; return true;
            default: unit = MetricUnit.Count; return false;
        }
    }

    /// <summary>Parses a direction name written by <see cref="Direction(MetricDirection)"/>.</summary>
    public static bool TryParseDirection(string? text, out MetricDirection direction)
    {
        switch (text?.Trim().ToLowerInvariant())
        {
            case "lowerisbetter" or "lower" or "down": direction = MetricDirection.LowerIsBetter; return true;
            case "higherisbetter" or "higher" or "up": direction = MetricDirection.HigherIsBetter; return true;
            case "neutral": direction = MetricDirection.Neutral; return true;
            default: direction = MetricDirection.Neutral; return false;
        }
    }

    /// <summary>Parses a clock name written by <see cref="Clock(MetricClock)"/>.</summary>
    public static bool TryParseClock(string? text, out MetricClock clock)
    {
        switch (text?.Trim().ToLowerInvariant())
        {
            case "exact": clock = MetricClock.Exact; return true;
            case "frame": clock = MetricClock.Frame; return true;
            case "tick": clock = MetricClock.Tick; return true;
            case "stopwatch" or "wall": clock = MetricClock.Stopwatch; return true;
            default: clock = MetricClock.Exact; return false;
        }
    }
}

/// <summary>One metric reading, for code that publishes a whole profile at once.</summary>
/// <param name="Name">The metric's name.</param>
/// <param name="Value">Its value.</param>
/// <param name="Unit">What the value is measured in.</param>
/// <param name="Direction">Which way is better.</param>
/// <param name="Clock">Which clock produced it.</param>
/// <param name="Tags">Optional labels.</param>
public sealed record MetricReading(
    string Name,
    double Value,
    MetricUnit Unit = MetricUnit.Count,
    MetricDirection Direction = MetricDirection.Neutral,
    MetricClock Clock = MetricClock.Exact,
    IReadOnlyDictionary<string, string>? Tags = null);
