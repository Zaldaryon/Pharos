namespace Zaldaryon.Pharos;

/// <summary>
/// Thrown when an asynchronous Pharos waiting operation times out.
/// </summary>
public class PharosTimeoutException : TimeoutException
{
    public PharosTimeoutException()
    {
    }

    public PharosTimeoutException(string? message)
        : base(message)
    {
    }

    public PharosTimeoutException(string? message, Exception? innerException)
        : base(message, innerException)
    {
    }
}
