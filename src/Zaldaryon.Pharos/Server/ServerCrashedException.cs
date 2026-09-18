using System;

namespace Zaldaryon.Pharos.Server;

/// <summary>
/// Thrown when the embedded server encounters a fatal error during tick processing.
/// </summary>
/// <remarks>
/// When the server crashes, all pending TickUntilAsync waiters and game thread dispatch tasks
/// are faulted with this exception, preserving the original crash cause.
/// </remarks>
public sealed class ServerCrashedException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="ServerCrashedException"/> class.
    /// </summary>
    public ServerCrashedException()
        : base("The embedded server crashed.")
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ServerCrashedException"/> class with a specified error message.
    /// </summary>
    /// <param name="message">The message that describes the error.</param>
    public ServerCrashedException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="ServerCrashedException"/> class with a specified error message
    /// and a reference to the inner exception that is the cause of this exception.
    /// </summary>
    /// <param name="message">The message that describes the error.</param>
    /// <param name="innerException">The exception that is the cause of the current exception.</param>
    public ServerCrashedException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>
    /// Creates a ServerCrashedException wrapping the original server exception.
    /// </summary>
    /// <param name="serverException">The exception that caused the server to crash.</param>
    /// <returns>A new ServerCrashedException with the server exception as the inner exception.</returns>
    public static ServerCrashedException FromServerException(Exception serverException)
    {
        return new ServerCrashedException(
            $"The embedded server crashed: {serverException.Message}",
            serverException);
    }
}
