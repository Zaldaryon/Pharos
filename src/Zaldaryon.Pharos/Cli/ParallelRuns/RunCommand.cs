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
          --repeat <n>             Run the full selection n times (default: 1)
          --until-fail --max <n>   Repeat full runs until a failure or the maximum is reached
          --rerun-failed <json>    Run raw failed test rows from a prior pharos-run.json
          --quarantine <json>      Apply an expiring quarantine to eligible body failures
          --shard <n>/<total>      Run only the groups assigned to shard n of total
          --timings <path>         A TRX or JSON timing file for balancing shards
          --trx <path>             Where the merged TRX goes (default: <results-dir>/pharos.trx)
          --manifest <json>        Where the run manifest JSON goes (also --json <path>)
          --report <dir>           Write report.html, report.md and timings.json there
          --results-dir <dir>      Worker logs, TRX files and failure artifacts (default: pharos-run)
          --test-timeout <min>     Stop a test host whose test runs longer, keeping the results
                                   so far (default: 30, scaled by PHAROS_TIMEOUT_SCALE)
          --worker-timeout <min>   Stop a worker after this many minutes (default: 60,
                                   scaled by PHAROS_TIMEOUT_SCALE)
          --game <path>            The Vintage Story install workers use (default: VINTAGE_STORY)
          --json [path]            Write JSON lines to standard output, or save manifest to path
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
        if (options.WaitForDebugger)
        {
            stdout.WriteLine($"[pharos] waiting for debugger, PID {Environment.ProcessId}");
            stdout.Flush();
            while (!Debugger.IsAttached) Thread.Sleep(250);
            stdout.WriteLine("[pharos] debugger attached.");
        }
        if (options.Repeat < 1 || (options.UntilFail && options.Max is null or < 1) || (!options.UntilFail && options.Max != null) || (options.UntilFail && options.Repeat != 1))
        {
            stderr.WriteLine("Error: --repeat must be positive; --until-fail requires positive --max and cannot be combined with --repeat.");
            return ExitError;
        }

        if (options.LoadedQuarantine == null)
        {
            string defaultQuarantine = Path.Combine(Environment.CurrentDirectory, "pharos.quarantine.json");
            string? quarantinePath = options.QuarantineFile ?? (File.Exists(defaultQuarantine) ? defaultQuarantine : null);
            if (options.QuarantineFile != null && !File.Exists(options.QuarantineFile))
            {
                stderr.WriteLine($"Error: quarantine file {Path.GetFullPath(options.QuarantineFile)} does not exist.");
                return ExitError;
            }
            if (quarantinePath != null)
            {
                try
                {
                    options = options with { LoadedQuarantine = QuarantinePolicy.Load(quarantinePath, DateOnly.FromDateTime(DateTime.UtcNow)) };
                    stdout.WriteLine($"Using quarantine file: {Path.GetFullPath(quarantinePath)} (UTC expiry day is valid through that date).");
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidDataException or FormatException)
                {
                    stderr.WriteLine($"Error: {ex.Message}");
                    return ExitError;
                }
            }
        }

        if (options.RerunFailed is { } sourceManifestPath)
        {
            if (options.Repeat != 1 || options.UntilFail)
            {
                stderr.WriteLine("Error: --rerun-failed cannot be combined with --repeat or --until-fail.");
                return ExitError;
            }

            string filter;
            string discoveryFilter;
            try
            {
                IReadOnlyList<(string FullyQualifiedName, string DisplayName)> failureRows = FailedRows(sourceManifestPath);
                if (failureRows.Count == 0)
                {
                    stdout.WriteLine($"No failed tests in {Path.GetFullPath(sourceManifestPath)}; nothing to rerun.");
                    return ExitPassed;
                }
                string rerunFilter = string.Join("|", failureRows.Select(test => Filter.ForTestCase(test.FullyQualifiedName, test.DisplayName)));
                string failedNames = string.Join("|", failureRows.Select(test => Filter.ForTest(test.FullyQualifiedName)).Distinct(StringComparer.Ordinal));
                discoveryFilter = options.Filter is { Length: > 0 } currentFilter && DiscoveryCanFilter(currentFilter)
                    ? $"({currentFilter})&({failedNames})"
                    : failedNames;
                filter = options.Filter is { Length: > 0 } current ? $"({current})&({rerunFilter})" : rerunFilter;
                stdout.WriteLine($"Rerunning {failureRows.Count} failed test row(s) from {Path.GetFullPath(sourceManifestPath)}.");
                Dictionary<string, string> workerEnvironment = new(options.WorkerEnvironment)
                {
                    [ScenarioGate.RunExplicitVariable] = "1",
                };
                options = options with { WorkerEnvironment = workerEnvironment };
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
            {
                stderr.WriteLine($"Error: {ex.Message}");
                return ExitError;
            }
            options = options with { Filter = filter, DiscoveryFilter = discoveryFilter, RerunFailed = null };
        }

        int maximumRuns = options.UntilFail ? options.Max!.Value : options.Repeat;
        if (maximumRuns == 1 && !options.UntilFail) return await RunOnceAsync(options, stdout, stderr, ct).ConfigureAwait(false);

        if (options.ListOnly)
        {
            stderr.WriteLine("Error: --list cannot be combined with repeated runs.");
            return ExitError;
        }

        string root = Path.GetFullPath(options.ResultsDirectory);
        List<object> runs = [];
        bool failed = false;
        for (int number = 1; number <= maximumRuns; number++)
        {
            if (ct.IsCancellationRequested) { failed = true; break; }
            string runDirectory = Path.Combine(root, $"run-{number:D3}");
            if (Directory.Exists(runDirectory)) Directory.Delete(runDirectory, recursive: true);
            string runTrx = Path.Combine(runDirectory, Path.GetFileName(options.TrxPath ?? "pharos.trx"));
            stdout.WriteLine($"Run {number}/{maximumRuns}");
            int exit = await RunOnceAsync(options with
            {
                Repeat = 1,
                UntilFail = false,
                Max = null,
                ResultsDirectory = runDirectory,
                TrxPath = runTrx,
            }, stdout, stderr, ct).ConfigureAwait(false);
            failed |= exit != ExitPassed;

            string runManifestPath = ScenarioRunManifestWriter.PathFor(runTrx);
            JsonElement? manifest = null;
            if (File.Exists(runManifestPath))
            {
                using JsonDocument document = JsonDocument.Parse(File.ReadAllText(runManifestPath));
                manifest = document.RootElement.Clone();
            }
            runs.Add(new { number, exitCode = exit, manifest = Path.GetFullPath(runManifestPath), data = manifest });
            if (options.UntilFail && (exit != ExitPassed || manifest is { } data && HasRawFailure(data))) break;
        }

        Directory.CreateDirectory(root);
        string aggregatePath = Path.Combine(root, "pharos-repeat.json");
        string temporary = aggregatePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(new
            {
                schemaVersion = 1,
                mode = options.UntilFail ? "until-fail" : "repeat",
                requestedRuns = maximumRuns,
                completedRuns = runs.Count,
                runs,
            }, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temporary, aggregatePath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
        stdout.WriteLine($"Aggregate run manifest: {aggregatePath}");
        return failed || ct.IsCancellationRequested ? ExitFailed : ExitPassed;
    }

    private static async Task<int> RunOnceAsync(RunOptions options, TextWriter stdout, TextWriter stderr, CancellationToken ct)
    {
        Output output = new(options.Json ? null : stdout, options.Json ? stdout : null);
        string dotnet = Worker.DotnetHost();
        string results = Path.GetFullPath(options.ResultsDirectory);
        string trxPath = Path.GetFullPath(options.TrxPath ?? Path.Combine(results, "pharos.trx"));
        string previousManifest = ScenarioRunManifestWriter.PathFor(trxPath);
        if (File.Exists(previousManifest)) File.Delete(previousManifest);

        // Listing evaluates names and traits only; a filter on anything else (DisplayName, Name)
        // would list nothing. Then every test is listed, and the workers apply the filter.
        string? discoveryFilter = options.DiscoveryFilter ?? options.Filter;
        // Rerun discovery narrows to failed FQNs, while execution also filters exact theory rows
        // (and may intersect a user filter). It is therefore a candidate superset, not an exact
        // list of the rows that must report results.
        bool exactDiscovery = options.DiscoveryFilter == null && DiscoveryCanFilter(options.Filter);
        bool canFilterDiscovery = DiscoveryCanFilter(discoveryFilter);
        string? listFilter = canFilterDiscovery ? NormalizeForListing(discoveryFilter) : null;
        const string FallbackNote = "The filter uses properties listing cannot evaluate: every test is grouped, and each worker applies the filter.";
        if (!canFilterDiscovery) output.Note(FallbackNote);

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
        IReadOnlyDictionary<string, TimeSpan>? durations = PreviousDurations(trxPath, options.TimingsPath);
        IReadOnlyList<TestGroup> groups = TestGroups.Create(tests, options.Grouping, collections, live, durations);
        if (options.Shard is { } shard)
        {
            groups = TestGroups.PartitionForShard(groups, shard.Index, shard.Total, durations);
            output.Note($"Shard {shard.Index}/{shard.Total}: {groups.Count} group(s), {groups.Sum(g => g.Tests.Count)} test(s).");
        }

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
        string attemptsDirectory = Path.Combine(results, "attempts");
        if (Directory.Exists(attemptsDirectory)) Directory.Delete(attemptsDirectory, recursive: true);
        Directory.CreateDirectory(attemptsDirectory);
        int parallel = Math.Min(options.Parallel, groups.Count);

        Dictionary<string, string> environment = new(options.WorkerEnvironment);
        // A name filter is an explicit request for the selected scenario. Broad trait filters
        // such as Category=Live do not opt every explicit scenario into the run.
        if (SelectsByTestName(options.Filter) && string.IsNullOrEmpty(Environment.GetEnvironmentVariable(ScenarioGate.RunExplicitVariable)))
        {
            environment[ScenarioGate.RunExplicitVariable] = "1";
        }
        // One id for the whole run in every failure's run.json.
        string runId = environment.GetValueOrDefault(Reporting.FailureArtifactWriter.RunIdVariable)
            ?? Environment.GetEnvironmentVariable(Reporting.FailureArtifactWriter.RunIdVariable)
            ?? Guid.NewGuid().ToString("N")[..12];
        environment[Reporting.FailureArtifactWriter.RunIdVariable] = runId;

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

        Worker worker = new(dotnet, assembly, workers, options.Filter, environment, options.WorkingDirectory);
        double scale = TimeoutScale();
        TimeSpan timeout = options.WorkerTimeout * scale;
        TimeSpan testTimeout = options.TestTimeout * scale;
        string? gamePath = options.GamePath != null ? Path.GetFullPath(options.GamePath) : GamePathOrNull();
        output.Started(groups.Count, parallel, xvfb, gamePath, gamePath == null ? null : InstalledGame.ReadVersion(gamePath));

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
                [Zaldaryon.Pharos.Scenarios.ScenarioAttemptManifestWriter.DirectoryVariable] = Path.Combine(attemptsDirectory, $"worker-{index}"),
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

        string manifestPath = options.ManifestPath != null
            ? Path.GetFullPath(options.ManifestPath)
            : ScenarioRunManifestWriter.PathFor(trxPath);
        ScenarioRunManifest runManifest = ScenarioRunManifestWriter.Write(manifestPath, runId, start, DateTimeOffset.Now, assembly, trxPath, merged,
            outcomes, notStarted, attemptsDirectory, options.LoadedQuarantine);
        output.Note($"Attempt manifest: {manifestPath}");

        if (!string.IsNullOrWhiteSpace(options.ReportDirectory))
        {
            string reportDirectory = Path.GetFullPath(options.ReportDirectory);
            try
            {
                ReportPaths report = ReportCommand.Generate(trxPath, manifestPath, artifacts,
                    Path.Combine(reportDirectory, "report.html"),
                    Path.Combine(reportDirectory, "report.md"),
                    Path.Combine(reportDirectory, "timings.json"));
                output.Note($"Report: {report.Html}");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException or System.Xml.XmlException)
            {
                // The tests already ran; a report that cannot be written does not change their result.
                output.Note($"Report could not be written: {ex.Message}");
            }
        }

        HashSet<(string FullyQualifiedName, string DisplayName)> quarantined = runManifest.Tests
            .Where(test => test.Quarantined)
            .Select(test => (test.FullyQualifiedName, test.DisplayName)).ToHashSet();
        if (quarantined.Count > 0)
        {
            foreach (ScenarioRunTest test in runManifest.Tests.Where(test => test.Quarantined))
                output.Note($"Quarantined {test.FullyQualifiedName} ({test.DisplayName}) — owner {test.QuarantineOwner}, expires {test.QuarantineExpiresOn} UTC.");
        }
        Summary summary = new(outcomes, notStarted, watch.Elapsed, trxPath, workers, quarantined);
        if (summary.Total == 0) output.Note("No tests matched.");
        output.Summary(summary);
        bool infrastructureFailed = outcomes.Any(outcome => outcome.Crashed || outcome.TimedOut || outcome.Lost.Count > 0) || notStarted.Count > 0;
        bool testsFailed = Trx.Results(merged).Any(result => result.Failed && !quarantined.Contains((result.FullyQualifiedName, result.TestName)));
        return infrastructureFailed || testsFailed || ct.IsCancellationRequested ? ExitFailed : ExitPassed;
    }

    internal static IReadOnlyList<(string FullyQualifiedName, string DisplayName)> FailedRows(string path)
    {
        if (!File.Exists(path)) throw new InvalidDataException($"The pharos run manifest {Path.GetFullPath(path)} does not exist.");
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
        JsonElement root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("schemaVersion", out JsonElement schema) || schema.ValueKind != JsonValueKind.Number ||
            !schema.TryGetInt32(out int version) || version != 1)
            throw new InvalidDataException("The file is not a supported pharos run manifest (schemaVersion 1 expected).");
        List<JsonElement> testArrays = [];
        if (root.TryGetProperty("tests", out JsonElement tests))
        {
            if (tests.ValueKind != JsonValueKind.Array) throw new InvalidDataException("The pharos run manifest needs a tests array.");
            testArrays.Add(tests);
        }
        else if (root.TryGetProperty("runs", out JsonElement runs) && runs.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement run in runs.EnumerateArray())
            {
                if (run.ValueKind != JsonValueKind.Object || !run.TryGetProperty("data", out JsonElement data))
                    throw new InvalidDataException("The pharos repeat manifest contains an invalid run entry.");
                // An interrupted run can have no aggregate data yet. It has no raw test rows to rerun.
                if (data.ValueKind == JsonValueKind.Null) continue;
                if (data.ValueKind != JsonValueKind.Object || !data.TryGetProperty("schemaVersion", out JsonElement runSchema) ||
                    runSchema.ValueKind != JsonValueKind.Number || !runSchema.TryGetInt32(out int runVersion) || runVersion != 1 ||
                    !data.TryGetProperty("tests", out JsonElement runTests) || runTests.ValueKind != JsonValueKind.Array)
                    throw new InvalidDataException("The pharos repeat manifest contains unsupported run data.");
                testArrays.Add(runTests);
            }
        }
        else throw new InvalidDataException("The file is not a supported pharos run manifest (schemaVersion 1 expected).");

        List<(string FullyQualifiedName, string DisplayName)> failedRows = [];
        foreach (JsonElement array in testArrays)
        {
            foreach (JsonElement test in array.EnumerateArray())
            {
                if (test.ValueKind != JsonValueKind.Object || !test.TryGetProperty("rawOutcome", out JsonElement outcome) || outcome.ValueKind != JsonValueKind.String ||
                    !test.TryGetProperty("fullyQualifiedName", out JsonElement fqnElement) || fqnElement.ValueKind != JsonValueKind.String ||
                    !test.TryGetProperty("displayName", out JsonElement displayElement) || displayElement.ValueKind != JsonValueKind.String)
                    throw new InvalidDataException("The pharos run manifest contains an invalid test row.");
                if (outcome.GetString() != "Failed") continue;
                string? fqn = fqnElement.GetString();
                string? display = displayElement.GetString();
                if (string.IsNullOrWhiteSpace(fqn) || string.IsNullOrWhiteSpace(display) ||
                    display.Contains("(worker aborted)", StringComparison.Ordinal) || display.Contains("(not started)", StringComparison.Ordinal)) continue;
                failedRows.Add((fqn, display));
            }
        }
        return failedRows.Distinct().ToArray();
    }

    private static bool HasRawFailure(JsonElement manifest) =>
        manifest.ValueKind == JsonValueKind.Object && manifest.TryGetProperty("tests", out JsonElement tests) && tests.ValueKind == JsonValueKind.Array &&
        tests.EnumerateArray().Any(test => test.ValueKind == JsonValueKind.Object && test.TryGetProperty("rawOutcome", out JsonElement outcome) && outcome.ValueKind == JsonValueKind.String && outcome.GetString() is "Failed" or "Error" or "Timeout" or "Aborted");

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

    internal static bool SelectsByTestName(string? filter) =>
        !string.IsNullOrWhiteSpace(filter) && ExplicitNameFilterPattern().IsMatch(filter);

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

    // Opt in only when the entire expression is one positive name selector. VSTest filters can
    // combine and negate terms, so finding a name property somewhere in the expression is unsafe.
    [System.Text.RegularExpressions.GeneratedRegex(@"^\s*(?:FullyQualifiedName|Name)\s*(?:=|~)\s*[^!|&()]+\s*$", System.Text.RegularExpressions.RegexOptions.IgnoreCase)]
    private static partial System.Text.RegularExpressions.Regex ExplicitNameFilterPattern();

    // How long each test took in the last run's merged TRX or timing baseline, if there is one.
    private static IReadOnlyDictionary<string, TimeSpan>? PreviousDurations(string trxPath, string? timingsPath = null)
    {
        if (timingsPath != null && TestGroups.LoadDurations(timingsPath) is { } fromExplicit) return fromExplicit;
        if (File.Exists(trxPath) && TestGroups.LoadDurations(trxPath) is { } fromTrx) return fromTrx;
        string defaultTimings = "scripts/ci-timings.json";
        if (File.Exists(defaultTimings) && TestGroups.LoadDurations(defaultTimings) is { } fromDefault) return fromDefault;
        return null;
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

    /// <summary>Whether the assembly uses Microsoft.Testing.Platform / xUnit v3 runner.</summary>
    internal static bool IsTestingPlatform(string assembly)
    {
        string deps = Path.ChangeExtension(assembly, ".deps.json");
        if (!File.Exists(deps)) return false;
        try
        {
            string text = File.ReadAllText(deps);
            return text.Contains("\"Microsoft.Testing.Platform\"") || text.Contains("\"xunit.v3\"");
        }
        catch
        {
            return false;
        }
    }

    /// <summary>The fully qualified names of the tests <paramref name="filter"/> matches; nothing runs.</summary>
    internal static async Task<IReadOnlyList<string>> ListAsync(string dotnet, string assembly, string? filter, string results, CancellationToken ct)
    {
        Directory.CreateDirectory(results);
        string list = Path.Combine(results, "tests.txt");
        File.Delete(list);

        if (IsTestingPlatform(assembly))
        {
            List<string> args = [assembly, "-list", "methods"];
            if (!string.IsNullOrWhiteSpace(filter))
            {
                if (filter.Contains('=') && !filter.Contains('|') && !filter.Contains('&') && !filter.Contains('~'))
                {
                    args.AddRange(["-trait", filter]);
                }
                else
                {
                    args.AddRange(["-filter", filter]);
                }
            }

            ProcessResult listing = await RunProcessAsync(dotnet, args, ct).ConfigureAwait(false);
            if (listing.ExitCode != 0)
            {
                throw new RunSetupException($"Listing the tests of {assembly} failed:\n{listing.Output.Trim()}");
            }

            return listing.Output
                .Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(l => !l.StartsWith("xUnit.net") && !l.StartsWith("===") && l.Contains('.'))
                .ToList();
        }

        List<string> vstestArgs = ["vstest", assembly, "--ListFullyQualifiedTests", $"--ListTestsTargetPath:{list}"];
        if (!string.IsNullOrWhiteSpace(filter)) vstestArgs.Add($"--TestCaseFilter:{filter}");
        ProcessResult vstestListing = await RunProcessAsync(dotnet, vstestArgs, ct).ConfigureAwait(false);
        if (vstestListing.ExitCode != 0 || !File.Exists(list))
        {
            throw new RunSetupException($"Listing the tests of {assembly} failed:\n{vstestListing.Output.Trim()}");
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
    internal sealed record Summary(IReadOnlyList<WorkerOutcome> Outcomes, IReadOnlyList<TrxResult> NotStarted, TimeSpan Duration, string TrxPath, string WorkersDirectory,
        IReadOnlySet<(string FullyQualifiedName, string DisplayName)> Quarantined)
    {
        public int Passed => Outcomes.Sum(o => o.Passed);

        public int Failed => Outcomes.Sum(o => o.Lost.Count + o.Results.Count(r => r.Failed && !Quarantined.Contains((r.FullyQualifiedName, r.TestName)))) + NotStarted.Count;

        public int Skipped => Outcomes.Sum(o => o.Results.Count(r => r.Skipped && !Quarantined.Contains((r.FullyQualifiedName, r.TestName))));

        public int QuarantinedCount => Quarantined.Count;

        public int Total => Passed + Failed + Skipped + QuarantinedCount;
    }

    // The install workers will use when no --game is given; nothing is booted to find it.
    private static string? GamePathOrNull()
    {
        try
        {
            string path = Platform.HeadlessPlatformResolver.ResolveGamePath();
            return File.Exists(Path.Combine(path, "VintagestoryAPI.dll")) ? path : null;
        }
        catch
        {
            return null;
        }
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

        public void Started(int groups, int parallel, bool xvfb, string? gamePath, string? gameVersion)
        {
            lock (_gate)
            {
                OptimumInfo? optimum = gamePath == null ? null : OptimumInstall.Detect(gamePath);
                if (gamePath != null) text?.WriteLine($"Vintage Story {gameVersion ?? "(version unknown)"}{(optimum == null ? "" : $" ({OptimumInstall.Describe(optimum)})")} at {gamePath}");
                text?.WriteLine($"Running {groups} groups, {parallel} at a time{(xvfb ? ", each on its own virtual display" : "")}.");
                Json(new { @event = "started", groups, parallel, xvfb, gamePath, gameVersion, optimum = optimum?.Version });
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
                    quarantined = summary.QuarantinedCount,
                    total = summary.Total,
                    durationMs = (long)summary.Duration.TotalMilliseconds,
                    trx = summary.TrxPath,
                });
                if (text == null) return;

                text.WriteLine();
                List<(string Name, string? Message)> failures = summary.Outcomes
                    .SelectMany(o => o.Results.Where(r => r.Failed && !summary.Quarantined.Contains((r.FullyQualifiedName, r.TestName))).Concat(o.Lost))
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
                text.WriteLine($"{(summary.Failed > 0 ? "Failed!" : "Passed!")}  - Failed: {summary.Failed}, Passed: {summary.Passed}, Skipped: {summary.Skipped}, Quarantined: {summary.QuarantinedCount}, Total: {summary.Total}, Duration: {Format(summary.Duration)}");
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
