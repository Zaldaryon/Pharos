using System.Text.Json;
using Vintagestory.API.Common;
using Xunit;
using Zaldaryon.Pharos.Reporting;
using Zaldaryon.Pharos.Server;
using Zaldaryon.Pharos.Tests.XUnit;

namespace Zaldaryon.Pharos.Tests.Reporting;

/// <summary>
/// <see cref="FailureArtifactWriter"/> without a game: where it writes, what run.json holds,
/// and how it copes with steps that fail or hang.
/// </summary>
[Collection(FailureArtifactCollection.Name)]
public sealed class FailureArtifactWriterTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "pharos-artifacts-test-" + Guid.NewGuid().ToString("N")[..8]);

    public FailureArtifactWriterTests()
    {
        FailureArtifactWriter.RootOverride = _root;
    }

    public void Dispose()
    {
        FailureArtifactWriter.RootOverride = null;
        FailureArtifactWriter.KeepSandboxOverride = null;
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public void Write_SavesRunInfo_UnderClassAndMethod()
    {
        FailureReport report = FailureArtifactWriter.Write(Failure("Zaldaryon.Pharos.Tests.Reporting.FailureArtifactWriterTests.Broken", "Broken"));

        Assert.Equal(Path.Combine(_root, nameof(FailureArtifactWriterTests), "Broken"), report.Directory);
        Assert.Equal(["run.json"], report.Files);
        Assert.Empty(report.Problems);

        using JsonDocument run = JsonDocument.Parse(File.ReadAllText(Path.Combine(report.Directory, "run.json")));
        JsonElement root = run.RootElement;
        Assert.Equal("Broken", root.GetProperty("method").GetString());
        Assert.Equal("failed", root.GetProperty("outcome").GetString());
        Assert.Equal(typeof(InvalidOperationException).FullName, root.GetProperty("exception").GetString());
        Assert.Equal("it broke", root.GetProperty("message").GetString());
        Assert.Equal("4242", root.GetProperty("seed").GetString());
        Assert.Equal(12, root.GetProperty("frames").GetInt64());
        Assert.False(string.IsNullOrEmpty(root.GetProperty("gameVersion").GetString()));
        Assert.False(string.IsNullOrEmpty(root.GetProperty("pharosVersion").GetString()));
        Assert.False(string.IsNullOrEmpty(root.GetProperty("runId").GetString()));
    }

    [Fact]
    public void Write_NeverReusesAFolder()
    {
        string first = FailureArtifactWriter.Write(Failure("X.Again", "Again")).Directory;
        string second = FailureArtifactWriter.Write(Failure("X.Again", "Again")).Directory;

        Assert.NotEqual(first, second);
        Assert.EndsWith("Again-2", second);
    }

    [Fact]
    public void TheoryRows_GetFoldersOfTheirOwn_WithSafeNames()
    {
        string one = FailureArtifactWriter.Write(Failure("X.Rows(value: \"a:b\", n: 1)", "Rows")).Directory;
        string two = FailureArtifactWriter.Write(Failure("X.Rows(value: \"a:b\", n: 2)", "Rows")).Directory;

        Assert.NotEqual(one, two);
        foreach (string folder in new[] { Path.GetFileName(one), Path.GetFileName(two) })
        {
            Assert.StartsWith("Rows-", folder);
            Assert.DoesNotContain(folder, c => "\":<>|*?\r\n ".Contains(c));
        }
    }

    [Fact]
    public void Sanitize_KeepsNamesShortAndSafe()
    {
        Assert.Equal("Outer_Inner_1", FailureArtifactWriter.Sanitize("Outer+Inner`1"));
        Assert.Equal(60, FailureArtifactWriter.Sanitize(new string('a', 200)).Length);
        Assert.Equal("test", FailureArtifactWriter.Sanitize("::"));
    }

    [Fact]
    public void AStepThatThrows_IsListedAndTheRestIsSaved()
    {
        FailureReport report = FailureArtifactWriter.Write(Failure("X.Throws", "Throws") with
        {
            AddFiles = _ => throw new IOException("disk full"),
        });

        Assert.Contains("run.json", report.Files);
        Assert.Contains(report.Problems, p => p.Contains("disk full"));
        Assert.Contains("Not saved:", report.Summary());
    }

    [Fact]
    public void AStepThatHangs_IsGivenUp()
    {
        TimeSpan limit = FailureArtifactWriter.StepTimeout;
        FailureArtifactWriter.StepTimeout = TimeSpan.FromMilliseconds(200);
        using ManualResetEventSlim release = new();

        try
        {
            FailureReport report = FailureArtifactWriter.Write(Failure("X.Hangs", "Hangs") with { AddFiles = _ => release.Wait() });

            Assert.Contains(report.Problems, p => p.Contains("gave up"));
            Assert.Contains("run.json", report.Files);
        }
        finally
        {
            FailureArtifactWriter.StepTimeout = limit;
            release.Set();
        }
    }

    [Fact]
    public void ScenarioFiles_GoIntoTheFolder()
    {
        FailureReport report = FailureArtifactWriter.Write(Failure("X.Own", "Own") with
        {
            AddFiles = dir => File.WriteAllText(Path.Combine(dir, "mine.txt"), "hello"),
        });

        Assert.Equal("hello", File.ReadAllText(Path.Combine(report.Directory, "mine.txt")));
    }

    [Fact]
    public void GameLogs_AreCopiedFromTheServerDataPath_EvenWhileOpenForWriting()
    {
        string dataPath = Path.Combine(_root, "server-data");
        Directory.CreateDirectory(Path.Combine(dataPath, "Logs"));
        string log = Path.Combine(dataPath, "Logs", "server-main.log");

        using (FileStream open = new(log, FileMode.Create, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete))
        using (StreamWriter writer = new(open))
        {
            writer.Write("booted");
            writer.Flush();

            FailureReport report = FailureArtifactWriter.Write(Failure("X.Logs", "Logs") with
            {
                ServerDataPath = dataPath,
                Artifacts = FailureArtifacts.Logs,
            });

            Assert.Equal(["server-logs"], report.Files);
            Assert.Equal("booted", File.ReadAllText(Path.Combine(report.Directory, "server-logs", "server-main.log")));
        }
    }

    [Fact]
    public void KeepSandbox_RetainsTheSandbox()
    {
        FailureArtifactWriter.KeepSandboxOverride = true;
        ServerSandbox sandbox = new();

        try
        {
            FailureReport report = FailureArtifactWriter.Write(Failure("X.Keep", "Keep") with { Sandbox = sandbox });
            sandbox.Dispose();

            Assert.Equal([sandbox.RootPath], report.Kept);
            Assert.True(Directory.Exists(sandbox.RootPath), "The kept sandbox was deleted");
            Assert.Contains("Kept for inspection: " + sandbox.RootPath, report.Summary());
        }
        finally
        {
            if (Directory.Exists(sandbox.RootPath)) Directory.Delete(sandbox.RootPath, recursive: true);
        }
    }

    [Fact]
    public void Summary_ListsTheLoggedErrors_UpToALimit()
    {
        LogEntry[] errors = [.. Enumerable.Range(0, 25).Select(i => new LogEntry(EnumAppSide.Server, EnumLogType.Error, "boom " + i))];
        FailureReport report = new("/dir", ["run.json"], errors, [], []);

        string summary = report.Summary();

        Assert.Contains("Pharos saved run.json to /dir", summary);
        Assert.Contains("logged 25 error(s)", summary);
        Assert.Contains("boom 19", summary);
        Assert.DoesNotContain("boom 20", summary);
        Assert.Contains("and 5 more", summary);
    }

    private static ScenarioFailure Failure(string displayName, string method) => new()
    {
        DisplayName = displayName,
        TestClass = typeof(FailureArtifactWriterTests),
        MethodName = method,
        Exception = new InvalidOperationException("it broke"),
        World = new ServerWorldOptions { Seed = "4242" },
        Frames = 12,
    };
}
