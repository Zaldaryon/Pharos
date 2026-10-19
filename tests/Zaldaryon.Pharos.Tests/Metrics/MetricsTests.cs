using System.Text.Json;
using Xunit;
using Zaldaryon.Pharos.Metrics;
using MetricsApi = Zaldaryon.Pharos.Metrics.Metrics;
using MetricsTimer = Zaldaryon.Pharos.Metrics.MetricTimer;
using Zaldaryon.Pharos.Reporting;
using Zaldaryon.Pharos.Scenarios;

namespace Zaldaryon.Pharos.Tests.Metrics;

public sealed class MetricsTests
{
    [Fact]
    public void Record_FormsOneSeriesWithStatistics()
    {
        MetricsCollector collector = new();
        collector.Record("forge.ticks", 10, MetricUnit.Count, MetricDirection.LowerIsBetter);
        collector.Record("forge.ticks", 20, MetricUnit.Count, MetricDirection.LowerIsBetter);

        MetricSeries series = collector.Snapshot()["forge.ticks"];
        Assert.Equal(2, series.Count);
        Assert.Equal(30, series.Sum);
        Assert.Equal(15, series.Mean);
        Assert.Equal(10, series.Min);
        Assert.Equal(20, series.Max);
        Assert.Equal(10, series.P50);
        Assert.Equal(20, series.P95);
        Assert.Equal(30, series.Value);
        Assert.Equal(MetricClock.Exact, series.Clock);
    }

    [Fact]
    public void Percentile_UsesNearestRank()
    {
        MetricsCollector collector = new();
        for (int i = 1; i <= 20; i++) collector.Record("frame", i, MetricUnit.Milliseconds);

        MetricSeries series = collector.Snapshot()["frame"];
        Assert.Equal(10.5, series.Mean);
        Assert.Equal(10, series.P50);
        Assert.Equal(19, series.P95);
        Assert.Equal(20, series.P99);
    }

    [Fact]
    public void Time_RecordsMillisecondsWhenDisposedOnce()
    {
        MetricsCollector collector = new();
        using (MetricsTimer timer = new(collector, "work", MetricDirection.LowerIsBetter, null))
        {
            Thread.Sleep(5);
        }

        MetricSeries series = collector.Snapshot()["work"];
        Assert.Single(series.Samples);
        Assert.True(series.Samples[0] >= 0);
        Assert.Equal(MetricUnit.Milliseconds, series.Unit);
        Assert.Equal(MetricClock.Stopwatch, series.Clock);
    }

    [Fact]
    public void Record_MillisecondsDefaultToTheStopwatchClock()
    {
        MetricsCollector collector = new();
        collector.Record("fill", 12, MetricUnit.Milliseconds);
        collector.Record("calls", 2);

        Assert.Equal(MetricClock.Stopwatch, collector.Snapshot()["fill"].Clock);
        Assert.Equal(MetricClock.Exact, collector.Snapshot()["calls"].Clock);
    }

    [Fact]
    public void Record_RejectsEmptyNamesAndNonFiniteValues()
    {
        MetricsCollector collector = new();
        Assert.Throws<ArgumentException>(() => collector.Record("", 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => collector.Record("x", double.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() => collector.Record("x", double.PositiveInfinity));
    }

    [Fact]
    public void OutsideScenario_TimeMeasuresAndRecordIsNoOp()
    {
        Assert.False(MetricsApi.IsActive);
        MetricsApi.Record("ignored", 1);
        using (MetricsApi.Time("ignored")) { }
        Assert.Equal(0, MetricsApi.Current?.SeriesCount ?? 0);
    }

    [Fact]
    public void Scopes_NestAndRestore()
    {
        MetricsCollector outer = new();
        MetricsCollector inner = new();
        using (MetricsApi.BeginScope(outer))
        {
            Assert.Same(outer, MetricsApi.Current);
            using (MetricsApi.BeginScope(inner))
            {
                Assert.Same(inner, MetricsApi.Current);
                MetricsApi.Record("inner", 1);
            }

            Assert.Same(outer, MetricsApi.Current);
            MetricsApi.Record("outer", 2);
        }

        Assert.Null(MetricsApi.Current);
        Assert.False(outer.Snapshot().ContainsKey("inner"));
        Assert.Single(outer.Snapshot()["outer"].Samples);
    }

    [Fact]
    public void CollectMetrics_ResolvesMethodClassAndAssemblyFlags()
    {
        Assert.Equal(Metric.Memory | Metric.Tick, CollectMetricsAttribute.Resolve(
            typeof(MetricsAttributeFixture), typeof(MetricsAttributeFixture).GetMethod(nameof(MetricsAttributeFixture.Body))));
    }

    [CollectMetrics(Metric.Memory)]
    private sealed class MetricsAttributeFixture
    {
        [CollectMetrics(Metric.Tick)]
        public void Body() { }
    }

    [Fact]
    public void Summary_SerialisesToCamelCase()
    {
        MetricsCollector collector = new();
        collector.Bytes("net.bytes", 42);
        MetricSummary summary = collector.Summaries()["net.bytes"];

        string json = JsonSerializer.Serialize(summary, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        });

        Assert.Contains("\"unit\":\"bytes\"", json);
        Assert.Contains("\"direction\":\"lowerIsBetter\"", json);
        Assert.Contains("\"value\":42", json);
    }
}

public sealed class MetricsWriterTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "pharos-metrics-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { }
    }

    [Fact]
    public void Write_UsesTheDirectoryTheFailureChose()
    {
        string? previous = FailureArtifactWriter.RootOverride;
        FailureArtifactWriter.RootOverride = _root;
        ScenarioAttemptState? previousState = ScenarioAttemptState.Current;
        ScenarioAttemptState state = new();
        ScenarioAttemptState.Current = state;
        try
        {
            ScenarioTestInfo test = new("MetricsTests.Metrics_Body", typeof(MetricsWriterTests), "Metrics_Body");
            string folder = Path.Combine(_root, "failure-folder");
            MetricsCollector collector = new();
            collector.Count("custom.count", 7);

            string path = MetricsWriter.Write(test, collector, DateTimeOffset.UtcNow, TimeSpan.FromMilliseconds(1),
                directory: folder);

            Assert.Equal(Path.Combine(folder, "result.json"), path);
            Assert.True(File.Exists(path), path);
            Assert.Equal(folder, state.MetricsDirectory);
        }
        finally
        {
            ScenarioAttemptState.Current = previousState;
            FailureArtifactWriter.RootOverride = previous;
        }
    }

    [Fact]
    public void Complete_WritesResultJsonForAPassedTest()
    {
        string? previous = FailureArtifactWriter.RootOverride;
        FailureArtifactWriter.RootOverride = _root;
        ScenarioAttemptState? previousState = ScenarioAttemptState.Current;
        ScenarioAttemptState state = new();
        ScenarioAttemptState.Current = state;
        try
        {
            ScenarioTestInfo test = new("MetricsTests.Metrics_Body", typeof(MetricsWriterTests), "Metrics_Body");
            ScenarioMetricsSession session = ScenarioMetricsSession.Start(test, client: null, server: null);
            MetricsApi.Record("custom.count", 7);
            using (MetricsApi.Time("custom.time")) { }
            session.Complete();

            string path = Path.Combine(_root, nameof(MetricsWriterTests), "Metrics_Body", "result.json");
            Assert.True(File.Exists(path), path);

            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
            JsonElement metrics = document.RootElement.GetProperty("metrics");
            Assert.Equal(7, metrics.GetProperty("custom.count").GetProperty("value").GetDouble());
            Assert.True(metrics.TryGetProperty("custom.time", out JsonElement time));
            Assert.Equal("ms", time.GetProperty("unit").GetString());
            Assert.Equal("stopwatch", time.GetProperty("clock").GetString());
            Assert.NotNull(state.Metrics);
            Assert.True(state.Metrics!.ContainsKey("custom.count"));
        }
        finally
        {
            ScenarioAttemptState.Current = previousState;
            FailureArtifactWriter.RootOverride = previous;
        }
    }

    [Fact]
    public void BridgeMetrics_ReachTheCollector()
    {
        MetricsCollector collector = new();
        using IDisposable subscription = BridgeMetricSubscription.Subscribe(collector);

        Zaldaryon.Pharos.Bridge.PharosMetrics.Count("mymod.forge.calls", 3, "lowerIsBetter");
        using (Zaldaryon.Pharos.Bridge.PharosMetrics.Time("mymod.forge.time")) { }

        Assert.Equal(3, collector.Snapshot()["mymod.forge.calls"].Value);
        Assert.Single(collector.Snapshot()["mymod.forge.time"].Samples);
        Assert.Equal(MetricUnit.Milliseconds, collector.Snapshot()["mymod.forge.time"].Unit);
        Assert.Equal(MetricClock.Stopwatch, collector.Snapshot()["mymod.forge.time"].Clock);
    }

    [Fact]
    public void AttemptManifest_IncludesTheMetricSummaries()
    {
        string? previous = Environment.GetEnvironmentVariable(ScenarioAttemptManifestWriter.DirectoryVariable);
        Environment.SetEnvironmentVariable(ScenarioAttemptManifestWriter.DirectoryVariable, _root);
        try
        {
            Dictionary<string, MetricSummary> metrics = new()
            {
                ["mymod.forge.calls"] = new MetricSummary
                {
                    Unit = "count",
                    Direction = "lowerIsBetter",
                    Clock = "exact",
                    Count = 1,
                    Value = 3,
                },
            };

            ScenarioAttemptManifestWriter.Write("Pharos.Tests", "Tests.Metrics", "Tests.Metrics",
                new ScenarioAttemptPolicy(1, 1), [], "Passed", false, metrics);

            string file = Assert.Single(Directory.GetFiles(_root, "*.json"));
            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(file));
            JsonElement summary = document.RootElement.GetProperty("metrics").GetProperty("mymod.forge.calls");
            Assert.Equal(3, summary.GetProperty("value").GetDouble());
            Assert.Equal("exact", summary.GetProperty("clock").GetString());
        }
        finally
        {
            Environment.SetEnvironmentVariable(ScenarioAttemptManifestWriter.DirectoryVariable, previous);
        }
    }
}
