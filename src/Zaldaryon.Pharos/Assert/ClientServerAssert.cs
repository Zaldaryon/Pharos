using System.IO;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Zaldaryon.Pharos.Player;
using Zaldaryon.Pharos.Server;

namespace Zaldaryon.Pharos.Assertions;

/// <summary>
/// Static assertion methods for verifying client-server synchronization in loopback sessions.
/// </summary>
/// <remarks>
/// <para>
/// These assertions validate that the client and server agree on game state after
/// coordinated stepping. They are designed for use in integration tests that verify
/// network synchronization, prediction reconciliation, and state replication.
/// </para>
/// </remarks>
public static class ClientServerAssert
{
    /// <summary>Asserts that a loaded entity has the same code, life state, and position on both sides.</summary>
    /// <param name="session">The connected client-server session.</param>
    /// <param name="entityId">The entity's shared ID.</param>
    /// <param name="tolerance">Maximum distance between the two positions.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="tolerance"/> is negative or not finite.</exception>
    /// <exception cref="InvalidOperationException">The session or either world is unavailable.</exception>
    /// <exception cref="PharosAssertException">The entity is missing on a side or its state differs.</exception>
    public static void EntitySynced(ClientServerLoopbackSession session, long entityId, double tolerance = 0.1)
    {
        ArgumentNullException.ThrowIfNull(session);
        if (!double.IsFinite(tolerance) || tolerance < 0)
            throw new ArgumentOutOfRangeException(nameof(tolerance), tolerance, "Tolerance must be finite and non-negative.");
        EnsureConnected(session);

        EmbeddedServerHost host = session.NativeServer ?? throw new InvalidOperationException("The session has no embedded server.");
        EntitySyncState? server = host.RunOnGameThread<EntitySyncState?>(() =>
        {
            Entity? entity = host.Server.GetEntityById(entityId);
            return entity is null ? null : EntitySyncState.From(entity);
        });
        EntitySyncState? client = session.Client.RunOnClientThread<EntitySyncState?>(() =>
        {
            var world = session.Client.Client.World ?? throw new InvalidOperationException("Client world is not loaded.");
            Entity? entity = world.GetEntityById(entityId);
            return entity is null ? null : EntitySyncState.From(entity);
        });

        if (server is null || client is null)
        {
            throw new PharosAssertException($"Entity {entityId} is {(server is null && client is null ? "missing on both sides" : server is null ? "missing on the server" : "missing on the client")}.");
        }
        EntitySyncState serverState = server.Value;
        EntitySyncState clientState = client.Value;
        if (!string.Equals(serverState.Code, clientState.Code, StringComparison.Ordinal))
        {
            throw new PharosAssertException($"Entity {entityId} has code '{serverState.Code}' on the server and '{clientState.Code}' on the client.");
        }
        if (serverState.Alive != clientState.Alive)
        {
            throw new PharosAssertException($"Entity {entityId} is {(serverState.Alive ? "alive" : "dead")} on the server and {(clientState.Alive ? "alive" : "dead")} on the client.");
        }
        if (serverState.Dimension != clientState.Dimension)
        {
            throw new PharosAssertException($"Entity {entityId} is in dimension {serverState.Dimension} on the server and dimension {clientState.Dimension} on the client.");
        }

        double distance = serverState.Position.DistanceTo(clientState.Position);
        if (distance > tolerance)
        {
            throw new PharosAssertException(
                $"Entity {entityId} is at {serverState.Position} on the server and {clientState.Position} on the client. " +
                $"Distance {distance:F4} exceeds tolerance {tolerance:F4}.");
        }
    }

    /// <summary>Asserts that one watched attribute has the same serialized value on both sides.</summary>
    /// <param name="session">The connected client-server session.</param>
    /// <param name="entityId">The entity's shared ID.</param>
    /// <param name="path">The attribute key, or a dot-separated path into nested watched attributes.</param>
    /// <exception cref="ArgumentException"><paramref name="path"/> is empty or contains an empty segment.</exception>
    /// <exception cref="InvalidOperationException">The session or either world is unavailable.</exception>
    /// <exception cref="PharosAssertException">The entity or attribute is missing on a side, or its value differs.</exception>
    public static void WatchedAttributeSynced(ClientServerLoopbackSession session, long entityId, string path)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        string[] segments = path.Split('.');
        if (segments.Any(string.IsNullOrWhiteSpace)) throw new ArgumentException("Attribute path cannot contain empty segments.", nameof(path));
        EnsureConnected(session);

        EmbeddedServerHost host = session.NativeServer ?? throw new InvalidOperationException("The session has no embedded server.");
        byte[]? server = host.RunOnGameThread(() =>
        {
            Entity? entity = host.Server.GetEntityById(entityId);
            return entity is null ? null : ReadWatchedAttribute(entity, segments);
        });
        byte[]? client = session.Client.RunOnClientThread(() =>
        {
            var world = session.Client.Client.World ?? throw new InvalidOperationException("Client world is not loaded.");
            Entity? entity = world.GetEntityById(entityId);
            return entity is null ? null : ReadWatchedAttribute(entity, segments);
        });

        if (server is null || client is null)
        {
            throw new PharosAssertException($"Watched attribute '{path}' for entity {entityId} is {(server is null && client is null ? "missing on both sides" : server is null ? "missing on the server" : "missing on the client")}.");
        }
        if (!server.AsSpan().SequenceEqual(client))
        {
            throw new PharosAssertException($"Watched attribute '{path}' for entity {entityId} differs between client and server.");
        }
    }

    /// <summary>
    /// Asserts that the block at the specified position is synchronized between client and server.
    /// </summary>
    /// <param name="session">The loopback session to verify.</param>
    /// <param name="pos">The block position to check.</param>
    /// <exception cref="ArgumentNullException"><paramref name="session"/> or <paramref name="pos"/> is null.</exception>
    /// <exception cref="InvalidOperationException">The session is not connected.</exception>
    /// <exception cref="PharosAssertException">The block IDs do not match between client and server.</exception>
    public static void ClientServerBlockSynced(ClientServerLoopbackSession session, BlockPos pos)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(pos);

        if (!session.IsConnected)
        {
            throw new InvalidOperationException("Session is not connected. Wait for player to join before asserting synchronization.");
        }

        int clientBlockId = GetClientBlockId(session, pos);
        int serverBlockId = GetServerBlockId(session, pos);

        if (clientBlockId != serverBlockId)
        {
            throw new PharosAssertException(
                $"Block at {pos} is not synchronized. Client has block ID {clientBlockId}, server has block ID {serverBlockId}.");
        }
    }

    /// <summary>
    /// Asserts that the player position is synchronized between client and server within the specified tolerance.
    /// </summary>
    /// <param name="session">The loopback session to verify.</param>
    /// <param name="maxDistance">Maximum allowed distance between client and server positions. Default is 0.05 blocks.</param>
    /// <exception cref="ArgumentNullException"><paramref name="session"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="maxDistance"/> is negative or zero.</exception>
    /// <exception cref="InvalidOperationException">The session is not connected.</exception>
    /// <exception cref="PharosAssertException">The player positions differ by more than the allowed distance.</exception>
    public static void PlayerPositionSynced(ClientServerLoopbackSession session, double maxDistance = 0.05)
    {
        ArgumentNullException.ThrowIfNull(session);

        if (maxDistance <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxDistance), maxDistance, "Maximum distance must be positive.");
        }

        if (!session.IsConnected)
        {
            throw new InvalidOperationException("Session is not connected. Wait for player to join before asserting synchronization.");
        }

        Vec3d clientPos = GetClientPlayerPosition(session);
        Vec3d serverPos = GetServerPlayerPosition(session);

        double distance = clientPos.DistanceTo(serverPos);

        if (distance > maxDistance)
        {
            throw new PharosAssertException(
                $"Player position is not synchronized. Client at {clientPos}, server at {serverPos}. " +
                $"Distance {distance:F4} exceeds maximum allowed {maxDistance:F4}.");
        }
    }

    /// <summary>
    /// Asserts that the specified inventory is synchronized between client and server.
    /// </summary>
    /// <param name="session">The loopback session to verify.</param>
    /// <param name="inventoryId">The inventory ID to check (e.g., "hotbar-0", "backpack-0").</param>
    /// <exception cref="ArgumentNullException"><paramref name="session"/> or <paramref name="inventoryId"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="inventoryId"/> is empty or whitespace.</exception>
    /// <exception cref="InvalidOperationException">The session is not connected.</exception>
    /// <exception cref="PharosAssertException">The inventory contents do not match between client and server.</exception>
    public static void InventorySynced(ClientServerLoopbackSession session, string inventoryId)
    {
        ArgumentNullException.ThrowIfNull(session);

        if (string.IsNullOrWhiteSpace(inventoryId))
        {
            throw new ArgumentException("Inventory ID cannot be null or whitespace.", nameof(inventoryId));
        }

        if (!session.IsConnected)
        {
            throw new InvalidOperationException("Session is not connected. Wait for player to join before asserting synchronization.");
        }

        var clientSlots = GetClientInventorySlots(session, inventoryId);
        var serverSlots = GetServerInventorySlots(session, inventoryId);

        if (clientSlots == null && serverSlots == null)
        {
            return; // Both null means inventory doesn't exist on either side (valid sync)
        }

        if (clientSlots == null || serverSlots == null)
        {
            throw new PharosAssertException(
                $"Inventory '{inventoryId}' exists on {(clientSlots != null ? "client" : "server")} but not on {(clientSlots != null ? "server" : "client")}.");
        }

        if (clientSlots.Length != serverSlots.Length)
        {
            throw new PharosAssertException(
                $"Inventory '{inventoryId}' has {clientSlots.Length} slots on client but {serverSlots.Length} slots on server.");
        }

        for (int i = 0; i < clientSlots.Length; i++)
        {
            var clientSlot = clientSlots[i];
            var serverSlot = serverSlots[i];

            bool clientEmpty = clientSlot.ItemId == 0 || clientSlot.StackSize == 0;
            bool serverEmpty = serverSlot.ItemId == 0 || serverSlot.StackSize == 0;

            if (clientEmpty != serverEmpty)
            {
                throw new PharosAssertException(
                    $"Inventory '{inventoryId}' slot {i} mismatch: client is {(clientEmpty ? "empty" : "occupied")}, server is {(serverEmpty ? "empty" : "occupied")}.");
            }

            if (!clientEmpty)
            {
                if (clientSlot.ItemId != serverSlot.ItemId)
                {
                    throw new PharosAssertException(
                        $"Inventory '{inventoryId}' slot {i} item mismatch: client has item ID {clientSlot.ItemId}, server has item ID {serverSlot.ItemId}.");
                }

                if (clientSlot.StackSize != serverSlot.StackSize)
                {
                    throw new PharosAssertException(
                        $"Inventory '{inventoryId}' slot {i} stack size mismatch: client has {clientSlot.StackSize}, server has {serverSlot.StackSize}.");
                }
            }
        }
    }

    /// <summary>
    /// Asserts that the player's client-side predicted position has been reconciled with the server's authoritative position.
    /// </summary>
    /// <param name="session">The loopback session to verify.</param>
    /// <param name="maxDistance">Maximum allowed distance after reconciliation. Default is 0.01 blocks.</param>
    /// <exception cref="ArgumentNullException"><paramref name="session"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="maxDistance"/> is negative or zero.</exception>
    /// <exception cref="InvalidOperationException">The session is not connected.</exception>
    /// <exception cref="PharosAssertException">The reconciliation tolerance was exceeded.</exception>
    public static void PredictionReconciled(ClientServerLoopbackSession session, double maxDistance = 0.01)
    {
        ArgumentNullException.ThrowIfNull(session);

        if (maxDistance <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxDistance), maxDistance, "Maximum distance must be positive.");
        }

        if (!session.IsConnected)
        {
            throw new InvalidOperationException("Session is not connected. Wait for player to join before asserting reconciliation.");
        }

        // For prediction reconciliation, we verify that after server updates,
        // the client's position matches the server's authoritative state
        Vec3d clientPos = GetClientPlayerPosition(session);
        Vec3d serverPos = GetServerPlayerPosition(session);

        double distance = clientPos.DistanceTo(serverPos);

        if (distance > maxDistance)
        {
            throw new PharosAssertException(
                $"Client prediction not reconciled. Client at {clientPos}, server authoritative at {serverPos}. " +
                $"Distance {distance:F4} exceeds reconciliation tolerance {maxDistance:F4}.");
        }
    }

    /// <summary>
    /// Asserts that the survival state the client shows is the state the server holds.
    /// </summary>
    /// <remarks>
    /// Only health and wetness are compared by default: the survival mod works out oxygen, air and
    /// body temperature on each side for itself, from the calendar and the climate, so the two drift
    /// and a client can hold its own value for a while. Ask for them by name with
    /// <paramref name="values"/> when a test wants to see that. Step the session first: a server
    /// change reaches the client on the next frame.
    /// </remarks>
    /// <param name="session">The loopback session to verify.</param>
    /// <param name="playerName">
    /// The name of the playing client, as it joined. The client holds its own player's vitals and
    /// no other's, so this must be that player's name.
    /// </param>
    /// <param name="tolerance">How far a float may differ. Default is 0.5, which covers the precision the attribute packets carry.</param>
    /// <param name="values">Which values to compare. See <see cref="VitalsValues"/>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="session"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="playerName"/> is empty or whitespace.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="tolerance"/> is negative.</exception>
    /// <exception cref="InvalidOperationException">
    /// The session is not connected, the client has no player entity, the name is not the client's
    /// own player, or that player is not on the server.
    /// </exception>
    /// <exception cref="PharosAssertException">The two sides disagree on a value both carry.</exception>
    public static void VitalsSynced(ClientServerLoopbackSession session, string playerName, float tolerance = 0.5f, VitalsValues values = VitalsValues.SyncedByDefault)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentException.ThrowIfNullOrWhiteSpace(playerName);
        ArgumentOutOfRangeException.ThrowIfNegative(tolerance);

        if (!session.IsConnected)
        {
            throw new InvalidOperationException("Session is not connected. Wait for player to join before asserting synchronization.");
        }

        EmbeddedServerHost host = session.NativeServer ?? throw new InvalidOperationException("The session has no embedded server.");
        IServerPlayer? serverPlayer = host.Server.Clients.Values
            .Select(c => c.Player)
            .FirstOrDefault(p => string.Equals(p.PlayerName, playerName, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"No player named '{playerName}' is connected to the server.");

        string? clientName = session.Client.Client.player?.PlayerName;
        if (!string.Equals(clientName, playerName, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"The client holds its own player's vitals and no other's: it is '{clientName ?? "not joined"}', not '{playerName}'.");
        }

        if (serverPlayer.Entity is not { } entity || session.Client.Client.EntityPlayer is not { } clientEntity)
        {
            throw new InvalidOperationException($"'{playerName}' has no entity on one of the two sides.");
        }

        VitalsReading server = host.RunOnGameThread(() => VitalsBehaviors.Read(entity));
        VitalsReading client = session.Client.RunOnClientThread(() => VitalsBehaviors.Read(clientEntity));

        if (!server.Matches(client, tolerance, values, out string why, out IReadOnlyList<string> compared))
        {
            throw new PharosAssertException(
                $"'{playerName}' shows different vitals on the two sides: {why}. Server {server}, client {client}. Compared {string.Join(", ", compared)}.");
        }
    }

    #region Private Helpers

    private static void EnsureConnected(ClientServerLoopbackSession session)
    {
        if (!session.IsConnected)
        {
            throw new InvalidOperationException("Session is not connected. Wait for player to join before asserting synchronization.");
        }
    }

    private static byte[]? ReadWatchedAttribute(Entity entity, IReadOnlyList<string> path)
    {
        IAttribute current = entity.WatchedAttributes;
        foreach (string segment in path)
        {
            if (current is not ITreeAttribute tree || !tree.TryGetAttribute(segment, out current!)) return null;
        }

        using MemoryStream stream = new();
        using (BinaryWriter writer = new(stream, System.Text.Encoding.UTF8, leaveOpen: true))
        {
            writer.Write(current.GetAttributeId());
            current.ToBytes(writer);
            writer.Flush();
        }
        return stream.ToArray();
    }

    private static int GetClientBlockId(ClientServerLoopbackSession session, BlockPos pos)
    {
        var clientWorld = session.Client.Client.World;
        if (clientWorld == null)
        {
            throw new InvalidOperationException("Client world is not loaded.");
        }

        var block = clientWorld.BlockAccessor.GetBlock(pos);
        return block?.Id ?? 0;
    }

    private static int GetServerBlockId(ClientServerLoopbackSession session, BlockPos pos)
    {
        var serverApi = session.NativeServer?.Server?.Api;
        if (serverApi == null)
        {
            throw new InvalidOperationException("Server API is not available.");
        }

        var block = serverApi.World.BlockAccessor.GetBlock(pos);
        return block?.Id ?? 0;
    }

    private static Vec3d GetClientPlayerPosition(ClientServerLoopbackSession session)
    {
        var player = session.Client.Client.player?.Entity;
        if (player == null)
        {
            throw new InvalidOperationException("Client player entity is not available.");
        }

        return player.Pos.XYZ;
    }

    private static Vec3d GetServerPlayerPosition(ClientServerLoopbackSession session)
    {
        var serverApi = session.NativeServer?.Server?.Api;
        if (serverApi == null)
        {
            throw new InvalidOperationException("Server API is not available.");
        }

        // Find the player on the server by matching UID
        string clientUid = session.Client.Client.player?.PlayerUID ?? "";
        var serverPlayer = serverApi.World.AllOnlinePlayers
            .FirstOrDefault(p => p.PlayerUID == clientUid);

        if (serverPlayer?.Entity == null)
        {
            throw new InvalidOperationException($"Server player with UID '{clientUid}' not found.");
        }

        return serverPlayer.Entity.Pos.XYZ;
    }

    private static InventorySlotInfo[]? GetClientInventorySlots(ClientServerLoopbackSession session, string inventoryId)
    {
        var playerInventoryManager = session.Client.Client.player?.InventoryManager;
        if (playerInventoryManager == null)
        {
            return null;
        }

        var inventory = playerInventoryManager.GetOwnInventory(inventoryId);
        if (inventory == null)
        {
            return null;
        }

        var slots = new InventorySlotInfo[inventory.Count];
        for (int i = 0; i < inventory.Count; i++)
        {
            var slot = inventory[i];
            slots[i] = new InventorySlotInfo(
                slot?.Itemstack?.Item?.Id ?? slot?.Itemstack?.Block?.Id ?? 0,
                slot?.Itemstack?.StackSize ?? 0);
        }

        return slots;
    }

    private static InventorySlotInfo[]? GetServerInventorySlots(ClientServerLoopbackSession session, string inventoryId)
    {
        var serverApi = session.NativeServer?.Server?.Api;
        if (serverApi == null)
        {
            return null;
        }

        string clientUid = session.Client.Client.player?.PlayerUID ?? "";
        var serverPlayer = serverApi.World.AllOnlinePlayers
            .FirstOrDefault(p => p.PlayerUID == clientUid) as Vintagestory.API.Server.IServerPlayer;

        if (serverPlayer?.InventoryManager == null)
        {
            return null;
        }

        var inventory = serverPlayer.InventoryManager.GetOwnInventory(inventoryId);
        if (inventory == null)
        {
            return null;
        }

        var slots = new InventorySlotInfo[inventory.Count];
        for (int i = 0; i < inventory.Count; i++)
        {
            var slot = inventory[i];
            slots[i] = new InventorySlotInfo(
                slot?.Itemstack?.Item?.Id ?? slot?.Itemstack?.Block?.Id ?? 0,
                slot?.Itemstack?.StackSize ?? 0);
        }

        return slots;
    }

    #endregion

    /// <summary>
    /// Represents a snapshot of an inventory slot's contents.
    /// </summary>
    private readonly record struct InventorySlotInfo(int ItemId, int StackSize);

    private readonly record struct EntitySyncState(string? Code, bool Alive, int Dimension, Vec3d Position)
    {
        public static EntitySyncState From(Entity entity)
        {
            Vec3d position = entity.Pos.XYZ;
            return new EntitySyncState(entity.Code?.ToString(), entity.Alive, entity.Pos.Dimension, new Vec3d(position.X, position.Y, position.Z));
        }
    }
}
