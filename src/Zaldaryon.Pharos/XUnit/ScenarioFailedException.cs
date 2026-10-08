namespace Zaldaryon.Pharos.XUnit;

/// <summary>
/// A scenario failure, with what Pharos saved about it: the original failure's type and message,
/// then where the failure artifacts are and the errors the client and server logged.
/// </summary>
/// <remarks>
/// It carries the original exception's stack trace and no inner exception, so a test runner shows
/// the failure once. The original is in <see cref="Failure"/>.
/// </remarks>
public sealed class ScenarioFailedException : Exception
{
    private readonly string? _stackTrace;

    /// <summary>Wraps <paramref name="failure"/> with <paramref name="details"/>.</summary>
    public ScenarioFailedException(Exception failure, string details)
        : base(Describe(failure) + Environment.NewLine + Environment.NewLine + details)
    {
        Failure = failure;
        _stackTrace = failure.StackTrace;
    }

    /// <summary>The failure as the test or the setup raised it.</summary>
    public Exception Failure { get; }

    /// <inheritdoc />
    public override string? StackTrace => _stackTrace;

    private static string Describe(Exception failure) => failure.GetType().FullName + ": " + failure.Message;
}
