using System.Reflection;
using Xunit;
using Zaldaryon.Pharos.Bootstrap;
using Zaldaryon.Pharos.Core;
using Zaldaryon.Pharos.Player;
using Zaldaryon.Pharos.Server;
using Zaldaryon.Pharos.XUnit;

namespace Zaldaryon.Pharos.Tests.XUnit;

/// <summary>
/// Headless-safe tests for <see cref="ClientServerScenarioAttribute"/> and <see cref="ClientServerScenarioBase"/>.
/// Tests validate attribute properties, base class structure, property types, and IAsyncLifetime shape
/// without requiring live server/client boot.
/// </summary>
public class ClientServerScenarioTests
{
    // BindingFlags for protected members
    private const BindingFlags ProtectedInstance = BindingFlags.Instance | BindingFlags.NonPublic;

    #region ClientServerScenarioAttribute Tests

    [Fact]
    public void ClientServerScenarioAttribute_InheritsFactAttribute()
    {
        Assert.Equal(typeof(Xunit.FactAttribute), typeof(ClientServerScenarioAttribute).BaseType);
    }

    [Fact]
    public void ClientServerScenarioAttribute_HasMethodOnlyUsage()
    {
        var attr = typeof(ClientServerScenarioAttribute).GetCustomAttribute<AttributeUsageAttribute>();
        Assert.NotNull(attr);
        Assert.Equal(AttributeTargets.Method, attr.ValidOn);
        Assert.False(attr.AllowMultiple);
        Assert.True(attr.Inherited);
    }

    [Fact]
    public void ClientServerScenarioAttribute_DefaultIsolation_IsRollback()
    {
        var attr = new ClientServerScenarioAttribute();
        Assert.Equal(WorldIsolation.Rollback, attr.Isolation);
    }

    [Fact]
    public void ClientServerScenarioAttribute_DefaultTimeout_Is180Seconds()
    {
        var attr = new ClientServerScenarioAttribute();
        Assert.Equal(180_000, attr.TimeoutMs);
        Assert.Equal(180_000, ClientServerScenarioAttribute.DefaultTimeoutMs);
    }

    [Fact]
    public void ClientServerScenarioAttribute_TimeoutMs_CanBeSet()
    {
        var attr = new ClientServerScenarioAttribute { TimeoutMs = 60_000 };
        Assert.Equal(60_000, attr.TimeoutMs);
    }

    [Fact]
    public void ClientServerScenarioAttribute_Isolation_CanBeSet()
    {
        var attr = new ClientServerScenarioAttribute { Isolation = WorldIsolation.Restart };
        Assert.Equal(WorldIsolation.Restart, attr.Isolation);
    }

    [Fact]
    public void ClientServerScenarioAttribute_WaitForPlayerJoin_DefaultIsTrue()
    {
        var attr = new ClientServerScenarioAttribute();
        Assert.True(attr.WaitForPlayerJoin);
    }

    [Fact]
    public void ClientServerScenarioAttribute_WaitForPlayerJoin_CanBeSet()
    {
        var attr = new ClientServerScenarioAttribute { WaitForPlayerJoin = false };
        Assert.False(attr.WaitForPlayerJoin);
    }

    #endregion

    #region ClientServerScenarioBase Structure Tests

    [Fact]
    public void ClientServerScenarioBase_IsAbstractClass()
    {
        Assert.True(typeof(ClientServerScenarioBase).IsAbstract);
    }

    [Fact]
    public void ClientServerScenarioBase_ImplementsIAsyncLifetime()
    {
        Assert.True(typeof(IAsyncLifetime).IsAssignableFrom(typeof(ClientServerScenarioBase)));
    }

    [Fact]
    public void ClientServerScenarioBase_HasClientProperty()
    {
        var prop = typeof(ClientServerScenarioBase).GetProperty("Client", ProtectedInstance);
        Assert.NotNull(prop);
        Assert.Equal(typeof(HeadlessClient), prop.PropertyType);
        Assert.True(prop.CanRead);
        Assert.False(prop.CanWrite);
    }

    [Fact]
    public void ClientServerScenarioBase_HasServerHostProperty()
    {
        var prop = typeof(ClientServerScenarioBase).GetProperty("ServerHost", ProtectedInstance);
        Assert.NotNull(prop);
        Assert.Equal(typeof(EmbeddedServerHost), prop.PropertyType);
        Assert.True(prop.CanRead);
        Assert.False(prop.CanWrite);
    }

    [Fact]
    public void ClientServerScenarioBase_HasServerProperty()
    {
        var prop = typeof(ClientServerScenarioBase).GetProperty("Server", ProtectedInstance);
        Assert.NotNull(prop);
        Assert.Equal(typeof(Vintagestory.Server.ServerMain), prop.PropertyType);
        Assert.True(prop.CanRead);
        Assert.False(prop.CanWrite);
    }

    [Fact]
    public void ClientServerScenarioBase_HasSessionProperty()
    {
        var prop = typeof(ClientServerScenarioBase).GetProperty("Session", ProtectedInstance);
        Assert.NotNull(prop);
        Assert.Equal(typeof(ClientServerLoopbackSession), prop.PropertyType);
        Assert.True(prop.CanRead);
        Assert.False(prop.CanWrite);
    }

    [Fact]
    public void ClientServerScenarioBase_HasPlayerProperty()
    {
        var prop = typeof(ClientServerScenarioBase).GetProperty("Player", ProtectedInstance);
        Assert.NotNull(prop);
        Assert.Equal(typeof(IClientTestPlayer), prop.PropertyType);
        Assert.True(prop.CanRead);
        Assert.False(prop.CanWrite);
    }

    [Fact]
    public void ClientServerScenarioBase_HasIsConnectedProperty()
    {
        var prop = typeof(ClientServerScenarioBase).GetProperty("IsConnected", ProtectedInstance);
        Assert.NotNull(prop);
        Assert.Equal(typeof(bool), prop.PropertyType);
        Assert.True(prop.CanRead);
        Assert.False(prop.CanWrite);
    }

    #endregion

    #region Virtual Configuration Property Tests

    [Fact]
    public void ClientServerScenarioBase_WorldIsolation_IsVirtual()
    {
        var prop = typeof(ClientServerScenarioBase).GetProperty("WorldIsolation", ProtectedInstance);
        Assert.NotNull(prop);
        var getter = prop.GetGetMethod(nonPublic: true);
        Assert.NotNull(getter);
        Assert.True(getter.IsVirtual);
    }

    [Fact]
    public void ClientServerScenarioBase_WorldOptions_IsVirtual()
    {
        var prop = typeof(ClientServerScenarioBase).GetProperty("WorldOptions", ProtectedInstance);
        Assert.NotNull(prop);
        Assert.Equal(typeof(ServerWorldOptions), prop.PropertyType);
        var getter = prop.GetGetMethod(nonPublic: true);
        Assert.NotNull(getter);
        Assert.True(getter.IsVirtual);
    }

    [Fact]
    public void ClientServerScenarioBase_ClientOptions_IsVirtual()
    {
        var prop = typeof(ClientServerScenarioBase).GetProperty("ClientOptions", ProtectedInstance);
        Assert.NotNull(prop);
        Assert.Equal(typeof(HeadlessClientOptions), prop.PropertyType);
        var getter = prop.GetGetMethod(nonPublic: true);
        Assert.NotNull(getter);
        Assert.True(getter.IsVirtual);
    }

    [Fact]
    public void ClientServerScenarioBase_PlayerJoinTimeout_IsVirtual()
    {
        var prop = typeof(ClientServerScenarioBase).GetProperty("PlayerJoinTimeout", ProtectedInstance);
        Assert.NotNull(prop);
        Assert.Equal(typeof(TimeSpan), prop.PropertyType);
        var getter = prop.GetGetMethod(nonPublic: true);
        Assert.NotNull(getter);
        Assert.True(getter.IsVirtual);
    }

    [Fact]
    public void ClientServerScenarioBase_WaitForPlayerJoinOnInit_IsVirtual()
    {
        var prop = typeof(ClientServerScenarioBase).GetProperty("WaitForPlayerJoinOnInit", ProtectedInstance);
        Assert.NotNull(prop);
        Assert.Equal(typeof(bool), prop.PropertyType);
        var getter = prop.GetGetMethod(nonPublic: true);
        Assert.NotNull(getter);
        Assert.True(getter.IsVirtual);
    }

    [Fact]
    public void ClientServerScenarioBase_PlayerName_IsVirtual()
    {
        var prop = typeof(ClientServerScenarioBase).GetProperty("PlayerName", ProtectedInstance);
        Assert.NotNull(prop);
        Assert.Equal(typeof(string), prop.PropertyType);
        var getter = prop.GetGetMethod(nonPublic: true);
        Assert.NotNull(getter);
        Assert.True(getter.IsVirtual);
    }

    #endregion

    #region IAsyncLifetime Method Tests

    [Fact]
    public void ClientServerScenarioBase_InitializeAsync_IsVirtual()
    {
        var method = typeof(ClientServerScenarioBase).GetMethod("InitializeAsync", BindingFlags.Instance | BindingFlags.Public);
        Assert.NotNull(method);
        Assert.True(method.IsVirtual);
        Assert.Equal(typeof(Task), method.ReturnType);
    }

    [Fact]
    public void ClientServerScenarioBase_DisposeAsync_IsVirtual()
    {
        var method = typeof(ClientServerScenarioBase).GetMethod("DisposeAsync", BindingFlags.Instance | BindingFlags.Public);
        Assert.NotNull(method);
        Assert.True(method.IsVirtual);
        Assert.Equal(typeof(Task), method.ReturnType);
    }

    #endregion

    #region Step Methods Tests

    [Fact]
    public void ClientServerScenarioBase_HasStepMethod()
    {
        var method = typeof(ClientServerScenarioBase).GetMethod("Step", ProtectedInstance);
        Assert.NotNull(method);
        Assert.Equal(typeof(void), method.ReturnType);

        var parameters = method.GetParameters();
        Assert.Single(parameters);
        Assert.Equal("dt", parameters[0].Name);
        Assert.Equal(typeof(float), parameters[0].ParameterType);
        Assert.True(parameters[0].HasDefaultValue);
    }

    [Fact]
    public void ClientServerScenarioBase_HasStepFramesMethod()
    {
        var method = typeof(ClientServerScenarioBase).GetMethod("StepFrames", ProtectedInstance);
        Assert.NotNull(method);
        Assert.Equal(typeof(void), method.ReturnType);

        var parameters = method.GetParameters();
        Assert.Equal(2, parameters.Length);
        Assert.Equal("count", parameters[0].Name);
        Assert.Equal(typeof(int), parameters[0].ParameterType);
        Assert.Equal("dt", parameters[1].Name);
        Assert.Equal(typeof(float), parameters[1].ParameterType);
    }

    [Fact]
    public void ClientServerScenarioBase_HasStepUntilAsyncMethod()
    {
        var method = typeof(ClientServerScenarioBase).GetMethod("StepUntilAsync", ProtectedInstance);
        Assert.NotNull(method);
        Assert.Equal(typeof(Task<bool>), method.ReturnType);

        var parameters = method.GetParameters();
        Assert.Equal(4, parameters.Length);
        Assert.Equal("condition", parameters[0].Name);
        Assert.Equal(typeof(Func<bool>), parameters[0].ParameterType);
    }

    [Fact]
    public void ClientServerScenarioBase_HasWaitForWorldReadyAsyncMethod()
    {
        var method = typeof(ClientServerScenarioBase).GetMethod("WaitForWorldReadyAsync", ProtectedInstance);
        Assert.NotNull(method);
        Assert.Equal(typeof(Task<bool>), method.ReturnType);
    }

    #endregion
}
