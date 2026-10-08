using System.Collections.Concurrent;
using System.Runtime.ExceptionServices;
using Zaldaryon.Pharos.XUnit.Execution;

namespace Zaldaryon.Pharos.Platform;

/// <summary>
/// A dedicated, long-lived thread that plays the part of a game thread: the client's main thread
/// for an engine-mode client, the server's game thread for an embedded server.
/// </summary>
/// <remarks>
/// <para>
/// Both sides of the game run on one long-lived thread each, and parts of the engine depend on
/// it. On the client the GL context is current there, <c>RuntimeEnv.MainThreadId</c> names it,
/// and the shape tessellator keeps a thread-static tessellator on it for good while handing
/// one-off tessellation to thread pool threads, each of which must start without one. On the
/// server the frame profiler, the network channels' send buffers and parts of entity physics are
/// thread-static, created on the game thread, and some listeners refuse registration from any
/// other thread.
/// </para>
/// <para>
/// A test, though, runs on whichever thread pool thread xUnit picks for each continuation.
/// Driving the engine from those threads left the client's tessellator on a pool thread, where
/// the engine's next background tessellation aborted the process, and made the server tick into
/// null thread-static state. Engine work is therefore marshalled onto one of these threads, which
/// is not a thread pool thread and lives as long as its host. Calls block the caller until the
/// work is done and rethrow its exception, so stepping stays synchronous and lockstep.
/// </para>
/// </remarks>
internal sealed class EngineThread : IDisposable
{
    private readonly BlockingCollection<Action> _work = new();
    private readonly Thread _thread;
    private int _disposed;

    public EngineThread(string name)
    {
        _thread = new Thread(Run)
        {
            IsBackground = true,
            Name = name,
        };

        // The thread outlives whatever started it, a scenario body included: it must not carry
        // that body's abort token or other async-locals.
        using (ExecutionContext.SuppressFlow())
        {
            _thread.Start();
        }
    }

    /// <summary>Whether the caller is already on this thread.</summary>
    public bool IsCurrent => Environment.CurrentManagedThreadId == _thread.ManagedThreadId;

    /// <summary>Runs <paramref name="action"/> on this thread and waits for it.</summary>
    public void Invoke(Action action) => Invoke<object?>(() =>
    {
        action();
        return null;
    });

    /// <summary>Runs <paramref name="func"/> on this thread and returns its result.</summary>
    public T Invoke<T>(Func<T> func)
    {
        // A scenario body that ran out of time may not queue any more work.
        ScenarioAbort.ThrowIfAborted();
        if (IsCurrent) return func();
        if (Volatile.Read(ref _disposed) != 0) throw new ObjectDisposedException(nameof(EngineThread));

        T result = default!;
        ExceptionDispatchInfo? error = null;
        using ManualResetEventSlim done = new();

        _work.Add(() =>
        {
            try
            {
                result = func();
            }
            catch (Exception ex)
            {
                error = ExceptionDispatchInfo.Capture(ex);
            }
            finally
            {
                done.Set();
            }
        });

        // Work already queued is waited for even after an abort: a body that returned while its
        // work still ran would let the teardown queue behind it. If the game thread is stuck, the
        // body stays stuck too, and the pipeline gives its host up instead of tearing it down.
        done.Wait();
        error?.Throw();
        return result;
    }

    /// <summary>Stops the thread once the queued work has run.</summary>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;

        _work.CompleteAdding();
        if (!IsCurrent)
        {
            _thread.Join();
        }
    }

    private void Run()
    {
        foreach (Action work in _work.GetConsumingEnumerable())
        {
            work();
        }
    }
}
