using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Zaldaryon.Pharos.XUnit.Execution;

namespace Zaldaryon.Pharos.Server;

/// <summary>The light at one block position, by light source.</summary>
/// <param name="Block">The light from block sources, such as torches.</param>
/// <param name="Sun">The light from the sky.</param>
/// <param name="Max">The brighter of the two.</param>
public readonly record struct LightReading(int Block, int Sun, int Max);

/// <summary>The room around a block position, as the game's room registry reads it.</summary>
/// <param name="ExitCount">The open sides of the room. Zero means it is enclosed.</param>
/// <param name="SkylightCount">The room's blocks that see the sky.</param>
/// <param name="NonSkylightCount">The room's blocks that do not see the sky.</param>
/// <param name="IsCellar">Whether the room is no bigger than the game's cellar dimensions and is enclosed.</param>
/// <param name="Min">One corner of the room's bounding box, inclusive.</param>
/// <param name="Max">The opposite corner of the room's bounding box, inclusive.</param>
public sealed record RoomReading(int ExitCount, int SkylightCount, int NonSkylightCount, bool IsCellar, BlockPos Min, BlockPos Max)
{
    /// <summary>Whether no side of the room is open.</summary>
    public bool Enclosed => ExitCount == 0;
}

/// <summary>An entity an explosion hurt.</summary>
/// <param name="EntityId">The entity's id.</param>
/// <param name="Code">The entity's code.</param>
/// <param name="Damage">How much health it lost.</param>
public sealed record DamagedEntity(long EntityId, string Code, float Damage);

/// <summary>What an explosion did.</summary>
/// <param name="Destroyed">The blocks the explosion destroyed.</param>
/// <param name="Drops">The items the destroyed blocks dropped.</param>
/// <param name="Damaged">The entities the explosion hurt.</param>
public sealed record ExplosionReport(
    IReadOnlyList<BlockPos> Destroyed,
    IReadOnlyList<EntityItem> Drops,
    IReadOnlyList<DamagedEntity> Damaged);

/// <summary>Waits for and reads the server's world physics: light, liquids, falling blocks, relighting, rooms and fire.</summary>
public sealed class WorldPhysicsDriver
{
    // The falling-block entity lives in the game's content assemblies, which Pharos does not reference.
    private const string FallingBlockType = "EntityBlockFalling";
    private const string RoomRegistryType = "Vintagestory.GameContent.RoomRegistry";
    private readonly EmbeddedServerHost _host;
    private FireDriver? _fire;

    internal WorldPhysicsDriver(EmbeddedServerHost host) => _host = host;

    /// <summary>Starts fires and runs their spread: the game's fire block and its burning behaviour.</summary>
    public FireDriver Fire => _fire ??= new FireDriver(_host);

    /// <summary>Reads the block, sun and combined light at a position on the game thread.</summary>
    /// <param name="pos">The block position.</param>
    /// <exception cref="ArgumentNullException"><paramref name="pos"/> is null.</exception>
    public LightReading Light(BlockPos pos)
    {
        ArgumentNullException.ThrowIfNull(pos);
        return _host.RunOnGameThread(() =>
        {
            IBlockAccessor blocks = ServerApi.World.BlockAccessor;
            return new LightReading(
                blocks.GetLightLevel(pos, EnumLightLevelType.OnlyBlockLight),
                blocks.GetLightLevel(pos, EnumLightLevelType.OnlySunLight),
                blocks.GetLightLevel(pos, EnumLightLevelType.MaxLight));
        });
    }

    /// <summary>
    /// Waits until the lighting queue is empty and a lighting pass has finished since it last held work.
    /// It does not wait for the other background systems; see <see cref="EmbeddedServerHost.WaitForBackgroundWorkAsync"/>.
    /// </summary>
    /// <param name="timeout">How long to wait; 30 seconds by default, scaled by <c>PHAROS_TIMEOUT_SCALE</c>.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <exception cref="InvalidOperationException">Called on the server's game thread, or the server is not running.</exception>
    /// <exception cref="TimeoutException">The lighting queue still had work when the time was up.</exception>
    public Task WaitForRelightAsync(TimeSpan? timeout = null, CancellationToken ct = default)
    {
        if (_host.IsOnGameThread) throw new InvalidOperationException("WaitForRelightAsync waits for other threads and cannot run on the server's game thread.");
        if (!_host.IsRunning) throw new InvalidOperationException("The server is not running.");
        return BackgroundWork.WaitAsync(_host.Server, timeout ?? TimeSpan.FromSeconds(30 * ScenarioTimeouts.Scale), ct, BackgroundWork.Kind.Lighting);
    }

    /// <summary>Ticks the server until no falling block entity is alive.</summary>
    /// <param name="maxTicks">The most ticks to run.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <exception cref="TimeoutException">Falling blocks were still alive after <paramref name="maxTicks"/>.</exception>
    public async Task WaitForFallingBlocksAsync(int maxTicks = 600, CancellationToken ct = default)
    {
        bool settled = await _host.TickUntilAsync(() => FallingBlocksAlive() == 0, maxTicks, ct).ConfigureAwait(false);
        if (!settled) throw new TimeoutException($"{FallingBlocksAlive()} falling block(s) were still alive after {maxTicks} ticks.");
    }

    /// <summary>Ticks the server until no liquid spread callback is pending in the box.</summary>
    /// <param name="min">One corner of the box, inclusive.</param>
    /// <param name="max">The opposite corner of the box, inclusive.</param>
    /// <param name="maxTicks">The most ticks to run.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <exception cref="ArgumentNullException"><paramref name="min"/> or <paramref name="max"/> is null.</exception>
    /// <exception cref="TimeoutException">Liquid spread was still pending after <paramref name="maxTicks"/>.</exception>
    public async Task WaitForLiquidsSettledAsync(BlockPos min, BlockPos max, int maxTicks = 600, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(min);
        ArgumentNullException.ThrowIfNull(max);
        bool settled = await _host.TickUntilAsync(() => PendingLiquidCallbacks(min, max) == 0, maxTicks, ct).ConfigureAwait(false);
        if (!settled) throw new TimeoutException($"{PendingLiquidCallbacks(min, max)} liquid callback(s) were still pending after {maxTicks} ticks.");
    }

    /// <summary>
    /// Reads the room at a position from the game's room registry. The registry finds the room by a
    /// flood fill from the position, and it caches rooms until their chunks change.
    /// </summary>
    /// <param name="pos">The block position inside or next to the room.</param>
    /// <exception cref="ArgumentNullException"><paramref name="pos"/> is null.</exception>
    /// <exception cref="InvalidOperationException">The game's room registry is not loaded, or it found no room.</exception>
    public RoomReading Room(BlockPos pos)
    {
        ArgumentNullException.ThrowIfNull(pos);
        return _host.RunOnGameThread(() =>
        {
            // The registry lives in the game's content assemblies, which Pharos does not reference.
            object registry = ServerApi.ModLoader.GetModSystem(RoomRegistryType)
                ?? throw new InvalidOperationException($"The game's {RoomRegistryType} is not loaded.");
            var getRoom = registry.GetType().GetMethod("GetRoomForPosition")
                ?? throw new MissingMethodException(RoomRegistryType, "GetRoomForPosition");
            object room = getRoom.Invoke(registry, [pos.Copy()])
                ?? throw new InvalidOperationException($"The room registry found no room at {pos}.");
            Cuboidi location = (Cuboidi)ProcessingDriver.Member(room, "Location")!;
            return new RoomReading(
                (int)ProcessingDriver.Member(room, "ExitCount")!,
                (int)ProcessingDriver.Member(room, "SkylightCount")!,
                (int)ProcessingDriver.Member(room, "NonSkylightCount")!,
                (bool)ProcessingDriver.Member(room, "IsSmallRoom")!,
                new BlockPos(location.X1, location.Y1, location.Z1, pos.dimension),
                new BlockPos(location.X2, location.Y2, location.Z2, pos.dimension));
        });
    }

    /// <summary>
    /// Holds the lighting thread between its passes, so light queued while it is held stays queued until the returned
    /// scope is disposed. A rollback run while it is held drops the queued light for the chunks it restores.
    /// </summary>
    /// <param name="timeout">How long to wait for the thread to stop; 10 seconds by default, scaled by <c>PHAROS_TIMEOUT_SCALE</c>.</param>
    /// <exception cref="InvalidOperationException">Called on the server's game thread, or the lighting thread is not running.</exception>
    /// <exception cref="TimeoutException">The thread did not stop within <paramref name="timeout"/>.</exception>
    public IDisposable PauseLighting(TimeSpan? timeout = null)
    {
        if (_host.IsOnGameThread) throw new InvalidOperationException("PauseLighting waits for the lighting thread and cannot run on the server's game thread.");
        return LightingThread.Pause(_host.Server, timeout);
    }

    /// <summary>The lighting tasks queued and not yet run.</summary>
    public int LightingQueued => _host.RunOnGameThread(() =>
    {
        var map = _host.Server.WorldMap;
        lock (map.LightingTasksLock) return map.LightingTasks.Count;
    });

    /// <summary>
    /// Unloads the chunk columns under a box, as the server does when no player is near them: each changed chunk is written to the
    /// save, and the server forgets it. The next read or edit of those blocks loads them from the save again, or <see cref="ReloadChunksAsync"/> does it now.
    /// </summary>
    /// <param name="min">One corner of the box, inclusive.</param>
    /// <param name="max">The opposite corner, inclusive.</param>
    /// <exception cref="ArgumentNullException"><paramref name="min"/> or <paramref name="max"/> is null.</exception>
    /// <exception cref="InvalidOperationException">A player stands in one of the columns, or the game's unloading system is missing.</exception>
    public Task UnloadChunksAsync(BlockPos min, BlockPos max)
    {
        ArgumentNullException.ThrowIfNull(min);
        ArgumentNullException.ThrowIfNull(max);
        if (_host.IsOnGameThread) throw new InvalidOperationException("UnloadChunksAsync waits for the game thread and cannot run on it.");
        _host.UnloadChunks(min, max);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Loads the chunks under a box from the save, keeping their columns loaded, and ticks the server until they are there. The
    /// chunks that were unloaded come back as new objects, with what the save held.
    /// </summary>
    /// <remarks>
    /// Only the chunks are reloaded. The map regions and their mod data stay as they were: a region is released by the game only
    /// after two minutes without loaded chunks, and a full reload of the save is <see cref="EmbeddedServerHost.RestartAsync"/>.
    /// </remarks>
    /// <param name="min">One corner of the box, inclusive.</param>
    /// <param name="max">The opposite corner, inclusive.</param>
    /// <param name="ct">Cancels the wait.</param>
    /// <exception cref="TimeoutException">The chunks did not load in time.</exception>
    public async Task ReloadChunksAsync(BlockPos min, BlockPos max, CancellationToken ct = default)
    {
        await _host.EnsureChunksLoadedAsync(min, max, ct: ct);

        // The map's one-chunk lookup cache may hold the absence of a chunk read while it was unloaded.
        _host.RunOnGameThread(() => WorldSnapshot.ForgetCachedChunk(_host.Server));
    }

    private ICoreServerAPI ServerApi => (ICoreServerAPI)_host.Server.Api;

    private int FallingBlocksAlive() => _host.RunOnGameThread(() =>
        ServerApi.World.LoadedEntities.Values.Count(e => e.Alive && e.GetType().Name == FallingBlockType));

    private int PendingLiquidCallbacks(BlockPos min, BlockPos max) => _host.TickListeners.Callbacks().Count(c =>
        c.Pos is { } p && Inside(p, min, max) && IsLiquidHandler(c.Handler));

    private static bool Inside(BlockPos p, BlockPos min, BlockPos max) =>
        p.X >= Math.Min(min.X, max.X) && p.X <= Math.Max(min.X, max.X)
        && p.Y >= Math.Min(min.Y, max.Y) && p.Y <= Math.Max(min.Y, max.Y)
        && p.Z >= Math.Min(min.Z, max.Z) && p.Z <= Math.Max(min.Z, max.Z);

    // The game's liquid spread callbacks are bound to the fluid layer's handlers.
    private static bool IsLiquidHandler(string handler) =>
        handler.Contains("Fluid", StringComparison.OrdinalIgnoreCase) || handler.Contains("Liquid", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Sets off an explosion the way the game does, and reports what it destroyed, what dropped
    /// and who was hurt.
    /// </summary>
    /// <param name="pos">Where the explosion starts.</param>
    /// <param name="blastType">The kind of blast.</param>
    /// <param name="destructionRadius">How many blocks it destroys.</param>
    /// <param name="injureRadius">How many blocks it hurts.</param>
    /// <param name="blockDropChanceMultiplier">How likely the destroyed blocks are to drop.</param>
    /// <param name="ignitedByPlayerUid">The player who set it off, when one did.</param>
    /// <param name="ct">Cancels the wait.</param>
    /// <returns>What the explosion did.</returns>
    public async Task<ExplosionReport> ExplodeAsync(
        BlockPos pos,
        EnumBlastType blastType = EnumBlastType.RockBlast,
        double destructionRadius = 4,
        double injureRadius = 6,
        float blockDropChanceMultiplier = 1,
        string? ignitedByPlayerUid = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(pos);

        int reach = (int)Math.Ceiling(Math.Max(destructionRadius, injureRadius)) + 2;
        Dictionary<BlockPos, int> blocksBefore = SnapshotBlocks(pos, reach);
        Dictionary<long, float> healthBefore = SnapshotHealth(pos, reach);
        HashSet<long> itemsBefore = SnapshotItemIds(pos, reach);

        _host.RunOnGameThread(() => _host.Api.World.CreateExplosion(
            pos,
            blastType,
            destructionRadius,
            injureRadius,
            blockDropChanceMultiplier,
            ignitedByPlayerUid));

        for (int tick = 0; tick < 3; tick++)
        {
            ct.ThrowIfCancellationRequested();
            _host.Tick();
            await Task.Yield();
        }

        List<BlockPos> destroyed = [];
        foreach (KeyValuePair<BlockPos, int> entry in blocksBefore)
        {
            if (entry.Value == 0) continue;
            if (_host.Api.World.BlockAccessor.GetBlock(entry.Key).Id == 0) destroyed.Add(entry.Key);
        }

        List<EntityItem> drops = [];
        foreach (EntityItem item in ItemsIn(pos, reach))
        {
            if (!itemsBefore.Contains(item.EntityId)) drops.Add(item);
        }

        List<DamagedEntity> damaged = [];
        foreach (KeyValuePair<long, float> entry in healthBefore)
        {
            Entity? entity = _host.Api.World.GetEntityById(entry.Key);
            if (entity == null) continue;

            float now = Health(entity);
            if (now < entry.Value) damaged.Add(new DamagedEntity(entry.Key, entity.Code?.ToString() ?? "", entry.Value - now));
        }

        return new ExplosionReport(destroyed, drops, damaged);
    }

    private Dictionary<BlockPos, int> SnapshotBlocks(BlockPos center, int reach)
    {
        Dictionary<BlockPos, int> blocks = [];
        _host.RunOnGameThread(() =>
        {
            BlockPos from = center.AddCopy(-reach, -reach, -reach);
            BlockPos to = center.AddCopy(reach, reach, reach);
            for (int x = from.X; x <= to.X; x++)
            {
                for (int y = from.Y; y <= to.Y; y++)
                {
                    for (int z = from.Z; z <= to.Z; z++)
                    {
                        BlockPos pos = new(x, y, z);
                        blocks[pos] = _host.Api.World.BlockAccessor.GetBlock(pos).Id;
                    }
                }
            }
        });

        return blocks;
    }

    private Dictionary<long, float> SnapshotHealth(BlockPos center, int reach)
    {
        Dictionary<long, float> health = [];
        _host.RunOnGameThread(() =>
        {
            foreach (Entity entity in EntitiesIn(center, reach))
            {
                health[entity.EntityId] = Health(entity);
            }
        });

        return health;
    }

    private HashSet<long> SnapshotItemIds(BlockPos center, int reach)
    {
        HashSet<long> ids = [];
        _host.RunOnGameThread(() =>
        {
            foreach (EntityItem item in ItemsIn(center, reach)) ids.Add(item.EntityId);
        });

        return ids;
    }

    private List<EntityItem> ItemsIn(BlockPos center, int reach)
    {
        List<EntityItem> items = [];
        _host.RunOnGameThread(() =>
        {
            foreach (Entity entity in EntitiesIn(center, reach))
            {
                if (entity is EntityItem item) items.Add(item);
            }
        });

        return items;
    }

    private List<Entity> EntitiesIn(BlockPos center, int reach)
    {
        List<Entity> entities = [];
        BlockPos from = center.AddCopy(-reach, -reach, -reach);
        BlockPos to = center.AddCopy(reach, reach, reach);
        foreach (Entity entity in _host.Api.World.GetEntitiesInsideCuboid(from, to)) entities.Add(entity);

        return entities;
    }

    private static float Health(Entity entity)
    {
        return entity.WatchedAttributes.GetTreeAttribute("health")?.GetFloat("currenthealth", 20) ?? 20;
    }
}
