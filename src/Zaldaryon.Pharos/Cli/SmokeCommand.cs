using System.Globalization;
using System.Runtime.InteropServices;
using Zaldaryon.Pharos.Reporting;
using Zaldaryon.Pharos.Server;
using Zaldaryon.Pharos.XUnit;
using Zaldaryon.Pharos.XUnit.Execution;

namespace Zaldaryon.Pharos.Cli;

/// <summary>What <c>pharos smoke</c> runs. See <c>docs/smoke-test.md</c>.</summary>
public sealed record SmokeOptions
{
    /// <summary>The mods to load on the server and the client: folders, .zip or .dll files.</summary>
    public IReadOnlyList<string> Mods { get; init; } = [];

    /// <summary>How many frames to play.</summary>
    public int Frames { get; init; } = 600;

    /// <summary>Server console commands to run once the player has joined.</summary>
    public IReadOnlyList<string> Commands { get; init; } = [];

    /// <summary>Whether boot warnings fail the run.</summary>
    public bool Strict { get; init; }

    /// <summary>Regular expressions of boot diagnostics to allow.</summary>
    public IReadOnlyList<string> Allow { get; init; } = [];

    /// <summary>Fragments of logged errors to allow.</summary>
    public IReadOnlyList<string> AllowErrors { get; init; } = [];

    /// <summary>The watchdog for the play, in seconds; null for the default five minutes.</summary>
    public int? TimeoutSeconds { get; init; }

    /// <summary>Where failure artifacts go; null for <c>PHAROS_ARTIFACTS</c>, else <c>./pharos-artifacts</c>.</summary>
    public string? ArtifactsDirectory { get; init; }

    /// <summary>The game install; null for <c>VINTAGE_STORY</c>.</summary>
    public string? GamePath { get; init; }

    /// <summary>The world seed.</summary>
    public string? Seed { get; init; }

    /// <summary>The world type, <c>superflat</c> by default.</summary>
    public string? WorldType { get; init; }

    /// <summary>The play style, <c>creativebuilding</c> by default.</summary>
    public string? PlayStyle { get; init; }

    /// <summary>Whether the game's own output is shown instead of written to <c>smoke.log</c>.</summary>
    public bool Verbose { get; init; }
}

/// <summary>
/// <c>pharos smoke</c>: boots a server and an engine-mode client with the mods, joins, plays and
/// fails on a crash, a timeout, a failed command, a logged error or, with <c>--strict</c>, a boot
/// warning. See <c>docs/smoke-test.md</c>.
/// </summary>
public static class SmokeCommand
{
    /// <summary>The smoke test passed.</summary>
    public const int ExitPassed = 0;

    /// <summary>The smoke test failed: the mod has a problem.</summary>
    public const int ExitFailed = 1;

    /// <summary>The arguments or the environment are wrong: the smoke test could not run.</summary>
    public const int ExitError = 2;

    /// <summary>The help text.</summary>
    public static string HelpText => """
        Pharos CLI - Smoke Command

        Boots a server and a client with the mods, joins, walks and looks around, and fails on a
        crash, a timeout, a failed command, a logged error or, with --strict, a boot warning.

        Usage: pharos smoke --mod <path> [--mod <path>...] [options]

        Options:
          --mod <path>            A mod to load: a folder, a .zip or a .dll (repeatable, required)
          --frames <n>            Frames to play, the server ticking once per frame (default: 600)
          --command <text>        A server console command to run after the join (repeatable)
          --strict                Fail on any warning logged while booting
          --allow <regex>         A boot warning to allow with --strict (repeatable)
          --allow-error <text>    A fragment of a logged error to allow (repeatable)
          --timeout <seconds>     How long the play may take (default: 300)
          --artifacts <dir>       Where failure artifacts go (default: $PHAROS_ARTIFACTS or ./pharos-artifacts)
          --game <dir>            The Vintage Story install (default: $VINTAGE_STORY)
          --seed <seed>           The world seed
          --world-type <type>     The world type (default: superflat)
          --play-style <style>    The play style (default: creativebuilding)
          -v, --verbose           Show the game's own output instead of writing it to smoke.log
          -h, --help              Show this help

        Exit codes:
          0 - The smoke test passed
          1 - The smoke test failed: see the message and the artifacts
          2 - Bad arguments or environment (no game install, no display, a missing mod)

        Examples:
          xvfb-run -a pharos smoke --mod bin/Release/mymod.zip
          xvfb-run -a pharos smoke --mod mymod.zip --strict --command "/time set day" --frames 1200
        """;

    /// <summary>Parses the arguments after <c>smoke</c>. Returns null for help.</summary>
    /// <exception cref="ArgumentException">An argument is wrong.</exception>
    public static SmokeOptions? Parse(IReadOnlyList<string> args)
    {
        List<string> mods = [], commands = [], allow = [], allowErrors = [];
        SmokeOptions options = new();

        for (int i = 0; i < args.Count; i++)
        {
            string arg = args[i];
            switch (arg)
            {
                case "-h" or "--help":
                    return null;
                case "--mod":
                    mods.Add(Value(args, ref i, arg));
                    break;
                case "--command":
                    commands.Add(Value(args, ref i, arg));
                    break;
                case "--allow":
                    allow.Add(Value(args, ref i, arg));
                    break;
                case "--allow-error":
                    allowErrors.Add(Value(args, ref i, arg));
                    break;
                case "--strict":
                    options = options with { Strict = true };
                    break;
                case "-v" or "--verbose":
                    options = options with { Verbose = true };
                    break;
                case "--frames":
                    options = options with { Frames = PositiveInt(Value(args, ref i, arg), arg) };
                    break;
                case "--timeout":
                    options = options with { TimeoutSeconds = PositiveInt(Value(args, ref i, arg), arg) };
                    break;
                case "--artifacts":
                    options = options with { ArtifactsDirectory = Value(args, ref i, arg) };
                    break;
                case "--game":
                    options = options with { GamePath = Value(args, ref i, arg) };
                    break;
                case "--seed":
                    options = options with { Seed = Value(args, ref i, arg) };
                    break;
                case "--world-type":
                    options = options with { WorldType = Value(args, ref i, arg) };
                    break;
                case "--play-style":
                    options = options with { PlayStyle = Value(args, ref i, arg) };
                    break;
                default:
                    throw new ArgumentException($"Unknown option '{arg}'.");
            }
        }

        if (mods.Count == 0) throw new ArgumentException("Name at least one mod with --mod.");

        return options with { Mods = mods, Commands = commands, Allow = allow, AllowErrors = allowErrors };
    }

    /// <summary>Runs <c>pharos smoke</c> with <paramref name="args"/>, the arguments after <c>smoke</c>.</summary>
    public static int Run(IReadOnlyList<string> args, TextWriter stdout, TextWriter stderr)
    {
        SmokeOptions? options;
        try
        {
            options = Parse(args);
        }
        catch (ArgumentException ex)
        {
            stderr.WriteLine("Error: " + ex.Message);
            stderr.WriteLine();
            stderr.WriteLine(HelpText);
            return ExitError;
        }

        if (options == null)
        {
            stdout.WriteLine(HelpText);
            return ExitPassed;
        }

        return Run(options, stdout, stderr);
    }

    /// <summary>Runs the smoke test described by <paramref name="options"/>.</summary>
    public static int Run(SmokeOptions options, TextWriter stdout, TextWriter stderr)
    {
        if (Preflight(options) is { } problem)
        {
            stderr.WriteLine("Error: " + problem);
            return ExitError;
        }

        string artifacts = Path.GetFullPath(options.ArtifactsDirectory
            ?? (Environment.GetEnvironmentVariable(FailureArtifactWriter.DirectoryVariable) is { Length: > 0 } fromEnvironment
                ? fromEnvironment
                : Path.Combine(Directory.GetCurrentDirectory(), "pharos-artifacts")));
        Directory.CreateDirectory(artifacts);
        string log = Path.Combine(artifacts, "smoke.log");

        stdout.WriteLine($"Smoke testing {string.Join(", ", options.Mods.Select(Path.GetFileName))} for {options.Frames} frames{(options.Strict ? ", strict" : "")}...");
        if (!options.Verbose) stdout.WriteLine($"The game's output goes to {log}");

        ScenarioRunner.Result result;
        TextWriter originalOut = Console.Out, originalError = Console.Error;
        StreamWriter? gameOutput = options.Verbose ? null : new StreamWriter(log, append: false) { AutoFlush = true };
        try
        {
            if (gameOutput != null)
            {
                Console.SetOut(gameOutput);
                Console.SetError(gameOutput);
            }

            result = RunScenario(options, artifacts).GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            Console.SetOut(originalOut);
            Console.SetError(originalError);
            stderr.WriteLine($"Error: the smoke test could not run: {ex.GetType().Name}: {ex.Message}");
            return ExitError;
        }
        finally
        {
            Console.SetOut(originalOut);
            Console.SetError(originalError);
            gameOutput?.Dispose();
        }

        if (result.Succeeded)
        {
            stdout.WriteLine("Smoke test passed.");
            return ExitPassed;
        }

        stdout.WriteLine("Smoke test failed:");
        foreach (string failure in result.Failures)
        {
            stdout.WriteLine(failure);
        }

        return ExitFailed;
    }

    /// <summary>What keeps the smoke test from running at all, or null.</summary>
    internal static string? Preflight(SmokeOptions options)
    {
        string? game = options.GamePath ?? Environment.GetEnvironmentVariable("VINTAGE_STORY");
        if (string.IsNullOrEmpty(game))
        {
            return "no Vintage Story install: set VINTAGE_STORY or pass --game.";
        }

        if (!File.Exists(Path.Combine(game, "VintagestoryAPI.dll")))
        {
            return $"'{game}' is not a Vintage Story install: it has no VintagestoryAPI.dll.";
        }

        foreach (string mod in options.Mods)
        {
            if (!File.Exists(mod) && !Directory.Exists(mod))
            {
                return $"the mod '{mod}' does not exist.";
            }
        }

        if (options.Allow.FirstOrDefault(p => !IsValidPattern(p)) is { } invalid)
        {
            return $"--allow \"{invalid}\" is not a valid regular expression.";
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux)
            && string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DISPLAY"))
            && string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WAYLAND_DISPLAY")))
        {
            return "no display: run it under a virtual display, for example `xvfb-run -a pharos smoke ...`.";
        }

        return null;
    }

    private static async Task<ScenarioRunner.Result> RunScenario(SmokeOptions options, string artifacts)
    {
        // The scenario reads the game install from VINTAGE_STORY, like every Pharos host.
        if (options.GamePath != null) Environment.SetEnvironmentVariable("VINTAGE_STORY", Path.GetFullPath(options.GamePath));

        FailureArtifactWriter.RootOverride = artifacts;
        ScenarioTimeouts.Override = options.TimeoutSeconds is { } seconds
            ? (int)Math.Min(int.MaxValue, seconds * 1000L)
            : null;
        BootCheck.Forget(typeof(CliModSmokeTest));

        try
        {
            return await ScenarioRunner.RunAsync(typeof(CliModSmokeTest), nameof(ModSmokeTest.ModBootsJoinsAndPlays), options).ConfigureAwait(false);
        }
        finally
        {
            BootCheck.Forget(typeof(CliModSmokeTest));
            FailureArtifactWriter.RootOverride = null;
            ScenarioTimeouts.Override = null;
        }
    }

    private static string Value(IReadOnlyList<string> args, ref int i, string option)
    {
        if (i + 1 >= args.Count || args[i + 1].StartsWith("--", StringComparison.Ordinal))
        {
            throw new ArgumentException($"{option} needs a value.");
        }

        return args[++i];
    }

    private static int PositiveInt(string value, string option) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int number) && number > 0
            ? number
            : throw new ArgumentException($"{option} needs a positive number, not '{value}'.");

    private static bool IsValidPattern(string pattern)
    {
        try
        {
            _ = new System.Text.RegularExpressions.Regex(pattern);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    /// <summary>The smoke test the command runs: a <see cref="ModSmokeTest"/> configured by the options.</summary>
    internal sealed class CliModSmokeTest(SmokeOptions options) : ModSmokeTest
    {
        protected override IReadOnlyList<string> ServerModPaths => [.. options.Mods.Select(Path.GetFullPath)];

        protected override IReadOnlyList<string> ClientModPaths => [];

        protected override int SmokeFrames => options.Frames;

        protected override IReadOnlyList<string> SmokeCommands => options.Commands;

        protected override bool StrictBoot => options.Strict;

        protected override IEnumerable<AllowBootDiagnosticAttribute> BootAllowances => [.. options.Allow.Select(p => new AllowBootDiagnosticAttribute(p))];

        protected override IEnumerable<string> AllowedLoggedErrors => options.AllowErrors;

        protected override ServerWorldOptions WorldOptions
        {
            get
            {
                ServerWorldOptions world = new() { WorldName = "Pharos-Smoke" };
                return world with
                {
                    Seed = options.Seed ?? world.Seed,
                    WorldType = options.WorldType ?? world.WorldType,
                    PlayStyle = options.PlayStyle ?? world.PlayStyle,
                };
            }
        }
    }
}
