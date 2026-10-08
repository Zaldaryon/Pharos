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

    /// <summary>Variables set in every worker's environment, for tests of the runner.</summary>
    internal IReadOnlyDictionary<string, string> WorkerEnvironment { get; init; } = new Dictionary<string, string>();

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
                case "--json":
                    options = options with { Json = true };
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
        return options with { Target = target };
    }

    private static int Positive(string name, string value) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int number) && number > 0
            ? number
            : throw new ArgumentException($"{name} takes a whole number above 0, not '{value}'.");
}
