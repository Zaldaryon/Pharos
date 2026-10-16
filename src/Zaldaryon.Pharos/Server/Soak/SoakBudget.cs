namespace Zaldaryon.Pharos.Server;

/// <summary>
/// A limit a soak run checks, and the readings it checks it against.
/// </summary>
internal abstract record SoakBudget(string Name, bool Checkpoint)
{
    /// <summary>The breach this budget reports for <paramref name="sample"/>, or null when the sample keeps the budget.</summary>
    public abstract SoakBreach? Check(SoakSample sample, SoakBaseline baseline, bool enforceTiming);
}

/// <summary>The readings a run keeps from the end of its warm-up, for the budgets that compare against them.</summary>
internal sealed class SoakBaseline
{
    /// <summary>The post-warm-up managed heap after a collection, or null before the warm-up ends.</summary>
    public long? ManagedBytes { get; set; }

    /// <summary>The post-warm-up tick listener count, or null before the warm-up ends.</summary>
    public int? TickListeners { get; set; }
}

/// <summary>A count that must stay under a limit at every sample.</summary>
internal sealed record CeilingBudget(string Name, Func<SoakSample, long> Reading, long Limit)
    : SoakBudget(Name, Checkpoint: false)
{
    public override SoakBreach? Check(SoakSample sample, SoakBaseline baseline, bool enforceTiming)
    {
        long value = Reading(sample);
        return value < Limit ? null : new SoakBreach(Name, Limit, value, sample.Tick, sample.TotalDays, Gating: true);
    }
}

/// <summary>
/// Growth of the managed heap after a forced collection, from the warm-up baseline. It is checked
/// only at checkpoints, where the collection is done.
/// </summary>
internal sealed record GrowthBudget(long Limit) : SoakBudget("managed memory growth", Checkpoint: true)
{
    public override SoakBreach? Check(SoakSample sample, SoakBaseline baseline, bool enforceTiming)
    {
        if (sample.ManagedBytesAfterGc is not { } now || baseline.ManagedBytes is not { } before) return null;
        long growth = now - before;
        return growth < Limit ? null : new SoakBreach(Name, Limit, growth, sample.Tick, sample.TotalDays, Gating: true);
    }
}

/// <summary>
/// The tick listener count must stay within a tolerance of the warm-up baseline. Checked at
/// checkpoints, where the background work has finished.
/// </summary>
internal sealed record StabilityBudget(int Tolerance) : SoakBudget("tick listeners stable", Checkpoint: true)
{
    public override SoakBreach? Check(SoakSample sample, SoakBaseline baseline, bool enforceTiming)
    {
        if (baseline.TickListeners is not { } before) return null;
        long drift = Math.Abs((long)sample.TickListeners - before);
        return drift <= Tolerance ? null : new SoakBreach(Name, Tolerance, drift, sample.Tick, sample.TotalDays, Gating: true);
    }
}

/// <summary>
/// A tick time percentile under a limit, once the run has at least <see cref="MinimumTicks"/> ticks.
/// Advisory unless timing is enforced, because wall-clock time depends on the machine.
/// </summary>
internal sealed record TickTimeBudget(string Name, Func<SoakSample, TimeSpan?> Reading, double LimitMs)
    : SoakBudget(Name, Checkpoint: false)
{
    /// <summary>The ticks a percentile needs before it means anything.</summary>
    public const int MinimumTicks = 100;

    public override SoakBreach? Check(SoakSample sample, SoakBaseline baseline, bool enforceTiming)
    {
        if (sample.Tick < MinimumTicks || Reading(sample) is not { } time) return null;
        double value = time.TotalMilliseconds;
        return value < LimitMs ? null : new SoakBreach(Name, LimitMs, value, sample.Tick, sample.TotalDays, enforceTiming);
    }
}
