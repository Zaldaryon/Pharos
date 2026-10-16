using Xunit;
using Zaldaryon.Pharos.Server;

namespace Zaldaryon.Pharos.Tests.Server;

/// <summary>The soak budgets, baseline, and CSV, checked without a game.</summary>
public class SoakMonitorUnitTests
{
    [Fact]
    public void EntitiesBelow_BreachesAtTheLimit()
    {
        SoakMonitor monitor = new SoakMonitor().EntitiesBelow(3);
        SoakEvaluator evaluator = new(monitor.Budgets, TimeSpan.Zero, enforceTiming: false);

        Assert.Empty(evaluator.Evaluate(Sample(tick: 1, entities: 2)));

        SoakBreach breach = Assert.Single(evaluator.Evaluate(Sample(tick: 2, entities: 3)));
        Assert.Equal("entities", breach.Budget);
        Assert.Equal(3, breach.Limit);
        Assert.Equal(3, breach.Value);
        Assert.True(breach.Gating);
    }

    [Fact]
    public void GrowthAndStability_CompareWithTheWarmUpBaseline_AndOnlyAtCheckpoints()
    {
        SoakMonitor monitor = new SoakMonitor().ManagedMemoryGrowthBelow(1_000).TickListenersStable();
        SoakEvaluator evaluator = new(monitor.Budgets, TimeSpan.FromHours(1), enforceTiming: false);

        // A checkpoint before the warm-up ends takes no baseline and reports nothing.
        Assert.Empty(evaluator.Evaluate(Sample(tick: 10, simulated: TimeSpan.FromMinutes(30), heapAfterGc: 5_000, listeners: 7, checkpoint: true)));

        // The first checkpoint after the warm-up takes the baseline: 5000 bytes and 7 listeners.
        Assert.Empty(evaluator.Evaluate(Sample(tick: 11, simulated: TimeSpan.FromHours(1), heapAfterGc: 5_000, listeners: 7, checkpoint: true)));

        // A plain sample skips both checkpoint budgets, even with a large listener count.
        Assert.Empty(evaluator.Evaluate(Sample(tick: 12, simulated: TimeSpan.FromHours(1.5), heapAfterGc: null, listeners: 9)));

        // At the next checkpoint the heap has grown by 1000 bytes, and the listener count has drifted by 2.
        List<SoakBreach> breaches = evaluator.Evaluate(Sample(tick: 13, simulated: TimeSpan.FromHours(2), heapAfterGc: 6_000, listeners: 9, checkpoint: true));
        Assert.Equal(["managed memory growth", "tick listeners stable"], breaches.Select(b => b.Budget));
        Assert.Equal(1_000, breaches[0].Value);
        Assert.Equal(2, breaches[1].Value);
    }

    [Fact]
    public void TickListenersStable_AllowsTheTolerance()
    {
        SoakMonitor monitor = new SoakMonitor().TickListenersStable(tolerance: 1);
        SoakEvaluator evaluator = new(monitor.Budgets, TimeSpan.Zero, enforceTiming: false);
        evaluator.Evaluate(Sample(tick: 1, heapAfterGc: 0, listeners: 5, checkpoint: true));

        Assert.Empty(evaluator.Evaluate(Sample(tick: 2, heapAfterGc: 0, listeners: 6, checkpoint: true)));
        Assert.Single(evaluator.Evaluate(Sample(tick: 3, heapAfterGc: 0, listeners: 7, checkpoint: true)));
    }

    [Fact]
    public void TickTimeBudgets_NeedAHundredTicks_AndAreAdvisoryUnlessEnforced()
    {
        SoakMonitor monitor = new SoakMonitor().TickMs.P99Below(10);
        TimeSpan slow = TimeSpan.FromMilliseconds(50);

        SoakEvaluator advisory = new(monitor.Budgets, TimeSpan.Zero, enforceTiming: false);
        Assert.Empty(advisory.Evaluate(Sample(tick: 99, tickP99: slow)));
        SoakBreach recorded = Assert.Single(advisory.Evaluate(Sample(tick: 100, tickP99: slow)));
        Assert.False(recorded.Gating);

        SoakEvaluator enforced = new(monitor.Budgets, TimeSpan.Zero, enforceTiming: true);
        Assert.True(Assert.Single(enforced.Evaluate(Sample(tick: 100, tickP99: slow))).Gating);
    }

    [Fact]
    public void Budgets_RejectNonPositiveLimits()
    {
        SoakMonitor monitor = new();

        Assert.Throws<ArgumentOutOfRangeException>(() => monitor.EntitiesBelow(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => monitor.ManagedMemoryGrowthBelow(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => monitor.TickMs.P50Below(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => monitor.TickListenersStable(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => monitor.SampleEvery(TimeSpan.Zero));
    }

    [Fact]
    public void Validate_RejectsACheckpointThatIsNotASampleMultiple()
    {
        SoakMonitor monitor = new SoakMonitor().SampleEvery(TimeSpan.FromHours(1)).CheckpointEvery(TimeSpan.FromMinutes(90));

        Assert.Throws<ArgumentException>(() => monitor.Validate());
    }

    [Fact]
    public void Csv_HasAFixedHeader_AndEmptyCellsForMissingValues()
    {
        SoakReport report = new(days: 1, speed: 60, tickMs: 50);
        report.AddSample(new SoakSample(
            Tick: 7, TotalDays: 0.5, Simulated: TimeSpan.FromMinutes(90), Wall: TimeSpan.FromSeconds(2),
            TickP50: TimeSpan.FromMilliseconds(1.5), TickP99: null, TickMax: null,
            ManagedBytes: 1000, ManagedBytesAfterGc: null, Entities: 4, LoadedChunks: 2, TickListeners: 3, Callbacks: 1,
            RandomTickPasses: 0, Checkpoint: false));

        string[] lines = report.ToCsv().Split('\n');

        Assert.Equal("tick,total_days,simulated_seconds,wall_seconds,tick_p50_ms,tick_p99_ms,tick_max_ms,managed_bytes,managed_bytes_after_gc,entities,loaded_chunks,tick_listeners,callbacks,random_tick_passes,checkpoint", lines[0]);
        Assert.Equal("7,0.5,5400,2,1.5,,,1000,,4,2,3,1,0,false", lines[1]);
    }

    [Fact]
    public void BreachException_NamesTheBudgetAndTheValue()
    {
        SoakBreach breach = new("entities", 3, 5, Tick: 100, TotalDays: 1.25, Gating: true);
        SoakBudgetExceededException error = new(breach, new SoakReport(days: 1, speed: 60, tickMs: 50));

        Assert.Contains("'entities'", error.Message);
        Assert.Contains("value 5", error.Message);
        Assert.Same(breach, error.Breach);
    }

    private static SoakSample Sample(
        long tick = 1, double days = 0.1, TimeSpan? simulated = null, int entities = 0, long? heapAfterGc = null,
        int listeners = 0, TimeSpan? tickP99 = null, bool checkpoint = false) =>
        new(tick, days, simulated ?? TimeSpan.FromHours(tick), TimeSpan.Zero, TickP50: null, TickP99: tickP99, TickMax: null,
            ManagedBytes: 0, ManagedBytesAfterGc: heapAfterGc, Entities: entities, LoadedChunks: 0, TickListeners: listeners,
            Callbacks: 0, RandomTickPasses: 0, Checkpoint: checkpoint);
}
