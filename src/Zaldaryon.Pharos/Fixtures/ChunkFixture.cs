using System;
using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Zaldaryon.Pharos.Timing;

namespace Zaldaryon.Pharos.Fixtures;

/// <summary>
/// Represents a standalone chunk definition with blocks, lighting, entities, and coordinates.
/// Used to mock or inject chunk data directly into ClientWorldMap without a live server.
/// </summary>
public sealed class ChunkFixture
{
    public const int ChunkSize = 32;
    public const int BlockCount = ChunkSize * ChunkSize * ChunkSize; // 32768

    /// <summary>
    /// World-space chunk coordinates.
    /// </summary>
    public ChunkPos Position { get; set; }

    /// <summary>
    /// Default sunlight level applied across the chunk (0..31).
    /// </summary>
    public byte DefaultSunlight { get; set; } = 31;

    /// <summary>
    /// Map of chunk-local 3D block index to block code (e.g. "game:rock-granite").
    /// </summary>
    public Dictionary<int, string> BlockCodes { get; set; } = new();

    /// <summary>
    /// Map of chunk-local 3D block index to explicit integer block ID.
    /// </summary>
    public Dictionary<int, int> BlockIds { get; set; } = new();

    /// <summary>
    /// Custom per-block sunlight overrides (0..31).
    /// </summary>
    public Dictionary<int, byte> CustomSunlight { get; set; } = new();

    /// <summary>
    /// Custom per-block blocklight overrides (0..31).
    /// </summary>
    public Dictionary<int, byte> CustomBlocklight { get; set; } = new();

    /// <summary>
    /// List of entities positioned in this chunk.
    /// </summary>
    public List<Entity> Entities { get; set; } = new();

    /// <summary>
    /// Dictionary of block entities placed in this chunk.
    /// </summary>
    public Dictionary<BlockPos, BlockEntity> BlockEntities { get; set; } = new();

    /// <summary>
    /// Computes the 1D chunk-local array index from (x, y, z) coordinates in [0..31].
    /// Formula: (y * 32 + z) * 32 + x.
    /// </summary>
    public static int ToIndex(int x, int y, int z)
    {
        if (x < 0 || x >= ChunkSize || y < 0 || y >= ChunkSize || z < 0 || z >= ChunkSize)
        {
            throw new ArgumentOutOfRangeException(
                $"Coordinates ({x}, {y}, {z}) are outside valid chunk dimensions (0..{ChunkSize - 1}).");
        }
        return (y * ChunkSize + z) * ChunkSize + x;
    }

    /// <summary>
    /// Decomposes a 1D chunk-local array index back to (x, y, z) coordinates in [0..31].
    /// </summary>
    public static (int x, int y, int z) FromIndex(int index)
    {
        if (index < 0 || index >= BlockCount)
        {
            throw new ArgumentOutOfRangeException(nameof(index), $"Index must be within 0..{BlockCount - 1}.");
        }

        int x = index % ChunkSize;
        int rem = index / ChunkSize;
        int z = rem % ChunkSize;
        int y = rem / ChunkSize;
        return (x, y, z);
    }
}
