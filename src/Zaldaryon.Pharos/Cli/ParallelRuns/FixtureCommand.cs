using Zaldaryon.Pharos.XUnit.Execution;

namespace Zaldaryon.Pharos.Cli.ParallelRuns;

/// <summary>
/// <c>pharos fixture</c>: runs one builder scenario and saves the world it leaves as a save file,
/// so test worlds are made from code and can be made again.
/// </summary>
public static class FixtureCommand
{
    /// <summary>The help for <c>pharos fixture</c>.</summary>
    public const string HelpText = """
        pharos fixture - run a builder scenario and save the world it leaves

        Usage: pharos fixture <test project | folder | .dll> --scenario <name> --out <world.vcdbs> [options]

          --scenario <name>       The test that builds the world: Namespace.Class.Method, or any
                                  dotted tail of it (Class.Method) that names one test
          --out <path>            Where to write the save; replaced only when the run succeeds
          -c, --configuration <c> Build configuration of a project (default: Release)
          --no-build              Use the project's last build
          --game <path>           Vintage Story install (else VINTAGE_STORY)
          --xvfb auto|always|never  Run the test under xvfb-run (default: auto, on Linux without a display)
          --test-timeout <min>    Stop the test after this long (default: 30)
          --results-dir <dir>     Where the run's results and logs go (default: pharos-fixture)
          -v, --verbose           Print the test's output

        The test must be a [ServerScenario] or a [ClientServerScenario] test. Once it passes, the
        world of its server is saved, before the class rolls it back. Boot tests from the save with
        [ServerWorld(SaveFile = "...")].

        Exit codes: 0 the save was written, 1 the test failed or its world could not be saved,
        2 the scenario named no test or several, or the test saved no world.

        Example:
          pharos fixture tests/MyMod.Tests --scenario VillageBuilder.Build --out tests/MyMod.Tests/Fixtures/village.vcdbs
        """;

    /// <summary>Runs <c>pharos fixture</c> with the arguments that follow <c>fixture</c>.</summary>
    public static int Run(IReadOnlyList<string> args, TextWriter stdout, TextWriter stderr)
    {
        FixtureOptions? options;
        try
        {
            options = FixtureOptions.Parse(args);
        }
        catch (ArgumentException ex)
        {
            stderr.WriteLine($"Error: {ex.Message}");
            stderr.WriteLine();
            stderr.WriteLine(HelpText);
            return RunCommand.ExitError;
        }

        if (options == null)
        {
            stdout.WriteLine(HelpText);
            return RunCommand.ExitPassed;
        }

        using CancellationTokenSource cancel = new();
        ConsoleCancelEventHandler onCancel = (_, e) =>
        {
            e.Cancel = true;
            cancel.Cancel();
        };
        Console.CancelKeyPress += onCancel;
        try
        {
            return RunAsync(options, stdout, stderr, cancel.Token).GetAwaiter().GetResult();
        }
        finally
        {
            Console.CancelKeyPress -= onCancel;
        }
    }

    internal static async Task<int> RunAsync(FixtureOptions options, TextWriter stdout, TextWriter stderr, CancellationToken ct)
    {
        RunOptions run = options.Run;
        string results = Path.GetFullPath(run.ResultsDirectory);
        string assembly;
        string test;
        try
        {
            assembly = await RunCommand.ResolveAssemblyAsync(run, Worker.DotnetHost(), stderr, ct).ConfigureAwait(false);
            IReadOnlyList<string> listed = await RunCommand.ListAsync(Worker.DotnetHost(), assembly, $"FullyQualifiedName~{Escape(options.Scenario.Split('.', '+')[^1])}", results, ct).ConfigureAwait(false);
            test = Pick(options.Scenario, listed);
        }
        catch (RunCommand.RunSetupException ex)
        {
            stderr.WriteLine($"Error: {ex.Message}");
            return RunCommand.ExitError;
        }

        string output = Path.GetFullPath(options.Out);
        string temp = Path.Combine(results, "fixture-" + Guid.NewGuid().ToString("N")[..8] + ".vcdbs");
        Directory.CreateDirectory(results);

        RunOptions single = run with
        {
            Target = assembly,
            NoBuild = true,
            Filter = $"FullyQualifiedName={Escape(test)}",
            Parallel = 1,
            WorkerEnvironment = new Dictionary<string, string>(run.WorkerEnvironment)
            {
                [FixtureExport.OutVariable] = temp,
                [FixtureExport.TestVariable] = test,
            },
        };

        try
        {
            int exit = await RunCommand.RunAsync(single, stdout, stderr, ct).ConfigureAwait(false);
            if (exit != RunCommand.ExitPassed)
            {
                stderr.WriteLine($"Error: {test} did not pass; {output} was left as it was.");
                return exit;
            }

            if (!File.Exists(temp))
            {
                stderr.WriteLine($"Error: {test} passed but saved no world. Is it a [ServerScenario] or [ClientServerScenario] test?");
                return RunCommand.ExitError;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(output)!);
            File.Move(temp, output, overwrite: true);
            stdout.WriteLine($"Wrote {output} ({new FileInfo(output).Length / 1024} KB) from {test}.");
            return RunCommand.ExitPassed;
        }
        finally
        {
            if (File.Exists(temp)) File.Delete(temp);
        }
    }

    /// <summary>The one test <paramref name="scenario"/> names among the listed ones.</summary>
    internal static string Pick(string scenario, IReadOnlyList<string> listed)
    {
        List<string> matches = listed.Where(name => Names(name, scenario)).ToList();
        List<string> distinct = matches.Distinct(StringComparer.Ordinal).ToList();
        return distinct.Count switch
        {
            0 => throw new RunCommand.RunSetupException($"No test is named {scenario}."),
            > 1 => throw new RunCommand.RunSetupException($"{scenario} names {distinct.Count} tests; give more of the name:\n  {string.Join("\n  ", distinct.Take(20))}"),
            _ when matches.Count > 1 => throw new RunCommand.RunSetupException($"{distinct[0]} is a theory with {matches.Count} rows; a fixture comes from a single test."),
            _ => distinct[0],
        };
    }

    // The whole name, or a dotted tail of it; nested classes may be written with '.' or '+'.
    private static bool Names(string fullyQualifiedName, string scenario)
    {
        string name = fullyQualifiedName.Replace('+', '.');
        string wanted = scenario.Replace('+', '.');
        return name == wanted || name.EndsWith("." + wanted, StringComparison.Ordinal);
    }

    // A value in a test case filter: its operators and parentheses escaped.
    private static string Escape(string value)
    {
        System.Text.StringBuilder escaped = new();
        foreach (char c in value)
        {
            if (c is '\\' or '(' or ')' or '&' or '|' or '=' or '!' or '~') escaped.Append('\\');
            escaped.Append(c);
        }

        return escaped.ToString();
    }
}

/// <summary>The options of <c>pharos fixture</c>.</summary>
/// <param name="Scenario">The builder test, as given.</param>
/// <param name="Out">Where the save goes.</param>
/// <param name="Run">How the test is built and run.</param>
public sealed record FixtureOptions(string Scenario, string Out, RunOptions Run)
{
    /// <summary>Reads the arguments that follow <c>fixture</c>; null asks for the help.</summary>
    /// <exception cref="ArgumentException">The arguments are wrong.</exception>
    public static FixtureOptions? Parse(IReadOnlyList<string> args)
    {
        string? scenario = null;
        string? output = null;
        List<string> rest = [];
        for (int i = 0; i < args.Count; i++)
        {
            string arg = args[i];
            string Value() => i + 1 < args.Count ? args[++i] : throw new ArgumentException($"{arg} needs a value.");
            switch (arg)
            {
                case "-h" or "--help":
                    return null;
                case "--scenario":
                    scenario = Value();
                    break;
                case "--out":
                    output = Value();
                    break;
                case "--filter" or "--parallel" or "-p" or "--group" or "--list" or "--json" or "--trx" or "--worker-timeout":
                    throw new ArgumentException($"pharos fixture runs a single test: {arg} does not apply.");
                default:
                    rest.Add(arg);
                    break;
            }
        }

        if (string.IsNullOrWhiteSpace(scenario)) throw new ArgumentException("Name the builder test with --scenario.");
        if (string.IsNullOrWhiteSpace(output)) throw new ArgumentException("Say where the save goes with --out.");
        RunOptions run = RunOptions.Parse(rest) ?? throw new ArgumentException("Name the test assembly or project.");
        if (!rest.Contains("--results-dir")) run = run with { ResultsDirectory = "pharos-fixture" };

        // The worker must outlive the test's own timeout.
        TimeSpan workerTimeout = run.TestTimeout + TimeSpan.FromMinutes(10);
        if (run.WorkerTimeout < workerTimeout) run = run with { WorkerTimeout = workerTimeout };
        return new FixtureOptions(scenario, output, run);
    }
}
