using System.Globalization;
using Zaldaryon.Pharos.Metrics;
using Zaldaryon.Pharos.Performance;

namespace Zaldaryon.Pharos.Server;

/// <summary>
/// What one profiled thing did in a measured run: how often it was called, what it cost, and how
/// that cost spread over the ticks.
/// </summary>
/// <remarks>
/// Times are the CPU time of the calls themselves, measured with <c>Stopwatch.GetTimestamp</c> on
/// the server thread, not the wall time between ticks. They are noisy and meant for comparison
/// between two runs; <see cref="Calls"/> and the byte and packet counts are exact.
/// </remarks>
public sealed class ServerProfileEntry
{
    internal ServerProfileEntry(string name, long calls, IReadOnlyList<double> perTickMs, double p95MsPerTick, int instances)
    {
        Name = name;
        Calls = calls;
        PerTickMs = perTickMs;
        TotalMs = perTickMs.Sum();
        P95MsPerTick = p95MsPerTick;
        Instances = instances;
    }

    /// <summary>The system type name, the mod id, the block entity or entity code, or the handler.</summary>
    public string Name { get; }

    /// <summary>How many calls were measured.</summary>
    public long Calls { get; }

    /// <summary>The total time of every call, in milliseconds.</summary>
    public double TotalMs { get; }

    /// <summary>How many ticks the run measured, including ticks in which this entry did not run.</summary>
    public int Ticks => PerTickMs.Count;

    /// <summary>What this entry cost in each measured tick, in milliseconds.</summary>
    public IReadOnlyList<double> PerTickMs { get; }

    /// <summary>
    /// The 95th percentile of the time this entry cost per tick, in milliseconds, counting a tick
    /// in which it did not run as zero.
    /// </summary>
    public double P95MsPerTick { get; }

    /// <summary>The average time this entry cost per tick, in milliseconds.</summary>
    public double MeanMsPerTick => Ticks == 0 ? 0 : TotalMs / Ticks;

    /// <summary>
    /// How many instances were seen: block positions, entity ids, or listeners, depending on what
    /// this entry describes. Zero when the profile does not track instances for this kind.
    /// </summary>
    public int Instances { get; }

    /// <inheritdoc/>
    public override string ToString() => string.Create(CultureInfo.InvariantCulture,
        $"{Name}: {Calls} calls, {TotalMs:0.###} ms total, {MeanMsPerTick:0.####} ms/tick, p95 {P95MsPerTick:0.###} ms/tick");
}

/// <summary>What one AI task cost while the measured entities ran it.</summary>
/// <remarks>
/// The code comes from the game's task registry, so it is stable across runs: <c>wander</c>,
/// <c>seekentity</c>, <c>idle</c> and so on. A task a mod registered under its own code keeps that
/// code; one it did not register is named by its type.
/// </remarks>
public sealed class AiTaskProfile
{
    internal AiTaskProfile(string code, long calls, IReadOnlyList<double> perTickMs, double p95MsPerTick)
    {
        Code = code;
        Calls = calls;
        PerTickMs = perTickMs;
        TotalMs = perTickMs.Sum();
        P95MsPerTick = p95MsPerTick;
    }

    /// <summary>The task's code in the game's AI task registry, or its type name.</summary>
    public string Code { get; }

    /// <summary>How often the task was asked whether to run, or ran.</summary>
    public long Calls { get; }

    /// <summary>The total time of those calls, in milliseconds.</summary>
    public double TotalMs { get; }

    /// <summary>How many ticks the run measured.</summary>
    public int Ticks => PerTickMs.Count;

    /// <summary>What the task cost in each measured tick, in milliseconds.</summary>
    public IReadOnlyList<double> PerTickMs { get; }

    /// <summary>The 95th percentile of the task's cost per tick, in milliseconds.</summary>
    public double P95MsPerTick { get; }

    /// <summary>The average cost per tick, in milliseconds.</summary>
    public double MeanMsPerTick => Ticks == 0 ? 0 : TotalMs / Ticks;

    /// <inheritdoc/>
    public override string ToString() => string.Create(CultureInfo.InvariantCulture,
        $"{Code}: {Calls} calls, {TotalMs:0.###} ms total, p95 {P95MsPerTick:0.###} ms/tick");
}

/// <summary>What the measured run cost per server system, mod, block entity type, entity type and AI task.</summary>
/// <remarks>
/// <para>
/// A value is attributed to the narrowest thing that owns the work, and the levels nest: a block
/// entity's tick listener time is in <see cref="BlockEntities"/> and in the mod that registered
/// it, while an AI task runs inside its entity's behaviors, so its time is also in
/// <see cref="Entities"/> and <see cref="EntityBehaviors"/>. Do not add the levels together.
/// </para>
/// <para>
/// Get one from <see cref="ServerProfileDriver.MeasureAsync"/>; publish it into the running
/// scenario with <see cref="Publish"/> so <c>pharos compare</c> can see the numbers.
/// </para>
/// </remarks>
public sealed class ServerProfile
{
    internal ServerProfile(int ticks, TimingStats tickTimes,
        IReadOnlyDictionary<string, ServerProfileEntry> systems,
        IReadOnlyDictionary<string, ServerProfileEntry> mods,
        IReadOnlyDictionary<string, ServerProfileEntry> blockEntities,
        IReadOnlyDictionary<string, ServerProfileEntry> entities,
        IReadOnlyDictionary<string, ServerProfileEntry> entityBehaviors,
        IReadOnlyDictionary<string, ServerProfileEntry> listeners,
        IReadOnlyList<AiTaskProfile> aiTasks,
        BandwidthProfile bandwidth)
    {
        Ticks = ticks;
        TickTimes = tickTimes;
        Systems = systems;
        Mods = mods;
        BlockEntities = blockEntities;
        Entities = entities;
        EntityBehaviors = entityBehaviors;
        Listeners = listeners;
        AiTasks = aiTasks;
        Bandwidth = bandwidth;
    }

    /// <summary>How many ticks the run measured.</summary>
    public int Ticks { get; }

    /// <summary>The whole tick's duration, one sample per measured tick.</summary>
    public TimingStats TickTimes { get; }

    /// <summary>Time in each server system's <c>OnServerTick</c>, by system type name.</summary>
    public IReadOnlyDictionary<string, ServerProfileEntry> Systems { get; }

    /// <summary>Time in the tick listeners and callbacks of each mod, by mod id.</summary>
    public IReadOnlyDictionary<string, ServerProfileEntry> Mods { get; }

    /// <summary>Time in the tick listeners tied to a block entity, by block entity type.</summary>
    public IReadOnlyDictionary<string, ServerProfileEntry> BlockEntities { get; }

    /// <summary>Time in each entity type's <c>OnGameTick</c>, by entity code.</summary>
    public IReadOnlyDictionary<string, ServerProfileEntry> Entities { get; }

    /// <summary>
    /// Time in each entity behavior, by <c>entity code/behavior type</c>, as the part of
    /// <see cref="Entities"/> the behaviors account for.
    /// </summary>
    public IReadOnlyDictionary<string, ServerProfileEntry> EntityBehaviors { get; }

    /// <summary>Time in each tick listener and callback, by the type and method it calls.</summary>
    public IReadOnlyDictionary<string, ServerProfileEntry> Listeners { get; }

    /// <summary>Time in each AI task, most expensive first.</summary>
    public IReadOnlyList<AiTaskProfile> AiTasks { get; }

    /// <summary>Packets and bytes per player, packet id and mod channel.</summary>
    public BandwidthProfile Bandwidth { get; }

    /// <summary>The mean tick duration, in milliseconds.</summary>
    public double MeanMsPerTick => TickTimes.Count == 0 ? 0 : TickTimes.Mean.TotalMilliseconds;

    /// <summary>The 95th percentile of the tick duration, in milliseconds.</summary>
    public double P95MsPerTick => TickTimes.P95.TotalMilliseconds;

    /// <summary>
    /// Every number as a named metric, for a scenario's <c>result.json</c> and for
    /// <c>pharos compare</c>.
    /// </summary>
    /// <remarks>
    /// Times are recorded per tick, so a comparison has a distribution to test rather than one
    /// total. Counts and bytes are recorded once each, and their clock is exact. The names are:
    /// <c>server.tick.system.&lt;name&gt;.ms</c>, <c>server.tick.mod.&lt;id&gt;.ms</c>,
    /// <c>server.tick.be.&lt;type&gt;.ms</c>, <c>server.tick.entity.&lt;code&gt;.ms</c>,
    /// <c>server.tick.behavior.&lt;code&gt;.&lt;type&gt;.ms</c>, <c>server.tick.listener.&lt;handler&gt;.ms</c>,
    /// <c>server.ai.&lt;task&gt;.ms</c>, <c>net.sent.&lt;player&gt;.bytes</c> and its
    /// <c>received</c>, <c>packets</c> and per-channel forms.
    /// </remarks>
    public IReadOnlyList<MetricReading> Metrics()
    {
        List<MetricReading> readings = [];
        AddTickSeries(readings, "server.tick.ms", [.. TickTimes.Samples.Select(s => s.TotalMilliseconds)]);
        foreach ((string name, ServerProfileEntry entry) in Systems) AddEntry(readings, $"server.tick.system.{name}", entry);
        foreach ((string name, ServerProfileEntry entry) in Mods) AddEntry(readings, $"server.tick.mod.{name}", entry);
        foreach ((string name, ServerProfileEntry entry) in BlockEntities) AddEntry(readings, $"server.tick.be.{name}", entry);
        foreach ((string name, ServerProfileEntry entry) in Entities) AddEntry(readings, $"server.tick.entity.{name}", entry);
        foreach ((string name, ServerProfileEntry entry) in EntityBehaviors)
        {
            AddEntry(readings, $"server.tick.behavior.{name.Replace('/', '.')}", entry);
        }

        foreach ((string name, ServerProfileEntry entry) in Listeners) AddEntry(readings, $"server.tick.listener.{name}", entry);
        foreach (AiTaskProfile task in AiTasks) AddEntry(readings, $"server.ai.{task.Code}", task);
        Bandwidth.AddMetrics(readings);
        return readings;
    }

    /// <summary>Publishes every metric into the scenario running on this flow; a no-op outside one.</summary>
    public void Publish() => Zaldaryon.Pharos.Metrics.Metrics.Publish(Metrics());

    private static void AddEntry(List<MetricReading> readings, string name, ServerProfileEntry entry)
    {
        AddTickSeries(readings, $"{name}.ms", entry.PerTickMs);
        readings.Add(new MetricReading($"{name}.calls", entry.Calls, MetricUnit.Count,
            MetricDirection.Neutral, MetricClock.Exact));
    }

    private static void AddEntry(List<MetricReading> readings, string name, AiTaskProfile task)
    {
        AddTickSeries(readings, $"{name}.ms", task.PerTickMs);
        readings.Add(new MetricReading($"{name}.calls", task.Calls, MetricUnit.Count,
            MetricDirection.Neutral, MetricClock.Exact));
    }

    private static void AddTickSeries(List<MetricReading> readings, string name, IEnumerable<double> samples)
    {
        foreach (double sample in samples)
        {
            readings.Add(new MetricReading(name, sample, MetricUnit.Milliseconds,
                MetricDirection.LowerIsBetter, MetricClock.Tick));
        }
    }
}
