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
    private static readonly AsyncLocal<CancellationToken> s_token = new();

    /// <summary>The token of the scenario body running on this flow, if any.</summary>
    public static CancellationToken Token => s_token.Value;

    /// <summary>Marks the current flow as a scenario body that <paramref name="token"/> aborts.</summary>
    public static void Enter(CancellationToken token) => s_token.Value = token;

    /// <summary>Throws when the scenario body running on this flow was aborted.</summary>
    /// <exception cref="ScenarioAbortedException">It was.</exception>
    public static void ThrowIfAborted()
    {
        if (s_token.Value.IsCancellationRequested) throw new ScenarioAbortedException();
    }
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
