using System.Xml.Linq;
using Xunit;
using Zaldaryon.Pharos.Cli.ParallelRuns;

namespace Zaldaryon.Pharos.Tests.Cli;

public sealed class ExplicitRunFilterTests
{
    [Theory]
    [InlineData("FullyQualifiedName~MyScenario", true)]
    [InlineData("Name=MyScenario", true)]
    [InlineData("FullyQualifiedName!~MyScenario", false)]
    [InlineData("FullyQualifiedName!=MyScenario", false)]
    [InlineData("(FullyQualifiedName~MyScenario)|Category=Live", false)]
    [InlineData("FullyQualifiedName~MyScenario&Category=Live", false)]
    [InlineData("Category=Live|FullyQualifiedName~MyScenario", false)]
    [InlineData("(Name=MyScenario)", false)]
    [InlineData("Category=Live", false)]
    [InlineData(null, false)]
    public void NameFiltersOptInToExplicitScenarios(string? filter, bool expected) =>
        Assert.Equal(expected, RunCommand.SelectsByTestName(filter));
}

public class RunOptionsTests
{
    [Fact]
    public void Defaults_RunOneWorkerPerClass()
    {
        RunOptions options = RunOptions.Parse(["tests/My.Tests"])!;

        Assert.Equal("tests/My.Tests", options.Target);
        Assert.Equal(1, options.Parallel);
        Assert.Equal(RunGrouping.Class, options.Grouping);
        Assert.Equal(TimeSpan.FromMinutes(60), options.WorkerTimeout);
        Assert.Equal(XvfbMode.Auto, options.Xvfb);
        Assert.False(options.ListOnly);
    }

    [Fact]
    public void EveryOption_IsRead()
    {
        RunOptions options = RunOptions.Parse(
            ["My.Tests.dll", "--filter", "Category=Live", "-p", "4", "--group", "collection", "--trx", "out.trx", "--list", "--json",
             "--results-dir", "r", "--worker-timeout", "5", "-c", "Debug", "--no-build", "--xvfb", "never", "-v"])!;

        Assert.Equal("Category=Live", options.Filter);
        Assert.Equal(4, options.Parallel);
        Assert.Equal(RunGrouping.Collection, options.Grouping);
        Assert.Equal("out.trx", options.TrxPath);
        Assert.True(options.ListOnly && options.Json && options.NoBuild && options.Verbose);
        Assert.Equal("r", options.ResultsDirectory);
        Assert.Equal(TimeSpan.FromMinutes(5), options.WorkerTimeout);
        Assert.Equal("Debug", options.Configuration);
        Assert.Equal(XvfbMode.Never, options.Xvfb);
    }

    [Fact]
    public void RepeatedRunOptions_AreValidated()
    {
        Assert.Equal(3, RunOptions.Parse(["tests/My.Tests", "--repeat", "3"])!.Repeat);
        Assert.True(RunOptions.Parse(["tests/My.Tests", "--until-fail", "--max", "4"])!.UntilFail);
        Assert.Throws<ArgumentException>(() => RunOptions.Parse(["tests/My.Tests", "--until-fail"]));
        Assert.Throws<ArgumentException>(() => RunOptions.Parse(["tests/My.Tests", "--max", "2"]));
        Assert.Throws<ArgumentException>(() => RunOptions.Parse(["tests/My.Tests", "--repeat", "2", "--until-fail", "--max", "3"]));
    }

    [Fact]
    public void FailedRows_UsesRawOutcomeAndExactTheoryDisplayIdentity()
    {
        string manifest = Path.Combine(Path.GetTempPath(), "pharos-rerun-" + Guid.NewGuid().ToString("N") + ".json");
        File.WriteAllText(manifest, """
            {"schemaVersion":1,"tests":[
              {"fullyQualifiedName":"Ns.Theory.Row","displayName":"Ns.Theory.Row(value: 1)","rawOutcome":"Failed","finalOutcome":"Quarantined"},
              {"fullyQualifiedName":"Ns.Theory.Row","displayName":"Ns.Theory.Row(value: 2)","rawOutcome":"Passed","finalOutcome":"Passed"},
              {"fullyQualifiedName":"Ns.Crashed.(worker aborted)","displayName":"Ns.Crashed (worker aborted)","rawOutcome":"Failed","finalOutcome":"Failed"}
            ]}
            """);
        try
        {
            Assert.Equal([("Ns.Theory.Row", "Ns.Theory.Row(value: 1)")], RunCommand.FailedRows(manifest));
            Assert.Equal("FullyQualifiedName=Ns.Theory.Row&DisplayName=Ns.Theory.Row\\(value: 1\\)",
                Filter.ForTestCase("Ns.Theory.Row", "Ns.Theory.Row(value: 1)"));
        }
        finally { File.Delete(manifest); }
    }

    [Fact]
    public async Task RerunFailed_WithNoRawFailuresReturnsWithoutStartingAWorker()
    {
        string manifest = Path.Combine(Path.GetTempPath(), "pharos-rerun-empty-" + Guid.NewGuid().ToString("N") + ".json");
        File.WriteAllText(manifest, "{\"schemaVersion\":1,\"tests\":[{\"fullyQualifiedName\":\"Ns.Pass\",\"displayName\":\"Passes\",\"rawOutcome\":\"Passed\"}]}");
        try
        {
            RunOptions options = RunOptions.Parse(["does-not-exist.dll", "--rerun-failed", manifest])!;
            StringWriter output = new();
            Assert.Equal(RunCommand.ExitPassed, await RunCommand.RunAsync(options, output, new StringWriter(), CancellationToken.None));
            Assert.Contains("nothing to rerun", output.ToString());
        }
        finally { File.Delete(manifest); }
    }

    [Fact]
    public void Summary_CountsQuarantineSeparatelyFromPassFailAndSkip()
    {
        TrxResult failed = new("Ns.Flaky", "Flaky", "Failed", TimeSpan.Zero, "body failure", null);
        WorkerOutcome outcome = new(new TestGroup("Ns", [failed.FullyQualifiedName]), 1, 1, false, null, TimeSpan.Zero, "worker.log", null, [failed], []);
        RunCommand.Summary summary = new([outcome], [], TimeSpan.Zero, "run.trx", "workers",
            new HashSet<(string FullyQualifiedName, string DisplayName)> { (failed.FullyQualifiedName, failed.TestName) });

        Assert.Equal(0, summary.Passed);
        Assert.Equal(0, summary.Failed);
        Assert.Equal(0, summary.Skipped);
        Assert.Equal(1, summary.QuarantinedCount);
        Assert.Equal(1, summary.Total);
    }

    [Theory]
    [InlineData]
    [InlineData("a.dll", "b.dll")]
    [InlineData("a.dll", "--parallel", "0")]
    [InlineData("a.dll", "--parallel")]
    [InlineData("a.dll", "--group", "method")]
    [InlineData("a.dll", "--bogus")]
    public void BadArguments_AreRefused(params string[] args)
    {
        Assert.Throws<ArgumentException>(() => RunOptions.Parse(args));
    }

    [Fact]
    public void Help_ReturnsNull()
    {
        Assert.Null(RunOptions.Parse(["--help"]));
    }
}

public class TestGroupsTests
{
    [Fact]
    public void Tests_AreGroupedByClass_LargestFirst()
    {
        IReadOnlyList<TestGroup> groups = TestGroups.Create(
            ["Ns.A.One", "Ns.B.One", "Ns.B.Two", "Ns.Outer+Inner.One", "Ns.B.Two"], RunGrouping.Class);

        Assert.Equal(["Ns.B", "Ns.A", "Ns.Outer+Inner"], groups.Select(g => g.Name));
        Assert.Equal(["Ns.B.One", "Ns.B.Two"], groups[0].Tests);
    }

    [Fact]
    public void Tests_AreGroupedByCollection_WhenAsked()
    {
        Dictionary<string, string> collections = new() { ["Ns.A"] = "Shared", ["Ns.B"] = "Shared" };

        IReadOnlyList<TestGroup> groups = TestGroups.Create(["Ns.A.One", "Ns.B.One", "Ns.C.One"], RunGrouping.Collection, collections);

        Assert.Equal(["Shared", "Ns.C"], groups.Select(g => g.Name));
        Assert.Equal(["Ns.A.One", "Ns.B.One"], groups[0].Tests);
    }

    [Fact]
    public void Collections_AreReadFromMetadata_WithInheritance()
    {
        IReadOnlyDictionary<string, string> collections = TestGroups.Collections(typeof(TestGroupsTests).Assembly.Location);

        Assert.Equal("Sequential", collections["Zaldaryon.Pharos.Tests.Server.LiveDataFilesServerTests"]);
        Assert.Equal("ParallelRunTests", collections[typeof(CollectionBase).FullName!]);
        Assert.Equal("ParallelRunTests", collections[typeof(CollectionDerived).FullName!]);
        Assert.Equal("ParallelRunTests", collections[typeof(CollectionDerived).FullName!.Replace('+', '+')]);
        Assert.False(collections.ContainsKey(typeof(TestGroupsTests).FullName!));
    }

    [Fact]
    public void LiveTests_GetAGroupPerClass_AndTheRestShareOne()
    {
        HashSet<string> live = ["Ns.Live1.One", "Ns.Live1.Two", "Ns.Live2.One"];

        IReadOnlyList<TestGroup> groups = TestGroups.Create(["Ns.Live1.One", "Ns.Live1.Two", "Ns.Live2.One", "Ns.Unit1.One", "Ns.Unit2.One"], RunGrouping.Class, live: live);

        Assert.Equal(["Ns.Live1", TestGroups.OtherTestsGroup, "Ns.Live2"], groups.Select(g => g.Name));
        Assert.Equal(["Ns.Unit1.One", "Ns.Unit2.One"], groups[1].Tests);
    }

    [Fact]
    public void LastRunsDurations_StartTheLongestGroupsFirst_AndNewOnesBeforeThem()
    {
        Dictionary<string, TimeSpan> durations = new()
        {
            ["Ns.Short.One"] = TimeSpan.FromSeconds(1),
            ["Ns.Short.Two"] = TimeSpan.FromSeconds(1),
            ["Ns.Long.One"] = TimeSpan.FromMinutes(3),
        };

        IReadOnlyList<TestGroup> groups = TestGroups.Create(["Ns.Short.One", "Ns.Short.Two", "Ns.Long.One", "Ns.New.One"], RunGrouping.Class, durations: durations);

        Assert.Equal(["Ns.New", "Ns.Long", "Ns.Short"], groups.Select(g => g.Name));
        Assert.Equal(TimeSpan.FromSeconds(3), TestGroups.Durations(
        [
            new TrxResult("Ns.A.T", "Ns.A.T(x: 1)", "Passed", TimeSpan.FromSeconds(1), null, null),
            new TrxResult("Ns.A.T", "Ns.A.T(x: 2)", "Passed", TimeSpan.FromSeconds(2), null, null),
        ])["Ns.A.T"]);
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData("Category=Live", true)]
    [InlineData("FullyQualifiedName~Ns.Name=Odd&Category!=Live", true)]
    [InlineData("DisplayName~swrast", false)]
    [InlineData("Category=Live|Name=Foo", false)]
    [InlineData("(Name!~Foo)", false)]
    [InlineData("displayname~x", false)]
    public void Discovery_CanApplyNameAndTraitFilters_Only(string? filter, bool can)
    {
        Assert.Equal(can, RunCommand.DiscoveryCanFilter(filter));
    }

    [Fact]
    public void Listing_GetsFullyQualifiedNameInItsOwnCase()
    {
        Assert.Equal("FullyQualifiedName~Ns.fullyqualifiedname&Category=Live", RunCommand.NormalizeForListing("fullyqualifiedname~Ns.fullyqualifiedname&Category=Live"));
    }

    [Fact]
    public void Filters_MatchExactly_AndEscapeTheirSyntax()
    {
        Assert.Equal("FullyQualifiedName=Ns.A.B|FullyQualifiedName=Ns.A\\(x\\)", Filter.ForGroup(new TestGroup("Ns.A", ["Ns.A.B", "Ns.A(x)"])));
    }

    [Collection("ParallelRunTests")]
    public abstract class CollectionBase;

    public sealed class CollectionDerived : CollectionBase;
}

public class TrxTests
{
    [Fact]
    public void Results_JoinTheirClassAndMethod()
    {
        XDocument trx = Run(("Ns.A", "One", "Ns.A.One(x: 1)", "Passed"), ("Ns.A", "Two", "Ns.A.Two", "Failed"));

        IReadOnlyList<TrxResult> results = Trx.Results(trx);

        Assert.Equal(["Ns.A.One", "Ns.A.Two"], results.Select(r => r.FullyQualifiedName));
        Assert.Equal("Ns.A.One(x: 1)", results[0].TestName);
        Assert.True(results[0].Passed);
        Assert.True(results[1].Failed);
        Assert.Equal("boom", results[1].Message);
    }

    [Fact]
    public void Abort_ComesFromTheRunsErrors()
    {
        XDocument clean = Run(("Ns.A", "One", "Ns.A.One", "Passed"));
        XDocument crashed = Run(("Ns.A", "One", "Ns.A.One", "Passed"));
        crashed.Root!.Add(new XElement(Trx.Ns + "ResultSummary", new XAttribute("outcome", "Failed"),
            new XElement(Trx.Ns + "RunInfos",
                new XElement(Trx.Ns + "RunInfo", new XAttribute("outcome", "Warning"), new XElement(Trx.Ns + "Text", "just a warning")),
                new XElement(Trx.Ns + "RunInfo", new XAttribute("outcome", "Error"), new XElement(Trx.Ns + "Text", "Test host process crashed")))));

        Assert.Null(Trx.Abort(clean));
        Assert.Contains("Test host process crashed", Trx.Abort(crashed));
        Assert.DoesNotContain("just a warning", Trx.Abort(crashed));

        XDocument merged = Trx.Merge([clean, crashed], [], DateTimeOffset.Now, DateTimeOffset.Now, "run");
        Assert.Contains("Test host process crashed", Trx.Abort(merged));
    }

    [Fact]
    public void Merge_KeepsEveryResult_AndFailsTheMissingOnes()
    {
        XDocument first = Run(("Ns.A", "One", "Ns.A.One", "Passed"));
        XDocument second = Run(("Ns.B", "One", "Ns.B.One", "NotExecuted"));
        TrxResult lost = new("Ns.C.One", "Ns.C.One", "Failed", TimeSpan.Zero, "The worker crashed.", null);

        XDocument merged = Trx.Merge([first, second], [lost], DateTimeOffset.Now.AddMinutes(-1), DateTimeOffset.Now, "run");

        IReadOnlyList<TrxResult> results = Trx.Results(merged);
        Assert.Equal(["Ns.A.One", "Ns.B.One", "Ns.C.One"], results.Select(r => r.FullyQualifiedName));
        Assert.Equal("The worker crashed.", results[2].Message);
        XElement counters = merged.Root!.Element(Trx.Ns + "ResultSummary")!.Element(Trx.Ns + "Counters")!;
        Assert.Equal("3", (string?)counters.Attribute("total"));
        Assert.Equal("1", (string?)counters.Attribute("passed"));
        Assert.Equal("1", (string?)counters.Attribute("failed"));
        Assert.Equal("1", (string?)counters.Attribute("notExecuted"));
        Assert.Equal("Failed", (string?)merged.Root!.Element(Trx.Ns + "ResultSummary")!.Attribute("outcome"));
        Assert.Equal(3, merged.Root!.Element(Trx.Ns + "TestEntries")!.Elements().Count());
    }

    private static XDocument Run(params (string ClassName, string Method, string Name, string Outcome)[] tests)
    {
        XNamespace ns = Trx.Ns;
        XElement definitions = new(ns + "TestDefinitions");
        XElement results = new(ns + "Results");
        XElement entries = new(ns + "TestEntries");
        foreach ((string className, string method, string name, string outcome) in tests)
        {
            string id = Guid.NewGuid().ToString();
            string execution = Guid.NewGuid().ToString();
            definitions.Add(new XElement(ns + "UnitTest", new XAttribute("name", name), new XAttribute("id", id),
                new XElement(ns + "TestMethod", new XAttribute("className", className), new XAttribute("name", method))));
            entries.Add(new XElement(ns + "TestEntry", new XAttribute("testId", id), new XAttribute("executionId", execution)));
            XElement result = new(ns + "UnitTestResult", new XAttribute("executionId", execution), new XAttribute("testId", id),
                new XAttribute("testName", name), new XAttribute("outcome", outcome), new XAttribute("duration", "00:00:01.5000000"));
            if (outcome == "Failed")
            {
                result.Add(new XElement(ns + "Output", new XElement(ns + "ErrorInfo", new XElement(ns + "Message", "boom"))));
            }

            results.Add(result);
        }

        return new XDocument(new XElement(ns + "TestRun", results, definitions, entries));
    }
}

/// <summary>
/// Runs <c>pharos run</c> for real against this assembly: each probe class below runs in a worker
/// of its own, and does what the worker's environment asks.
/// </summary>
public class RunCommandTests : IDisposable
{
    private readonly string _results = Path.Combine(Path.GetTempPath(), "pharos-run-" + Guid.NewGuid().ToString("N")[..8]);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_results, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    [Fact]
    public async Task Groups_RunInWorkers_AndTheirResultsAreMerged()
    {
        StringWriter stdout = new();
        int exit = await RunCommand.RunAsync(Options("FullyQualifiedName~ParallelRunProbes&Category!=ProbeFailure"), stdout, new StringWriter(), CancellationToken.None);

        Assert.Equal(RunCommand.ExitPassed, exit);
        IReadOnlyList<TrxResult> results = Trx.Results(XDocument.Load(Path.Combine(_results, "pharos.trx")));
        Assert.Contains(results, r => r.FullyQualifiedName == typeof(ParallelRunProbes.First).FullName + ".Passes" && r.Passed);
        Assert.Contains(results, r => r.FullyQualifiedName == typeof(ParallelRunProbes.Second).FullName + ".Passes" && r.Passed);
        Assert.Contains("Passed!", stdout.ToString());

        using System.Text.Json.JsonDocument manifest = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(_results, "pharos-run.json")));
        Assert.Equal(1, manifest.RootElement.GetProperty("schemaVersion").GetInt32());
        Assert.Equal(typeof(RunCommandTests).Assembly.Location, manifest.RootElement.GetProperty("assembly").GetString());
        Assert.Equal(Path.GetFullPath(Path.Combine(_results, "pharos.trx")), manifest.RootElement.GetProperty("trx").GetString());
        Assert.Equal(2, manifest.RootElement.GetProperty("workers").GetArrayLength());
        Assert.All(manifest.RootElement.GetProperty("tests").EnumerateArray(), test =>
        {
            Assert.Equal("Passed", test.GetProperty("rawOutcome").GetString());
            Assert.False(test.TryGetProperty("flaky", out _)); // No scenario attempt metadata means unknown.
        });
    }

    [Fact]
    public async Task SingleAttemptScenarios_WriteMetadataAndQuarantineOnlyBodyFailure()
    {
        const string runId = "provided-parent-run-id";
        string fqn = typeof(ScenarioAttemptCliProbe).FullName!;
        string otherFqn = typeof(ScenarioAttemptCliProbeOther).FullName!;
        Directory.CreateDirectory(_results);
        string quarantinePath = Path.Combine(_results, "quarantine.json");
        string expiry = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(1).ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
        File.WriteAllText(quarantinePath, System.Text.Json.JsonSerializer.Serialize(new
        {
            schemaVersion = 1,
            entries = new[]
            {
                new { fullyQualifiedName = fqn + ".FailsWhenAsked", owner = "tests", expiresOn = expiry },
                new { fullyQualifiedName = otherFqn + ".FailsWhenAsked", owner = "tests", expiresOn = expiry },
            },
        }));
        RunOptions options = Options($"FullyQualifiedName={fqn}.Passes|FullyQualifiedName={fqn}.FailsWhenAsked|FullyQualifiedName={otherFqn}.FailsWhenAsked") with
        {
            Parallel = 8,
            QuarantineFile = quarantinePath,
            WorkerEnvironment = new Dictionary<string, string>
            {
                ["PHAROS_RUN_ID"] = runId,
                ["PHAROS_PROBE_SCENARIO_FAIL"] = "1",
                ["PHAROS_RUN_EXPLICIT"] = "1",
            },
        };
        StringWriter output = new();
        int exit = await RunCommand.RunAsync(options, output, new StringWriter(), CancellationToken.None);

        Assert.Equal(RunCommand.ExitPassed, exit);
        Assert.Contains("Quarantined: 2", output.ToString());
        Assert.Contains("Failed: 0, Passed: 1, Skipped: 0", output.ToString());
        IReadOnlyList<TrxResult> trx = Trx.Results(XDocument.Load(Path.Combine(_results, "pharos.trx")));
        Assert.Contains(trx, result => result.FullyQualifiedName == fqn + ".Passes" && result.Passed);
        Assert.Contains(trx, result => result.FullyQualifiedName == fqn + ".FailsWhenAsked" && result.Failed);
        Assert.Contains(trx, result => result.FullyQualifiedName == otherFqn + ".FailsWhenAsked" && result.Failed);
        using System.Text.Json.JsonDocument manifest = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(_results, "pharos-run.json")));
        Assert.Equal(runId, manifest.RootElement.GetProperty("runId").GetString());
        System.Text.Json.JsonElement[] tests = manifest.RootElement.GetProperty("tests").EnumerateArray().ToArray();
        Assert.Equal(3, tests.Length);
        System.Text.Json.JsonElement passed = Assert.Single(tests, test => test.GetProperty("fullyQualifiedName").GetString() == fqn + ".Passes");
        Assert.Equal("Passed", passed.GetProperty("rawOutcome").GetString());
        Assert.False(passed.GetProperty("flaky").GetBoolean());
        Assert.Equal(1, passed.GetProperty("attempts").GetArrayLength());
        Assert.Equal("body", passed.GetProperty("attempts")[0].GetProperty("phase").GetString());
        System.Text.Json.JsonElement failed = Assert.Single(tests, test => test.GetProperty("fullyQualifiedName").GetString() == fqn + ".FailsWhenAsked");
        Assert.Equal("Failed", failed.GetProperty("rawOutcome").GetString());
        Assert.Equal("Quarantined", failed.GetProperty("finalOutcome").GetString());
        Assert.True(failed.GetProperty("quarantined").GetBoolean());
        Assert.Equal("body", failed.GetProperty("failurePhase").GetString());
        System.Text.Json.JsonElement otherFailed = Assert.Single(tests, test => test.GetProperty("fullyQualifiedName").GetString() == otherFqn + ".FailsWhenAsked");
        Assert.Equal("Quarantined", otherFailed.GetProperty("finalOutcome").GetString());

        string rerunResults = Path.Combine(_results, "rerun");
        RunOptions rerun = Options("") with
        {
            ResultsDirectory = rerunResults,
            Parallel = 8,
            RerunFailed = Path.Combine(_results, "pharos-run.json"),
            Filter = "FullyQualifiedName=" + fqn + ".FailsWhenAsked",
            WorkerEnvironment = new Dictionary<string, string> { ["PHAROS_PROBE_SCENARIO_FAIL"] = "1" },
        };
        StringWriter rerunOutput = new();
        Assert.Equal(RunCommand.ExitFailed, await RunCommand.RunAsync(rerun, rerunOutput, new StringWriter(), CancellationToken.None));
        Assert.Contains("Rerunning 2 failed test row", rerunOutput.ToString());
        IReadOnlyList<TrxResult> rerunTrx = Trx.Results(XDocument.Load(Path.Combine(rerunResults, "pharos.trx")));
        Assert.Single(rerunTrx);
        Assert.Equal(fqn + ".FailsWhenAsked", rerunTrx[0].FullyQualifiedName);
        Assert.True(rerunTrx[0].Failed);
        using System.Text.Json.JsonDocument rerunManifest = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(rerunResults, "pharos-run.json")));
        Assert.DoesNotContain(rerunManifest.RootElement.GetProperty("tests").EnumerateArray(), test => test.GetProperty("fullyQualifiedName").GetString() == otherFqn + ".FailsWhenAsked");
    }

    [Fact]
    public async Task Repeat_RunsIndependentExecutionsAndWritesAnAggregateManifest()
    {
        StringWriter stdout = new();
        int exit = await RunCommand.RunAsync(Options("FullyQualifiedName~ParallelRunProbes&Category!=ProbeFailure") with { Repeat = 2 }, stdout, new StringWriter(), CancellationToken.None);

        Assert.Equal(RunCommand.ExitPassed, exit);
        Assert.True(File.Exists(Path.Combine(_results, "run-001", "pharos.trx")));
        Assert.True(File.Exists(Path.Combine(_results, "run-002", "pharos.trx")));
        using System.Text.Json.JsonDocument aggregate = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(_results, "pharos-repeat.json")));
        Assert.Equal("repeat", aggregate.RootElement.GetProperty("mode").GetString());
        Assert.Equal(2, aggregate.RootElement.GetProperty("completedRuns").GetInt32());
        Assert.Equal(2, aggregate.RootElement.GetProperty("runs").GetArrayLength());
    }

    [Fact]
    public async Task UntilFail_StopsAtTheFirstFailedExecution()
    {
        RunOptions options = Options("FullyQualifiedName~ParallelRunProbes&Category=ProbeFailure") with
        {
            UntilFail = true,
            Max = 3,
            WorkerEnvironment = new Dictionary<string, string> { ["PHAROS_PROBE_FAIL"] = "1" },
        };

        int exit = await RunCommand.RunAsync(options, new StringWriter(), new StringWriter(), CancellationToken.None);

        Assert.Equal(RunCommand.ExitFailed, exit);
        using System.Text.Json.JsonDocument aggregate = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(_results, "pharos-repeat.json")));
        Assert.Equal(1, aggregate.RootElement.GetProperty("completedRuns").GetInt32());
        Assert.Equal("until-fail", aggregate.RootElement.GetProperty("mode").GetString());
    }

    [Fact]
    public async Task AWorkerThatCrashes_IsReportedWhateverTheSystemLanguage()
    {
        // vstest writes its messages in the worker's language, and the abort is recognized by its English text. The worker is therefore
        // asked for English output, so a Portuguese system still reports the crash. The locale is set for the worker only.
        StringWriter stdout = new();
        RunOptions options = Options("FullyQualifiedName~ParallelRunProbes&Category!=ProbeFailure") with
        {
            WorkerEnvironment = new Dictionary<string, string>
            {
                ["PHAROS_PROBE_CRASH"] = typeof(ParallelRunProbes.First).FullName!,
                ["LANG"] = "pt_BR.UTF-8",
                ["LC_ALL"] = "pt_BR.UTF-8",
            },
        };

        int exit = await RunCommand.RunAsync(options, stdout, new StringWriter(), CancellationToken.None);

        Assert.Equal(RunCommand.ExitFailed, exit);
        IReadOnlyList<TrxResult> results = Trx.Results(XDocument.Load(Path.Combine(_results, "pharos.trx")));
        Assert.Contains(results, r => r.FullyQualifiedName == typeof(ParallelRunProbes.First).FullName + ".(worker aborted)" && r.Failed);
    }

    [Fact]
    public async Task AWorkerThatCrashes_FailsItsClass_AndTheOthersStillRun()
    {
        StringWriter stdout = new();
        RunOptions options = Options("FullyQualifiedName~ParallelRunProbes&Category!=ProbeFailure") with
        {
            WorkerEnvironment = new Dictionary<string, string> { ["PHAROS_PROBE_CRASH"] = typeof(ParallelRunProbes.First).FullName! },
        };

        int exit = await RunCommand.RunAsync(options, stdout, new StringWriter(), CancellationToken.None);

        Assert.Equal(RunCommand.ExitFailed, exit);
        IReadOnlyList<TrxResult> results = Trx.Results(XDocument.Load(Path.Combine(_results, "pharos.trx")));
        Assert.Contains(results, r => r.FullyQualifiedName == typeof(ParallelRunProbes.First).FullName + ".(worker aborted)" && r.Failed);
        Assert.DoesNotContain(results, r => r.FullyQualifiedName == typeof(ParallelRunProbes.First).FullName + ".Passes" && r.Passed);
        Assert.Contains(results, r => r.FullyQualifiedName.Contains("+Second.") && r.Passed);
        Assert.Contains("CRASHED", stdout.ToString());
    }

    [Fact]
    public async Task ATestThatHangs_StopsItsTestHost_AndFailsItsClass()
    {
        StringWriter stdout = new();
        RunOptions options = Options("FullyQualifiedName~ParallelRunProbes&Category!=ProbeFailure") with
        {
            TestTimeout = TimeSpan.FromSeconds(15),
            WorkerEnvironment = new Dictionary<string, string> { ["PHAROS_PROBE_HANG"] = typeof(ParallelRunProbes.Second).FullName! },
        };

        int exit = await RunCommand.RunAsync(options, stdout, new StringWriter(), CancellationToken.None);

        Assert.Equal(RunCommand.ExitFailed, exit);
        IReadOnlyList<TrxResult> results = Trx.Results(XDocument.Load(Path.Combine(_results, "pharos.trx")));
        Assert.Contains(results, r => r.FullyQualifiedName == typeof(ParallelRunProbes.Second).FullName + ".(worker aborted)" && r.Failed);
        Assert.Contains(results, r => r.FullyQualifiedName.Contains("+First.") && r.Passed);
        Assert.Contains("CRASHED", stdout.ToString());
    }

    [Fact]
    public async Task AWorkerThatCrashesAfterItsLastTest_StillFailsItsClass()
    {
        StringWriter stdout = new();
        RunOptions options = Options("FullyQualifiedName~ParallelRunProbes&Category!=ProbeFailure") with
        {
            WorkerEnvironment = new Dictionary<string, string> { ["PHAROS_PROBE_CRASH_LATE"] = "1" },
        };

        int exit = await RunCommand.RunAsync(options, stdout, new StringWriter(), CancellationToken.None);

        Assert.Equal(RunCommand.ExitFailed, exit);
        IReadOnlyList<TrxResult> results = Trx.Results(XDocument.Load(Path.Combine(_results, "pharos.trx")));
        Assert.Contains(results, r => r.FullyQualifiedName == typeof(ParallelRunProbes.Second).FullName + ".(worker aborted)" && r.Failed);
    }

    [Fact]
    public async Task AFailingTest_FailsTheRun_WithItsMessage()
    {
        StringWriter stdout = new();
        RunOptions options = Options("FullyQualifiedName~ParallelRunProbes&Category=ProbeFailure") with
        {
            WorkerEnvironment = new Dictionary<string, string> { ["PHAROS_PROBE_FAIL"] = "1" },
        };

        int exit = await RunCommand.RunAsync(options, stdout, new StringWriter(), CancellationToken.None);

        Assert.Equal(RunCommand.ExitFailed, exit);
        Assert.Contains("the probe failed", stdout.ToString());
        Assert.Contains("Failed!", stdout.ToString());

        // A failing test is no crash: one failure, and no aborted worker.
        Assert.Contains("0 passed, 1 failed, 0 skipped", stdout.ToString());
        Assert.DoesNotContain("CRASHED", stdout.ToString());
        IReadOnlyList<TrxResult> results = Trx.Results(XDocument.Load(Path.Combine(_results, "pharos.trx")));
        Assert.Single(results);
        Assert.Null(Trx.Abort(XDocument.Load(Path.Combine(_results, "workers", "1.trx"))));
    }

    [Fact]
    public async Task Json_WritesOneEventPerLine()
    {
        StringWriter stdout = new();
        await RunCommand.RunAsync(Options("FullyQualifiedName~ParallelRunProbes&Category!=ProbeFailure") with { Json = true }, stdout, new StringWriter(), CancellationToken.None);

        List<System.Text.Json.JsonElement> events = stdout.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => System.Text.Json.JsonDocument.Parse(line).RootElement).ToList();
        Assert.Equal("listed", events[0].GetProperty("event").GetString());
        Assert.Equal(2, events.Count(e => e.GetProperty("event").GetString() == "groupFinished"));
        Assert.Equal(2, events.Count(e => e.GetProperty("event").GetString() == "test"));
        Assert.Equal("summary", events[^1].GetProperty("event").GetString());
        Assert.Equal(2, events[^1].GetProperty("passed").GetInt32());
    }

    [Fact]
    public async Task List_RunsNothing()
    {
        StringWriter stdout = new();
        int exit = await RunCommand.RunAsync(Options("FullyQualifiedName~ParallelRunProbes") with { ListOnly = true }, stdout, new StringWriter(), CancellationToken.None);

        Assert.Equal(RunCommand.ExitPassed, exit);
        Assert.Contains(typeof(ParallelRunProbes.First).FullName + ".Passes", stdout.ToString());
        Assert.False(Directory.Exists(Path.Combine(_results, "workers")));
    }

    private RunOptions Options(string filter) => new()
    {
        Target = typeof(RunCommandTests).Assembly.Location,
        Filter = filter,
        Parallel = 2,
        ResultsDirectory = _results,
        Xvfb = XvfbMode.Never,
    };
}

/// <summary>Tests <see cref="RunCommandTests"/> runs in workers; elsewhere they pass at once.</summary>
public static class ParallelRunProbes
{
    private static void Act(Type probe)
    {
        if (Environment.GetEnvironmentVariable("PHAROS_PROBE_CRASH") == probe.FullName) AbortWorker("pharos run probe: crashing as asked");
        if (Environment.GetEnvironmentVariable("PHAROS_PROBE_HANG") == probe.FullName) Thread.Sleep(Timeout.Infinite);
    }

    private static void AbortWorker(string message)
    {
        // Exit abruptly without invoking Windows crash reporting or opening a system dialog.
        // The parent still observes a failed worker, including after its last test passed.
        Console.Error.WriteLine(message);
        Environment.Exit(134);
    }

    public class First
    {
        [Fact]
        public void Passes() => Act(typeof(First));
    }

    public class Second : IClassFixture<Second.LateCrash>
    {
        [Fact]
        public void Passes() => Act(typeof(Second));

        /// <summary>Crashes the worker once the class's tests are done, when asked.</summary>
        public sealed class LateCrash : IDisposable
        {
            public void Dispose()
            {
                if (Environment.GetEnvironmentVariable("PHAROS_PROBE_CRASH_LATE") == "1") AbortWorker("pharos run probe: crashing after the last test");
            }
        }
    }

    [Trait("Category", "ProbeFailure")]
    public class Failing
    {
        [Fact]
        public void Fails()
        {
            if (Environment.GetEnvironmentVariable("PHAROS_PROBE_FAIL") == "1") Assert.Fail("the probe failed");
        }
    }
}

/// <summary>Simple scenario used to prove the CLI writes metadata for default one-attempt runs.</summary>
public sealed class ScenarioAttemptCliProbe
{
    [Zaldaryon.Pharos.XUnit.ServerScenario]
    public void Passes() { }

    [Zaldaryon.Pharos.XUnit.ServerScenario(Explicit = true)]
    public void FailsWhenAsked()
    {
        if (Environment.GetEnvironmentVariable("PHAROS_PROBE_SCENARIO_FAIL") == "1") Xunit.Assert.Fail("body failed for quarantine probe");
    }
}

/// <summary>Second failed row used to verify rerun filtering intersects the user's FQN selection.</summary>
public sealed class ScenarioAttemptCliProbeOther
{
    [Zaldaryon.Pharos.XUnit.ServerScenario(Explicit = true)]
    public void FailsWhenAsked()
    {
        if (Environment.GetEnvironmentVariable("PHAROS_PROBE_SCENARIO_FAIL") == "1") Xunit.Assert.Fail("second body failure for rerun filter probe");
    }
}
