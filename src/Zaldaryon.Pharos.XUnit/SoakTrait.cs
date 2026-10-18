using Xunit.Abstractions;
using Xunit.Sdk;

namespace Zaldaryon.Pharos.XUnit;

/// <summary>
/// Marks a test, or every test of a class, as a soak test: it runs game days of a real server, so
/// it gives the test <c>Category=Soak</c>. Pair it with a scenario attribute, which gives
/// <c>Category=Live</c>. The quick and full lanes leave soak tests out, and the nightly run is the
/// only one that runs them.
/// </summary>
[TraitDiscoverer(SoakTraitDiscoverer.TypeName, SoakTraitDiscoverer.AssemblyName)]
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
public sealed class SoakAttribute : Attribute, ITraitAttribute
{
}

/// <summary>
/// Gives the tests <see cref="SoakAttribute"/> marks the <c>Category=Soak</c> trait.
/// </summary>
public sealed class SoakTraitDiscoverer : ITraitDiscoverer
{
    /// <summary>The discoverer's type name, for <see cref="TraitDiscovererAttribute"/>.</summary>
    internal const string TypeName = "Zaldaryon.Pharos.XUnit.SoakTraitDiscoverer";

    /// <summary>The discoverer's assembly name, for <see cref="TraitDiscovererAttribute"/>.</summary>
    internal const string AssemblyName = "Zaldaryon.Pharos.XUnit";

    /// <inheritdoc />
    public IEnumerable<KeyValuePair<string, string>> GetTraits(IAttributeInfo traitAttribute)
    {
        yield return new KeyValuePair<string, string>(PharosTraits.Category, PharosTraits.Soak);
    }
}
