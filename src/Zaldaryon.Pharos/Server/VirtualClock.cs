namespace Zaldaryon.Pharos.Server;

/// <summary>
/// Drives an embedded server's time by ticks instead of the wall clock, until it is disposed.
/// Each <see cref="EmbeddedServerHost.Tick"/> moves the server's time by exactly
/// <see cref="TickMs"/>. Get one from <see cref="EmbeddedServerHost.UseVirtualClock"/>.
/// </summary>
/// <remarks>
/// <para>
/// What follows the clock: which server systems run and with what <c>dt</c>, tick listeners and
/// delayed callbacks, <c>world.ElapsedMilliseconds</c>, the server API's uptime, and, unless asked
/// otherwise, the calendar. Hunger, AI, block entity ticks such as a firepit's, fire spread and
/// entity physics therefore advance by simulated time, whatever the machine's speed.
/// </para>
/// <para>
/// What does not: the server's background threads (relight, random block ticks, the creature
/// spawner, chunk generation and async pathfinding) keep their own clocks and run on real time.
/// <see cref="EmbeddedServerHost.WaitForBackgroundWorkAsync"/> waits until they are idle.
/// </para>
/// <para>
/// Disposing goes back to the wall clock, carrying on from the simulated time: the server's
/// time never goes backwards.
/// </para>
/// </remarks>
public sealed class VirtualClock : IDisposable
{
    /// <summary>
    /// The longest step <see cref="Advance(TimeSpan)"/> takes: 250 ms. Entity physics discards
    /// the time it has to catch up on once it passes 0.4 s, so longer steps lose physics time.
    /// </summary>
    public static readonly TimeSpan DefaultMaxStep = TimeSpan.FromMilliseconds(250);

    private readonly EmbeddedServerHost _host;
    private readonly ServerClockState _state;
    private readonly long _startMs;
    private long _endMs = -1;

    internal VirtualClock(EmbeddedServerHost host, ServerClockState state, int tickMs, bool followsCalendar, long startMs)
    {
        _host = host;
        _state = state;
        TickMs = tickMs;
        FollowsCalendar = followsCalendar;
        _startMs = startMs;
    }

    /// <summary>How far each server tick moves the server's time, in milliseconds.</summary>
    public int TickMs { get; }

    /// <summary>Whether the calendar follows the simulated time rather than the wall clock.</summary>
    public bool FollowsCalendar { get; }

    /// <summary>Whether the clock still drives the server: false once disposed.</summary>
    public bool IsActive => Volatile.Read(ref _endMs) < 0;

    /// <summary>
    /// The server's time now, in milliseconds since it started: what <c>world.ElapsedMilliseconds</c>
    /// reads on the server.
    /// </summary>
    public long ElapsedMilliseconds => _state.Read();

    /// <summary>The simulated time since this clock started, up to when it was disposed.</summary>
    public TimeSpan Elapsed
    {
        get
        {
            long end = Volatile.Read(ref _endMs);
            return TimeSpan.FromMilliseconds((end < 0 ? _state.Read() : end) - _startMs);
        }
    }

    /// <summary>
    /// Moves the server's time forward by <paramref name="span"/>, ticking the server in steps of
    /// <see cref="DefaultMaxStep"/> or less. Returns the ticks run.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="span"/> is negative.</exception>
    /// <exception cref="ObjectDisposedException">The clock was disposed.</exception>
    /// <exception cref="ServerCrashedException">The server crashed during a tick.</exception>
    public int Advance(TimeSpan span) => Advance(span, DefaultMaxStep);

    /// <summary>
    /// Moves the server's time forward by <paramref name="span"/>, rounded to whole milliseconds,
    /// ticking the server in steps of <paramref name="maxStep"/> or less. Returns the ticks run.
    /// </summary>
    /// <remarks>
    /// Each step is one tick with that <c>dt</c>. Hunger works in 10-second buckets and physics
    /// drops what it has to catch up on past 0.4 s, so long steps change what is simulated.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="span"/> is negative, or <paramref name="maxStep"/> is not from 1 ms to 1 s.
    /// </exception>
    /// <exception cref="ObjectDisposedException">The clock was disposed.</exception>
    /// <exception cref="InvalidOperationException">The server is not running, or would not run a tick.</exception>
    /// <exception cref="ServerCrashedException">The server crashed during a tick.</exception>
    public int Advance(TimeSpan span, TimeSpan maxStep)
    {
        if (span < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(span), span, "Time only moves forward.");
        if (maxStep < TimeSpan.FromMilliseconds(1) || maxStep > TimeSpan.FromSeconds(1))
        {
            throw new ArgumentOutOfRangeException(nameof(maxStep), maxStep, "The step must be from 1 ms to 1 s.");
        }

        int ticks = 0;
        foreach (long step in Steps((long)Math.Round(span.TotalMilliseconds), (long)Math.Round(maxStep.TotalMilliseconds)))
        {
            ObjectDisposedException.ThrowIf(!IsActive, this);
            if (!_host.IsRunning) throw new InvalidOperationException("The server is not running.");

            _state.SetPendingStep(step);
            try
            {
                for (int attempt = 0; ; attempt++)
                {
                    _host.Tick();
                    ticks++;
                    if (!_state.HasPendingStep) break;
                    if (attempt >= 100) throw new InvalidOperationException("The server did not run a tick in 100 attempts: it is suspended or in standby.");
                }
            }
            finally
            {
                // A step a failed tick did not take must not stretch a later one.
                _state.SetPendingStep(0);
            }
        }

        return ticks;
    }

    /// <summary>
    /// Splits <paramref name="totalMs"/> into steps of <paramref name="maxStepMs"/>, the last one
    /// shorter when it does not divide evenly.
    /// </summary>
    internal static IEnumerable<long> Steps(long totalMs, long maxStepMs)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(totalMs);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxStepMs, 1);
        for (long left = totalMs; left > 0; left -= maxStepMs)
        {
            yield return Math.Min(left, maxStepMs);
        }
    }

    /// <summary>
    /// Hands the server back to the wall clock, carrying on from the simulated time without a jump.
    /// Safe to call more than once, from any thread.
    /// </summary>
    public void Dispose() => _host.EndVirtualClock(this);

    /// <summary>Marks the clock ended at <paramref name="endMs"/>; called by the host.</summary>
    internal void MarkEnded(long endMs) => Volatile.Write(ref _endMs, endMs);
}
