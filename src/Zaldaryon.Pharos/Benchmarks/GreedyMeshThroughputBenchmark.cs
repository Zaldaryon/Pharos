using Zaldaryon.Pharos.Fixtures;

namespace Zaldaryon.Pharos.Benchmarks;

/// <summary>
/// Benchmarks greedy mesh algorithm throughput across different chunk complexity tiers.
/// Measures faces merged per millisecond using ChunkFixtureGenerator patterns.
/// </summary>
public sealed class GreedyMeshThroughputBenchmark : PerformanceBenchmark
{
    private readonly ComplexityTier _tier;
    private ChunkFixture? _fixture;
    private int _expectedFaces;

    /// <summary>
    /// Complexity tier for the greedy mesh benchmark.
    /// </summary>
    public enum ComplexityTier
    {
        /// <summary>Solid chunk with all same blocks - maximum merge potential.</summary>
        Uniform,
        /// <summary>3D checkerboard pattern - no merge potential.</summary>
        Checkerboard,
        /// <summary>Random block distribution - partial merge potential.</summary>
        Random
    }

    /// <summary>
    /// Creates a greedy mesh benchmark for the specified complexity tier.
    /// </summary>
    /// <param name="tier">The complexity tier to benchmark.</param>
    public GreedyMeshThroughputBenchmark(ComplexityTier tier)
    {
        _tier = tier;
    }

    /// <inheritdoc/>
    public override string Name => $"GreedyMesh_{_tier}";

    /// <inheritdoc/>
    public override string Description => _tier switch
    {
        ComplexityTier.Uniform => "Greedy merge on solid chunk (maximum merge potential)",
        ComplexityTier.Checkerboard => "Greedy merge on checkerboard pattern (no merge potential)",
        ComplexityTier.Random => "Greedy merge on random blocks (partial merge potential)",
        _ => $"Greedy mesh throughput for {_tier} complexity tier"
    };

    /// <inheritdoc/>
    public override float BaselineMs => _tier switch
    {
        ComplexityTier.Uniform => 2.0f,      // ~500k faces/s baseline for uniform
        ComplexityTier.Checkerboard => 5.0f, // ~100k faces/s baseline for worst case
        ComplexityTier.Random => 3.5f,       // ~200k faces/s baseline for mixed
        _ => 3.0f
    };

    /// <inheritdoc/>
    public override int WarmupIterations => 2;

    /// <inheritdoc/>
    public override int MeasurementIterations => 5;

    /// <summary>
    /// Gets the complexity tier for this benchmark instance.
    /// </summary>
    public ComplexityTier Tier => _tier;

    /// <summary>
    /// Gets the number of faces expected/processed for the configured tier.
    /// Available after Setup() is called.
    /// </summary>
    public int ExpectedFaces => _expectedFaces;

    /// <inheritdoc/>
    protected override void Setup()
    {
        // Generate fixture based on complexity tier
        _fixture = _tier switch
        {
            ComplexityTier.Uniform => ChunkFixtureGenerator.Solid("game:rock-granite"),
            ComplexityTier.Checkerboard => ChunkFixtureGenerator.Checkerboard("game:rock-granite", "game:air"),
            ComplexityTier.Random => ChunkFixtureGenerator.Random(
                ["game:rock-granite", "game:soil-medium", "game:air", "game:sand-normal"],
                seed: 42),
            _ => ChunkFixtureGenerator.Solid("game:rock-granite")
        };

        // Pre-calculate expected faces for metrics
        _expectedFaces = CountExpectedFaces(_fixture);
    }

    /// <inheritdoc/>
    protected override void ExecuteWorkload()
    {
        if (_fixture is null) return;

        // Run greedy mesh algorithm simulation multiple iterations
        const int iterations = 100;
        int totalFacesMerged = 0;

        for (int iter = 0; iter < iterations; iter++)
        {
            totalFacesMerged += RunGreedyMerge(_fixture);
        }

        // Prevent optimization
        if (totalFacesMerged < 0) throw new InvalidOperationException();
    }

    /// <inheritdoc/>
    protected override void Cleanup()
    {
        _fixture = null;
    }

    /// <summary>
    /// Simulates greedy mesh merging algorithm on chunk fixture.
    /// Returns number of faces after merge (fewer = better compression).
    /// </summary>
    private static int RunGreedyMerge(ChunkFixture fixture)
    {
        // Greedy mesh algorithm simulation:
        // For each face direction (6 total), scan the 2D slice and merge adjacent faces
        // that have the same block type.

        int totalFaces = 0;

        // Process each of the 6 face directions
        foreach (var direction in new[] { FaceDir.PosX, FaceDir.NegX, FaceDir.PosY, FaceDir.NegY, FaceDir.PosZ, FaceDir.NegZ })
        {
            totalFaces += ProcessFaceDirection(fixture, direction);
        }

        return totalFaces;
    }

    /// <summary>
    /// Processes one face direction using greedy meshing.
    /// </summary>
    private static int ProcessFaceDirection(ChunkFixture fixture, FaceDir direction)
    {
        const int size = ChunkFixture.ChunkSize;
        int mergedFaces = 0;

        // For each slice perpendicular to the face direction
        for (int depth = 0; depth < size; depth++)
        {
            // Create visibility mask for this slice
            bool[,] mask = new bool[size, size];
            string?[,] blockTypes = new string[size, size];

            // Fill mask based on face direction
            for (int u = 0; u < size; u++)
            {
                for (int v = 0; v < size; v++)
                {
                    (int x, int y, int z) = GetCoordinates(direction, depth, u, v);
                    
                    var blockCode = GetBlockCodeAt(fixture, x, y, z);
                    bool isSolid = !string.Equals(blockCode, ChunkFixtureGenerator.AirBlockCode, 
                        StringComparison.OrdinalIgnoreCase);

                    // Check if this face is visible (neighbor in face direction is air or outside)
                    bool isVisible = isSolid && IsFaceVisible(fixture, direction, x, y, z);

                    mask[u, v] = isVisible;
                    blockTypes[u, v] = isVisible ? blockCode : null;
                }
            }

            // Greedy merge the mask
            mergedFaces += GreedyMergeMask(mask, blockTypes);
        }

        return mergedFaces;
    }

    /// <summary>
    /// Performs greedy merging on a 2D visibility mask.
    /// Returns count of merged quads generated.
    /// </summary>
    private static int GreedyMergeMask(bool[,] mask, string?[,] blockTypes)
    {
        const int size = ChunkFixture.ChunkSize;
        int quadCount = 0;

        for (int j = 0; j < size; j++)
        {
            for (int i = 0; i < size;)
            {
                if (!mask[i, j])
                {
                    i++;
                    continue;
                }

                string? blockType = blockTypes[i, j];

                // Find width (how far we can extend in i direction)
                int width = 1;
                while (i + width < size && mask[i + width, j] && 
                       string.Equals(blockTypes[i + width, j], blockType, StringComparison.Ordinal))
                {
                    width++;
                }

                // Find height (how far we can extend in j direction)
                int height = 1;
                bool done = false;
                while (j + height < size && !done)
                {
                    // Check if entire row matches
                    for (int k = 0; k < width; k++)
                    {
                        if (!mask[i + k, j + height] || 
                            !string.Equals(blockTypes[i + k, j + height], blockType, StringComparison.Ordinal))
                        {
                            done = true;
                            break;
                        }
                    }
                    if (!done) height++;
                }

                // Mark the merged region as processed
                for (int dj = 0; dj < height; dj++)
                {
                    for (int di = 0; di < width; di++)
                    {
                        mask[i + di, j + dj] = false;
                    }
                }

                quadCount++;
                i += width;
            }
        }

        return quadCount;
    }

    /// <summary>
    /// Gets coordinates based on face direction, depth, and UV coordinates.
    /// </summary>
    private static (int x, int y, int z) GetCoordinates(FaceDir direction, int depth, int u, int v)
    {
        return direction switch
        {
            FaceDir.PosX => (depth, v, u),
            FaceDir.NegX => (ChunkFixture.ChunkSize - 1 - depth, v, u),
            FaceDir.PosY => (u, depth, v),
            FaceDir.NegY => (u, ChunkFixture.ChunkSize - 1 - depth, v),
            FaceDir.PosZ => (u, v, depth),
            FaceDir.NegZ => (u, v, ChunkFixture.ChunkSize - 1 - depth),
            _ => (u, v, depth)
        };
    }

    /// <summary>
    /// Checks if a face is visible (neighbor block is air or outside chunk).
    /// </summary>
    private static bool IsFaceVisible(ChunkFixture fixture, FaceDir direction, int x, int y, int z)
    {
        const int size = ChunkFixture.ChunkSize;

        // Get neighbor position
        (int nx, int ny, int nz) = direction switch
        {
            FaceDir.PosX => (x + 1, y, z),
            FaceDir.NegX => (x - 1, y, z),
            FaceDir.PosY => (x, y + 1, z),
            FaceDir.NegY => (x, y - 1, z),
            FaceDir.PosZ => (x, y, z + 1),
            FaceDir.NegZ => (x, y, z - 1),
            _ => (x, y, z)
        };

        // Outside chunk bounds = visible
        if (nx < 0 || nx >= size || ny < 0 || ny >= size || nz < 0 || nz >= size)
        {
            return true;
        }

        // Air neighbor = visible
        var neighborCode = GetBlockCodeAt(fixture, nx, ny, nz);
        return string.Equals(neighborCode, ChunkFixtureGenerator.AirBlockCode, 
            StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Gets block code at position, with air as default.
    /// </summary>
    private static string GetBlockCodeAt(ChunkFixture fixture, int x, int y, int z)
    {
        int idx = ChunkFixture.ToIndex(x, y, z);
        return fixture.BlockCodes.TryGetValue(idx, out var code) ? code : ChunkFixtureGenerator.AirBlockCode;
    }

    /// <summary>
    /// Counts expected faces for the given fixture (before merging).
    /// </summary>
    private static int CountExpectedFaces(ChunkFixture fixture)
    {
        const int size = ChunkFixture.ChunkSize;
        int faces = 0;

        for (int y = 0; y < size; y++)
        {
            for (int z = 0; z < size; z++)
            {
                for (int x = 0; x < size; x++)
                {
                    var blockCode = GetBlockCodeAt(fixture, x, y, z);
                    bool isSolid = !string.Equals(blockCode, ChunkFixtureGenerator.AirBlockCode,
                        StringComparison.OrdinalIgnoreCase);

                    if (!isSolid) continue;

                    // Count visible faces for this solid block
                    foreach (var dir in new[] { FaceDir.PosX, FaceDir.NegX, FaceDir.PosY, FaceDir.NegY, FaceDir.PosZ, FaceDir.NegZ })
                    {
                        if (IsFaceVisible(fixture, dir, x, y, z))
                        {
                            faces++;
                        }
                    }
                }
            }
        }

        return faces;
    }

    private enum FaceDir { PosX, NegX, PosY, NegY, PosZ, NegZ }
}

/// <summary>
/// Factory for creating all greedy mesh benchmark instances.
/// </summary>
public static class GreedyMeshBenchmarks
{
    /// <summary>
    /// Creates benchmarks for all complexity tiers.
    /// </summary>
    public static IReadOnlyList<GreedyMeshThroughputBenchmark> CreateAll()
    {
        return
        [
            new GreedyMeshThroughputBenchmark(GreedyMeshThroughputBenchmark.ComplexityTier.Uniform),
            new GreedyMeshThroughputBenchmark(GreedyMeshThroughputBenchmark.ComplexityTier.Checkerboard),
            new GreedyMeshThroughputBenchmark(GreedyMeshThroughputBenchmark.ComplexityTier.Random)
        ];
    }
}
