using System.Reflection;
using Xunit;
using Xunit.Sdk;
using Zaldaryon.Pharos.XUnit;

namespace Zaldaryon.Pharos.Tests.XUnit;

public class PharosTraitsTests
{
    [Theory]
    [InlineData(typeof(ClientScenarioAttribute))]
    [InlineData(typeof(ClientTheoryAttribute))]
    [InlineData(typeof(ServerScenarioAttribute))]
    [InlineData(typeof(ServerTheoryAttribute))]
    [InlineData(typeof(ClientServerScenarioAttribute))]
    public void ScenarioAttributes_MarkTheirTestsLive(Type attribute)
    {
        Assert.True(typeof(ITraitAttribute).IsAssignableFrom(attribute), $"{attribute.Name} gives its tests no trait");

        // xUnit finds the discoverer by the type and assembly names the attribute gives it.
        CustomAttributeData discoverer = Assert.Single(
            attribute.GetCustomAttributesData(), a => a.AttributeType == typeof(TraitDiscovererAttribute));
        string typeName = (string)discoverer.ConstructorArguments[0].Value!;
        string assemblyName = (string)discoverer.ConstructorArguments[1].Value!;

        Assert.Equal(typeof(LiveTraitDiscoverer), Type.GetType($"{typeName}, {assemblyName}"));
    }

    [Fact]
    public void LiveTraitDiscoverer_GivesTheLiveCategory()
    {
        KeyValuePair<string, string> trait = Assert.Single(new LiveTraitDiscoverer().GetTraits(null!));

        Assert.Equal(PharosTraits.Category, trait.Key);
        Assert.Equal(PharosTraits.Live, trait.Value);
    }
}
