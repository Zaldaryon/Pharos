using Vintagestory.API.MathTools;
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

    #region Private Helpers

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
}
