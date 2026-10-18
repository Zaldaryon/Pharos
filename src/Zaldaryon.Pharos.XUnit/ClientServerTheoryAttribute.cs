using Xunit.Sdk;

namespace Zaldaryon.Pharos.XUnit;

/// <summary>
/// Marks a test method as a client-server theory (Theory-based, data-driven).
/// The test class should inherit from <see cref="ClientServerScenarioBase"/> for lifecycle management.
/// </summary>
[XunitTestCaseDiscoverer(Execution.ScenarioTheoryDiscoverer.TypeName, Execution.ScenarioTheoryDiscoverer.AssemblyName)]
[TraitDiscoverer(LiveTraitDiscoverer.TypeName, LiveTraitDiscoverer.AssemblyName)]
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public sealed class ClientServerTheoryAttribute : Xunit.TheoryAttribute, ITraitAttribute
{
    /// <summary>Whether the scenario runs only through an explicit name filter or PHAROS_RUN_EXPLICIT=1.</summary>
    public bool Explicit { get; set; }

    /// <summary>How many fresh scenario lifecycles to run at most when a body attempt fails.</summary>
    public int Attempts { get; set; } = 1;

    /// <summary>How many successful attempts are required; the scenario passes as soon as this count is reached.</summary>
    public int RequiredSuccesses { get; set; } = 1;

    /// <summary>The default timeout in milliseconds for client-server scenario tests (180 seconds).</summary>
    public const int DefaultTimeoutMs = 180_000;

    /// <summary>
    /// Gets or sets the world isolation mode for this test.
    /// When not set, uses <see cref="Isolation.Rollback"/>.
    /// </summary>
    public Isolation Isolation { get; set; } = Isolation.Rollback;

    /// <summary>Legacy world isolation mode.</summary>
    [Obsolete("Use Isolation instead. This property will be removed in 0.8.")]
    public WorldIsolation WorldIsolation
    {
        get => (WorldIsolation)Isolation;
        set => Isolation = (Isolation)value;
    }

    /// <summary>
    /// The watchdog timeout in milliseconds for the test body, 0 for none. A body that runs longer
    /// fails, and its failure artifacts are saved. Scaled by <c>PHAROS_TIMEOUT_SCALE</c>.
    /// </summary>
    public int TimeoutMs { get; set; } = DefaultTimeoutMs;

    /// <summary>
    /// Gets or sets whether to wait for the player to fully join before the test starts.
    /// When true (default), InitializeAsync blocks until the player has spawned into the world.
    /// </summary>
    public bool WaitForPlayerJoin { get; set; } = true;

    /// <summary>Initializes a new instance of the <see cref="ClientServerTheoryAttribute"/> class.</summary>
    public ClientServerTheoryAttribute()
    {
    }
}
