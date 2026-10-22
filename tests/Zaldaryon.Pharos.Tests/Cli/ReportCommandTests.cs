using System.Text.Json;
using System.Xml.Linq;
using Xunit;
using Zaldaryon.Pharos.Cli;
using Zaldaryon.Pharos.Cli.ParallelRuns;

namespace Zaldaryon.Pharos.Tests.Cli;

/// <summary>The <c>pharos report</c> command: the HTML page, the Markdown summary and the timings.</summary>
public sealed class ReportCommandTests : IDisposable
{
    private static readonly byte[] s_png = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==");

    private readonly string _root = Path.Combine(Path.GetTempPath(), "pharos-report-" + Guid.NewGuid().ToString("N"));

    public ReportCommandTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A failed test leaves its report behind for reading.
        }
    }

    [Fact]
    public void Generate_WritesThePageTheSummaryAndTheTimings()
    {
        string trx = WriteTrx();
        string artifacts = Directory.CreateDirectory(Path.Combine(_root, "artifacts", "worker-1", "SampleTests", "Fails")).FullName;
        File.WriteAllBytes(Path.Combine(artifacts, "screenshot.png"), s_png);
        File.WriteAllText(Path.Combine(artifacts, "server.log"), "line one\nline two\n");
        File.WriteAllText(Path.Combine(artifacts, "run.json"), "{}");
        string manifest = WriteManifest(trx, artifacts);
        string html = Path.Combine(_root, "report.html");
        string markdown = Path.Combine(_root, "report.md");
        string timings = Path.Combine(_root, "timings.json");

        ReportPaths paths = ReportCommand.Generate(trx, manifest, Path.Combine(_root, "artifacts"), html, markdown, timings);

        Assert.Equal(Path.GetFullPath(html), paths.Html);
        string page = File.ReadAllText(html);
        Assert.Contains("1 passed", page, StringComparison.Ordinal);
        Assert.Contains("1 failed", page, StringComparison.Ordinal);
        Assert.Contains("Boom", page, StringComparison.Ordinal);
        Assert.Contains("data:image/png;base64,", page, StringComparison.Ordinal);
        Assert.Contains("Last lines of server.log", page, StringComparison.Ordinal);
        Assert.Contains("line two", page, StringComparison.Ordinal);

        string summary = File.ReadAllText(markdown);
        Assert.Contains("2 tests: 1 passed, 1 failed, 0 skipped", summary, StringComparison.Ordinal);
        Assert.Contains("| `SampleTests.Fails` | Boom", summary, StringComparison.Ordinal);
        Assert.Contains("artifacts/worker-1/SampleTests/Fails", summary, StringComparison.Ordinal);

        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(timings));
        JsonElement root = document.RootElement;
        Assert.Equal(1, root.GetProperty("passed").GetInt32());
        Assert.Equal(1, root.GetProperty("failed").GetInt32());
        JsonElement failed = root.GetProperty("tests").EnumerateArray().Single(test => test.GetProperty("outcome").GetString() == "Failed");
        Assert.Equal("body", failed.GetProperty("attempts")[0].GetProperty("phase").GetString());
    }

    [Fact]
    public void Run_WritesTheReportAndPrintsWhereItWent()
    {
        string trx = WriteTrx();
        StringWriter stdout = new();
        StringWriter stderr = new();

        int exit = ReportCommand.Run([trx, "--artifacts", Path.Combine(_root, "missing-artifacts")], stdout, stderr);

        Assert.Equal(0, exit);
        Assert.Contains("HTML:", stdout.ToString(), StringComparison.Ordinal);
        Assert.True(File.Exists(Path.Combine(_root, "report.html")));
        Assert.True(File.Exists(Path.Combine(_root, "report.md")));
        Assert.True(File.Exists(Path.Combine(_root, "timings.json")));
        Assert.Equal("", stderr.ToString());
    }

    [Fact]
    public void Run_Help_PrintsTheUsage()
    {
        StringWriter stdout = new();
        StringWriter stderr = new();

        int exit = ReportCommand.Run(["--help"], stdout, stderr);

        Assert.Equal(0, exit);
        Assert.Contains("pharos report", stdout.ToString(), StringComparison.Ordinal);
        Assert.Equal("", stderr.ToString());
    }

    [Fact]
    public void Run_MissingFile_ReturnsAnError()
    {
        StringWriter stdout = new();
        StringWriter stderr = new();

        int exit = ReportCommand.Run([Path.Combine(_root, "nope.trx")], stdout, stderr);

        Assert.Equal(2, exit);
        Assert.Contains("does not exist", stderr.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void RunCommand_ParsesTheReportDirectory()
    {
        RunOptions options = RunOptions.Parse(["Tests.dll", "--report", "out/report"]);

        Assert.Equal("out/report", options.ReportDirectory);
        Assert.Contains("--report <dir>", RunCommand.HelpText, StringComparison.Ordinal);
    }

    [Fact]
    public void PhCliRunner_DispatchesToReport()
    {
        StringWriter stdout = new();
        StringWriter stderr = new();

        int exit = PhCliRunner.Run(["report", "--help"], stdout, stderr);

        Assert.Equal(0, exit);
        Assert.Contains("write an HTML page", stdout.ToString(), StringComparison.Ordinal);
    }

    private string WriteTrx()
    {
        XNamespace ns = Trx.Ns;
        XDocument trx = new(
            new XElement(ns + "TestRun",
                new XElement(ns + "TestDefinitions",
                    new XElement(ns + "UnitTest", new XAttribute("id", "1"),
                        new XElement(ns + "TestMethod", new XAttribute("className", "SampleTests"), new XAttribute("name", "Passes"))),
                    new XElement(ns + "UnitTest", new XAttribute("id", "2"),
                        new XElement(ns + "TestMethod", new XAttribute("className", "SampleTests"), new XAttribute("name", "Fails")))),
                new XElement(ns + "Results",
                    new XElement(ns + "UnitTestResult", new XAttribute("testId", "1"), new XAttribute("testName", "SampleTests.Passes"),
                        new XAttribute("outcome", "Passed"), new XAttribute("duration", "00:00:01.5000000")),
                    new XElement(ns + "UnitTestResult", new XAttribute("testId", "2"), new XAttribute("testName", "SampleTests.Fails"),
                        new XAttribute("outcome", "Failed"), new XAttribute("duration", "00:00:02.5000000"),
                        new XElement(ns + "Output", new XElement(ns + "ErrorInfo",
                            new XElement(ns + "Message", "Boom"),
                            new XElement(ns + "StackTrace", "at SampleTests.Fails()")))))));
        string path = Path.Combine(_root, "pharos.trx");
        trx.Save(path);
        return path;
    }

    private string WriteManifest(string trx, string artifacts)
    {
        var manifest = new
        {
            schemaVersion = 1,
            runId = "test-run",
            startedUtc = "2026-01-02T03:04:05Z",
            finishedUtc = "2026-01-02T03:04:15Z",
            assembly = Path.Combine(_root, "Tests.dll"),
            trx,
            workers = new[] { new { index = 1, group = "SampleTests", exitCode = 0, timedOut = false, aborted = (string?)null, lostTests = Array.Empty<string>() } },
            lostTests = Array.Empty<string>(),
            metadataWarnings = Array.Empty<string>(),
            tests = new object[]
            {
                new
                {
                    fullyQualifiedName = "SampleTests.Passes",
                    displayName = "SampleTests.Passes",
                    rawOutcome = "Passed",
                    finalOutcome = "Passed",
                    flaky = (bool?)null,
                    quarantined = false,
                    failurePhase = (string?)null,
                    attempts = Array.Empty<object>(),
                },
                new
                {
                    fullyQualifiedName = "SampleTests.Fails",
                    displayName = "SampleTests.Fails",
                    rawOutcome = "Failed",
                    finalOutcome = "Failed",
                    flaky = (bool?)null,
                    quarantined = false,
                    failurePhase = "body",
                    attempts = new[]
                    {
                        new
                        {
                            number = 1,
                            phase = "body",
                            outcome = "Failed",
                            durationSeconds = 2.5,
                            failure = "Boom",
                            artifactDirectories = new[] { artifacts },
                        },
                    },
                },
            },
        };
        string path = Path.Combine(_root, "pharos-run.json");
        File.WriteAllText(path, JsonSerializer.Serialize(manifest, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true,
        }));
        return path;
    }
}
