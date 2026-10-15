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
}
