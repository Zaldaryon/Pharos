using System.Text.Json;
using Xunit;
using Zaldaryon.Pharos.Cli.ParallelRuns;
using Zaldaryon.Pharos.Cli.ParallelRuns.Compare;
using Zaldaryon.Pharos.Metrics;

namespace Zaldaryon.Pharos.Tests.Cli;

public class CompareCommandTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "pharos-compare-" + Guid.NewGuid().ToString("N")[..8]);

    public CompareCommandTests() => Directory.CreateDirectory(_folder);

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    // A summary of one metric as a run writes it.
    private static MetricSummary S(string clock, string direction, double value, double p50 = 0) => new()
    {
        Unit = clock == "exact" ? "bytes" : "ms",
        Direction = direction,
        Clock = clock,
        Count = 5,
        Sum = value,
        Min = value,
        Max = value,
        Mean = value,
        P50 = p50 == 0 ? value : p50,
        P95 = value,
        P99 = value,
        Value = value,
    };

    private static ScenarioRunTest T(string name, string outcome = "Passed", params (string Name, MetricSummary Summary)[] metrics) =>
        new("My.Tests." + name, name, outcome, outcome, null, false, null, null, null, null,
            metrics.Length == 0 ? null : metrics.ToDictionary(metric => metric.Name, metric => metric.Summary, StringComparer.Ordinal));

    private static ScenarioRunManifest M(params ScenarioRunTest[] tests) =>
        new(1, "run", DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, "My.Tests.dll", "a.trx", [], [], [], tests);

    private static ComparisonResult Build(IReadOnlyList<ScenarioRunManifest> baseline, IReadOnlyList<ScenarioRunManifest> candidate,
        CompareAnalysisOptions? options = null) =>
        ComparisonModel.Build(baseline, candidate, "baseline", "candidate", options ?? new CompareAnalysisOptions());

    private static MetricComparison Row(ComparisonResult result, string name) =>
        result.Metrics.Single(metric => metric.Name == name);

    // ---- options ----

    [Fact]
    public void Parse_ReadsEveryOption()
    {
        CompareOptions options = CompareOptions.Parse(
            ["tests/MyMod.Tests", "--baseline", "origin/main", "--repeat", "7", "--filter", "Forge*", "--parallel", "3",
             "--shard", "2/4", "--game", "/games/vs", "-c", "Debug", "--no-build", "--xvfb", "always", "--alpha", "0.01",
             "--min-effect", "5%", "--bootstrap", "100", "--seed", "3", "--budgets", "b.json", "--md", "s.md", "--json", "r.json",
             "--results-dir", "out", "-v"])!;

        Assert.Equal("tests/MyMod.Tests", options.Target);
        Assert.Equal("origin/main", options.BaselineSpec);
        Assert.Equal(7, options.Repeat);
        Assert.Equal("Forge*", options.Filter);
        Assert.Equal(3, options.Parallel);
        Assert.Equal(new ShardOptions(2, 4), options.Shard);
        Assert.Equal("/games/vs", options.GamePath);
        Assert.Equal("Debug", options.Configuration);
        Assert.True(options.NoBuild);
        Assert.Equal(XvfbMode.Always, options.Xvfb);
        Assert.Equal(0.01, options.Alpha);
        Assert.Equal(0.05, options.MinEffect);
        Assert.Equal(100, options.BootstrapSamples);
        Assert.Equal(3, options.Seed);
        Assert.Equal("b.json", options.BudgetsPath);
        Assert.Equal("s.md", options.MarkdownPath);
        Assert.Equal("r.json", options.JsonPath);
        Assert.Equal("out", options.ResultsDirectory);
        Assert.True(options.Verbose);
    }

    [Fact]
    public void Parse_Defaults_AreTheDocumentedOnes()
    {
        CompareOptions options = CompareOptions.Parse(["a.dll", "--baseline", "origin/main"])!;

        Assert.Equal(5, options.Repeat);
        Assert.Equal(1, options.Parallel);
        Assert.Null(options.Shard);
        Assert.Equal("Release", options.Configuration);
        Assert.Equal(XvfbMode.Auto, options.Xvfb);
        Assert.Equal(0.05, options.Alpha);
        Assert.Equal(0.01, options.MinEffect);
        Assert.Equal("pharos-compare", options.ResultsDirectory);
    }

    [Fact]
    public void Parse_Help_ReturnsNull() => Assert.Null(CompareOptions.Parse(["--help"]));

    [Theory]
    [InlineData("")]
    [InlineData("a.dll")]
    [InlineData("--baseline origin/main")]
    [InlineData("a.dll --baseline origin/main --repeat 0")]
    [InlineData("a.dll --baseline origin/main --shard 5/4")]
    [InlineData("a.dll --baseline origin/main --xvfb sometimes")]
    [InlineData("a.dll --baseline origin/main --min-effect 5")]
    [InlineData("a.dll --baseline origin/main --alpha 1")]
    [InlineData("a.dll --baseline origin/main --nope")]
    [InlineData("a.dll b.dll --baseline origin/main")]
    [InlineData("a.dll --baseline")]
    public void Parse_BadArguments_Throw(string line) =>
        Assert.Throws<ArgumentException>(() => CompareOptions.Parse(line.Split(' ', StringSplitOptions.RemoveEmptyEntries)));

    [Fact]
    public void Run_Help_PrintsTheUsage()
    {
        StringWriter stdout = new(), stderr = new();

        int exit = CompareCommand.Run(["--help"], stdout, stderr);

        Assert.Equal(CompareCommand.ExitOk, exit);
        Assert.Contains("pharos compare", stdout.ToString());
        Assert.Equal("", stderr.ToString());
    }

    [Fact]
    public void Run_WithoutArguments_ReportsTheErrorAndTheUsage()
    {
        StringWriter stdout = new(), stderr = new();

        int exit = CompareCommand.Run([], stdout, stderr);

        Assert.Equal(CompareCommand.ExitError, exit);
        Assert.Contains("Name the test assembly", stderr.ToString());
        Assert.Contains("pharos compare", stderr.ToString());
    }

    [Fact]
    public void Run_WithAMissingBaselineFile_ReportsTheError() 
    {
        StringWriter stdout = new(), stderr = new();

        int exit = CompareCommand.Run(["a.dll", "--baseline", Path.Combine(_folder, "nope.zip")], stdout, stderr);

        Assert.Equal(CompareCommand.ExitError, exit);
        Assert.Contains("not a file, a folder", stderr.ToString());
    }

    // ---- baseline resolution ----

    private BaselineEnvironment Environment(params string[] refs) =>
        new(_folder, Path.Combine(_folder, "data"), reference => refs.Contains(reference));

    [Fact]
    public void Baseline_AManifestFile_IsReused()
    {
        string manifest = Path.Combine(_folder, "pharos-run.json");
        File.WriteAllText(manifest, """{ "schemaVersion": 1, "tests": [] }""");

        BaselinePlan plan = BaselinePlanner.Resolve(manifest, Environment());

        Assert.Equal(BaselineKind.Run, plan.Kind);
        Assert.Equal(manifest, plan.Path);
    }

    [Fact]
    public void Baseline_AFolderHoldingAManifest_IsReused()
    {
        string folder = Path.Combine(_folder, "run");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "pharos-run.json"), """{ "tests": [] }""");

        BaselinePlan plan = BaselinePlanner.Resolve(folder, Environment());

        Assert.Equal(BaselineKind.Run, plan.Kind);
        Assert.Equal(Path.Combine(folder, "pharos-run.json"), plan.Path);
    }

    [Fact]
    public void Baseline_AFolderThatIsNotARunOrAMod_IsRejected()
    {
        string folder = Path.Combine(_folder, "empty");
        Directory.CreateDirectory(folder);

        ArgumentException error = Assert.Throws<ArgumentException>(() => BaselinePlanner.Resolve(folder, Environment()));
        Assert.Contains("neither a run", error.Message);
    }

    [Fact]
    public void Baseline_AModZip_IsStaged()
    {
        string zip = Path.Combine(_folder, "mymod.zip");
        File.WriteAllText(zip, "zip");

        BaselinePlan plan = BaselinePlanner.Resolve(zip, Environment());

        Assert.Equal(BaselineKind.ModBuild, plan.Kind);
        Assert.Equal(zip, plan.Path);
    }

    [Fact]
    public void Baseline_AModFolder_IsStaged()
    {
        string folder = Path.Combine(_folder, "mymod");
        Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, "modinfo.json"), "{}");

        BaselinePlan plan = BaselinePlanner.Resolve(folder, Environment());

        Assert.Equal(BaselineKind.ModBuild, plan.Kind);
    }

    [Fact]
    public void Baseline_AModDll_IsStaged()
    {
        string dll = Path.Combine(_folder, "mymod.dll");
        File.WriteAllText(dll, "dll");

        BaselinePlan plan = BaselinePlanner.Resolve(dll, Environment());

        Assert.Equal(BaselineKind.ModBuild, plan.Kind);
    }

    [Theory]
    [InlineData("notes.txt", "text")]
    [InlineData("a.trx", "<TestRun />")]
    [InlineData("not-a-manifest.json", """{ "schemaVersion": 1 }""")]
    public void Baseline_AFileThatIsNeitherAManifestNorAModBuild_IsRejected(string name, string contents)
    {
        string file = Path.Combine(_folder, name);
        File.WriteAllText(file, contents);

        ArgumentException error = Assert.Throws<ArgumentException>(() => BaselinePlanner.Resolve(file, Environment()));

        Assert.Contains("neither a run manifest", error.Message);
    }

    [Fact]
    public void Baseline_AReleasedZip_ComesFromTheModCache()
    {
        string zip = Path.Combine(_folder, "data", "mods", "mymod", "1.4.2", "mymod.zip");
        Directory.CreateDirectory(Path.GetDirectoryName(zip)!);
        File.WriteAllText(zip, "zip");

        BaselinePlan plan = BaselinePlanner.Resolve("mymod@1.4.2", Environment());

        Assert.Equal(BaselineKind.ModBuild, plan.Kind);
        Assert.Equal(zip, plan.Path);
    }

    [Fact]
    public void Baseline_AReleasedZipThatIsNotCached_SaysWhereToPutIt()
    {
        ArgumentException error = Assert.Throws<ArgumentException>(() => BaselinePlanner.Resolve("mymod@1.4.2", Environment()));

        Assert.Contains("No local build of mymod@1.4.2", error.Message);
        Assert.Contains(Path.Combine("mods", "mymod"), error.Message);
    }

    [Fact]
    public void Baseline_AGitRef_IsBuiltInAWorktree()
    {
        BaselinePlan plan = BaselinePlanner.Resolve("origin/main", Environment("origin/main"));

        Assert.Equal(BaselineKind.GitRef, plan.Kind);
        Assert.Contains("origin/main", plan.Description);
    }

    [Fact]
    public void Baseline_SomethingUnknown_IsRejected() =>
        Assert.Throws<ArgumentException>(() => BaselinePlanner.Resolve("does-not-exist", Environment()));

    [Theory]
    [InlineData("mymod@1.4.2", true)]
    [InlineData("mymod", false)]
    [InlineData("@1.4.2", false)]
    [InlineData("mymod@", false)]
    [InlineData("a@b@c", false)]
    [InlineData("dir/mymod@1.4.2", false)]
    [InlineData("C:\\mymod@1.4.2", false)]
    public void ModSpec_IsOnlyIdAtVersion(string spec, bool expected) =>
        Assert.Equal(expected, BaselinePlanner.TryParseModSpec(spec, out _, out _));

    // ---- budgets ----

    private BudgetPolicy Budgets(string json)
    {
        string path = Path.Combine(_folder, "budgets-" + Guid.NewGuid().ToString("N")[..6] + ".json");
        File.WriteAllText(path, json);
        return BudgetPolicy.Load(path);
    }

    [Fact]
    public void Budgets_AListOfRules_MatchesAGlobAndFailsOverTheLimit()
    {
        BudgetPolicy policy = Budgets("""{ "budgets": [ { "metric": "tick.modsystem.mymod:*", "limit": "+5%" } ] }""");

        (string? budget, bool over) = policy.Evaluate("tick.modsystem.mymod:ForgeSystem.ms", 100, 106, "lowerIsBetter");
        (string? within, bool ok) = policy.Evaluate("tick.modsystem.mymod:ForgeSystem.ms", 100, 104, "lowerIsBetter");

        Assert.Equal("+5%", budget);
        Assert.True(over);
        Assert.Equal("+5%", within);
        Assert.False(ok);
    }

    [Fact]
    public void Budgets_APlainMap_MatchesByPattern()
    {
        BudgetPolicy policy = Budgets("""{ "net.channel.mymod.bytes": "+0%", "gl.drawcalls.*": "+10%" }""");

        Assert.True(policy.Evaluate("net.channel.mymod.bytes", 100, 101, "lowerIsBetter").OverBudget);
        Assert.False(policy.Evaluate("net.channel.mymod.bytes", 100, 100, "lowerIsBetter").OverBudget);
        Assert.True(policy.Evaluate("gl.drawcalls.terrain.draws", 10, 12, "lowerIsBetter").OverBudget);
        Assert.Null(policy.Evaluate("some.other.metric", 1, 100, "lowerIsBetter").Budget);
    }

    [Fact]
    public void Budgets_AnAbsoluteLimit_IsComparedInTheMetricUnit()
    {
        BudgetPolicy policy = Budgets("""{ "budgets": [ { "metric": "count.of.things", "limit": "10" } ] }""");

        Assert.False(policy.Evaluate("count.of.things", 100, 110, "lowerIsBetter").OverBudget);
        Assert.True(policy.Evaluate("count.of.things", 100, 111, "lowerIsBetter").OverBudget);
    }

    [Fact]
    public void Budgets_HigherIsBetter_RegressesOnADecrease()
    {
        BudgetPolicy policy = Budgets("""{ "budgets": [ { "metric": "fps.avg", "limit": "+5%" } ] }""");

        Assert.True(policy.Evaluate("fps.avg", 100, 94, "higherIsBetter").OverBudget);
        Assert.False(policy.Evaluate("fps.avg", 100, 96, "higherIsBetter").OverBudget);
    }

    [Fact]
    public void Budgets_ANeutralMetric_IsNeverOverBudget() =>
        Assert.False(Budgets("""{ "budgets": [ { "metric": "players", "limit": "+0%" } ] }""")
            .Evaluate("players", 1, 5, "neutral").OverBudget);

    [Fact]
    public void Budgets_NoRules_MatchesNothing()
    {
        Assert.True(BudgetPolicy.Empty.IsEmpty);
        Assert.Null(BudgetPolicy.Empty.Match("anything"));
    }

    [Theory]
    [InlineData("""{ "budgets": [] }""")]
    [InlineData("""{ "budgets": [ { "limit": "+5%" } ] }""")]
    [InlineData("""{ "budgets": [ { "metric": "a", "limit": "lots" } ] }""")]
    [InlineData("""{ "budgets": [ { "metric": "a", "limit": "-5%" } ] }""")]
    [InlineData("""{ "budgets": 3 }""")]
    [InlineData("not json")]
    public void Budgets_ABadFile_IsAFormatError(string json) =>
        Assert.Throws<FormatException>(() => Budgets(json));

    [Theory]
    [InlineData("a.b.c", "a.b.c", true)]
    [InlineData("a.b.c", "a.b.d", false)]
    [InlineData("tick.mod:*", "tick.mod:ForgeSystem.ms", true)]
    [InlineData("tick.mod:*", "tick.modsystem:ForgeSystem.ms", false)]
    [InlineData("gl.drawcalls.?", "gl.drawcalls.x", true)]
    [InlineData("gl.drawcalls.?", "gl.drawcalls.xy", false)]
    public void Glob_MatchesTheWholeName(string pattern, string value, bool expected) =>
        Assert.Equal(expected, Glob.IsMatch(pattern, value));

    // ---- the comparison ----

    [Fact]
    public void Model_AnExactMetric_ReportsTheDeltaAndRatio()
    {
        ComparisonResult result = Build(
            [M(T("Forge", metrics: ("forge.bytes", S("exact", "lowerIsBetter", 1000))))],
            [M(T("Forge", metrics: ("forge.bytes", S("exact", "lowerIsBetter", 1200))))]);

        MetricComparison row = Row(result, "forge.bytes");
        Assert.Equal(MetricVerdict.Regressed, row.Verdict);
        Assert.Equal(1000, row.Baseline);
        Assert.Equal(1200, row.Candidate);
        Assert.Equal(200, row.Delta);
        Assert.Equal(0.2, row.Ratio);
        Assert.Null(row.PValue);
    }

    [Fact]
    public void Model_ANeutralExactMetric_IsChangedRatherThanRegressed()
    {
        ComparisonResult result = Build(
            [M(T("Count", metrics: ("things", S("exact", "neutral", 1))))],
            [M(T("Count", metrics: ("things", S("exact", "neutral", 2))))]);

        Assert.Equal(MetricVerdict.Changed, Row(result, "things").Verdict);
    }

    [Fact]
    public void Model_ASampledMetric_IsTestedAcrossTheRuns()
    {
        ScenarioRunManifest[] baseline = [.. Enumerable.Range(0, 5).Select(_ => M(T("Tick", metrics: ("tick.ms", S("tick", "lowerIsBetter", 10)))))];
        ScenarioRunManifest[] candidate = [.. Enumerable.Range(0, 5).Select(_ => M(T("Tick", metrics: ("tick.ms", S("tick", "lowerIsBetter", 20)))))];

        MetricComparison row = Row(Build(baseline, candidate), "tick.ms");

        Assert.Equal(MetricVerdict.Regressed, row.Verdict);
        Assert.True(row.PValue < 0.05, $"p was {row.PValue}");
        Assert.Equal(5, row.BaselineSamples);
        Assert.Equal(5, row.CandidateSamples);
        Assert.NotNull(row.ConfidenceLow);
    }

    [Fact]
    public void Model_ASampledMetricWithOneRunPerSide_IsInconclusive()
    {
        ComparisonResult result = Build(
            [M(T("Tick", metrics: ("tick.ms", S("tick", "lowerIsBetter", 10))))],
            [M(T("Tick", metrics: ("tick.ms", S("tick", "lowerIsBetter", 30))))]);

        Assert.Equal(MetricVerdict.Inconclusive, Row(result, "tick.ms").Verdict);
    }

    [Fact]
    public void Model_ASignificantButTinyChange_IsUnchanged()
    {
        ScenarioRunManifest[] baseline = [.. Enumerable.Range(0, 5).Select(_ => M(T("Tick", metrics: ("tick.ms", S("tick", "lowerIsBetter", 1000)))))];
        ScenarioRunManifest[] candidate = [.. Enumerable.Range(0, 5).Select(_ => M(T("Tick", metrics: ("tick.ms", S("tick", "lowerIsBetter", 1001)))))];
        CompareAnalysisOptions options = new(MinEffect: 0.05, BootstrapSamples: 0);

        Assert.Equal(MetricVerdict.Unchanged, Row(Build(baseline, candidate, options), "tick.ms").Verdict);
    }

    [Fact]
    public void Model_AMetricOnOneSideOnly_IsAddedOrRemoved()
    {
        ComparisonResult result = Build(
            [M(T("A", metrics: ("gone.metric", S("exact", "lowerIsBetter", 5))))],
            [M(T("A", metrics: ("new.metric", S("exact", "lowerIsBetter", 7))))]);

        Assert.Equal(MetricVerdict.Removed, Row(result, "gone.metric").Verdict);
        Assert.Equal(5, Row(result, "gone.metric").Baseline);
        Assert.Null(Row(result, "gone.metric").Candidate);
        Assert.Equal(MetricVerdict.Added, Row(result, "new.metric").Verdict);
        Assert.Equal(7, Row(result, "new.metric").Candidate);
    }

    [Fact]
    public void Model_AChangedOutcome_IsBehaviourChange()
    {
        ComparisonResult result = Build(
            [M(T("Steady"), T("Fixed"))],
            [M(T("Steady", "Failed"), T("Fixed"))]);

        Assert.Contains(result.Behaviour.NewFailures, change => change.Test == "Steady");
        Assert.True(result.BehaviourChanged);
        Assert.False(result.Clean);
    }

    [Fact]
    public void Model_NoChangeAtAll_IsClean()
    {
        ComparisonResult result = Build(
            [M(T("Steady", metrics: ("a", S("exact", "lowerIsBetter", 1))))],
            [M(T("Steady", metrics: ("a", S("exact", "lowerIsBetter", 1))))]);

        Assert.True(result.Clean);
        Assert.Empty(result.OverBudget);
    }

    [Fact]
    public void Model_ABudget_FlagsTheMetricThatBrokeIt()
    {
        CompareAnalysisOptions options = new(Budgets: Budgets("""{ "budgets": [ { "metric": "forge.bytes", "limit": "+5%" } ] }"""));

        ComparisonResult result = Build(
            [M(T("Forge", metrics: ("forge.bytes", S("exact", "lowerIsBetter", 1000))))],
            [M(T("Forge", metrics: ("forge.bytes", S("exact", "lowerIsBetter", 1200))))],
            options);

        MetricComparison row = Row(result, "forge.bytes");
        Assert.Equal("+5%", row.Budget);
        Assert.True(row.OverBudget);
        Assert.Same(row, Assert.Single(result.OverBudget));
        Assert.False(result.Clean);
    }

    [Fact]
    public void Model_ExactMetricsAggregateBySummingTheTestsInARun()
    {
        ComparisonResult result = Build(
            [M(T("A", metrics: ("bytes", S("exact", "lowerIsBetter", 100))), T("B", metrics: ("bytes", S("exact", "lowerIsBetter", 150))))],
            [M(T("A", metrics: ("bytes", S("exact", "lowerIsBetter", 100))), T("B", metrics: ("bytes", S("exact", "lowerIsBetter", 200))))]);

        Assert.Equal(250, Row(result, "bytes").Baseline);
        Assert.Equal(300, Row(result, "bytes").Candidate);
    }

    [Fact]
    public void Model_SampledMetricsTakeTheMedianOfTheTestsInARun()
    {
        ComparisonResult result = Build(
            [M(T("A", metrics: ("ms", S("tick", "lowerIsBetter", 10, p50: 10))), T("B", metrics: ("ms", S("tick", "lowerIsBetter", 30, p50: 30))))],
            [M(T("A", metrics: ("ms", S("tick", "lowerIsBetter", 10, p50: 10))), T("B", metrics: ("ms", S("tick", "lowerIsBetter", 30, p50: 30))))]);

        Assert.Equal(20, Row(result, "ms").Baseline);
        Assert.Equal(20, Row(result, "ms").Candidate);
    }

    // ---- the run schedule ----

    [Theory]
    [InlineData(1, "B1 C1")]
    [InlineData(2, "B1 C1 C2 B2")]
    [InlineData(3, "B1 C1 C2 B2 B3 C3")]
    [InlineData(5, "B1 C1 C2 B2 B3 C3 C4 B4 B5 C5")]
    public void Schedule_AlternatesInPairsWithTheSameNumberOfRunsOnEachSide(int repeat, string expected) =>
        Assert.Equal(expected, string.Join(' ', CompareCommand.Schedule(repeat)
            .Select(run => $"{(run.Baseline ? "B" : "C")}{run.Index}")));

    [Fact]
    public void Schedule_AlwaysRunsBothSides()
    {
        IReadOnlyList<(bool Baseline, int Index)> runs = CompareCommand.Schedule(5);

        Assert.Equal(5, runs.Count(run => run.Baseline));
        Assert.Equal(5, runs.Count(run => !run.Baseline));
        Assert.Contains(runs, run => !run.Baseline);
    }

    [Fact]
    public void Schedule_RejectsARepeatBelowOne() => Assert.Throws<ArgumentOutOfRangeException>(() => CompareCommand.Schedule(0));

    // ---- the report ----

    private static ComparisonResult Sample() => Build(
        [M(T("Steady"), T("Heals", "Failed"))],
        [M(T("Steady", "Failed"), T("Heals"))]);

    [Fact]
    public void Report_AnUnchangedMetric_ReadsAsZeroPercent()
    {
        ComparisonResult result = Build(
            [M(T("Steady", metrics: ("bytes", S("exact", "lowerIsBetter", 100))))],
            [M(T("Steady", metrics: ("bytes", S("exact", "lowerIsBetter", 100))))]);
        StringWriter console = new();

        ComparisonReport.WriteConsole(result, new CompareAnalysisOptions(), console);

        Assert.Contains("| bytes | 100 | 100 | 0% |", ComparisonReport.Markdown(result));
        Assert.DoesNotContain("00%", ComparisonReport.Markdown(result));
        Assert.DoesNotContain("00%", console.ToString());
    }

    [Fact]
    public void Console_NamesSampledMetricsTheWayTheMarkdownTableDoes()
    {
        ComparisonResult result = Build(
            [M(T("Tick", metrics: ("tick.ms", S("tick", "lowerIsBetter", 10))))],
            [M(T("Tick", metrics: ("tick.ms", S("tick", "lowerIsBetter", 10))))]);
        StringWriter console = new();

        ComparisonReport.WriteConsole(result, new CompareAnalysisOptions(), console);

        Assert.Contains("tick.ms (p50)", console.ToString());
        Assert.Contains("| tick.ms (p50) |", ComparisonReport.Markdown(result));
    }

    [Fact]
    public void Markdown_HasTheTableAndTheBehaviourNote()
    {
        string markdown = ComparisonReport.Markdown(Sample());

        Assert.Contains("| Metric | baseline | candidate | Delta | 95% CI | Verdict |", markdown);
        Assert.Contains("Behaviour changed", markdown);
    }

    [Fact]
    public void Json_HasTheSchemaAndTheChangedTests()
    {
        ComparisonResult result = Sample();
        CompareAnalysisOptions options = new();

        using JsonDocument document = JsonDocument.Parse(ComparisonReport.Json(result, options, CompareCommand.ExitOk));

        Assert.Equal(1, document.RootElement.GetProperty("schemaVersion").GetInt32());
        Assert.Equal("baseline", document.RootElement.GetProperty("baseline").GetString());
        Assert.Equal(0, document.RootElement.GetProperty("exitCode").GetInt32());
        JsonElement changed = document.RootElement.GetProperty("changedTests");
        Assert.Equal(2, changed.GetArrayLength());
    }

    [Fact]
    public void Console_NamesTheVerdictsAndTheSections()
    {
        StringWriter output = new();

        ComparisonReport.WriteConsole(Sample(), new CompareAnalysisOptions(), output);

        string text = output.ToString();
        Assert.Contains("baseline (1 run(s)) -> candidate (1 run(s))", text);
        Assert.Contains("New failures (1):", text);
        Assert.Contains("Fixed (1):", text);
    }
}
