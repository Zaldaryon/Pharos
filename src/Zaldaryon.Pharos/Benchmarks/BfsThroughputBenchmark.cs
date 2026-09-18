using Zaldaryon.Pharos.Culling;
using Zaldaryon.Pharos.Timing;

namespace Zaldaryon.Pharos.Benchmarks;

/// <summary>
/// Benchmarks BFS visibility traversal throughput at different view distances.
/// Uses BfsDebugHook to simulate chunk graph traversal without GPU access.
/// </summary>
public sealed class BfsThroughputBenchmark : PerformanceBenchmark
{
    private readonly int _viewDistance;
    private readonly string _tier;
    private BfsDebugHook? _hook;
    private ChunkPos _origin;
    private int _totalChunks;

    /// <summary>
    /// Standard view distances for benchmarking.
    /// </summary>
    public static class ViewDistances
    {
        /// <summary>Low view distance - 64 chunks radius.</summary>
        public const int Low = 64;
        /// <summary>Medium view distance - 128 chunks radius.</summary>
        public const int Medium = 128;
        /// <summary>High view distance - 256 chunks radius.</summary>
        public const int High = 256;
    }

    /// <summary>
    /// Creates a BFS throughput benchmark for the specified view distance.
    /// </summary>
    /// <param name="viewDistance">The maximum traversal depth (view distance in chunks).</param>
    public BfsThroughputBenchmark(int viewDistance)
    {
        if (viewDistance < 1)
            throw new ArgumentOutOfRangeException(nameof(viewDistance), "View distance must be positive");

        _viewDistance = viewDistance;
        _tier = viewDistance switch
        {
            <= 64 => "64",
            <= 128 => "128",
            _ => "256"
        };
    }

    /// <inheritdoc/>
    public override string Name => $"BfsThroughput_ViewDistance{_tier}";

    /// <inheritdoc/>
    public override string Description => _viewDistance switch
    {
        <= 64 => "BFS traversal throughput at view distance 64 (low)",
        <= 128 => "BFS traversal throughput at view distance 128 (medium)",
        _ => "BFS traversal throughput at view distance 256 (high)"
    };

    /// <inheritdoc/>
    public override float BaselineMs => _viewDistance switch
    {
        <= 64 => 5.0f,    // ~10k chunks, ~2M chunks/s baseline
        <= 128 => 20.0f,  // ~80k chunks, ~4M chunks/s baseline
        _ => 80.0f        // ~500k chunks, ~6M chunks/s baseline
    };

    /// <inheritdoc/>
    public override int WarmupIterations => 2;

    /// <inheritdoc/>
    public override int MeasurementIterations => 5;

    /// <summary>
    /// Gets the configured view distance for this benchmark.
    /// </summary>
    public int ViewDistance => _viewDistance;

    /// <summary>
    /// Gets the total number of chunks in the synthetic graph.
    /// Available after Setup() is called.
    /// </summary>
    public int TotalChunks => _totalChunks;

    /// <inheritdoc/>
    protected override void Setup()
    {
        // Create synthetic chunk graph centered at origin
        _origin = new ChunkPos(0, 0, 0);
        var chunks = GenerateChunkGraph(_viewDistance);
        _totalChunks = chunks.Count;

        // Create BFS hook with ~10% opaque chunks for realistic blocking
        var random = new Random(42);
        _hook = new BfsDebugHook(chunks, pos => random.NextDouble() < 0.1);
    }

    /// <inheritdoc/>
    protected override void ExecuteWorkload()
    {
        if (_hook is null) return;

        // Run BFS traversal multiple times
        const int iterations = 10;
        int totalVisited = 0;

        for (int iter = 0; iter < iterations; iter++)
        {
            var stats = _hook.Traverse(_origin, _viewDistance);
            totalVisited += stats.TotalNodesVisited;
        }

        // Prevent optimization
        if (totalVisited < 0) throw new InvalidOperationException();
    }

    /// <inheritdoc/>
    protected override void Cleanup()
    {
        _hook = null;
    }

    /// <summary>
    /// Generates a cubic chunk graph centered at origin with the given radius.
    /// </summary>
    private static HashSet<ChunkPos> GenerateChunkGraph(int radius)
    {
        var chunks = new HashSet<ChunkPos>();

        // Limit the actual radius to keep benchmarks reasonable
        // Use a spherical approximation within the cube to reduce chunk count
        int effectiveRadius = Math.Min(radius, 64);
        int radiusSq = effectiveRadius * effectiveRadius;

        for (int x = -effectiveRadius; x <= effectiveRadius; x++)
        {
            for (int y = -8; y <= 8; y++) // Vertical range is typically smaller
            {
                for (int z = -effectiveRadius; z <= effectiveRadius; z++)
                {
                    // Use spherical distance check for more realistic chunk distribution
                    int distSq = x * x + z * z; // Ignore Y for horizontal distance
                    if (distSq <= radiusSq)
                    {
                        chunks.Add(new ChunkPos(x, y, z));
                    }
                }
            }
        }

        return chunks;
    }
}

/// <summary>
/// Factory for creating all BFS throughput benchmark instances.
/// </summary>
public static class BfsThroughputBenchmarks
{
    /// <summary>
    /// Creates benchmarks for all standard view distances.
    /// </summary>
    public static IReadOnlyList<BfsThroughputBenchmark> CreateAll()
    {
        return
        [
            new BfsThroughputBenchmark(BfsThroughputBenchmark.ViewDistances.Low),
            new BfsThroughputBenchmark(BfsThroughputBenchmark.ViewDistances.Medium),
            new BfsThroughputBenchmark(BfsThroughputBenchmark.ViewDistances.High)
        ];
    }
}
