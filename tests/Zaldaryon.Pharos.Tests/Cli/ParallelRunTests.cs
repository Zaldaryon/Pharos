using System.Xml.Linq;
using Xunit;
using Zaldaryon.Pharos.Cli.ParallelRuns;

namespace Zaldaryon.Pharos.Tests.Cli;

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
        if (Environment.GetEnvironmentVariable("PHAROS_PROBE_CRASH") == probe.FullName) Environment.FailFast("pharos run probe: crashing as asked");
        if (Environment.GetEnvironmentVariable("PHAROS_PROBE_HANG") == probe.FullName) Thread.Sleep(Timeout.Infinite);
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
                if (Environment.GetEnvironmentVariable("PHAROS_PROBE_CRASH_LATE") == "1") Environment.FailFast("pharos run probe: crashing after the last test");
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
