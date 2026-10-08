using System.Globalization;
using System.Xml.Linq;

namespace Zaldaryon.Pharos.Cli.ParallelRuns;

/// <summary>One test result read from a TRX file, or made up for a test a worker never reported.</summary>
internal sealed record TrxResult(
    string FullyQualifiedName,
    string TestName,
    string Outcome,
    TimeSpan Duration,
    string? Message,
    string? StackTrace)
{
    public bool Passed => Outcome == "Passed";

    public bool Failed => Outcome is "Failed" or "Error" or "Timeout" or "Aborted";

    public bool Skipped => !Passed && !Failed;
}

/// <summary>Reads the TRX files workers write and merges them into one.</summary>
/// <remarks>
/// A TRX holds each test case twice: a <c>UnitTest</c> definition, whose <c>TestMethod</c> gives
/// the class and method, and a <c>UnitTestResult</c> with the outcome, joined by the test's id.
/// </remarks>
internal static class Trx
{
    public static readonly XNamespace Ns = "http://microsoft.com/schemas/VisualStudio/TeamTest/2010";

    private const string UnitTestType = "13cdc9d9-ddb5-4fa4-a97d-d965ccfc6d4b";
    private const string NotInAListId = "8c84fa94-04c1-424b-9868-57a2d4851a1d";
    private const string AllLoadedId = "19431567-8539-422a-85d7-44ee4e166bda";

    /// <summary>The results in <paramref name="trx"/>.</summary>
    public static IReadOnlyList<TrxResult> Results(XDocument trx)
    {
        XElement root = trx.Root ?? throw new FormatException("The TRX file is empty.");
        Dictionary<string, string> fqns = [];
        foreach (XElement test in root.Elements(Ns + "TestDefinitions").Elements(Ns + "UnitTest"))
        {
            XElement? method = test.Element(Ns + "TestMethod");
            if ((string?)test.Attribute("id") is { } id && method != null)
            {
                fqns[id] = $"{(string?)method.Attribute("className")}.{(string?)method.Attribute("name")}";
            }
        }

        List<TrxResult> results = [];
        foreach (XElement result in root.Elements(Ns + "Results").Elements(Ns + "UnitTestResult"))
        {
            string name = (string?)result.Attribute("testName") ?? "";
            string fqn = (string?)result.Attribute("testId") is { } testId && fqns.TryGetValue(testId, out string? known) ? known : name;
            XElement? error = result.Element(Ns + "Output")?.Element(Ns + "ErrorInfo");
            results.Add(new TrxResult(
                fqn,
                name,
                (string?)result.Attribute("outcome") ?? "NotExecuted",
                TimeSpan.TryParse((string?)result.Attribute("duration"), CultureInfo.InvariantCulture, out TimeSpan duration) ? duration : TimeSpan.Zero,
                (string?)error?.Element(Ns + "Message"),
                (string?)error?.Element(Ns + "StackTrace")));
        }

        return results;
    }

    // What vstest writes when the test host dies or is stopped. The xUnit adapter also records an
    // error for every failing test, which is no abort.
    private static readonly string[] s_abortMarkers = ["test run was aborted", "Test host process crashed", "Testhost process exited", "testhost process exited"];

    /// <summary>
    /// Why the run in <paramref name="trx"/> stopped early, from the errors vstest recorded (a
    /// test host that crashed, or one stopped as hung), or null when it ran to its end.
    /// </summary>
    public static string? Abort(XDocument trx)
    {
        XElement? summary = trx.Root?.Element(Ns + "ResultSummary");
        List<string> errors = summary?.Element(Ns + "RunInfos")?.Elements(Ns + "RunInfo")
            .Where(info => (string?)info.Attribute("outcome") == "Error")
            .Select(info => ((string?)info.Element(Ns + "Text") ?? "").Trim())
            .Where(text => s_abortMarkers.Any(marker => text.Contains(marker, StringComparison.OrdinalIgnoreCase)))
            .ToList() ?? [];
        if (errors.Count > 0) return "The test host stopped early: " + string.Join("\n", errors);
        return (string?)summary?.Attribute("outcome") == "Aborted" ? "The test run was aborted." : null;
    }

    /// <summary>
    /// One TRX holding every test of <paramref name="runs"/>, plus a failed result for each test in
    /// <paramref name="missing"/>, which no worker reported.
    /// </summary>
    public static XDocument Merge(IEnumerable<XDocument> runs, IEnumerable<TrxResult> missing, DateTimeOffset start, DateTimeOffset finish, string runName)
    {
        XElement definitions = new(Ns + "TestDefinitions");
        XElement entries = new(Ns + "TestEntries");
        XElement results = new(Ns + "Results");
        XElement infos = new(Ns + "RunInfos");
        Counters counters = new();

        foreach (XDocument run in runs)
        {
            XElement root = run.Root!;
            foreach (XElement test in root.Elements(Ns + "TestDefinitions").Elements()) definitions.Add(new XElement(test));
            foreach (XElement entry in root.Elements(Ns + "TestEntries").Elements()) entries.Add(new XElement(entry));
            foreach (XElement info in root.Elements(Ns + "ResultSummary").Elements(Ns + "RunInfos").Elements()) infos.Add(new XElement(info));
            foreach (XElement result in root.Elements(Ns + "Results").Elements())
            {
                results.Add(new XElement(result));
                counters.Add((string?)result.Attribute("outcome"));
            }
        }

        foreach (TrxResult lost in missing)
        {
            string testId = Guid.NewGuid().ToString();
            string executionId = Guid.NewGuid().ToString();
            int dot = lost.FullyQualifiedName.LastIndexOf('.');
            definitions.Add(new XElement(Ns + "UnitTest",
                new XAttribute("name", lost.TestName),
                new XAttribute("id", testId),
                new XElement(Ns + "Execution", new XAttribute("id", executionId)),
                new XElement(Ns + "TestMethod",
                    new XAttribute("className", dot > 0 ? lost.FullyQualifiedName[..dot] : ""),
                    new XAttribute("name", dot > 0 ? lost.FullyQualifiedName[(dot + 1)..] : lost.FullyQualifiedName),
                    new XAttribute("adapterTypeName", "executor://pharos/run"))));
            entries.Add(new XElement(Ns + "TestEntry",
                new XAttribute("testId", testId), new XAttribute("executionId", executionId), new XAttribute("testListId", NotInAListId)));
            results.Add(new XElement(Ns + "UnitTestResult",
                new XAttribute("executionId", executionId),
                new XAttribute("testId", testId),
                new XAttribute("testName", lost.TestName),
                new XAttribute("computerName", Environment.MachineName),
                new XAttribute("duration", lost.Duration.ToString("c", CultureInfo.InvariantCulture)),
                new XAttribute("testType", UnitTestType),
                new XAttribute("outcome", "Failed"),
                new XAttribute("testListId", NotInAListId),
                new XElement(Ns + "Output",
                    new XElement(Ns + "ErrorInfo",
                        new XElement(Ns + "Message", lost.Message ?? ""),
                        new XElement(Ns + "StackTrace", lost.StackTrace ?? "")))));
            counters.Add("Failed");
        }

        string time(DateTimeOffset t) => t.ToString("o", CultureInfo.InvariantCulture);
        return new XDocument(
            new XDeclaration("1.0", "utf-8", null),
            new XElement(Ns + "TestRun",
                new XAttribute("id", Guid.NewGuid().ToString()),
                new XAttribute("name", runName),
                new XElement(Ns + "Times",
                    new XAttribute("creation", time(finish)), new XAttribute("queuing", time(start)),
                    new XAttribute("start", time(start)), new XAttribute("finish", time(finish))),
                results,
                definitions,
                entries,
                new XElement(Ns + "TestLists",
                    new XElement(Ns + "TestList", new XAttribute("name", "Results Not in a List"), new XAttribute("id", NotInAListId)),
                    new XElement(Ns + "TestList", new XAttribute("name", "All Loaded Results"), new XAttribute("id", AllLoadedId))),
                new XElement(Ns + "ResultSummary",
                    new XAttribute("outcome", counters.Failed > 0 ? "Failed" : "Completed"),
                    counters.ToElement(),
                    infos.HasElements ? infos : null)));
    }

    private sealed class Counters
    {
        public int Total;
        public int Executed;
        public int Passed;
        public int Failed;
        public int NotExecuted;

        public void Add(string? outcome)
        {
            Total++;
            switch (outcome)
            {
                case "Passed":
                    Executed++;
                    Passed++;
                    break;
                case "Failed" or "Error" or "Timeout" or "Aborted":
                    Executed++;
                    Failed++;
                    break;
                default:
                    NotExecuted++;
                    break;
            }
        }

        public XElement ToElement() => new(Ns + "Counters",
            new XAttribute("total", Total), new XAttribute("executed", Executed), new XAttribute("passed", Passed),
            new XAttribute("failed", Failed), new XAttribute("error", 0), new XAttribute("timeout", 0), new XAttribute("aborted", 0),
            new XAttribute("inconclusive", 0), new XAttribute("passedButRunAborted", 0), new XAttribute("notRunnable", 0),
            new XAttribute("notExecuted", NotExecuted), new XAttribute("disconnected", 0), new XAttribute("warning", 0),
            new XAttribute("completed", 0), new XAttribute("inProgress", 0), new XAttribute("pending", 0));
    }
}
