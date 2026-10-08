using System.Globalization;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Vintagestory.API.Config;
using Zaldaryon.Pharos.Bootstrap;
using Zaldaryon.Pharos.Core;
using Zaldaryon.Pharos.Server;

namespace Zaldaryon.Pharos.Reporting;

/// <summary>What failed, and what was running when it did.</summary>
internal sealed record ScenarioFailure
{
    /// <summary>The test's display name, theory arguments included.</summary>
    public required string DisplayName { get; init; }

    /// <summary>The test class.</summary>
    public required Type TestClass { get; init; }

    /// <summary>The test method's name.</summary>
    public required string MethodName { get; init; }

    /// <summary>Whether the test ran out of time rather than failing.</summary>
    public bool TimedOut { get; init; }

    /// <summary>Why it failed.</summary>
    public required Exception Exception { get; init; }

    /// <summary>The client, when the scenario has one.</summary>
    public HeadlessClient? Client { get; init; }

    /// <summary>The server, when the scenario has one.</summary>
    public EmbeddedServerHost? Server { get; init; }

    /// <summary>The frames the client stepped during the test, when known.</summary>
    public long? Frames { get; init; }

    /// <summary>The ticks the server ran during the test, when known.</summary>
    public long? ServerTicks { get; init; }

    /// <summary>
    /// Whether the client may be reached for a screenshot: false when a timed-out body may still
    /// hold its GL context.
    /// </summary>
    public bool ClientReachable { get; init; } = true;

    /// <summary>Where the server keeps its files, when the server did not boot far enough to say.</summary>
    public string? ServerDataPath { get; init; }

    /// <summary>The server's sandbox, kept with <c>PHAROS_KEEP_SANDBOX=1</c>.</summary>
    public ServerSandbox? Sandbox { get; init; }

    /// <summary>The world the server booted.</summary>
    public ServerWorldOptions? World { get; init; }

    /// <summary>How the scenario isolates its tests, for run.json.</summary>
    public string? Isolation { get; init; }

    /// <summary>What to save.</summary>
    public FailureArtifacts Artifacts { get; init; } = FailureArtifacts.Default;

    /// <summary>Lets the scenario add its own files to the folder.</summary>
    public Action<string>? AddFiles { get; init; }
}

/// <summary>What <see cref="FailureArtifactWriter.Write"/> saved.</summary>
internal sealed record FailureReport(
    string Directory,
    IReadOnlyList<string> Files,
    IReadOnlyList<LogEntry> Errors,
    IReadOnlyList<string> Kept,
    IReadOnlyList<string> Problems)
{
    /// <summary>The most logged errors listed in the failure message.</summary>
    public const int Listed = 20;

    /// <summary>The text added to the failure message.</summary>
    public string Summary()
    {
        StringBuilder text = new();
        text.Append(Files.Count > 0
            ? $"Pharos saved {string.Join(", ", Files)} to {Directory}"
            : "Pharos saved no failure artifacts");

        foreach (string kept in Kept)
        {
            text.AppendLine().Append("Kept for inspection: ").Append(kept);
        }

        foreach (string problem in Problems)
        {
            text.AppendLine().Append("Not saved: ").Append(problem);
        }

        if (Errors.Count > 0)
        {
            text.AppendLine().Append(CultureInfo.InvariantCulture,
                $"The client and server logged {Errors.Count} error(s) during the test (since boot for a freshly booted host):");
            foreach (LogEntry error in Errors.Take(Listed))
            {
                text.AppendLine().Append("  ").Append(error);
            }

            if (Errors.Count > Listed)
            {
                text.AppendLine().Append(CultureInfo.InvariantCulture, $"  ... and {Errors.Count - Listed} more in client.log and server.log");
            }
        }

        return text.ToString();
    }
}

/// <summary>
/// Saves what is needed to understand a failed scenario: a screenshot, the logs, the traffic and
/// the run's details. See <c>docs/failure-artifacts.md</c>.
/// </summary>
internal static class FailureArtifactWriter
{
    /// <summary>Where artifacts go; defaults to <c>TestResults/pharos</c> next to the test assembly.</summary>
    public const string DirectoryVariable = "PHAROS_ARTIFACTS";

    /// <summary>Set to 1 to keep a failed scenario's sandbox and client data folder.</summary>
    public const string KeepSandboxVariable = "PHAROS_KEEP_SANDBOX";

    /// <summary>Names the run in run.json; defaults to the GitHub Actions run, or one id per process.</summary>
    public const string RunIdVariable = "PHAROS_RUN_ID";

    /// <summary>How long one step may take before it is given up, so a wedged game cannot hang the report.</summary>
    internal static TimeSpan StepTimeout { get; set; } = TimeSpan.FromSeconds(15);

    private const long MaxCopiedLogBytes = 20L * 1024 * 1024;
    private const int MaxNameLength = 60;
    private static readonly string s_processRunId = Guid.NewGuid().ToString("N")[..12];
    private static readonly object s_directoryLock = new();

    private static readonly AsyncLocal<string?> s_rootOverride = new();

    /// <summary>Saves this flow's artifacts under another folder, for tests of Pharos itself.</summary>
    internal static string? RootOverride
    {
        get => s_rootOverride.Value;
        set => s_rootOverride.Value = value;
    }

    /// <summary>The root folder artifacts are saved under.</summary>
    public static string Root =>
        RootOverride is { } overridden ? overridden
        : Environment.GetEnvironmentVariable(DirectoryVariable) is { Length: > 0 } root
            ? Path.GetFullPath(root)
            : Path.Combine(AppContext.BaseDirectory, "TestResults", "pharos");

    private static readonly AsyncLocal<bool?> s_keepSandboxOverride = new();

    /// <summary>Overrides <c>PHAROS_KEEP_SANDBOX</c> on this flow, for tests of Pharos itself.</summary>
    internal static bool? KeepSandboxOverride
    {
        get => s_keepSandboxOverride.Value;
        set => s_keepSandboxOverride.Value = value;
    }

    /// <summary>Whether <c>PHAROS_KEEP_SANDBOX</c> asks to keep failed sandboxes.</summary>
    public static bool KeepSandbox => KeepSandboxOverride ?? Environment.GetEnvironmentVariable(KeepSandboxVariable) is "1" or "true" or "TRUE" or "True";

    /// <summary>The run id written to run.json.</summary>
    public static string RunId =>
        Environment.GetEnvironmentVariable(RunIdVariable) is { Length: > 0 } id ? id
        : Environment.GetEnvironmentVariable("GITHUB_RUN_ID") is { Length: > 0 } gh
            ? gh + "-" + (Environment.GetEnvironmentVariable("GITHUB_RUN_ATTEMPT") ?? "1")
        : s_processRunId;

    /// <summary>
    /// Saves the artifacts of <paramref name="failure"/>. Never throws: what cannot be saved is
    /// listed in <see cref="FailureReport.Problems"/>.
    /// </summary>
    public static FailureReport Write(ScenarioFailure failure)
    {
        List<string> files = [];
        List<string> kept = [];
        List<string> problems = [];
        List<LogEntry> errors = [];

        foreach (LogCapture? logs in new[] { failure.Client?.Logs, failure.Server?.Logs })
        {
            if (logs != null) errors.AddRange(logs.Errors);
        }

        string directory;
        try
        {
            directory = CreateDirectory(failure);
        }
        catch (Exception ex)
        {
            return new FailureReport(Root, files, errors, kept, [$"the artifact folder ({ex.Message})"]);
        }

        FailureArtifacts artifacts = failure.Artifacts;

        if (artifacts.HasFlag(FailureArtifacts.Screenshot) && failure.ClientReachable && failure.Client is { IsDisposed: false } client)
        {
            SaveScreenshot(client, Path.Combine(directory, "screenshot.png"), files, problems);
        }

        if (artifacts.HasFlag(FailureArtifacts.Logs))
        {
            if (failure.Client != null)
            {
                Step("client.log", files, problems, () => WriteLog(failure.Client.Logs, Path.Combine(directory, "client.log")));
                CopyGameLogs(failure.Client.DataPath, Path.Combine(directory, "client-logs"), "client-logs", files, problems);
            }

            if (failure.Server != null)
            {
                Step("server.log", files, problems, () => WriteLog(failure.Server.Logs, Path.Combine(directory, "server.log")));
            }

            CopyGameLogs(failure.Server?.DataPath ?? failure.ServerDataPath, Path.Combine(directory, "server-logs"), "server-logs", files, problems);
        }

        if (artifacts.HasFlag(FailureArtifacts.Packets) && failure.Client is { } recorded && recorded.PacketRecorder.PacketCount > 0)
        {
            Step("packets.json", files, problems, () => recorded.PacketRecorder.SaveToJson(Path.Combine(directory, "packets.json")));
        }

        if (KeepSandbox)
        {
            if (failure.Sandbox != null)
            {
                failure.Sandbox.Retain();
                kept.Add(failure.Sandbox.RootPath);
            }

            if (failure.Client?.RetainDataPath() is { } clientData)
            {
                kept.Add(clientData);
            }
        }

        if (failure.AddFiles != null)
        {
            Step("the scenario's own files", [], problems, () => failure.AddFiles(directory));
        }

        if (artifacts.HasFlag(FailureArtifacts.RunInfo))
        {
            Step("run.json", files, problems, () => WriteRunInfo(failure, Path.Combine(directory, "run.json"), files, kept));
        }

        return new FailureReport(directory, files, errors, kept, problems);
    }

    /// <summary>The folder for one failed test: <c>Root/Class/Method</c>, never one used before.</summary>
    internal static string CreateDirectory(ScenarioFailure failure)
    {
        string classFolder = Sanitize(failure.TestClass.Name);
        string testFolder = Sanitize(failure.MethodName);

        // A theory row's display name carries its arguments; the hash tells the rows apart.
        if (!failure.DisplayName.EndsWith("." + failure.MethodName, StringComparison.Ordinal) && failure.DisplayName != failure.MethodName)
        {
            string hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(failure.DisplayName)))[..8].ToLowerInvariant();
            testFolder = testFolder[..Math.Min(testFolder.Length, MaxNameLength - 9)] + "-" + hash;
        }

        lock (s_directoryLock)
        {
            string path = Path.Combine(Root, classFolder, testFolder);
            for (int n = 2; Directory.Exists(path); n++)
            {
                path = Path.Combine(Root, classFolder, testFolder + "-" + n.ToString(CultureInfo.InvariantCulture));
            }

            Directory.CreateDirectory(path);
            return path;
        }
    }

    internal static string Sanitize(string name)
    {
        StringBuilder safe = new(name.Length);
        foreach (char c in name)
        {
            safe.Append(char.IsLetterOrDigit(c) || c is '-' or '_' or '.' ? c : '_');
        }

        string result = safe.ToString().Trim('.', '_');
        if (result.Length == 0) result = "test";
        return result.Length > MaxNameLength ? result[..MaxNameLength] : result;
    }

    /// <summary>
    /// An engine-mode client renders on its own thread, where the capture is queued, with a time
    /// limit. A fixture-mode client's GL context is current on the thread that renders it, and
    /// making it current on another one aborts the process (X BadAccess), so it is read only on
    /// that thread.
    /// </summary>
    private static void SaveScreenshot(HeadlessClient client, string path, List<string> files, List<string> problems)
    {
        const string name = "screenshot.png";
        if (client.BootMode == ClientBootMode.Engine)
        {
            Step(name, files, problems, () => client.CaptureFrame().SaveToPng(path));
            return;
        }

        try
        {
            if (!client.Window.NativeWindow.Context.IsCurrent)
            {
                problems.Add($"{name} (the fixture-mode client renders on another thread)");
                return;
            }

            client.CaptureFrame().SaveToPng(path);
            files.Add(name);
        }
        catch (Exception ex)
        {
            problems.Add($"{name} ({ex.GetType().Name}: {ex.Message})");
        }
    }

    /// <summary>Runs one step with a time limit; a step that fails or hangs is listed, not thrown.</summary>
    private static void Step(string name, List<string> files, List<string> problems, Action step)
    {
        // A thread of its own: a wedged game must not take a thread pool thread with it.
        Exception? error = null;
        Thread thread = new(() =>
        {
            try
            {
                step();
            }
            catch (Exception ex)
            {
                error = ex;
            }
        })
        {
            IsBackground = true,
            Name = "Pharos failure artifacts",
        };
        if (ExecutionContext.IsFlowSuppressed())
        {
            thread.Start();
        }
        else
        {
            using (ExecutionContext.SuppressFlow())
            {
                thread.Start();
            }
        }

        if (!thread.Join(StepTimeout))
        {
            problems.Add($"{name} (gave up after {StepTimeout.TotalSeconds:0} s)");
        }
        else if (error != null)
        {
            problems.Add($"{name} ({error.GetType().Name}: {error.Message})");
        }
        else
        {
            files.Add(name);
        }
    }

    private static void WriteLog(LogCapture logs, string path)
    {
        using StreamWriter writer = new(path, append: false, new UTF8Encoding(false));
        foreach (LogEntry entry in logs.Entries)
        {
            writer.WriteLine(entry.ToString());
        }
    }

    /// <summary>Copies the game's own log files (<c>Logs/*.log</c> under a data path), each up to 20 MB.</summary>
    private static void CopyGameLogs(string? dataPath, string target, string name, List<string> files, List<string> problems)
    {
        if (string.IsNullOrEmpty(dataPath)) return;

        string source = Path.Combine(dataPath, "Logs");
        if (!Directory.Exists(source)) return;

        string[] logs;
        try
        {
            logs = Directory.GetFiles(source, "*.log");
        }
        catch (Exception ex)
        {
            problems.Add($"{name} ({ex.Message})");
            return;
        }

        if (logs.Length == 0) return;

        Step(name, files, problems, () =>
        {
            Directory.CreateDirectory(target);
            foreach (string log in logs)
            {
                CopyTail(log, Path.Combine(target, Path.GetFileName(log)));
            }
        });
    }

    /// <summary>Copies a file the game may still be writing, keeping its last 20 MB.</summary>
    private static void CopyTail(string source, string destination)
    {
        using FileStream input = new(source, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        if (input.Length > MaxCopiedLogBytes) input.Seek(-MaxCopiedLogBytes, SeekOrigin.End);
        using FileStream output = new(destination, FileMode.Create, FileAccess.Write);
        input.CopyTo(output);
    }

    private static void WriteRunInfo(ScenarioFailure failure, string path, IReadOnlyList<string> files, IReadOnlyList<string> kept)
    {
        Dictionary<string, object?> info = new()
        {
            ["test"] = failure.DisplayName,
            ["class"] = failure.TestClass.FullName,
            ["method"] = failure.MethodName,
            ["outcome"] = failure.TimedOut ? "timedOut" : "failed",
            ["exception"] = failure.Exception.GetType().FullName,
            ["message"] = failure.Exception.Message,
            ["gameVersion"] = GameVersionLoaded,
            ["pharosVersion"] = PharosVersion,
            ["seed"] = failure.World?.Seed,
            ["worldType"] = failure.World?.WorldType,
            ["playStyle"] = failure.World?.PlayStyle,
            ["isolation"] = failure.Isolation,
            ["clientBootMode"] = failure.Client?.BootMode.ToString(),
            ["frames"] = failure.Frames,
            ["serverTicks"] = failure.ServerTicks,
            ["runId"] = RunId,
            ["timeUtc"] = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture),
            ["os"] = RuntimeInformation.OSDescription,
            ["runtime"] = RuntimeInformation.FrameworkDescription,
            ["processId"] = Environment.ProcessId,
            ["files"] = files.ToArray(),
            ["kept"] = kept.ToArray(),
        };

        File.WriteAllText(path, JsonSerializer.Serialize(info, new JsonSerializerOptions { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }), new UTF8Encoding(false));
    }

    /// <summary>The version of the game that is loaded, which can differ from the one Pharos was built against.</summary>
    internal static string GameVersionLoaded { get; } =
        typeof(GameVersion).GetField(nameof(GameVersion.ShortGameVersion), BindingFlags.Public | BindingFlags.Static)?.GetRawConstantValue() as string
        ?? "unknown";

    internal static string PharosVersion { get; } =
        typeof(FailureArtifactWriter).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? typeof(FailureArtifactWriter).Assembly.GetName().Version?.ToString()
        ?? "unknown";
}
