using Vintagestory.API.MathTools;

namespace Zaldaryon.Pharos.Timing;

/// <summary>
/// Represents a 3D chunk coordinate in world space.
/// </summary>
public readonly record struct ChunkPos(int X, int Y, int Z)
{
    public ChunkPos(Vec3i vec) : this(vec.X, vec.Y, vec.Z) { }

    /// <summary>
    /// Creates a ChunkPos from block coordinates by dividing by chunk size (32).
    /// </summary>
    public static ChunkPos FromBlockPos(BlockPos pos)
    {
        ArgumentNullException.ThrowIfNull(pos);
        return new ChunkPos(pos.X >> 5, pos.InternalY >> 5, pos.Z >> 5);
    }

    /// <summary>
    /// Creates a ChunkPos from raw block coordinates.
    /// </summary>
    public static ChunkPos FromBlockCoordinates(int blockX, int blockY, int blockZ)
    {
        return new ChunkPos(blockX >> 5, blockY >> 5, blockZ >> 5);
    }

    /// <summary>
    /// Converts this chunk position to a Vintage Story Vec3i.
    /// </summary>
    public Vec3i ToVec3i() => new(X, Y, Z);

    public override string ToString() => $"ChunkPos({X}, {Y}, {Z})";
}
