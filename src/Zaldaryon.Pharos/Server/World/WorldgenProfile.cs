using System.Globalization;
using Zaldaryon.Pharos.Metrics;
using Zaldaryon.Pharos.Performance;

namespace Zaldaryon.Pharos.Server;

/// <summary>
/// What one world generator cost while a chunk area was generated: its pass, the mod that
/// registered it, how often it ran, and how long each column, region or map chunk took.
/// </summary>
/// <remarks>
/// Times are the CPU time of the handler's own calls, measured with
/// <see cref="System.Diagnostics.Stopwatch.GetTimestamp"/> on whichever worldgen thread ran it.
/// They are noisy and meant for comparison between two runs; <see cref="Calls"/> and
/// <see cref="Items"/> are exact.
/// </remarks>
public sealed class WorldgenHandlerProfile
{
    private readonly TimingStats _stats;

    internal WorldgenHandlerProfile(string stage, string handler, string? mod, long calls,
        IReadOnlyList<double> perItemMs, TimingStats stats)
    {
        Stage = stage;
        Handler = handler;
        Mod = mod;
        Calls = calls;
        PerItemMs = perItemMs;
        _stats = stats;
    }

    /// <summary><c>terrain</c>, <c>terrain_features</c>, <c>vegetation</c>, <c>sunlight_flood</c>, <c>done</c>, <c>region</c> or <c>mapchunk</c>.</summary>
    public string Stage { get; }

    /// <summary>The handler's type and method, as <see cref="ModOwners.Describe"/> names it.</summary>
    public string Handler { get; }

    /// <summary>The mod that registered the handler, or null for the game's own generators.</summary>
    public string? Mod { get; }

    /// <summary>The mod id, or <c>game</c> for a generator that belongs to no mod.</summary>
    public string Owner => Mod ?? "game";

    /// <summary>Whether this handler runs per chunk column, rather than per map region or map chunk.</summary>
    public bool IsPass => Stage is not ("region" or "mapchunk");

    /// <summary>How many times the handler ran.</summary>
    public long Calls { get; }

    /// <summary>
    /// How many columns, regions or map chunks it ran for. A handler registered for a pass that a
    /// column skipped does not count that column.
    /// </summary>
    public int Items => PerItemMs.Count;

    /// <summary>The time for each column, region or map chunk, in the order the work finished.</summary>
    public IReadOnlyList<double> PerItemMs { get; }

    /// <summary>The sum of the per-item times, in milliseconds.</summary>
    public double TotalMs => _stats.Total.TotalMilliseconds;

    /// <summary>The mean time of one column, region or map chunk, in milliseconds.</summary>
    public double MeanMsPerItem => _stats.Mean.TotalMilliseconds;

    /// <summary>The 95th percentile of the per-item time, in milliseconds.</summary>
    public double P95MsPerItem => _stats.P95.TotalMilliseconds;

    /// <summary>The slowest column, region or map chunk, in milliseconds.</summary>
    public double MaxMsPerItem => _stats.Max.TotalMilliseconds;

    /// <summary>
    /// The handler's numbers as named metrics: one sample per item for <c>.ms</c>, and exact
    /// <c>.calls</c> and <c>.items</c> counts.
    /// </summary>
    internal void AddMetrics(List<MetricReading> readings)
    {
        string name = $"worldgen.{Stage}.{Owner}.{Handler}";
        foreach (double sample in PerItemMs)
        {
            readings.Add(new MetricReading(name + ".ms", sample, MetricUnit.Milliseconds,
                MetricDirection.LowerIsBetter, MetricClock.Stopwatch));
        }

        readings.Add(new MetricReading(name + ".calls", Calls, MetricUnit.Count,
            MetricDirection.Neutral, MetricClock.Exact));
        readings.Add(new MetricReading(name + ".items", Items, MetricUnit.Count,
            MetricDirection.Neutral, MetricClock.Exact));
    }

    /// <inheritdoc/>
    public override string ToString() => string.Create(CultureInfo.InvariantCulture,
        $"{Stage} {Owner} {Handler}: {MeanMsPerItem:F3} ms per item, {Calls} calls over {Items} items");
}

/// <summary>
/// What one mod's world generators cost together: its per-column samples summed across its handlers
/// and passes, so a mod that registers handlers in several passes appears once.
/// </summary>
public sealed class WorldgenModProfile
{
    private readonly TimingStats _stats;

    internal WorldgenModProfile(string owner, string? mod, long calls, int columns,
        IReadOnlyList<double> perColumnMs, IReadOnlyList<WorldgenHandlerProfile> handlers, TimingStats stats)
    {
        Owner = owner;
        Mod = mod;
        Calls = calls;
        Columns = columns;
        PerColumnMs = perColumnMs;
        Handlers = handlers;
        _stats = stats;
    }

    /// <summary>The mod id, or <c>game</c> for the game's own generators.</summary>
    public string Owner { get; }

    /// <summary>The mod id, or null for the game's own generators.</summary>
    public string? Mod { get; }

    /// <summary>How many times the mod's handlers ran.</summary>
    public long Calls { get; }

    /// <summary>How many columns the mod's handlers ran for.</summary>
    public int Columns { get; }

    /// <summary>The time for each column the mod's handlers touched, summed across its handlers.</summary>
    public IReadOnlyList<double> PerColumnMs { get; }

    /// <summary>The mod's handlers that ran, most expensive first.</summary>
    public IReadOnlyList<WorldgenHandlerProfile> Handlers { get; }

    /// <summary>The sum of the per-column times, in milliseconds.</summary>
    public double TotalMs => _stats.Total.TotalMilliseconds;

    /// <summary>The mean cost of one column the mod's handlers touched, in milliseconds.</summary>
    public double MeanMsPerColumn => _stats.Mean.TotalMilliseconds;

    /// <summary>The 95th percentile of the per-column time, in milliseconds.</summary>
    public double P95MsPerColumn => _stats.P95.TotalMilliseconds;

    /// <summary>The slowest column the mod's handlers touched, in milliseconds.</summary>
    public double MaxMsPerColumn => _stats.Max.TotalMilliseconds;

    internal void AddMetrics(List<MetricReading> readings)
    {
        string name = $"worldgen.mod.{Owner}";
        foreach (double sample in PerColumnMs)
        {
            readings.Add(new MetricReading(name + ".ms", sample, MetricUnit.Milliseconds,
                MetricDirection.LowerIsBetter, MetricClock.Stopwatch));
        }

        readings.Add(new MetricReading(name + ".calls", Calls, MetricUnit.Count,
            MetricDirection.Neutral, MetricClock.Exact));
        readings.Add(new MetricReading(name + ".columns", Columns, MetricUnit.Count,
            MetricDirection.Neutral, MetricClock.Exact));
    }

    /// <inheritdoc/>
    public override string ToString() => string.Create(CultureInfo.InvariantCulture,
        $"{Owner}: {MeanMsPerColumn:F3} ms per column over {Columns} columns, {Calls} calls");
}

/// <summary>
/// What generating a fresh chunk area cost: the wall time, how many columns were requested and
/// generated, and one entry per worldgen handler.
/// </summary>
/// <remarks>
/// The game generates neighbouring columns too, because a column's terrain features and vegetation
/// need its neighbours. <see cref="Columns"/> counts every column any handler ran for, which can be
/// larger than <see cref="RequestedColumns"/>; the per-column samples include those neighbours
/// because the request caused them.
/// </remarks>
public sealed class WorldgenProfile : IMetricSource
{
    internal WorldgenProfile(int requestedColumns, int columns, double wallMs,
        IReadOnlyList<WorldgenHandlerProfile> handlers, IReadOnlyList<WorldgenModProfile> mods)
    {
        RequestedColumns = requestedColumns;
        Columns = columns;
        WallMs = wallMs;
        Handlers = handlers;
        Mods = mods;
    }

    /// <summary>How many columns the measurement asked for.</summary>
    public int RequestedColumns { get; }

    /// <summary>How many columns were generated, neighbours included.</summary>
    public int Columns { get; }

    /// <summary>The wall time from the request to the game's loaded callback, in milliseconds.</summary>
    public double WallMs { get; }

    /// <summary>Every handler that ran, most expensive first.</summary>
    public IReadOnlyList<WorldgenHandlerProfile> Handlers { get; }

    /// <summary>Every mod whose pass handlers ran, most expensive first; the game is <c>game</c>.</summary>
    public IReadOnlyList<WorldgenModProfile> Mods { get; }

    /// <summary>One mod's aggregate, or a thrown <see cref="KeyNotFoundException"/> when it ran no pass handler.</summary>
    /// <param name="modId">The mod id, or <c>game</c> for the game's own generators.</param>
    public WorldgenModProfile Mod(string modId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modId);
        return Mods.FirstOrDefault(mod => string.Equals(mod.Owner, modId, StringComparison.Ordinal))
            ?? throw new KeyNotFoundException($"The mod '{modId}' ran no worldgen pass handler in this measurement.");
    }

    /// <summary>The per-pass handlers, most expensive first.</summary>
    public IReadOnlyList<WorldgenHandlerProfile> Passes =>
        [.. Handlers.Where(handler => handler.Stage is not ("region" or "mapchunk"))];

    /// <summary>The map region generators, most expensive first.</summary>
    public IReadOnlyList<WorldgenHandlerProfile> MapRegions =>
        [.. Handlers.Where(handler => handler.Stage == "region")];

    /// <summary>The map chunk generators, most expensive first.</summary>
    public IReadOnlyList<WorldgenHandlerProfile> MapChunks =>
        [.. Handlers.Where(handler => handler.Stage == "mapchunk")];

    /// <summary>The sum of every per-pass handler's time, in milliseconds.</summary>
    public double TotalPassMs => Passes.Sum(handler => handler.TotalMs);

    /// <summary>The mean per-pass generation cost of one generated column, in milliseconds.</summary>
    public double MeanMsPerColumn => Columns == 0 ? 0 : TotalPassMs / Columns;

    /// <summary>
    /// Every number as a named metric, for a scenario's <c>result.json</c> and for
    /// <c>pharos compare</c>. Handler and mod times are one sample per column, region or map chunk,
    /// with the stopwatch clock; counts are exact.
    /// </summary>
    public IReadOnlyList<MetricReading> Metrics()
    {
        List<MetricReading> readings =
        [
            new("worldgen.wall.ms", WallMs, MetricUnit.Milliseconds,
                MetricDirection.LowerIsBetter, MetricClock.Stopwatch),
            new("worldgen.requested.columns", RequestedColumns, MetricUnit.Count,
                MetricDirection.Neutral, MetricClock.Exact),
            new("worldgen.columns", Columns, MetricUnit.Count,
                MetricDirection.Neutral, MetricClock.Exact),
        ];

        foreach (WorldgenHandlerProfile handler in Handlers) handler.AddMetrics(readings);
        foreach (WorldgenModProfile mod in Mods) mod.AddMetrics(readings);
        return readings;
    }

    /// <summary>Publishes every metric into the scenario running on this flow; a no-op outside one.</summary>
    public void Publish() => Zaldaryon.Pharos.Metrics.Metrics.Publish(Metrics());

    /// <inheritdoc/>
    public override string ToString() => string.Create(CultureInfo.InvariantCulture,
        $"{MeanMsPerColumn:F3} ms per column over {Columns} columns, {WallMs:F0} ms wall");
}
