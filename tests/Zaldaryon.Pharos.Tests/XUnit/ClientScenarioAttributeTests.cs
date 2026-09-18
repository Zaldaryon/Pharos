using System.Reflection;
using Xunit;
using Zaldaryon.Pharos.Core;
using Zaldaryon.Pharos.Player;
using Zaldaryon.Pharos.Timing;
using Zaldaryon.Pharos.XUnit;

namespace Zaldaryon.Pharos.Tests.XUnit;

/// <summary>
/// Tests for xUnit client scenario attributes and base classes.
/// </summary>
public class ClientScenarioAttributeTests
{
    [Fact]
    public void ClientScenarioAttribute_InheritsFactAttribute()
    {
        Assert.Equal(typeof(Xunit.FactAttribute), typeof(ClientScenarioAttribute).BaseType);
    }

    [Fact]
    public void ClientTheoryAttribute_InheritsTheoryAttribute()
    {
        Assert.Equal(typeof(Xunit.TheoryAttribute), typeof(ClientTheoryAttribute).BaseType);
    }

    [Fact]
    public void IsolationMode_SharedClient_IsDefault()
    {
        Assert.Equal(IsolationMode.SharedClient, default(IsolationMode));
    }

    [Fact]
    public void IsolationMode_HasThreeValues()
    {
        var values = Enum.GetValues<IsolationMode>();
        Assert.Equal(3, values.Length);
    }

    [Fact]
    public void ClientScenarioBase_HasClientProperty()
    {
        var prop = typeof(ClientScenarioBase).GetProperty(
            "Client",
            BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.NotNull(prop);
        Assert.Equal(typeof(HeadlessClient), prop.PropertyType);
    }

    [Fact]
    public void ClientScenarioBase_HasPlayerProperty()
    {
        var prop = typeof(ClientScenarioBase).GetProperty(
            "Player",
            BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.NotNull(prop);
        Assert.Equal(typeof(IClientTestPlayer), prop.PropertyType);
    }

    [Fact]
    public void ClientScenarioBase_HasFrameControllerProperty()
    {
        var prop = typeof(ClientScenarioBase).GetProperty(
            "FrameController",
            BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.NotNull(prop);
        Assert.Equal(typeof(DeterministicFrameController), prop.PropertyType);
    }

    [Fact]
    public void ClientFixture_EnsureInitialized_SetsIsInitialized()
    {
        using var fixture = new ClientFixture();
        Assert.False(fixture.IsInitialized);

        fixture.EnsureInitialized();

        Assert.True(fixture.IsInitialized);
    }

    [Fact]
    public void ClientFixture_Dispose_DoesNotThrow_WhenClientIsNull()
    {
        var fixture = new ClientFixture();
        Assert.Null(fixture.Client);

        var ex = Record.Exception(() => fixture.Dispose());

        Assert.Null(ex);
    }

    [Fact]
    public void ClientScenarioBase_DefaultIsolationMode_IsShared()
    {
        var scenario = new ConcreteClientScenario();
        Assert.Equal(IsolationMode.SharedClient, scenario.ExposedIsolationMode);
    }

    /// <summary>
    /// Concrete subclass of <see cref="ClientScenarioBase"/> for testing purposes.
    /// </summary>
    private sealed class ConcreteClientScenario : ClientScenarioBase
    {
        public IsolationMode ExposedIsolationMode => IsolationMode;
    }
}
