using Xunit.Sdk;

namespace Zaldaryon.Pharos.XUnit;

/// <summary>
/// Marks a test method as a server scenario (Fact-based).
/// The test class should inherit from <see cref="ServerScenarioBase"/> for lifecycle management.
/// </summary>
/// <remarks>
/// <para>
/// Server scenarios run sequentially to avoid concurrent server access.
/// A watchdog timer enforces test timeout (default 120 seconds).
/// </para>
/// <para>
/// Use <see cref="ServerWorldAttribute"/> to configure world parameters.
/// Use <see cref="ServerModsAttribute"/> to stage mods before server bootstrap.
/// </para>
/// </remarks>
[XunitTestCaseDiscoverer(Execution.ScenarioFactDiscoverer.TypeName, Execution.ScenarioFactDiscoverer.AssemblyName)]
[TraitDiscoverer(LiveTraitDiscoverer.TypeName, LiveTraitDiscoverer.AssemblyName)]
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public sealed class ServerScenarioAttribute : Xunit.FactAttribute, ITraitAttribute
{
    /// <summary>
    /// The default timeout in milliseconds for server scenario tests (120 seconds).
    /// </summary>
    public const int DefaultTimeoutMs = 120_000;

    /// <summary>
    /// Gets or sets the world isolation mode for this test.
    /// When not set, uses <see cref="WorldIsolation.Rollback"/>.
    /// </summary>
    public WorldIsolation Isolation { get; set; } = WorldIsolation.Rollback;

    /// <summary>
    /// The watchdog timeout in milliseconds for the test body, 0 for none. A body that runs longer
    /// fails, and its failure artifacts are saved. Scaled by <c>PHAROS_TIMEOUT_SCALE</c>. xUnit's own
    /// <c>Timeout</c>, when set, takes its place.
    /// </summary>
    public int TimeoutMs { get; set; } = DefaultTimeoutMs;

    /// <summary>
    /// Initializes a new instance of the <see cref="ServerScenarioAttribute"/> class.
    /// </summary>
    public ServerScenarioAttribute()
    {
        // Timeout property is inherited from FactAttribute
        // but we expose TimeoutMs as the primary configuration point
    }
}
