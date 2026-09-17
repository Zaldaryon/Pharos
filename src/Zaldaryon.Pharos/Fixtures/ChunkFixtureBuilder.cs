using System;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Zaldaryon.Pharos.Timing;

namespace Zaldaryon.Pharos.Fixtures;

/// <summary>
/// Fluent builder for constructing synthetic chunk fixtures in memory.
/// </summary>
public sealed class ChunkFixtureBuilder
{
    private readonly ChunkFixture _fixture = new();

    /// <summary>
    /// Sets the target world-space chunk position.
    /// </summary>
    public ChunkFixtureBuilder At(ChunkPos position)
    {
        _fixture.Position = position;
        return this;
    }

    /// <summary>
    /// Sets the target world-space chunk position using raw coordinates.
    /// </summary>
    public ChunkFixtureBuilder At(int chunkX, int chunkY, int chunkZ)
    {
        _fixture.Position = new ChunkPos(chunkX, chunkY, chunkZ);
        return this;
    }

    /// <summary>
    /// Sets the default sunlight level for the entire chunk (clamped to 0..31).
    /// </summary>
    public ChunkFixtureBuilder WithDefaultSunlight(byte level)
    {
        _fixture.DefaultSunlight = Math.Clamp(level, (byte)0, (byte)31);
        return this;
    }

    /// <summary>
    /// Sets a block at chunk-local coordinates (0..31) using an asset code string.
    /// </summary>
    public ChunkFixtureBuilder SetBlock(int x, int y, int z, string blockCode)
    {
        ArgumentException.ThrowIfNullOrEmpty(blockCode);
        int idx = ChunkFixture.ToIndex(x, y, z);
        _fixture.BlockCodes[idx] = blockCode;
        _fixture.BlockIds.Remove(idx);
        return this;
    }

    /// <summary>
    /// Sets a block at chunk-local coordinates (0..31) using an explicit integer block ID.
    /// </summary>
    public ChunkFixtureBuilder SetBlock(int x, int y, int z, int blockId)
    {
        int idx = ChunkFixture.ToIndex(x, y, z);
        _fixture.BlockIds[idx] = blockId;
        _fixture.BlockCodes.Remove(idx);
        return this;
    }

    /// <summary>
    /// Fills an inclusive 3D bounding box within the chunk with a block code.
    /// </summary>
    public ChunkFixtureBuilder Fill(int minX, int minY, int minZ, int maxX, int maxY, int maxZ, string blockCode)
    {
        ArgumentException.ThrowIfNullOrEmpty(blockCode);
        int startX = Math.Min(minX, maxX);
        int endX = Math.Max(minX, maxX);
        int startY = Math.Min(minY, maxY);
        int endY = Math.Max(minY, maxY);
        int startZ = Math.Min(minZ, maxZ);
        int endZ = Math.Max(minZ, maxZ);

        for (int y = startY; y <= endY; y++)
        {
            for (int z = startZ; z <= endZ; z++)
            {
                for (int x = startX; x <= endX; x++)
                {
                    SetBlock(x, y, z, blockCode);
                }
            }
        }
        return this;
    }

    /// <summary>
    /// Fills an inclusive 3D bounding box within the chunk with an explicit block ID.
    /// </summary>
    public ChunkFixtureBuilder Fill(int minX, int minY, int minZ, int maxX, int maxY, int maxZ, int blockId)
    {
        int startX = Math.Min(minX, maxX);
        int endX = Math.Max(minX, maxX);
        int startY = Math.Min(minY, maxY);
        int endY = Math.Max(minY, maxY);
        int startZ = Math.Min(minZ, maxZ);
        int endZ = Math.Max(minZ, maxZ);

        for (int y = startY; y <= endY; y++)
        {
            for (int z = startZ; z <= endZ; z++)
            {
                for (int x = startX; x <= endX; x++)
                {
                    SetBlock(x, y, z, blockId);
                }
            }
        }
        return this;
    }

    /// <summary>
    /// Sets a custom sunlight level at chunk-local coordinates (clamped to 0..31).
    /// </summary>
    public ChunkFixtureBuilder SetSunlight(int x, int y, int z, byte level)
    {
        int idx = ChunkFixture.ToIndex(x, y, z);
        _fixture.CustomSunlight[idx] = Math.Clamp(level, (byte)0, (byte)31);
        return this;
    }

    /// <summary>
    /// Sets a custom blocklight level at chunk-local coordinates (clamped to 0..31).
    /// </summary>
    public ChunkFixtureBuilder SetBlocklight(int x, int y, int z, byte level)
    {
        int idx = ChunkFixture.ToIndex(x, y, z);
        _fixture.CustomBlocklight[idx] = Math.Clamp(level, (byte)0, (byte)31);
        return this;
    }

    /// <summary>
    /// Adds an entity into this chunk fixture.
    /// </summary>
    public ChunkFixtureBuilder AddEntity(Entity entity)
    {
        ArgumentNullException.ThrowIfNull(entity);
        _fixture.Entities.Add(entity);
        return this;
    }

    /// <summary>
    /// Adds a block entity placed at a specific block position in this chunk.
    /// </summary>
    public ChunkFixtureBuilder AddBlockEntity(BlockPos pos, BlockEntity blockEntity)
    {
        ArgumentNullException.ThrowIfNull(pos);
        ArgumentNullException.ThrowIfNull(blockEntity);
        _fixture.BlockEntities[pos] = blockEntity;
        return this;
    }

    /// <summary>
    /// Builds and returns the configured ChunkFixture instance.
    /// </summary>
    public ChunkFixture Build()
    {
        return _fixture;
    }

    /// <summary>
    /// Implicit conversion operator to ChunkFixture.
    /// </summary>
    public static implicit operator ChunkFixture(ChunkFixtureBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.Build();
    }
}
