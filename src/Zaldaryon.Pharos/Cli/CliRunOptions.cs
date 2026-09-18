namespace Zaldaryon.Pharos.Cli;

/// <summary>
/// Configuration options for the Pharos CLI runner.
/// Parsed from command-line arguments.
/// </summary>
/// <param name="AssemblyPath">Path to the test assembly to execute. Null means discover in current directory.</param>
/// <param name="Filter">Optional filter string to match test names (case-insensitive contains).</param>
/// <param name="OutputDir">Optional directory path for test output files.</param>
/// <param name="Verbose">Enable verbose output with detailed test information.</param>
/// <param name="MaxParallel">Maximum parallel test execution (1 = sequential).</param>
public sealed record CliRunOptions(
    string? AssemblyPath,
    string? Filter,
    string? OutputDir,
    bool Verbose,
    int MaxParallel = 1)
{
    /// <summary>
    /// Default options for running tests in the current directory.
    /// </summary>
    public static CliRunOptions Default => new(null, null, null, false, 1);

    /// <summary>
    /// Parses command-line arguments into CLI options.
    /// </summary>
    /// <param name="args">Command-line arguments to parse.</param>
    /// <returns>Parsed options or null if help was requested.</returns>
    /// <exception cref="ArgumentException">Thrown when an invalid argument is encountered.</exception>
    public static CliRunOptions? Parse(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);

        string? assemblyPath = null;
        string? filter = null;
        string? outputDir = null;
        bool verbose = false;
        int maxParallel = 1;

        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i];

            if (arg is "--help" or "-h" or "-?")
            {
                return null;
            }

            if (arg is "--verbose" or "-v")
            {
                verbose = true;
                continue;
            }

            if (arg is "--assembly" or "-a")
            {
                if (i + 1 >= args.Length)
                    throw new ArgumentException("--assembly requires a path argument");
                assemblyPath = args[++i];
                continue;
            }

            if (arg is "--filter" or "-f")
            {
                if (i + 1 >= args.Length)
                    throw new ArgumentException("--filter requires a pattern argument");
                filter = args[++i];
                continue;
            }

            if (arg is "--output-dir" or "-o")
            {
                if (i + 1 >= args.Length)
                    throw new ArgumentException("--output-dir requires a path argument");
                outputDir = args[++i];
                continue;
            }

            if (arg is "--max-parallel" or "-p")
            {
                if (i + 1 >= args.Length)
                    throw new ArgumentException("--max-parallel requires a number argument");
                if (!int.TryParse(args[++i], out maxParallel) || maxParallel < 1)
                    throw new ArgumentException("--max-parallel must be a positive integer");
                continue;
            }

            // Unknown argument starting with -- is an error
            if (arg.StartsWith('-'))
                throw new ArgumentException($"Unknown argument: {arg}");

            // Positional argument is treated as assembly path
            assemblyPath ??= arg;
        }

        return new CliRunOptions(assemblyPath, filter, outputDir, verbose, maxParallel);
    }

    /// <summary>
    /// Gets the help text for the CLI runner.
    /// </summary>
    public static string GetHelpText() => """
        Pharos CLI Runner - Headless test execution for Pharos scenarios

        Usage: pharos [options] [assembly-path]

        Options:
          -a, --assembly <path>      Path to the test assembly
          -f, --filter <pattern>     Filter tests by name (case-insensitive contains)
          -o, --output-dir <path>    Directory for test output files
          -v, --verbose              Enable verbose output
          -p, --max-parallel <n>     Maximum parallel test execution (default: 1)
          -h, --help                 Show this help message

        Exit codes:
          0 - All tests passed
          1 - One or more tests failed
          2 - Error occurred (invalid arguments, assembly not found, etc.)

        Examples:
          pharos MyTests.dll
          pharos -a MyTests.dll -f "Inventory" -v
          pharos --assembly MyTests.dll --output-dir ./results
        """;
}
