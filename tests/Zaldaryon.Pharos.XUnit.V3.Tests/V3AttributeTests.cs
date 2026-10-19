using Xunit;
using Xunit.v3;
using Zaldaryon.Pharos.Core;
using Zaldaryon.Pharos.UI;
using Zaldaryon.Pharos.XUnit;

namespace Zaldaryon.Pharos.XUnit.V3.Tests;

public class V3AttributeTests
{
    [Fact]
    public void ClientScenarioAttribute_HasLiveTrait()
    {
        ClientScenarioAttribute attr = new();
        IReadOnlyCollection<KeyValuePair<string, string>> traits = attr.GetTraits();

        Assert.Contains(new KeyValuePair<string, string>(PharosTraits.Category, PharosTraits.Live), traits);
        Assert.Equal(ClientScenarioAttribute.DefaultTimeoutMs, attr.TimeoutMs);
    }

    [Fact]
    public void ServerScenarioAttribute_HasLiveTrait()
    {
        ServerScenarioAttribute attr = new();
        IReadOnlyCollection<KeyValuePair<string, string>> traits = attr.GetTraits();

        Assert.Contains(new KeyValuePair<string, string>(PharosTraits.Category, PharosTraits.Live), traits);
        Assert.Equal(ServerScenarioAttribute.DefaultTimeoutMs, attr.TimeoutMs);
        Assert.Equal(Isolation.Rollback, attr.Isolation);
    }

    [Fact]
    public void ClientServerScenarioAttribute_HasLiveTrait()
    {
        ClientServerScenarioAttribute attr = new();
        IReadOnlyCollection<KeyValuePair<string, string>> traits = attr.GetTraits();

        Assert.Contains(new KeyValuePair<string, string>(PharosTraits.Category, PharosTraits.Live), traits);
        Assert.Equal(ClientServerScenarioAttribute.DefaultTimeoutMs, attr.TimeoutMs);
        Assert.True(attr.WaitForPlayerJoin);
    }

    [Fact]
    public void ClientTheoryAttribute_HasLiveTrait()
    {
        ClientTheoryAttribute attr = new();
        IReadOnlyCollection<KeyValuePair<string, string>> traits = attr.GetTraits();

        Assert.Contains(new KeyValuePair<string, string>(PharosTraits.Category, PharosTraits.Live), traits);
        Assert.Equal(ClientTheoryAttribute.DefaultTimeoutMs, attr.TimeoutMs);
    }

    [Fact]
    public void ServerTheoryAttribute_HasLiveTrait()
    {
        ServerTheoryAttribute attr = new();
        IReadOnlyCollection<KeyValuePair<string, string>> traits = attr.GetTraits();

        Assert.Contains(new KeyValuePair<string, string>(PharosTraits.Category, PharosTraits.Live), traits);
        Assert.Equal(ServerTheoryAttribute.DefaultTimeoutMs, attr.TimeoutMs);
    }

    [Fact]
    public void LiveTraitAttribute_ReturnsLiveCategory()
    {
        LiveTraitAttribute attr = new();
        Assert.Contains(new KeyValuePair<string, string>(PharosTraits.Category, PharosTraits.Live), attr.GetTraits());
    }

    [Fact]
    public void SoakTraitAttribute_ReturnsSoakCategory()
    {
        SoakTraitAttribute attr = new();
        Assert.Contains(new KeyValuePair<string, string>(PharosTraits.Category, PharosTraits.Soak), attr.GetTraits());
    }

    [Fact]
    public async Task ClientSettingsMatrixAttribute_ProvidesDataRows()
    {
        ClientSettingsMatrixAttribute attr = new("minimum", "guiScale:1.5");
        var method = typeof(V3AttributeTests).GetMethod(nameof(ClientSettingsMatrixAttribute_ProvidesDataRows))!;
        var rows = await attr.GetData(method, new Xunit.Sdk.DisposalTracker());

        Assert.Equal(2, rows.Count);
        Assert.True(attr.SupportsDiscoveryEnumeration());
    }

    [Fact]
    public async Task WindowSizesAttribute_ProvidesDataRows()
    {
        WindowSizesAttribute attr = new("1280x720", "1920x1080") { GuiScales = [1f, 1.5f] };
        var method = typeof(V3AttributeTests).GetMethod(nameof(WindowSizesAttribute_ProvidesDataRows))!;
        var rows = await attr.GetData(method, new Xunit.Sdk.DisposalTracker());

        Assert.Equal(4, rows.Count);
        Assert.True(attr.SupportsDiscoveryEnumeration());
    }
}
