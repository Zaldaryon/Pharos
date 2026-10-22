using System.Reflection;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;

namespace Zaldaryon.Pharos.Server;

/// <summary>
/// The snow of the server: how much lies on a block, and the game's own accumulation of it.
/// </summary>
public sealed class SnowDriver
{
    private const string SystemType = "Vintagestory.GameContent.WeatherSystemServer";

    private readonly EmbeddedServerHost _host;

    internal SnowDriver(EmbeddedServerHost host) => _host = host;

    /// <summary>
    /// The snow level on a block, the way the block itself says it.
    /// </summary>
    /// <param name="pos">The block to read.</param>
    public float LevelAt(BlockPos pos)
    {
        ArgumentNullException.ThrowIfNull(pos);

        return _host.RunOnGameThread(() =>
        {
            Block block = _host.Api.World.BlockAccessor.GetBlock(pos);

            // The snow layer carries its height in its variant.
            if (block.BlockMaterial == EnumBlockMaterial.Snow
                && block.Variant.TryGetValue("height", out string? height)
                && int.TryParse(height, out int level))
            {
                return level;
            }

            return block.GetSnowLevel(pos);
        });
    }

    /// <summary>
    /// Whether a block takes snow at all.
    /// </summary>
    /// <param name="pos">The block to read.</param>
    public bool AllowsSnow(BlockPos pos)
    {
        ArgumentNullException.ThrowIfNull(pos);

        return _host.RunOnGameThread(() =>
        {
            Block block = _host.Api.World.BlockAccessor.GetBlock(pos);
            return block.AllowSnowCoverage(_host.Api.World, pos);
        });
    }

    /// <summary>
    /// Asks the game's own snow system to look at the chunks of a region now, and waits for the
    /// snow to settle.
    /// </summary>
    /// <param name="min">One corner of the region, inclusive.</param>
    /// <param name="max">The opposite corner of the region, inclusive.</param>
    /// <param name="maxTicks">The most ticks to wait.</param>
    /// <param name="ct">Cancels the wait.</param>
    /// <returns>Whether the world's weather system was there to do the work.</returns>
    public async Task<bool> UpdateAsync(
        BlockPos min,
        BlockPos max,
        int maxTicks = 200,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(min);
        ArgumentNullException.ThrowIfNull(max);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxTicks);

        bool queued = _host.RunOnGameThread(() =>
        {
            object? snow = FindSnowSystem();
            if (snow == null) return false;

            foreach (Vec2i chunk in ChunksOf(min, max))
            {
                Member.Call(snow, "AddToCheckQueue", chunk);
            }

            return true;
        });

        if (!queued) return false;

        for (int tick = 0; tick < maxTicks; tick++)
        {
            ct.ThrowIfCancellationRequested();
            _host.Tick();
            await Task.Yield();
        }

        return true;
    }

    private IEnumerable<Vec2i> ChunksOf(BlockPos min, BlockPos max)
    {
        int size = GlobalConstants.ChunkSize;
        int firstX = min.X / size;
        int lastX = max.X / size;
        int firstZ = min.Z / size;
        int lastZ = max.Z / size;

        for (int x = firstX; x <= lastX; x++)
        {
            for (int z = firstZ; z <= lastZ; z++)
            {
                yield return new Vec2i(x, z);
            }
        }
    }

    private object? FindSnowSystem()
    {
        object? system = _host.Api.ModLoader.GetModSystem(SystemType);
        if (system == null) return null;

        return Member.Get(system, "snowSimSnowAccu");
    }

    /// <summary>
    /// The weather system's members, found by name: Pharos does not build against the essentials
    /// mod. A member the game renamed is reported by name.
    /// </summary>
    private static class Member
    {
        private const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

        public static object? Get(object target, string name)
        {
            Type type = target.GetType();
            if (type.GetProperty(name, Flags) is { } property) return property.GetValue(target);
            if (type.GetField(name, Flags) is { } field) return field.GetValue(target);
            throw new MissingMemberException(type.FullName, name);
        }

        public static object? Call(object target, string name, params object?[] args)
        {
            Type type = target.GetType();
            MethodInfo method = type.GetMethods(Flags)
                .FirstOrDefault(m => m.Name == name && m.GetParameters().Length == args.Length && m.GetParameters().Zip(args).All(p => p.Second == null || p.First.ParameterType.IsInstanceOfType(p.Second)))
                ?? throw new MissingMemberException(type.FullName, name);
            try
            {
                return method.Invoke(target, args);
            }
            catch (TargetInvocationException ex) when (ex.InnerException != null)
            {
                global::System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
                throw;
            }
        }
    }
}
