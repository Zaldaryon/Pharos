namespace Zaldaryon.Pharos.XUnit.Execution;

/// <summary>
/// Stops a scenario body that ran out of time from driving the game any further.
/// </summary>
/// <remarks>
/// A test body cannot be killed, so when it outlives its timeout it is asked to stop: the body
/// runs with a token in an async-local, and every call that steps the client or the server or
/// marshals work onto their game threads checks it and throws <see cref="ScenarioAbortedException"/>.
/// The failure capture and the teardown run outside the body's flow, so they still reach the
/// game.
/// </remarks>
internal static class ScenarioAbort
{
    private static readonly AsyncLocal<AbortScope?> s_scope = new();

    /// <summary>The scope of the scenario body running on this flow, if any.</summary>
    public static AbortScope? Current => s_scope.Value;

    /// <summary>The token of the scenario body running on this flow, if any.</summary>
    public static CancellationToken Token => s_scope.Value?.Token ?? default;

    /// <summary>Marks the current flow as the scenario body of <paramref name="scope"/>, or as none.</summary>
    public static void Enter(AbortScope? scope) => s_scope.Value = scope;

    /// <summary>Throws when the scenario body running on this flow was aborted.</summary>
    /// <exception cref="ScenarioAbortedException">It was.</exception>
    public static void ThrowIfAborted()
    {
        if (s_scope.Value is { Token.IsCancellationRequested: true }) throw new ScenarioAbortedException();
    }
}

/// <summary>One scenario body's abort: the token that stops it, and whether it left the game stuck.</summary>
internal sealed class AbortScope(CancellationToken token)
{
    private volatile bool _wedged;

    /// <summary>Cancelled when the body runs out of time.</summary>
    public CancellationToken Token { get; } = token;

    /// <summary>
    /// Whether the body was let go while work it gave a game thread was still running: the game
    /// is stuck, and its host must be given up rather than torn down.
    /// </summary>
    public bool Wedged => _wedged;

    /// <summary>Records that the game is stuck on this body's work.</summary>
    public void MarkWedged() => _wedged = true;
}

/// <summary>
/// Thrown into a scenario body that kept running after its timeout, the next time it steps the
/// game or calls onto a game thread.
/// </summary>
public sealed class ScenarioAbortedException : OperationCanceledException
{
    /// <summary>Creates the exception.</summary>
    public ScenarioAbortedException()
        : base("The scenario ran out of time and was stopped; it may no longer drive the client or the server.")
    {
    }
}
