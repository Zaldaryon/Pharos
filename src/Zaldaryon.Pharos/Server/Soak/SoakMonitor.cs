namespace Zaldaryon.Pharos.Server;

/// <summary>
/// The budgets and sampling a soak run follows. The run passes the monitor to the callback of
/// <see cref="ServerSimulation.DaysAsync"/>, which fills it in.
/// </summary>
/// <remarks>
/// Every <c>Below</c> budget names a limit the reading must stay under. Sampling and checkpoint
/// intervals are game time, and a checkpoint must fall on a sample.
/// </remarks>
public sealed class SoakMonitor
{
    private readonly List<SoakBudget> _budgets = [];

    internal SoakMonitor() => TickMs = new SoakTickTime(this);

    /// <summary>The tick time budgets. Advisory unless <c>PHAROS_SOAK_TIMING=enforce</c> is set.</summary>
    public SoakTickTime TickMs { get; }

    /// <summary>How often the run samples, in game time. The default is one game hour.</summary>
    public TimeSpan SampleInterval { get; private set; } = TimeSpan.FromHours(1);

    /// <summary>How often the run checkpoints, in game time, where it collects the heap and compares stability. The default is one game day.</summary>
    public TimeSpan CheckpointInterval { get; private set; } = TimeSpan.FromDays(1);

    /// <summary>The game time before the baseline is taken, for the budgets that compare against it. The default is one game hour.</summary>
    public TimeSpan WarmUpDuration { get; private set; } = TimeSpan.FromHours(1);

    /// <summary>The managed heap after a checkpoint may grow by less than <paramref name="bytes"/> from the warm-up baseline.</summary>
    public SoakMonitor ManagedMemoryGrowthBelow(long bytes) =>
        Add(new GrowthBudget(Positive(bytes, nameof(bytes))));

    /// <summary>The loaded entities stay under <paramref name="count"/> at every sample.</summary>
    public SoakMonitor EntitiesBelow(int count) =>
        Add(new CeilingBudget("entities", sample => sample.Entities, Positive(count, nameof(count))));

    /// <summary>The loaded chunks stay under <paramref name="count"/> at every sample.</summary>
    public SoakMonitor LoadedChunksBelow(int count) =>
        Add(new CeilingBudget("loaded chunks", sample => sample.LoadedChunks, Positive(count, nameof(count))));

    /// <summary>The tick listeners stay under <paramref name="count"/> at every sample.</summary>
    public SoakMonitor TickListenersBelow(int count) =>
        Add(new CeilingBudget("tick listeners", sample => sample.TickListeners, Positive(count, nameof(count))));

    /// <summary>At each checkpoint, the tick listener count stays within <paramref name="tolerance"/> of the warm-up baseline.</summary>
    public SoakMonitor TickListenersStable(int tolerance = 0)
    {
        if (tolerance < 0) throw new ArgumentOutOfRangeException(nameof(tolerance), tolerance, "The tolerance cannot be negative.");
        return Add(new StabilityBudget(tolerance));
    }

    /// <summary>Samples the run every <paramref name="gameTime"/>.</summary>
    public SoakMonitor SampleEvery(TimeSpan gameTime)
    {
        SampleInterval = Positive(gameTime, nameof(gameTime));
        return this;
    }

    /// <summary>Checkpoints the run every <paramref name="gameTime"/>, which must be a whole number of sample intervals.</summary>
    public SoakMonitor CheckpointEvery(TimeSpan gameTime)
    {
        CheckpointInterval = Positive(gameTime, nameof(gameTime));
        return this;
    }

    /// <summary>Takes the baseline at the first checkpoint after <paramref name="gameTime"/>.</summary>
    public SoakMonitor WarmUp(TimeSpan gameTime)
    {
        if (gameTime < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(gameTime), gameTime, "The warm-up cannot be negative.");
        WarmUpDuration = gameTime;
        return this;
    }

    internal IReadOnlyList<SoakBudget> Budgets => _budgets;

    internal SoakMonitor Add(SoakBudget budget)
    {
        _budgets.Add(budget);
        return this;
    }

    /// <summary>Checks the sampling settings, which the run needs before it starts.</summary>
    internal void Validate()
    {
        if (CheckpointInterval.Ticks % SampleInterval.Ticks != 0)
        {
            throw new ArgumentException(
                $"The checkpoint interval ({CheckpointInterval}) must be a whole number of sample intervals ({SampleInterval}).",
                nameof(CheckpointInterval));
        }
    }

    private static TimeSpan Positive(TimeSpan time, string name) =>
        time > TimeSpan.Zero ? time : throw new ArgumentOutOfRangeException(name, time, "The interval must be positive.");

    private static long Positive(long value, string name) =>
        value > 0 ? value : throw new ArgumentOutOfRangeException(name, value, "The limit must be positive.");

    private static int Positive(int value, string name) =>
        value > 0 ? value : throw new ArgumentOutOfRangeException(name, value, "The limit must be positive.");
}

/// <summary>The tick time budgets of a <see cref="SoakMonitor"/>.</summary>
public sealed class SoakTickTime
{
    private readonly SoakMonitor _monitor;

    internal SoakTickTime(SoakMonitor monitor) => _monitor = monitor;

    /// <summary>The median tick time stays under <paramref name="milliseconds"/>, once the run has 100 ticks.</summary>
    public SoakMonitor P50Below(double milliseconds) =>
        _monitor.Add(new TickTimeBudget("tick p50", sample => sample.TickP50, Limit(milliseconds)));

    /// <summary>The 99th percentile tick time stays under <paramref name="milliseconds"/>, once the run has 100 ticks.</summary>
    public SoakMonitor P99Below(double milliseconds) =>
        _monitor.Add(new TickTimeBudget("tick p99", sample => sample.TickP99, Limit(milliseconds)));

    private static double Limit(double milliseconds) =>
        milliseconds > 0 ? milliseconds : throw new ArgumentOutOfRangeException(nameof(milliseconds), milliseconds, "The limit must be positive.");
}
