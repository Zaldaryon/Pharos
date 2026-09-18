using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.Server;

namespace Zaldaryon.Pharos.XUnit;

/// <summary>
/// A test player implementation that wraps a server-side player for scenario testing.
/// </summary>
/// <remarks>
/// <para>
/// This class provides controlled access to player state, privileges, and inventory
/// for server scenario tests. It backs onto a real or simulated <see cref="IServerPlayer"/>
/// when connected to a running server.
/// </para>
/// <para>
/// When the server is not running or the player is disconnected, property accessors
/// return null or default values, and methods operate as no-ops or throw descriptive
/// exceptions, allowing tests to safely verify behavior without a live server.
/// </para>
/// <para>
/// Cleanup is handled by <see cref="Disconnect"/>, which removes the player from
/// the server's player data and uid registries.
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

    /// <inheritdoc/>
    public IServerPlayer? Player => _player;

    /// <inheritdoc/>
    public EntityPlayer? Entity => _player?.Entity;

    /// <inheritdoc/>
    public string PlayerUID => _playerUid;

    /// <inheritdoc/>
    public string RoleCode
    {
        get => _player?.Role?.Code ?? _roleCode;
        set
        {
            _roleCode = value ?? "suplayer";

            if (_player is ServerPlayer serverPlayer && _server is not null)
            {
                IPlayerRole? role = _server.Config?.Roles?.Find(r =>
                    string.Equals(r.Code, value, StringComparison.OrdinalIgnoreCase));

                if (role is not null)
                {
                    serverPlayer.Role = role;
                }
            }
        }
    }

    /// <summary>
    /// Gets the server instance this player is associated with.
    /// </summary>
    public ServerMain? Server => _server;

    /// <summary>
    /// Gets whether this player is currently connected to a server.
    /// </summary>
    public bool IsConnected => _player is not null && _server is not null;

    /// <summary>
    /// Gets the set of privileges explicitly granted to this test player.
    /// </summary>
    public IReadOnlySet<string> GrantedPrivileges => _grantedPrivileges;

    /// <summary>
    /// Gets the set of privileges explicitly revoked from this test player.
    /// </summary>
    public IReadOnlySet<string> RevokedPrivileges => _revokedPrivileges;

    /// <inheritdoc/>
    public void GrantPrivilege(params string[] privileges)
    {
        ArgumentNullException.ThrowIfNull(privileges);

        foreach (string privilege in privileges)
        {
            if (string.IsNullOrWhiteSpace(privilege)) continue;

            _grantedPrivileges.Add(privilege);
            _revokedPrivileges.Remove(privilege);
        }
    }

    /// <inheritdoc/>
    public void RevokePrivilege(params string[] privileges)
    {
        ArgumentNullException.ThrowIfNull(privileges);

        foreach (string privilege in privileges)
        {
            if (string.IsNullOrWhiteSpace(privilege)) continue;

            _revokedPrivileges.Add(privilege);
            _grantedPrivileges.Remove(privilege);
        }
    }

    /// <inheritdoc/>
    public Task TeleportTo(double x, double y, double z)
    {
        if (_player?.Entity is null)
        {
            return Task.CompletedTask;
        }

        // Server-side teleport uses TeleportToDouble
        _player.Entity.TeleportToDouble(x, y, z);
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public void GiveItem(string itemCode, int quantity = 1)
    {
        ArgumentNullException.ThrowIfNull(itemCode);

        if (quantity <= 0) return;

        if (_player?.Entity is null || _server?.Api is null)
        {
            return;
        }

        ICoreServerAPI api = (ICoreServerAPI)_server.Api;

        Item? item = api.World?.GetItem(new AssetLocation(itemCode));
        Block? block = item is null ? api.World?.GetBlock(new AssetLocation(itemCode)) : null;

        if (item is null && block is null)
        {
            return;
        }

        ItemStack stack = item is not null
            ? new ItemStack(item, quantity)
            : new ItemStack(block!, quantity);

        _player.Entity.TryGiveItemStack(stack);
    }

    /// <inheritdoc/>
    public bool HasItem(string itemCode, int quantity = 1)
    {
        ArgumentNullException.ThrowIfNull(itemCode);

        if (quantity <= 0) return true;

        if (_player?.Entity is null || _server?.Api is null)
        {
            return false;
        }

        ICoreServerAPI api = (ICoreServerAPI)_server.Api;

        Item? item = api.World?.GetItem(new AssetLocation(itemCode));
        Block? block = item is null ? api.World?.GetBlock(new AssetLocation(itemCode)) : null;

        if (item is null && block is null)
        {
            return false;
        }

        IInventory[]? inventories = _player.InventoryManager?.Inventories?.Values?.ToArray();
        if (inventories is null) return false;

        int totalFound = 0;
        foreach (IInventory inv in inventories)
        {
            foreach (ItemSlot slot in inv)
            {
                if (slot?.Itemstack is null) continue;

                bool matches = item is not null
                    ? slot.Itemstack.Item?.Id == item.Id
                    : slot.Itemstack.Block?.Id == block!.Id;

                if (matches)
                {
                    totalFound += slot.Itemstack.StackSize;
                    if (totalFound >= quantity) return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Checks whether this player has the specified privilege.
    /// </summary>
    /// <param name="privilege">The privilege to check.</param>
    /// <returns>True if the player has the privilege.</returns>
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
    /// Associates this test player with the specified server player instance.
    /// </summary>
    /// <param name="player">The server player to bind to.</param>
    /// <param name="server">The server instance.</param>
    internal void Bind(IServerPlayer player, ServerMain server)
    {
        _player = player;
        _server = server;

        RoleCode = _roleCode;
    }

    /// <summary>
    /// Disconnects this test player from the server, purging player data.
    /// </summary>
    /// <remarks>
    /// This method removes the player from <c>PlayerDataManager</c> and <c>PlayersByUid</c>
    /// to ensure clean state between tests.
    /// </remarks>
    public void Disconnect()
    {
        if (_server is null || _player is null) return;

        try
        {
            // Remove from PlayersByUid dictionary
            if (_server.PlayersByUid?.ContainsKey(_playerUid) == true)
            {
                _server.PlayersByUid.Remove(_playerUid);
            }

            // Remove from connected clients if present
            if (_player is ServerPlayer serverPlayer && serverPlayer.ClientId > 0)
            {
                _server.Clients?.TryRemove(serverPlayer.ClientId, out _);
            }

            // Remove player data from PlayerDataManager
            if (_server.PlayerDataManager?.PlayerDataByUid?.ContainsKey(_playerUid) == true)
            {
                _server.PlayerDataManager.PlayerDataByUid.Remove(_playerUid);
            }
        }
        catch
        {
            // Best effort cleanup
        }
        finally
        {
            _player = null;
            _server = null;
        }
    }

    /// <summary>
    /// Releases resources and disconnects the player if connected.
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        Disconnect();
    }
}
