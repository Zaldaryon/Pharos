using System.Reflection;
using Vintagestory.API.Common;
using Vintagestory.API.Server;
using Vintagestory.Server;
using Xunit;
using Zaldaryon.Pharos.Server;
using Zaldaryon.Pharos.XUnit;

namespace Zaldaryon.Pharos.Tests.XUnit;

/// <summary>
/// Tests for <see cref="ServerScenarioBase"/> and related command execution types.
/// </summary>
public class ServerScenarioBaseTests
{
    [Fact]
    public void CommandResult_Ok_ReturnsTrueForSuccess()
    {
        var result = new CommandResult(EnumCommandStatus.Success, "Done");
        Assert.True(result.Ok);
    }

    [Fact]
    public void CommandResult_Ok_ReturnsFalseForError()
    {
        var result = new CommandResult(EnumCommandStatus.Error, "Failed");
        Assert.False(result.Ok);
    }

    [Fact]
    public void CommandResult_Ok_ReturnsFalseForUnknownLegacy()
    {
        var result = new CommandResult(EnumCommandStatus.UnknownLegacy, null);
        Assert.False(result.Ok);
    }

    [Fact]
    public void CommandResult_Ok_ReturnsTrueForDeferred()
    {
        var result = new CommandResult(EnumCommandStatus.Deferred, null);
        Assert.True(result.Ok);
    }

    [Fact]
    public void CommandResult_Success_CreatesOkResult()
    {
        var result = CommandResult.Success("Test message", 42);
        Assert.True(result.Ok);
        Assert.Equal(EnumCommandStatus.Success, result.Status);
        Assert.Equal("Test message", result.StatusMessage);
        Assert.Equal(42, result.ReturnValue);
    }

    [Fact]
    public void CommandResult_Error_CreatesFailedResult()
    {
        var result = CommandResult.Error("Something went wrong");
        Assert.False(result.Ok);
        Assert.Equal(EnumCommandStatus.Error, result.Status);
        Assert.Equal("Something went wrong", result.StatusMessage);
    }

    [Fact]
    public void CommandResult_Deferred_CreatesDeferredResult()
    {
        var result = CommandResult.Deferred();
        Assert.True(result.Ok);
        Assert.Equal(EnumCommandStatus.Deferred, result.Status);
        Assert.Null(result.StatusMessage);
    }

    [Fact]
    public void CommandExecutionException_ContainsResult()
    {
        var result = CommandResult.Error("Test error");
        var ex = new CommandExecutionException("Command failed", result);

        Assert.Equal("Command failed", ex.Message);
        Assert.Same(result, ex.Result);
    }

    [Fact]
    public void CommandExecutionException_SupportsInnerException()
    {
        var result = CommandResult.Error("Test error");
        var inner = new InvalidOperationException("Inner");
        var ex = new CommandExecutionException("Command failed", result, inner);

        Assert.Same(inner, ex.InnerException);
        Assert.Same(result, ex.Result);
    }

    [Fact]
    public void ServerScenarioBase_HasHostProperty()
    {
        var prop = typeof(ServerScenarioBase).GetProperty(
            "Host",
            BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.NotNull(prop);
        Assert.Equal(typeof(EmbeddedServerHost), prop.PropertyType);
    }

    [Fact]
    public void ServerScenarioBase_HasServerProperty()
    {
        var prop = typeof(ServerScenarioBase).GetProperty(
            "Server",
            BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.NotNull(prop);
        Assert.Equal(typeof(ServerMain), prop.PropertyType);
    }

    [Fact]
    public void ServerScenarioBase_HasApiProperty()
    {
        var prop = typeof(ServerScenarioBase).GetProperty(
            "Api",
            BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.NotNull(prop);
        Assert.Equal(typeof(ICoreServerAPI), prop.PropertyType);
    }

    [Fact]
    public void ServerScenarioBase_DefaultWorldIsolation_IsRollback()
    {
        var scenario = new ConcreteServerScenario();
        Assert.Equal(WorldIsolation.Rollback, scenario.ExposedWorldIsolation);
    }

    [Fact]
    public void ServerScenarioBase_ExecuteCommand_ThrowsWhenNotInitialized()
    {
        var scenario = new ConcreteServerScenario();

        var ex = Assert.ThrowsAsync<InvalidOperationException>(
            () => scenario.ExecuteCommand("/test"));

        Assert.NotNull(ex);
    }

    [Fact]
    public void ServerScenarioBase_ExecuteSuccess_ThrowsWhenNotInitialized()
    {
        var scenario = new ConcreteServerScenario();

        var ex = Assert.ThrowsAsync<InvalidOperationException>(
            () => scenario.ExecuteSuccess("/test"));

        Assert.NotNull(ex);
    }

    [Fact]
    public void ServerScenarioBase_ExecuteCommandAsPlayer_ThrowsOnNullPlayer()
    {
        var scenario = new ConcreteServerScenario();

        var ex = Assert.ThrowsAsync<ArgumentNullException>(
            () => scenario.ExecuteCommandAsPlayer(null!, "/test"));

        Assert.NotNull(ex);
    }

    [Fact]
    public void ServerScenarioBase_HasExecuteCommandMethod()
    {
        var method = typeof(ServerScenarioBase).GetMethod(
            "ExecuteCommand",
            BindingFlags.Instance | BindingFlags.Public);

        Assert.NotNull(method);
        Assert.Equal(typeof(Task<CommandResult>), method.ReturnType);
    }

    [Fact]
    public void ServerScenarioBase_HasExecuteSuccessMethod()
    {
        var method = typeof(ServerScenarioBase).GetMethod(
            "ExecuteSuccess",
            BindingFlags.Instance | BindingFlags.Public);

        Assert.NotNull(method);
        Assert.Equal(typeof(Task), method.ReturnType);
    }

    [Fact]
    public void ServerScenarioBase_HasExecuteCommandAsPlayerMethod()
    {
        var method = typeof(ServerScenarioBase).GetMethod(
            "ExecuteCommandAsPlayer",
            BindingFlags.Instance | BindingFlags.Public);

        Assert.NotNull(method);
        Assert.Equal(typeof(Task<CommandResult>), method.ReturnType);

        var parameters = method.GetParameters();
        Assert.Equal(2, parameters.Length);
        Assert.Equal(typeof(IServerTestPlayer), parameters[0].ParameterType);
        Assert.Equal(typeof(string), parameters[1].ParameterType);
    }

    [Fact]
    public void ServerScenarioBase_HasExecuteSuccessAsPlayerMethod()
    {
        var method = typeof(ServerScenarioBase).GetMethod(
            "ExecuteSuccessAsPlayer",
            BindingFlags.Instance | BindingFlags.Public);

        Assert.NotNull(method);
        Assert.Equal(typeof(Task), method.ReturnType);

        var parameters = method.GetParameters();
        Assert.Equal(2, parameters.Length);
        Assert.Equal(typeof(IServerTestPlayer), parameters[0].ParameterType);
        Assert.Equal(typeof(string), parameters[1].ParameterType);
    }

    [Fact]
    public void IServerTestPlayer_HasRequiredProperties()
    {
        var type = typeof(IServerTestPlayer);

        Assert.NotNull(type.GetProperty("Player"));
        Assert.NotNull(type.GetProperty("Entity"));
        Assert.NotNull(type.GetProperty("PlayerUID"));
        Assert.NotNull(type.GetProperty("RoleCode"));
    }

    [Fact]
    public void IServerTestPlayer_HasRequiredMethods()
    {
        var type = typeof(IServerTestPlayer);

        Assert.NotNull(type.GetMethod("GrantPrivilege"));
        Assert.NotNull(type.GetMethod("RevokePrivilege"));
        Assert.NotNull(type.GetMethod("TeleportTo"));
        Assert.NotNull(type.GetMethod("GiveItem"));
        Assert.NotNull(type.GetMethod("HasItem"));
    }

    /// <summary>
    /// Concrete subclass of <see cref="ServerScenarioBase"/> for testing purposes.
    /// </summary>
    private sealed class ConcreteServerScenario : ServerScenarioBase
    {
        public WorldIsolation ExposedWorldIsolation => WorldIsolation;
    }
}
