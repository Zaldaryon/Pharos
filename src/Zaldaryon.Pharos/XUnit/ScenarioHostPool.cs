using Zaldaryon.Pharos.Platform;

namespace Zaldaryon.Pharos.XUnit;

/// <summary>
/// Serializes scenario tests and keeps one booted host alive between consecutive tests of the
/// same class.
/// </summary>
/// <remarks>
/// <para>
/// The Vintage Story client and server keep process-wide static state (<c>GamePaths</c>,
/// <c>ScreenManager.Platform</c>, the shader registry, the server logger). Two hosts booting or
/// running at the same time in one process corrupt each other, and xUnit runs test classes in
/// parallel by default. Every scenario therefore holds <see cref="EnterAsync"/> for its whole
/// lifetime, from boot to teardown, which runs scenarios one at a time whatever the test
/// collection layout.
/// </para>
/// <para>
/// Booting is the expensive part of a scenario (tens of seconds for an engine-mode client), so
/// isolation modes that allow it reuse the host of the previous test when it belongs to the same
/// test class and was booted with the same configuration. The pool holds at most one host.
/// Requesting a different one disposes it first, and the last one is disposed when the process
/// exits.
/// </para>
/// </remarks>
internal static class ScenarioHostPool
{
    private static readonly SemaphoreSlim s_gate = new(1, 1);
    private static readonly object s_lock = new();
    private static PooledHost? s_pooled;

    private static int s_reclaim;
    private static string? s_poisoned;

    static ScenarioHostPool()
    {
        AppDomain.CurrentDomain.ProcessExit += (_, _) => Clear();
    }

    /// <summary>
    /// Waits until no other scenario is running. Dispose the result to let the next one in.
    /// </summary>
    public static async Task<IDisposable> EnterAsync(TimeSpan timeout, CancellationToken ct = default)
    {
        ThrowIfPoisoned();
        if (!await s_gate.WaitAsync(timeout, ct).ConfigureAwait(false))
        {
            throw new TimeoutException(
                $"Another Pharos scenario held the host for longer than {timeout.TotalSeconds:F0} seconds. " +
                "Scenarios run one at a time because the game keeps process-wide static state.");
        }

        // The scenario that held the gate may have wedged the game on its way out.
        try
        {
            ThrowIfPoisoned();
        }
        catch
        {
            s_gate.Release();
            throw;
        }

        return new Releaser();
    }

    /// <summary>
    /// Stops every later scenario in this process from booting: a timed-out scenario left the
    /// game's threads busy, and a new host would share the game's process-wide state with them.
    /// </summary>
    internal static void Poison(string reason) => Volatile.Write(ref s_poisoned, reason);

    private static void ThrowIfPoisoned()
    {
        if (Volatile.Read(ref s_poisoned) is { } reason)
        {
            throw new InvalidOperationException(
                $"No scenario can run in this process any more: {reason} kept running on the game's threads after it timed out. " +
                "See its failure artifacts for where it was stuck.");
        }
    }

    /// <summary>
    /// Takes the pooled host when it matches <paramref name="owner"/> and <paramref name="key"/>,
    /// otherwise disposes whatever is pooled. The caller owns the result until it calls
    /// <see cref="Return"/> or disposes it.
    /// </summary>
    public static T? Take<T>(Type owner, object key) where T : class, IDisposable
    {
        lock (s_lock)
        {
            if (s_pooled is { } pooled && pooled.Owner == owner && pooled.Host is T host && Equals(pooled.Key, key))
            {
                s_pooled = null;
                return host;
            }

            DisposePooled();
            return null;
        }
    }

    /// <summary>
    /// Pools <paramref name="host"/> for the next test of <paramref name="owner"/>.
    /// </summary>
    public static void Return(Type owner, object key, IDisposable host)
    {
        lock (s_lock)
        {
            DisposePooled();
            s_pooled = new PooledHost(owner, key, host);
        }
    }

    /// <summary>
    /// Disposes the pooled host, if any.
    /// </summary>
    public static void Clear()
    {
        lock (s_lock)
        {
            DisposePooled();
        }
    }

    /// <summary>
    /// Notes that the running scenario disposed a client or server, so the memory it held is
    /// reclaimed when the scenario leaves the host.
    /// </summary>
    internal static void HostDisposed() => Volatile.Write(ref s_reclaim, 1);

    private static void DisposePooled()
    {
        PooledHost? pooled = s_pooled;
        s_pooled = null;
        if (pooled != null) HostDisposed();

        try
        {
            pooled?.Host.Dispose();
        }
        catch
        {
            // A host that fails to shut down must not fail the test that happens to replace it.
        }
    }

    private sealed record PooledHost(Type Owner, object Key, IDisposable Host);

    private sealed class Releaser : IDisposable
    {
        private int _released;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0)
            {
                if (Interlocked.Exchange(ref s_reclaim, 0) == 1)
                {
                    NativeHeap.Reclaim();
                }

                s_gate.Release();
            }
        }
    }
}
