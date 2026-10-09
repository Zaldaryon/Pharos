using System.Text.Json;
using System.Xml.Linq;
using Xunit;
using Zaldaryon.Pharos.Cli;
using Zaldaryon.Pharos.Cli.ParallelRuns;

namespace Zaldaryon.Pharos.Tests.Cli;

public class DiffCommandTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "pharos-diff-" + Guid.NewGuid().ToString("N")[..8]);

    public DiffCommandTests() => Directory.CreateDirectory(_folder);

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    private static TrxResult R(string name, string outcome, double seconds = 1, string? message = null) =>
        new("My.Tests." + name.Split('(')[0], name, outcome, TimeSpan.FromSeconds(seconds), message, null);

    [Fact]
    public void Compare_SortsEachTestIntoWhatChanged()
    {
        TrxResult[] before =
        [
            R("Breaks", "Passed"), R("Heals", "Failed"), R("StillBroken", "Failed"), R("Gone", "Passed"),
            R("Slows", "Passed", 2), R("Speeds", "Passed", 10), R("Steady", "Passed", 2), R("WasSkipped", "NotExecuted"),
            R("Rows(1)", "Passed"), R("Rows(2)", "Passed"),
        ];
        TrxResult[] after =
        [
            R("Breaks", "Failed", message: "boom\nat line 3"), R("Heals", "Passed"), R("StillBroken", "Failed"), R("Brand", "Failed"),
            R("Slows", "Passed", 5), R("Speeds", "Passed", 3), R("Steady", "Passed", 2.5), R("WasSkipped", "Failed"),
            R("Rows(1)", "Passed"), R("Rows(2)", "Failed"),
        ];

        RunDiff diff = DiffCommand.Compare(before, after, slowerThan: 0.5, minDelta: TimeSpan.FromSeconds(1));

        Assert.Equal(["Brand", "Breaks", "Rows(2)", "WasSkipped"], diff.NewFailures.Select(c => c.Test));
        Assert.Equal("boom\nat line 3", diff.NewFailures.Single(c => c.Test == "Breaks").Message);
        Assert.Equal(["Heals"], diff.Fixed.Select(c => c.Test));
        Assert.Equal(["StillBroken"], diff.StillFailing.Select(c => c.Test));
        Assert.Equal(["Gone"], diff.Vanished.Select(c => c.Test));
        Assert.Equal(["Brand"], diff.Added.Select(c => c.Test));
        Assert.Equal(["Slows"], diff.Slower.Select(c => c.Test));
        Assert.Equal(["Speeds"], diff.Faster.Select(c => c.Test));
    }

    [Fact]
    public void Compare_IgnoresSmallSlowdowns_UnderTheMinimumDelta()
    {
        RunDiff diff = DiffCommand.Compare([R("Quick", "Passed", 0.1)], [R("Quick", "Passed", 0.5)]);
        Assert.Empty(diff.Slower);
    }

    [Fact]
    public void Run_ExitsOneOnANewFailure_AndZeroWithoutOne()
    {
        string before = Write("before.trx", ("A", "Passed", 1.0), ("B", "Passed", 1.0));
        string broken = Write("broken.trx", ("A", "Failed", 1.0), ("B", "Passed", 1.0));
        string same = Write("same.trx", ("A", "Passed", 1.0), ("B", "Passed", 1.0));

        StringWriter output = new();
        Assert.Equal(DiffCommand.ExitRegressions, DiffCommand.Run([before, broken], output, new StringWriter()));
        Assert.Contains("New failures (1):", output.ToString());
        Assert.Contains("A (passed -> failed)", output.ToString());
        Assert.Contains("Regressions: 1 new failure(s).", output.ToString());

        Assert.Equal(DiffCommand.ExitNoRegressions, DiffCommand.Run([before, same], new StringWriter(), new StringWriter()));
    }

    [Fact]
    public void Run_CountsSlowerAndVanishedTests_OnlyWhenAsked()
    {
        string before = Write("before.trx", ("A", "Passed", 1.0), ("B", "Passed", 1.0));
        string after = Write("after.trx", ("A", "Passed", 10.0));

        Assert.Equal(DiffCommand.ExitNoRegressions, DiffCommand.Run([before, after], new StringWriter(), new StringWriter()));
        Assert.Equal(DiffCommand.ExitRegressions, DiffCommand.Run([before, after, "--fail-on-slower"], new StringWriter(), new StringWriter()));
        StringWriter output = new();
        Assert.Equal(DiffCommand.ExitRegressions, DiffCommand.Run([before, after, "--fail-on-vanished"], output, new StringWriter()));
        Assert.Contains("Regressions: 1 vanished test(s).", output.ToString());
    }

    [Fact]
    public void Compare_ReportsTestsSkippedNow_AndNeverCountsThemFixed()
    {
        RunDiff diff = DiffCommand.Compare(
            [R("WasPassing", "Passed"), R("WasFailing", "Failed"), R("StaysSkipped", "NotExecuted")],
            [R("WasPassing", "NotExecuted"), R("WasFailing", "NotExecuted"), R("StaysSkipped", "NotExecuted")]);

        Assert.Equal(["WasFailing", "WasPassing"], diff.NewlySkipped.Select(c => c.Test));
        Assert.Empty(diff.Fixed);
        Assert.Empty(diff.NewFailures);
    }

    [Fact]
    public void Run_CountsNewlySkippedTests_OnlyWhenAsked()
    {
        string before = Write("before.trx", ("A", "Passed", 1.0));
        string after = Write("after.trx", ("A", "NotExecuted", 0.0));

        StringWriter output = new();
        Assert.Equal(DiffCommand.ExitNoRegressions, DiffCommand.Run([before, after], output, new StringWriter()));
        Assert.Contains("Newly skipped (1):", output.ToString());
        Assert.Equal(DiffCommand.ExitRegressions, DiffCommand.Run([before, after, "--fail-on-skipped"], new StringWriter(), new StringWriter()));
    }

    [Fact]
    public void Run_CountsASecondRunThatStoppedEarly_OrHasNoResults_AsARegression()
    {
        string before = Write("before.trx", ("A", "Passed", 1.0), ("B", "Passed", 1.0));
        string crashed = Write("crashed.trx", [("A", "Passed", 1.0)], abort: "The active test run was aborted. Reason: Test host process crashed");
        string empty = Write("empty.trx");

        StringWriter output = new();
        Assert.Equal(DiffCommand.ExitRegressions, DiffCommand.Run([before, crashed], output, new StringWriter()));
        Assert.Contains("The second run stopped early", output.ToString());
        Assert.Equal(DiffCommand.ExitRegressions, DiffCommand.Run([before, empty], new StringWriter(), new StringWriter()));
        Assert.Equal(DiffCommand.ExitNoRegressions, DiffCommand.Run([empty, empty], new StringWriter(), new StringWriter()));
    }

    [Fact]
    public void Compare_KeepsTestsOfTheSameNameInTwoClassesApart()
    {
        TrxResult a = new("My.First.Works", "Works", "Passed", TimeSpan.FromSeconds(1), null, null);
        TrxResult b = new("My.Second.Works", "Works", "Passed", TimeSpan.FromSeconds(1), null, null);

        RunDiff diff = DiffCommand.Compare([a, b], [a, b with { Outcome = "Failed" }]);

        TestChange failure = Assert.Single(diff.NewFailures);
        Assert.Equal("My.Second.Works", failure.FullyQualifiedName);
        Assert.Equal(2, diff.AfterCount);
    }

    [Fact]
    public void Compare_KeepsTheWorstResultOfATestReportedTwice_AndCountsTheDuplicates()
    {
        RunDiff diff = DiffCommand.Compare(
            [R("A", "Passed"), R("B", "Passed")],
            [R("A", "NotExecuted"), R("A", "Passed"), R("B", "Passed"), R("B", "Failed")]);

        Assert.Equal(["B"], diff.NewFailures.Select(c => c.Test));
        Assert.Empty(diff.NewlySkipped);
        Assert.Equal(2, diff.AfterDuplicates);
        Assert.Equal(0, diff.BeforeDuplicates);
    }

    [Theory]
    [InlineData("PassedButRunAborted", false)]
    [InlineData("NotRunnable", true)]
    [InlineData("Disconnected", true)]
    [InlineData("Inconclusive", false)]
    public void Compare_ReadsTheRarerOutcomes(string outcome, bool fails) =>
        Assert.Equal(fails, DiffCommand.Compare([R("A", "Passed")], [R("A", outcome)]).NewFailures.Count == 1);

    [Fact]
    public void Compare_DecidesByTheDeltaAlone_WhenATestTookNoTimeBefore()
    {
        Assert.Single(DiffCommand.Compare([R("A", "Passed", 0)], [R("A", "Passed", 2)]).Slower);
        Assert.Empty(DiffCommand.Compare([R("A", "Passed", 0)], [R("A", "Passed", 0.5)]).Slower);
    }

    [Fact]
    public void Run_Json_ListsTheChanges()
    {
        string before = Write("before.trx", ("A", "Passed", 1.0));
        string after = Write("after.trx", ("A", "Failed", 1.0));
        StringWriter output = new();

        DiffCommand.Run([before, after, "--json"], output, new StringWriter());

        using JsonDocument json = JsonDocument.Parse(output.ToString());
        JsonElement root = json.RootElement;
        Assert.Equal(
            ["schemaVersion", "before", "after", "slowerThan", "minDeltaSeconds", "regressed", "exitCode",
             "newFailures", "fixed", "stillFailing", "newlySkipped", "vanished", "added", "slower", "faster",
             "beforeCount", "afterCount", "beforeDuplicates", "afterDuplicates"],
            root.EnumerateObject().Select(p => p.Name));
        Assert.Equal(1, root.GetProperty("schemaVersion").GetInt32());
        Assert.True(root.GetProperty("regressed").GetBoolean());
        Assert.Equal(1, root.GetProperty("exitCode").GetInt32());
        Assert.Equal(0.5, root.GetProperty("slowerThan").GetDouble());

        JsonElement failure = root.GetProperty("newFailures")[0];
        Assert.Equal(
            ["test", "fullyQualifiedName", "before", "after", "beforeDuration", "afterDuration", "beforeSeconds", "afterSeconds"],
            failure.EnumerateObject().Select(p => p.Name));
        Assert.Equal("A", failure.GetProperty("test").GetString());
        Assert.Equal("Passed", failure.GetProperty("before").GetString());
        Assert.Equal("00:00:01", failure.GetProperty("afterDuration").GetString());
        Assert.Equal(1.0, failure.GetProperty("afterSeconds").GetDouble());
        Assert.Equal(1, root.GetProperty("afterCount").GetInt32());
    }

    [Theory]
    [InlineData("only-one.trx")]
    [InlineData("a.trx", "b.trx", "--slower-than", "lots")]
    [InlineData("a.trx", "b.trx", "--nope")]
    [InlineData("a.trx", "b.trx", "--min-delta", "1e20")]
    [InlineData("a.trx", "b.trx", "--min-delta", "Infinity")]
    [InlineData("a.trx", "b.trx", "--slower-than", "Infinity")]
    public void Run_ExitsTwoOnBadArguments(params string[] args) =>
        Assert.Equal(DiffCommand.ExitError, DiffCommand.Run(args, new StringWriter(), new StringWriter()));

    [Fact]
    public void Run_ExitsTwoOnAMissingOrBrokenFile()
    {
        string broken = Path.Combine(_folder, "broken.trx");
        File.WriteAllText(broken, "not xml");
        Assert.Equal(DiffCommand.ExitError, DiffCommand.Run([Path.Combine(_folder, "missing.trx"), broken], new StringWriter(), new StringWriter()));
        Assert.Equal(DiffCommand.ExitError, DiffCommand.Run([broken, broken], new StringWriter(), new StringWriter()));

        string notTrx = Path.Combine(_folder, "other.xml");
        File.WriteAllText(notTrx, "<foo/>");
        StringWriter error = new();
        Assert.Equal(DiffCommand.ExitError, DiffCommand.Run([notTrx, notTrx], new StringWriter(), error));
        Assert.Contains("not a TRX file", error.ToString());
        Assert.DoesNotContain("Usage:", error.ToString());

        error = new StringWriter();
        Assert.Equal(DiffCommand.ExitError, DiffCommand.Run([_folder, notTrx], new StringWriter(), error));
        Assert.Contains("is a folder", error.ToString());
    }

    [Fact]
    public void Help_IsReachableThroughTheCli()
    {
        StringWriter output = new();
        Assert.Equal(0, PhCliRunner.Run(["help", "diff"], output, new StringWriter()));
        Assert.Contains("pharos diff - compare two test runs", output.ToString());
    }

    [Fact]
    public void Options_ReadPercentagesAndSeconds()
    {
        DiffOptions options = DiffOptions.Parse(["a", "b", "--slower-than", "25%", "--min-delta", "0.5"])!;
        Assert.Equal(0.25, options.SlowerThan, 3);
        Assert.Equal(TimeSpan.FromSeconds(0.5), options.MinDelta);
        Assert.Null(DiffOptions.Parse(["--help"]));
    }

    // A TRX as vstest writes it: definitions joined to results by the test's id.
    private string Write(string name, params (string Test, string Outcome, double Seconds)[] tests) => Write(name, tests, abort: null);

    private string Write(string name, (string Test, string Outcome, double Seconds)[] tests, string? abort)
    {
        XNamespace ns = Trx.Ns;
        XElement definitions = new(ns + "TestDefinitions");
        XElement results = new(ns + "Results");
        foreach ((string test, string outcome, double seconds) in tests)
        {
            string id = Guid.NewGuid().ToString();
            definitions.Add(new XElement(ns + "UnitTest", new XAttribute("id", id), new XAttribute("name", test),
                new XElement(ns + "TestMethod", new XAttribute("className", "My.Tests"), new XAttribute("name", test))));
            results.Add(new XElement(ns + "UnitTestResult", new XAttribute("testId", id), new XAttribute("testName", test),
                new XAttribute("outcome", outcome), new XAttribute("duration", TimeSpan.FromSeconds(seconds).ToString("c"))));
        }

        string path = Path.Combine(_folder, name);
        XElement summary = new(ns + "ResultSummary", new XAttribute("outcome", abort == null ? "Completed" : "Failed"));
        if (abort != null) summary.Add(new XElement(ns + "RunInfos", new XElement(ns + "RunInfo", new XAttribute("outcome", "Error"), new XElement(ns + "Text", abort))));
        new XDocument(new XElement(ns + "TestRun", definitions, results, summary)).Save(path);
        return path;
    }
}
