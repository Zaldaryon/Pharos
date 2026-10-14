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

/// <summary>Waits for and reads the server's world physics: light, liquids, falling blocks and relighting.</summary>
public sealed class WorldPhysicsDriver
{
    // The falling-block entity lives in the game's content assemblies, which Pharos does not reference.
    private const string FallingBlockType = "EntityBlockFalling";
    private readonly EmbeddedServerHost _host;

    internal WorldPhysicsDriver(EmbeddedServerHost host) => _host = host;

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
