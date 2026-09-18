using System.Collections.Concurrent;

namespace Zaldaryon.Pharos.Bridge;

/// <summary>
/// Thread-safe bridge channel that enqueues game events for consumption by scenario code.
/// Only one channel can be active at a time. When no channel is active, publish calls are no-ops.
/// </summary>
public sealed class BridgeChannel : IBridgeChannel
{
    private static readonly object s_activeLock = new();
    private static BridgeChannel? s_active;

    private readonly ConcurrentQueue<BridgeEvent> _queue = new();

    /// <summary>
    /// Gets the currently active bridge channel, or null if none is active.
    /// </summary>
    public static BridgeChannel? Active
    {
        get
        {
            lock (s_activeLock)
            {
                return s_active;
            }
        }
    }

    /// <summary>
    /// Activates the given channel as the current active channel.
    /// Any previously active channel is replaced.
    /// </summary>
    /// <param name="channel">The channel to activate.</param>
    /// <exception cref="ArgumentNullException">Thrown when channel is null.</exception>
    public static void Activate(BridgeChannel channel)
    {
        ArgumentNullException.ThrowIfNull(channel);
        lock (s_activeLock)
        {
            s_active = channel;
        }
    }

    /// <summary>
    /// Deactivates the current active channel, setting it to null.
    /// </summary>
    public static void Deactivate()
    {
        lock (s_activeLock)
        {
            s_active = null;
        }
    }

    /// <inheritdoc />
    public void PublishFrameStart(double dt)
    {
        if (ReferenceEquals(Active, this))
        {
            _queue.Enqueue(BridgeEvent.FrameStart(dt));
        }
    }

    /// <inheritdoc />
    public void PublishFrameEnd(double dt)
    {
        if (ReferenceEquals(Active, this))
        {
            _queue.Enqueue(BridgeEvent.FrameEnd(dt));
        }
    }

    /// <inheritdoc />
    public void PublishChunkTessellated(int chunkX, int chunkY, int chunkZ)
    {
        if (ReferenceEquals(Active, this))
        {
            _queue.Enqueue(BridgeEvent.ChunkTessellated(chunkX, chunkY, chunkZ));
        }
    }

    /// <inheritdoc />
    public void PublishGuiStateChanged(string screenName)
    {
        if (ReferenceEquals(Active, this))
        {
            _queue.Enqueue(BridgeEvent.GuiStateChanged(screenName));
        }
    }

    /// <summary>
    /// Drains one event from the queue.
    /// </summary>
    /// <returns>The next event, or null if the queue is empty.</returns>
    public BridgeEvent? Drain()
    {
        return _queue.TryDequeue(out var ev) ? ev : null;
    }

    /// <summary>
    /// Drains all pending events from the queue.
    /// </summary>
    /// <returns>A list of all pending events. May be empty but never null.</returns>
    public IReadOnlyList<BridgeEvent> DrainAll()
    {
        var result = new List<BridgeEvent>();
        while (_queue.TryDequeue(out var ev))
        {
            result.Add(ev);
        }
        return result;
    }
}
