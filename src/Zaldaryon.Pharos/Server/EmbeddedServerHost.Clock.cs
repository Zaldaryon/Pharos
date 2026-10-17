using Vintagestory.Common;
using Zaldaryon.Pharos.XUnit.Execution;

namespace Zaldaryon.Pharos.Server;

public sealed partial class EmbeddedServerHost
{
    private readonly object _clockLock = new();
    private ServerClockState? _clockState;
    private VirtualClock? _virtualClock;
    private bool _clockStarting;

    // The clock in use, or the last one the test used, for run.json.
    private VirtualClock? _lastClock;

    /// <summary>The virtual clock driving the server's time, or null when it follows the wall clock.</summary>
    public VirtualClock? VirtualClock
    {
        get
        {
            lock (_clockLock) return _virtualClock;
        }
    }

    /// <summary>
    /// Makes ticks, not the wall clock, drive the server's time until the returned clock is
    /// disposed: each <see cref="Tick"/> then moves it by exactly <paramref name="tickMs"/>. See
    /// <see cref="Server.VirtualClock"/> and <c>docs/writing-server-scenarios.md</c>.
    /// </summary>
    /// <remarks>
    /// The simulated time starts where the server's time is: the first <c>dt</c> each system and
    /// listener gets afterwards still includes the real time since it last ran. Every one after
    /// that is exact.
    /// </remarks>
    /// <param name="tickMs">How far each tick moves the server's time, from 1 to 1000 milliseconds.</param>
    /// <param name="followCalendar">
    /// Whether the calendar follows the simulated time too. Clients keep projecting their calendar
    /// on real time between the calendar packets the server sends.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="tickMs"/> is outside 1 to 1000.</exception>
    /// <exception cref="InvalidOperationException">The server is not running, or a virtual clock is already in use.</exception>
    /// <exception cref="NotSupportedException">This game version reads the server's time where Pharos cannot reach it.</exception>
    public VirtualClock UseVirtualClock(int tickMs = 50, bool followCalendar = true)
    {
        if (tickMs < 1 || tickMs > 1000) throw new ArgumentOutOfRangeException(nameof(tickMs), tickMs, "The tick must be from 1 to 1000 milliseconds.");
        if (!IsRunning) throw new InvalidOperationException("The server is not running.");
        ServerClockPatches.EnsureSupported();

        lock (_clockLock)
        {
            if (_virtualClock != null || _clockStarting) throw new InvalidOperationException("A virtual clock already drives this server; dispose it first.");
            _clockStarting = true;
        }

        // The game thread is not waited on under the lock: code it runs may read VirtualClock.
        try
        {
            ServerClockState state = ClockState;
            VirtualClock clock = RunOnGameThread(() =>
            {
                GameCalendar? calendar = Server.Calendar as GameCalendar;

                // Bank the calendar's real time first, then follow from now.
                if (followCalendar) calendar?.Tick();
                state.Enter(tickMs);
                if (followCalendar && calendar != null) state.FollowCalendar(CalendarDriver.Internals.Watch(calendar));
                return new VirtualClock(this, state, tickMs, followCalendar && calendar != null, state.Read());
            });

            lock (_clockLock)
            {
                _virtualClock = clock;
                _lastClock = clock;
            }

            return clock;
        }
        finally
        {
            lock (_clockLock) _clockStarting = false;
        }
    }

    /// <summary>
    /// Waits until the server's background threads have no work queued: lighting, chunk loading
    /// and generation, random block ticks, the creature spawner and async pathfinding. It does not
    /// tick the server and does not move a virtual clock. What those threads hand to the game
    /// thread (random ticks to run, creatures to spawn, chunk columns loaded) is done on the
    /// following ticks.
    /// </summary>
    /// <param name="timeout">How long to wait; 30 seconds by default, scaled by <c>PHAROS_TIMEOUT_SCALE</c>.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <exception cref="InvalidOperationException">Called on the server's game thread, or the server is not running.</exception>
    /// <exception cref="TimeoutException">A background system still had work when the time was up; the message names it.</exception>
    public Task WaitForBackgroundWorkAsync(TimeSpan? timeout = null, CancellationToken ct = default)
    {
        if (_gameThread.IsCurrent) throw new InvalidOperationException("WaitForBackgroundWorkAsync waits for other threads and cannot run on the server's game thread.");
        if (!IsRunning) throw new InvalidOperationException("The server is not running.");
        return BackgroundWork.WaitAsync(Server, timeout ?? TimeSpan.FromSeconds(30 * ScenarioTimeouts.Scale), ct);
    }

    /// <summary>The server's time now, in milliseconds: simulated under a virtual clock, the wall clock's otherwise.</summary>
    internal long ServerNowMs => ClockState.Read();

    /// <summary>The server's time state, made the one Pharos answers reads with.</summary>
    private ServerClockState ClockState
    {
        get
        {
            _clockState ??= new ServerClockState(Server.totalUnpausedTime);
            ServerClockPatches.Current = _clockState;
            return _clockState;
        }
    }

    /// <summary>
    /// Ends <paramref name="clock"/>, or whichever clock is in use when null, on the game thread.
    /// </summary>
    internal void EndVirtualClock(VirtualClock? clock = null)
    {
        VirtualClock? active;
        lock (_clockLock)
        {
            active = _virtualClock;
            if (active == null || (clock != null && !ReferenceEquals(clock, active))) return;
            _virtualClock = null;
        }

        ServerClockState state = _clockState!;
        void End()
        {
            long end = state.Read();
            try
            {
                // Bank the simulated time the calendar has not taken yet, then hand it back.
                if (!_disposed && state.CalendarWatch != null && Server.Calendar is GameCalendar calendar) calendar.Tick();
                end = state.Read();
            }
            finally
            {
                state.StopFollowingCalendar();
                state.Exit();
                active.MarkEnded(end);
            }
        }

        if (_disposed || _gameThread.IsCurrent)
        {
            End();
            return;
        }

        try
        {
            _gameThread.Invoke(End);
        }
        catch (ObjectDisposedException)
        {
            End();
        }
    }

    /// <summary>
    /// Ends any clock a test left in use and forgets the last one, so the next test starts on the
    /// wall clock; called when a scenario test ends, after its failure is captured.
    /// </summary>
    internal void ResetVirtualClock()
    {
        _faults?.Clear();
        EndVirtualClock();
        lock (_clockLock) _lastClock = null;
        LastSoakReport = null;
        LastStateHashes = null;
    }

    /// <summary>The clock in use, or the last one used since the test began, for run.json.</summary>
    internal ServerClockInfo ClockInfo
    {
        get
        {
            lock (_clockLock)
            {
                return _lastClock is { } clock
                    ? new ServerClockInfo("virtual", clock.TickMs, clock.FollowsCalendar, clock.IsActive, (long)clock.Elapsed.TotalMilliseconds)
                    : new ServerClockInfo("real", null, null, false, null);
            }
        }
    }
}

/// <summary>How a server's time ran during a test, for run.json.</summary>
/// <param name="Mode"><c>real</c> or <c>virtual</c>.</param>
/// <param name="TickMs">The virtual clock's tick, in milliseconds.</param>
/// <param name="FollowsCalendar">Whether the calendar followed the virtual clock.</param>
/// <param name="Active">Whether the clock was still in use when the failure was captured.</param>
/// <param name="SimulatedMs">The simulated time the clock ran, in milliseconds.</param>
internal sealed record ServerClockInfo(string Mode, int? TickMs, bool? FollowsCalendar, bool Active, long? SimulatedMs);
