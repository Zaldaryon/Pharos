namespace Zaldaryon.Pharos.Server;

/// <summary>
/// Profiles the server's work for a number of ticks and reports what each system, mod, block
/// entity, entity, behavior and AI task cost, and what each player's traffic was.
/// </summary>
/// <remarks>
/// <para>
/// Get one from <see cref="EmbeddedServerHost.Profile"/>. <see cref="MeasureAsync"/> advances the
/// server itself, so the world keeps running while it measures; set the world up first, then
/// measure a stretch of ticks. Times are the CPU time of each call, measured with the stopwatch on
/// the server thread, so they do not depend on the clock mode being virtual.
/// </para>
/// <para>
/// Only one measurement runs at a time, and a measurement that is open while another test's
/// automatic tick collector runs is fine: both read the same tick.
/// </para>
/// </remarks>
public sealed class ServerProfileDriver
{
    private readonly EmbeddedServerHost _host;

    internal ServerProfileDriver(EmbeddedServerHost host) => _host = host;

    /// <summary>
    /// Measures <paramref name="ticks"/> server ticks and returns the profile.
    /// </summary>
    /// <param name="ticks">How many ticks to run and measure; at least one.</param>
    /// <param name="ct">Cancels the measurement between ticks.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="ticks"/> is zero or negative.</exception>
    /// <exception cref="InvalidOperationException">Another measurement is already open.</exception>
    /// <remarks>
    /// The last tick may not be measured when the server spends it suspended, as during an autosave:
    /// <see cref="ServerProfile.Ticks"/> is the number of ticks that ran.
    /// </remarks>
    public async Task<ServerProfile> MeasureAsync(int ticks, CancellationToken ct = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(ticks);
        ct.ThrowIfCancellationRequested();

        ServerProfiler profiler = _host.RunOnGameThread(() => ServerProfiler.Open(_host, ticks));
        try
        {
            await Task.Run(() =>
            {
                for (int i = 0; i < ticks; i++)
                {
                    ct.ThrowIfCancellationRequested();
                    _host.Tick();
                }
            }, ct).ConfigureAwait(false);
        }
        catch
        {
            _host.RunOnGameThread(profiler.Abort);
            throw;
        }

        return _host.RunOnGameThread(profiler.Close);
    }

    /// <summary>Measures <paramref name="ticks"/> ticks on the calling thread.</summary>
    /// <param name="ticks">How many ticks to run and measure; at least one.</param>
    public ServerProfile Measure(int ticks)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(ticks);
        ServerProfiler profiler = _host.RunOnGameThread(() => ServerProfiler.Open(_host, ticks));
        try
        {
            for (int i = 0; i < ticks; i++) _host.Tick();
        }
        catch
        {
            _host.RunOnGameThread(profiler.Abort);
            throw;
        }

        return _host.RunOnGameThread(profiler.Close);
    }
}
