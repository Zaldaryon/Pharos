using Zaldaryon.Pharos.Server;

namespace Atlas.Api;

/// <summary>
/// Compatibility shim for Atlas.Api.IWorldSession.
/// Maps to <see cref="EmbeddedServerHost"/> in Pharos.
/// </summary>
/// <remarks>
/// <para>
/// This interface provides source compatibility for existing Atlas test suites.
/// For new code, use <see cref="EmbeddedServerHost"/> directly.
/// </para>
/// <para>
/// The interface extends the base host to provide Atlas API naming conventions
/// while delegating all functionality to the native Pharos implementation.
/// </para>
/// </remarks>
[Obsolete("Use EmbeddedServerHost from Zaldaryon.Pharos.Server instead. This shim exists only for migration.")]
public interface IWorldSession : IDisposable, IAsyncDisposable
{
    /// <summary>
    /// Gets the underlying Pharos embedded server host.
    /// </summary>
    EmbeddedServerHost Host { get; }

    /// <summary>
    /// Returns true if the server is running and not disposed.
    /// </summary>
    bool IsRunning { get; }

    /// <summary>
    /// Returns the current number of connected clients.
    /// </summary>
    int ConnectedClientCount { get; }

    /// <summary>
    /// Gets the number of simulation ticks that have been processed.
    /// </summary>
    int TickCount { get; }

    /// <summary>
    /// Advances the server by exactly one simulation tick.
    /// </summary>
    void Tick();

    /// <summary>
    /// Advances the server by the specified number of consecutive ticks.
    /// </summary>
    /// <param name="count">The number of ticks to process.</param>
    void Ticks(int count);

    /// <summary>
    /// Advances the server until the specified predicate returns true or the maximum tick count is reached.
    /// </summary>
    Task<bool> TickUntilAsync(Func<bool> predicate, int maxTicks = 600, CancellationToken ct = default);
}
