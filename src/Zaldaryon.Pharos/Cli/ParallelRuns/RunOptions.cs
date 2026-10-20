using System.Globalization;

namespace Zaldaryon.Pharos.Cli.ParallelRuns;

/// <summary>When workers get a virtual display of their own.</summary>
public enum XvfbMode
{
    /// <summary>On Linux, when there is no display and <c>xvfb-run</c> is installed.</summary>
    Auto,

    /// <summary>Always: fails when <c>xvfb-run</c> is missing.</summary>
    Always,

    /// <summary>Never: workers use the display the runner has, if any.</summary>
    Never,
}

/// <summary>How tests are partitioned into shards across CI runners.</summary>
public sealed record ShardOptions(int Index, int Total);

/// <summary>What <c>pharos run</c> runs. See <c>docs/parallel-runs.md</c>.</summary>
public sealed record RunOptions
{
    /// <summary>The test assembly, or a project or its folder to build first.</summary>
    public required string Target { get; init; }

    /// <summary>A <c>dotnet test --filter</c> expression, applied when the tests are listed.</summary>
    public string? Filter { get; init; }

    /// <summary>How many workers run at once.</summary>
    public int Parallel { get; init; } = 1;

    /// <summary>How tests are split between workers.</summary>
    public RunGrouping Grouping { get; init; } = RunGrouping.Class;

    /// <summary>Where the merged TRX goes; null for <c>pharos.trx</c> in <see cref="ResultsDirectory"/>.</summary>
    public string? TrxPath { get; init; }

    /// <summary>Lists the groups and their tests, and runs nothing.</summary>
    public bool ListOnly { get; init; }

    /// <summary>Writes JSON lines to standard output instead of text.</summary>
    public bool Json { get; init; }

    /// <summary>Path to write the run manifest JSON to; overrides the default in ResultsDirectory.</summary>
    public string? ManifestPath { get; init; }

    /// <summary>The shard to run: 1-indexed shard number and total shards.</summary>
    public ShardOptions? Shard { get; init; }

    /// <summary>Path to a TRX or JSON timing baseline file for balancing test groups and shards.</summary>
    public string? TimingsPath { get; init; }

    /// <summary>Where worker logs, TRX files and failure artifacts go.</summary>
    public string ResultsDirectory { get; init; } = "pharos-run";

    /// <summary>How long a worker may run before it is stopped, scaled by <c>PHAROS_TIMEOUT_SCALE</c>.</summary>
    public TimeSpan WorkerTimeout { get; init; } = TimeSpan.FromMinutes(60);

    /// <summary>
    /// How long one test may run before vstest stops its worker's test host, keeping the results
    /// so far; scaled by <c>PHAROS_TIMEOUT_SCALE</c>.
    /// </summary>
    public TimeSpan TestTimeout { get; init; } = TimeSpan.FromMinutes(30);

    /// <summary>The game install workers use; null for <c>VINTAGE_STORY</c>.</summary>
    public string? GamePath { get; init; }

    /// <summary>The configuration a project is built in.</summary>
    public string Configuration { get; init; } = "Release";

    /// <summary>Uses a project's last build instead of building it.</summary>
    public bool NoBuild { get; init; }

    /// <summary>When workers get a virtual display of their own.</summary>
    public XvfbMode Xvfb { get; init; } = XvfbMode.Auto;

    /// <summary>Prints each worker's command.</summary>
    public bool Verbose { get; init; }

    /// <summary>Number of complete independent runs.</summary>
    public int Repeat { get; init; } = 1;

    /// <summary>Repeats until a test or worker fails, bounded by <see cref="Max"/>.</summary>
    public bool UntilFail { get; init; }

    /// <summary>Maximum number of runs for <see cref="UntilFail"/>.</summary>
    public int? Max { get; init; }

    /// <summary>Run only raw failures from a previous pharos run manifest.</summary>
    public string? RerunFailed { get; init; }

    /// <summary>Listing-only filter used to narrow discovery without changing worker selection.</summary>
    internal string? DiscoveryFilter { get; init; }

    /// <summary>Quarantine file to apply; null uses pharos.quarantine.json in the current directory when present.</summary>
    public string? QuarantineFile { get; init; }

    internal QuarantinePolicy? LoadedQuarantine { get; init; }

    /// <summary>Variables set in every worker's environment, for tests of the runner.</summary>
    internal IReadOnlyDictionary<string, string> WorkerEnvironment { get; init; } = new Dictionary<string, string>();

    /// <summary>
    /// The directory each worker runs in; null to inherit the command's. <c>pharos compare</c> sets
    /// it to the side's checkout so the tests' relative mod and data paths resolve there.
    /// </summary>
    internal string? WorkingDirectory { get; init; }

    /// <summary>The options in <paramref name="args"/>, which follow <c>run</c>; null when help was asked for.</summary>
    /// <exception cref="ArgumentException">An argument is unknown, missing its value, or out of range.</exception>
    public static RunOptions? Parse(IReadOnlyList<string> args)
    {
        string? target = null;
        RunOptions options = new() { Target = "" };
        for (int i = 0; i < args.Count; i++)
        {
            string arg = args[i];
            string Value() => i + 1 < args.Count ? args[++i] : throw new ArgumentException($"{arg} needs a value.");

            switch (arg)
            {
                case "-h" or "--help":
                    return null;
                case "--filter":
                    options = options with { Filter = Value() };
                    break;
                case "--parallel" or "-p":
                    options = options with { Parallel = Positive(arg, Value()) };
                    break;
                case "--group":
                    options = options with
                    {
                        Grouping = Value() switch
                        {
                            "class" => RunGrouping.Class,
                            "collection" => RunGrouping.Collection,
                            string other => throw new ArgumentException($"--group takes class or collection, not '{other}'."),
                        },
                    };
                    break;
                case "--trx":
                    options = options with { TrxPath = Value() };
                    break;
                case "--list":
                    options = options with { ListOnly = true };
                    break;
                case "--repeat":
                    options = options with { Repeat = Positive(arg, Value()) };
                    break;
                case "--until-fail":
                    options = options with { UntilFail = true };
                    break;
                case "--max":
                    options = options with { Max = Positive(arg, Value()) };
                    break;
                case "--rerun-failed":
                    options = options with { RerunFailed = Value() };
                    break;
                case "--quarantine":
                    options = options with { QuarantineFile = Value() };
                    break;
                case "--shard":
                    string shard = Value();
                    int slash = shard.IndexOf('/');
                    if (slash <= 0 || slash >= shard.Length - 1
                        || !int.TryParse(shard[..slash], NumberStyles.Integer, CultureInfo.InvariantCulture, out int shardIndex)
                        || !int.TryParse(shard[(slash + 1)..], NumberStyles.Integer, CultureInfo.InvariantCulture, out int shardTotal)
                        || shardIndex < 1 || shardTotal < 1 || shardIndex > shardTotal)
                    {
                        throw new ArgumentException($"--shard takes <index>/<total> with 1 <= index <= total, not '{shard}'.");
                    }
                    options = options with { Shard = new ShardOptions(shardIndex, shardTotal) };
                    break;
                case "--timings":
                    options = options with { TimingsPath = Value() };
                    break;
                case "--manifest":
                    options = options with { ManifestPath = Value() };
                    break;
                case "--json":
                    if (i + 1 < args.Count && !args[i + 1].StartsWith('-') && (target != null || args[i + 1].EndsWith(".json", StringComparison.OrdinalIgnoreCase)))
                    {
                        options = options with { ManifestPath = Value() };
                    }
                    else
                    {
                        options = options with { Json = true };
                    }
                    break;
                case "--results-dir":
                    options = options with { ResultsDirectory = Value() };
                    break;
                case "--worker-timeout":
                    options = options with { WorkerTimeout = TimeSpan.FromMinutes(Positive(arg, Value())) };
                    break;
                case "--test-timeout":
                    options = options with { TestTimeout = TimeSpan.FromMinutes(Positive(arg, Value())) };
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
                case "-v" or "--verbose":
                    options = options with { Verbose = true };
                    break;
                default:
                    if (arg.StartsWith('-')) throw new ArgumentException($"Unknown option: {arg}");
                    if (target != null) throw new ArgumentException($"Only one test assembly or project can be run, not '{target}' and '{arg}'.");
                    target = arg;
                    break;
            }
        }

        if (target == null) throw new ArgumentException("Name the test assembly or project to run.");
        if (options.UntilFail && options.Max == null) throw new ArgumentException("--until-fail needs --max <n>.");
        if (!options.UntilFail && options.Max != null) throw new ArgumentException("--max is only valid with --until-fail.");
        if (options.UntilFail && options.Repeat != 1) throw new ArgumentException("--until-fail cannot be combined with --repeat.");
        if (options.RerunFailed != null && (options.UntilFail || options.Repeat != 1)) throw new ArgumentException("--rerun-failed cannot be combined with --repeat or --until-fail.");
        return options with { Target = target };
    }

    private static int Positive(string name, string value) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int number) && number > 0
            ? number
            : throw new ArgumentException($"{name} takes a whole number above 0, not '{value}'.");
}
