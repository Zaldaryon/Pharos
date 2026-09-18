using System.Diagnostics;
using System.Reflection;
using Xunit;
using Zaldaryon.Pharos.Reporting;

namespace Zaldaryon.Pharos.Cli;

/// <summary>
/// CLI entry point for running Pharos xUnit tests without an IDE.
/// Discovers and executes [Fact] and [Theory] tests via reflection.
/// Also provides migration tools via subcommands.
/// </summary>
public static class PhCliRunner
{
    /// <summary>
    /// Exit code returned when all tests pass.
    /// </summary>
    public const int ExitCodeSuccess = 0;

    /// <summary>
    /// Exit code returned when one or more tests fail.
    /// </summary>
    public const int ExitCodeTestFailure = 1;

    /// <summary>
    /// Exit code returned when an error occurs (invalid args, assembly not found, etc.).
    /// </summary>
    public const int ExitCodeError = 2;

    /// <summary>
    /// Runs the CLI with the specified arguments.
    /// </summary>
    /// <param name="args">Command-line arguments.</param>
    /// <returns>Exit code (0 = pass, 1 = fail, 2 = error).</returns>
    public static int Run(string[] args)
    {
        return Run(args, Console.Out, Console.Error);
    }

    /// <summary>
    /// Runs the CLI with the specified arguments and output writers.
    /// Allows for testable output redirection.
    /// </summary>
    /// <param name="args">Command-line arguments.</param>
    /// <param name="stdout">Standard output writer.</param>
    /// <param name="stderr">Standard error writer.</param>
    /// <returns>Exit code (0 = pass, 1 = fail, 2 = error).</returns>
    public static int Run(string[] args, TextWriter stdout, TextWriter stderr)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(stdout);
        ArgumentNullException.ThrowIfNull(stderr);

        // Check for subcommands first
        if (args.Length > 0)
        {
            switch (args[0])
            {
                case "migrate-atlas":
                    return RunMigrateAtlas(args, stdout, stderr);

                case "help" when args.Length > 1 && args[1] == "migrate-atlas":
                    stdout.WriteLine(MigrateAtlasCommand.GetHelpText());
                    return ExitCodeSuccess;
            }
        }

        try
        {
            var options = CliRunOptions.Parse(args);
            if (options is null)
            {
                stdout.WriteLine(GetMainHelpText());
                return ExitCodeSuccess;
            }

            var result = Execute(options, stdout, stderr);
            stdout.Write(result.FormatSummary(options.Verbose));

            if (options.OutputDir is not null)
            {
                SaveReport(result, options);
            }

            return result.ExitCode;
        }
        catch (ArgumentException ex)
        {
            stderr.WriteLine($"Error: {ex.Message}");
            stderr.WriteLine();
            stderr.WriteLine(GetMainHelpText());
            return ExitCodeError;
        }
        catch (Exception ex)
        {
            stderr.WriteLine($"Fatal error: {ex.Message}");
            if (args.Contains("--verbose") || args.Contains("-v"))
            {
                stderr.WriteLine(ex.StackTrace);
            }
            return ExitCodeError;
        }
    }

    /// <summary>
    /// Runs the migrate-atlas subcommand.
    /// </summary>
    private static int RunMigrateAtlas(string[] args, TextWriter stdout, TextWriter stderr)
    {
        try
        {
            var options = MigrateAtlasCommand.ParseArgs(args);
            if (options is null)
            {
                stdout.WriteLine(MigrateAtlasCommand.GetHelpText());
                return ExitCodeSuccess;
            }

            return MigrateAtlasCommand.Run(options, stdout, stderr);
        }
        catch (ArgumentException ex)
        {
            stderr.WriteLine($"Error: {ex.Message}");
            stderr.WriteLine();
            stderr.WriteLine(MigrateAtlasCommand.GetHelpText());
            return ExitCodeError;
        }
        catch (Exception ex)
        {
            stderr.WriteLine($"Fatal error: {ex.Message}");
            if (args.Contains("--verbose") || args.Contains("-v"))
            {
                stderr.WriteLine(ex.StackTrace);
            }
            return ExitCodeError;
        }
    }

    /// <summary>
    /// Gets the main CLI help text including available subcommands.
    /// </summary>
    public static string GetMainHelpText() => """
        Pharos CLI - Headless test execution and migration tools

        Usage: pharos [command] [options] [arguments]

        Commands:
          (default)         Run xUnit tests from a test assembly
          migrate-atlas     Migrate Atlas test suites to Pharos
          help <command>    Show help for a specific command

        Test Runner Options:
          -a, --assembly <path>      Path to the test assembly
          -f, --filter <pattern>     Filter tests by name (case-insensitive contains)
          -o, --output-dir <path>    Directory for test output files
          -v, --verbose              Enable verbose output
          -p, --max-parallel <n>     Maximum parallel test execution (default: 1)
          -h, --help                 Show this help message

        Exit codes:
          0 - All tests passed (or successful operation)
          1 - One or more tests failed
          2 - Error occurred (invalid arguments, assembly not found, etc.)

        Examples:
          pharos MyTests.dll
          pharos -a MyTests.dll -f "Inventory" -v
          pharos migrate-atlas ./MyTestProject --dry-run
          pharos help migrate-atlas
        """;

    /// <summary>
    /// Executes tests based on the provided options.
    /// </summary>
    /// <param name="options">CLI options.</param>
    /// <param name="stdout">Standard output writer for progress.</param>
    /// <param name="stderr">Standard error writer for errors.</param>
    /// <returns>Test run result.</returns>
    public static CliRunResult Execute(CliRunOptions options, TextWriter stdout, TextWriter stderr)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(stdout);
        ArgumentNullException.ThrowIfNull(stderr);

        var stopwatch = Stopwatch.StartNew();

        // Load the assembly
        Assembly? assembly;
        try
        {
            assembly = LoadAssembly(options.AssemblyPath);
            if (assembly is null)
            {
                stderr.WriteLine($"Could not find test assembly: {options.AssemblyPath ?? "(current directory)"}");
                return CliRunResult.Error("Assembly not found", stopwatch.ElapsedMilliseconds);
            }
        }
        catch (Exception ex)
        {
            stderr.WriteLine($"Failed to load assembly: {ex.Message}");
            return CliRunResult.Error($"Assembly load failed: {ex.Message}", stopwatch.ElapsedMilliseconds);
        }

        if (options.Verbose)
        {
            stdout.WriteLine($"Loaded assembly: {assembly.GetName().Name}");
        }

        // Discover tests
        var testMethods = DiscoverTests(assembly, options.Filter);

        if (testMethods.Count == 0)
        {
            stdout.WriteLine("No tests found matching the filter criteria.");
            return CliRunResult.Empty(stopwatch.ElapsedMilliseconds);
        }

        if (options.Verbose)
        {
            stdout.WriteLine($"Discovered {testMethods.Count} test(s)");
        }

        // Run tests
        var (passCount, failCount, skippedCount, failures) = RunTests(testMethods, options, stdout, stderr);

        stopwatch.Stop();
        return new CliRunResult(passCount, failCount, skippedCount, stopwatch.ElapsedMilliseconds, failures);
    }

    /// <summary>
    /// Loads an assembly from the specified path, or searches the current directory.
    /// </summary>
    internal static Assembly? LoadAssembly(string? assemblyPath)
    {
        if (assemblyPath is not null)
        {
            if (!File.Exists(assemblyPath))
                return null;
            return Assembly.LoadFrom(Path.GetFullPath(assemblyPath));
        }

        // Search current directory for test assemblies
        var currentDir = Directory.GetCurrentDirectory();
        var candidates = Directory.GetFiles(currentDir, "*.Tests.dll")
            .Concat(Directory.GetFiles(currentDir, "*.Test.dll"))
            .ToArray();

        if (candidates.Length == 0)
            return null;

        // Load the first candidate
        return Assembly.LoadFrom(candidates[0]);
    }

    /// <summary>
    /// Discovers test methods with [Fact] or [Theory] attributes.
    /// </summary>
    /// <param name="assembly">Assembly to search.</param>
    /// <param name="filter">Optional name filter (case-insensitive contains).</param>
    /// <returns>List of test methods.</returns>
    public static IReadOnlyList<MethodInfo> DiscoverTests(Assembly assembly, string? filter)
    {
        ArgumentNullException.ThrowIfNull(assembly);

        var tests = new List<MethodInfo>();

        foreach (var type in assembly.GetTypes())
        {
            if (type.IsAbstract)
                continue;

            foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance))
            {
                // Check for [Fact] or [Theory] attributes
                var hasFactAttribute = method.GetCustomAttributes()
                    .Any(a => IsFactOrTheoryAttribute(a.GetType()));

                if (!hasFactAttribute)
                    continue;

                var fullName = $"{type.FullName}.{method.Name}";

                // Apply filter if specified
                if (filter is not null &&
                    !fullName.Contains(filter, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                tests.Add(method);
            }
        }

        return tests;
    }

    /// <summary>
    /// Checks if a type is a Fact or Theory attribute (including derived types).
    /// </summary>
    internal static bool IsFactOrTheoryAttribute(Type attributeType)
    {
        // Walk up the inheritance chain
        var current = attributeType;
        while (current is not null)
        {
            var name = current.Name;
            if (name is "FactAttribute" or "TheoryAttribute")
                return true;
            current = current.BaseType;
        }

        return false;
    }

    /// <summary>
    /// Runs the discovered tests and returns results.
    /// </summary>
    private static (int passCount, int failCount, int skippedCount, List<CliTestFailure> failures)
        RunTests(IReadOnlyList<MethodInfo> testMethods, CliRunOptions options, TextWriter stdout, TextWriter stderr)
    {
        int passCount = 0;
        int failCount = 0;
        int skippedCount = 0;
        var failures = new List<CliTestFailure>();

        foreach (var method in testMethods)
        {
            var testName = $"{method.DeclaringType?.FullName}.{method.Name}";

            // Check for Skip property on FactAttribute
            var factAttr = method.GetCustomAttributes()
                .FirstOrDefault(a => IsFactOrTheoryAttribute(a.GetType()));

            if (factAttr is not null)
            {
                var skipProp = factAttr.GetType().GetProperty("Skip");
                var skipReason = skipProp?.GetValue(factAttr) as string;
                if (!string.IsNullOrEmpty(skipReason))
                {
                    skippedCount++;
                    if (options.Verbose)
                    {
                        stdout.WriteLine($"  SKIP: {testName} - {skipReason}");
                    }
                    continue;
                }
            }

            if (options.Verbose)
            {
                stdout.Write($"  Running: {testName}... ");
            }

            try
            {
                // Create instance and invoke
                var instance = Activator.CreateInstance(method.DeclaringType!);
                method.Invoke(instance, null);

                passCount++;
                if (options.Verbose)
                {
                    stdout.WriteLine("PASS");
                }
            }
            catch (TargetInvocationException tie) when (tie.InnerException is not null)
            {
                failCount++;
                var failure = CliTestFailure.FromException(testName, tie.InnerException);
                failures.Add(failure);

                if (options.Verbose)
                {
                    stdout.WriteLine("FAIL");
                }
            }
            catch (Exception ex)
            {
                failCount++;
                var failure = CliTestFailure.FromException(testName, ex);
                failures.Add(failure);

                if (options.Verbose)
                {
                    stdout.WriteLine("FAIL");
                }
            }
        }

        return (passCount, failCount, skippedCount, failures);
    }

    /// <summary>
    /// Saves a test report to the output directory.
    /// </summary>
    private static void SaveReport(CliRunResult result, CliRunOptions options)
    {
        if (options.OutputDir is null)
            return;

        Directory.CreateDirectory(options.OutputDir);

        var builder = new TestReportBuilder("CLI Test Run");
        foreach (var failure in result.Failures)
        {
            builder.AddFailResult(failure.TestName, failure.FailureMessage);
        }

        // Add pass results (we don't track individual names, so use count)
        for (int i = 0; i < result.PassCount; i++)
        {
            builder.AddPassResult($"Test_{i + 1}");
        }

        builder.WithDuration(result.DurationMs);
        var report = builder.Build();

        var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        var htmlPath = Path.Combine(options.OutputDir, $"pharos_report_{timestamp}.html");
        var mdPath = Path.Combine(options.OutputDir, $"pharos_report_{timestamp}.md");

        report.SaveHtml(htmlPath);
        report.SaveMarkdown(mdPath);
    }

    /// <summary>
    /// Computes the exit code for a given result.
    /// </summary>
    /// <param name="passCount">Number of passing tests.</param>
    /// <param name="failCount">Number of failing tests.</param>
    /// <returns>Exit code.</returns>
    public static int ComputeExitCode(int passCount, int failCount) =>
        failCount > 0 ? ExitCodeTestFailure : ExitCodeSuccess;

    /// <summary>
    /// Matches a test name against a filter pattern.
    /// </summary>
    /// <param name="testName">Full test name.</param>
    /// <param name="filter">Filter pattern (case-insensitive contains).</param>
    /// <returns>True if the test matches the filter.</returns>
    public static bool MatchesFilter(string testName, string? filter)
    {
        if (string.IsNullOrEmpty(filter))
            return true;

        return testName.Contains(filter, StringComparison.OrdinalIgnoreCase);
    }
}
