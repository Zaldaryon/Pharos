using Xunit.Abstractions;
using Xunit.Sdk;

namespace Zaldaryon.Pharos.XUnit;

/// <summary>
/// The traits Pharos gives tests, for filtering a test run.
/// </summary>
/// <remarks>
/// Every test marked with a scenario attribute (<see cref="ClientScenarioAttribute"/>,
/// <see cref="ClientTheoryAttribute"/>, <see cref="ServerScenarioAttribute"/>,
/// <see cref="ServerTheoryAttribute"/>, <see cref="ClientServerScenarioAttribute"/>) is
/// <c>Category=Live</c>: it boots a real client or server, which takes seconds. A quick run
/// leaves those out with <c>dotnet test --filter "Category!=Live"</c>. A test that boots a host
/// without a scenario attribute can say so with <c>[Trait(PharosTraits.Category, PharosTraits.Live)]</c>.
/// </remarks>
public static class PharosTraits
{
    /// <summary>The trait name Pharos uses.</summary>
    public const string Category = "Category";

    /// <summary>Tests that boot a real client or server.</summary>
    public const string Live = "Live";

    /// <summary>Tests that measure throughput and take long to run.</summary>
    public const string Benchmark = "Benchmark";
}

/// <summary>
/// Gives the tests a scenario attribute marks the <c>Category=Live</c> trait.
/// </summary>
public sealed class LiveTraitDiscoverer : ITraitDiscoverer
{
    /// <summary>The discoverer's type name, for <see cref="TraitDiscovererAttribute"/>.</summary>
    internal const string TypeName = "Zaldaryon.Pharos.XUnit.LiveTraitDiscoverer";

    /// <summary>The discoverer's assembly name, for <see cref="TraitDiscovererAttribute"/>.</summary>
    internal const string AssemblyName = "Zaldaryon.Pharos";

    /// <inheritdoc />
    public IEnumerable<KeyValuePair<string, string>> GetTraits(IAttributeInfo traitAttribute)
    {
        yield return new KeyValuePair<string, string>(PharosTraits.Category, PharosTraits.Live);
    }
}
