using System.Text.Json;
using System.Xml.Linq;
using Xunit;
using Zaldaryon.Pharos.Cli.ParallelRuns;

namespace Zaldaryon.Pharos.Tests.Cli;

public sealed class MergeCommandTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "pharos-merge-" + Guid.NewGuid().ToString("N")[..8]);

    public MergeCommandTests() => Directory.CreateDirectory(_folder);

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    [Fact]
    public void MergeCommand_MergesJsonManifests()
    {
        string m1 = Path.Combine(_folder, "run-1.json");
        string m2 = Path.Combine(_folder, "run-2.json");
        string merged = Path.Combine(_folder, "merged.json");

        File.WriteAllText(m1, """
            {
              "schemaVersion": 1,
              "runId": "run123",
              "startedUtc": "2026-10-17T10:00:00Z",
              "finishedUtc": "2026-10-17T10:05:00Z",
              "assembly": "Tests.dll",
              "trx": "shard1.trx",
              "workers": [
                { "index": 1, "group": "ClassA", "exitCode": 0, "timedOut": false, "lostTests": [] }
              ],
              "lostTests": [],
              "metadataWarnings": ["warn1"],
              "tests": [
                { "fullyQualifiedName": "ClassA.T1", "displayName": "ClassA.T1", "rawOutcome": "Passed", "finalOutcome": "Passed" }
              ]
            }
            """);

        File.WriteAllText(m2, """
            {
              "schemaVersion": 1,
              "runId": "run123",
              "startedUtc": "2026-10-17T10:01:00Z",
              "finishedUtc": "2026-10-17T10:06:00Z",
              "assembly": "Tests.dll",
              "trx": "shard2.trx",
              "workers": [
                { "index": 1, "group": "ClassB", "exitCode": 0, "timedOut": false, "lostTests": [] }
              ],
              "lostTests": [],
              "metadataWarnings": ["warn2"],
              "tests": [
                { "fullyQualifiedName": "ClassB.T1", "displayName": "ClassB.T1", "rawOutcome": "Passed", "finalOutcome": "Passed" }
              ]
            }
            """);

        StringWriter output = new();
        StringWriter error = new();
        int exit = MergeCommand.Run([m1, m2, "-o", merged], output, error);

        Assert.Equal(MergeCommand.ExitSuccess, exit);
        Assert.True(File.Exists(merged));

        ScenarioRunManifest result = ScenarioRunManifestWriter.Load(merged);
        Assert.Equal(2, result.Tests.Count);
        Assert.Equal(2, result.Workers.Count);
        Assert.Equal(2, result.MetadataWarnings.Count);
        Assert.Equal(DateTimeOffset.Parse("2026-10-17T10:00:00Z"), result.StartedUtc);
        Assert.Equal(DateTimeOffset.Parse("2026-10-17T10:06:00Z"), result.FinishedUtc);
    }

    [Fact]
    public void MergeCommand_MergesTrxFiles()
    {
        string trx1 = Path.Combine(_folder, "t1.trx");
        string trx2 = Path.Combine(_folder, "t2.trx");
        string mergedTrx = Path.Combine(_folder, "merged.trx");

        XNamespace ns = Trx.Ns;
        new XDocument(new XElement(ns + "TestRun",
            new XElement(ns + "TestDefinitions",
                new XElement(ns + "UnitTest", new XAttribute("id", "u1"), new XAttribute("name", "Test1"),
                    new XElement(ns + "TestMethod", new XAttribute("className", "C1"), new XAttribute("name", "Test1")))),
            new XElement(ns + "Results",
                new XElement(ns + "UnitTestResult", new XAttribute("testId", "u1"), new XAttribute("testName", "Test1"), new XAttribute("outcome", "Passed"))),
            new XElement(ns + "ResultSummary", new XAttribute("outcome", "Completed")))).Save(trx1);

        new XDocument(new XElement(ns + "TestRun",
            new XElement(ns + "TestDefinitions",
                new XElement(ns + "UnitTest", new XAttribute("id", "u2"), new XAttribute("name", "Test2"),
                    new XElement(ns + "TestMethod", new XAttribute("className", "C2"), new XAttribute("name", "Test2")))),
            new XElement(ns + "Results",
                new XElement(ns + "UnitTestResult", new XAttribute("testId", "u2"), new XAttribute("testName", "Test2"), new XAttribute("outcome", "Passed"))),
            new XElement(ns + "ResultSummary", new XAttribute("outcome", "Completed")))).Save(trx2);

        StringWriter output = new();
        int exit = MergeCommand.Run([trx1, trx2, "-o", mergedTrx], output, new StringWriter());

        Assert.Equal(MergeCommand.ExitSuccess, exit);
        Assert.True(File.Exists(mergedTrx));

        XDocument loaded = XDocument.Load(mergedTrx);
        Assert.Equal(2, loaded.Root?.Element(ns + "Results")?.Elements(ns + "UnitTestResult").Count());
    }

    [Fact]
    public void MergeCommand_RejectsMissingOutputOrFiles()
    {
        Assert.Equal(MergeCommand.ExitError, MergeCommand.Run(["--output", "out.json"], new StringWriter(), new StringWriter()));
        Assert.Equal(MergeCommand.ExitError, MergeCommand.Run(["file1.json"], new StringWriter(), new StringWriter()));
        Assert.Equal(MergeCommand.ExitError, MergeCommand.Run(["file1.json", "file2.trx", "-o", "out.json"], new StringWriter(), new StringWriter()));
    }
}
