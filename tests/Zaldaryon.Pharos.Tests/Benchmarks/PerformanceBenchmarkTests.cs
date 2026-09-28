using Zaldaryon.Pharos.Benchmarks;
using Xunit;

namespace Zaldaryon.Pharos.Tests.Benchmarks;

public sealed class PerformanceBenchmarkTests
{
    [Fact]
    public void RunBenchmark_RunsSetupWorkloadAndCleanupForWarmupsAndMeasurements()
    {
        var benchmark = new RecordingBenchmark(warmups: 2, measurements: 3);

        BenchmarkResult result = benchmark.RunBenchmark();

        Assert.Equal(5, benchmark.SetupCount);
        Assert.Equal(5, benchmark.WorkloadCount);
        Assert.Equal(5, benchmark.CleanupCount);
        Assert.Equal(
            new[] { "setup", "work", "cleanup", "setup", "work", "cleanup", "setup", "work", "cleanup", "setup", "work", "cleanup", "setup", "work", "cleanup" },
            benchmark.Events);
        Assert.Equal(3, result.TimingDistribution?.SampleCount);
    }

    [Theory]
    [InlineData(-1, 1, "WarmupIterations")]
    [InlineData(0, 0, "MeasurementIterations")]
    [InlineData(0, -1, "MeasurementIterations")]
    public void RunBenchmark_InvalidIterationCounts_ThrowBeforeRunning(
        int warmups,
        int measurements,
        string expectedParameter)
    {
        var benchmark = new RecordingBenchmark(warmups, measurements);

        ArgumentOutOfRangeException exception = Assert.Throws<ArgumentOutOfRangeException>(() => benchmark.RunBenchmark());

        Assert.Equal(expectedParameter, exception.ParamName);
        Assert.Empty(benchmark.Events);
    }

    [Fact]
    public void RunBenchmark_CleansUpWhenWorkloadThrows()
    {
        var benchmark = new RecordingBenchmark(warmups: 0, measurements: 1, throwDuringWorkload: true);

        Assert.Throws<InvalidOperationException>(() => benchmark.RunBenchmark());

        Assert.Equal(new[] { "setup", "work", "cleanup" }, benchmark.Events);
    }

    [Fact]
    public void RunBenchmark_CleansUpWhenSetupThrows()
    {
        var benchmark = new RecordingBenchmark(warmups: 0, measurements: 1, throwDuringSetup: true);

        Assert.Throws<InvalidOperationException>(() => benchmark.RunBenchmark());

        Assert.Equal(new[] { "setup", "cleanup" }, benchmark.Events);
        Assert.Equal(0, benchmark.WorkloadCount);
    }

    [Fact]
    public void BenchmarkResult_Create_ProvidesRawSamplesAndDistributionStatistics()
    {
        double[] samples = Enumerable.Range(1, 100).Select(value => (double)value).Reverse().ToArray();

        BenchmarkResult result = BenchmarkResult.Create(
            "Distribution",
            elapsedMs: 50.5f,
            baselineMs: 50.5f,
            sampleTimingsMs: samples);

        BenchmarkTimingSummary distribution = Assert.IsType<BenchmarkTimingSummary>(result.TimingDistribution);
        Assert.Equal(samples, distribution.SampleTimingsMs);
        Assert.Equal(100, distribution.SampleCount);
        Assert.Equal(1, distribution.MinMs);
        Assert.Equal(50.5, distribution.MedianMs);
        Assert.Equal(95, distribution.P95Ms);
        Assert.Equal(99, distribution.P99Ms);
        Assert.Equal(50.5, distribution.MeanMs);
        Assert.Equal(Math.Sqrt(833.25), distribution.StandardDeviationMs, precision: 10);
    }

    [Fact]
    public void BenchmarkResult_LegacyConstructorAndFactoryRemainCompatible()
    {
        var direct = new BenchmarkResult("Legacy", 10, 8, true, 25);
        BenchmarkResult created = BenchmarkResult.Create("Legacy", 10, 8);

        Assert.Equal(10, direct.ElapsedMs);
        Assert.Equal(10, created.ElapsedMs);
        Assert.Null(direct.TimingDistribution);
        Assert.Null(created.TimingDistribution);
        Assert.Equal("[PASS] Legacy: 10.00ms (baseline: 8.00ms, +25.0%)", direct.FormatSummary());
    }

    private sealed class RecordingBenchmark(
        int warmups,
        int measurements,
        bool throwDuringWorkload = false,
        bool throwDuringSetup = false) : PerformanceBenchmark
    {
        public override string Name => "Recording";
        public override string Description => "Records benchmark lifecycle calls.";
        public override float BaselineMs => 100;
        public override int WarmupIterations => warmups;
        public override int MeasurementIterations => measurements;

        public List<string> Events { get; } = [];
        public int SetupCount { get; private set; }
        public int WorkloadCount { get; private set; }
        public int CleanupCount { get; private set; }

        protected override void Setup()
        {
            Events.Add("setup");
            SetupCount++;
            if (throwDuringSetup)
            {
                throw new InvalidOperationException("Setup failed.");
            }
        }

        protected override void ExecuteWorkload()
        {
            Events.Add("work");
            WorkloadCount++;
            if (throwDuringWorkload)
            {
                throw new InvalidOperationException("Workload failed.");
            }
        }

        protected override void Cleanup()
        {
            Events.Add("cleanup");
            CleanupCount++;
        }
    }
}
