using System.Diagnostics;

namespace Zaldaryon.Pharos.Server;

/// <summary>
/// One server's time as its game code reads it: the server's own stopwatch plus an offset, or a
/// virtual time that only ticks move.
/// </summary>
/// <remarks>
/// The virtual time is written on the server's game thread and read from any thread: the chunk
/// thread and the network threads read the server's time too.
/// </remarks>
internal sealed class ServerClockState
{
    private readonly Func<long> _realMs;
    private long _offsetMs;
    private long _nowMs;
    private long _pendingStepMs;
    private long _calendarMarkMs;
    private volatile bool _virtual;
    private volatile Stopwatch? _calendarWatch;

    /// <summary>Follows <paramref name="watch"/>, the server's <c>totalUnpausedTime</c>.</summary>
    public ServerClockState(Stopwatch watch)
        : this(watch, () => watch.ElapsedMilliseconds)
    {
    }

    /// <summary>For tests: <paramref name="realMs"/> stands in for the stopwatch's reading.</summary>
    internal ServerClockState(Stopwatch? watch, Func<long> realMs)
    {
        Watch = watch;
        _realMs = realMs;
    }

    /// <summary>The server's stopwatch whose reads this state answers.</summary>
    public Stopwatch? Watch { get; }

    /// <summary>Whether ticks, not the wall clock, move the time.</summary>
    public bool IsVirtual => _virtual;

    /// <summary>How far each tick moves the virtual time.</summary>
    public int TickMs { get; private set; }

    /// <summary>Whether a step set by <see cref="SetPendingStep"/> waits for the next tick.</summary>
    public bool HasPendingStep => Volatile.Read(ref _pendingStepMs) > 0;

    /// <summary>The calendar stopwatch that follows the virtual time, or null.</summary>
    public Stopwatch? CalendarWatch => _calendarWatch;

    /// <summary>The server's time in milliseconds, as the game reads it.</summary>
    public long Read() => _virtual ? Volatile.Read(ref _nowMs) : _realMs() + Volatile.Read(ref _offsetMs);

    /// <summary>The server's time as a <see cref="TimeSpan"/>, as <c>Stopwatch.Elapsed</c> gives it.</summary>
    public TimeSpan ReadElapsed()
    {
        if (_virtual) return TimeSpan.FromMilliseconds(Volatile.Read(ref _nowMs));
        TimeSpan real = Watch?.Elapsed ?? TimeSpan.FromMilliseconds(_realMs());
        return real + TimeSpan.FromMilliseconds(Volatile.Read(ref _offsetMs));
    }

    /// <summary>Switches to virtual time, starting where the server's time is now.</summary>
    public void Enter(int tickMs)
    {
        Volatile.Write(ref _nowMs, Read());
        Volatile.Write(ref _pendingStepMs, 0);
        TickMs = tickMs;
        _virtual = true;
    }

    /// <summary>Goes back to the stopwatch, carrying on from the virtual time without a jump.</summary>
    public void Exit()
    {
        if (!_virtual) return;
        long now = Volatile.Read(ref _nowMs);
        Volatile.Write(ref _offsetMs, now - _realMs());
        Volatile.Write(ref _pendingStepMs, 0);
        _calendarWatch = null;
        _virtual = false;
    }

    /// <summary>Makes the next tick move the time by <paramref name="stepMs"/> instead of <see cref="TickMs"/>.</summary>
    public void SetPendingStep(long stepMs) => Volatile.Write(ref _pendingStepMs, stepMs);

    /// <summary>
    /// Called where a server tick reads its time: moves the virtual time by one step and returns
    /// it. On the stopwatch, only reads.
    /// </summary>
    public long BeginTick()
    {
        if (!_virtual) return Read();
        long step = Interlocked.Exchange(ref _pendingStepMs, 0);
        if (step <= 0) step = TickMs;
        long now = Volatile.Read(ref _nowMs) + step;
        Volatile.Write(ref _nowMs, now);

        // While the calendar's watch stands still, no time passes for it.
        if (_calendarWatch is { IsRunning: false }) Volatile.Write(ref _calendarMarkMs, now);
        return now;
    }

    /// <summary>Makes the calendar whose watch is <paramref name="watch"/> follow the virtual time from now.</summary>
    public void FollowCalendar(Stopwatch watch)
    {
        Volatile.Write(ref _calendarMarkMs, Read());
        _calendarWatch = watch;
    }

    /// <summary>Stops the calendar following the virtual time.</summary>
    public void StopFollowingCalendar() => _calendarWatch = null;

    /// <summary>
    /// The milliseconds the followed calendar has to bank, from the virtual time passed since it
    /// last did, and marks them banked; null when <paramref name="watch"/> is not followed.
    /// </summary>
    public long? TakeCalendarElapsed(Stopwatch watch)
    {
        if (!_virtual || !ReferenceEquals(watch, _calendarWatch)) return null;
        long now = Volatile.Read(ref _nowMs);
        long elapsed = now - Volatile.Read(ref _calendarMarkMs);
        Volatile.Write(ref _calendarMarkMs, now);
        return Math.Max(0, elapsed);
    }

    /// <summary>The milliseconds the followed calendar has yet to bank, or null when <paramref name="watch"/> is not followed.</summary>
    public long? PendingCalendarElapsed(Stopwatch watch)
    {
        if (!_virtual || !ReferenceEquals(watch, _calendarWatch)) return null;
        return Math.Max(0, Volatile.Read(ref _nowMs) - Volatile.Read(ref _calendarMarkMs));
    }
}
