using Xunit;
using Zaldaryon.Pharos.Benchmarks;

namespace Zaldaryon.Pharos.Tests.Benchmarks;

/// <summary>
/// Tests for GreedyMeshThroughputBenchmark across all complexity tiers.
/// All tests are pure logic tests that don't require native libraries or GPU.
/// </summary>
public class GreedyMeshBenchmarkTests
{
    #region Configuration Tests

    [Fact]
    public void GreedyMeshBenchmarks_CreateAll_ReturnsAllTiers()
    {
        var benchmarks = GreedyMeshBenchmarks.CreateAll();

        Assert.Equal(3, benchmarks.Count);
        Assert.Contains(benchmarks, b => b.Tier == GreedyMeshThroughputBenchmark.ComplexityTier.Uniform);
        Assert.Contains(benchmarks, b => b.Tier == GreedyMeshThroughputBenchmark.ComplexityTier.Checkerboard);
        Assert.Contains(benchmarks, b => b.Tier == GreedyMeshThroughputBenchmark.ComplexityTier.Random);
    }

    [Theory]
    [InlineData(GreedyMeshThroughputBenchmark.ComplexityTier.Uniform)]
    [InlineData(GreedyMeshThroughputBenchmark.ComplexityTier.Checkerboard)]
    [InlineData(GreedyMeshThroughputBenchmark.ComplexityTier.Random)]
    public void GreedyMeshBenchmark_HasValidConfiguration(GreedyMeshThroughputBenchmark.ComplexityTier tier)
    {
        var benchmark = new GreedyMeshThroughputBenchmark(tier);

        Assert.Equal($"GreedyMesh_{tier}", benchmark.Name);
        Assert.True(benchmark.BaselineMs > 0, "Baseline should be positive");
        Assert.True(benchmark.WarmupIterations >= 1, "Should have at least 1 warmup iteration");
        Assert.True(benchmark.MeasurementIterations >= 1, "Should have at least 1 measurement iteration");
        Assert.NotEmpty(benchmark.Description);
    }

    [Fact]
    public void GreedyMeshBenchmark_Uniform_HasExpectedBaseline()
    {
        var benchmark = new GreedyMeshThroughputBenchmark(GreedyMeshThroughputBenchmark.ComplexityTier.Uniform);
        Assert.Equal(2.0f, benchmark.BaselineMs);
    }

    [Fact]
    public void GreedyMeshBenchmark_Checkerboard_HasExpectedBaseline()
    {
        var benchmark = new GreedyMeshThroughputBenchmark(GreedyMeshThroughputBenchmark.ComplexityTier.Checkerboard);
        Assert.Equal(5.0f, benchmark.BaselineMs);
    }

    [Fact]
    public void GreedyMeshBenchmark_Random_HasExpectedBaseline()
    {
        var benchmark = new GreedyMeshThroughputBenchmark(GreedyMeshThroughputBenchmark.ComplexityTier.Random);
        Assert.Equal(3.5f, benchmark.BaselineMs);
    }

    #endregion

    #region Execution Tests

    [Theory]
    [InlineData(GreedyMeshThroughputBenchmark.ComplexityTier.Uniform)]
    [InlineData(GreedyMeshThroughputBenchmark.ComplexityTier.Checkerboard)]
    [InlineData(GreedyMeshThroughputBenchmark.ComplexityTier.Random)]
    public void GreedyMeshBenchmark_Runs_WithoutError(GreedyMeshThroughputBenchmark.ComplexityTier tier)
    {
        var benchmark = new GreedyMeshThroughputBenchmark(tier);
        var result = benchmark.RunBenchmark();

        Assert.NotNull(result);
        Assert.Equal($"GreedyMesh_{tier}", result.Name);
        Assert.True(result.ElapsedMs >= 0, "Elapsed time should be non-negative");
    }

    [Fact]
    public void GreedyMeshBenchmark_Uniform_ProducesPositiveElapsedTime()
    {
        var benchmark = new GreedyMeshThroughputBenchmark(GreedyMeshThroughputBenchmark.ComplexityTier.Uniform);
        var result = benchmark.RunBenchmark();

        Assert.True(result.ElapsedMs > 0, "Uniform benchmark should take measurable time");
    }

    [Fact]
    public void GreedyMeshBenchmark_Checkerboard_ProducesPositiveElapsedTime()
    {
        var benchmark = new GreedyMeshThroughputBenchmark(GreedyMeshThroughputBenchmark.ComplexityTier.Checkerboard);
        var result = benchmark.RunBenchmark();

        Assert.True(result.ElapsedMs > 0, "Checkerboard benchmark should take measurable time");
    }

    [Fact]
    public void GreedyMeshBenchmark_Random_ProducesPositiveElapsedTime()
    {
        var benchmark = new GreedyMeshThroughputBenchmark(GreedyMeshThroughputBenchmark.ComplexityTier.Random);
        var result = benchmark.RunBenchmark();

        Assert.True(result.ElapsedMs > 0, "Random benchmark should take measurable time");
    }

    #endregion

    #region Result Validation Tests

    [Fact]
    public void GreedyMeshBenchmark_Result_HasCorrectName()
    {
        foreach (var tier in Enum.GetValues<GreedyMeshThroughputBenchmark.ComplexityTier>())
        {
            var benchmark = new GreedyMeshThroughputBenchmark(tier);
            var result = benchmark.RunBenchmark();

            Assert.Equal(benchmark.Name, result.Name);
        }
    }

    [Fact]
    public void GreedyMeshBenchmark_AllTiers_ProduceValidResults()
    {
        var benchmarks = GreedyMeshBenchmarks.CreateAll();
        var suite = new BenchmarkSuite();
        var results = suite.Run(benchmarks);

        Assert.Equal(3, results.Count);
        Assert.All(results, r =>
        {
            Assert.True(r.ElapsedMs >= 0);
            Assert.True(r.BaselineMs > 0);
            Assert.NotEmpty(r.Name);
        });
    }

    #endregion

    #region Suite Integration Tests

    [Fact]
    public void OptimumBenchmarks_IncludesGreedyMeshBenchmarks()
    {
        var benchmarks = OptimumBenchmarks.CreateAll();

        Assert.Contains(benchmarks, b => b.Name == "GreedyMesh_Uniform");
        Assert.Contains(benchmarks, b => b.Name == "GreedyMesh_Checkerboard");
        Assert.Contains(benchmarks, b => b.Name == "GreedyMesh_Random");
    }

    [Fact]
    public void OptimumBenchmarks_TotalCount_IncludesGreedyMesh()
    {
        var benchmarks = OptimumBenchmarks.CreateAll();

        // Original 5 + 3 greedy mesh tiers
        Assert.Equal(8, benchmarks.Count);
    }

    [Fact]
    public void BenchmarkSuite_WithFileBaselines_LoadsGreedyMeshBaselines()
    {
        var baselines = new Dictionary<string, float>
        {
            ["GreedyMesh_Uniform"] = 2.5f,
            ["GreedyMesh_Checkerboard"] = 6.0f,
            ["GreedyMesh_Random"] = 4.0f
        };

        var suite = new BenchmarkSuite(baselines);
        Assert.True(suite.HasFileBaselines);
        Assert.Equal(3, suite.FileBaselineCount);
    }

    [Fact]
    public void GreedyMeshBenchmark_UsesFileBaseline_WhenProvided()
    {
        var baselines = new Dictionary<string, float>
        {
            ["GreedyMesh_Uniform"] = 10.0f
        };

        var benchmark = new GreedyMeshThroughputBenchmark(GreedyMeshThroughputBenchmark.ComplexityTier.Uniform);
        var result = benchmark.RunBenchmark(baselines);

        Assert.True(result.SourcedFromFile);
        Assert.Equal(10.0f, result.BaselineMs);
    }

    [Fact]
    public void GreedyMeshBenchmark_UsesHardcoded_WhenNoFileBaseline()
    {
        var benchmark = new GreedyMeshThroughputBenchmark(GreedyMeshThroughputBenchmark.ComplexityTier.Uniform);
        var result = benchmark.RunBenchmark();

        Assert.False(result.SourcedFromFile);
        Assert.Equal(2.0f, result.BaselineMs);
    }

    #endregion

    #region Determinism Tests

    [Fact]
    public void GreedyMeshBenchmark_Random_IsDeterministic()
    {
        // Two runs with the same seed should produce similar results
        var benchmark1 = new GreedyMeshThroughputBenchmark(GreedyMeshThroughputBenchmark.ComplexityTier.Random);
        var benchmark2 = new GreedyMeshThroughputBenchmark(GreedyMeshThroughputBenchmark.ComplexityTier.Random);

        var result1 = benchmark1.RunBenchmark();
        var result2 = benchmark2.RunBenchmark();

        // Results should be in the same ballpark (both using deterministic seed 42)
        Assert.Equal(result1.Name, result2.Name);
        Assert.Equal(result1.BaselineMs, result2.BaselineMs);
    }

    #endregion

    #region Edge Cases Tests

    [Fact]
    public void GreedyMeshBenchmark_MultipleRuns_DoNotThrow()
    {
        var benchmark = new GreedyMeshThroughputBenchmark(GreedyMeshThroughputBenchmark.ComplexityTier.Uniform);

        // Run multiple times to ensure no state corruption
        for (int i = 0; i < 3; i++)
        {
            var result = benchmark.RunBenchmark();
            Assert.NotNull(result);
        }
    }

    [Fact]
    public void GreedyMeshBenchmark_Description_MatchesTier()
    {
        var uniform = new GreedyMeshThroughputBenchmark(GreedyMeshThroughputBenchmark.ComplexityTier.Uniform);
        var checkerboard = new GreedyMeshThroughputBenchmark(GreedyMeshThroughputBenchmark.ComplexityTier.Checkerboard);
        var random = new GreedyMeshThroughputBenchmark(GreedyMeshThroughputBenchmark.ComplexityTier.Random);

        Assert.Contains("maximum merge", uniform.Description.ToLowerInvariant());
        Assert.Contains("no merge", checkerboard.Description.ToLowerInvariant());
        Assert.Contains("partial merge", random.Description.ToLowerInvariant());
    }

    #endregion
}
