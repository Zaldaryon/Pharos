using System.Runtime.CompilerServices;
using Xunit.v3;
using Zaldaryon.Pharos.XUnit;
using Zaldaryon.Pharos.XUnit.V3.Execution;

namespace Zaldaryon.Pharos.XUnit.V3;

/// <summary>
/// Marks a test method as a client theory (Theory-based, data-driven).
/// The test class should inherit from ClientScenarioBase for lifecycle management.
/// </summary>
[XunitTestCaseDiscoverer(typeof(ScenarioTheoryDiscovererV3))]
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public sealed class ClientTheoryAttribute : Xunit.TheoryAttribute, ITraitAttribute
{
    /// <summary>How many fresh scenario lifecycles to run at most when a body attempt fails.</summary>
    public int Attempts { get; set; } = 1;

    /// <summary>How many successful attempts are required; the scenario passes as soon as this count is reached.</summary>
    public int RequiredSuccesses { get; set; } = 1;

    /// <summary>The default watchdog timeout in milliseconds for the test body (120 seconds).</summary>
    public const int DefaultTimeoutMs = 120_000;

    /// <summary>
    /// The watchdog timeout in milliseconds for the test body, 0 for none. A body that runs longer
    /// fails, and its failure artifacts are saved. Scaled by PHAROS_TIMEOUT_SCALE.
    /// </summary>
    public int TimeoutMs { get; set; } = DefaultTimeoutMs;

    /// <summary>Initializes a new instance of the ClientTheoryAttribute class.</summary>
    public ClientTheoryAttribute(
        [CallerFilePath] string? sourceFilePath = null,
        [CallerLineNumber] int sourceLineNumber = 0)
        : base(sourceFilePath, sourceLineNumber)
    {
    }

    /// <inheritdoc />
    public IReadOnlyCollection<KeyValuePair<string, string>> GetTraits() =>
        [new(PharosTraits.Category, PharosTraits.Live)];
}
