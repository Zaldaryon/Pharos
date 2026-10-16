namespace Zaldaryon.Pharos.Server;

/// <summary>
/// The faults a test injects into an embedded server. A fault lasts until it is disposed. <see cref="Clear"/> disposes the
/// rest; a scenario calls it when it ends, before the world is rolled back.
/// </summary>
public sealed class ServerFaults
{
    private readonly EmbeddedServerHost _host;
    private readonly object _lock = new();
    private ServerFault[] _active = [];

    internal ServerFaults(EmbeddedServerHost host)
    {
        _host = host;
    }

    /// <summary>
    /// Makes every <paramref name="every"/>th tick take <paramref name="delayMs"/> longer on the server's clock, counting from when
    /// the fault starts, as a tick on a busy machine would. The stretch is simulated time, so it needs a virtual clock.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="every"/> is not positive, or <paramref name="delayMs"/> is negative.</exception>
    /// <exception cref="InvalidOperationException">No virtual clock is in use.</exception>
    public ServerFault SlowTicks(int every, int delayMs)
    {
        if (every < 1) throw new ArgumentOutOfRangeException(nameof(every), every, "A slow tick needs a positive interval.");
        if (delayMs < 0) throw new ArgumentOutOfRangeException(nameof(delayMs), delayMs, "A delay cannot be negative.");
        if (_host.VirtualClock is not { IsActive: true })
        {
            throw new InvalidOperationException("SlowTicks stretches simulated time, so it needs a virtual clock: call UseVirtualClock first.");
        }

        var fault = new SlowTickFault(this, every, delayMs);
        Add(fault);
        return fault;
    }

    /// <summary>Disposes every fault still in place.</summary>
    public void Clear()
    {
        ServerFault[] faults;
        lock (_lock)
        {
            faults = _active;
            _active = [];
        }

        foreach (ServerFault fault in faults) fault.Dispose();
    }

    // Runs on the game thread before each tick, so a fault can stretch the step that tick takes.
    internal void BeforeTick()
    {
        ServerFault[] faults;
        lock (_lock) faults = _active;
        foreach (ServerFault fault in faults) fault.BeforeTick(_host);
    }

    private void Add(ServerFault fault)
    {
        lock (_lock) _active = [.. _active, fault];
    }

    internal void Remove(ServerFault fault)
    {
        lock (_lock) _active = [.. _active.Where(f => !ReferenceEquals(f, fault))];
    }
}

/// <summary>A fault a test injected, active until it is disposed.</summary>
public abstract class ServerFault : IDisposable
{
    private readonly ServerFaults _owner;
    private int _hits;
    private int _disposed;

    internal ServerFault(ServerFaults owner)
    {
        _owner = owner;
    }

    /// <summary>How many times the fault acted.</summary>
    public int Hits => Volatile.Read(ref _hits);

    /// <summary>Whether the fault is still in place.</summary>
    public bool IsActive => Volatile.Read(ref _disposed) == 0;

    /// <summary>Takes the fault out. It stops acting at once; disposing it again does nothing.</summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _owner.Remove(this);
    }

    /// <summary>Called on the game thread before each tick, while the fault is in place.</summary>
    internal abstract void BeforeTick(EmbeddedServerHost host);

    protected void RecordHit() => Interlocked.Increment(ref _hits);
}

internal sealed class SlowTickFault(ServerFaults owner, int every, int delayMs) : ServerFault(owner)
{
    private int _ticks;

    internal override void BeforeTick(EmbeddedServerHost host)
    {
        _ticks++;
        if (_ticks % every != 0) return;
        if (host.TryStretchNextTick(delayMs)) RecordHit();
    }
}
