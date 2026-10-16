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
    public void Write_ListsWhatTheRollbackPutBack()
    {
        Zaldaryon.Pharos.XUnit.IsolationReport isolation = new(Zaldaryon.Pharos.XUnit.IsolationKind.RolledBack, null, 4, 2, TimeSpan.FromMilliseconds(80))
        {
            DataFilesRestored = 1,
            PlayersRestored = 1,
            ModDataKeysRestored = 3,
            MapRegionsRestored = 2,
        };
        FailureReport report = FailureArtifactWriter.Write(Failure("X.Isolated", "Isolated") with { IsolationReport = isolation });

        using JsonDocument run = JsonDocument.Parse(File.ReadAllText(Path.Combine(report.Directory, "run.json")));
        JsonElement json = run.RootElement.GetProperty("isolationReport");
        Assert.Equal("RolledBack", json.GetProperty("kind").GetString());
        Assert.Equal(4, json.GetProperty("chunksRestored").GetInt32());
        Assert.Equal(1, json.GetProperty("dataFilesRestored").GetInt32());
        Assert.Equal(1, json.GetProperty("playersRestored").GetInt32());
        Assert.Equal(3, json.GetProperty("modDataKeysRestored").GetInt32());
        Assert.Equal(2, json.GetProperty("mapRegionsRestored").GetInt32());
    }

    [Fact]
    public void Write_SavesTheRandomSeed_ForAReplay()
    {
        FailureReport report = FailureArtifactWriter.Write(Failure("X.Seeded", "Seeded") with { RandomSeed = 1234, RandomSeedSource = "[Seeded] on the method" });

        using JsonDocument run = JsonDocument.Parse(File.ReadAllText(Path.Combine(report.Directory, "run.json")));
        Assert.Equal(1234, run.RootElement.GetProperty("randomSeed").GetInt32());
        Assert.Equal("[Seeded] on the method", run.RootElement.GetProperty("randomSeedSource").GetString());

        using JsonDocument unseeded = JsonDocument.Parse(File.ReadAllText(Path.Combine(FailureArtifactWriter.Write(Failure("X.Unseeded", "Unseeded")).Directory, "run.json")));
        Assert.Equal(JsonValueKind.Null, unseeded.RootElement.GetProperty("randomSeed").ValueKind);
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

    [Fact]
    public void RunJson_RecordsServerClock()
    {
        JsonElement Clock(ServerClockInfo? clock, string method)
        {
            FailureReport report = FailureArtifactWriter.Write(Failure("Clock." + method, method) with { ServerClock = clock });
            using JsonDocument run = JsonDocument.Parse(File.ReadAllText(Path.Combine(report.Directory, "run.json")));
            return run.RootElement.GetProperty("serverClock").Clone();
        }

        JsonElement simulated = Clock(new ServerClockInfo("virtual", 50, true, false, 30_000), "Virtual");
        Assert.Equal("virtual", simulated.GetProperty("mode").GetString());
        Assert.Equal(50, simulated.GetProperty("tickMs").GetInt32());
        Assert.True(simulated.GetProperty("followsCalendar").GetBoolean());
        Assert.False(simulated.GetProperty("active").GetBoolean());
        Assert.Equal(30_000, simulated.GetProperty("simulatedMs").GetInt64());

        JsonElement real = Clock(new ServerClockInfo("real", null, null, false, null), "Real");
        Assert.Equal("real", real.GetProperty("mode").GetString());
        Assert.Equal(JsonValueKind.Null, real.GetProperty("tickMs").ValueKind);

        // No server, no clock.
        Assert.Equal(JsonValueKind.Null, Clock(null, "NoServer").ValueKind);
    }

    [Fact]
    public void Write_SavesASoakRun_AsCsvAndAsASummaryInRunInfo()
    {
        SoakReport soak = new(days: 1, speed: 1440, tickMs: 50);
        soak.AddSample(new SoakSample(
            Tick: 100, TotalDays: 0.5, Simulated: TimeSpan.FromHours(1), Wall: TimeSpan.FromSeconds(2),
            TickP50: null, TickP99: null, TickMax: null, ManagedBytes: 1, ManagedBytesAfterGc: null,
            Entities: 9, LoadedChunks: 1, TickListeners: 2, Callbacks: 0, RandomTickPasses: 0, Checkpoint: false));
        soak.AddBreach(new SoakBreach("entities", 5, 9, Tick: 100, TotalDays: 0.5, Gating: true));

        FailureReport report = FailureArtifactWriter.Write(new ScenarioFailure
        {
            DisplayName = "Zaldaryon.Pharos.Tests.Reporting.FailureArtifactWriterTests.Soaked",
            TestClass = typeof(FailureArtifactWriterTests),
            MethodName = "Soaked",
            Exception = new InvalidOperationException("it broke"),
            Soak = soak,
        });

        Assert.Contains("soak.csv", report.Files);
        Assert.StartsWith("tick,total_days,game_seconds,", File.ReadAllText(Path.Combine(report.Directory, "soak.csv")));

        using JsonDocument run = JsonDocument.Parse(File.ReadAllText(Path.Combine(report.Directory, "run.json")));
        JsonElement summary = run.RootElement.GetProperty("soak");
        Assert.False(summary.GetProperty("completed").GetBoolean());
        Assert.Equal("entities", summary.GetProperty("breaches")[0].GetProperty("budget").GetString());
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
