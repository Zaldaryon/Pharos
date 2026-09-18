using Zaldaryon.Pharos.Server;

namespace Atlas.Api;

/// <summary>
/// Implementation of <see cref="IWorldSession"/> that wraps <see cref="EmbeddedServerHost"/>.
/// </summary>
[Obsolete("Use EmbeddedServerHost from Zaldaryon.Pharos.Server instead. This shim exists only for migration.")]
public sealed class WorldSession : IWorldSession
{
    private readonly EmbeddedServerHost _host;
    private bool _disposed;

    /// <summary>
    /// Initializes a new world session wrapping an existing embedded server host.
    /// </summary>
    /// <param name="host">The embedded server host to wrap.</param>
    public WorldSession(EmbeddedServerHost host)
    {
        ArgumentNullException.ThrowIfNull(host);
        _host = host;
    }

    /// <inheritdoc />
    public EmbeddedServerHost Host => _host;

    /// <inheritdoc />
    public bool IsRunning => _host.IsRunning;

    /// <inheritdoc />
    public int ConnectedClientCount => _host.ConnectedClientCount;

    /// <inheritdoc />
    public int TickCount => _host.TickCount;

    /// <inheritdoc />
    public void Tick() => _host.Tick();

    /// <inheritdoc />
    public void Ticks(int count) => _host.Ticks(count);

    /// <inheritdoc />
    public Task<bool> TickUntilAsync(Func<bool> predicate, int maxTicks = 600, CancellationToken ct = default)
        => _host.TickUntilAsync(predicate, maxTicks, ct);

    /// <summary>
    /// Creates a new world session with the specified options.
    /// </summary>
    /// <param name="options">World configuration options. When null, uses default superflat creative settings.</param>
    /// <returns>A running world session wrapping an embedded server host.</returns>
    public static WorldSession Create(WorldOptions? options = null)
    {
        ServerWorldOptions serverOptions = options?.ToServerWorldOptions() ?? new ServerWorldOptions();
        EmbeddedServerHost host = EmbeddedServerHost.Boot(serverOptions);
        return new WorldSession(host);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _host.Dispose();
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        await _host.DisposeAsync().ConfigureAwait(false);
    }
}
