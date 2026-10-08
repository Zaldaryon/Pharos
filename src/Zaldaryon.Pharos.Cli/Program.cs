using System.Runtime.CompilerServices;

namespace Zaldaryon.Pharos.Cli;

/// <summary>The <c>pharos</c> command. See <c>docs/smoke-test.md</c> and <c>pharos help</c>.</summary>
internal static class Program
{
    private static int Main(string[] args)
    {
        if (Launcher.IsStaged)
        {
            return RunHere(args);
        }

        // Help and Atlas migration need no game; the test runner, benchmarks and smoke tests do.
        bool needsGame = args.Length > 0 && args[0] is not ("help" or "migrate-atlas" or "-h" or "--help")
            && !args.Contains("--help") && !args.Contains("-h");
        if (!needsGame) return RunHere(args);

        string? game = Launcher.GamePath(args);
        if (game == null)
        {
            Console.Error.WriteLine("Error: no Vintage Story install: set VINTAGE_STORY or pass --game.");
            return 2;
        }

        try
        {
            return Launcher.RunStaged(game, args);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            Console.Error.WriteLine($"Error: could not prepare pharos for the install at {game}: {ex.Message}");
            return 2;
        }
    }

    /// <summary>
    /// Runs the command in this process. Kept apart from <see cref="Main"/> so nothing of Pharos,
    /// which references the game, is loaded before the launcher has decided where to run.
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static int RunHere(string[] args)
    {
        if (args.Length > 0 && args[0] == "smoke")
        {
            StartWholeRunWatchdog(args);

            // A crash on one of the game's threads is a failed smoke test, not a broken tool.
            AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            {
                Console.Error.WriteLine($"Smoke test failed: the game crashed: {e.ExceptionObject}");
                Console.Error.Flush();
                Environment.Exit(1);
            };
        }

        int code = PhCliRunner.Run(args);
        Console.Out.Flush();
        Console.Error.Flush();

        // The game's own threads may outlive the run; the exit code is final.
        Environment.Exit(code);
        return code;
    }

    /// <summary>
    /// The scenario watchdog covers the play; this one covers the whole run, boot and teardown
    /// included, so a mod that hangs while loading cannot hang a CI job.
    /// </summary>
    private static void StartWholeRunWatchdog(string[] args)
    {
        int index = Array.IndexOf(args, "--timeout");
        int playSeconds = index >= 0 && index + 1 < args.Length && int.TryParse(args[index + 1], out int seconds) && seconds > 0 ? seconds : 300;
        double scale = double.TryParse(Environment.GetEnvironmentVariable("PHAROS_TIMEOUT_SCALE"), System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out double factor) && factor > 0 ? factor : 1;
        TimeSpan limit = (TimeSpan.FromSeconds(playSeconds) + TimeSpan.FromMinutes(10)) * scale;

        Thread watchdog = new(() =>
        {
            Thread.Sleep(limit);
            Console.Error.WriteLine($"Error: the smoke test did not finish within {limit.TotalMinutes:0} minutes, boot and teardown included; giving up.");
            Console.Error.Flush();
            Environment.Exit(1);
        })
        {
            IsBackground = true,
            Name = "pharos smoke watchdog",
        };
        watchdog.Start();
    }
}
