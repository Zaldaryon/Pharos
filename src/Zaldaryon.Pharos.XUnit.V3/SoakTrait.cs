using Xunit.v3;
using Zaldaryon.Pharos.XUnit;

namespace Zaldaryon.Pharos.XUnit.V3;

/// <summary>
/// Marks a test method or class with Category=Soak trait in xUnit v3.
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
public sealed class SoakTraitAttribute : Attribute, ITraitAttribute
{
    /// <inheritdoc />
    public IReadOnlyCollection<KeyValuePair<string, string>> GetTraits() =>
        [new(PharosTraits.Category, PharosTraits.Soak)];
}
