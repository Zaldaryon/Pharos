using Xunit;
using Zaldaryon.Pharos.Benchmarks;

namespace Zaldaryon.Pharos.Tests.Benchmarks;

/// <summary>
/// Tests for BfsThroughputBenchmark across all view distances.
/// All tests are pure logic tests that don't require native libraries or GPU.
/// </summary>
public class BfsThroughputBenchmarkTests
{
    #region Configuration Tests

    [Fact]
    public void BfsThroughputBenchmarks_CreateAll_ReturnsAllViewDistances()
    {
        var benchmarks = BfsThroughputBenchmarks.CreateAll();

        Assert.Equal(3, benchmarks.Count);
        Assert.Contains(benchmarks, b => b.ViewDistance == BfsThroughputBenchmark.ViewDistances.Low);
        Assert.Contains(benchmarks, b => b.ViewDistance == BfsThroughputBenchmark.ViewDistances.Medium);
        Assert.Contains(benchmarks, b => b.ViewDistance == BfsThroughputBenchmark.ViewDistances.High);
    }

    [Theory]
    [InlineData(BfsThroughputBenchmark.ViewDistances.Low)]
    [InlineData(BfsThroughputBenchmark.ViewDistances.Medium)]
    [InlineData(BfsThroughputBenchmark.ViewDistances.High)]
    public void BfsThroughputBenchmark_HasValidConfiguration(int viewDistance)
    {
        var benchmark = new BfsThroughputBenchmark(viewDistance);

        Assert.Contains("BfsThroughput", benchmark.Name);
        Assert.True(benchmark.BaselineMs > 0, "Baseline should be positive");
        Assert.True(benchmark.WarmupIterations >= 1, "Should have at least 1 warmup iteration");
        Assert.True(benchmark.MeasurementIterations >= 1, "Should have at least 1 measurement iteration");
        Assert.NotEmpty(benchmark.Description);
    }

    [Fact]
    public void BfsThroughputBenchmark_ViewDistance64_HasExpectedName()
    {
        var benchmark = new BfsThroughputBenchmark(BfsThroughputBenchmark.ViewDistances.Low);
        Assert.Equal("BfsThroughput_ViewDistance64", benchmark.Name);
    }

    [Fact]
    public void BfsThroughputBenchmark_ViewDistance128_HasExpectedName()
    {
        var benchmark = new BfsThroughputBenchmark(BfsThroughputBenchmark.ViewDistances.Medium);
        Assert.Equal("BfsThroughput_ViewDistance128", benchmark.Name);
    }

    [Fact]
    public void BfsThroughputBenchmark_ViewDistance256_HasExpectedName()
    {
        var benchmark = new BfsThroughputBenchmark(BfsThroughputBenchmark.ViewDistances.High);
        Assert.Equal("BfsThroughput_ViewDistance256", benchmark.Name);
    }

    [Fact]
    public void BfsThroughputBenchmark_ViewDistance64_HasExpectedBaseline()
    {
        var benchmark = new BfsThroughputBenchmark(BfsThroughputBenchmark.ViewDistances.Low);
        Assert.Equal(5.0f, benchmark.BaselineMs);
    }

    [Fact]
    public void BfsThroughputBenchmark_ViewDistance128_HasExpectedBaseline()
    {
        var benchmark = new BfsThroughputBenchmark(BfsThroughputBenchmark.ViewDistances.Medium);
        Assert.Equal(20.0f, benchmark.BaselineMs);
    }

    [Fact]
    public void BfsThroughputBenchmark_ViewDistance256_HasExpectedBaseline()
    {
        var benchmark = new BfsThroughputBenchmark(BfsThroughputBenchmark.ViewDistances.High);
        Assert.Equal(80.0f, benchmark.BaselineMs);
    }

    [Fact]
    public void BfsThroughputBenchmark_ThrowsOnInvalidViewDistance()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new BfsThroughputBenchmark(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new BfsThroughputBenchmark(-1));
    }

    #endregion

    #region Execution Tests

    [Theory]
    [InlineData(BfsThroughputBenchmark.ViewDistances.Low)]
    [InlineData(BfsThroughputBenchmark.ViewDistances.Medium)]
    [InlineData(BfsThroughputBenchmark.ViewDistances.High)]
    public void BfsThroughputBenchmark_Runs_WithoutError(int viewDistance)
    {
        var benchmark = new BfsThroughputBenchmark(viewDistance);
        var result = benchmark.RunBenchmark();

        Assert.NotNull(result);
        Assert.Contains("BfsThroughput", result.Name);
        Assert.True(result.ElapsedMs >= 0, "Elapsed time should be non-negative");
    }

    [Fact]
    public void BfsThroughputBenchmark_ViewDistance64_ProducesPositiveElapsedTime()
    {
        var benchmark = new BfsThroughputBenchmark(BfsThroughputBenchmark.ViewDistances.Low);
        var result = benchmark.RunBenchmark();

        Assert.True(result.ElapsedMs > 0, "ViewDistance64 benchmark should take measurable time");
    }

    [Fact]
    public void BfsThroughputBenchmark_ViewDistance128_ProducesPositiveElapsedTime()
    {
        var benchmark = new BfsThroughputBenchmark(BfsThroughputBenchmark.ViewDistances.Medium);
        var result = benchmark.RunBenchmark();

        Assert.True(result.ElapsedMs > 0, "ViewDistance128 benchmark should take measurable time");
    }

    [Fact]
    public void BfsThroughputBenchmark_ViewDistance256_ProducesPositiveElapsedTime()
    {
        var benchmark = new BfsThroughputBenchmark(BfsThroughputBenchmark.ViewDistances.High);
        var result = benchmark.RunBenchmark();

        Assert.True(result.ElapsedMs > 0, "ViewDistance256 benchmark should take measurable time");
    }

    #endregion

    #region Result Validation Tests

    [Fact]
    public void BfsThroughputBenchmark_Result_HasCorrectName()
    {
        foreach (var viewDistance in new[] { BfsThroughputBenchmark.ViewDistances.Low, 
            BfsThroughputBenchmark.ViewDistances.Medium, BfsThroughputBenchmark.ViewDistances.High })
        {
            var benchmark = new BfsThroughputBenchmark(viewDistance);
            var result = benchmark.RunBenchmark();

            Assert.Equal(benchmark.Name, result.Name);
        }
    }

    [Fact]
    public void BfsThroughputBenchmark_AllViewDistances_ProduceValidResults()
    {
        var benchmarks = BfsThroughputBenchmarks.CreateAll();
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
    public void OptimumBenchmarks_IncludesBfsThroughputBenchmarks()
    {
        var benchmarks = OptimumBenchmarks.CreateAll();

        Assert.Contains(benchmarks, b => b.Name == "BfsThroughput_ViewDistance64");
        Assert.Contains(benchmarks, b => b.Name == "BfsThroughput_ViewDistance128");
        Assert.Contains(benchmarks, b => b.Name == "BfsThroughput_ViewDistance256");
    }

    [Fact]
    public void OptimumBenchmarks_TotalCount_IncludesBfsThroughput()
    {
        var benchmarks = OptimumBenchmarks.CreateAll();

        // Original 5 + 3 greedy mesh tiers + 3 BFS view distances
        Assert.Equal(11, benchmarks.Count);
    }

    [Fact]
    public void BenchmarkSuite_WithFileBaselines_LoadsBfsThroughputBaselines()
    {
        var baselines = new Dictionary<string, float>
        {
            ["BfsThroughput_ViewDistance64"] = 6.0f,
            ["BfsThroughput_ViewDistance128"] = 25.0f,
            ["BfsThroughput_ViewDistance256"] = 100.0f
        };

        var suite = new BenchmarkSuite(baselines);
        Assert.True(suite.HasFileBaselines);
        Assert.Equal(3, suite.FileBaselineCount);
    }

    [Fact]
    public void BfsThroughputBenchmark_UsesFileBaseline_WhenProvided()
    {
        var baselines = new Dictionary<string, float>
        {
            ["BfsThroughput_ViewDistance64"] = 10.0f
        };

        var benchmark = new BfsThroughputBenchmark(BfsThroughputBenchmark.ViewDistances.Low);
        var result = benchmark.RunBenchmark(baselines);

        Assert.True(result.SourcedFromFile);
        Assert.Equal(10.0f, result.BaselineMs);
    }

    [Fact]
    public void BfsThroughputBenchmark_UsesHardcoded_WhenNoFileBaseline()
    {
        var benchmark = new BfsThroughputBenchmark(BfsThroughputBenchmark.ViewDistances.Low);
        var result = benchmark.RunBenchmark();

        Assert.False(result.SourcedFromFile);
        Assert.Equal(5.0f, result.BaselineMs);
    }

    #endregion

    #region ViewDistance Constants Tests

    [Fact]
    public void ViewDistances_Low_Is64()
    {
        Assert.Equal(64, BfsThroughputBenchmark.ViewDistances.Low);
    }

    [Fact]
    public void ViewDistances_Medium_Is128()
    {
        Assert.Equal(128, BfsThroughputBenchmark.ViewDistances.Medium);
    }

    [Fact]
    public void ViewDistances_High_Is256()
    {
        Assert.Equal(256, BfsThroughputBenchmark.ViewDistances.High);
    }

    #endregion

    #region Edge Cases Tests

    [Fact]
    public void BfsThroughputBenchmark_MultipleRuns_DoNotThrow()
    {
        var benchmark = new BfsThroughputBenchmark(BfsThroughputBenchmark.ViewDistances.Low);

        // Run multiple times to ensure no state corruption
        for (int i = 0; i < 3; i++)
        {
            var result = benchmark.RunBenchmark();
            Assert.NotNull(result);
        }
    }

    [Fact]
    public void BfsThroughputBenchmark_Description_MatchesViewDistance()
    {
        var low = new BfsThroughputBenchmark(BfsThroughputBenchmark.ViewDistances.Low);
        var medium = new BfsThroughputBenchmark(BfsThroughputBenchmark.ViewDistances.Medium);
        var high = new BfsThroughputBenchmark(BfsThroughputBenchmark.ViewDistances.High);

        Assert.Contains("64", low.Description);
        Assert.Contains("low", low.Description.ToLowerInvariant());
        Assert.Contains("128", medium.Description);
        Assert.Contains("medium", medium.Description.ToLowerInvariant());
        Assert.Contains("256", high.Description);
        Assert.Contains("high", high.Description.ToLowerInvariant());
    }

    [Fact]
    public void BfsThroughputBenchmark_CustomViewDistance_AcceptsIntermediateValue()
    {
        var benchmark = new BfsThroughputBenchmark(100);
        
        Assert.Equal(100, benchmark.ViewDistance);
        Assert.Contains("128", benchmark.Name); // Should round up to 128 tier
    }

    #endregion
}
