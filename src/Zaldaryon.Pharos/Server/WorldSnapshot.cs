using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.CompilerServices;
using Newtonsoft.Json;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.API.Util;
using Vintagestory.Common;
using Vintagestory.Common.Database;
using Vintagestory.Server;

namespace Zaldaryon.Pharos.Server;

/// <summary>
/// The state of a running server's world at one moment, restored in place without a restart.
/// </summary>
/// <remarks>
/// <para>
/// A snapshot holds every loaded chunk in the game's own save format: blocks, fluids, light,
/// decor, block entities, the entities stored with the chunk, and mod data. It also holds the
/// height maps of their map chunks, the mod data of the save game and of every loaded map region,
/// the world and server data of every known player, and, for the players connected when it was
/// taken, their position, game mode, inventories and entity state.
/// </para>
/// <para>
/// <see cref="Restore"/> runs on the server's game thread. It serializes each loaded chunk again
/// and leaves the ones that did not change alone. A changed chunk is swapped for its saved copy
/// the way the game swaps a chunk it unloads and loads again from disk: its entities are
/// despawned, the saved ones are loaded with their original ids, and its block entities are
/// initialized. Connected clients are sent the restored chunks again, so a joined client sees the
/// restored world without reconnecting.
/// </para>
/// <para>
/// Players that joined after the snapshot are forgotten, so a player joining under the same name
/// later starts fresh. Connected players are put back where they were, in the game mode and with
/// the inventories they had, with their entity's attribute trees and stats (health, satiety, body
/// temperature, damage over time, character class and traits), their spawn point, role,
/// privileges and mod data. A player that died is revived. Their clients are sent the restored
/// entity and player data.
/// </para>
/// <para>
/// The save game's mod data (<c>SaveGame.StoreData</c>) and each map region's mod data are put
/// back key by key; a key stored with null counts as absent. Mods that keep a copy of that data in
/// memory reload it themselves.
/// </para>
/// <para>
/// The calendar and the weather are restored too: the time, its speed and a freeze; each loaded
/// region's cloud pattern, wind, weather event and snow accumulation; the precipitation override
/// and whether the weather changes on its own. Clients are sent the restored calendar and weather.
/// </para>
/// <para>
/// Not restored: chunks first loaded after the snapshot and unloaded again, what mods keep in
/// memory, and tick listeners or event handlers a test registered.
/// </para>
/// </remarks>
public sealed record WorldSnapshot : IWorldSnapshot
{
    /// <inheritdoc/>
    public Guid SnapshotId { get; }

    /// <inheritdoc/>
    public DateTime CapturedAtUtc { get; }

    /// <inheritdoc/>
    public ServerWorldOptions Options { get; }

    /// <summary>
    /// Each loaded chunk in the game's save format, keyed by its 3D chunk index.
    /// </summary>
    public IReadOnlyDictionary<long, byte[]> ChunkData => _chunks;

    private readonly Dictionary<long, byte[]> _chunks;

    private readonly Dictionary<long, MapChunkState> _mapChunks;

    /// <summary>
    /// Chunks loaded after the snapshot, as they were when they loaded, until a restore finds
    /// out where they sit. See <see cref="TrackChunksLoadedLater"/>.
    /// </summary>
    private readonly List<(ServerChunk Chunk, byte[] Saved, MapChunkState? Map)> _loadedLater = [];

    /// <summary>Each map region's mod data, keyed by its region index.</summary>
    private readonly Dictionary<long, Dictionary<string, byte[]>> _mapRegions = [];

    /// <summary>
    /// Map regions loaded after the snapshot, with their mod data as it was when they loaded. The
    /// chunk thread loads regions, so this is locked.
    /// </summary>
    private readonly List<(long Index, Dictionary<string, byte[]> ModData)> _regionsLoadedLater = [];

    private IReadOnlyDictionary<string, byte[]> SaveGameModData { get; init; } = new Dictionary<string, byte[]>();

    private string[] RuntimePrivilegeCodes { get; init; } = [];

    private IReadOnlyDictionary<string, byte[]> WorldPlayerData { get; }

    private IReadOnlyDictionary<string, string> ServerPlayerData { get; }

    private IReadOnlyDictionary<string, OnlinePlayerState> OnlinePlayers { get; }

    private CalendarDriver.Saved? Calendar { get; init; }

    private WeatherDriver.Saved? Weather { get; init; }

    private WorldSnapshot(
        ServerWorldOptions options,
        IReadOnlyDictionary<long, byte[]> chunkData,
        Dictionary<long, MapChunkState>? mapChunks = null,
        IReadOnlyDictionary<string, byte[]>? worldPlayerData = null,
        IReadOnlyDictionary<string, string>? serverPlayerData = null,
        IReadOnlyDictionary<string, OnlinePlayerState>? onlinePlayers = null)
    {
        SnapshotId = Guid.NewGuid();
        CapturedAtUtc = DateTime.UtcNow;
        Options = options;
        _chunks = new Dictionary<long, byte[]>(chunkData);
        _mapChunks = mapChunks ?? [];
        WorldPlayerData = worldPlayerData ?? new Dictionary<string, byte[]>();
        ServerPlayerData = serverPlayerData ?? new Dictionary<string, string>();
        OnlinePlayers = onlinePlayers ?? new Dictionary<string, OnlinePlayerState>();
    }

    /// <summary>
    /// Captures the world of <paramref name="host"/>. Call it on the server's game thread, or use
    /// <see cref="EmbeddedServerHost.TakeSnapshot"/>.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="host"/> is null.</exception>
    /// <exception cref="InvalidOperationException">The server is not running.</exception>
    public static WorldSnapshot CreateFrom(EmbeddedServerHost host)
    {
        ArgumentNullException.ThrowIfNull(host);

        if (!host.IsRunning)
        {
            throw new InvalidOperationException("Cannot create snapshot from a stopped server.");
        }

        ServerMain server = host.Server;
        Dictionary<long, byte[]> chunks = [];
        Dictionary<long, MapChunkState> mapChunks = [];

        foreach (long index in server.LoadedChunkIndices)
        {
            ServerChunk? chunk = server.GetLoadedChunk(index);
            if (chunk == null) continue;

            chunks[index] = chunk.ToBytes();

            long mapIndex = server.WorldMap.ChunkIndex3dToIndex2d(index);
            if (!mapChunks.ContainsKey(mapIndex) && chunk.serverMapChunk is { } mapChunk)
            {
                ChunkPos pos = server.WorldMap.ChunkPosFromChunkIndex3D(index);
                mapChunks[mapIndex] = MapChunkState.From(pos.X, pos.Z, mapChunk);
            }
        }

        Dictionary<string, byte[]> worldPlayerData = [];
        foreach ((string uid, ServerWorldPlayerData data) in server.PlayerDataManager.WorldDataByUID)
        {
            data.BeforeSerialization();
            worldPlayerData[uid] = SerializerUtil.Serialize(data);
        }

        Dictionary<string, string> serverPlayerData = server.PlayerDataManager.PlayerDataByUid
            .ToDictionary(p => p.Key, p => JsonConvert.SerializeObject(p.Value));

        Dictionary<string, OnlinePlayerState> online = [];
        foreach (ConnectedClient client in server.Clients.Values)
        {
            if (client.Player is { Entity: not null } player)
            {
                online[player.PlayerUID] = OnlinePlayerState.From(player);
            }
        }

        ICoreServerAPI api = (ICoreServerAPI)server.Api;
        WorldSnapshot snapshot = new(host.Options, chunks, mapChunks, worldPlayerData, serverPlayerData, online)
        {
            Calendar = host.Calendar.Capture(),
            Weather = host.Weather.Capture(),
            SaveGameModData = StateRestore.Copy((api.WorldManager.SaveGame as SaveGame)?.ModData),
            RuntimePrivilegeCodes = [.. ServerConfigInternals.RuntimePrivilegeCodes(server.Config)],
        };

        foreach ((long index, IMapRegion region) in api.WorldManager.AllLoadedMapRegions)
        {
            snapshot._mapRegions[index] = StateRestore.Copy(region.ModData);
        }

        return snapshot;
    }

    /// <summary>
    /// Creates an empty snapshot with the specified options, for tests.
    /// </summary>
    internal static WorldSnapshot CreateEmpty(ServerWorldOptions? options = null) =>
        new(options ?? new ServerWorldOptions(), new Dictionary<long, byte[]>());

    /// <summary>
    /// Creates a snapshot holding only the given chunks, for tests.
    /// </summary>
    internal static WorldSnapshot CreateWithData(ServerWorldOptions options, IReadOnlyDictionary<long, byte[]> chunkData) =>
        new(options, chunkData);

    /// <inheritdoc/>
    public void Restore(EmbeddedServerHost host) => RestoreCore(host, WorldRestoreFilter.All);

    /// <summary>
    /// Where the connected player <paramref name="playerUid"/> stood when the snapshot was taken,
    /// or null when that player was not connected.
    /// </summary>
    internal Vec3d? PositionOf(string playerUid) =>
        OnlinePlayers.TryGetValue(playerUid, out OnlinePlayerState? state) ? new Vec3d(state.X, state.Y, state.Z) : null;

    /// <summary>
    /// Records every chunk the server loads from now on as it was when it loaded, so a restore
    /// puts those back too. Call it on the game thread; dispose the result to stop.
    /// </summary>
    /// <remarks>
    /// A chunk that unloads during a test is saved with its changes, and loads again with them.
    /// </remarks>
    internal IDisposable TrackChunksLoadedLater(EmbeddedServerHost host)
    {
        ICoreServerAPI api = (ICoreServerAPI)host.Server.Api;
        IServerEventAPI events = api.Event;
        MapRegionLoadedDelegate onRegionLoaded = (coord, region) =>
        {
            Dictionary<string, byte[]> modData;
            try
            {
                modData = StateRestore.Copy(region.ModData);
            }
            catch (InvalidOperationException)
            {
                // Written to while it was copied, on the chunk thread: left out.
                return;
            }

            lock (_regionsLoadedLater) _regionsLoadedLater.Add((api.WorldManager.MapRegionIndex2D(coord.X, coord.Y), modData));
        };

        ChunkColumnLoadedDelegate onLoaded = (coord, chunks) =>
        {
            MapChunkState? map = chunks.OfType<ServerChunk>().FirstOrDefault()?.serverMapChunk is { } mapChunk
                ? MapChunkState.From(coord.X, coord.Y, mapChunk)
                : null;

            foreach (IWorldChunk chunk in chunks)
            {
                if (chunk is ServerChunk serverChunk) _loadedLater.Add((serverChunk, serverChunk.ToBytes(), map));
            }
        };

        events.ChunkColumnLoaded += onLoaded;
        events.MapRegionLoaded += onRegionLoaded;
        return new Unsubscriber(() =>
        {
            events.ChunkColumnLoaded -= onLoaded;
            events.MapRegionLoaded -= onRegionLoaded;
        });
    }

    /// <summary>
    /// Moves the chunks recorded by <see cref="TrackChunksLoadedLater"/> that are still loaded
    /// into <see cref="ChunkData"/>, now that their index can be looked up.
    /// </summary>
    private void AdoptChunksLoadedLater(ServerMain server)
    {
        lock (_regionsLoadedLater)
        {
            foreach ((long index, Dictionary<string, byte[]> modData) in _regionsLoadedLater)
            {
                _mapRegions.TryAdd(index, modData);
            }

            _regionsLoadedLater.Clear();
        }

        if (_loadedLater.Count == 0) return;

        Dictionary<ServerChunk, long> indexOf = new(ReferenceEqualityComparer.Instance);
        foreach (long index in server.LoadedChunkIndices)
        {
            if (server.GetLoadedChunk(index) is { } chunk) indexOf[chunk] = index;
        }

        foreach ((ServerChunk chunk, byte[] saved, MapChunkState? map) in _loadedLater)
        {
            if (!indexOf.TryGetValue(chunk, out long index) || _chunks.ContainsKey(index)) continue;

            _chunks[index] = saved;
            long mapIndex = server.WorldMap.ChunkIndex3dToIndex2d(index);
            if (map != null) _mapChunks.TryAdd(mapIndex, map);
        }

        _loadedLater.Clear();
    }

    /// <summary>
    /// Restores the world, putting back the keys <paramref name="filter"/> allows, and returns what
    /// it put back. Runs on the calling thread, which must be the server's game thread.
    /// </summary>
    internal WorldRestoreResult RestoreCore(EmbeddedServerHost host, WorldRestoreFilter filter)
    {
        ArgumentNullException.ThrowIfNull(filter);
        ArgumentNullException.ThrowIfNull(host);

        if (!host.IsRunning)
        {
            throw new InvalidOperationException("Cannot restore snapshot to a stopped server.");
        }

        ServerMain server = host.Server;
        AdoptChunksLoadedLater(server);

        // The calendar first: restored weather and block entities read the time. Mod data before
        // the weather, which keeps state of its own in the save game and in each region.
        if (Calendar != null) host.Calendar.Restore(Calendar);
        int regions = RestoreMapRegions(server, filter);
        int modData = RestoreSaveGameModData(server, filter);
        if (Weather != null) host.Weather.Restore(Weather);
        RestorePlayerData(server);

        List<(long Index, ServerChunk Current, byte[] Saved)> changed = [];
        foreach ((long index, byte[] saved) in ChunkData)
        {
            ServerChunk? current = server.GetLoadedChunk(index);
            if (current == null || current.ToBytes().AsSpan().SequenceEqual(saved)) continue;

            changed.Add((index, current, saved));
        }

        if (changed.Count > 0)
        {
            // The lighting thread may hold tasks for the chunks about to be replaced. Hold it between passes while the
            // chunks are restored, then drop the tasks for those chunks, so none lights a chunk after the rollback.
            using (LightingThread.Pause(server))
            {
                foreach ((long index, ServerChunk current, byte[] saved) in changed) RestoreChunk(server, index, current, saved);
                LightingThread.DropQueued(server, changed.Select(c => c.Index).ToHashSet());

                // The map's one-chunk lookup cache may still hold a chunk object the restore just replaced.
                ForgetCachedChunk(server);
            }
        }

        int restored = changed.Count;

        foreach ((long mapIndex, MapChunkState state) in _mapChunks)
        {
            if (server.WorldMap.GetMapChunk(state.ChunkX, state.ChunkZ) is ServerMapChunk mapChunk && state.RestoreInto(mapChunk))
            {
                foreach (ConnectedClient client in server.Clients.Values)
                {
                    if (client.DidSendMapChunk(mapIndex)) client.forceSendMapChunks.Add(mapIndex);
                }
            }
        }

        int players = RestoreOnlinePlayers(server, filter);
        return new WorldRestoreResult(restored, players, modData, regions);
    }

    private int RestoreSaveGameModData(ServerMain server, WorldRestoreFilter filter)
    {
        if (((ICoreServerAPI)server.Api).WorldManager.SaveGame is not SaveGame save) return 0;

        save.ModData ??= new System.Collections.Concurrent.ConcurrentDictionary<string, byte[]>();
        return StateRestore.ModDataInPlace(save.ModData, SaveGameModData, filter.RestoresModData);
    }

    /// <summary>
    /// Puts back each loaded map region's mod data and sends a changed region to every client that
    /// has it. Returns how many regions changed.
    /// </summary>
    private int RestoreMapRegions(ServerMain server, WorldRestoreFilter filter)
    {
        IWorldManagerAPI world = ((ICoreServerAPI)server.Api).WorldManager;
        int changed = 0;
        foreach ((long index, Dictionary<string, byte[]> saved) in _mapRegions)
        {
            if (world.GetMapRegion(index) is not ServerMapRegion region) continue;

            region.ModData ??= [];
            if (StateRestore.ModDataInPlace(region.ModData, saved, filter.RestoresModData) == 0) continue;

            region.DirtyForSaving = true;
            changed++;

            // Sent at once: the chunk sender only goes through its queue while a client is still
            // being sent chunks.
            Vec3i pos = world.MapRegionPosFromIndex2D(index);
            foreach (ConnectedClient client in server.Clients.Values)
            {
                if (client.DidSendMapRegion(index)) server.SendPacket(client.Id, region.ToPacket(pos.X, pos.Z));
            }
        }

        return changed;
    }

    private static void RestoreChunk(ServerMain server, long index, ServerChunk current, byte[] saved)
    {
        // Players are not stored with the chunk; the restored copy must keep tracking them.
        List<Entity> kept = current.Entities?.Where(e => e is EntityPlayer).ToList() ?? [];

        // What ServerSystemUnloadChunks does to a chunk it unloads.
        current.RemoveEntitiesAndBlockEntities(server);

        ServerChunk restored = ServerChunk.FromBytes(saved, ServerInternals.ChunkDataPool(server), server);
        restored.serverMapChunk = current.serverMapChunk;

        ref FastRWLock chunksLock = ref ServerInternals.LoadedChunksLock(server);
        chunksLock.AcquireWriteLock();
        try
        {
            ServerInternals.LoadedChunks(server)[index] = restored;
            restored.MarkToPack();
        }
        finally
        {
            chunksLock.ReleaseWriteLock();
        }

        // What ServerSystemSupplyChunks does to a chunk it loads from the save.
        if (restored.Entities != null)
        {
            foreach (Entity entity in restored.Entities.Where(e => e != null).ToList())
            {
                if (!server.LoadEntity(entity, index)) restored.RemoveEntity(entity.EntityId);
            }
        }

        foreach (BlockEntity blockEntity in restored.BlockEntities.Values.Where(b => b != null).ToList())
        {
            try
            {
                blockEntity.Initialize(server.Api);
            }
            catch (Exception ex)
            {
                ServerMain.Logger.Notification("Exception thrown when trying to initialize a block entity @{0}: {1}", blockEntity.Pos, ex);
                blockEntity.UnregisterAllTickListeners();
            }
        }

        foreach (Entity player in kept)
        {
            restored.AddEntity(player);
        }

        restored.MarkModified();

        // Every client that has the chunk gets it again, with its entities.
        ChunkPos pos = server.WorldMap.ChunkPosFromChunkIndex3D(index);
        server.BroadcastChunk(pos.X, pos.InternalY, pos.Z, onlyIfInRange: false);
        ServerInternals.TriggerChunkDirty(server, new Vec3i(pos.X, pos.InternalY, pos.Z), restored);
    }

    private void RestorePlayerData(ServerMain server)
    {
        PlayerDataManager players = server.PlayerDataManager;
        HashSet<string> online = server.Clients.Values
            .Where(c => c.Player != null)
            .Select(c => c.Player.PlayerUID)
            .ToHashSet();

        foreach (string uid in players.WorldDataByUID.Keys.ToList())
        {
            if (online.Contains(uid)) continue;

            if (WorldPlayerData.TryGetValue(uid, out byte[]? saved))
            {
                ServerWorldPlayerData data = SerializerUtil.Deserialize<ServerWorldPlayerData>(saved);
                data.Init(server);
                players.WorldDataByUID[uid] = data;
            }
            else
            {
                players.WorldDataByUID.Remove(uid);
            }
        }

        foreach (string uid in players.PlayerDataByUid.Keys.ToList())
        {
            if (ServerPlayerData.TryGetValue(uid, out string? saved) && online.Contains(uid))
            {
                // A connected player holds its server data object: it is restored in place, so the
                // player and the permissions API go on seeing the same one.
                ServerPlayerData live = players.PlayerDataByUid[uid];
                JsonConvert.PopulateObject(saved, live, s_replaceCollections);
                if (OnlinePlayers.TryGetValue(uid, out OnlinePlayerState? state) && state.Entity is { } entity)
                {
                    PlayerEntityState.PlayerDataInternals.RuntimePrivileges(live) = [.. entity.RuntimePrivileges];
                }
            }
            else if (saved != null)
            {
                players.PlayerDataByUid[uid] = JsonConvert.DeserializeObject<ServerPlayerData>(saved)!;
            }
            else if (!online.Contains(uid))
            {
                players.PlayerDataByUid.Remove(uid);
            }
        }

        players.playerDataDirty = true;

        ref HashSet<string> codes = ref ServerConfigInternals.RuntimePrivilegeCodes(server.Config);
        codes = [.. RuntimePrivilegeCodes];
    }

    private static readonly JsonSerializerSettings s_replaceCollections = new() { ObjectCreationHandling = ObjectCreationHandling.Replace };

    private int RestoreOnlinePlayers(ServerMain server, WorldRestoreFilter filter)
    {
        int restored = 0;
        foreach (ConnectedClient client in server.Clients.Values)
        {
            if (client.Player is { Entity: not null } player && OnlinePlayers.TryGetValue(player.PlayerUID, out OnlinePlayerState? state))
            {
                bool gameModeChanged = state.RestoreInto(player, filter);

                // Game mode, spawn point, role and privileges go out in one player data packet.
                server.BroadcastPlayerData(player, sendInventory: gameModeChanged, sendPrivileges: true);
                restored++;
            }
        }

        return restored;
    }

    private sealed class Unsubscriber(Action unsubscribe) : IDisposable
    {
        private Action? _unsubscribe = unsubscribe;

        public void Dispose() => Interlocked.Exchange(ref _unsubscribe, null)?.Invoke();
    }

    /// <summary>The parts of a map chunk that block changes update.</summary>
    private sealed record MapChunkState(int ChunkX, int ChunkZ, ushort[] RainHeightMap, ushort YMax)
    {
        public static MapChunkState From(int chunkX, int chunkZ, ServerMapChunk mapChunk) =>
            new(chunkX, chunkZ, (ushort[])mapChunk.RainHeightMap.Clone(), mapChunk.YMax);

        /// <summary>Writes the saved state back and returns whether anything changed.</summary>
        public bool RestoreInto(ServerMapChunk mapChunk)
        {
            if (mapChunk.YMax == YMax && mapChunk.RainHeightMap.AsSpan().SequenceEqual(RainHeightMap)) return false;

            RainHeightMap.CopyTo(mapChunk.RainHeightMap, 0);
            mapChunk.YMax = YMax;
            mapChunk.MarkDirty();
            return true;
        }
    }

    /// <summary>Where a connected player stood, in which game mode, holding what, in what state.</summary>
    private sealed record OnlinePlayerState(
        double X, double Y, double Z, float Yaw, float Pitch, int Dimension,
        EnumGameMode GameMode,
        IReadOnlyDictionary<string, byte[]> Inventories)
    {
        /// <summary>The player's entity state and the rest of its player data.</summary>
        public PlayerEntityState? Entity { get; init; }

        public static OnlinePlayerState From(IServerPlayer player)
        {
            EntityPos pos = player.Entity.Pos;
            Dictionary<string, byte[]> inventories = [];
            foreach ((string id, IInventory inventory) in player.InventoryManager.Inventories)
            {
                if (!Restorable(inventory, out InventoryBasePlayer? inv)) continue;

                TreeAttribute tree = new();
                inv.ToTreeAttributes(tree);
                inventories[id] = tree.ToBytes();
            }

            return new OnlinePlayerState(pos.X, pos.Y, pos.Z, pos.Yaw, pos.Pitch, pos.Dimension, player.WorldData.CurrentGameMode, inventories)
            {
                Entity = PlayerEntityState.From(player),
            };
        }

        /// <summary>Puts the player back and returns whether its game mode changed.</summary>
        public bool RestoreInto(IServerPlayer player, WorldRestoreFilter filter)
        {
            bool gameModeChanged = player.WorldData.CurrentGameMode != GameMode;
            if (gameModeChanged) player.WorldData.CurrentGameMode = GameMode;

            Entity?.RestoreInto(player, filter);

            foreach ((string id, IInventory inventory) in player.InventoryManager.Inventories)
            {
                if (!Restorable(inventory, out InventoryBasePlayer? inv) || !Inventories.TryGetValue(id, out byte[]? saved)) continue;

                inv.FromTreeAttributes(TreeAttribute.CreateFromBytes(saved));
                for (int slot = 0; slot < inv.Count; slot++)
                {
                    inv.MarkSlotDirty(slot);
                }
            }

            EntityPlayer entity = player.Entity;
            if (entity.Pos.Dimension == Dimension)
            {
                entity.TeleportToDouble(X, Y, Z);
                entity.Pos.Yaw = Yaw;
                entity.Pos.Pitch = Pitch;
            }

            return gameModeChanged;
        }
    }

    /// <summary>
    /// The inventories the game saves with a player. The creative inventory is built from the
    /// item registry rather than saved, and has no slots to restore.
    /// </summary>
    private static bool Restorable(IInventory inventory, [NotNullWhen(true)] out InventoryBasePlayer? restorable)
    {
        restorable = inventory is InventoryBasePlayer player and not InventoryPlayerCreative ? player : null;
        return restorable != null;
    }

    /// <summary>The server config's runtime privileges, which the game keeps internal.</summary>
    private static class ServerConfigInternals
    {
        [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "RuntimePrivileveCodes")]
        public static extern ref HashSet<string> RuntimePrivilegeCodes(ServerConfig config);
    }

    /// <summary>The map's one-chunk lookup cache, which the game keeps internal.</summary>
    private static class WorldMapInternals
    {
        [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "prevChunkX")]
        public static extern ref int PrevChunkX(ServerWorldMap map);

        [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "prevChunkY")]
        public static extern ref int PrevChunkY(ServerWorldMap map);

        [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "prevChunkZ")]
        public static extern ref int PrevChunkZ(ServerWorldMap map);

        [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "prevChunk")]
        public static extern ref IWorldChunk? PrevChunk(ServerWorldMap map);
    }

    /// <summary>
    /// Empties the map's one-chunk lookup cache, so its next lookup reads the loaded chunk table again. The cache
    /// is read and written under the loaded chunk table's lock, which is the lock taken here.
    /// </summary>
    internal static void ForgetCachedChunk(ServerMain server)
    {
        ServerWorldMap map = server.WorldMap;
        lock (ServerInternals.LoadedChunks(server))
        {
            WorldMapInternals.PrevChunkX(map) = -1;
            WorldMapInternals.PrevChunkY(map) = -1;
            WorldMapInternals.PrevChunkZ(map) = -1;
            WorldMapInternals.PrevChunk(map) = null;
        }
    }

    /// <summary>The server's chunk table, which the game keeps internal.</summary>
    private static class ServerInternals
    {
        [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "loadedChunks")]
        public static extern ref Dictionary<long, ServerChunk> LoadedChunks(ServerMain server);

        [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "loadedChunksLock")]
        public static extern ref FastRWLock LoadedChunksLock(ServerMain server);

        [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "serverChunkDataPool")]
        public static extern ref ChunkDataPool ChunkDataPool(ServerMain server);

        /// <summary>
        /// Raises the chunk dirty event as for a newly loaded chunk, so mods that cache per chunk
        /// (rooms, for one) drop what they cached. The event API's class is internal.
        /// </summary>
        public static void TriggerChunkDirty(ServerMain server, Vec3i coord, IWorldChunk chunk)
        {
            object events = ((ICoreServerAPI)server.Api).Event;
            MethodInfo trigger = events.GetType().GetMethod("TriggerChunkDirty", BindingFlags.Instance | BindingFlags.Public)
                ?? throw new MissingMethodException(events.GetType().Name, "TriggerChunkDirty");
            trigger.Invoke(events, [coord, chunk, EnumChunkDirtyReason.NewlyLoaded]);
        }
    }
}
