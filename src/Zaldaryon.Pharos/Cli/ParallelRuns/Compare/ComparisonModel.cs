using Zaldaryon.Pharos.Metrics;
using Zaldaryon.Pharos.Statistics;

namespace Zaldaryon.Pharos.Cli.ParallelRuns.Compare;

/// <summary>What happened to one metric between the baseline and the candidate.</summary>
internal enum MetricVerdict
{
    /// <summary>The change is significant and in the better direction.</summary>
    Improved,

    /// <summary>The change is significant and in the worse direction.</summary>
    Regressed,

    /// <summary>A metric with no preferred direction that moved.</summary>
    Changed,

    /// <summary>No significant change, or one smaller than the minimum effect size.</summary>
    Unchanged,

    /// <summary>Too few samples, or an interval too wide to call.</summary>
    Inconclusive,

    /// <summary>Only the candidate has this metric.</summary>
    Added,

    /// <summary>Only the baseline has this metric.</summary>
    Removed,
}

/// <summary>One metric compared across the baseline and the candidate.</summary>
/// <param name="Name">The metric's name.</param>
/// <param name="Unit">Its unit as written by <see cref="MetricSummary.Unit"/>.</param>
/// <param name="Clock">Its clock, which decides whether a test is used.</param>
/// <param name="Direction">Which way is better.</param>
/// <param name="Baseline">The baseline value: the median of the per-run values for a sampled metric, the median total for an exact one.</param>
/// <param name="Candidate">The candidate's value, read the same way.</param>
/// <param name="Delta">The candidate value minus the baseline value.</param>
/// <param name="Ratio">The delta as a fraction of the baseline value; null when the baseline is zero.</param>
/// <param name="Verdict">What the comparison concluded.</param>
/// <param name="PValue">The two-sided p-value for a sampled metric; null for an exact one.</param>
/// <param name="ConfidenceLow">The low end of the bootstrap interval for the median difference; null when no interval was asked for.</param>
/// <param name="ConfidenceHigh">The high end of that interval.</param>
/// <param name="BaselineSamples">How many baseline values were collected.</param>
/// <param name="CandidateSamples">How many candidate values were collected.</param>
/// <param name="Budget">The matching budget, such as <c>+5%</c>, or null when none matched.</param>
/// <param name="OverBudget">Whether the change is worse than the budget allows.</param>
internal sealed record MetricComparison(
    string Name,
    string Unit,
    string Clock,
    string Direction,
    double? Baseline,
    double? Candidate,
    double? Delta,
    double? Ratio,
    MetricVerdict Verdict,
    double? PValue,
    double? ConfidenceLow,
    double? ConfidenceHigh,
    int BaselineSamples,
    int CandidateSamples,
    string? Budget,
    bool OverBudget);

/// <summary>How a comparison decides significance and what the budgets allow.</summary>
/// <param name="Alpha">The significance level: a p-value at or above it is unchanged.</param>
/// <param name="MinEffect">The smallest relative change worth reporting; below it a significant change is unchanged.</param>
/// <param name="BootstrapSamples">Resamples behind the confidence interval; 0 skips the interval.</param>
/// <param name="Seed">The bootstrap seed.</param>
/// <param name="Budgets">The budgets to apply, if any.</param>
internal sealed record CompareAnalysisOptions(
    double Alpha = 0.05,
    double MinEffect = 0.01,
    int BootstrapSamples = RankStatistics.DefaultBootstrapSamples,
    int Seed = RankStatistics.DefaultSeed,
    BudgetPolicy? Budgets = null);

/// <summary>The whole comparison: metrics, behaviour, and the tests that only exist on one side.</summary>
/// <param name="BaselineLabel">What the baseline was, for the report.</param>
/// <param name="CandidateLabel">What the candidate was.</param>
/// <param name="BaselineRuns">How many baseline runs were read.</param>
/// <param name="CandidateRuns">How many candidate runs were read.</param>
/// <param name="Metrics">Every metric name seen on either side, ordered by name.</param>
/// <param name="Behaviour">What changed in the test outcomes.</param>
internal sealed record ComparisonResult(
    string BaselineLabel,
    string CandidateLabel,
    int BaselineRuns,
    int CandidateRuns,
    IReadOnlyList<MetricComparison> Metrics,
    RunDiff Behaviour)
{
    /// <summary>The metrics that moved in the worse direction beyond their budget.</summary>
    public IReadOnlyList<MetricComparison> OverBudget => [.. Metrics.Where(metric => metric.OverBudget)];

    /// <summary>Whether any test changed outcome at all.</summary>
    public bool BehaviourChanged =>
        Behaviour.NewFailures.Count + Behaviour.Fixed.Count + Behaviour.Added.Count + Behaviour.Vanished.Count > 0;

    /// <summary>Whether every metric is unchanged or added, so nothing needs a second look.</summary>
    public bool Clean => !BehaviourChanged
        && Metrics.All(metric => metric.Verdict is MetricVerdict.Unchanged or MetricVerdict.Added or MetricVerdict.Removed);
}

/// <summary>
/// Builds the per-metric comparison and the behaviour change from the manifests of one or more runs
/// per side.
/// </summary>
/// <remarks>
/// A metric is aggregated by name across the tests and repeats of a side. An exact metric (a count, a
/// byte total, a tick count) adds up, so the side's value is the median of the per-run totals; a
/// sampled metric takes each run's median across the tests that recorded it, giving one value per
/// run for the rank test.
/// </remarks>
internal static class ComparisonModel
{
    /// <summary>Compares two sides.</summary>
    /// <param name="baseline">The baseline side's manifests, oldest run first.</param>
    /// <param name="candidate">The candidate side's manifests.</param>
    /// <param name="baselineLabel">What the baseline was.</param>
    /// <param name="candidateLabel">What the candidate was.</param>
    /// <param name="options">Significance, effect size and budgets.</param>
    public static ComparisonResult Build(IReadOnlyList<ScenarioRunManifest> baseline, IReadOnlyList<ScenarioRunManifest> candidate,
        string baselineLabel, string candidateLabel, CompareAnalysisOptions options)
    {
        ArgumentNullException.ThrowIfNull(baseline);
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(options);

        Dictionary<string, SideSeries> first = Aggregate(baseline);
        Dictionary<string, SideSeries> second = Aggregate(candidate);

        List<MetricComparison> metrics = [];
        foreach (string name in first.Keys.Concat(second.Keys).Distinct(StringComparer.Ordinal).OrderBy(name => name, StringComparer.Ordinal))
        {
            bool inBaseline = first.TryGetValue(name, out SideSeries? left);
            bool inCandidate = second.TryGetValue(name, out SideSeries? right);
            if (!inBaseline) metrics.Add(Missing(name, right!, MetricVerdict.Added, options));
            else if (!inCandidate) metrics.Add(Missing(name, left!, MetricVerdict.Removed, options));
            else metrics.Add(Compare(name, left!, right!, options));
        }

        RunDiff behaviour = DiffCommand.Compare(Results(baseline), Results(candidate));
        return new ComparisonResult(baselineLabel, candidateLabel, baseline.Count, candidate.Count, metrics, behaviour);
    }

    /// <summary>The manifests' test results, for the outcome comparison.</summary>
    internal static IReadOnlyList<TrxResult> Results(IReadOnlyList<ScenarioRunManifest> manifests)
    {
        ScenarioRunManifest merged = ScenarioRunManifestWriter.Merge(manifests);
        return [.. merged.Tests.Select(test => new TrxResult(
            test.FullyQualifiedName,
            test.DisplayName,
            test.FinalOutcome ?? test.RawOutcome,
            TimeSpan.Zero,
            test.FailurePhase != null ? $"Failed in phase {test.FailurePhase}" : null,
            null)
        {
            Flaky = test.Flaky,
        })];
    }

    // One value per run, plus the shape of the metric.
    private sealed record SideSeries(string Unit, string Clock, string Direction, IReadOnlyList<double> Values);

    private static Dictionary<string, SideSeries> Aggregate(IReadOnlyList<ScenarioRunManifest> manifests)
    {
        Dictionary<string, (string Unit, string Clock, string Direction, List<double> Values)> collected = new(StringComparer.Ordinal);
        foreach (ScenarioRunManifest manifest in manifests)
        {
            Dictionary<string, (string Unit, string Clock, string Direction, List<double> Samples, double Total)> perRun = new(StringComparer.Ordinal);
            foreach (ScenarioRunTest test in manifest.Tests)
            {
                if (test.Metrics == null) continue;
                foreach ((string name, MetricSummary summary) in test.Metrics)
                {
                    if (!perRun.TryGetValue(name, out var run))
                    {
                        run = (summary.Unit, summary.Clock, summary.Direction, [], 0);
                    }
                    run.Samples.Add(summary.P50);
                    run.Total += summary.Value;
                    perRun[name] = run;
                }
            }

            foreach ((string name, var run) in perRun)
            {
                if (!collected.TryGetValue(name, out var series))
                {
                    series = (run.Unit, run.Clock, run.Direction, []);
                }
                series.Values.Add(run.Clock == "exact" ? run.Total : RankStatistics.Median(run.Samples));
                collected[name] = series;
            }
        }

        return collected.ToDictionary(entry => entry.Key, entry => new SideSeries(
            entry.Value.Unit, entry.Value.Clock, entry.Value.Direction, entry.Value.Values), StringComparer.Ordinal);
    }

    private static MetricComparison Compare(string name, SideSeries baseline, SideSeries candidate, CompareAnalysisOptions options)
    {
        string clock = candidate.Clock;
        string unit = candidate.Unit;
        string direction = candidate.Direction;
        bool exact = clock == "exact";
        double baselineValue = RankStatistics.Median(baseline.Values);
        double candidateValue = RankStatistics.Median(candidate.Values);
        double delta = candidateValue - baselineValue;
        double? ratio = baselineValue == 0 ? null : delta / baselineValue;

        MetricVerdict verdict;
        double? pValue = null;
        double? low = null, high = null;
        if (exact)
        {
            verdict = VerdictFor(delta, direction);
        }
        else
        {
            RankTest test = RankStatistics.Compare(baseline.Values, candidate.Values, options.Alpha, options.BootstrapSamples, options.Seed);
            pValue = test.PValue;
            if (!double.IsNaN(test.ConfidenceLow))
            {
                low = test.ConfidenceLow;
                high = test.ConfidenceHigh;
            }
            bool tooFew = baseline.Values.Count < 2 || candidate.Values.Count < 2;
            bool tooWide = low is { } l && high is { } h && Math.Abs(h - l) > Math.Max(Math.Abs(baselineValue), double.Epsilon);
            bool significant = test.PValue < options.Alpha;
            bool bigEnough = baselineValue == 0 ? delta != 0 : Math.Abs(delta) / Math.Abs(baselineValue) >= options.MinEffect;
            verdict = tooFew || (significant && tooWide)
                ? MetricVerdict.Inconclusive
                : significant && bigEnough ? VerdictFor(delta, direction) : MetricVerdict.Unchanged;
        }

        (string? budget, bool over) = options.Budgets?.Evaluate(name, baselineValue, candidateValue, direction) ?? (null, false);
        return new MetricComparison(name, unit, clock, direction, baselineValue, candidateValue, delta, ratio, verdict,
            pValue, low, high, baseline.Values.Count, candidate.Values.Count, budget, over);
    }

    private static MetricComparison Missing(string name, SideSeries side, MetricVerdict verdict, CompareAnalysisOptions options)
    {
        double value = RankStatistics.Median(side.Values);
        return new MetricComparison(name, side.Unit, side.Clock, side.Direction,
            verdict == MetricVerdict.Removed ? value : null,
            verdict == MetricVerdict.Added ? value : null,
            null, null, verdict, null, null, null,
            verdict == MetricVerdict.Removed ? side.Values.Count : 0,
            verdict == MetricVerdict.Added ? side.Values.Count : 0,
            options.Budgets?.Match(name), false);
    }

    private static MetricVerdict VerdictFor(double delta, string direction)
    {
        if (delta == 0 || direction == "neutral") return delta == 0 ? MetricVerdict.Unchanged : MetricVerdict.Changed;
        bool worse = direction == "lowerIsBetter" ? delta > 0 : delta < 0;
        return worse ? MetricVerdict.Regressed : MetricVerdict.Improved;
    }
}
