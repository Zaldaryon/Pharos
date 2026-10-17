using System;

namespace Zaldaryon.Pharos.XUnit;

/// <summary>
/// Specifies how test isolation is handled for client scenarios.
/// </summary>
[Obsolete("Use Isolation instead. This enum will be removed in 0.8.")]
public enum IsolationMode
{
    /// <summary>
    /// Tests share a single HeadlessClient instance across the test class.
    /// </summary>
    SharedClient = 0,

    /// <summary>
    /// State is rolled back between tests, but the client is reused.
    /// </summary>
    RollbackState = 1,

    /// <summary>
    /// Each test gets a fresh HeadlessClient instance.
    /// </summary>
    FreshClient = 2
}
