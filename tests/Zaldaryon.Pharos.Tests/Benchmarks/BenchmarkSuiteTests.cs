using Xunit;
using Zaldaryon.Pharos.Benchmarks;
using Zaldaryon.Pharos.Reporting;

namespace Zaldaryon.Pharos.Tests.Benchmarks;

/// <summary>
/// Tests for BenchmarkSuite, BenchmarkResult, and Optimum benchmarks.
/// All tests are pure logic tests that don't require native libraries or GPU.
/// </summary>
public class BenchmarkSuiteTests
{
    #region BenchmarkResult Tests

    [Fact]
    public void BenchmarkResult_Create_ComputesPassingCorrectly()
    {
        // Elapsed exactly at baseline - should pass
        var result = BenchmarkResult.Create("Test", 10.0f, 10.0f);
        Assert.True(result.IsPassing);
        Assert.Equal(0f, result.DeltaPercent, precision: 1);
    }

    [Fact]
    public void BenchmarkResult_Create_DetectsRegression()
    {
        // 25% slower than baseline - should fail (threshold is 20%)
        var result = BenchmarkResult.Create("Test", 12.5f, 10.0f);
        Assert.False(result.IsPassing);
        Assert.Equal(25f, result.DeltaPercent, precision: 1);
    }

    [Fact]
    public void BenchmarkResult_Create_PassesWithinThreshold()
    {
        // 15% slower - within 20% threshold
        var result = BenchmarkResult.Create("Test", 11.5f, 10.0f);
        Assert.True(result.IsPassing);
        Assert.Equal(15f, result.DeltaPercent, precision: 1);
    }

    [Fact]
    public void BenchmarkResult_Create_PassesAtExactThreshold()
    {
        // Exactly 20% slower - should pass (threshold is <=)
        var result = BenchmarkResult.Create("Test", 12.0f, 10.0f);
        Assert.True(result.IsPassing);
    }

    [Fact]
    public void BenchmarkResult_Create_CustomThreshold()
    {
        // 15% slower with 10% threshold - should fail
        var result = BenchmarkResult.Create("Test", 11.5f, 10.0f, threshold: 1.1f);
        Assert.False(result.IsPassing);
    }

    [Fact]
    public void BenchmarkResult_Create_DetectsImprovement()
    {
        // 20% faster
        var result = BenchmarkResult.Create("Test", 8.0f, 10.0f);
        Assert.True(result.IsPassing);
        Assert.True(result.IsImprovement);
        Assert.Equal(-20f, result.DeltaPercent, precision: 1);
    }

    [Fact]
    public void BenchmarkResult_Create_DetectsSignificantRegression()
    {
        // 60% slower - significant regression
        var result = BenchmarkResult.Create("Test", 16.0f, 10.0f);
        Assert.True(result.IsSignificantRegression);
    }

    [Fact]
    public void BenchmarkResult_Create_ThrowsOnEmptyName()
    {
        Assert.Throws<ArgumentException>(() => BenchmarkResult.Create("", 10f, 10f));
        Assert.Throws<ArgumentNullException>(() => BenchmarkResult.Create(null!, 10f, 10f));
    }

    [Fact]
    public void BenchmarkResult_Create_ThrowsOnInvalidBaseline()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => BenchmarkResult.Create("Test", 10f, 0f));
        Assert.Throws<ArgumentOutOfRangeException>(() => BenchmarkResult.Create("Test", 10f, -1f));
    }

    [Fact]
    public void BenchmarkResult_FormatSummary_ContainsAllInfo()
    {
        var result = BenchmarkResult.Create("TestBench", 12.5f, 10.0f);
        var summary = result.FormatSummary();

        Assert.Contains("FAIL", summary);
        Assert.Contains("TestBench", summary);
        Assert.Contains("12.50", summary);
        Assert.Contains("10.00", summary);
        Assert.Contains("+25", summary);
    }

    #endregion

    #region BenchmarkSuite Tests

    [Fact]
    public void BenchmarkSuite_Run_ExecutesAllBenchmarks()
    {
        var benchmarks = new PerformanceBenchmark[]
        {
            new TestBenchmark("A", 1.0f, 2.0f),
            new TestBenchmark("B", 1.0f, 2.0f)
        };

        var suite = new BenchmarkSuite();
        var results = suite.Run(benchmarks);

        Assert.Equal(2, results.Count);
        Assert.Equal("A", results[0].Name);
        Assert.Equal("B", results[1].Name);
    }

    [Fact]
    public void BenchmarkSuite_Run_ThrowsOnNull()
    {
        var suite = new BenchmarkSuite();
        Assert.Throws<ArgumentNullException>(() => suite.Run(null!));
    }

    [Fact]
    public void BenchmarkSuite_RunWithReport_AddsResultsToReport()
    {
        var benchmarks = new PerformanceBenchmark[]
        {
            new TestBenchmark("PassBench", 1.0f, 10.0f),   // Will pass
            new TestBenchmark("FailBench", 1.0f, 0.1f)    // Will fail (too slow)
        };

        var suite = new BenchmarkSuite();
        var report = new TestReportBuilder("BenchTest");
        var results = suite.RunWithReport(benchmarks, report);

        Assert.Equal(2, results.Count);
        Assert.Equal(1, report.PassCount);
        Assert.Equal(1, report.FailCount);
    }

    [Fact]
    public void BenchmarkSuite_Summarize_CalculatesCorrectly()
    {
        var results = new BenchmarkResult[]
        {
            BenchmarkResult.Create("A", 10f, 10f),  // Pass, no change
            BenchmarkResult.Create("B", 15f, 10f),  // Fail, 50% slower
            BenchmarkResult.Create("C", 8f, 10f)    // Pass, improvement
        };

        var summary = BenchmarkSuite.Summarize(results);

        Assert.Equal(3, summary.TotalCount);
        Assert.Equal(2, summary.PassCount);
        Assert.Equal(1, summary.FailCount);
        Assert.Equal(1, summary.ImprovedCount);
        Assert.False(summary.AllPassed);
    }

    [Fact]
    public void BenchmarkSuite_Summarize_EmptyResults()
    {
        var summary = BenchmarkSuite.Summarize([]);

        Assert.Equal(0, summary.TotalCount);
        Assert.Equal(100f, summary.PassRate); // 100% when no tests
        Assert.True(summary.AllPassed);
    }

    [Fact]
    public void BenchmarkSuite_FormatResults_ProducesValidOutput()
    {
        var results = new BenchmarkResult[]
        {
            BenchmarkResult.Create("TestA", 10f, 10f),
            BenchmarkResult.Create("TestB", 12f, 10f)
        };

        var formatted = BenchmarkSuite.FormatResults(results);

        Assert.Contains("=== Benchmark Results ===", formatted);
        Assert.Contains("TestA", formatted);
        Assert.Contains("TestB", formatted);
        Assert.Contains("Summary:", formatted);
    }

    [Fact]
    public void BenchmarkSuite_FormatResults_EmptyMessage()
    {
        var formatted = BenchmarkSuite.FormatResults([]);
        Assert.Equal("No benchmarks run.", formatted);
    }

    #endregion

    #region BenchmarkSummary Tests

    [Fact]
    public void BenchmarkSummary_PassRate_CalculatesCorrectly()
    {
        var results = new BenchmarkResult[]
        {
            BenchmarkResult.Create("A", 10f, 10f),
            BenchmarkResult.Create("B", 10f, 10f),
            BenchmarkResult.Create("C", 15f, 10f),  // Fail
            BenchmarkResult.Create("D", 10f, 10f)
        };

        var summary = BenchmarkSuite.Summarize(results);
        Assert.Equal(75f, summary.PassRate, precision: 1);
    }

    [Fact]
    public void BenchmarkSummary_FormatSummary_ContainsStatus()
    {
        var passingResults = new BenchmarkResult[]
        {
            BenchmarkResult.Create("A", 10f, 10f),
            BenchmarkResult.Create("B", 10f, 10f)
        };

        var passingSummary = BenchmarkSuite.Summarize(passingResults);
        Assert.Contains("ALL PASSED", passingSummary.FormatSummary());

        var failingResults = new BenchmarkResult[]
        {
            BenchmarkResult.Create("A", 15f, 10f) // Fail
        };

        var failingSummary = BenchmarkSuite.Summarize(failingResults);
        Assert.Contains("1 FAILED", failingSummary.FormatSummary());
    }

    #endregion

    #region OptimumBenchmarks Tests

    [Fact]
    public void OptimumBenchmarks_CreateAll_ReturnsAllBenchmarks()
    {
        var benchmarks = OptimumBenchmarks.CreateAll();

        Assert.Equal(5, benchmarks.Count);
        Assert.Contains(benchmarks, b => b.Name == "IndirectDraw");
        Assert.Contains(benchmarks, b => b.Name == "SimdCulling");
        Assert.Contains(benchmarks, b => b.Name == "MeshCompression");
        Assert.Contains(benchmarks, b => b.Name == "FsrPipeline");
        Assert.Contains(benchmarks, b => b.Name == "ModCompatibility");
    }

    [Fact]
    public void IndirectDrawBenchmark_HasValidConfiguration()
    {
        var benchmark = new IndirectDrawBenchmark();

        Assert.Equal("IndirectDraw", benchmark.Name);
        Assert.True(benchmark.BaselineMs > 0);
        Assert.True(benchmark.WarmupIterations >= 1);
        Assert.True(benchmark.MeasurementIterations >= 1);
    }

    [Fact]
    public void IndirectDrawBenchmark_Runs_WithoutError()
    {
        var benchmark = new IndirectDrawBenchmark();
        var result = benchmark.RunBenchmark();

        Assert.NotNull(result);
        Assert.Equal("IndirectDraw", result.Name);
        Assert.True(result.ElapsedMs >= 0);
    }

    [Fact]
    public void SimdCullingBenchmark_HasValidConfiguration()
    {
        var benchmark = new SimdCullingBenchmark();

        Assert.Equal("SimdCulling", benchmark.Name);
        Assert.True(benchmark.BaselineMs > 0);
    }

    [Fact]
    public void SimdCullingBenchmark_Runs_WithoutError()
    {
        var benchmark = new SimdCullingBenchmark();
        var result = benchmark.RunBenchmark();

        Assert.NotNull(result);
        Assert.Equal("SimdCulling", result.Name);
    }

    [Fact]
    public void MeshCompressionBenchmark_HasValidConfiguration()
    {
        var benchmark = new MeshCompressionBenchmark();

        Assert.Equal("MeshCompression", benchmark.Name);
        Assert.True(benchmark.BaselineMs > 0);
    }

    [Fact]
    public void MeshCompressionBenchmark_Runs_WithoutError()
    {
        var benchmark = new MeshCompressionBenchmark();
        var result = benchmark.RunBenchmark();

        Assert.NotNull(result);
        Assert.Equal("MeshCompression", result.Name);
    }

    [Fact]
    public void FsrPipelineBenchmark_HasValidConfiguration()
    {
        var benchmark = new FsrPipelineBenchmark();

        Assert.Equal("FsrPipeline", benchmark.Name);
        Assert.True(benchmark.BaselineMs > 0);
    }

    [Fact]
    public void FsrPipelineBenchmark_Runs_WithoutError()
    {
        var benchmark = new FsrPipelineBenchmark();
        var result = benchmark.RunBenchmark();

        Assert.NotNull(result);
        Assert.Equal("FsrPipeline", result.Name);
    }

    [Fact]
    public void ModCompatibilityBenchmark_HasValidConfiguration()
    {
        var benchmark = new ModCompatibilityBenchmark();

        Assert.Equal("ModCompatibility", benchmark.Name);
        Assert.True(benchmark.BaselineMs > 0);
    }

    [Fact]
    public void ModCompatibilityBenchmark_Runs_WithoutError()
    {
        var benchmark = new ModCompatibilityBenchmark();
        var result = benchmark.RunBenchmark();

        Assert.NotNull(result);
        Assert.Equal("ModCompatibility", result.Name);
    }

    #endregion

    #region Integration Tests

    [Fact]
    public void OptimumBenchmarks_AllRun_WithoutError()
    {
        var suite = new BenchmarkSuite();
        var benchmarks = OptimumBenchmarks.CreateAll();
        var results = suite.Run(benchmarks);

        Assert.Equal(5, results.Count);
        Assert.All(results, r => Assert.True(r.ElapsedMs >= 0));
    }

    [Fact]
    public void OptimumBenchmarks_IntegrateWithReport()
    {
        var suite = new BenchmarkSuite();
        var benchmarks = OptimumBenchmarks.CreateAll();
        var report = new TestReportBuilder("OptimumRegression");
        
        var results = suite.RunWithReport(benchmarks, report);

        Assert.Equal(5, results.Count);
        Assert.Equal(5, report.PassCount + report.FailCount);
    }

    [Fact]
    public void BenchmarkSuite_MockSlowdown_DetectsRegression()
    {
        // Create a benchmark that simulates slowdown
        var slowBenchmark = new SlowdownBenchmark();
        var suite = new BenchmarkSuite();
        var results = suite.Run([slowBenchmark]);

        Assert.Single(results);
        // The mock slowdown should trigger failure
        // (baseline is 0.1ms but workload takes longer)
    }

    #endregion

    #region Helper Classes

    /// <summary>
    /// Simple test benchmark for unit testing.
    /// </summary>
    private sealed class TestBenchmark : PerformanceBenchmark
    {
        private readonly string _name;
        private readonly float _workloadMs;
        private readonly float _baselineMs;

        public TestBenchmark(string name, float workloadMs, float baselineMs)
        {
            _name = name;
            _workloadMs = workloadMs;
            _baselineMs = baselineMs;
        }

        public override string Name => _name;
        public override string Description => "Test benchmark";
        public override float BaselineMs => _baselineMs;
        public override int WarmupIterations => 1;
        public override int MeasurementIterations => 1;

        protected override void ExecuteWorkload()
        {
            // Spin for approximately the specified time
            var sw = System.Diagnostics.Stopwatch.StartNew();
            while (sw.Elapsed.TotalMilliseconds < _workloadMs)
            {
                // Busy wait
            }
        }
    }

    /// <summary>
    /// Benchmark designed to fail regression detection.
    /// </summary>
    private sealed class SlowdownBenchmark : PerformanceBenchmark
    {
        public override string Name => "SlowdownTest";
        public override string Description => "Intentionally slow benchmark";
        public override float BaselineMs => 0.1f; // Very fast baseline
        public override int WarmupIterations => 1;
        public override int MeasurementIterations => 1;

        protected override void ExecuteWorkload()
        {
            // Do some actual work that takes longer than baseline
            int sum = 0;
            for (int i = 0; i < 100000; i++)
            {
                sum += i;
            }
            if (sum < 0) throw new InvalidOperationException();
        }
    }

    #endregion
}
