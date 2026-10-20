using System.Diagnostics;
using System.Globalization;
using Zaldaryon.Pharos.Statistics;
using Zaldaryon.Pharos.XUnit;

namespace Zaldaryon.Pharos.Cli.ParallelRuns.Compare;

/// <summary>The options of <c>pharos compare</c>.</summary>
internal sealed record CompareOptions
{
    /// <summary>The test assembly, or the project or folder to build first.</summary>
    public required string Target { get; init; }

    /// <summary>What to compare against: a git ref, a run manifest, a mod build or <c>id@version</c>.</summary>
    public required string BaselineSpec { get; init; }

    /// <summary>How many runs each side gets, interleaved.</summary>
    public int Repeat { get; init; } = 5;

    /// <summary>A <c>dotnet test --filter</c> expression.</summary>
    public string? Filter { get; init; }

    /// <summary>How many workers each run uses.</summary>
    public int Parallel { get; init; } = 1;

    /// <summary>The shard to run, 1-indexed, applied to both sides.</summary>
    public ShardOptions? Shard { get; init; }

    /// <summary>The game install; null for <c>VINTAGE_STORY</c>.</summary>
    public string? GamePath { get; init; }

    /// <summary>The configuration a project is built in.</summary>
    public string Configuration { get; init; } = "Release";

    /// <summary>Uses the existing builds instead of building.</summary>
    public bool NoBuild { get; init; }

    /// <summary>When workers get a virtual display of their own.</summary>
    public XvfbMode Xvfb { get; init; } = XvfbMode.Auto;

    /// <summary>The significance level a p-value is compared against.</summary>
    public double Alpha { get; init; } = 0.05;

    /// <summary>The smallest relative change worth flagging, as a fraction.</summary>
    public double MinEffect { get; init; } = 0.01;

    /// <summary>The budgets file; null when no metric may fail the command.</summary>
    public string? BudgetsPath { get; init; }

    /// <summary>Where to write the Markdown table.</summary>
    public string? MarkdownPath { get; init; }

    /// <summary>Where to write the JSON report.</summary>
    public string? JsonPath { get; init; }

    /// <summary>Where the per-run artifacts, worker logs and manifests go.</summary>
    public string ResultsDirectory { get; init; } = "pharos-compare";

    /// <summary>The bootstrap resamples behind the confidence interval.</summary>
    public int BootstrapSamples { get; init; } = RankStatistics.DefaultBootstrapSamples;

    /// <summary>The bootstrap seed.</summary>
    public int Seed { get; init; } = RankStatistics.DefaultSeed;

    /// <summary>Prints each run's command line.</summary>
    public bool Verbose { get; init; }

    /// <summary>Reads the arguments that follow <c>compare</c>; null asks for the help.</summary>
    /// <exception cref="ArgumentException">An argument is unknown, missing its value, or out of range.</exception>
    public static CompareOptions? Parse(IReadOnlyList<string> args)
    {
        string? target = null, baseline = null;
        CompareOptions options = new() { Target = "", BaselineSpec = "" };
        for (int i = 0; i < args.Count; i++)
        {
            string arg = args[i];
            string Value() => i + 1 < args.Count ? args[++i] : throw new ArgumentException($"{arg} needs a value.");

            switch (arg)
            {
                case "-h" or "--help":
                    return null;
                case "--baseline" or "-b":
                    baseline = Value();
                    break;
                case "--repeat":
                    options = options with { Repeat = Positive(arg, Value()) };
                    break;
                case "--filter":
                    options = options with { Filter = Value() };
                    break;
                case "--parallel" or "-p":
                    options = options with { Parallel = Positive(arg, Value()) };
                    break;
                case "--shard":
                    options = options with { Shard = ParseShard(arg, Value()) };
                    break;
                case "--game":
                    options = options with { GamePath = Value() };
                    break;
                case "--configuration" or "-c":
                    options = options with { Configuration = Value() };
                    break;
                case "--no-build":
                    options = options with { NoBuild = true };
                    break;
                case "--xvfb":
                    options = options with
                    {
                        Xvfb = Value() switch
                        {
                            "auto" => XvfbMode.Auto,
                            "always" => XvfbMode.Always,
                            "never" => XvfbMode.Never,
                            string other => throw new ArgumentException($"--xvfb takes auto, always or never, not '{other}'."),
                        },
                    };
                    break;
                case "--alpha":
                    options = options with { Alpha = Fraction(arg, Value(), 0.5) };
                    break;
                case "--min-effect":
                    options = options with { MinEffect = Percent(arg, Value()) };
                    break;
                case "--bootstrap":
                    options = options with { BootstrapSamples = Positive(arg, Value()) };
                    break;
                case "--seed":
                    options = options with { Seed = NonNegative(arg, Value()) };
                    break;
                case "--budgets":
                    options = options with { BudgetsPath = Value() };
                    break;
                case "--md":
                    options = options with { MarkdownPath = Value() };
                    break;
                case "--json":
                    options = options with { JsonPath = Value() };
                    break;
                case "--results-dir":
                    options = options with { ResultsDirectory = Value() };
                    break;
                case "-v" or "--verbose":
                    options = options with { Verbose = true };
                    break;
                default:
                    if (arg.StartsWith('-')) throw new ArgumentException($"Unknown option: {arg}");
                    if (target != null) throw new ArgumentException($"Only one test assembly or project can be compared, not '{target}' and '{arg}'.");
                    target = arg;
                    break;
            }
        }

        if (target == null) throw new ArgumentException("Name the test assembly or project to compare.");
        if (baseline == null) throw new ArgumentException("Name a baseline with --baseline <git ref | run.json | mod zip | id@version>.");
        return options with { Target = target, BaselineSpec = baseline };
    }

    private static int Positive(string name, string value) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int number) && number > 0
            ? number
            : throw new ArgumentException($"{name} takes a whole number above 0, not '{value}'.");

    private static int NonNegative(string name, string value) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int number) && number >= 0
            ? number
            : throw new ArgumentException($"{name} takes a whole number, not '{value}'.");

    private static ShardOptions ParseShard(string name, string value)
    {
        int slash = value.IndexOf('/');
        if (slash <= 0 || slash >= value.Length - 1
            || !int.TryParse(value[..slash], NumberStyles.Integer, CultureInfo.InvariantCulture, out int index)
            || !int.TryParse(value[(slash + 1)..], NumberStyles.Integer, CultureInfo.InvariantCulture, out int total)
            || index < 1 || total < 1 || index > total)
        {
            throw new ArgumentException($"{name} takes <index>/<total> with 1 <= index <= total, not '{value}'.");
        }
        return new ShardOptions(index, total);
    }

    // "0.05" or "5%" for a significance level between 0 and exclusive of the upper bound.
    private static double Fraction(string name, string value, double maximum)
    {
        bool percent = value.EndsWith('%');
        if (!double.TryParse(percent ? value[..^1] : value, NumberStyles.Float, CultureInfo.InvariantCulture, out double number)
            || !double.IsFinite(number))
        {
            throw new ArgumentException($"{name} takes a number below {maximum.ToString(CultureInfo.InvariantCulture)}, not '{value}'.");
        }
        double fraction = percent ? number / 100 : number;
        if (fraction <= 0 || fraction >= maximum)
        {
            throw new ArgumentException($"{name} takes a number above 0 and below {maximum.ToString(CultureInfo.InvariantCulture)}, not '{value}'.");
        }
        return fraction;
    }

    // "5%" for a relative change. The percent sign is required, as the help says.
    private static double Percent(string name, string value)
    {
        if (!value.EndsWith('%')
            || !double.TryParse(value[..^1], NumberStyles.Float, CultureInfo.InvariantCulture, out double number)
            || !double.IsFinite(number) || number < 0)
        {
            throw new ArgumentException($"{name} takes a percentage such as 5%, not '{value}'.");
        }
        return number / 100;
    }
}

/// <summary>What a comparison needs from the environment, so the resolution is testable.</summary>
/// <param name="CurrentDirectory">The repository root the command runs from.</param>
/// <param name="DataRoot">Pharos's data folder, where a released mod zip is looked for.</param>
/// <param name="GitRefExists">Checks whether a git ref resolves in the repository.</param>
internal sealed record BaselineEnvironment(string CurrentDirectory, string DataRoot, Func<string, bool> GitRefExists);

/// <summary>How a baseline is provided.</summary>
internal enum BaselineKind
{
    /// <summary>A previous run's manifest or a folder holding one; nothing is rebuilt or run.</summary>
    Run,

    /// <summary>A mod build (a zip, a folder or a dll) staged in place of the declared one.</summary>
    ModBuild,

    /// <summary>A git ref, built in a temporary worktree.</summary>
    GitRef,
}

/// <summary>A baseline resolved to what the command must do with it.</summary>
/// <param name="Kind">How the baseline is provided.</param>
/// <param name="Description">How the report names it.</param>
/// <param name="Path">The manifest, the mod build or the git ref.</param>
internal sealed record BaselinePlan(BaselineKind Kind, string Description, string Path);

/// <summary>
/// Resolves <c>--baseline</c>: a previous run, a mod build, a released zip in the local cache, or a
/// git ref built in a temporary worktree.
/// </summary>
internal static class BaselinePlanner
{
    /// <summary>Resolves the baseline.</summary>
    /// <param name="spec">The <c>--baseline</c> value.</param>
    /// <param name="environment">The paths and git check to use.</param>
    /// <returns>The plan.</returns>
    /// <exception cref="ArgumentException">The baseline cannot be resolved.</exception>
    public static BaselinePlan Resolve(string spec, BaselineEnvironment environment)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(spec);
        ArgumentNullException.ThrowIfNull(environment);

        if (File.Exists(spec))
        {
            if (LooksLikeManifest(spec)) return new BaselinePlan(BaselineKind.Run, $"run {Path.GetFullPath(spec)}", spec);
            if (LooksLikeModBuild(spec)) return new BaselinePlan(BaselineKind.ModBuild, $"mod {spec}", spec);
            throw new ArgumentException(
                $"'{spec}' is a file, but neither a run manifest (a pharos-run.json) nor a mod build (a .zip or a .dll). "
                + "Pass the manifest a run wrote, the zip of the mod build, or the id@version of a released one.");
        }

        if (Directory.Exists(spec))
        {
            string manifest = Path.Combine(spec, "pharos-run.json");
            if (File.Exists(manifest)) return new BaselinePlan(BaselineKind.Run, $"run {Path.GetFullPath(manifest)}", manifest);
            if (File.Exists(Path.Combine(spec, "modinfo.json")) || Directory.EnumerateFiles(spec, "*.zip").Any())
            {
                return new BaselinePlan(BaselineKind.ModBuild, $"mod folder {spec}", spec);
            }
            throw new ArgumentException($"'{spec}' is a folder, but neither a run (pharos-run.json) nor a mod (modinfo.json or a zip).");
        }

        if (TryParseModSpec(spec, out string? id, out string? version))
        {
            string? cached = FindCachedMod(environment.DataRoot, id!, version!);
            return cached != null
                ? new BaselinePlan(BaselineKind.ModBuild, $"mod {id}@{version}", cached)
                : throw new ArgumentException(
                    $"No local build of {id}@{version} was found under {Path.Combine(environment.DataRoot, "mods")}. "
                    + $"Put the released zip in {Path.Combine(environment.DataRoot, "mods", id!)}/{version}/, or pass its path with --baseline.");
        }

        if (environment.GitRefExists(spec))
        {
            return new BaselinePlan(BaselineKind.GitRef, $"git {spec}", spec);
        }

        throw new ArgumentException($"'{spec}' is not a file, a folder, a mod id@version, or a git ref in this repository.");
    }

    /// <summary>The released-mod cache locations tried for <c>id@version</c>, in order.</summary>
    /// <param name="dataRoot">Pharos's data folder.</param>
    /// <param name="id">The mod id.</param>
    /// <param name="version">The version.</param>
    /// <returns>The first path that exists, or null.</returns>
    public static string? FindCachedMod(string dataRoot, string id, string version)
    {
        string root = Path.Combine(dataRoot, "mods");
        string versioned = Path.Combine(root, id, version);
        if (Directory.Exists(versioned))
        {
            string? zip = Directory.EnumerateFiles(versioned, "*.zip").OrderBy(file => file, StringComparer.Ordinal).FirstOrDefault();
            return zip ?? versioned;
        }
        foreach (string candidate in new[] { Path.Combine(versioned, id + "-" + version + ".zip"), Path.Combine(root, id + "-" + version + ".zip") })
        {
            if (File.Exists(candidate)) return candidate;
        }
        return null;
    }

    /// <summary>Whether the spec looks like <c>id@version</c> rather than a path.</summary>
    /// <param name="spec">The spec.</param>
    /// <param name="id">The id, when it parses.</param>
    /// <param name="version">The version, when it parses.</param>
    public static bool TryParseModSpec(string spec, out string? id, out string? version)
    {
        id = null;
        version = null;
        int at = spec.IndexOf('@');
        if (at <= 0 || at != spec.LastIndexOf('@') || at == spec.Length - 1) return false;
        if (spec.Contains('/') || spec.Contains('\\') || spec.Contains(':')) return false;
        id = spec[..at];
        version = spec[(at + 1)..];
        return id.All(character => char.IsLetterOrDigit(character) || character is '.' or '-' or '_')
            && version.All(character => char.IsLetterOrDigit(character) || character is '.' or '-' or '_');
    }

    private static bool LooksLikeManifest(string path)
    {
        if (!string.Equals(Path.GetExtension(path), ".json", StringComparison.OrdinalIgnoreCase)) return false;
        try
        {
            using var document = System.Text.Json.JsonDocument.Parse(File.ReadAllText(path));
            return document.RootElement.ValueKind == System.Text.Json.JsonValueKind.Object
                && document.RootElement.TryGetProperty("tests", out System.Text.Json.JsonElement tests)
                && tests.ValueKind == System.Text.Json.JsonValueKind.Array;
        }
        catch
        {
            return false;
        }
    }

    // A mod build is the zip the game loads or the dll it contains; anything else is a mistake worth naming.
    private static bool LooksLikeModBuild(string path) =>
        Path.GetExtension(path).ToLowerInvariant() is ".zip" or ".dll";
}

/// <summary>
/// <c>pharos compare</c>: runs the same tests against a baseline and a candidate, alternating the
/// runs so machine noise hits both sides, and reports each metric's delta with a verdict.
/// </summary>
internal static class CompareCommand
{
    /// <summary>Nothing regressed beyond its budget.</summary>
    public const int ExitOk = 0;

    /// <summary>A metric moved in the worse direction past its budget.</summary>
    public const int ExitOverBudget = 1;

    /// <summary>The arguments, the baseline or a run were wrong.</summary>
    public const int ExitError = 2;

    /// <summary>The help for <c>pharos compare</c>.</summary>
    public const string HelpText = """
        pharos compare - compare the same tests against a baseline

        Usage: pharos compare <test project|assembly> --baseline <spec> [options]

          --baseline <spec>       What to compare against:
                                    a git ref          built in a temporary worktree (origin/main)
                                    a pharos-run.json  a previous run, reused as it is
                                    a folder           one holding pharos-run.json, or a mod build
                                    a mod zip/folder/dll  staged instead of the declared build
                                    <id>@<version>     a released zip from the local mod cache
          --repeat <n>            Runs per side, interleaved (default: 5)
          --filter <expr>         A dotnet test filter
          -p, --parallel <n>      Workers per run (default: 1)
          --shard <n>/<total>     Run one shard of the tests on both sides
          --game <path>           The Vintage Story install (default: $VINTAGE_STORY)
          -c, --configuration <n> Build configuration (default: Release)
          --no-build              Use the existing builds
          --xvfb auto|always|never  Virtual display per worker (default: auto)
          --alpha <n>             Significance level (default: 0.05)
          --min-effect <n%>       Ignore significant changes smaller than this (default: 1%)
          --bootstrap <n>         Bootstrap resamples for the interval (default: 2000)
          --seed <n>              Bootstrap seed, so an interval is reproducible
          --budgets <json>        Thresholds that fail the command, see docs/compare.md
          --md <path>             Write the Markdown table there
          --json <path>           Write the JSON report there
          --results-dir <dir>     Per-run artifacts and manifests (default: pharos-compare)
          -v, --verbose           Print each run's command line

        Both sides run the same selected tests on the same machine, alternating in pairs (baseline,
        candidate, candidate, baseline, ...) so drift and load hit both equally. Exact metrics
        (counts, bytes) are compared directly; sampled ones (times) go through a rank test with a
        bootstrap interval for the median difference. Each metric is reported as improved,
        regressed, changed, unchanged or inconclusive, and metrics or tests that only one side has
        are listed as added or removed.

        Exit codes: 0 nothing over budget, 1 a metric is over budget, 2 bad input or a failed run.

        Examples:
          pharos compare tests/MyMod.Tests --baseline origin/main --repeat 7 --filter "Forge*"
          pharos compare tests/MyMod.Tests --baseline mymod@1.4.2 --budgets pharos.compare.json
          pharos compare tests/MyMod.Tests --baseline pharos-run/baseline/pharos-run.json --md summary.md
        """;

    /// <summary>Runs <c>pharos compare</c> with the arguments that follow <c>compare</c>.</summary>
    /// <param name="args">The arguments.</param>
    /// <param name="stdout">Where the report goes.</param>
    /// <param name="stderr">Where errors go.</param>
    /// <returns>The exit code.</returns>
    public static int Run(IReadOnlyList<string> args, TextWriter stdout, TextWriter stderr)
    {
        CompareOptions? options;
        try
        {
            options = CompareOptions.Parse(args);
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
            return ExitOk;
        }

        try
        {
            return RunAsync(options, stdout, stderr).GetAwaiter().GetResult();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or System.Text.Json.JsonException or FormatException or System.ComponentModel.Win32Exception)
        {
            stderr.WriteLine($"Error: {ex.Message}");
            return ExitError;
        }
    }

    private static async Task<int> RunAsync(CompareOptions options, TextWriter stdout, TextWriter stderr)
    {
        BudgetPolicy budgets;
        try
        {
            budgets = options.BudgetsPath == null ? BudgetPolicy.Empty : BudgetPolicy.Load(options.BudgetsPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException)
        {
            stderr.WriteLine($"Error: {ex.Message}");
            return ExitError;
        }

        string root = Environment.CurrentDirectory;
        BaselineEnvironment environment = new(root, DataRoot(), GitRefExists);
        BaselinePlan plan;
        try
        {
            plan = BaselinePlanner.Resolve(options.BaselineSpec, environment);
        }
        catch (ArgumentException ex)
        {
            stderr.WriteLine($"Error: {ex.Message}");
            return ExitError;
        }

        CompareAnalysisOptions analysis = new(options.Alpha, options.MinEffect, options.BootstrapSamples, options.Seed, budgets);
        string results = Path.GetFullPath(options.ResultsDirectory);
        Directory.CreateDirectory(results);

        using Worktree? worktree = plan.Kind == BaselineKind.GitRef ? Worktree.Create(root, plan.Path) : null;
        return plan.Kind switch
        {
            BaselineKind.Run => await CompareAgainstManifestAsync(options, analysis, plan, stdout, stderr).ConfigureAwait(false),
            BaselineKind.GitRef => await CompareInterleavedAsync(options, analysis, plan, worktree!, stdout, stderr).ConfigureAwait(false),
            _ => await CompareAgainstModBuildAsync(options, analysis, plan, stdout, stderr).ConfigureAwait(false),
        };
    }

    // A previous run: only the candidate runs, so nothing is interleaved.
    private static async Task<int> CompareAgainstManifestAsync(CompareOptions options, CompareAnalysisOptions analysis,
        BaselinePlan plan, TextWriter stdout, TextWriter stderr)
    {
        stdout.WriteLine($"Baseline: {plan.Description} (reused; the candidate runs {options.Repeat} time(s)).");
        List<ScenarioRunManifest> candidate = [];
        HashSet<string> built = new(StringComparer.Ordinal);
        for (int index = 1; index <= options.Repeat; index++)
        {
            int exit = await RunSideAsync(options, "candidate", index, options.Target, Environment.CurrentDirectory, new Dictionary<string, string>(), built, stdout, stderr).ConfigureAwait(false);
            if (exit == RunCommand.ExitError) return ExitError;
            candidate.Add(LoadManifest(options, "candidate", index));
        }

        return Report(options, analysis, plan, [ScenarioRunManifestWriter.Load(plan.Path)], candidate, stdout);
    }

    /// <summary>
    /// The order a comparison runs in: <paramref name="repeat"/> runs per side, alternating in pairs,
    /// so the baseline and the candidate see the same drift.
    /// </summary>
    /// <param name="repeat">How many runs each side gets.</param>
    /// <returns>One entry per run, in the order they run: whether it is a baseline run, and the side's 1-based run number.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="repeat"/> is below 1.</exception>
    internal static IReadOnlyList<(bool Baseline, int Index)> Schedule(int repeat)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(repeat, 1);
        List<(bool, int)> runs = new(repeat * 2);
        for (int run = 0; run < repeat * 2; run++)
        {
            // baseline, candidate, candidate, baseline, and on: two of each side per cycle.
            runs.Add(((run % 4) is 0 or 3, run / 2 + 1));
        }
        return runs;
    }

    // A mod build: the candidate test runs with the baseline mod staged instead of the declared one.
    private static async Task<int> CompareAgainstModBuildAsync(CompareOptions options, CompareAnalysisOptions analysis,
        BaselinePlan plan, TextWriter stdout, TextWriter stderr)
    {
        string overrideDirectory = Path.Combine(Path.GetFullPath(options.ResultsDirectory), "baseline-mod");
        if (Directory.Exists(overrideDirectory)) Directory.Delete(overrideDirectory, recursive: true);
        StageOverride(plan.Path, overrideDirectory);
        stdout.WriteLine($"Baseline: {plan.Description} (staged for the baseline runs).");
        Dictionary<string, string> environment = new() { [ScenarioAttributes.OverrideVariable] = overrideDirectory };

        List<ScenarioRunManifest> baseline = [], candidate = [];
        HashSet<string> built = new(StringComparer.Ordinal);
        foreach ((bool isBaseline, int index) in Schedule(options.Repeat))
        {
            string side = isBaseline ? "baseline" : "candidate";
            int exit = await RunSideAsync(options, side, index, options.Target, Environment.CurrentDirectory,
                isBaseline ? environment : new Dictionary<string, string>(), built, stdout, stderr).ConfigureAwait(false);
            if (exit == RunCommand.ExitError) return ExitError;
            (isBaseline ? baseline : candidate).Add(LoadManifest(options, side, index));
        }

        return Report(options, analysis, plan, baseline, candidate, stdout);
    }

    // A git ref: both the baseline and the candidate test build, alternating in pairs.
    private static async Task<int> CompareInterleavedAsync(CompareOptions options, CompareAnalysisOptions analysis,
        BaselinePlan plan, Worktree worktree, TextWriter stdout, TextWriter stderr)
    {
        string target = Path.GetFullPath(options.Target);
        string relative = Path.GetRelativePath(Environment.CurrentDirectory, target);
        if (OutsideRepository(relative))
        {
            stderr.WriteLine($"Error: '{options.Target}' is outside the repository, so a {plan.Description} baseline cannot build it in the "
                + "temporary worktree. Run pharos compare from the repository root, or pass a run manifest or a mod build as the baseline.");
            return ExitError;
        }

        string baselineTarget = Path.Combine(worktree.Root, relative);
        stdout.WriteLine($"Baseline: {plan.Description} (worktree {worktree.Root}).");

        List<ScenarioRunManifest> baseline = [], candidate = [];
        HashSet<string> built = new(StringComparer.Ordinal);
        foreach ((bool isBaseline, int index) in Schedule(options.Repeat))
        {
            string side = isBaseline ? "baseline" : "candidate";
            int exit = await RunSideAsync(options, side, index,
                isBaseline ? baselineTarget : target,
                isBaseline ? worktree.Root : Environment.CurrentDirectory,
                new Dictionary<string, string>(), built, stdout, stderr).ConfigureAwait(false);
            if (exit == RunCommand.ExitError) return ExitError;
            (isBaseline ? baseline : candidate).Add(LoadManifest(options, side, index));
        }

        return Report(options, analysis, plan, baseline, candidate, stdout);
    }

    // Whether a path relative to the repository root points outside it.
    private static bool OutsideRepository(string relative) =>
        Path.IsPathRooted(relative)
        || relative == ".."
        || relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)
        || (Path.AltDirectorySeparatorChar != Path.DirectorySeparatorChar
            && relative.StartsWith(".." + Path.AltDirectorySeparatorChar, StringComparison.Ordinal));

    // One side's run through the same machinery as `pharos run`. A side is built on its first run and
    // reused after that, so alternating runs do not rebuild the same project for nothing.
    private static async Task<int> RunSideAsync(CompareOptions options, string side, int index, string target,
        string workingDirectory, IReadOnlyDictionary<string, string> environment, HashSet<string> built,
        TextWriter stdout, TextWriter stderr)
    {
        string results = Path.Combine(Path.GetFullPath(options.ResultsDirectory), $"{side}-{index}");
        if (Directory.Exists(results)) Directory.Delete(results, recursive: true);
        Directory.CreateDirectory(results);
        RunOptions run = new()
        {
            Target = target,
            Filter = options.Filter,
            Parallel = options.Parallel,
            Shard = options.Shard,
            GamePath = options.GamePath,
            Configuration = options.Configuration,
            NoBuild = options.NoBuild || !built.Add(Path.GetFullPath(target)),
            Xvfb = options.Xvfb,
            Verbose = options.Verbose,
            ResultsDirectory = results,
            ManifestPath = Path.Combine(results, "pharos-run.json"),
            WorkerEnvironment = environment,
            WorkingDirectory = workingDirectory,
        };

        StringWriter captured = new();
        Stopwatch watch = Stopwatch.StartNew();
        int exit = await RunCommand.RunAsync(run, captured, stderr, CancellationToken.None).ConfigureAwait(false);
        watch.Stop();
        if (exit == RunCommand.ExitError)
        {
            stderr.WriteLine($"The {side} run {index} did not start:{Environment.NewLine}{captured}");
            return exit;
        }

        ScenarioRunManifest manifest = ScenarioRunManifestWriter.Load(run.ManifestPath!);
        int passed = manifest.Tests.Count(test => test.FinalOutcome is "Passed");
        int failed = manifest.Tests.Count(test => test.FinalOutcome is not ("Passed" or "Skipped" or "NotExecuted"));
        stdout.WriteLine($"  {results}: {passed} passed, {failed} failed in {watch.Elapsed.TotalSeconds.ToString("0.0", CultureInfo.InvariantCulture)}s");
        return exit;
    }

    private static ScenarioRunManifest LoadManifest(CompareOptions options, string side, int index)
    {
        string path = Path.Combine(Path.GetFullPath(options.ResultsDirectory), $"{side}-{index}", "pharos-run.json");
        return ScenarioRunManifestWriter.Load(path);
    }

    private static int Report(CompareOptions options, CompareAnalysisOptions analysis, BaselinePlan plan,
        IReadOnlyList<ScenarioRunManifest> baseline, IReadOnlyList<ScenarioRunManifest> candidate, TextWriter stdout)
    {
        ComparisonResult result = ComparisonModel.Build(baseline, candidate, plan.Description, "candidate (working tree)", analysis);
        int exitCode = result.OverBudget.Count > 0 ? ExitOverBudget : ExitOk;
        stdout.WriteLine();
        ComparisonReport.WriteConsole(result, analysis, stdout);
        if (plan.Kind == BaselineKind.ModBuild && result.Clean)
        {
            stdout.WriteLine();
            stdout.WriteLine($"Note: nothing moved at all. A baseline mod build is matched to a declared mod path by file name, "
                + $"so check that '{Path.GetFileName(plan.Path)}' is the name the selected tests stage.");
        }
        if (options.MarkdownPath is { } markdown)
        {
            File.WriteAllText(markdown, ComparisonReport.Markdown(result));
            stdout.WriteLine($"Markdown: {Path.GetFullPath(markdown)}");
        }
        if (options.JsonPath is { } json)
        {
            File.WriteAllText(json, ComparisonReport.Json(result, analysis, exitCode));
            stdout.WriteLine($"JSON: {Path.GetFullPath(json)}");
        }
        return exitCode;
    }

    // A mod build is staged under its own file or folder name, which is what PHAROS_MOD_OVERRIDE matches.
    private static void StageOverride(string artifact, string directory)
    {
        Directory.CreateDirectory(directory);
        if (Directory.Exists(artifact))
        {
            string target = Path.Combine(directory, new DirectoryInfo(artifact).Name);
            CopyDirectory(artifact, target);
        }
        else
        {
            File.Copy(artifact, Path.Combine(directory, Path.GetFileName(artifact)), overwrite: true);
        }
    }

    private static void CopyDirectory(string source, string target)
    {
        foreach (string file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            string destination = Path.Combine(target, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(file, destination, overwrite: true);
        }
    }

    private static string DataRoot() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Pharos");

    private static bool GitRefExists(string reference)
    {
        try
        {
            using Process process = Process.Start(new ProcessStartInfo("git")
            {
                ArgumentList = { "rev-parse", "--verify", "--quiet", reference + "^{commit}" },
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            })!;
            process.StandardOutput.ReadToEnd();
            process.StandardError.ReadToEnd();
            process.WaitForExit();
            return process.ExitCode == 0;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return false;
        }
    }

    /// <summary>A temporary git worktree holding the baseline checkout.</summary>
    private sealed class Worktree : IDisposable
    {
        private Worktree(string repository, string root)
        {
            Repository = repository;
            Root = root;
        }

        /// <summary>The repository the worktree belongs to.</summary>
        public string Repository { get; }

        /// <summary>The worktree's directory.</summary>
        public string Root { get; }

        /// <summary>Checks a ref out into a new temporary worktree.</summary>
        /// <param name="repository">The repository to add the worktree to.</param>
        /// <param name="reference">The git ref to check out.</param>
        /// <exception cref="InvalidOperationException">git refused to add the worktree.</exception>
        public static Worktree Create(string repository, string reference)
        {
            string root = Path.Combine(Path.GetTempPath(), "pharos-compare-" + Guid.NewGuid().ToString("N")[..8]);
            Git(repository, "worktree", "add", "--detach", root, reference);
            return new Worktree(repository, root);
        }

        /// <summary>Removes the worktree, discarding anything left in it.</summary>
        public void Dispose()
        {
            try
            {
                Git(Repository, "worktree", "remove", "--force", Root);
            }
            catch (InvalidOperationException)
            {
                // If git refuses, the directory stays; deleting it by hand would lose nothing but is not worth a failure.
            }
        }

        private static void Git(string repository, params string[] arguments)
        {
            ProcessStartInfo start = new("git")
            {
                WorkingDirectory = repository,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            foreach (string argument in arguments) start.ArgumentList.Add(argument);
            using Process process = Process.Start(start)!;
            process.StandardOutput.ReadToEnd();
            string error = process.StandardError.ReadToEnd();
            process.WaitForExit();
            if (process.ExitCode != 0) throw new InvalidOperationException($"git {string.Join(' ', arguments)} failed: {error.Trim()}");
        }
    }
}
