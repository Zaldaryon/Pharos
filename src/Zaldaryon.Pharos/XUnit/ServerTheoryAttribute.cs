namespace Zaldaryon.Pharos.XUnit;

/// <summary>
/// Marks a test method as a server theory (Theory-based, data-driven).
/// The test class should inherit from <see cref="ServerScenarioBase"/> for lifecycle management.
/// </summary>
/// <remarks>
/// <para>
/// Server theories run sequentially to avoid concurrent server access.
/// A watchdog timer enforces test timeout (default 120 seconds).
/// </para>
/// <para>
/// Use <see cref="ServerWorldAttribute"/> to configure world parameters.
/// Use <see cref="ServerModsAttribute"/> to stage mods before server bootstrap.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public sealed class ServerTheoryAttribute : Xunit.TheoryAttribute
{
    /// <summary>
    /// The default timeout in milliseconds for server theory tests (120 seconds).
    /// </summary>
    public const int DefaultTimeoutMs = 120_000;

    /// <summary>
    /// Gets or sets the world isolation mode for this test.
    /// When not set, uses <see cref="WorldIsolation.Rollback"/>.
    /// </summary>
    public WorldIsolation Isolation { get; set; } = WorldIsolation.Rollback;

    /// <summary>
    /// Gets or sets the watchdog timeout in milliseconds.
    /// When not set, uses <see cref="DefaultTimeoutMs"/> (120 seconds).
    /// </summary>
    public int TimeoutMs { get; set; } = DefaultTimeoutMs;

    /// <summary>
    /// Initializes a new instance of the <see cref="ServerTheoryAttribute"/> class.
    /// </summary>
    public ServerTheoryAttribute()
    {
        // Timeout property is inherited from TheoryAttribute
        // but we expose TimeoutMs as the primary configuration point
    }
}
