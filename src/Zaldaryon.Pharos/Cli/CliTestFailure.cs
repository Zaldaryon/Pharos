namespace Zaldaryon.Pharos.Cli;

/// <summary>
/// Represents a single test failure with diagnostic information.
/// </summary>
/// <param name="TestName">Fully qualified name of the failed test.</param>
/// <param name="FailureMessage">The failure message or exception message.</param>
/// <param name="StackTrace">Stack trace at the point of failure, if available.</param>
public sealed record CliTestFailure(
    string TestName,
    string FailureMessage,
    string StackTrace)
{
    /// <summary>
    /// Creates a failure record from an exception.
    /// </summary>
    /// <param name="testName">Name of the test that failed.</param>
    /// <param name="exception">The exception that caused the failure.</param>
    /// <returns>A new CliTestFailure instance.</returns>
    public static CliTestFailure FromException(string testName, Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        return new CliTestFailure(
            testName,
            exception.Message,
            exception.StackTrace ?? string.Empty);
    }

    /// <summary>
    /// Formats the failure for console output.
    /// </summary>
    /// <param name="verbose">Include stack trace in output.</param>
    /// <returns>Formatted failure string.</returns>
    public string Format(bool verbose = false)
    {
        if (verbose && !string.IsNullOrEmpty(StackTrace))
        {
            return $"""
                FAIL: {TestName}
                  Message: {FailureMessage}
                  Stack Trace:
                    {StackTrace.Replace("\n", "\n    ")}
                """;
        }

        return $"FAIL: {TestName} - {FailureMessage}";
    }
}
