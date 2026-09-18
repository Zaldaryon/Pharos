namespace Zaldaryon.Pharos.Assertions;

/// <summary>
/// Exception thrown when a PharosAssert assertion fails.
/// </summary>
public sealed class PharosAssertException : Exception
{
    public PharosAssertException(string message) : base(message) { }
    public PharosAssertException(string message, Exception innerException) : base(message, innerException) { }
}
