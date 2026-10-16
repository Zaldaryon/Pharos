using System.Reflection;
using HarmonyLib;

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

    /// <summary>
    /// Makes the next <paramref name="times"/> calls of <paramref name="method"/> on <typeparamref name="T"/> throw an
    /// <see cref="InjectedFaultException"/>, then lets the calls through again. A tick listener that throws is logged by the server as
    /// a fatal error, and the tick goes on without it.
    /// </summary>
    /// <remarks>
    /// The method is patched when the first fault names it, and the patch stays in place. An inlined method cannot be patched, so the
    /// target has to be reached through a delegate or a virtual call, and it is marked <c>NoInlining</c> in tests.
    /// </remarks>
    /// <exception cref="ArgumentException"><paramref name="method"/> is empty, or <typeparamref name="T"/> has no such method.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="times"/> is not positive.</exception>
    public ServerFault ThrowIn<T>(string method, int times = 1, Type[]? parameters = null) where T : class
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(method);
        if (times < 1) throw new ArgumentOutOfRangeException(nameof(times), times, "A fault throws at least once.");
        MethodInfo target = AccessTools.Method(typeof(T), method, parameters)
            ?? throw new ArgumentException($"{typeof(T).Name} has no method {method}.", nameof(method));

        var fault = new ThrowInFault(this, target, times);
        Add(fault);
        FaultPatches.Add(fault);
        return fault;
    }

    /// <summary>
    /// Makes every database write of a save the test makes on the game thread throw <paramref name="exception"/>. A write the game
    /// catches is logged as an error, and the save goes on. A write the game does not catch escapes the save, which throws it. The
    /// game's off-thread saves run on the chunk thread after a save, and they are not failed: a chunk they write is stored.
    /// </summary>
    /// <remarks>
    /// The write methods of the game's database connection are patched when the first such fault is added, and the patch stays.
    /// </remarks>
    public ServerFault FailSaves(Exception? exception = null)
    {
        var fault = new FailSavesFault(this, exception ?? new IOException("disk full"), _host);
        Add(fault);
        FaultPatches.AddSave(fault);
        return fault;
    }

    /// <summary>
    /// Holds back the chunks the server loads: for the next <paramref name="ticks"/> ticks, the chunk thread starts no load, so a column
    /// requested now is still missing until the ticks are done. A load already under way can still finish.
    /// </summary>
    /// <remarks>
    /// The fault counts ticks, not time, so the hold is the same on every machine. The hold is a lower bound: once the ticks are done,
    /// loads take as long as they take on the chunk thread.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="ticks"/> is not positive.</exception>
    public ServerFault DelayChunkLoads(int ticks)
    {
        if (ticks < 1) throw new ArgumentOutOfRangeException(nameof(ticks), ticks, "A delay needs at least one tick.");
        var fault = new DelayChunkLoadsFault(this, ticks);
        Add(fault);
        FaultPatches.AddChunkDelay(fault);
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
        Ended();
    }

    /// <summary>Called on the game thread before each tick, while the fault is in place.</summary>
    internal abstract void BeforeTick(EmbeddedServerHost host);

    /// <summary>Called once, after the fault has been taken out of its registry.</summary>
    internal virtual void Ended()
    {
    }

    internal void RecordHit() => Interlocked.Increment(ref _hits);
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

internal sealed class ThrowInFault(ServerFaults owner, MethodInfo method, int times) : ServerFault(owner)
{
    private int _remaining = times;

    internal MethodInfo Method { get; } = method;

    internal override void BeforeTick(EmbeddedServerHost host)
    {
    }

    internal override void Ended() => FaultPatches.Remove(this);

    // Takes one throw from the count, when any is left.
    internal bool TryTake()
    {
        int current;
        do
        {
            current = Volatile.Read(ref _remaining);
            if (current <= 0) return false;
        }
        while (Interlocked.CompareExchange(ref _remaining, current - 1, current) != current);

        RecordHit();
        return true;
    }
}

internal sealed class FailSavesFault(ServerFaults owner, Exception exception, EmbeddedServerHost host) : ServerFault(owner)
{
    internal Exception Exception { get; } = exception;

    // Only a save the game runs on the game thread, while the server is saving, fails.
    internal bool Applies => host.IsOnGameThread && host.Server.Saving;

    internal override void BeforeTick(EmbeddedServerHost host)
    {
    }

    internal override void Ended() => FaultPatches.RemoveSave(this);
}

internal sealed class DelayChunkLoadsFault(ServerFaults owner, int ticks) : ServerFault(owner)
{
    // Written on the game thread, read on the chunk thread.
    private long _ticks;

    internal override void BeforeTick(EmbeddedServerHost host) => Interlocked.Increment(ref _ticks);

    // Asked by the chunk thread before each load pass: true while the loads are held.
    internal bool Holds()
    {
        if (Volatile.Read(ref _ticks) >= ticks) return false;
        RecordHit();
        return true;
    }

    internal override void Ended() => FaultPatches.RemoveChunkDelay(this);
}
