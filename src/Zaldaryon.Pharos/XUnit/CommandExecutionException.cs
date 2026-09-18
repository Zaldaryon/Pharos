namespace Zaldaryon.Pharos.XUnit;

/// <summary>
/// Exception thrown when a server command execution fails.
/// </summary>
public sealed class CommandExecutionException : Exception
{
    /// <summary>
    /// Gets the command result that caused the exception.
    /// </summary>
    public CommandResult Result { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="CommandExecutionException"/> class.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="result">The command result that caused the exception.</param>
    public CommandExecutionException(string message, CommandResult result)
        : base(message)
    {
        Result = result;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="CommandExecutionException"/> class.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="result">The command result that caused the exception.</param>
    /// <param name="innerException">The inner exception.</param>
    public CommandExecutionException(string message, CommandResult result, Exception innerException)
        : base(message, innerException)
    {
        Result = result;
    }
}
