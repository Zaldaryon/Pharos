using Xunit.Sdk;

namespace Zaldaryon.Pharos.XUnit;

/// <summary>
/// Marks a test method as a client theory (Theory-based, data-driven).
/// The test class should inherit from <see cref="ClientScenarioBase"/> for lifecycle management.
/// </summary>
[XunitTestCaseDiscoverer(Execution.ScenarioTheoryDiscoverer.TypeName, Execution.ScenarioTheoryDiscoverer.AssemblyName)]
[TraitDiscoverer(LiveTraitDiscoverer.TypeName, LiveTraitDiscoverer.AssemblyName)]
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public sealed class ClientTheoryAttribute : Xunit.TheoryAttribute, ITraitAttribute
{
    /// <summary>The default watchdog timeout in milliseconds for the test body (120 seconds).</summary>
    public const int DefaultTimeoutMs = 120_000;

    /// <summary>
    /// The watchdog timeout in milliseconds for the test body, 0 for none. A body that runs longer
    /// fails, and its failure artifacts are saved. Scaled by <c>PHAROS_TIMEOUT_SCALE</c>.
    /// </summary>
    public int TimeoutMs { get; set; } = DefaultTimeoutMs;
}
