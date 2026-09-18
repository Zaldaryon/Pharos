using System.Diagnostics;
using Zaldaryon.Pharos.Timing;

namespace Zaldaryon.Pharos.Server;

/// <summary>
/// Harness for comparing world generation output between two configurations.
/// Supports both synthetic in-memory comparison and live server comparison modes.
/// Designed to detect non-determinism in parallel vs serial worldgen execution.
/// </summary>
public sealed class WorldgenParityHarness
{
    private readonly Dictionary<ChunkPos, int[]> _serialData = new();
    private readonly Dictionary<ChunkPos, int[]> _parallelData = new();

    /// <summary>
    /// Gets the count of chunks in the serial dataset.
    /// </summary>
    public int SerialChunkCount => _serialData.Count;

    /// <summary>
    /// Gets the count of chunks in the parallel dataset.
    /// </summary>
    public int ParallelChunkCount => _parallelData.Count;

    /// <summary>
    /// Registers block data for a chunk in the serial configuration.
    /// </summary>
    /// <param name="pos">Chunk position.</param>
    /// <param name="blockData">Block ID array for the chunk (length should be 32^3 = 32768 for standard chunks).</param>
    public void AddSerialChunk(ChunkPos pos, int[] blockData)
    {
        ArgumentNullException.ThrowIfNull(blockData);
        _serialData[pos] = blockData;
    }

    /// <summary>
    /// Registers block data for a chunk in the parallel configuration.
    /// </summary>
    /// <param name="pos">Chunk position.</param>
    /// <param name="blockData">Block ID array for the chunk.</param>
    public void AddParallelChunk(ChunkPos pos, int[] blockData)
    {
        ArgumentNullException.ThrowIfNull(blockData);
        _parallelData[pos] = blockData;
    }

    /// <summary>
    /// Clears all registered chunk data from both configurations.
    /// </summary>
    public void Clear()
    {
        _serialData.Clear();
        _parallelData.Clear();
    }

    /// <summary>
    /// Compares all chunks in both datasets and returns a parity result.
    /// Only compares chunks that exist in both serial and parallel datasets.
    /// </summary>
    /// <returns>WorldgenParityResult with mismatch details.</returns>
    public WorldgenParityResult CompareWorldgen()
    {
        Stopwatch sw = Stopwatch.StartNew();
        List<ChunkPos> mismatches = [];

        // Find common chunks
        HashSet<ChunkPos> commonChunks = new(_serialData.Keys);
        commonChunks.IntersectWith(_parallelData.Keys);

        foreach (ChunkPos pos in commonChunks)
        {
            int[] serial = _serialData[pos];
            int[] parallel = _parallelData[pos];

            if (!BlockDataEqual(serial, parallel))
            {
                mismatches.Add(pos);
            }
        }

        sw.Stop();
        return new WorldgenParityResult(commonChunks.Count, mismatches, sw.Elapsed.TotalMilliseconds);
    }

    /// <summary>
    /// Compares world generation using the provided seed and worker configurations.
    /// This is a simplified entry point that delegates to the internal comparison logic.
    /// </summary>
    /// <param name="seed">World seed for generation.</param>
    /// <param name="serialWorkerCount">Number of workers for serial configuration (typically 1).</param>
    /// <param name="parallelWorkerCount">Number of workers for parallel configuration.</param>
    /// <param name="chunkRadius">Radius of chunks to compare around origin.</param>
    /// <returns>WorldgenParityResult from comparing the registered data.</returns>
    /// <remarks>
    /// In the current implementation, this method uses pre-registered synthetic data.
    /// For live server comparison, use AddSerialChunk/AddParallelChunk to populate data
    /// from actual worldgen runs before calling CompareWorldgen().
    /// </remarks>
    public WorldgenParityResult CompareWorldgen(int seed, int serialWorkerCount, int parallelWorkerCount, int chunkRadius)
    {
        // Store parameters for potential future use or logging
        _ = seed;
        _ = serialWorkerCount;
        _ = parallelWorkerCount;
        _ = chunkRadius;

        return CompareWorldgen();
    }

    /// <summary>
    /// Generates synthetic identical chunk data for testing the harness itself.
    /// Creates matching data in both serial and parallel datasets.
    /// </summary>
    /// <param name="chunkRadius">Radius of chunks to generate around origin.</param>
    /// <param name="seed">Seed for generating deterministic block patterns.</param>
    public void GenerateSyntheticIdenticalData(int chunkRadius, int seed = 12345)
    {
        Clear();
        Random rng = new(seed);

        for (int x = -chunkRadius; x <= chunkRadius; x++)
        {
            for (int y = 0; y < 8; y++) // 8 vertical chunks
            {
                for (int z = -chunkRadius; z <= chunkRadius; z++)
                {
                    ChunkPos pos = new(x, y, z);
                    int[] blockData = GenerateChunkData(rng);
                    
                    // Same data for both configurations
                    _serialData[pos] = blockData;
                    _parallelData[pos] = (int[])blockData.Clone();
                }
            }
        }
    }

    /// <summary>
    /// Generates synthetic data with intentional mismatches for testing.
    /// </summary>
    /// <param name="chunkRadius">Radius of chunks to generate around origin.</param>
    /// <param name="mismatchPositions">Specific positions to introduce mismatches at.</param>
    /// <param name="seed">Seed for generating deterministic block patterns.</param>
    public void GenerateSyntheticMismatchedData(int chunkRadius, IEnumerable<ChunkPos> mismatchPositions, int seed = 12345)
    {
        Clear();
        Random rng = new(seed);
        HashSet<ChunkPos> mismatchSet = new(mismatchPositions);

        for (int x = -chunkRadius; x <= chunkRadius; x++)
        {
            for (int y = 0; y < 8; y++)
            {
                for (int z = -chunkRadius; z <= chunkRadius; z++)
                {
                    ChunkPos pos = new(x, y, z);
                    int[] serialData = GenerateChunkData(rng);
                    int[] parallelData;

                    if (mismatchSet.Contains(pos))
                    {
                        // Introduce mismatch by modifying some blocks
                        parallelData = (int[])serialData.Clone();
                        parallelData[0] = serialData[0] + 1;
                        parallelData[100] = serialData[100] ^ 0xFF;
                    }
                    else
                    {
                        parallelData = (int[])serialData.Clone();
                    }

                    _serialData[pos] = serialData;
                    _parallelData[pos] = parallelData;
                }
            }
        }
    }

    /// <summary>
    /// Gets block data difference details for a specific chunk.
    /// Useful for debugging what blocks differ between configurations.
    /// </summary>
    /// <param name="pos">Chunk position to analyze.</param>
    /// <returns>List of (index, serialValue, parallelValue) tuples for differing blocks.</returns>
    public IReadOnlyList<(int Index, int SerialValue, int ParallelValue)> GetBlockDifferences(ChunkPos pos)
    {
        if (!_serialData.TryGetValue(pos, out int[]? serial) ||
            !_parallelData.TryGetValue(pos, out int[]? parallel))
        {
            return [];
        }

        List<(int, int, int)> diffs = [];
        int len = Math.Min(serial.Length, parallel.Length);

        for (int i = 0; i < len; i++)
        {
            if (serial[i] != parallel[i])
            {
                diffs.Add((i, serial[i], parallel[i]));
            }
        }

        // Check length difference
        if (serial.Length != parallel.Length)
        {
            diffs.Add((-1, serial.Length, parallel.Length));
        }

        return diffs;
    }

    private static bool BlockDataEqual(int[] a, int[] b)
    {
        if (a.Length != b.Length) return false;
        
        for (int i = 0; i < a.Length; i++)
        {
            if (a[i] != b[i]) return false;
        }
        
        return true;
    }

    private static int[] GenerateChunkData(Random rng)
    {
        const int chunkSize = 32 * 32 * 32; // Standard chunk dimensions
        int[] data = new int[chunkSize];
        
        // Generate simple terrain pattern
        for (int i = 0; i < chunkSize; i++)
        {
            // Mix of air (0), stone (1), and dirt (2)
            data[i] = rng.Next(0, 3);
        }
        
        return data;
    }
}
