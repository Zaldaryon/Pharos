using System.Reflection;
using Xunit;
using Xunit.v3;
using Zaldaryon.Pharos.Core;
using Zaldaryon.Pharos.Scenarios;
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

    [Fact]
    public async Task WorldConfigMatrixAttribute_ProvidesDataRows()
    {
        WorldConfigMatrixAttribute attr = new("creatureHostility", "aggressive", "passive");
        var rows = await attr.GetData(TheoryMethod(), new Xunit.Sdk.DisposalTracker());

        Assert.Equal(2, rows.Count);
        Assert.True(attr.SupportsDiscoveryEnumeration());
        Assert.Equal(ScenarioConfigurationMatrixKind.World, attr.Kind);
    }

    [Fact]
    public async Task ServerConfigMatrixAttribute_ProvidesDataRows()
    {
        ServerConfigMatrixAttribute attr = new("MaxChunkRadius", "4", "8");
        var rows = await attr.GetData(TheoryMethod(), new Xunit.Sdk.DisposalTracker());

        Assert.Equal(2, rows.Count);
        Assert.Equal(ScenarioConfigurationMatrixKind.Server, attr.Kind);
    }

    [Fact]
    public async Task ModConfigMatrixAttribute_ProvidesDataRows()
    {
        ModConfigMatrixAttribute attr = new("mymod.json", "easy.json", "hard.json");
        var rows = await attr.GetData(TheoryMethod(), new Xunit.Sdk.DisposalTracker());

        Assert.Equal(2, rows.Count);
        Assert.Equal(ScenarioConfigurationMatrixKind.Mod, attr.Kind);
    }

    [Fact]
    public async Task CommandPrivilegeMatrixAttribute_ProvidesDataRows()
    {
        CommandPrivilegeMatrixAttribute attr = new("/mycmd reset", "suplayer", "admin") { Expected = "denied,allowed" };
        var rows = await attr.GetData(PrivilegeMethod(), new Xunit.Sdk.DisposalTracker());

        Assert.Equal(2, rows.Count);
        Assert.True(attr.SupportsDiscoveryEnumeration());
    }

    private static MethodInfo TheoryMethod() =>
        typeof(Rows).GetMethod("TheRow", BindingFlags.NonPublic | BindingFlags.Instance)!;

    private static MethodInfo PrivilegeMethod() =>
        typeof(Rows).GetMethod("ThePrivilegeRow", BindingFlags.NonPublic | BindingFlags.Instance)!;

    private sealed class Rows
    {
        private void TheRow(string value)
        {
        }

        private void ThePrivilegeRow(CommandPrivilegeCase privilege)
        {
        }
    }
}
