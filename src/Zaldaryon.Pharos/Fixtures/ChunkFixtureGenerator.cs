using System;
using System.Collections.Generic;

namespace Zaldaryon.Pharos.Fixtures;

/// <summary>
/// Generates ChunkFixture instances with configurable block patterns.
/// Useful for creating synthetic test data without a live server.
/// </summary>
public static class ChunkFixtureGenerator
{
    /// <summary>
    /// Default air block code used when no block is specified.
    /// </summary>
    public const string AirBlockCode = "game:air";

    /// <summary>
    /// Generates a chunk filled entirely with a single block type.
    /// </summary>
    /// <param name="blockCode">The block code to fill the chunk with.</param>
    /// <returns>A ChunkFixture with all blocks set to the specified type.</returns>
    public static ChunkFixture Solid(string blockCode)
    {
        ArgumentException.ThrowIfNullOrEmpty(blockCode);

        var fixture = new ChunkFixture();
        for (int y = 0; y < ChunkFixture.ChunkSize; y++)
        {
            for (int z = 0; z < ChunkFixture.ChunkSize; z++)
            {
                for (int x = 0; x < ChunkFixture.ChunkSize; x++)
                {
                    int idx = ChunkFixture.ToIndex(x, y, z);
                    fixture.BlockCodes[idx] = blockCode;
                }
            }
        }
        return fixture;
    }

    /// <summary>
    /// Generates a chunk with alternating blocks in a 3D checkerboard pattern.
    /// </summary>
    /// <param name="block1">Block code for even-parity positions (x+y+z even).</param>
    /// <param name="block2">Block code for odd-parity positions (x+y+z odd).</param>
    /// <returns>A ChunkFixture with a checkerboard pattern.</returns>
    public static ChunkFixture Checkerboard(string block1, string block2)
    {
        ArgumentException.ThrowIfNullOrEmpty(block1);
        ArgumentException.ThrowIfNullOrEmpty(block2);

        var fixture = new ChunkFixture();
        for (int y = 0; y < ChunkFixture.ChunkSize; y++)
        {
            for (int z = 0; z < ChunkFixture.ChunkSize; z++)
            {
                for (int x = 0; x < ChunkFixture.ChunkSize; x++)
                {
                    int idx = ChunkFixture.ToIndex(x, y, z);
                    bool isEven = (x + y + z) % 2 == 0;
                    fixture.BlockCodes[idx] = isEven ? block1 : block2;
                }
            }
        }
        return fixture;
    }

    /// <summary>
    /// Generates a chunk with randomly distributed blocks from a set of block codes.
    /// </summary>
    /// <param name="blockCodes">Array of block codes to randomly select from.</param>
    /// <param name="seed">Random seed for reproducible generation.</param>
    /// <returns>A ChunkFixture with randomly placed blocks.</returns>
    public static ChunkFixture Random(string[] blockCodes, int seed)
    {
        ArgumentNullException.ThrowIfNull(blockCodes);
        if (blockCodes.Length == 0)
        {
            throw new ArgumentException("Block codes array cannot be empty.", nameof(blockCodes));
        }

        var random = new Random(seed);
        var fixture = new ChunkFixture();

        for (int y = 0; y < ChunkFixture.ChunkSize; y++)
        {
            for (int z = 0; z < ChunkFixture.ChunkSize; z++)
            {
                for (int x = 0; x < ChunkFixture.ChunkSize; x++)
                {
                    int idx = ChunkFixture.ToIndex(x, y, z);
                    int blockIndex = random.Next(blockCodes.Length);
                    fixture.BlockCodes[idx] = blockCodes[blockIndex];
                }
            }
        }
        return fixture;
    }

    /// <summary>
    /// Generates a chunk with horizontal layers of different blocks.
    /// Each layer definition specifies the starting Y level and the block code for that layer.
    /// Layers are processed in order, with later definitions overwriting earlier ones.
    /// </summary>
    /// <param name="layers">List of (startY, blockCode) tuples defining each layer.</param>
    /// <returns>A ChunkFixture with layered blocks.</returns>
    public static ChunkFixture Layered(IReadOnlyList<(int y, string blockCode)> layers)
    {
        ArgumentNullException.ThrowIfNull(layers);
        if (layers.Count == 0)
        {
            throw new ArgumentException("Layers list cannot be empty.", nameof(layers));
        }

        // First fill entire chunk with air
        var fixture = new ChunkFixture();
        for (int idx = 0; idx < ChunkFixture.BlockCount; idx++)
        {
            fixture.BlockCodes[idx] = AirBlockCode;
        }

        // Sort layers by Y ascending so we can process them in order
        var sortedLayers = new List<(int y, string blockCode)>(layers);
        sortedLayers.Sort((a, b) => a.y.CompareTo(b.y));

        // Apply each layer from its start Y to either the next layer's Y or the chunk top
        for (int i = 0; i < sortedLayers.Count; i++)
        {
            var (startY, blockCode) = sortedLayers[i];
            ArgumentException.ThrowIfNullOrEmpty(blockCode);

            int endY = (i + 1 < sortedLayers.Count) ? sortedLayers[i + 1].y : ChunkFixture.ChunkSize;

            // Clamp to valid chunk bounds
            startY = Math.Max(0, Math.Min(startY, ChunkFixture.ChunkSize - 1));
            endY = Math.Max(startY, Math.Min(endY, ChunkFixture.ChunkSize));

            for (int y = startY; y < endY; y++)
            {
                for (int z = 0; z < ChunkFixture.ChunkSize; z++)
                {
                    for (int x = 0; x < ChunkFixture.ChunkSize; x++)
                    {
                        int idx = ChunkFixture.ToIndex(x, y, z);
                        fixture.BlockCodes[idx] = blockCode;
                    }
                }
            }
        }

        return fixture;
    }

    /// <summary>
    /// Generates a chunk with horizontal layers using a simplified Y-to-block mapping.
    /// Each entry in the dictionary maps a Y level to a block code. Blocks at Y levels
    /// not in the dictionary are filled with air.
    /// </summary>
    /// <param name="yToBlock">Dictionary mapping Y levels to block codes.</param>
    /// <returns>A ChunkFixture with the specified Y layers.</returns>
    public static ChunkFixture FromYMap(IReadOnlyDictionary<int, string> yToBlock)
    {
        ArgumentNullException.ThrowIfNull(yToBlock);

        var fixture = new ChunkFixture();

        // Fill with air by default
        for (int idx = 0; idx < ChunkFixture.BlockCount; idx++)
        {
            fixture.BlockCodes[idx] = AirBlockCode;
        }

        // Apply specific Y layers
        foreach (var (y, blockCode) in yToBlock)
        {
            if (y < 0 || y >= ChunkFixture.ChunkSize)
            {
                continue;
            }

            ArgumentException.ThrowIfNullOrEmpty(blockCode);

            for (int z = 0; z < ChunkFixture.ChunkSize; z++)
            {
                for (int x = 0; x < ChunkFixture.ChunkSize; x++)
                {
                    int idx = ChunkFixture.ToIndex(x, y, z);
                    fixture.BlockCodes[idx] = blockCode;
                }
            }
        }

        return fixture;
    }

    /// <summary>
    /// Generates an empty chunk filled entirely with air.
    /// </summary>
    /// <returns>A ChunkFixture with all blocks set to air.</returns>
    public static ChunkFixture Empty()
    {
        return Solid(AirBlockCode);
    }

    /// <summary>
    /// Gets the block code at a specific position in the fixture, or the default air code if not set.
    /// </summary>
    /// <param name="fixture">The chunk fixture to query.</param>
    /// <param name="x">X coordinate (0-31).</param>
    /// <param name="y">Y coordinate (0-31).</param>
    /// <param name="z">Z coordinate (0-31).</param>
    /// <returns>The block code at the position, or air if not set.</returns>
    public static string GetBlockCode(ChunkFixture fixture, int x, int y, int z)
    {
        ArgumentNullException.ThrowIfNull(fixture);
        int idx = ChunkFixture.ToIndex(x, y, z);
        return fixture.BlockCodes.TryGetValue(idx, out var code) ? code : AirBlockCode;
    }

    /// <summary>
    /// Checks if a position contains a block that is considered "solid" (non-air).
    /// </summary>
    /// <param name="fixture">The chunk fixture to query.</param>
    /// <param name="x">X coordinate (0-31).</param>
    /// <param name="y">Y coordinate (0-31).</param>
    /// <param name="z">Z coordinate (0-31).</param>
    /// <returns>True if the block is not air; otherwise false.</returns>
    public static bool IsSolid(ChunkFixture fixture, int x, int y, int z)
    {
        var code = GetBlockCode(fixture, x, y, z);
        return !string.Equals(code, AirBlockCode, StringComparison.OrdinalIgnoreCase);
    }
}
