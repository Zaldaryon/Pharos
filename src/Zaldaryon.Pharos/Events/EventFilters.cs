namespace Zaldaryon.Pharos.Events;

/// <summary>
/// Which server events a <see cref="ServerEventLog"/> records: one flag per event, and groups.
/// Only the chosen events are listened to.
/// </summary>
[Flags]
public enum ServerEvents
{
    /// <summary>Nothing.</summary>
    None = 0,

    /// <summary><see cref="Events.BlockPlaced"/>.</summary>
    BlockPlaced = 1 << 0,

    /// <summary><see cref="Events.BlockBroken"/>.</summary>
    BlockBroken = 1 << 1,

    /// <summary><see cref="Events.BlockUsed"/>.</summary>
    BlockUsed = 1 << 2,

    /// <summary><see cref="Events.PlayerDied"/>.</summary>
    PlayerDied = 1 << 3,

    /// <summary><see cref="Events.PlayerRespawned"/>.</summary>
    PlayerRespawned = 1 << 4,

    /// <summary><see cref="Events.GameModeChanged"/>.</summary>
    GameModeChanged = 1 << 5,

    /// <summary><see cref="Events.ActiveSlotChanged"/>.</summary>
    ActiveSlotChanged = 1 << 6,

    /// <summary><see cref="Events.EntityInteracted"/>.</summary>
    EntityInteracted = 1 << 7,

    /// <summary><see cref="Events.EntitySpawned"/>.</summary>
    EntitySpawned = 1 << 8,

    /// <summary><see cref="Events.EntityDied"/>.</summary>
    EntityDied = 1 << 9,

    /// <summary><see cref="Events.EntityDespawned"/>.</summary>
    EntityDespawned = 1 << 10,

    /// <summary><see cref="Events.EntityMounted"/>.</summary>
    EntityMounted = 1 << 11,

    /// <summary><see cref="Events.EntityUnmounted"/>.</summary>
    EntityUnmounted = 1 << 12,

    /// <summary><see cref="Events.EntitySpawnAttempted"/>.</summary>
    EntitySpawnAttempted = 1 << 13,

    /// <summary><see cref="Events.ChunkColumnLoaded"/>.</summary>
    ChunkColumnLoaded = 1 << 14,

    /// <summary><see cref="Events.ChunkColumnUnloaded"/>.</summary>
    ChunkColumnUnloaded = 1 << 15,

    /// <summary><see cref="Events.MapRegionLoaded"/>.</summary>
    MapRegionLoaded = 1 << 16,

    /// <summary><see cref="Events.WorldSaved"/>.</summary>
    WorldSaved = 1 << 17,

    /// <summary>Blocks placed, broken and used.</summary>
    Blocks = BlockPlaced | BlockBroken | BlockUsed,

    /// <summary>Players dying, respawning, changing game mode or hotbar slot, and interacting with entities.</summary>
    Players = PlayerDied | PlayerRespawned | GameModeChanged | ActiveSlotChanged | EntityInteracted,

    /// <summary>Entities spawning, dying, despawning, mounting and unmounting, and spawn attempts.</summary>
    Entities = EntitySpawned | EntityDied | EntityDespawned | EntityMounted | EntityUnmounted | EntitySpawnAttempted,

    /// <summary>Chunk columns loaded and unloaded, and map regions loaded.</summary>
    Chunks = ChunkColumnLoaded | ChunkColumnUnloaded | MapRegionLoaded,

    /// <summary>Every event above.</summary>
    All = Blocks | Players | Entities | Chunks | WorldSaved,
}

/// <summary>
/// Which client events a <see cref="ClientEventLog"/> records: one flag per event, and groups.
/// Only the chosen events are listened to.
/// </summary>
[Flags]
public enum ClientEvents
{
    /// <summary>Nothing.</summary>
    None = 0,

    /// <summary><see cref="Events.EntitySpawned"/>.</summary>
    EntitySpawned = 1 << 0,

    /// <summary><see cref="Events.EntityDied"/>.</summary>
    EntityDied = 1 << 1,

    /// <summary><see cref="Events.EntityDespawned"/>.</summary>
    EntityDespawned = 1 << 2,

    /// <summary><see cref="Events.EntityMounted"/>.</summary>
    EntityMounted = 1 << 3,

    /// <summary><see cref="Events.EntityUnmounted"/>.</summary>
    EntityUnmounted = 1 << 4,

    /// <summary><see cref="Events.MapRegionLoaded"/>.</summary>
    MapRegionLoaded = 1 << 5,

    /// <summary>Entities spawning, dying, despawning, mounting and unmounting.</summary>
    Entities = EntitySpawned | EntityDied | EntityDespawned | EntityMounted | EntityUnmounted,

    /// <summary>Every event above.</summary>
    All = Entities | MapRegionLoaded,
}
