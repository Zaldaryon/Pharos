using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Zaldaryon.Pharos.Statistics;

namespace Zaldaryon.Pharos.Cli.ParallelRuns.Compare;

/// <summary>What <c>pharos compare</c> writes as JSON. Version 1 of the format.</summary>
/// <param name="SchemaVersion">The format version.</param>
/// <param name="Baseline">What the baseline was.</param>
/// <param name="Candidate">What the candidate was.</param>
/// <param name="BaselineRuns">How many baseline runs were read.</param>
/// <param name="CandidateRuns">How many candidate runs were read.</param>
/// <param name="Alpha">The significance level used.</param>
/// <param name="MinEffect">The minimum relative change reported.</param>
/// <param name="OverBudget">The metrics past their budget.</param>
/// <param name="ExitCode">The code the command exits with.</param>
/// <param name="Metrics">Every metric comparison.</param>
/// <param name="NewFailures">Tests failing on the candidate side only.</param>
/// <param name="Fixed">Tests failing on the baseline side only.</param>
/// <param name="StillFailing">Tests failing on both sides.</param>
/// <param name="Added">Tests that only exist on the candidate side.</param>
/// <param name="Vanished">Tests that only exist on the baseline side.</param>
/// <param name="ChangedTests">Every test whose outcome differs.</param>
internal sealed record CompareReport(
    int SchemaVersion,
    string Baseline,
    string Candidate,
    int BaselineRuns,
    int CandidateRuns,
    double Alpha,
    double MinEffect,
    IReadOnlyList<MetricComparison> OverBudget,
    int ExitCode,
    IReadOnlyList<MetricComparison> Metrics,
    IReadOnlyList<TestChange> NewFailures,
    IReadOnlyList<TestChange> Fixed,
    IReadOnlyList<TestChange> StillFailing,
    IReadOnlyList<TestChange> Added,
    IReadOnlyList<TestChange> Vanished,
    IReadOnlyList<TestChange> ChangedTests);

/// <summary>Renders a comparison as text, Markdown or JSON.</summary>
internal static class ComparisonReport
{
    /// <summary>Writes the human-readable report.</summary>
    /// <param name="result">The comparison.</param>
    /// <param name="options">The analysis options, for the header.</param>
    /// <param name="output">Where to write.</param>
    public static void WriteConsole(ComparisonResult result, CompareAnalysisOptions options, TextWriter output)
    {
        output.WriteLine($"{result.BaselineLabel} ({result.BaselineRuns} run(s)) -> {result.CandidateLabel} ({result.CandidateRuns} run(s))");
        WriteMetrics(result, output);

        Section(output, "New failures", result.Behaviour.NewFailures, change => change.Test + Detail(change.Message));
        Section(output, "Fixed", result.Behaviour.Fixed, change => change.Test);
        Section(output, "Still failing", result.Behaviour.StillFailing, change => change.Test);
        Section(output, "Tests only on the candidate", result.Behaviour.Added, change => change.Test);
        Section(output, "Tests only on the baseline", result.Behaviour.Vanished, change => change.Test);

        output.WriteLine();
        if (result.OverBudget.Count > 0)
        {
            output.WriteLine($"Over budget: {string.Join(", ", result.OverBudget.Select(metric => $"{metric.Name} ({metric.Budget})"))}.");
        }
        else if (result.Clean)
        {
            output.WriteLine("No metric regressed and no outcome changed.");
        }
        else
        {
            output.WriteLine("No metric is over budget.");
        }
    }

    /// <summary>
    /// Writes only the metric lines and their summary, for a caller that reports behaviour on its own
    /// (as <c>pharos diff</c> does).
    /// </summary>
    /// <param name="result">The comparison.</param>
    /// <param name="output">Where to write.</param>
    public static void WriteMetrics(ComparisonResult result, TextWriter output)
    {
        output.WriteLine();

        if (result.Metrics.Count == 0)
        {
            output.WriteLine("No metrics were recorded on either side. Add [CollectMetrics] to the tests, or record with Metrics.Record.");
            return;
        }

        int width = result.Metrics.Max(metric => (metric.Name + Suffix(metric)).Length);
        foreach (MetricComparison metric in result.Metrics)
        {
            output.WriteLine($"  {(metric.Name + Suffix(metric)).PadRight(width)}  {Value(metric.Baseline)} -> {Value(metric.Candidate)}  {Delta(metric),8}  {Interval(metric),-22}  {Verdict(metric)}");
        }
        output.WriteLine();
        output.WriteLine($"Metrics: {result.Metrics.Count} compared, {result.Metrics.Count(metric => metric.Verdict == MetricVerdict.Improved)} improved, "
            + $"{result.Metrics.Count(metric => metric.Verdict == MetricVerdict.Regressed)} regressed, "
            + $"{result.Metrics.Count(metric => metric.Verdict is MetricVerdict.Added or MetricVerdict.Removed)} added or removed, "
            + $"{result.Metrics.Count(metric => metric.Verdict == MetricVerdict.Inconclusive)} inconclusive.");
    }

    /// <summary>The Markdown table, for a pull request or <c>$GITHUB_STEP_SUMMARY</c>.</summary>
    /// <param name="result">The comparison.</param>
    public static string Markdown(ComparisonResult result)
    {
        StringBuilder text = new();
        text.AppendLine($"| Metric | {result.BaselineLabel} | {result.CandidateLabel} | Delta | 95% CI | Verdict |");
        text.AppendLine("|---|---|---|---|---|---|");
        foreach (MetricComparison metric in result.Metrics)
        {
            string interval = metric.Clock == "exact"
                ? "exact"
                : metric.ConfidenceLow is { } low && metric.ConfidenceHigh is { } high
                    ? $"[{RankStatistics.Format(low)}, {RankStatistics.Format(high)}]"
                    : "";
            text.AppendLine($"| {metric.Name}{Suffix(metric)} | {Value(metric.Baseline)} | {Value(metric.Candidate)} | {Delta(metric)} | {interval} | {Verdict(metric)} |");
        }
        text.AppendLine();
        if (result.OverBudget.Count > 0)
        {
            text.AppendLine($"Exit 1: {result.OverBudget.Count} metric(s) over budget: {string.Join(", ", result.OverBudget.Select(metric => metric.Name))}.");
        }
        else
        {
            text.AppendLine(result.Clean ? "No regressions." : "No metric is over budget.");
        }
        if (result.BehaviourChanged)
        {
            text.AppendLine();
            text.AppendLine($"Behaviour changed: {result.Behaviour.NewFailures.Count} new failure(s), {result.Behaviour.Fixed.Count} fixed, "
                + $"{result.Behaviour.Added.Count} test(s) only on the candidate, {result.Behaviour.Vanished.Count} only on the baseline.");
        }
        return text.ToString();
    }

    /// <summary>The JSON report.</summary>
    /// <param name="result">The comparison.</param>
    /// <param name="options">The analysis options.</param>
    /// <param name="exitCode">The code the command exits with.</param>
    public static string Json(ComparisonResult result, CompareAnalysisOptions options, int exitCode)
    {
        CompareReport report = new(1, result.BaselineLabel, result.CandidateLabel, result.BaselineRuns, result.CandidateRuns,
            options.Alpha, options.MinEffect, result.OverBudget, exitCode, result.Metrics,
            result.Behaviour.NewFailures, result.Behaviour.Fixed, result.Behaviour.StillFailing,
            result.Behaviour.Added, result.Behaviour.Vanished, ChangedTests(result));
        return JsonSerializer.Serialize(report, s_json);
    }

    /// <summary>The tests whose outcome differs, for the JSON report.</summary>
    /// <param name="result">The comparison.</param>
    public static IReadOnlyList<TestChange> ChangedTests(ComparisonResult result) =>
        [.. result.Behaviour.NewFailures.Concat(result.Behaviour.Fixed).Concat(result.Behaviour.Added).Concat(result.Behaviour.Vanished)
            .OrderBy(change => change.FullyQualifiedName, StringComparer.Ordinal)];

    private static string Value(double? value) => value is { } number ? RankStatistics.Format(number) : "-";

    private static string Suffix(MetricComparison metric) => metric.Clock == "exact" ? "" : " (p50)";

    private static string Delta(MetricComparison metric)
    {
        if (metric.Delta is not { } delta) return "";
        if (delta == 0) return metric.Ratio is null ? "0" : "0%";
        if (metric.Ratio is not { } ratio) return Sign(delta);
        return Sign(delta) + (ratio * 100).ToString("0.#", CultureInfo.InvariantCulture) + "%";
    }

    private static string Sign(double value) => value > 0 ? "+" : "";

    private static string Interval(MetricComparison metric)
    {
        if (metric.Clock == "exact") return "exact";
        if (metric.ConfidenceLow is not { } low || metric.ConfidenceHigh is not { } high) return "";
        return $"[{RankStatistics.Format(low)}, {RankStatistics.Format(high)}]";
    }

    private static string Verdict(MetricComparison metric)
    {
        string verdict = metric.Verdict switch
        {
            MetricVerdict.Improved => "improved",
            MetricVerdict.Regressed => "regressed",
            MetricVerdict.Changed => "changed",
            MetricVerdict.Unchanged => "unchanged",
            MetricVerdict.Inconclusive => "inconclusive",
            MetricVerdict.Added => "added",
            MetricVerdict.Removed => "removed",
            _ => "unknown",
        };
        if (metric.PValue is { } p) verdict += $" (p={RankStatistics.Format(p)})";
        if (metric.Budget != null) verdict += $" (budget {metric.Budget})";
        return metric.OverBudget ? verdict + " OVER BUDGET" : verdict;
    }

    private static string Detail(string? message) => string.IsNullOrWhiteSpace(message) ? "" : ": " + message.Trim().Split('\n')[0].Trim();

    private static void Section(TextWriter output, string title, IReadOnlyList<TestChange> changes, Func<TestChange, string> line)
    {
        if (changes.Count == 0) return;
        output.WriteLine();
        output.WriteLine($"{title} ({changes.Count}):");
        foreach (TestChange change in changes) output.WriteLine("  " + line(change));
    }

    private static readonly JsonSerializerOptions s_json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };
}
