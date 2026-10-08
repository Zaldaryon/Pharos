using System.Globalization;

namespace Zaldaryon.Pharos.XUnit;

/// <summary>
/// The event-bus events Pharos fires on the server and on the client around a rollback, so a mod
/// can save and reload state of its own. Listen with <c>api.Event.RegisterEventBusListener</c>.
/// </summary>
/// <remarks>
/// The data is a <c>TreeAttribute</c> with <c>test</c>, the test whose world was captured or rolled
/// back, and, for <see cref="Restored"/>, <c>chunks</c>, how many chunks were restored. Each side's
/// event fires on that side's game thread. Do not step the game from a handler.
/// </remarks>
public static class RollbackEvents
{
    /// <summary>Fired once on each side when the world is captured, right after the hosts boot.</summary>
    public const string Captured = "pharos:rollback:captured";

    /// <summary>
    /// Fired on each side, the server first, after a rollback has put the world back and the client
    /// has it again, before the next test of the class starts.
    /// </summary>
    public const string Restored = "pharos:rollback:restored";
}

/// <summary>How a test's hosts were made ready for it.</summary>
public enum IsolationKind
{
    /// <summary>The class's first test booted them.</summary>
    FirstBoot,

    /// <summary>They were booted for this test, after an earlier test of the class.</summary>
    Restarted,

    /// <summary>The previous test's hosts were kept and its world rolled back.</summary>
    RolledBack,

    /// <summary>The previous test's server was kept as it was, under <see cref="WorldIsolation.Recycle"/>.</summary>
    Recycled,
}

/// <summary>How a test's hosts were made ready for it, and what it cost.</summary>
/// <param name="Kind">How they were made ready.</param>
/// <param name="Reason">
/// Why they were booted again instead of rolled back, when a rollback was expected: the previous
/// test disconnected the client, its rollback failed, another class took the pooled hosts. Null
/// otherwise.
/// </param>
/// <param name="ChunksRestored">How many chunks the rollback restored.</param>
/// <param name="ListenersRemoved">How many listeners and callbacks the previous test left that the rollback removed.</param>
/// <param name="Duration">How long the boot or the rollback took.</param>
public sealed record IsolationReport(IsolationKind Kind, string? Reason, int ChunksRestored, int ListenersRemoved, TimeSpan Duration)
{
    /// <summary>How many seeded data files and mod configs the rollback put back. See <see cref="DataFilesAttribute"/>.</summary>
    public int DataFilesRestored { get; init; }

    /// <inheritdoc />
    public override string ToString()
    {
        string ms = Duration.TotalMilliseconds.ToString("0", CultureInfo.InvariantCulture) + " ms";
        return Kind switch
        {
            IsolationKind.RolledBack => $"rolled back ({ChunksRestored} chunks, {ListenersRemoved} listeners removed, {(DataFilesRestored > 0 ? $"{DataFilesRestored} data file{(DataFilesRestored == 1 ? "" : "s")} restored, " : "")}{ms})",
            IsolationKind.Recycled => "recycled",
            IsolationKind.FirstBoot => $"booted ({ms})",
            _ => Reason != null ? $"booted again: {Reason} ({ms})" : $"booted again ({ms})",
        };
    }
}

/// <summary>A test whose world could not be rolled back, in a class with <see cref="ServerWorldAttribute.StrictIsolation"/>.</summary>
public sealed class IsolationException(string message) : Exception(message);
