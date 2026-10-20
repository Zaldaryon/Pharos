namespace Zaldaryon.Pharos.Server;

/// <summary>
/// Generates a fresh chunk area and reports what each world generator cost. See
/// <see cref="EmbeddedServerHost.Worldgen"/>.
/// </summary>
/// <remarks>
/// The measurement runs the server's own chunk queue, so the game decides how many passes run and
/// which neighbouring columns it needs. The area stays loaded afterwards, like any chunk a player
/// visits; measure an area the world has not generated, or the measurement refuses to start.
/// </remarks>
public sealed class ServerWorldgenDriver
{
    private const int DefaultMaxTicks = 4000;

    private readonly EmbeddedServerHost _host;

    internal ServerWorldgenDriver(EmbeddedServerHost host) => _host = host;

    /// <summary>Generates the chunk area and returns the profile.</summary>
    /// <param name="chunkX1">The first chunk X, inclusive.</param>
    /// <param name="chunkZ1">The first chunk Z, inclusive.</param>
    /// <param name="chunkX2">The last chunk X, inclusive.</param>
    /// <param name="chunkZ2">The last chunk Z, inclusive.</param>
    /// <param name="maxTicks">How many server ticks to run while waiting for the area; at least one.</param>
    /// <param name="ct">Cancels the wait between ticks.</param>
    /// <exception cref="ArgumentOutOfRangeException">The area leaves the world, covers more than 64 columns, or <paramref name="maxTicks"/> is not positive.</exception>
    /// <exception cref="InvalidOperationException">The area is already loaded, or another worldgen measurement is open.</exception>
    /// <exception cref="TimeoutException">The area did not finish generating within <paramref name="maxTicks"/>.</exception>
    public async Task<WorldgenProfile> MeasureAsync(int chunkX1, int chunkZ1, int chunkX2, int chunkZ2,
        int maxTicks = DefaultMaxTicks, CancellationToken ct = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxTicks);
        ct.ThrowIfCancellationRequested();

        WorldgenMeter meter = _host.RunOnGameThread(() => WorldgenMeter.Open(_host, chunkX1, chunkZ1, chunkX2, chunkZ2));
        try
        {
            bool finished = await _host.TryTickUntilAsync(() => meter.Completed, maxTicks, ct).ConfigureAwait(false);
            if (!finished)
            {
                throw new TimeoutException(
                    $"Worldgen for chunks ({chunkX1},{chunkZ1}) to ({chunkX2},{chunkZ2}) did not finish in {maxTicks} ticks.");
            }

            return _host.RunOnGameThread(meter.Close);
        }
        catch
        {
            _host.RunOnGameThread(meter.Abort);
            throw;
        }
    }

    /// <summary>Generates the chunk area on the calling thread and returns the profile.</summary>
    /// <param name="chunkX1">The first chunk X, inclusive.</param>
    /// <param name="chunkZ1">The first chunk Z, inclusive.</param>
    /// <param name="chunkX2">The last chunk X, inclusive.</param>
    /// <param name="chunkZ2">The last chunk Z, inclusive.</param>
    /// <param name="maxTicks">How many server ticks to run while waiting for the area; at least one.</param>
    /// <exception cref="ArgumentOutOfRangeException">The area leaves the world, covers more than 64 columns, or <paramref name="maxTicks"/> is not positive.</exception>
    /// <exception cref="InvalidOperationException">The area is already loaded, or another worldgen measurement is open.</exception>
    /// <exception cref="TimeoutException">The area did not finish generating within <paramref name="maxTicks"/>.</exception>
    public WorldgenProfile Measure(int chunkX1, int chunkZ1, int chunkX2, int chunkZ2,
        int maxTicks = DefaultMaxTicks)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxTicks);

        WorldgenMeter meter = _host.RunOnGameThread(() => WorldgenMeter.Open(_host, chunkX1, chunkZ1, chunkX2, chunkZ2));
        try
        {
            for (int i = 0; i < maxTicks && !meter.Completed; i++) _host.Tick();
            if (!meter.Completed)
            {
                throw new TimeoutException(
                    $"Worldgen for chunks ({chunkX1},{chunkZ1}) to ({chunkX2},{chunkZ2}) did not finish in {maxTicks} ticks.");
            }

            return _host.RunOnGameThread(meter.Close);
        }
        catch
        {
            _host.RunOnGameThread(meter.Abort);
            throw;
        }
    }
}
