using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Xml.Linq;

namespace Zaldaryon.Pharos.Cli.ParallelRuns;

/// <summary>What changed for one test between two runs.</summary>
/// <param name="Test">The test's display name, theory arguments included.</param>
/// <param name="FullyQualifiedName">The test method's full name.</param>
/// <param name="Before">Its outcome in the first run, or null when it was not there.</param>
/// <param name="After">Its outcome in the second run, or null when it is gone.</param>
/// <param name="BeforeDuration">How long it took in the first run.</param>
/// <param name="AfterDuration">How long it took in the second run.</param>
/// <param name="Message">The failure message in the second run, if it failed.</param>
public sealed record TestChange(
    string Test,
    string FullyQualifiedName,
    string? Before,
    string? After,
    TimeSpan? BeforeDuration,
    TimeSpan? AfterDuration,
    string? Message)
{
    /// <summary><see cref="BeforeDuration"/> in seconds, for tools that read the JSON.</summary>
    public double? BeforeSeconds => BeforeDuration?.TotalSeconds;

    /// <summary><see cref="AfterDuration"/> in seconds, for tools that read the JSON.</summary>
    public double? AfterSeconds => AfterDuration?.TotalSeconds;
}

/// <summary>What changed between two test runs.</summary>
/// <param name="NewFailures">Failing now; passing, skipped or absent before.</param>
/// <param name="Fixed">Passing now, failing before.</param>
/// <param name="StillFailing">Failing in both runs.</param>
/// <param name="NewlySkipped">Skipped now, passing or failing before.</param>
/// <param name="Vanished">In the first run only.</param>
/// <param name="Added">In the second run only.</param>
/// <param name="Slower">Passing in both, and much slower now.</param>
/// <param name="Faster">Passing in both, and much faster now.</param>
/// <param name="BeforeCount">The tests in the first run, each counted once.</param>
/// <param name="AfterCount">The tests in the second run, each counted once.</param>
/// <param name="BeforeDuplicates">Results of the first run reported twice for the same test.</param>
/// <param name="AfterDuplicates">Results of the second run reported twice for the same test.</param>
public sealed record RunDiff(
    IReadOnlyList<TestChange> NewFailures,
    IReadOnlyList<TestChange> Fixed,
    IReadOnlyList<TestChange> StillFailing,
    IReadOnlyList<TestChange> NewlySkipped,
    IReadOnlyList<TestChange> Vanished,
    IReadOnlyList<TestChange> Added,
    IReadOnlyList<TestChange> Slower,
    IReadOnlyList<TestChange> Faster,
    int BeforeCount,
    int AfterCount,
    int BeforeDuplicates = 0,
    int AfterDuplicates = 0);

/// <summary>What <c>pharos diff --json</c> prints. Version 1 of the format.</summary>
internal sealed record DiffReport(
    int SchemaVersion,
    string Before,
    string After,
    double SlowerThan,
    double MinDeltaSeconds,
    bool Regressed,
    int ExitCode,
    string? BeforeAborted,
    string? AfterAborted,
    IReadOnlyList<TestChange> NewFailures,
    IReadOnlyList<TestChange> Fixed,
    IReadOnlyList<TestChange> StillFailing,
    IReadOnlyList<TestChange> NewlySkipped,
    IReadOnlyList<TestChange> Vanished,
    IReadOnlyList<TestChange> Added,
    IReadOnlyList<TestChange> Slower,
    IReadOnlyList<TestChange> Faster,
    int BeforeCount,
    int AfterCount,
    int BeforeDuplicates,
    int AfterDuplicates);

/// <summary>
/// <c>pharos diff</c>: compares two TRX files and reports what newly fails, what was fixed, which
/// tests vanished, appeared or are now skipped, and which got much slower, with an exit code a CI
/// job can gate on.
/// </summary>
public static class DiffCommand
{
    /// <summary>No regression.</summary>
    public const int ExitNoRegressions = 0;

    /// <summary>
    /// A test newly fails, or the second run stopped early or has no results (or tests vanished,
    /// were skipped or slowed down, when asked to count those).
    /// </summary>
    public const int ExitRegressions = 1;

    /// <summary>The arguments or the files were wrong.</summary>
    public const int ExitError = 2;

    /// <summary>The help for <c>pharos diff</c>.</summary>
    public const string HelpText = """
        pharos diff - compare two test runs

        Usage: pharos diff <before.trx> <after.trx> [options]

          --slower-than <n%>      Report passing tests that took more than this much longer (default: 50%)
          --min-delta <seconds>   ... and at least this many seconds longer (default: 1)
          --fail-on-slower        Count slower tests as regressions
          --fail-on-vanished      Count tests that are gone from the second run as regressions
          --fail-on-skipped       Count tests skipped in the second run, and run before, as regressions
          --json                  Print the comparison as JSON

        Reports tests that newly fail (failing in the second run, passing, skipped or absent in
        the first), fixed tests, tests still failing, tests newly skipped, vanished and new tests,
        and duration changes. Tests are matched by their class, method and display name, so each
        theory row is compared on its own.

        A second run that stopped early (a test host that crashed or was stopped) or has no
        results while the first has some always counts as a regression.

        Exit codes: 0 no regressions, 1 regressions, 2 bad input.

        Example:
          pharos diff main.trx pr.trx --slower-than 50% --json > diff.json
        """;

    /// <summary>Runs <c>pharos diff</c> with the arguments that follow <c>diff</c>.</summary>
    public static int Run(IReadOnlyList<string> args, TextWriter stdout, TextWriter stderr)
    {
        DiffOptions? options;
        try
        {
            options = DiffOptions.Parse(args);
        }
        catch (ArgumentException ex)
        {
            stderr.WriteLine($"Error: {ex.Message}");
            stderr.WriteLine();
            stderr.WriteLine(HelpText);
            return ExitError;
        }

        if (options == null)
        {
            stdout.WriteLine(HelpText);
            return ExitNoRegressions;
        }

        TrxRun before, after;
        try
        {
            before = Load(options.Before);
            after = Load(options.After);
        }
        catch (Exception ex) when (ex is IOException or FormatException or System.Xml.XmlException or UnauthorizedAccessException)
        {
            stderr.WriteLine($"Error: {ex.Message}");
            return ExitError;
        }

        RunDiff diff = Compare(before.Results, after.Results, options.SlowerThan, options.MinDelta);
        string? afterAborted = after.Abort
            ?? (after.Results.Count == 0 && before.Results.Count > 0 ? "The second run has no test results." : null);

        List<string> counted = [];
        if (diff.NewFailures.Count > 0) counted.Add($"{diff.NewFailures.Count} new failure(s)");
        if (afterAborted != null) counted.Add("the second run stopped early");
        if (options.FailOnVanished && diff.Vanished.Count > 0) counted.Add($"{diff.Vanished.Count} vanished test(s)");
        if (options.FailOnSkipped && diff.NewlySkipped.Count > 0) counted.Add($"{diff.NewlySkipped.Count} newly skipped test(s)");
        if (options.FailOnSlower && diff.Slower.Count > 0) counted.Add($"{diff.Slower.Count} slower test(s)");
        int exitCode = counted.Count > 0 ? ExitRegressions : ExitNoRegressions;

        if (options.Json)
        {
            DiffReport report = new(1, options.Before, options.After, options.SlowerThan, options.MinDelta.TotalSeconds,
                exitCode != ExitNoRegressions, exitCode, before.Abort, afterAborted,
                diff.NewFailures, diff.Fixed, diff.StillFailing, diff.NewlySkipped, diff.Vanished, diff.Added, diff.Slower, diff.Faster,
                diff.BeforeCount, diff.AfterCount, diff.BeforeDuplicates, diff.AfterDuplicates);
            stdout.WriteLine(JsonSerializer.Serialize(report, s_json));
        }
        else
        {
            Print(diff, options, before.Abort, afterAborted, counted, stdout);
        }

        return exitCode;
    }

    /// <summary>Compares the results of two runs.</summary>
    internal static RunDiff Compare(IReadOnlyList<TrxResult> before, IReadOnlyList<TrxResult> after, double slowerThan = 0.5, TimeSpan? minDelta = null)
    {
        TimeSpan delta = minDelta ?? TimeSpan.FromSeconds(1);
        (Dictionary<string, TrxResult> first, int firstDuplicates) = ByTest(before);
        (Dictionary<string, TrxResult> second, int secondDuplicates) = ByTest(after);
        List<TestChange> newFailures = [], fixedTests = [], stillFailing = [], newlySkipped = [], vanished = [], added = [], slower = [], faster = [];

        foreach ((string key, TrxResult now) in second.OrderBy(e => e.Key, StringComparer.Ordinal))
        {
            first.TryGetValue(key, out TrxResult? then);
            Kind nowKind = KindOf(now.Outcome);
            Kind? thenKind = then == null ? null : KindOf(then.Outcome);
            TestChange change = new(DisplayName(now), now.FullyQualifiedName, then?.Outcome, now.Outcome, then?.Duration, now.Duration,
                nowKind == Kind.Failed ? now.Message : null);
            if (then == null) added.Add(change);
            if (nowKind == Kind.Failed && thenKind != Kind.Failed) newFailures.Add(change);
            else if (nowKind == Kind.Failed) stillFailing.Add(change);
            else if (nowKind == Kind.Passed && thenKind == Kind.Failed) fixedTests.Add(change);
            else if (nowKind == Kind.Skipped && thenKind is Kind.Passed or Kind.Failed) newlySkipped.Add(change);
            else if (nowKind == Kind.Passed && thenKind == Kind.Passed)
            {
                // A test that took no time before has no ratio to speak of; the delta alone decides.
                if (now.Duration - then!.Duration >= delta && now.Duration.TotalSeconds > then.Duration.TotalSeconds * (1 + slowerThan)) slower.Add(change);
                else if (then.Duration - now.Duration >= delta && then.Duration.TotalSeconds > now.Duration.TotalSeconds * (1 + slowerThan)) faster.Add(change);
            }
        }

        foreach ((string key, TrxResult then) in first.OrderBy(e => e.Key, StringComparer.Ordinal))
        {
            if (!second.ContainsKey(key)) vanished.Add(new TestChange(DisplayName(then), then.FullyQualifiedName, then.Outcome, null, then.Duration, null, null));
        }

        return new RunDiff(newFailures, fixedTests, stillFailing, newlySkipped, vanished, added,
            [.. slower.OrderByDescending(c => c.AfterDuration - c.BeforeDuration)],
            [.. faster.OrderByDescending(c => c.BeforeDuration - c.AfterDuration)],
            first.Count, second.Count, firstDuplicates, secondDuplicates);
    }

    private enum Kind
    {
        Skipped,
        Passed,
        Failed,
    }

    // diff's own reading of the outcomes vstest writes; a run that aborted after a test passed
    // still passed that test, and a test that could not run or lost its host failed.
    private static Kind KindOf(string outcome) => outcome switch
    {
        "Passed" or "PassedButRunAborted" => Kind.Passed,
        "Failed" or "Error" or "Timeout" or "Aborted" or "NotRunnable" or "Disconnected" => Kind.Failed,
        _ => Kind.Skipped,
    };

    private static string DisplayName(TrxResult result) => string.IsNullOrEmpty(result.TestName) ? result.FullyQualifiedName : result.TestName;

    // One result per test, keyed by method and display name, so two classes with a test of the same
    // display name stay apart. A test reported twice keeps its worst result: failed, then passed,
    // then skipped.
    private static (Dictionary<string, TrxResult> ByTest, int Duplicates) ByTest(IReadOnlyList<TrxResult> results)
    {
        Dictionary<string, TrxResult> byTest = new(StringComparer.Ordinal);
        int duplicates = 0;
        foreach (TrxResult result in results)
        {
            string key = result.FullyQualifiedName + "\0" + result.TestName;
            if (!byTest.TryGetValue(key, out TrxResult? seen))
            {
                byTest[key] = result;
                continue;
            }

            duplicates++;
            if (KindOf(result.Outcome) > KindOf(seen.Outcome)) byTest[key] = result;
        }

        return (byTest, duplicates);
    }

    private sealed record TrxRun(IReadOnlyList<TrxResult> Results, string? Abort);

    private static TrxRun Load(string path)
    {
        if (Directory.Exists(path)) throw new IOException($"{path} is a folder, not a TRX file.");
        if (!File.Exists(path)) throw new FileNotFoundException($"{path} does not exist.", path);
        XDocument trx = XDocument.Load(path);
        if (trx.Root?.Name != Trx.Ns + "TestRun") throw new FormatException($"{path} is not a TRX file: it has no TestRun element.");
        IReadOnlyList<TrxResult> results = Trx.Results(trx);
        string? abort = Trx.Abort(trx)
            ?? (results.Any(r => r.Outcome == "PassedButRunAborted") ? "The test run was aborted." : null);
        return new TrxRun(results, abort);
    }

    private static void Print(RunDiff diff, DiffOptions options, string? beforeAborted, string? afterAborted, IReadOnlyList<string> counted, TextWriter output)
    {
        output.WriteLine($"{options.Before} ({diff.BeforeCount} tests) -> {options.After} ({diff.AfterCount} tests)");
        if (beforeAborted != null) output.WriteLine($"The first run stopped early: {FirstLine(beforeAborted)}");
        if (afterAborted != null) output.WriteLine($"The second run stopped early: {FirstLine(afterAborted)}");
        if (diff.BeforeDuplicates + diff.AfterDuplicates > 0)
        {
            output.WriteLine($"Results reported twice for the same test: {diff.BeforeDuplicates} before, {diff.AfterDuplicates} after (the worst of each is compared).");
        }

        Section(output, "New failures", diff.NewFailures, c => $"{c.Test} ({c.Before?.ToLowerInvariant() ?? "new"} -> failed){Detail(c.Message)}");
        Section(output, "Fixed", diff.Fixed, c => c.Test);
        Section(output, "Still failing", diff.StillFailing, c => c.Test);
        Section(output, "Newly skipped", diff.NewlySkipped, c => $"{c.Test} (was {c.Before?.ToLowerInvariant()})");
        Section(output, "Vanished", diff.Vanished, c => $"{c.Test} (was {c.Before?.ToLowerInvariant()})");
        Section(output, "New tests", diff.Added, c => $"{c.Test} ({c.After?.ToLowerInvariant()})");
        Section(output, "Slower", diff.Slower, c => $"{c.Test}: {Seconds(c.BeforeDuration)} -> {Seconds(c.AfterDuration)}");
        Section(output, "Faster", diff.Faster, c => $"{c.Test}: {Seconds(c.BeforeDuration)} -> {Seconds(c.AfterDuration)}");
        output.WriteLine();
        output.WriteLine(counted.Count == 0 ? "No regressions." : $"Regressions: {string.Join(", ", counted)}.");
    }

    private static void Section(TextWriter output, string title, IReadOnlyList<TestChange> changes, Func<TestChange, string> line)
    {
        if (changes.Count == 0) return;
        output.WriteLine();
        output.WriteLine($"{title} ({changes.Count}):");
        foreach (TestChange change in changes) output.WriteLine("  " + line(change));
    }

    private static string FirstLine(string message) => message.Trim().Split('\n')[0].Trim();

    private static string Detail(string? message) => string.IsNullOrWhiteSpace(message) ? "" : ": " + FirstLine(message);

    private static string Seconds(TimeSpan? time) => string.Create(CultureInfo.InvariantCulture, $"{time?.TotalSeconds ?? 0:0.0}s");

    private static readonly JsonSerializerOptions s_json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };
}

/// <summary>The options of <c>pharos diff</c>.</summary>
public sealed record DiffOptions(string Before, string After)
{
    /// <summary>How much longer a passing test must take to be reported slower: 0.5 is 50%.</summary>
    public double SlowerThan { get; init; } = 0.5;

    /// <summary>The least it must take longer, so very short tests do not count.</summary>
    public TimeSpan MinDelta { get; init; } = TimeSpan.FromSeconds(1);

    /// <summary>Whether slower tests count as regressions.</summary>
    public bool FailOnSlower { get; init; }

    /// <summary>Whether tests gone from the second run count as regressions.</summary>
    public bool FailOnVanished { get; init; }

    /// <summary>Whether tests skipped in the second run, and run in the first, count as regressions.</summary>
    public bool FailOnSkipped { get; init; }

    /// <summary>Whether to print JSON rather than text.</summary>
    public bool Json { get; init; }

    /// <summary>Reads the arguments that follow <c>diff</c>; null asks for the help.</summary>
    /// <exception cref="ArgumentException">The arguments are wrong.</exception>
    public static DiffOptions? Parse(IReadOnlyList<string> args)
    {
        List<string> files = [];
        double slowerThan = 0.5;
        TimeSpan minDelta = TimeSpan.FromSeconds(1);
        bool failOnSlower = false, failOnVanished = false, failOnSkipped = false, json = false;
        for (int i = 0; i < args.Count; i++)
        {
            string arg = args[i];
            string Value() => i + 1 < args.Count ? args[++i] : throw new ArgumentException($"{arg} needs a value.");
            switch (arg)
            {
                case "-h" or "--help":
                    return null;
                case "--slower-than":
                    slowerThan = Percent(Value());
                    break;
                case "--min-delta":
                    string seconds = Value();
                    minDelta = double.TryParse(seconds, NumberStyles.Float, CultureInfo.InvariantCulture, out double s) && s >= 0 && s <= MaxSeconds
                        ? TimeSpan.FromSeconds(s)
                        : throw new ArgumentException($"--min-delta takes seconds from 0 to 86400, not '{seconds}'.");
                    break;
                case "--fail-on-slower":
                    failOnSlower = true;
                    break;
                case "--fail-on-vanished":
                    failOnVanished = true;
                    break;
                case "--fail-on-skipped":
                    failOnSkipped = true;
                    break;
                case "--json":
                    json = true;
                    break;
                default:
                    if (arg.StartsWith('-')) throw new ArgumentException($"Unknown option: {arg}");
                    files.Add(arg);
                    break;
            }
        }

        if (files.Count != 2) throw new ArgumentException("Name two TRX files: the run before and the run after.");
        return new DiffOptions(files[0], files[1]) { SlowerThan = slowerThan, MinDelta = minDelta, FailOnSlower = failOnSlower, FailOnVanished = failOnVanished, FailOnSkipped = failOnSkipped, Json = json };
    }

    // A day: far longer than any test, and well inside what a TimeSpan holds.
    private const double MaxSeconds = 86400;

    // "50%" or "50": half as long again.
    private static double Percent(string value)
    {
        string number = value.TrimEnd('%');
        return double.TryParse(number, NumberStyles.Float, CultureInfo.InvariantCulture, out double percent) && percent >= 0 && double.IsFinite(percent)
            ? percent / 100
            : throw new ArgumentException($"--slower-than takes a percentage such as 50%, not '{value}'.");
    }
}
