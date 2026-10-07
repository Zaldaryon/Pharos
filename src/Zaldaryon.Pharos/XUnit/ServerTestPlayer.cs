using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.Server;
using Zaldaryon.Pharos.Server;

namespace Zaldaryon.Pharos.XUnit;

/// <summary>
/// A test player on an embedded server.
/// </summary>
/// <remarks>
/// <para>
/// A player returned by <see cref="EmbeddedServerHost.JoinPlayerAsync"/> or
/// <see cref="ServerScenarioBase.CreateTestPlayerAsync"/> is a real multiplayer connection as far
/// as the server can tell: it went through the whole join sequence and is a playing player with a
/// spawned entity and inventories. There is no rendering client behind it. Everything it does goes
/// through the server's game thread, and chat and leaving go over its connection as packets.
/// </para>
/// <para>
/// A player created with a constructor and never joined keeps its privilege and role bookkeeping
/// locally, which unit tests rely on.
/// </para>
/// </remarks>
public sealed class ServerTestPlayer : IServerTestPlayer, IDisposable
{
    private readonly string _playerUid;
    private readonly HashSet<string> _grantedPrivileges = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _revokedPrivileges = new(StringComparer.OrdinalIgnoreCase);
    private string _roleCode = "suplayer";
    private IServerPlayer? _player;
    private ServerMain? _server;
    private EmbeddedServerHost? _host;
    private HeadlessPlayerConnection? _connection;
    private long _receivedPackets;
    private bool _disposed;

    /// <summary>
    /// Creates a new test player with the specified UID.
    /// </summary>
    /// <param name="playerUid">The unique identifier for this test player.</param>
    /// <exception cref="ArgumentNullException"><paramref name="playerUid"/> is null or whitespace.</exception>
    public ServerTestPlayer(string playerUid)
    {
        if (string.IsNullOrWhiteSpace(playerUid))
        {
            throw new ArgumentNullException(nameof(playerUid), "Player UID cannot be null or whitespace.");
        }

        _playerUid = playerUid;
    }

    /// <summary>
    /// Creates a new test player with a randomly generated UID.
    /// </summary>
    public ServerTestPlayer()
        : this("test-" + Guid.NewGuid().ToString("N")[..12])
    {
    }

    /// <inheritdoc />
    public IServerPlayer? Player => _player;

    /// <inheritdoc />
    public EntityPlayer? Entity => _player?.Entity;

    /// <inheritdoc />
    public string PlayerUID => _playerUid;

    /// <summary>The player's name, or null when not joined.</summary>
    public string? PlayerName => _player?.PlayerName;

    /// <inheritdoc />
    public string RoleCode
    {
        get => _player?.Role?.Code ?? _roleCode;
        set
        {
            _roleCode = value ?? "suplayer";

            if (_player is ServerPlayer serverPlayer && _server is not null)
            {
                OnGameThread(() =>
                {
                    IPlayerRole? role = _server.Config?.Roles?.Find(r =>
                        string.Equals(r.Code, _roleCode, StringComparison.OrdinalIgnoreCase));

                    if (role is not null)
                    {
                        ((ICoreServerAPI)_server.Api).Permissions.SetRole(serverPlayer, role);
                    }
                });
            }
        }
    }

    /// <summary>The server the player is on, or null when not joined.</summary>
    public ServerMain? Server => _server;

    /// <summary>
    /// Whether the player is joined and the server still has its connection.
    /// </summary>
    public bool IsConnected => _player is not null && _server is not null && (_connection is null || IsStillRegistered());

    /// <summary>How many packets the server has sent this player so far.</summary>
    public long ReceivedPacketCount => Interlocked.Read(ref _receivedPackets);

    /// <summary>Privileges granted through this player.</summary>
    public IReadOnlySet<string> GrantedPrivileges => _grantedPrivileges;

    /// <summary>Privileges revoked through this player.</summary>
    public IReadOnlySet<string> RevokedPrivileges => _revokedPrivileges;

    /// <inheritdoc />
    public void GrantPrivilege(params string[] privileges)
    {
        ArgumentNullException.ThrowIfNull(privileges);

        foreach (string privilege in privileges)
        {
            if (string.IsNullOrWhiteSpace(privilege)) continue;

            _grantedPrivileges.Add(privilege);
            _revokedPrivileges.Remove(privilege);

            if (_server is not null && _player is not null)
            {
                OnGameThread(() => Api!.Permissions.GrantPrivilege(_playerUid, privilege));
            }
        }
    }

    /// <inheritdoc />
    public void RevokePrivilege(params string[] privileges)
    {
        ArgumentNullException.ThrowIfNull(privileges);

        foreach (string privilege in privileges)
        {
            if (string.IsNullOrWhiteSpace(privilege)) continue;

            _revokedPrivileges.Add(privilege);
            _grantedPrivileges.Remove(privilege);

            if (_server is not null && _player is not null)
            {
                OnGameThread(() =>
                {
                    Api!.Permissions.RevokePrivilege(_playerUid, privilege);
                    Api.Permissions.DenyPrivilege(_playerUid, privilege);
                });
            }
        }
    }

    /// <inheritdoc />
    public Task TeleportTo(double x, double y, double z)
    {
        if (_player?.Entity is null)
        {
            return Task.CompletedTask;
        }

        OnGameThread(() => _player.Entity.TeleportToDouble(x, y, z));
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public void GiveItem(string itemCode, int quantity = 1)
    {
        ArgumentNullException.ThrowIfNull(itemCode);

        if (quantity <= 0) return;

        if (_player?.Entity is null || Api is null)
        {
            return;
        }

        CollectibleObject? collectible = Resolve(itemCode);
        if (collectible is null)
        {
            return;
        }

        OnGameThread(() => _player.Entity.TryGiveItemStack(new ItemStack(collectible, quantity)));
    }

    /// <inheritdoc />
    public bool HasItem(string itemCode, int quantity = 1)
    {
        ArgumentNullException.ThrowIfNull(itemCode);

        if (quantity <= 0) return true;

        if (_player?.Entity is null || Api is null)
        {
            return false;
        }

        CollectibleObject? collectible = Resolve(itemCode);
        if (collectible is null)
        {
            return false;
        }

        return OnGameThread(() =>
        {
            int totalFound = 0;
            foreach (IInventory inv in _player.InventoryManager?.Inventories?.Values?.ToArray() ?? [])
            {
                // The creative inventory is the item catalogue, not something the player holds,
                // and it is only populated on the client.
                if (inv.ClassName == GlobalConstants.creativeInvClassName) continue;

                foreach (ItemSlot slot in inv)
                {
                    if (slot?.Itemstack?.Collectible?.Id == collectible.Id && slot.Itemstack.Class == collectible.ItemClass)
                    {
                        totalFound += slot.Itemstack.StackSize;
                        if (totalFound >= quantity) return true;
                    }
                }
            }

            return false;
        });
    }

    /// <inheritdoc />
    public bool HasPrivilege(string privilege)
    {
        if (string.IsNullOrWhiteSpace(privilege)) return false;

        // Check tracked grants/revokes first
        if (_revokedPrivileges.Contains(privilege)) return false;
        if (_grantedPrivileges.Contains(privilege)) return true;

        // Check actual player privileges if connected
        return _player?.HasPrivilege(privilege) ?? false;
    }

    /// <summary>
    /// Sends a chat line from the player, as its chat box would, and lets the server handle it.
    /// A line starting with a slash runs as a command with this player as the caller.
    /// </summary>
    /// <exception cref="InvalidOperationException">The player is not joined.</exception>
    public Task SayAsync(string message, int ticks = 2)
    {
        ArgumentNullException.ThrowIfNull(message);
        HeadlessPlayerConnection connection = _connection
            ?? throw new InvalidOperationException("Only a joined player can chat.");

        OnGameThread(() => connection.Chat(message));
        _host!.Ticks(ticks);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Binds the player to a live server player without a connection of its own.
    /// </summary>
    internal void Bind(IServerPlayer player, ServerMain server)
    {
        _player = player;
        _server = server;

        RoleCode = _roleCode;
    }

    /// <summary>
    /// Binds the player to the server player its connection joined as.
    /// </summary>
    internal void Attach(IServerPlayer player, ServerMain server, EmbeddedServerHost host, HeadlessPlayerConnection connection)
    {
        _host = host;
        _connection = connection;
        _player = player;
        _server = server;
    }

    /// <summary>
    /// Takes the packets the server queued for this player. Called on the game thread every tick.
    /// </summary>
    internal void ReceivePending()
    {
        if (_connection is null) return;

        List<byte[]> packets = _connection.DrainReceived();
        Interlocked.Add(ref _receivedPackets, packets.Count);
    }

    /// <summary>
    /// Leaves the server: the player sends the leave packet a quitting client sends, the server
    /// disconnects it, and its socket is removed.
    /// </summary>
    public void Disconnect()
    {
        if (_server is null || _player is null) return;

        ServerMain server = _server;
        HeadlessPlayerConnection? connection = _connection;
        EmbeddedServerHost? host = _host;

        try
        {
            if (connection is not null && host is not null && host.IsRunning)
            {
                host.RunOnGameThread(connection.Leave);

                for (int i = 0; i < 200 && IsStillRegistered(); i++)
                {
                    host.Tick();
                }

                host.RunOnGameThread(() => connection.Close(server));
            }
        }
        catch
        {
            // Best effort: a stopping server drops the player anyway.
        }
        finally
        {
            _player = null;
            _server = null;
            _connection = null;
            _host = null;
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        Disconnect();
    }

    private ICoreServerAPI? Api => _server?.Api as ICoreServerAPI;

    private CollectibleObject? Resolve(string code)
    {
        AssetLocation location = new(code);
        return (CollectibleObject?)Api!.World.GetItem(location) ?? Api.World.GetBlock(location);
    }

    private bool IsStillRegistered()
    {
        ServerMain? server = _server;
        IServerPlayer? player = _player;
        if (server is null || player is null) return false;

        return server.Clients.TryGetValue(player.ClientId, out ConnectedClient? client)
            && ReferenceEquals(client.Player, player);
    }

    private void OnGameThread(Action action)
    {
        if (_host is null) action();
        else _host.RunOnGameThread(action);
    }

    private T OnGameThread<T>(Func<T> func) => _host is null ? func() : _host.RunOnGameThread(func);
}
