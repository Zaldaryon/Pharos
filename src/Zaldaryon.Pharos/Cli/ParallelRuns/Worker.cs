using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Security;
using System.Xml.Linq;

namespace Zaldaryon.Pharos.Cli.ParallelRuns;

/// <summary>How a worker ended, and the results of its group.</summary>
internal sealed record WorkerOutcome(
    TestGroup Group,
    int Index,
    int? ExitCode,
    bool TimedOut,
    string? Aborted,
    TimeSpan Duration,
    string LogPath,
    XDocument? Trx,
    IReadOnlyList<TrxResult> Results,
    IReadOnlyList<TrxResult> Lost)
{
    public int Passed => Results.Count(r => r.Passed);

    public int Failed => Results.Count(r => r.Failed) + Lost.Count;

    public int Skipped => Results.Count(r => r.Skipped);

    /// <summary>Whether the worker died, hung or never wrote its results.</summary>
    public bool Crashed => Aborted != null;
}

/// <summary>
/// Runs one group of tests in a <c>dotnet vstest</c> process of its own, which writes its results
/// to a TRX file.
/// </summary>
internal sealed class Worker
{
    private const int LogTailLines = 40;

    private readonly string _dotnet;
    private readonly string _assembly;
    private readonly string _workersDirectory;
    private readonly string? _userFilter;
    private readonly IReadOnlyDictionary<string, string> _environment;

    public Worker(string dotnet, string assembly, string workersDirectory, string? userFilter, IReadOnlyDictionary<string, string> environment)
    {
        _dotnet = dotnet;
        _assembly = assembly;
        _workersDirectory = workersDirectory;
        _userFilter = string.IsNullOrWhiteSpace(userFilter) ? null : userFilter;
        _environment = environment;
    }

    /// <summary>
    /// The filter a worker runs for <paramref name="group"/>: exactly its tests, and the user's
    /// filter too, which can narrow them further (to a theory's rows by display name, say).
    /// </summary>
    public string FilterFor(TestGroup group) =>
        _userFilter == null ? Filter.ForGroup(group) : $"({_userFilter})&({Filter.ForGroup(group)})";

    /// <summary>The command a worker runs for <paramref name="group"/>, without a display.</summary>
    /// <param name="testTimeout">How long one test may run before vstest stops the test host; the results so far are kept.</param>
    public IReadOnlyList<string> Command(int index, TimeSpan testTimeout) =>
    [
        _dotnet, "vstest", _assembly,
        $"--Settings:{SettingsPath(index)}",
        $"--logger:trx;LogFileName={TrxPath(index)}",
        $"--ResultsDirectory:{Path.Combine(_workersDirectory, index.ToString(CultureInfo.InvariantCulture))}",
        $"--Blame:CollectHangDump;TestTimeout={(long)testTimeout.TotalSeconds}s;HangDumpType=None",
    ];

    /// <summary>
    /// Runs <paramref name="group"/> as worker <paramref name="index"/>, on display
    /// <paramref name="display"/> when it is set. vstest stops a test that runs longer than
    /// <paramref name="testTimeout"/>; the whole worker is stopped after <paramref name="timeout"/>.
    /// </summary>
    public async Task<WorkerOutcome> RunAsync(TestGroup group, int index, int? display, TimeSpan testTimeout, TimeSpan timeout, IReadOnlyDictionary<string, string> extraEnvironment, bool exactDiscovery, CancellationToken ct)
    {
        string log = Path.Combine(_workersDirectory, $"{index}.log");
        Stopwatch watch = Stopwatch.StartNew();
        try
        {
            return await RunCoreAsync(group, index, display, testTimeout, timeout, extraEnvironment, exactDiscovery, log, watch, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            // A worker that cannot even start fails its group; the run goes on.
            return Aborted(group, index, null, false, $"The worker could not run: {ex.Message}", watch.Elapsed, log, null, [], exactDiscovery);
        }
    }

    private async Task<WorkerOutcome> RunCoreAsync(TestGroup group, int index, int? display, TimeSpan testTimeout, TimeSpan timeout, IReadOnlyDictionary<string, string> extraEnvironment, bool exactDiscovery, string log, Stopwatch watch, CancellationToken ct)
    {
        string trx = TrxPath(index);
        Directory.CreateDirectory(_workersDirectory);
        File.Delete(trx);

        // The filter goes in a settings file: on the command line, a large class's filter would
        // pass Windows's limit.
        await File.WriteAllTextAsync(SettingsPath(index),
            $"<RunSettings><RunConfiguration><TestCaseFilter>{SecurityElement.Escape(FilterFor(group))}</TestCaseFilter></RunConfiguration></RunSettings>",
            CancellationToken.None).ConfigureAwait(false);

        List<string> command = [.. Command(index, testTimeout)];
        if (display is { } number)
        {
            // Each slot starts its search for a free display elsewhere, so workers started at
            // the same moment do not race for the same one.
            command.InsertRange(0, ["xvfb-run", "-a", "-n", number.ToString(CultureInfo.InvariantCulture), "-s", "-screen 0 1280x720x24"]);
        }

        ProcessStartInfo start = new(command[0])
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
        };
        foreach (string arg in command.Skip(1)) start.ArgumentList.Add(arg);
        foreach ((string key, string value) in _environment) start.Environment[key] = value;
        foreach ((string key, string value) in extraEnvironment) start.Environment[key] = value;

        int? exitCode = null;
        bool timedOut = false;
        bool cancelled = false;
        object gate = new();
        bool closed = false;
        StreamWriter writer = new(log, append: false);
        void Write(string? line)
        {
            if (line == null) return;
            lock (gate)
            {
                // Output can still arrive after a killed worker is given up on.
                if (!closed) writer.WriteLine(line);
            }
        }

        try
        {
            Write("$ " + string.Join(' ', command.Select(Quote)));
            Write("filter: " + FilterFor(group));
            using Process process = new() { StartInfo = start };
            process.OutputDataReceived += (_, e) => Write(e.Data);
            process.ErrorDataReceived += (_, e) => Write(e.Data);
            process.Start();
            process.StandardInput.Close();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            using CancellationTokenSource limit = CancellationTokenSource.CreateLinkedTokenSource(ct);
            limit.CancelAfter(timeout);
            try
            {
                await process.WaitForExitAsync(limit.Token).ConfigureAwait(false);
                exitCode = process.ExitCode;
            }
            catch (OperationCanceledException)
            {
                cancelled = ct.IsCancellationRequested;
                timedOut = !cancelled;
                await KillAsync(process).ConfigureAwait(false);
                Write(timedOut ? $"pharos: the worker ran longer than {timeout.TotalMinutes:0} minutes and was stopped." : "pharos: the run was cancelled.");
            }
        }
        finally
        {
            lock (gate)
            {
                closed = true;
                writer.Dispose();
            }
        }

        watch.Stop();
        XDocument? document = null;
        IReadOnlyList<TrxResult> results = [];
        if (File.Exists(trx))
        {
            try
            {
                document = XDocument.Load(trx);
                results = Trx.Results(document);
            }
            catch (Exception ex) when (ex is System.Xml.XmlException or FormatException or IOException)
            {
                document = null;
            }
        }

        string? aborted = timedOut
            ? $"The worker ran longer than {timeout.TotalMinutes:0} minutes and was stopped."
            : cancelled
                ? "The run was cancelled."
                : document == null
                    ? $"The worker exited with code {exitCode} without writing its results."
                    : Trx.Abort(document);

        return Aborted(group, index, exitCode, timedOut, aborted, watch.Elapsed, log, document, results, exactDiscovery);
    }

    // The outcome, with a failed result for the group when the worker stopped early, and for every
    // listed test it never reported.
    private static WorkerOutcome Aborted(TestGroup group, int index, int? exitCode, bool timedOut, string? aborted, TimeSpan duration, string log, XDocument? document, IReadOnlyList<TrxResult> results, bool exactDiscovery)
    {
        List<TrxResult> lost = [];
        string details = $"{aborted ?? "The worker finished without reporting this test."}\nWorker log: {log}\n{Tail(log)}";
        if (aborted != null)
        {
            // The group itself fails, so a crash after its last test, while fixtures are
            // disposed, is not lost.
            lost.Add(new TrxResult($"{group.Name}.(worker aborted)", $"{group.Name} (worker aborted)", "Failed", duration, details, null));
        }

        // When listing gave exactly the tests the filter runs, a listed test with no result never
        // ran; otherwise it may only have been filtered out.
        if (exactDiscovery)
        {
            HashSet<string> reported = results.Select(r => r.FullyQualifiedName).ToHashSet(StringComparer.Ordinal);
            lost.AddRange(group.Tests
                .Where(test => !reported.Contains(test))
                .Select(test => new TrxResult(test, test, "Failed", TimeSpan.Zero, $"Not reported: {details}", null)));
        }

        return new WorkerOutcome(group, index, exitCode, timedOut, aborted, duration, log, document, results, lost);
    }

    private string TrxPath(int index) => Path.Combine(_workersDirectory, $"{index}.trx");

    private string SettingsPath(int index) => Path.Combine(_workersDirectory, $"{index}.runsettings");

    private static async Task KillAsync(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);

            // Waits for the output to end as well, unlike a WaitForExit with a timeout.
            using CancellationTokenSource grace = new(TimeSpan.FromSeconds(10));
            await process.WaitForExitAsync(grace.Token).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException or OperationCanceledException)
        {
            // Already gone, or not gone in time: its output is dropped.
        }
    }

    private static string Tail(string log)
    {
        try
        {
            string[] lines = File.ReadAllLines(log);
            return "Last lines of the worker's output:\n" + string.Join('\n', lines.TakeLast(LogTailLines));
        }
        catch (IOException)
        {
            return "";
        }
    }

    private static string Quote(string arg) =>
        arg.Length > 0 && !arg.Any(c => char.IsWhiteSpace(c) || c is '"' or '|' or '&' or ';' or '(' or ')') ? arg : "\"" + arg.Replace("\"", "\\\"") + "\"";

    /// <summary>The <c>dotnet</c> host: the one running this process, or the one on the path.</summary>
    public static string DotnetHost()
    {
        string executable = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "dotnet.exe" : "dotnet";
        if (Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") is { Length: > 0 } host && File.Exists(host)) return host;

        // The shared runtime lives under <dotnet root>/shared/Microsoft.NETCore.App/<version>/.
        string fromRuntime = Path.GetFullPath(Path.Combine(RuntimeEnvironment.GetRuntimeDirectory(), "..", "..", "..", executable));
        if (File.Exists(fromRuntime)) return fromRuntime;

        if (Environment.ProcessPath is { } process && Path.GetFileName(process).Equals(executable, StringComparison.OrdinalIgnoreCase)) return process;

        return executable;
    }
}
