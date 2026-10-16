using System.Diagnostics;
using System.Runtime.CompilerServices;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.Server;
using Vintagestory.Common;
using Vintagestory.Server;
using Zaldaryon.Pharos.Performance;

namespace Zaldaryon.Pharos.Server;

/// <summary>
/// Plays an embedded server forward in game days, on the virtual clock, for soak tests. Reach it as
/// <see cref="EmbeddedServerHost.Simulate"/>.
/// </summary>
/// <remarks>
/// <para>
/// A day is the calendar's day. Ticks run until the calendar's total days reach the target, so a
/// change to the pace, such as sleeping, is honored. The calendar must follow the virtual clock; an
/// active clock that does not follow the calendar is refused, and when none is active the run opens one.
/// </para>
/// <para>
/// Random block ticks and the creature spawner run on their own threads on real time. Their finished
/// passes are counted and reported, not driven, so a run without a player shows none.
/// </para>
/// </remarks>
public sealed class ServerSimulation
{
    /// <summary>The fastest speed a run may ask for, the calendar's baseline time speed modifier.</summary>
    public const float MaxSpeed = 10_000;

    /// <summary>The tick, in milliseconds, of a clock the run opens when none is active.</summary>
    public const int DefaultTickMs = 50;

    /// <summary>The environment variable that makes the wall-clock tick budgets gate the run.</summary>
    public const string TimingVariable = "PHAROS_SOAK_TIMING";

    private readonly EmbeddedServerHost _host;

    internal ServerSimulation(EmbeddedServerHost host) => _host = host;

    /// <summary>
    /// Runs the server for <paramref name="days"/> game days at <paramref name="speed"/>, the calendar's
    /// baseline time speed modifier (60 is the game's default), and returns what it measured.
    /// </summary>
    /// <param name="days">The game days to run. Must be positive.</param>
    /// <param name="speed">The time speed, from 1 to <see cref="MaxSpeed"/>.</param>
    /// <param name="monitor">Fills in the budgets and sampling of the run.</param>
    /// <param name="ct">Cancels the run between samples.</param>
    /// <returns>The report. A gating budget that breaks throws instead, with the report attached.</returns>
    /// <exception cref="SoakBudgetExceededException">A gating budget broke.</exception>
    /// <exception cref="InvalidOperationException">The clock does not follow the calendar, the calendar is frozen, or the calendar stopped moving.</exception>
    public async Task<SoakReport> DaysAsync(double days, float speed = 60, Action<SoakMonitor>? monitor = null, CancellationToken ct = default)
    {
        if (!double.IsFinite(days) || days <= 0) throw new ArgumentOutOfRangeException(nameof(days), days, "The number of days must be positive.");
        if (!(speed >= 1 && speed <= MaxSpeed)) throw new ArgumentOutOfRangeException(nameof(speed), speed, $"The speed must be from 1 to {MaxSpeed}.");

        SoakMonitor budgets = new();
        monitor?.Invoke(budgets);
        budgets.Validate();

        VirtualClock? opened = _host.VirtualClock is null ? _host.UseVirtualClock(DefaultTickMs) : null;
        try
        {
            VirtualClock clock = opened ?? _host.VirtualClock!;
            if (!clock.IsActive) throw new InvalidOperationException("The virtual clock is not active.");
            if (!clock.FollowsCalendar) throw new InvalidOperationException("A soak run needs a virtual clock that follows the calendar, so the days pass on the calendar.");
            if (_host.Calendar.Pace().IsFrozen) throw new InvalidOperationException("The calendar is frozen, so the days would not pass. Unfreeze it first.");

            return await RunAsync(clock, days, speed, budgets, ct).ConfigureAwait(false);
        }
        finally
        {
            opened?.Dispose();
        }
    }

    private async Task<SoakReport> RunAsync(VirtualClock clock, double days, float speed, SoakMonitor budgets, CancellationToken ct)
    {
        SoakReport report = new(days, speed, clock.TickMs);
        List<long> timings = [];
        long randomTicksAtStart = BackgroundWork.Finished(BackgroundWork.Kind.RandomTicks);
        long spawnerAtStart = BackgroundWork.Finished(BackgroundWork.Kind.Spawner);
        float previousSpeed = _host.Calendar.ReplaceBaselineSpeed(speed);
        bool previousPassTime = SetPassTimeWhenEmpty(true);
        Stopwatch wall = Stopwatch.StartNew();
        bool completed = false;
        TimeSpan simulated = TimeSpan.Zero;
        long ticks = 0;
        double firstDays = 0;
        double lastDays = 0;

        _host.TickTimes = timings;
        try
        {
            (double rate, float hoursPerDay, double startDays, _) = _host.Calendar.Pace();
            double gameSecondsPerTick = rate * clock.TickMs / 1000.0;
            long sampleTicks = Math.Max(1, (long)Math.Round(budgets.SampleInterval.TotalSeconds / gameSecondsPerTick));
            long samplesPerCheckpoint = Math.Max(1, (long)Math.Round(budgets.CheckpointInterval.TotalSeconds / budgets.SampleInterval.TotalSeconds));
            double target = startDays + days;
            long maxTicks = (long)Math.Ceiling(days * hoursPerDay * 3600 / gameSecondsPerTick * 1.25) + 100;
            SoakEvaluator evaluator = new(budgets.Budgets, budgets.WarmUpDuration, Environment.GetEnvironmentVariable(TimingVariable) == "enforce");

            double today = startDays;
            firstDays = startDays;
            long sample = 0;
            while (today < target)
            {
                ct.ThrowIfCancellationRequested();

                // The last chunk stops on the target day, so a run ends where the day ends.
                long remainingTicks = (long)Math.Ceiling((target - today) * hoursPerDay * 3600 / gameSecondsPerTick);
                long chunk = Math.Min(sampleTicks, Math.Max(1, remainingTicks));
                if (ticks + chunk > maxTicks)
                {
                    throw new InvalidOperationException(
                        $"The run passed {maxTicks} ticks before day {target:0.###}; the calendar has stopped or slowed well below the speed {speed}.");
                }

                _host.RunOnGameThread(() =>
                {
                    for (long i = 0; i < chunk; i++) _host.Tick();
                });
                ticks += chunk;
                sample++;

                bool last = chunk >= remainingTicks;
                bool checkpoint = last || sample % samplesPerCheckpoint == 0;
                SoakSample raw = await TakeSampleAsync(ticks, wall.Elapsed, timings, checkpoint, ct).ConfigureAwait(false);
                simulated = TimeSpan.FromHours((raw.TotalDays - firstDays) * hoursPerDay);
                SoakSample reading = raw with { Simulated = simulated };
                report.AddSample(reading);

                if (reading.TotalDays <= today) throw new InvalidOperationException($"The calendar did not move in {chunk} ticks at day {today:0.###}.");
                today = reading.TotalDays;
                lastDays = today;

                foreach (SoakBreach breach in evaluator.Evaluate(reading))
                {
                    report.AddBreach(breach);
                    if (breach.Gating) throw new SoakBudgetExceededException(breach, report);
                }
            }

            completed = true;
        }
        finally
        {
            _host.TickTimes = null;
            if (_host.IsRunning)
            {
                _host.Calendar.ReplaceBaselineSpeed(previousSpeed);
                SetPassTimeWhenEmpty(previousPassTime);
            }

            report.Ticks = ticks;
            report.CalendarDays = lastDays - firstDays;
            report.Simulated = simulated;
            report.WallTime = wall.Elapsed;
            report.TickTime = TimingStats.FromTicks([.. timings], timings.Count);
            report.RandomTickPasses = BackgroundWork.Finished(BackgroundWork.Kind.RandomTicks) - randomTicksAtStart;
            report.SpawnerPasses = BackgroundWork.Finished(BackgroundWork.Kind.Spawner) - spawnerAtStart;
            report.Completed = completed;
        }

        return report;
    }

    /// <summary>
    /// Reads the run's state. At a checkpoint the background work is idle and the heap is collected
    /// first, so the counts and the heap are stable when they are read.
    /// </summary>
    private async Task<SoakSample> TakeSampleAsync(long tick, TimeSpan wall, List<long> timings, bool checkpoint, CancellationToken ct)
    {
        long? heapAfterGc = null;
        if (checkpoint)
        {
            await _host.WaitForBackgroundWorkAsync(ct: ct).ConfigureAwait(false);
            GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
            GC.WaitForPendingFinalizers();
            GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true);
            heapAfterGc = GC.GetTotalMemory(forceFullCollection: false);
        }

        (int entities, int chunks, double totalDays) = _host.RunOnGameThread(() =>
        {
            ServerMain server = _host.Server;
            int loadedChunks;
            ref FastRWLock chunksLock = ref ServerInternals.LoadedChunksLock(server);
            chunksLock.AcquireReadLock();
            try
            {
                loadedChunks = ServerInternals.LoadedChunks(server).Count;
            }
            finally
            {
                chunksLock.ReleaseReadLock();
            }

            GameCalendar calendar = (GameCalendar)server.Calendar;
            return (((IServerWorldAccessor)server).LoadedEntities.Count, loadedChunks, calendar.TotalDays);
        });

        int tickListeners = _host.TickListeners.All().Count;
        int callbacks = _host.TickListeners.Callbacks().Count;

        (TimeSpan? p50, TimeSpan? p99, TimeSpan? max) = TickTimes(timings);
        return new SoakSample(
            Tick: tick,
            TotalDays: totalDays,
            Simulated: TimeSpan.Zero,
            Wall: wall,
            TickP50: p50,
            TickP99: p99,
            TickMax: max,
            ManagedBytes: GC.GetTotalMemory(forceFullCollection: false),
            ManagedBytesAfterGc: heapAfterGc,
            Entities: entities,
            LoadedChunks: chunks,
            TickListeners: tickListeners,
            Callbacks: callbacks,
            RandomTickPasses: BackgroundWork.Finished(BackgroundWork.Kind.RandomTicks),
            Checkpoint: checkpoint);
    }

    private static (TimeSpan? P50, TimeSpan? P99, TimeSpan? Max) TickTimes(List<long> timings)
    {
        if (timings.Count == 0) return (null, null, null);
        TimingStats stats = TimingStats.FromTicks([.. timings], timings.Count);
        return (stats.Median, stats.P99, stats.Max);
    }

    private bool SetPassTimeWhenEmpty(bool value) => _host.RunOnGameThread(() =>
    {
        bool previous = _host.Server.Config.PassTimeWhenEmpty;
        _host.Server.Config.PassTimeWhenEmpty = value;
        return previous;
    });

    /// <summary>The loaded chunk table, which the game keeps internal.</summary>
    private static class ServerInternals
    {
        [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "loadedChunks")]
        public static extern ref Dictionary<long, ServerChunk> LoadedChunks(ServerMain server);

        [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "loadedChunksLock")]
        public static extern ref FastRWLock LoadedChunksLock(ServerMain server);
    }
}
