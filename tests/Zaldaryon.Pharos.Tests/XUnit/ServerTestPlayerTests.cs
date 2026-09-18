using System;
using System.Reflection;
using System.Threading.Tasks;
using Vintagestory.API.Common;
using Vintagestory.API.Server;
using Vintagestory.Server;
using Xunit;
using Zaldaryon.Pharos.XUnit;

namespace Zaldaryon.Pharos.Tests.XUnit;

/// <summary>
/// Tests for <see cref="ServerTestPlayer"/> and <see cref="IServerTestPlayer"/>.
/// </summary>
public class ServerTestPlayerTests
{
    [Fact]
    public void Constructor_WithValidUid_SetsPlayerUID()
    {
        const string uid = "test-player-123";
        using var player = new ServerTestPlayer(uid);

        Assert.Equal(uid, player.PlayerUID);
    }

    [Fact]
    public void Constructor_Parameterless_GeneratesUniqueUID()
    {
        using var player1 = new ServerTestPlayer();
        using var player2 = new ServerTestPlayer();

        Assert.NotNull(player1.PlayerUID);
        Assert.NotNull(player2.PlayerUID);
        Assert.NotEqual(player1.PlayerUID, player2.PlayerUID);
        Assert.StartsWith("test-", player1.PlayerUID);
    }

    [Fact]
    public void Constructor_WithNullUid_ThrowsArgumentNullException()
    {
        var ex = Assert.Throws<ArgumentNullException>(() => new ServerTestPlayer(null!));
        Assert.Equal("playerUid", ex.ParamName);
    }

    [Fact]
    public void Constructor_WithWhitespaceUid_ThrowsArgumentNullException()
    {
        var ex = Assert.Throws<ArgumentNullException>(() => new ServerTestPlayer("   "));
        Assert.Equal("playerUid", ex.ParamName);
    }

    [Fact]
    public void Player_WhenNotBound_ReturnsNull()
    {
        using var player = new ServerTestPlayer();
        Assert.Null(player.Player);
    }

    [Fact]
    public void Entity_WhenNotBound_ReturnsNull()
    {
        using var player = new ServerTestPlayer();
        Assert.Null(player.Entity);
    }

    [Fact]
    public void IsConnected_WhenNotBound_ReturnsFalse()
    {
        using var player = new ServerTestPlayer();
        Assert.False(player.IsConnected);
    }

    [Fact]
    public void RoleCode_DefaultValue_IsSuplayer()
    {
        using var player = new ServerTestPlayer();
        Assert.Equal("suplayer", player.RoleCode);
    }

    [Fact]
    public void RoleCode_CanBeSet_WhenNotBound()
    {
        using var player = new ServerTestPlayer();
        player.RoleCode = "admin";
        Assert.Equal("admin", player.RoleCode);
    }

    [Fact]
    public void RoleCode_SetToNull_DefaultsToSuplayer()
    {
        using var player = new ServerTestPlayer();
        player.RoleCode = null!;
        Assert.Equal("suplayer", player.RoleCode);
    }

    [Fact]
    public void GrantPrivilege_AddsToGrantedPrivileges()
    {
        using var player = new ServerTestPlayer();

        player.GrantPrivilege("build", "chat");

        Assert.Contains("build", player.GrantedPrivileges);
        Assert.Contains("chat", player.GrantedPrivileges);
    }

    [Fact]
    public void GrantPrivilege_RemovesFromRevokedPrivileges()
    {
        using var player = new ServerTestPlayer();

        player.RevokePrivilege("build");
        Assert.Contains("build", player.RevokedPrivileges);

        player.GrantPrivilege("build");
        Assert.DoesNotContain("build", player.RevokedPrivileges);
        Assert.Contains("build", player.GrantedPrivileges);
    }

    [Fact]
    public void RevokePrivilege_AddsToRevokedPrivileges()
    {
        using var player = new ServerTestPlayer();

        player.RevokePrivilege("controlserver", "kick");

        Assert.Contains("controlserver", player.RevokedPrivileges);
        Assert.Contains("kick", player.RevokedPrivileges);
    }

    [Fact]
    public void RevokePrivilege_RemovesFromGrantedPrivileges()
    {
        using var player = new ServerTestPlayer();

        player.GrantPrivilege("ban");
        Assert.Contains("ban", player.GrantedPrivileges);

        player.RevokePrivilege("ban");
        Assert.DoesNotContain("ban", player.GrantedPrivileges);
        Assert.Contains("ban", player.RevokedPrivileges);
    }

    [Fact]
    public void GrantPrivilege_IgnoresNullArray()
    {
        using var player = new ServerTestPlayer();

        var ex = Assert.Throws<ArgumentNullException>(() => player.GrantPrivilege(null!));
        Assert.Equal("privileges", ex.ParamName);
    }

    [Fact]
    public void RevokePrivilege_IgnoresNullArray()
    {
        using var player = new ServerTestPlayer();

        var ex = Assert.Throws<ArgumentNullException>(() => player.RevokePrivilege(null!));
        Assert.Equal("privileges", ex.ParamName);
    }

    [Fact]
    public void GrantPrivilege_IgnoresEmptyAndWhitespaceStrings()
    {
        using var player = new ServerTestPlayer();

        player.GrantPrivilege("", "   ", "valid");

        Assert.Single(player.GrantedPrivileges);
        Assert.Contains("valid", player.GrantedPrivileges);
    }

    [Fact]
    public void GrantedPrivileges_IsCaseInsensitive()
    {
        using var player = new ServerTestPlayer();

        player.GrantPrivilege("Build");
        Assert.Contains("build", player.GrantedPrivileges);
        Assert.Contains("BUILD", player.GrantedPrivileges);
    }

    [Fact]
    public async Task TeleportTo_WhenNotBound_CompletesSuccessfully()
    {
        using var player = new ServerTestPlayer();

        // Should not throw, just complete as no-op
        await player.TeleportTo(100, 64, 200);
    }

    [Fact]
    public void GiveItem_WhenNotBound_DoesNotThrow()
    {
        using var player = new ServerTestPlayer();

        // Should not throw, just no-op
        player.GiveItem("game:sword-iron", 1);
    }

    [Fact]
    public void GiveItem_WithNullItemCode_ThrowsArgumentNullException()
    {
        using var player = new ServerTestPlayer();

        var ex = Assert.Throws<ArgumentNullException>(() => player.GiveItem(null!));
        Assert.Equal("itemCode", ex.ParamName);
    }

    [Fact]
    public void GiveItem_WithZeroQuantity_DoesNothing()
    {
        using var player = new ServerTestPlayer();

        // Should not throw, just no-op for zero/negative quantity
        player.GiveItem("game:stone", 0);
        player.GiveItem("game:stone", -1);
    }

    [Fact]
    public void HasItem_WhenNotBound_ReturnsFalse()
    {
        using var player = new ServerTestPlayer();

        Assert.False(player.HasItem("game:sword-iron"));
    }

    [Fact]
    public void HasItem_WithNullItemCode_ThrowsArgumentNullException()
    {
        using var player = new ServerTestPlayer();

        var ex = Assert.Throws<ArgumentNullException>(() => player.HasItem(null!));
        Assert.Equal("itemCode", ex.ParamName);
    }

    [Fact]
    public void HasItem_WithZeroOrNegativeQuantity_ReturnsTrue()
    {
        using var player = new ServerTestPlayer();

        Assert.True(player.HasItem("game:stone", 0));
        Assert.True(player.HasItem("game:stone", -1));
    }

    [Fact]
    public void Disconnect_WhenNotBound_DoesNotThrow()
    {
        using var player = new ServerTestPlayer();

        // Should not throw when not connected
        player.Disconnect();
    }

    [Fact]
    public void Dispose_CanBeCalledMultipleTimes()
    {
        var player = new ServerTestPlayer();

        player.Dispose();
        player.Dispose();
        player.Dispose();

        // No exception thrown
    }

    [Fact]
    public void IServerTestPlayer_HasPlayerProperty()
    {
        var prop = typeof(IServerTestPlayer).GetProperty("Player");
        Assert.NotNull(prop);
        Assert.Equal(typeof(IServerPlayer), prop.PropertyType);
    }

    [Fact]
    public void IServerTestPlayer_HasEntityProperty()
    {
        var prop = typeof(IServerTestPlayer).GetProperty("Entity");
        Assert.NotNull(prop);
        Assert.Equal(typeof(EntityPlayer), prop.PropertyType);
    }

    [Fact]
    public void IServerTestPlayer_HasPlayerUIDProperty()
    {
        var prop = typeof(IServerTestPlayer).GetProperty("PlayerUID");
        Assert.NotNull(prop);
        Assert.Equal(typeof(string), prop.PropertyType);
    }

    [Fact]
    public void IServerTestPlayer_HasRoleCodeProperty()
    {
        var prop = typeof(IServerTestPlayer).GetProperty("RoleCode");
        Assert.NotNull(prop);
        Assert.True(prop.CanRead);
        Assert.True(prop.CanWrite);
    }

    [Fact]
    public void IServerTestPlayer_HasGrantPrivilegeMethod()
    {
        var method = typeof(IServerTestPlayer).GetMethod("GrantPrivilege");
        Assert.NotNull(method);

        var parameters = method.GetParameters();
        Assert.Single(parameters);
        Assert.True(parameters[0].GetCustomAttribute<ParamArrayAttribute>() != null);
    }

    [Fact]
    public void IServerTestPlayer_HasRevokePrivilegeMethod()
    {
        var method = typeof(IServerTestPlayer).GetMethod("RevokePrivilege");
        Assert.NotNull(method);

        var parameters = method.GetParameters();
        Assert.Single(parameters);
        Assert.True(parameters[0].GetCustomAttribute<ParamArrayAttribute>() != null);
    }

    [Fact]
    public void IServerTestPlayer_HasTeleportToMethod()
    {
        var method = typeof(IServerTestPlayer).GetMethod("TeleportTo");
        Assert.NotNull(method);
        Assert.Equal(typeof(Task), method.ReturnType);

        var parameters = method.GetParameters();
        Assert.Equal(3, parameters.Length);
        Assert.All(parameters, p => Assert.Equal(typeof(double), p.ParameterType));
    }

    [Fact]
    public void IServerTestPlayer_HasGiveItemMethod()
    {
        var method = typeof(IServerTestPlayer).GetMethod("GiveItem");
        Assert.NotNull(method);

        var parameters = method.GetParameters();
        Assert.Equal(2, parameters.Length);
        Assert.Equal(typeof(string), parameters[0].ParameterType);
        Assert.Equal(typeof(int), parameters[1].ParameterType);
    }

    [Fact]
    public void IServerTestPlayer_HasHasItemMethod()
    {
        var method = typeof(IServerTestPlayer).GetMethod("HasItem");
        Assert.NotNull(method);
        Assert.Equal(typeof(bool), method.ReturnType);

        var parameters = method.GetParameters();
        Assert.Equal(2, parameters.Length);
        Assert.Equal(typeof(string), parameters[0].ParameterType);
        Assert.Equal(typeof(int), parameters[1].ParameterType);
    }

    [Fact]
    public void ServerTestPlayer_ImplementsIServerTestPlayer()
    {
        Assert.True(typeof(IServerTestPlayer).IsAssignableFrom(typeof(ServerTestPlayer)));
    }

    [Fact]
    public void ServerTestPlayer_ImplementsIDisposable()
    {
        Assert.True(typeof(IDisposable).IsAssignableFrom(typeof(ServerTestPlayer)));
    }

    [Fact]
    public void ServerTestPlayer_HasBindMethod()
    {
        var method = typeof(ServerTestPlayer).GetMethod(
            "Bind",
            BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.NotNull(method);

        var parameters = method.GetParameters();
        Assert.Equal(2, parameters.Length);
        Assert.Equal(typeof(IServerPlayer), parameters[0].ParameterType);
        Assert.Equal(typeof(ServerMain), parameters[1].ParameterType);
    }

    [Fact]
    public void ServerTestPlayer_HasDisconnectMethod()
    {
        var method = typeof(ServerTestPlayer).GetMethod("Disconnect");
        Assert.NotNull(method);
        Assert.Equal(typeof(void), method.ReturnType);
    }

    [Fact]
    public void ServerTestPlayer_HasServerProperty()
    {
        var prop = typeof(ServerTestPlayer).GetProperty("Server");
        Assert.NotNull(prop);
        Assert.Equal(typeof(ServerMain), prop.PropertyType);
    }

    [Fact]
    public void ServerTestPlayer_HasIsConnectedProperty()
    {
        var prop = typeof(ServerTestPlayer).GetProperty("IsConnected");
        Assert.NotNull(prop);
        Assert.Equal(typeof(bool), prop.PropertyType);
    }

    [Fact]
    public void ServerTestPlayer_HasGrantedPrivilegesProperty()
    {
        var prop = typeof(ServerTestPlayer).GetProperty("GrantedPrivileges");
        Assert.NotNull(prop);
        Assert.True(prop.PropertyType.IsAssignableTo(typeof(System.Collections.Generic.IReadOnlySet<string>)));
    }

    [Fact]
    public void ServerTestPlayer_HasRevokedPrivilegesProperty()
    {
        var prop = typeof(ServerTestPlayer).GetProperty("RevokedPrivileges");
        Assert.NotNull(prop);
        Assert.True(prop.PropertyType.IsAssignableTo(typeof(System.Collections.Generic.IReadOnlySet<string>)));
    }
}
