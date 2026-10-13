using Vintagestory.API.Common;

namespace Zaldaryon.Pharos.Events;

/// <summary>
/// The game events one side fired while the log recorded them, in firing order. Get one from
/// <see cref="ServerEventRecorder.Record"/> or <see cref="ClientEventRecorder.Record"/>, and dispose
/// it when done: disposing stops the listening and keeps the records.
/// </summary>
/// <remarks>
/// <para>
/// The game fires events synchronously, on its game thread, so records are in the exact order the
/// events fired. Records can be read from any thread while the game adds more.
/// </para>
/// <para>
/// A log a test leaves open is closed when the scenario ends, before its world is rolled back.
/// </para>
/// </remarks>
public abstract class GameEventLog : IDisposable
{
    private const int ListedInMessages = 30;

    private readonly object _lock = new();
    private readonly List<GameEventRecord> _records = [];
    private long _next;
    private Exception? _failure;
    private volatile bool _disposed;

    internal GameEventLog(EnumAppSide side)
    {
        Side = side;
    }

    /// <summary>The side whose events the log records.</summary>
    public EnumAppSide Side { get; }

    /// <summary>
    /// Whether the log is still listening: false once disposed, or once its server stopped or its
    /// client was disposed.
    /// </summary>
    public bool IsRecording => !_disposed && SourceAlive;

    /// <summary>How many records the log holds.</summary>
    public int Count
    {
        get
        {
            lock (_lock) return _records.Count;
        }
    }

    /// <summary>Every record, in firing order, as a copy.</summary>
    /// <exception cref="InvalidOperationException">Recording an event failed; the inner exception says why.</exception>
    public IReadOnlyList<GameEventRecord> All
    {
        get
        {
            lock (_lock)
            {
                ThrowIfFailed();
                return [.. _records];
            }
        }
    }

    /// <summary>The records of type <typeparamref name="T"/>, or of a type derived from it, in firing order.</summary>
    /// <exception cref="InvalidOperationException">Recording an event failed; the inner exception says why.</exception>
    public IReadOnlyList<T> Of<T>() where T : GameEventRecord
    {
        lock (_lock)
        {
            ThrowIfFailed();
            return [.. _records.OfType<T>()];
        }
    }

    /// <summary>
    /// The <see cref="GameEventRecord.Index"/> of the newest record, or -1 when nothing was recorded
    /// yet. Pass it as <c>since</c> to wait only for what comes after.
    /// </summary>
    public long Mark()
    {
        lock (_lock) return _next - 1;
    }

    /// <summary>
    /// Forgets the records so far. Indexes go on counting from where they were. A failure to record
    /// an event is not forgotten: every read after one throws.
    /// </summary>
    public void Clear()
    {
        lock (_lock) _records.Clear();
    }

    /// <summary>Stops listening. The records stay readable. Calling it again does nothing.</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try
        {
            Unsubscribe();
        }
        finally
        {
            Closed();
        }
    }

    /// <summary>Whether <see cref="Dispose"/> has run.</summary>
    internal bool IsDisposed => _disposed;

    /// <summary>Whether the server or client the log listens to is still there.</summary>
    internal abstract bool SourceAlive { get; }

    /// <summary>The step a record fired at, now.</summary>
    internal abstract long CurrentStep { get; }

    /// <summary>"ticks" or "frames".</summary>
    internal abstract string StepUnit { get; }

    /// <summary>Advances the side by one step.</summary>
    internal abstract Task StepAsync(CancellationToken ct);

    /// <summary>Removes the log's handlers; called once, by <see cref="Dispose"/>.</summary>
    internal abstract void Unsubscribe();

    /// <summary>Called once the log is disposed.</summary>
    internal virtual void Closed()
    {
    }

    /// <summary>
    /// Adds what <paramref name="make"/> builds, stamped with its index, side and step. It never
    /// throws into the game: the first failure is kept, and every read from then on throws it.
    /// </summary>
    internal void Add(Func<GameEventRecord> make)
    {
        if (_disposed) return;
        try
        {
            GameEventRecord record = make();
            long step = CurrentStep;
            lock (_lock)
            {
                _records.Add(record with { Index = _next++, Side = Side, Step = step });
            }
        }
        catch (Exception ex)
        {
            lock (_lock) _failure ??= ex;
        }
    }

    /// <summary>
    /// Waits, stepping <paramref name="maxSteps"/> steps at most, for a record of type
    /// <typeparamref name="T"/> after <paramref name="since"/> that matches
    /// <paramref name="predicate"/>, and returns the earliest such record.
    /// </summary>
    internal async Task<T> WaitCoreAsync<T>(System.Func<T, bool>? predicate, long? since, int maxSteps, CancellationToken ct) where T : GameEventRecord
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maxSteps);
        long from = since ?? -1;
        for (int step = 0; ; step++)
        {
            ct.ThrowIfCancellationRequested();
            T? found = Of<T>().FirstOrDefault(r => r.Index > from && (predicate?.Invoke(r) ?? true));
            if (found != null) return found;
            if (step >= maxSteps) break;
            if (!IsRecording)
            {
                throw new ObjectDisposedException(GetType().Name, _disposed
                    ? $"No {typeof(T).Name} matched in the log, and it was disposed, so no more will come."
                    : $"No {typeof(T).Name} matched in the log, and its {(Side == EnumAppSide.Server ? "server has stopped" : "client is gone")}.");
            }

            await StepAsync(ct).ConfigureAwait(false);
        }

        throw new TimeoutException($"No {typeof(T).Name}{(predicate != null ? " matching the predicate" : "")} was recorded{(since != null ? $" after #{since}" : "")} within {maxSteps} {StepUnit}. {Listing(All)}");
    }

    /// <summary>The records, one per line, for a failure message.</summary>
    internal static string Listing(IReadOnlyList<GameEventRecord> records)
    {
        if (records.Count == 0) return "Nothing was recorded.";
        IEnumerable<GameEventRecord> shown = records.Count > ListedInMessages ? records.Skip(records.Count - ListedInMessages) : records;
        return $"Recorded ({records.Count}{(records.Count > ListedInMessages ? $", the last {ListedInMessages}" : "")}):\n  " + string.Join("\n  ", shown);
    }

    private void ThrowIfFailed()
    {
        if (_failure != null) throw new InvalidOperationException("Recording a game event failed.", _failure);
    }
}
