using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Xml.Linq;

namespace Zaldaryon.Pharos.Cli.ParallelRuns;

/// <summary>
/// <c>pharos run</c>: lists a test assembly's tests without running them, splits them by class
/// or collection, and runs the groups in worker processes, several at once. See
/// <c>docs/parallel-runs.md</c>.
/// </summary>
/// <remarks>
/// The game keeps its state in statics, so a process holds one engine-mode client at a time and a
/// test run is serial. Each worker is a <c>dotnet vstest</c> process of its own, with its own client
/// data folder, server sandbox, ports and, under Linux without a display, virtual display; xUnit
/// runs its tests exactly as <c>dotnet test</c> would.
/// </remarks>
public static partial class RunCommand
{
    /// <summary>Every test passed.</summary>
    public const int ExitPassed = 0;

    /// <summary>A test failed, or a worker crashed or hung.</summary>
    public const int ExitFailed = 1;

    /// <summary>The arguments were wrong, or the tests could not be built or listed.</summary>
    public const int ExitError = 2;

    /// <summary>The help for <c>pharos run</c>.</summary>
    public const string HelpText = """
        pharos run - run a test assembly's classes in parallel worker processes

        Usage: pharos run <test.dll | project folder | .csproj> [options]

        Options:
          --filter <expr>          A dotnet test filter, e.g. "Category=Live"
          -p, --parallel <n>       How many workers run at once (default: 1)
          --group class|collection How tests are split between workers (default: class)
          --list                   List the groups and their tests; run nothing
          --trx <path>             Where the merged TRX goes (default: <results-dir>/pharos.trx)
          --results-dir <dir>      Worker logs, TRX files and failure artifacts (default: pharos-run)
          --test-timeout <min>     Stop a test host whose test runs longer, keeping the results
                                   so far (default: 30, scaled by PHAROS_TIMEOUT_SCALE)
          --worker-timeout <min>   Stop a worker after this many minutes (default: 60,
                                   scaled by PHAROS_TIMEOUT_SCALE)
          --game <path>            The Vintage Story install workers use (default: VINTAGE_STORY)
          --json                   Write JSON lines to standard output instead of text
          -c, --configuration <c>  Build configuration of a project (default: Release)
          --no-build               Use a project's last build
          --xvfb auto|always|never Give each worker its own virtual display (default: auto,
                                   on Linux without a display when xvfb-run is installed)
          -v, --verbose            Print each worker's command

        Tests in Category=Live get a worker per class (or collection); all other tests share one.
        Each worker is a `dotnet vstest` process. A worker that crashes or hangs fails its group
        and the tests it did not report; it never hangs the run.

        Exit codes: 0 all passed, 1 a test failed, 2 an error before the tests ran.

        Examples:
          pharos run tests/MyMod.Tests --filter "Category=Live" --parallel 4 --trx results.trx
          pharos run tests/MyMod.Tests --list
        """;

    /// <summary>Runs <c>pharos run</c> with the arguments that follow <c>run</c>.</summary>
    public static int Run(IReadOnlyList<string> args, TextWriter stdout, TextWriter stderr)
    {
        RunOptions? options;
        try
        {
            options = RunOptions.Parse(args);
        }
        catch (ArgumentException ex)
        {
            stderr.WriteLine($"Error: {ex.Message}");
            stderr.WriteLine();
            stderr.WriteLine(HelpText);
            return ExitError;
        }

        if (options == null)
        {
            stdout.WriteLine(HelpText);
            return ExitPassed;
        }

        using CancellationTokenSource cancel = new();
        ConsoleCancelEventHandler onCancel = (_, e) =>
        {
            e.Cancel = true;
            cancel.Cancel();
        };
        Console.CancelKeyPress += onCancel;

        // A CI runner stops a job with SIGTERM: the workers are stopped with it.
        using PosixSignalRegistration? terminate = OperatingSystem.IsWindows() ? null : PosixSignalRegistration.Create(PosixSignal.SIGTERM, context =>
        {
            context.Cancel = true;
            cancel.Cancel();
        });
        try
        {
            return RunAsync(options, stdout, stderr, cancel.Token).GetAwaiter().GetResult();
        }
        finally
        {
            Console.CancelKeyPress -= onCancel;
        }
    }

    internal static async Task<int> RunAsync(RunOptions options, TextWriter stdout, TextWriter stderr, CancellationToken ct)
    {
        Output output = new(options.Json ? null : stdout, options.Json ? stdout : null);
        string dotnet = Worker.DotnetHost();
        string results = Path.GetFullPath(options.ResultsDirectory);
        string trxPath = Path.GetFullPath(options.TrxPath ?? Path.Combine(results, "pharos.trx"));

        // Listing evaluates names and traits only; a filter on anything else (DisplayName, Name)
        // would list nothing. Then every test is listed, and the workers apply the filter.
        bool exactDiscovery = DiscoveryCanFilter(options.Filter);
        string? listFilter = exactDiscovery ? NormalizeForListing(options.Filter) : null;
        const string FallbackNote = "The filter uses properties listing cannot evaluate: every test is grouped, and each worker applies the filter.";
        if (!exactDiscovery) output.Note(FallbackNote);

        string assembly;
        IReadOnlyList<string> tests;
        HashSet<string> live;
        try
        {
            assembly = await ResolveAssemblyAsync(options, dotnet, stderr, ct).ConfigureAwait(false);
            tests = await ListAsync(dotnet, assembly, listFilter, results, ct).ConfigureAwait(false);
            if (tests.Count == 0 && listFilter != null)
            {
                // Listing is stricter than running about some filters: list everything, and let
                // the workers decide.
                exactDiscovery = false;
                listFilter = null;
                output.Note(FallbackNote);
                tests = await ListAsync(dotnet, assembly, null, results, ct).ConfigureAwait(false);
            }

            string liveFilter = $"{XUnit.PharosTraits.Category}={XUnit.PharosTraits.Live}";
            live = (await ListAsync(dotnet, assembly, listFilter == null ? liveFilter : $"({listFilter})&{liveFilter}", results, ct).ConfigureAwait(false))
                .ToHashSet(StringComparer.Ordinal);
        }
        catch (RunSetupException ex)
        {
            stderr.WriteLine($"Error: {ex.Message}");
            return ExitError;
        }

        IReadOnlyDictionary<string, string>? collections = options.Grouping == RunGrouping.Collection ? TestGroups.Collections(assembly) : null;
        IReadOnlyList<TestGroup> groups = TestGroups.Create(tests, options.Grouping, collections, live, PreviousDurations(trxPath));
        output.Listed(assembly, groups);

        if (options.ListOnly)
        {
            output.List(groups);
            return ExitPassed;
        }

        if (groups.Count == 0)
        {
            output.Note("No tests matched.");
            return ExitPassed;
        }

        bool xvfb;
        try
        {
            xvfb = UseXvfb(options.Xvfb);
        }
        catch (RunSetupException ex)
        {
            stderr.WriteLine($"Error: {ex.Message}");
            return ExitError;
        }

        string workers = Path.Combine(results, "workers");
        if (Directory.Exists(workers)) Directory.Delete(workers, recursive: true);
        Directory.CreateDirectory(workers);
        int parallel = Math.Min(options.Parallel, groups.Count);

        Dictionary<string, string> environment = new(options.WorkerEnvironment);
        // One id for the whole run in every failure's run.json.
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable(Reporting.FailureArtifactWriter.RunIdVariable)))
        {
            environment[Reporting.FailureArtifactWriter.RunIdVariable] = Guid.NewGuid().ToString("N")[..12];
        }

        // Software rendering uses every core for each client; several clients share them.
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("LP_NUM_THREADS")) && parallel > 1)
        {
            environment["LP_NUM_THREADS"] = Math.Max(1, Environment.ProcessorCount / parallel).ToString(CultureInfo.InvariantCulture);
        }

        if (options.GamePath != null) environment["VINTAGE_STORY"] = Path.GetFullPath(options.GamePath);
        string? userArtifacts = Environment.GetEnvironmentVariable(Reporting.FailureArtifactWriter.DirectoryVariable);
        string artifacts = !string.IsNullOrEmpty(userArtifacts) ? Path.GetFullPath(userArtifacts) : Path.Combine(results, "artifacts");

        // The last run's failures would mix with this run's.
        if (string.IsNullOrEmpty(userArtifacts) && Directory.Exists(artifacts)) Directory.Delete(artifacts, recursive: true);

        Worker worker = new(dotnet, assembly, workers, options.Filter, environment);
        double scale = TimeoutScale();
        TimeSpan timeout = options.WorkerTimeout * scale;
        TimeSpan testTimeout = options.TestTimeout * scale;
        output.Started(groups.Count, parallel, xvfb);

        DateTimeOffset start = DateTimeOffset.Now;
        Stopwatch watch = Stopwatch.StartNew();
        SemaphoreSlim slots = new(parallel, parallel);
        System.Collections.Concurrent.ConcurrentQueue<int> freeSlots = new(Enumerable.Range(0, parallel));
        int finished = 0;
        List<Task<WorkerOutcome>> running = [];
        for (int i = 0; i < groups.Count; i++)
        {
            TestGroup group = groups[i];
            int index = i + 1;
            await slots.WaitAsync(CancellationToken.None).ConfigureAwait(false);
            if (ct.IsCancellationRequested)
            {
                slots.Release();
                break;
            }

            freeSlots.TryDequeue(out int slot);
            if (options.Verbose) output.Note($"worker {index}: {string.Join(' ', worker.Command(index, testTimeout))}  filter: {worker.FilterFor(group)}");
            output.GroupStarted(group, index);
            Dictionary<string, string> own = new()
            {
                ["PHAROS_WORKER"] = index.ToString(CultureInfo.InvariantCulture),
                // Artifact folders are named after the class's short name: one root per worker
                // keeps two namespaces' FooTests apart.
                [Reporting.FailureArtifactWriter.DirectoryVariable] = Path.Combine(artifacts, $"worker-{index}"),
            };
            running.Add(Task.Run(async () =>
            {
                try
                {
                    WorkerOutcome outcome = await worker.RunAsync(group, index, xvfb ? 100 + (slot * 10) : null, testTimeout, timeout, own, exactDiscovery, ct).ConfigureAwait(false);
                    output.GroupFinished(outcome, Interlocked.Increment(ref finished), groups.Count);
                    return outcome;
                }
                finally
                {
                    freeSlots.Enqueue(slot);
                    slots.Release();
                }
            }, CancellationToken.None));
        }

        WorkerOutcome[] outcomes = await Task.WhenAll(running).ConfigureAwait(false);
        watch.Stop();

        // Groups never started because the run was cancelled fail too.
        List<TrxResult> notStarted = groups.Skip(running.Count)
            .Select(g => new TrxResult($"{g.Name}.(not started)", $"{g.Name} (not started)", "Failed", TimeSpan.Zero, "The run was cancelled before this group's worker started.", null))
            .ToList();

        Directory.CreateDirectory(Path.GetDirectoryName(trxPath)!);
        XDocument merged = Trx.Merge(
            outcomes.Where(o => o.Trx != null).Select(o => o.Trx!),
            outcomes.SelectMany(o => o.Lost).Concat(notStarted),
            start,
            DateTimeOffset.Now,
            $"pharos run {Path.GetFileName(assembly)}");
        merged.Save(trxPath);

        Summary summary = new(outcomes, notStarted, watch.Elapsed, trxPath, workers);
        if (summary.Total == 0) output.Note("No tests matched.");
        output.Summary(summary);
        return summary.Failed > 0 || ct.IsCancellationRequested ? ExitFailed : ExitPassed;
    }

    /// <summary>
    /// Whether listing can apply <paramref name="filter"/>: it evaluates fully qualified names and
    /// traits, but not <c>DisplayName</c> or <c>Name</c>.
    /// </summary>
    internal static bool DiscoveryCanFilter(string? filter)
    {
        if (string.IsNullOrWhiteSpace(filter)) return true;
        return !PropertyPattern().Matches(filter)
            .Any(m => m.Groups[1].Value.Equals("DisplayName", StringComparison.OrdinalIgnoreCase) || m.Groups[1].Value.Equals("Name", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// <paramref name="filter"/> with <c>FullyQualifiedName</c> spelt as listing expects: running
    /// takes any case, listing only the exact one.
    /// </summary>
    internal static string? NormalizeForListing(string? filter) =>
        filter == null ? null : PropertyPattern().Replace(filter, m =>
            m.Groups[1].Value.Equals("FullyQualifiedName", StringComparison.OrdinalIgnoreCase) ? "FullyQualifiedName" + m.Value[m.Groups[1].Length..] : m.Value);

    // A property name and the operator after it.
    [System.Text.RegularExpressions.GeneratedRegex(@"(?<![\\\w.])([A-Za-z_][\w.]*)(?=\s*(?:!=|!~|=|~))")]
    private static partial System.Text.RegularExpressions.Regex PropertyPattern();

    // How long each test took in the last run's merged TRX, if there is one.
    private static IReadOnlyDictionary<string, TimeSpan>? PreviousDurations(string trxPath)
    {
        try
        {
            return File.Exists(trxPath) ? TestGroups.Durations(Trx.Results(XDocument.Load(trxPath))) : null;
        }
        catch (Exception ex) when (ex is System.Xml.XmlException or FormatException or IOException)
        {
            return null;
        }
    }

    /// <summary>The test assembly: the target itself, or the build output of the project it names.</summary>
    internal static async Task<string> ResolveAssemblyAsync(RunOptions options, string dotnet, TextWriter stderr, CancellationToken ct)
    {
        string target = Path.GetFullPath(options.Target);
        if (target.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
        {
            return File.Exists(target) ? target : throw new RunSetupException($"The test assembly {target} does not exist.");
        }

        string project = target;
        if (Directory.Exists(target))
        {
            string[] projects = Directory.GetFiles(target, "*.*proj").Where(p => p.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase) || p.EndsWith(".fsproj", StringComparison.OrdinalIgnoreCase) || p.EndsWith(".vbproj", StringComparison.OrdinalIgnoreCase)).ToArray();
            project = projects.Length == 1
                ? projects[0]
                : throw new RunSetupException(projects.Length == 0 ? $"{target} has no project file." : $"{target} has several project files: name one.");
        }
        else if (!File.Exists(target))
        {
            throw new RunSetupException($"{target} does not exist.");
        }

        if (!options.NoBuild)
        {
            ProcessResult build = await RunProcessAsync(dotnet, ["build", project, "-c", options.Configuration, "--nologo", "-v", "quiet"], ct).ConfigureAwait(false);
            if (build.ExitCode != 0)
            {
                stderr.WriteLine(build.Output);
                throw new RunSetupException($"Building {project} failed.");
            }
        }

        ProcessResult property = await RunProcessAsync(dotnet, ["msbuild", project, "-getProperty:TargetPath", $"-p:Configuration={options.Configuration}", "-nologo"], ct).ConfigureAwait(false);
        string path = property.Output.Trim();
        if (property.ExitCode != 0 || path.Length == 0)
        {
            throw new RunSetupException($"Could not find the test assembly of {project}; if it targets several frameworks, name the built .dll instead.");
        }

        return File.Exists(path) ? path : throw new RunSetupException($"{path} does not exist: build {project}, or leave out --no-build.");
    }

    /// <summary>The fully qualified names of the tests <paramref name="filter"/> matches; nothing runs.</summary>
    internal static async Task<IReadOnlyList<string>> ListAsync(string dotnet, string assembly, string? filter, string results, CancellationToken ct)
    {
        Directory.CreateDirectory(results);
        string list = Path.Combine(results, "tests.txt");
        File.Delete(list);

        List<string> args = ["vstest", assembly, "--ListFullyQualifiedTests", $"--ListTestsTargetPath:{list}"];
        if (!string.IsNullOrWhiteSpace(filter)) args.Add($"--TestCaseFilter:{filter}");
        ProcessResult listing = await RunProcessAsync(dotnet, args, ct).ConfigureAwait(false);
        if (listing.ExitCode != 0 || !File.Exists(list))
        {
            throw new RunSetupException($"Listing the tests of {assembly} failed:\n{listing.Output.Trim()}");
        }

        return File.ReadAllLines(list).Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
    }

    private static bool UseXvfb(XvfbMode mode)
    {
        bool installed = OnPath("xvfb-run");
        switch (mode)
        {
            case XvfbMode.Never:
                return false;
            case XvfbMode.Always:
                return installed ? true : throw new RunSetupException("--xvfb always needs xvfb-run, which is not installed.");
            default:
                return RuntimeInformation.IsOSPlatform(OSPlatform.Linux)
                    && string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DISPLAY"))
                    && string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WAYLAND_DISPLAY"))
                    && installed;
        }
    }

    private static bool OnPath(string program) =>
        (Environment.GetEnvironmentVariable("PATH") ?? "")
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
            .Any(dir => File.Exists(Path.Combine(dir, program)));

    private static double TimeoutScale() =>
        double.TryParse(Environment.GetEnvironmentVariable("PHAROS_TIMEOUT_SCALE"), NumberStyles.Float, CultureInfo.InvariantCulture, out double scale) && scale > 0 ? scale : 1;

    private sealed record ProcessResult(int ExitCode, string Output);

    private static async Task<ProcessResult> RunProcessAsync(string program, IReadOnlyList<string> args, CancellationToken ct)
    {
        ProcessStartInfo start = new(program)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
        };
        foreach (string arg in args) start.ArgumentList.Add(arg);
        using Process process = Process.Start(start) ?? throw new RunSetupException($"Could not start {program}.");
        process.StandardInput.Close();
        Task<string> stdout = process.StandardOutput.ReadToEndAsync(ct);
        Task<string> stderr = process.StandardError.ReadToEndAsync(ct);
        try
        {
            await process.WaitForExitAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            throw new RunSetupException("The run was cancelled.");
        }

        return new ProcessResult(process.ExitCode, await stdout.ConfigureAwait(false) + await stderr.ConfigureAwait(false));
    }

    internal sealed class RunSetupException(string message) : Exception(message);

    /// <summary>What a run did, for the summary.</summary>
    private sealed record Summary(IReadOnlyList<WorkerOutcome> Outcomes, IReadOnlyList<TrxResult> NotStarted, TimeSpan Duration, string TrxPath, string WorkersDirectory)
    {
        public int Passed => Outcomes.Sum(o => o.Passed);

        public int Failed => Outcomes.Sum(o => o.Failed) + NotStarted.Count;

        public int Skipped => Outcomes.Sum(o => o.Skipped);

        public int Total => Passed + Failed + Skipped;
    }

    /// <summary>Writes what happens as text or as JSON lines, one at a time.</summary>
    private sealed class Output(TextWriter? text, TextWriter? json)
    {
        private readonly object _gate = new();

        public void Note(string message)
        {
            lock (_gate)
            {
                text?.WriteLine(message);
                Json(new { @event = "note", message });
            }
        }

        public void Listed(string assembly, IReadOnlyList<TestGroup> groups)
        {
            lock (_gate)
            {
                text?.WriteLine($"{groups.Sum(g => g.Tests.Count)} test methods in {groups.Count} groups from {assembly}");
                Json(new { @event = "listed", assembly, groups = groups.Count, tests = groups.Sum(g => g.Tests.Count) });
            }
        }

        public void List(IReadOnlyList<TestGroup> groups)
        {
            lock (_gate)
            {
                foreach (TestGroup group in groups)
                {
                    text?.WriteLine($"{group.Name} ({group.Tests.Count})");
                    foreach (string test in group.Tests) text?.WriteLine("    " + test);
                    Json(new { @event = "group", group = group.Name, tests = group.Tests });
                }
            }
        }

        public void Started(int groups, int parallel, bool xvfb)
        {
            lock (_gate)
            {
                text?.WriteLine($"Running {groups} groups, {parallel} at a time{(xvfb ? ", each on its own virtual display" : "")}.");
                Json(new { @event = "started", groups, parallel, xvfb });
            }
        }

        public void GroupStarted(TestGroup group, int index)
        {
            lock (_gate) Json(new { @event = "groupStarted", group = group.Name, worker = index, tests = group.Tests.Count });
        }

        public void GroupFinished(WorkerOutcome outcome, int finished, int total)
        {
            lock (_gate)
            {
                string state = outcome.TimedOut ? " TIMED OUT" : outcome.Crashed ? " CRASHED" : "";
                text?.WriteLine($"[{finished}/{total}] {outcome.Group.Name}: {outcome.Passed} passed, {outcome.Failed} failed, {outcome.Skipped} skipped ({Format(outcome.Duration)}){state}");
                foreach (TrxResult result in outcome.Results.Concat(outcome.Lost))
                {
                    Json(new
                    {
                        @event = "test",
                        name = result.TestName,
                        fullyQualifiedName = result.FullyQualifiedName,
                        outcome = result.Passed ? "passed" : result.Failed ? "failed" : "skipped",
                        durationMs = (long)result.Duration.TotalMilliseconds,
                        message = result.Message,
                    });
                }

                Json(new
                {
                    @event = "groupFinished",
                    group = outcome.Group.Name,
                    worker = outcome.Index,
                    exitCode = outcome.ExitCode,
                    timedOut = outcome.TimedOut,
                    crashed = outcome.Crashed,
                    aborted = outcome.Aborted,
                    passed = outcome.Passed,
                    failed = outcome.Failed,
                    skipped = outcome.Skipped,
                    durationMs = (long)outcome.Duration.TotalMilliseconds,
                    log = outcome.LogPath,
                });
            }
        }

        public void Summary(Summary summary)
        {
            lock (_gate)
            {
                Json(new
                {
                    @event = "summary",
                    passed = summary.Passed,
                    failed = summary.Failed,
                    skipped = summary.Skipped,
                    total = summary.Total,
                    durationMs = (long)summary.Duration.TotalMilliseconds,
                    trx = summary.TrxPath,
                });
                if (text == null) return;

                text.WriteLine();
                List<(string Name, string? Message)> failures = summary.Outcomes
                    .SelectMany(o => o.Results.Where(r => r.Failed).Concat(o.Lost))
                    .Concat(summary.NotStarted)
                    .Select(r => (r.TestName, r.Message))
                    .ToList();
                if (failures.Count > 0)
                {
                    text.WriteLine("Failed tests:");
                    foreach ((string name, string? message) in failures)
                    {
                        text.WriteLine($"  {name}");
                        foreach (string line in (message ?? "").Split('\n').Take(3)) text.WriteLine($"      {line.TrimEnd()}");
                    }

                    text.WriteLine();
                }

                text.WriteLine("Slowest groups:");
                foreach (WorkerOutcome slow in summary.Outcomes.OrderByDescending(o => o.Duration).Take(5))
                {
                    text.WriteLine($"  {Format(slow.Duration),8}  {slow.Group.Name}");
                }

                text.WriteLine();
                text.WriteLine($"{(summary.Failed > 0 ? "Failed!" : "Passed!")}  - Failed: {summary.Failed}, Passed: {summary.Passed}, Skipped: {summary.Skipped}, Total: {summary.Total}, Duration: {Format(summary.Duration)}");
                text.WriteLine($"Results: {summary.TrxPath}");
                text.WriteLine($"Worker logs: {summary.WorkersDirectory}");
            }
        }

        private void Json(object value)
        {
            json?.WriteLine(JsonSerializer.Serialize(value));
            json?.Flush();
        }

        private static string Format(TimeSpan time) =>
            time.TotalMinutes >= 1
                ? string.Create(CultureInfo.InvariantCulture, $"{(int)time.TotalMinutes}m{time.Seconds:00}s")
                : string.Create(CultureInfo.InvariantCulture, $"{time.TotalSeconds:0.0}s");
    }
}
