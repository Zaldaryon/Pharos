namespace Zaldaryon.Pharos.Network;

/// <summary>
/// Result of executing code within a crash containment scope.
/// </summary>
/// <param name="Crashed">Whether an exception was thrown during execution.</param>
/// <param name="ExceptionType">Fully qualified type name of the exception, if crashed.</param>
/// <param name="ExceptionMessage">Message from the exception, if crashed.</param>
/// <param name="StackTrace">Stack trace from the exception, if crashed.</param>
public sealed record CrashContainmentResult(
    bool Crashed,
    string? ExceptionType = null,
    string? ExceptionMessage = null,
    string? StackTrace = null)
{
    /// <summary>
    /// A successful result with no crash.
    /// </summary>
    public static CrashContainmentResult Success { get; } = new(Crashed: false);

    /// <summary>
    /// Creates a crash result from an exception.
    /// </summary>
    public static CrashContainmentResult FromException(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        return new CrashContainmentResult(
            Crashed: true,
            ExceptionType: exception.GetType().FullName,
            ExceptionMessage: exception.Message,
            StackTrace: exception.StackTrace);
    }

    /// <summary>
    /// Whether the execution completed successfully without crashing.
    /// </summary>
    public bool Succeeded => !Crashed;

    /// <summary>
    /// Gets a formatted summary of the crash for logging/display.
    /// </summary>
    public string? GetCrashSummary()
    {
        if (!Crashed) return null;
        return $"{ExceptionType}: {ExceptionMessage}";
    }
}
