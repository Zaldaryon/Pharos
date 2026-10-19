using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using Zaldaryon.Pharos.Reporting;
using Zaldaryon.Pharos.Scenarios;

namespace Zaldaryon.Pharos.Metrics;

/// <summary>Writes one test's metrics to <c>result.json</c>, next to its failure artifacts.</summary>
internal static class MetricsWriter
{
    internal const int SchemaVersion = 1;

    private static readonly JsonSerializerOptions s_json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>Writes the collector's metrics for <paramref name="test"/> and returns the file's path.</summary>
    /// <param name="test">The test the metrics belong to.</param>
    /// <param name="collector">The metrics to write.</param>
    /// <param name="started">When the test's body started.</param>
    /// <param name="duration">How long the body ran.</param>
    /// <param name="directory">The folder to write to; defaults to the test's own result folder.</param>
    /// <param name="runId">The run id; defaults to the process's.</param>
    internal static string Write(ScenarioTestInfo test, MetricsCollector collector, DateTimeOffset started,
        TimeSpan duration, string? directory = null, string? runId = null)
    {
        ArgumentNullException.ThrowIfNull(test);
        ArgumentNullException.ThrowIfNull(collector);

        directory ??= FailureArtifactWriter.CreateResultDirectory(test);
        Directory.CreateDirectory(directory);
        ScenarioAttemptState.Current?.SetMetricsDirectory(directory);
        string path = Path.Combine(directory, "result.json");

        IReadOnlyDictionary<string, MetricSeries> series = collector.Snapshot();
        Dictionary<string, object?> metrics = new(StringComparer.Ordinal);
        foreach ((string name, MetricSeries metric) in series)
        {
            Dictionary<string, object?> record = new()
            {
                ["unit"] = metric.UnitName,
                ["direction"] = metric.DirectionName,
                ["clock"] = metric.ClockName,
                ["samples"] = metric.Samples,
                ["count"] = metric.Count,
                ["sum"] = metric.Sum,
                ["min"] = metric.Min,
                ["max"] = metric.Max,
                ["mean"] = metric.Mean,
                ["p50"] = metric.P50,
                ["p95"] = metric.P95,
                ["p99"] = metric.P99,
                ["value"] = metric.Value,
            };
            if (metric.Tags.Count > 0) record["tags"] = metric.Tags;
            metrics[name] = record;
        }

        Dictionary<string, object?> document = new()
        {
            ["schemaVersion"] = SchemaVersion,
            ["test"] = test.DisplayName,
            ["class"] = test.TestClass.FullName,
            ["method"] = test.MethodName,
            ["runId"] = runId ?? FailureArtifactWriter.RunId,
            ["startedUtc"] = started.UtcDateTime.ToString("o", CultureInfo.InvariantCulture),
            ["durationMs"] = Math.Round(duration.TotalMilliseconds, 3),
            ["metrics"] = metrics,
        };

        string json = JsonSerializer.Serialize(document, s_json);
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, json);
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }

        return path;
    }
}
