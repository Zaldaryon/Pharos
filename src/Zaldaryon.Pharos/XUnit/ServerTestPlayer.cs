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
    private string? _joinedName;
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
        Vitals = new PlayerVitals(this, () => _host, () => _player?.Entity);
        Bot = new HeadlessPlayerBot(this);
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

    /// <summary>The connection the player's packets go through, or null for a player without one.</summary>
    internal HeadlessPlayerConnection? Connection => _connection;

    /// <inheritdoc />
    public EntityPlayer? Entity => _player?.Entity;

    /// <inheritdoc />
    public HeadlessPlayerBot Bot { get; }

    /// <inheritdoc />
    public IMountableSeat? MountedOn => Entity?.MountedOn;

    /// <summary>
    /// The player's survival state: health, hunger and the nutrients it feeds, oxygen and body
    /// temperature. Every value it reads and writes goes through the game's own survival
    /// behaviours, so what the game derives from them is the game's own.
    /// </summary>
    public PlayerVitals Vitals { get; }

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
    public long ReceivedPacketCount => Received.Total;

    /// <summary>
    /// Everything the server has sent this player so far, decoded: chat, entities it tracks for
    /// the player, particles, sounds, mod channel packets, player data, highlights and more.
    /// </summary>
    public ReceivedPackets Received { get; } = new();

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
    public Task<bool> MountAsync(Entity mountable)
    {
        ArgumentNullException.ThrowIfNull(mountable);
        return Task.FromResult(OnGameThread(() =>
        {
            EntityPlayer entity = Entity
                ?? throw new InvalidOperationException($"'{PlayerName ?? _playerUid}' has no entity: it has not joined.");
            IMountable provider = mountable.GetInterface<IMountable>()
                ?? throw new ArgumentException($"Entity {mountable.EntityId} does not provide mountable seats.", nameof(mountable));
            IMountableSeat? seat = provider.Seats.FirstOrDefault(candidate => candidate.CanMount(entity));
            return seat is not null && entity.TryMount(seat);
        }));
    }

    /// <inheritdoc />
    public Task<bool> MountAsync(BlockPos seatBlockPos)
    {
        ArgumentNullException.ThrowIfNull(seatBlockPos);
        return Task.FromResult(OnGameThread(() =>
        {
            EntityPlayer entity = Entity
                ?? throw new InvalidOperationException($"'{PlayerName ?? _playerUid}' has no entity: it has not joined.");
            IWorldAccessor world = Api?.World
                ?? throw new InvalidOperationException("The player is not attached to a server world.");
            BlockEntity? blockEntity = world.BlockAccessor.GetBlockEntity(seatBlockPos);
            IMountableSeat? seat = blockEntity as IMountableSeat;
            if (seat is null && blockEntity is IMountable provider)
            {
                seat = provider.Seats.FirstOrDefault(candidate => candidate.CanMount(entity));
            }

            return seat is not null && entity.TryMount(seat);
        }));
    }

    /// <inheritdoc />
    public Task<bool> UnmountAsync()
    {
        return Task.FromResult(OnGameThread(() =>
        {
            EntityPlayer entity = Entity
                ?? throw new InvalidOperationException($"'{PlayerName ?? _playerUid}' has no entity: it has not joined.");
            return entity.TryUnmount();
        }));
    }

    /// <inheritdoc />
    public async Task<bool> SleepAsync(BlockPos bedPos, int maxTicks = 20, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(bedPos);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxTicks);

        IMountableSeat? bedSeat = null;
        bool accepted = OnGameThread(() =>
        {
            IServerPlayer player = _player
                ?? throw new InvalidOperationException("The player is not attached to a server.");
            ICoreServerAPI api = Api
                ?? throw new InvalidOperationException("The player is not attached to a server world.");
            if (Entity?.MountedOn is not null) return false;

            Block block = api.World.BlockAccessor.GetBlock(bedPos);
            BlockSelection selection = new(bedPos.Copy(), BlockFacing.UP, block)
            {
                HitPosition = new Vintagestory.API.MathTools.Vec3d(0.5, 0.5, 0.5),
            };

            // This is the same handler the server invokes for a client's bed-use packet. In
            // particular, BlockBed enforces claim access, tiredness and temporal-storm rules.
            bool handled = block.OnBlockInteractStart(api.World, player, selection);
            if (handled) bedSeat = Entity?.MountedOn;
            return handled && bedSeat is not null;
        });

        if (!accepted) return false;

        for (int tick = 0; tick < maxTicks; tick++)
        {
            ct.ThrowIfCancellationRequested();
            Tick(1);
            if (OnGameThread(() => ReferenceEquals(Entity?.MountedOn, bedSeat))) return true;
        }

        return false;
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

    /// <summary>
    /// The code of the item in a slot of one of the player's inventories on the server, such as the survival inventory's
    /// <c>craftinggrid</c>, or null when the slot is empty or the player has no such inventory.
    /// </summary>
    /// <param name="inventoryClass">The inventory's class name, such as <c>craftinggrid</c>.</param>
    /// <param name="slot">The slot index in that inventory.</param>
    public string? SlotItem(string inventoryClass, int slot)
    {
        ArgumentNullException.ThrowIfNull(inventoryClass);

        if (_player?.Entity is null || Api is null) return null;

        return OnGameThread(() =>
        {
            IInventory? inventory = _player.InventoryManager?.Inventories?.Values?.FirstOrDefault(inv => inv.ClassName == inventoryClass);
            if (inventory is null || slot < 0 || slot >= inventory.Count) return null;
            return inventory[slot]?.Itemstack?.Collectible?.Code?.ToString();
        });
    }

    /// <summary>The remaining durability of the first stack of an item the player holds on the server, or null when it holds none.</summary>
    /// <param name="itemCode">The item's code, such as <c>game:knife-generic-flint</c>.</param>
    public int? ItemDurability(string itemCode)
    {
        ArgumentNullException.ThrowIfNull(itemCode);

        CollectibleObject? collectible = Resolve(itemCode);
        if (_player?.Entity is null || Api is null || collectible is null) return null;

        return OnGameThread(() =>
        {
            foreach (IInventory inv in _player.InventoryManager?.Inventories?.Values?.ToArray() ?? [])
            {
                if (inv.ClassName == GlobalConstants.creativeInvClassName) continue;

                foreach (ItemSlot slot in inv)
                {
                    ItemStack? stack = slot?.Itemstack;
                    if (stack?.Collectible?.Id == collectible.Id)
                    {
                        return (int?)stack.Collectible.GetRemainingDurability(stack);
                    }
                }
            }

            return null;
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

    /// <summary>Sends a chat message to a player group this player is in, through the same client packet a real client uses.</summary>
    /// <param name="message">The message text.</param>
    /// <param name="group">The group to send to.</param>
    /// <param name="ticks">Server ticks to run after sending, so the server handles the message.</param>
    /// <exception cref="ArgumentNullException"><paramref name="message"/> or <paramref name="group"/> is null.</exception>
    /// <exception cref="InvalidOperationException">The player has not joined.</exception>
    public Task SayAsync(string message, PlayerGroup group, int ticks = 2)
    {
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(group);
        HeadlessPlayerConnection connection = _connection
            ?? throw new InvalidOperationException("Only a joined player can chat.");

        int groupId = group.Uid;
        OnGameThread(() => connection.Chat(message, groupId));
        _host!.Ticks(ticks);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Damages the player by <paramref name="amount"/> through <c>Entity.ReceiveDamage</c>, so
    /// armour, resistances and every behaviour and mod hook the game runs for a real hit runs too.
    /// Then lets the server tick, so a hit that killed the player has been dealt with.
    /// </summary>
    /// <remarks>
    /// The game refuses damage while the player is in its invulnerability frames, unless the
    /// <see cref="DamageInfo"/> asks to ignore them. Damage of
    /// <see cref="EnumDamageType.Heal"/> heals instead, and is the way to bring a player back up.
    /// </remarks>
    /// <param name="amount">How much damage, in health points.</param>
    /// <param name="damage">How it damages, what caused it, and how hard. See <see cref="DamageInfo"/>.</param>
    /// <param name="ticks">How many server ticks to run afterwards.</param>
    /// <exception cref="InvalidOperationException">The player is not joined, or has no entity.</exception>
    public Task DamageAsync(float amount, DamageInfo? damage = null, int ticks = 2)
    {
        EntityPlayer? entity = _player?.Entity
            ?? throw new InvalidOperationException($"'{PlayerName ?? _playerUid}' has no entity: it has not joined.");

        DamageSource source = (damage ?? DamageInfo.Blunt).ToDamageSource(entity);
        OnGameThread(() => entity.ReceiveDamage(source, amount));
        Tick(ticks);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Kills the player, as <c>EntityPlayer.Die</c> does when health runs out: the death reason is
    /// set, <c>OnEntityDeath</c> fires and a real client opens its death screen.
    /// </summary>
    /// <remarks>
    /// Health is not touched, so the player keeps whatever it had; a test that wants the death to
    /// come from damage uses <see cref="DamageAsync"/>. A living player dies with a full health bar
    /// only this way, which is what the <c>/kill</c> command does.
    /// </remarks>
    /// <param name="damage">What the death is credited to. See <see cref="DamageInfo"/>.</param>
    /// <param name="ticks">How many server ticks to run afterwards.</param>
    /// <exception cref="InvalidOperationException">The player is not joined, or has no entity.</exception>
    /// <exception cref="InvalidOperationException">The player is already dead.</exception>
    public Task KillAsync(DamageInfo? damage = null, int ticks = 2)
    {
        EntityPlayer entity = _player?.Entity
            ?? throw new InvalidOperationException($"'{PlayerName ?? _playerUid}' has no entity: it has not joined.");

        if (!entity.Alive) throw new InvalidOperationException($"'{PlayerName ?? _playerUid}' is already dead.");

        DamageInfo info = damage ?? new DamageInfo(EnumDamageType.Injury, EnumDamageSource.Suicide);
        OnGameThread(() => entity.Die(EnumDespawnReason.Death, info.ToDamageSource(entity)));
        Tick(ticks);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Asks the server to respawn the player, by sending the packet a real client sends when its
    /// player clicks Respawn on the death screen. The server triggers <c>PlayerRespawn</c> and puts
    /// the player back at its spawn point, which is the bed or gear it last set, or a random spot
    /// around its spawn.
    /// </summary>
    /// <remarks>
    /// A living player is left alone: the server ignores the packet, and so does this, rather than
    /// reporting a respawn that never happened. In a client-server scenario use
    /// <c>Player.RespawnAsync()</c> instead, which clicks the client's own button and so covers the
    /// path a player takes.
    /// </remarks>
    /// <param name="maxTicks">How many ticks to wait for the player to be alive again.</param>
    /// <param name="ct">Cancels the wait.</param>
    /// <exception cref="InvalidOperationException">The player is not joined, or has no connection of its own.</exception>
    /// <exception cref="InvalidOperationException">The player is not dead.</exception>
    /// <exception cref="TimeoutException">The player was still dead after <paramref name="maxTicks"/> ticks.</exception>
    public async Task RespawnAsync(int maxTicks = 200, CancellationToken ct = default)
    {
        HeadlessPlayerConnection connection = _connection
            ?? throw new InvalidOperationException("Only a joined player can respawn: a player bound to a server without a connection has no client to click Respawn.");
        EntityPlayer entity = _player?.Entity
            ?? throw new InvalidOperationException($"'{PlayerName ?? _playerUid}' has no entity: it has not joined.");

        if (entity.Alive) throw new InvalidOperationException($"'{PlayerName ?? _playerUid}' is not dead.");

        OnGameThread(connection.RequestRespawn);

        for (int tick = 0; tick < maxTicks; tick++)
        {
            ct.ThrowIfCancellationRequested();
            Tick(1);
            if (entity.Alive) return;
        }

        throw new TimeoutException($"'{PlayerName ?? _playerUid}' was still dead after {maxTicks} ticks.");
    }

    /// <summary>
    /// Binds the player to a live server player without a connection of its own.
    /// </summary>
    internal void Bind(IServerPlayer player, ServerMain server, EmbeddedServerHost? host = null)
    {
        _player = player;
        _server = server;
        _host = host;

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
        _joinedName = player.PlayerName;
    }

    internal EmbeddedServerHost RequireBotHost() => _host
        ?? throw new InvalidOperationException("A headless bot action requires a player joined to an embedded server.");

    internal HeadlessPlayerConnection RequireBotConnection() => _connection
        ?? throw new InvalidOperationException("A headless bot action requires the player's own client connection.");

    internal T RunBotOnGameThread<T>(Func<T> action) => OnGameThread(action);

    internal void RunBotOnGameThread(Action action) => OnGameThread(action);

    /// <summary>
    /// Moves the player's link on by <paramref name="dtMs"/>. Called on the game thread every tick, before its packets are read.
    /// </summary>
    internal void AdvanceNetwork(double dtMs) => _connection?.AdvanceLink(dtMs);

    /// <summary>
    /// Takes the packets the server queued for this player. Called on the game thread every tick.
    /// </summary>
    internal void ReceivePending()
    {
        if (_connection is null) return;

        foreach (byte[] packet in _connection.DrainReceived())
        {
            Received.Add(packet);
        }
    }

    /// <summary>
    /// The player's network: how its link is degraded, scripted, severed and frozen. The player must be joined.
    /// </summary>
    /// <exception cref="InvalidOperationException">The player is not joined to an embedded server.</exception>
    public PlayerNetwork Network => new(RequireBotConnection(), this);

    /// <summary>
    /// Freezes the player's connection: it stops reading and answering the server's pings. The server then times the player out once the
    /// game's ping timeout has passed in simulated time, as it would for a client that stopped responding.
    /// </summary>
    /// <exception cref="InvalidOperationException">The player is not joined to an embedded server.</exception>
    public void Freeze() => FreezeNetwork();

    internal void FreezeNetwork()
    {
        HeadlessPlayerConnection connection = RequireBotConnection();
        EmbeddedServerHost host = RequireBotHost();
        ServerMain server = _server ?? throw new InvalidOperationException("The player is not connected to a server.");
        string name = _joinedName ?? _playerUid;
        host.RunOnGameThread(() => connection.Freeze(server, name));
    }

    /// <summary>
    /// Cuts the player's connection without the leave packet a quitting client sends. The server reads a lost connection on its next
    /// pass and drops the player, so <see cref="IsConnected"/> turns false.
    /// </summary>
    /// <exception cref="InvalidOperationException">The player is not joined to an embedded server.</exception>
    public void Sever() => RequireBotConnection().Sever();

    /// <summary>
    /// Joins the same player back, under the same name and UID, after it was severed or disconnected. The player keeps this test
    /// object, and the server restores the data it saved when the player left.
    /// </summary>
    /// <param name="maxTicks">How many server ticks each join step may take.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <exception cref="InvalidOperationException">The player is still connected, or was never joined to an embedded server.</exception>
    /// <exception cref="TimeoutException">A join step did not complete in time.</exception>
    public Task RejoinAsync(int maxTicks = 2000, CancellationToken ct = default)
    {
        if (IsConnected) throw new InvalidOperationException($"'{_joinedName ?? _playerUid}' is still connected: sever or disconnect it first.");
        EmbeddedServerHost host = _host ?? throw new InvalidOperationException("The player was never joined to an embedded server.");
        string name = _joinedName ?? throw new InvalidOperationException("The player has no name to rejoin under.");
        return host.RejoinPlayerAsync(name, this, maxTicks, ct);
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

    /// <summary>
    /// Runs the server forward, so an effect the game applies on its next tick has been applied.
    /// A player bound to a server it does not own has no clock to move.
    /// </summary>
    private void Tick(int count)
    {
        if (count <= 0 || _host is null) return;
        _host.Ticks(count);
    }
}
