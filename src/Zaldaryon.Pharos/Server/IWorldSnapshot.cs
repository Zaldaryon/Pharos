using System;

namespace Zaldaryon.Pharos.Server;

/// <summary>
/// Represents a captured snapshot of server world state for in-memory rollback.
/// </summary>
/// <remarks>
/// <para>
/// Snapshots enable fast test isolation by capturing and restoring world state
/// without rebooting the server. A typical capture/restore cycle completes in
/// under 100ms, compared to several seconds for a full server restart.
/// </para>
/// <para>
/// Implementations should be immutable records that hold a serialized copy
/// of the world state at the time of capture.
/// </para>
/// </remarks>
public interface IWorldSnapshot
{
    /// <summary>
    /// Unique identifier for this snapshot.
    /// </summary>
    Guid SnapshotId { get; }

    /// <summary>
    /// UTC timestamp when this snapshot was captured.
    /// </summary>
    DateTime CapturedAtUtc { get; }

    /// <summary>
    /// The server world options that were active when this snapshot was captured.
    /// </summary>
    ServerWorldOptions Options { get; }

    /// <summary>
    /// Restores this snapshot to the specified server host.
    /// </summary>
    /// <param name="host">The server host to restore to.</param>
    /// <exception cref="ArgumentNullException"><paramref name="host"/> is null.</exception>
    /// <exception cref="InvalidOperationException">The server is not in a state that allows restoration.</exception>
    void Restore(EmbeddedServerHost host);
}
