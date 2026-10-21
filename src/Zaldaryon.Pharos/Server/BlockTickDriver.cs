using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace Zaldaryon.Pharos.Server;

/// <summary>
/// The random block ticks of the server: the blocks that spread, melt and grow when the server
/// ticks them by chance.
/// </summary>
public sealed class BlockTickDriver
{
    private readonly EmbeddedServerHost _host;
    private int? _pausedTicksPerChunk;

    internal BlockTickDriver(EmbeddedServerHost host) => _host = host;

    /// <summary>Whether the background random ticks are paused.</summary>
    public bool IsPaused => _pausedTicksPerChunk != null;

    /// <summary>
    /// Ticks one block the way the server does, with a random of the test's own, and says whether
    /// the block wanted the tick.
    /// </summary>
    /// <param name="pos">The block to tick.</param>
    /// <param name="seed">The seed of the random the block sees.</param>
    /// <returns>Whether the block wanted the tick and was ticked.</returns>
    public bool Tick(BlockPos pos, int seed = 0)
    {
        ArgumentNullException.ThrowIfNull(pos);

        return _host.RunOnGameThread(() =>
        {
            Block block = _host.Api.World.BlockAccessor.GetBlock(pos);
            Random random = new(seed);
            if (!block.ShouldReceiveServerGameTicks(_host.Api.World, pos, random, out object extra)) return false;

            block.OnServerGameTick(_host.Api.World, pos, extra);
            return true;
        });
    }

    /// <summary>
    /// Ticks every block of a region many times, the way the server does, and counts the blocks
    /// that changed.
    /// </summary>
    /// <param name="min">One corner of the region, inclusive.</param>
    /// <param name="max">The opposite corner of the region, inclusive.</param>
    /// <param name="rounds">How many times the region is ticked.</param>
    /// <param name="seed">The seed of the random the blocks see.</param>
    /// <param name="ct">Cancels the wait.</param>
    /// <returns>How many blocks changed.</returns>
    public async Task<int> TickRegionAsync(
        BlockPos min,
        BlockPos max,
        int rounds = 1,
        int seed = 0,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(min);
        ArgumentNullException.ThrowIfNull(max);
        ArgumentOutOfRangeException.ThrowIfNegative(rounds);

        int changes = 0;
        for (int round = 0; round < rounds; round++)
        {
            ct.ThrowIfCancellationRequested();

            Dictionary<BlockPos, int> before = Snapshot(min, max);
            foreach (BlockPos pos in before.Keys)
            {
                Tick(pos, seed + round);
            }

            foreach (KeyValuePair<BlockPos, int> entry in before)
            {
                if (_host.Api.World.BlockAccessor.GetBlock(entry.Key).Id != entry.Value) changes++;
            }

            await Task.Yield();
        }

        return changes;
    }

    /// <summary>
    /// Stops the background random ticks, so the blocks of a test stay where they are.
    /// </summary>
    public void Pause()
    {
        _host.RunOnGameThread(() =>
        {
            if (_pausedTicksPerChunk != null) return;
            _pausedTicksPerChunk = _host.Config.RandomBlockTicksPerChunk;
            _host.Config.RandomBlockTicksPerChunk = 0;
        });
    }

    /// <summary>
    /// Lets the background random ticks run again.
    /// </summary>
    public void Resume()
    {
        _host.RunOnGameThread(() =>
        {
            if (_pausedTicksPerChunk == null) return;
            _host.Config.RandomBlockTicksPerChunk = _pausedTicksPerChunk.Value;
            _pausedTicksPerChunk = null;
        });
    }

    private Dictionary<BlockPos, int> Snapshot(BlockPos min, BlockPos max)
    {
        Dictionary<BlockPos, int> blocks = [];
        _host.RunOnGameThread(() =>
        {
            for (int x = min.X; x <= max.X; x++)
            {
                for (int y = min.Y; y <= max.Y; y++)
                {
                    for (int z = min.Z; z <= max.Z; z++)
                    {
                        BlockPos pos = new(x, y, z);
                        blocks[pos] = _host.Api.World.BlockAccessor.GetBlock(pos).Id;
                    }
                }
            }
        });

        return blocks;
    }
}
