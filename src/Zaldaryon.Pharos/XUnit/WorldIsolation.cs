namespace Zaldaryon.Pharos.XUnit;

/// <summary>
/// Specifies how world state isolation is handled for server scenarios.
/// </summary>
public enum WorldIsolation
{
    /// <summary>
    /// World state is rolled back to a snapshot between tests.
    /// This is the fastest isolation mode, reusing the server instance.
    /// </summary>
    Rollback = 0,

    /// <summary>
    /// The server is fully restarted between tests.
    /// This provides complete isolation but is the slowest option.
    /// </summary>
    Restart = 1,

    /// <summary>
    /// The server is reused without rollback between tests in the same class.
    /// Tests may observe side effects from previous tests.
    /// </summary>
    Recycle = 2
}
