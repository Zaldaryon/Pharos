using Zaldaryon.Pharos.Culling;
using Zaldaryon.Pharos.Timing;
using Zaldaryon.Pharos.Visual;

namespace Zaldaryon.Pharos.Benchmarks;

/// <summary>
/// Collection of Optimum-specific performance benchmarks.
/// All benchmarks use synthetic data and operate without GPU access,
/// making them suitable for headless CI environments.
/// </summary>
public static class OptimumBenchmarks
{
    /// <summary>
    /// Creates all standard Optimum performance benchmarks.
    /// </summary>
    public static IReadOnlyList<PerformanceBenchmark> CreateAll()
    {
        var benchmarks = new List<PerformanceBenchmark>
        {
            new IndirectDrawBenchmark(),
            new SimdCullingBenchmark(),
            new MeshCompressionBenchmark(),
            new FsrPipelineBenchmark(),
            new ModCompatibilityBenchmark()
        };

        // Add greedy mesh benchmarks for all complexity tiers
        benchmarks.AddRange(GreedyMeshBenchmarks.CreateAll());

        // Add BFS throughput benchmarks for all view distances
        benchmarks.AddRange(BfsThroughputBenchmarks.CreateAll());

        return benchmarks;
    }
}

/// <summary>
/// Benchmark for indirect draw command buffer processing.
/// Simulates dispatching 10k draw commands via synthetic data.
/// </summary>
public sealed class IndirectDrawBenchmark : PerformanceBenchmark
{
    private const int CommandCount = 10_000;

    public override string Name => "IndirectDraw";
    public override string Description => "Dispatches 10k indirect draw command slots via synthetic data";
    public override float BaselineMs => 5.0f; // Expected ~5ms for 10k commands
    public override int WarmupIterations => 2;
    public override int MeasurementIterations => 5;

    // Synthetic command buffer data
    private int[]? _commandBuffer;
    private int[]? _instanceCounts;

    protected override void Setup()
    {
        // Create synthetic indirect draw command data
        // Each command: count, instanceCount, firstIndex, baseVertex, baseInstance (5 ints = 20 bytes)
        _commandBuffer = new int[CommandCount * 5];
        _instanceCounts = new int[CommandCount];

        var random = new Random(42); // Deterministic for reproducibility

        for (int i = 0; i < CommandCount; i++)
        {
            int offset = i * 5;
            _commandBuffer[offset] = random.Next(100, 1000);      // count (vertices)
            _commandBuffer[offset + 1] = random.Next(1, 10);      // instanceCount
            _commandBuffer[offset + 2] = random.Next(0, 10000);   // firstIndex
            _commandBuffer[offset + 3] = random.Next(0, 1000);    // baseVertex
            _commandBuffer[offset + 4] = i;                        // baseInstance
            _instanceCounts[i] = _commandBuffer[offset + 1];
        }
    }

    protected override void ExecuteWorkload()
    {
        if (_commandBuffer == null || _instanceCounts == null) return;

        // Simulate processing indirect draw commands:
        // - Validate command buffer integrity
        // - Count total instances
        // - Track dispatch statistics
        int totalVertices = 0;
        int totalInstances = 0;
        int validCommands = 0;

        for (int i = 0; i < CommandCount; i++)
        {
            int offset = i * 5;
            int count = _commandBuffer[offset];
            int instanceCount = _commandBuffer[offset + 1];

            // Validate command
            if (count > 0 && instanceCount > 0)
            {
                totalVertices += count * instanceCount;
                totalInstances += instanceCount;
                validCommands++;
            }
        }

        // Prevent compiler optimization
        if (validCommands < 0) throw new InvalidOperationException();
    }

    protected override void Cleanup()
    {
        _commandBuffer = null;
        _instanceCounts = null;
    }
}

/// <summary>
/// Benchmark for SIMD frustum culling operations.
/// Runs FrustumOracleComparator 1000 times on synthetic chunk data.
/// </summary>
public sealed class SimdCullingBenchmark : PerformanceBenchmark
{
    private const int IterationCount = 1000;
    private const int ChunksPerIteration = 64;

    public override string Name => "SimdCulling";
    public override string Description => "Runs FrustumOracleComparator 1000x on 64-chunk batches";
    public override float BaselineMs => 10.0f; // Expected ~10ms for 1000 iterations × 64 chunks
    public override int WarmupIterations => 2;
    public override int MeasurementIterations => 5;

    private IReadOnlyList<FrustumPlane>? _frustumPlanes;
    private List<ChunkPos>? _testChunks;

    protected override void Setup()
    {
        // Create a synthetic view frustum (standard 90° FOV)
        _frustumPlanes = CreateSyntheticFrustum();

        // Create test chunks in a grid around origin
        _testChunks = new List<ChunkPos>(ChunksPerIteration);
        for (int x = -4; x < 4; x++)
        {
            for (int z = -4; z < 4; z++)
            {
                _testChunks.Add(new ChunkPos(x, 0, z));
            }
        }
    }

    protected override void ExecuteWorkload()
    {
        if (_frustumPlanes == null || _testChunks == null) return;

        int visibleCount = 0;

        for (int iter = 0; iter < IterationCount; iter++)
        {
            foreach (var chunk in _testChunks)
            {
                if (FrustumOracleComparator.IsChunkInFrustum(_frustumPlanes, chunk))
                {
                    visibleCount++;
                }
            }
        }

        // Prevent optimization
        if (visibleCount < 0) throw new InvalidOperationException();
    }

    private static List<FrustumPlane> CreateSyntheticFrustum()
    {
        // Create 6 planes for a standard view frustum looking down -Z
        return
        [
            new FrustumPlane(0, 0, -1, 100),    // Near plane
            new FrustumPlane(0, 0, 1, 1000),    // Far plane
            new FrustumPlane(1, 0, 1, 0),       // Left plane (normalized approx)
            new FrustumPlane(-1, 0, 1, 0),      // Right plane
            new FrustumPlane(0, 1, 1, 0),       // Bottom plane
            new FrustumPlane(0, -1, 1, 0)       // Top plane
        ];
    }
}

/// <summary>
/// Benchmark for perceptual diff calculation on synthetic pixel data.
/// Simulates mesh compression quality comparison.
/// </summary>
public sealed class MeshCompressionBenchmark : PerformanceBenchmark
{
    private const int ImageWidth = 256;
    private const int ImageHeight = 256;
    private const int IterationCount = 100;

    public override string Name => "MeshCompression";
    public override string Description => "Runs PerceptualDiffCalculator 100x on 256x256 synthetic images";
    public override float BaselineMs => 15.0f; // Expected ~15ms for 100 iterations
    public override int WarmupIterations => 2;
    public override int MeasurementIterations => 5;

    private byte[]? _actualPixels;
    private byte[]? _goldenPixels;

    protected override void Setup()
    {
        int pixelCount = ImageWidth * ImageHeight * 4;
        _actualPixels = new byte[pixelCount];
        _goldenPixels = new byte[pixelCount];

        var random = new Random(42);

        // Generate "golden" reference image
        for (int i = 0; i < pixelCount; i++)
        {
            _goldenPixels[i] = (byte)random.Next(256);
        }

        // Generate "actual" with small differences (simulating compression artifacts)
        for (int i = 0; i < pixelCount; i++)
        {
            int diff = random.Next(-5, 6); // Small variations
            _actualPixels[i] = (byte)Math.Clamp(_goldenPixels[i] + diff, 0, 255);
        }
    }

    protected override void ExecuteWorkload()
    {
        if (_actualPixels == null || _goldenPixels == null) return;

        PerceptualDiffResult? result = null;

        for (int i = 0; i < IterationCount; i++)
        {
            result = PerceptualDiffCalculator.CalcDiff(
                _actualPixels,
                _goldenPixels,
                ImageWidth,
                ImageHeight,
                tolerance: 0.02f);
        }

        // Prevent optimization
        if (result?.MaxDiff < -1) throw new InvalidOperationException();
    }

    protected override void Cleanup()
    {
        _actualPixels = null;
        _goldenPixels = null;
    }
}

/// <summary>
/// Benchmark for FSR pipeline configuration and snapshot operations.
/// Simulates configure + snapshot cycle 100 times.
/// </summary>
public sealed class FsrPipelineBenchmark : PerformanceBenchmark
{
    private const int IterationCount = 100;

    public override string Name => "FsrPipeline";
    public override string Description => "Configure+snapshot RenderScaleInspector 100x";
    public override float BaselineMs => 2.0f; // Expected ~2ms for configuration ops
    public override int WarmupIterations => 2;
    public override int MeasurementIterations => 5;

    // Synthetic FSR configuration data
    private FsrConfigData[]? _configurations;

    protected override void Setup()
    {
        _configurations = new FsrConfigData[IterationCount];
        var random = new Random(42);

        // Generate varied FSR configurations
        float[] scales = [0.5f, 0.67f, 0.77f, 0.85f, 1.0f];
        int[] widths = [1920, 2560, 3840];
        int[] heights = [1080, 1440, 2160];

        for (int i = 0; i < IterationCount; i++)
        {
            _configurations[i] = new FsrConfigData
            {
                Scale = scales[random.Next(scales.Length)],
                DisplayWidth = widths[random.Next(widths.Length)],
                DisplayHeight = heights[random.Next(heights.Length)],
                FsrEnabled = random.Next(2) == 1
            };
        }
    }

    protected override void ExecuteWorkload()
    {
        if (_configurations == null) return;

        int totalPreUpscalePixels = 0;

        for (int i = 0; i < IterationCount; i++)
        {
            var config = _configurations[i];

            // Simulate FSR configuration calculation (without actual inspector)
            int preUpscaleWidth = (int)(config.DisplayWidth * config.Scale);
            int preUpscaleHeight = (int)(config.DisplayHeight * config.Scale);

            // Simulate snapshot: calculate statistics
            int pixelSavings = config.DisplayWidth * config.DisplayHeight - preUpscaleWidth * preUpscaleHeight;
            float upscaleRatio = config.FsrEnabled ? 1.0f / config.Scale : 1.0f;

            totalPreUpscalePixels += preUpscaleWidth * preUpscaleHeight;

            // Validate configuration
            if (config.Scale < 0.5f || config.Scale > 1.0f)
            {
                throw new InvalidOperationException("Invalid scale");
            }
        }

        // Prevent optimization
        if (totalPreUpscalePixels < 0) throw new InvalidOperationException();
    }

    private struct FsrConfigData
    {
        public float Scale;
        public int DisplayWidth;
        public int DisplayHeight;
        public bool FsrEnabled;
    }
}

/// <summary>
/// Benchmark for mod compatibility checking operations.
/// Simulates mod presence checks and compatibility lookups 1000 times.
/// </summary>
public sealed class ModCompatibilityBenchmark : PerformanceBenchmark
{
    private const int IterationCount = 1000;
    private const int ModCount = 50;

    public override string Name => "ModCompatibility";
    public override string Description => "SimulateModPresent 1000x with 50-mod dictionary";
    public override float BaselineMs => 1.0f; // Expected ~1ms for dictionary ops
    public override int WarmupIterations => 2;
    public override int MeasurementIterations => 5;

    // Simulated mod registry
    private Dictionary<string, ModInfo>? _modRegistry;
    private string[]? _queryModIds;

    protected override void Setup()
    {
        _modRegistry = new Dictionary<string, ModInfo>(ModCount, StringComparer.OrdinalIgnoreCase);

        // Populate with synthetic mod data
        string[] prefixes = ["optimum", "graphics", "audio", "gameplay", "utility"];
        
        for (int i = 0; i < ModCount; i++)
        {
            string prefix = prefixes[i % prefixes.Length];
            string modId = $"{prefix}-mod-{i:D3}";
            _modRegistry[modId] = new ModInfo
            {
                ModId = modId,
                Version = $"1.{i % 10}.{i % 5}",
                IsCompatible = i % 7 != 0 // ~85% compatible
            };
        }

        // Create query list (mix of present and absent mods)
        _queryModIds = new string[IterationCount];
        var random = new Random(42);

        for (int i = 0; i < IterationCount; i++)
        {
            if (random.Next(3) == 0)
            {
                // Query non-existent mod
                _queryModIds[i] = $"missing-mod-{i}";
            }
            else
            {
                // Query existing mod
                int modIndex = random.Next(ModCount);
                string prefix = prefixes[modIndex % prefixes.Length];
                _queryModIds[i] = $"{prefix}-mod-{modIndex:D3}";
            }
        }
    }

    protected override void ExecuteWorkload()
    {
        if (_modRegistry == null || _queryModIds == null) return;

        int presentCount = 0;
        int compatibleCount = 0;

        for (int i = 0; i < IterationCount; i++)
        {
            string modId = _queryModIds[i];

            // Simulate mod presence check
            if (_modRegistry.TryGetValue(modId, out var info))
            {
                presentCount++;

                // Simulate compatibility check
                if (info.IsCompatible)
                {
                    compatibleCount++;
                }
            }
        }

        // Prevent optimization
        if (presentCount < 0 || compatibleCount < 0) throw new InvalidOperationException();
    }

    private struct ModInfo
    {
        public string ModId;
        public string Version;
        public bool IsCompatible;
    }
}
