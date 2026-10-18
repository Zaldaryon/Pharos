using System.Runtime.CompilerServices;
using Xunit.v3;
using Zaldaryon.Pharos.Core;
using Zaldaryon.Pharos.XUnit;
using Zaldaryon.Pharos.XUnit.V3.Execution;

namespace Zaldaryon.Pharos.XUnit.V3;

/// <summary>
/// Marks a test method as a server scenario (Fact-based).
/// The test class should inherit from ServerScenarioBase for lifecycle management.
/// </summary>
[XunitTestCaseDiscoverer(typeof(ScenarioFactDiscovererV3))]
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public sealed class ServerScenarioAttribute : Xunit.FactAttribute, ITraitAttribute
{
    /// <summary>How many fresh scenario lifecycles to run at most when a body attempt fails.</summary>
    public int Attempts { get; set; } = 1;

    /// <summary>How many successful attempts are required; the scenario passes as soon as this count is reached.</summary>
    public int RequiredSuccesses { get; set; } = 1;

    /// <summary>The default timeout in milliseconds for server scenario tests (120 seconds).</summary>
    public const int DefaultTimeoutMs = 120_000;

    /// <summary>Gets or sets the world isolation mode for this test.</summary>
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
    /// fails, and its failure artifacts are saved. Scaled by PHAROS_TIMEOUT_SCALE.
    /// </summary>
    public int TimeoutMs { get; set; } = DefaultTimeoutMs;

    /// <summary>Initializes a new instance of the ServerScenarioAttribute class.</summary>
    public ServerScenarioAttribute(
        [CallerFilePath] string? sourceFilePath = null,
        [CallerLineNumber] int sourceLineNumber = 0)
        : base(sourceFilePath, sourceLineNumber)
    {
    }

    /// <inheritdoc />
    public IReadOnlyCollection<KeyValuePair<string, string>> GetTraits() =>
        [new(PharosTraits.Category, PharosTraits.Live)];
}
