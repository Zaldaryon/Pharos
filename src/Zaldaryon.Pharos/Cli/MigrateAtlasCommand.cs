using System.Text;
using System.Text.RegularExpressions;

namespace Zaldaryon.Pharos.Cli;

/// <summary>
/// CLI command for migrating Atlas test suites to Pharos.
/// </summary>
public static class MigrateAtlasCommand
{
    /// <summary>
    /// Replacement patterns for .csproj package references.
    /// </summary>
    private static readonly (Regex pattern, string replacement)[] CsprojReplacements =
    [
        (new Regex(@"<PackageReference\s+Include\s*=\s*""Pixnop\.Atlas\.XUnit""[^/]*/?>", RegexOptions.Compiled), 
         @"<PackageReference Include=""Zaldaryon.Pharos.XUnit"" Version=""0.1.0"" />"),
        (new Regex(@"<PackageReference\s+Include\s*=\s*""Pixnop\.Atlas""[^/]*/?>", RegexOptions.Compiled), 
         @"<PackageReference Include=""Zaldaryon.Pharos.XUnit"" Version=""0.1.0"" />"),
    ];

    /// <summary>
    /// Replacement patterns for .cs using statements.
    /// </summary>
    private static readonly (string pattern, string replacement)[] UsingReplacements =
    [
        ("using Atlas.Api;", "using Zaldaryon.Pharos.Server;\nusing Zaldaryon.Pharos.XUnit;"),
        ("using Atlas.XUnit;", "using Zaldaryon.Pharos.XUnit;"),
    ];

    /// <summary>
    /// Replacement patterns for .cs type references.
    /// </summary>
    private static readonly (string pattern, string replacement)[] TypeReplacements =
    [
        ("IWorldSession", "EmbeddedServerHost"),
        ("WorldSession.Create", "EmbeddedServerHost.Boot"),
        ("WorldSession", "EmbeddedServerHost"),
        ("WorldOptions", "ServerWorldOptions"),
        ("ITestPlayer", "IServerTestPlayer"),
        ("[AtlasScenario]", "[ServerScenario]"),
        ("[AtlasScenario(", "[ServerScenario("),
        ("[AtlasTheory]", "[ServerTheory]"),
        ("[AtlasTheory(", "[ServerTheory("),
        ("AtlasScenarioBase", "ServerScenarioBase"),
        ("[AtlasWorld]", "[ServerWorld]"),
        ("[AtlasWorld(", "[ServerWorld("),
        ("[AtlasMods]", "[ServerMods]"),
        ("[AtlasMods(", "[ServerMods("),
    ];

    /// <summary>
    /// Options for the migrate-atlas command.
    /// </summary>
    public sealed record MigrateOptions(
        string Directory,
        bool DryRun,
        bool Verbose,
        bool Backup);

    /// <summary>
    /// Result of a file scan.
    /// </summary>
    public sealed record ScanResult(
        List<FileChange> Changes,
        int CsprojCount,
        int CsFileCount);

    /// <summary>
    /// Represents a single change to a file.
    /// </summary>
    public sealed record FileChange(
        string FilePath,
        List<LineChange> LineChanges);

    /// <summary>
    /// Represents a single line replacement.
    /// </summary>
    public sealed record LineChange(
        int LineNumber,
        string Before,
        string After);

    /// <summary>
    /// Parses migrate-atlas command arguments.
    /// </summary>
    public static MigrateOptions? ParseArgs(string[] args)
    {
        if (args.Length < 2)
            return null;

        string? directory = null;
        bool dryRun = false;
        bool verbose = false;
        bool backup = true;

        for (int i = 1; i < args.Length; i++)
        {
            string arg = args[i];

            if (arg is "--dry-run")
            {
                dryRun = true;
                continue;
            }

            if (arg is "--verbose" or "-v")
            {
                verbose = true;
                continue;
            }

            if (arg is "--no-backup")
            {
                backup = false;
                continue;
            }

            if (arg is "--backup")
            {
                backup = true;
                continue;
            }

            if (arg.StartsWith('-'))
                throw new ArgumentException($"Unknown option: {arg}");

            directory ??= arg;
        }

        if (directory is null)
            return null;

        return new MigrateOptions(directory, dryRun, verbose, backup);
    }

    /// <summary>
    /// Runs the migrate-atlas command.
    /// </summary>
    public static int Run(MigrateOptions options, TextWriter stdout, TextWriter stderr)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(stdout);
        ArgumentNullException.ThrowIfNull(stderr);

        if (!Directory.Exists(options.Directory))
        {
            stderr.WriteLine($"Error: Directory not found: {options.Directory}");
            return 2;
        }

        stdout.WriteLine($"Scanning: {options.Directory}");

        var scanResult = ScanDirectory(options.Directory, options.Verbose, stdout);

        stdout.WriteLine($"Found {scanResult.CsprojCount} .csproj files");
        stdout.WriteLine($"Found {scanResult.CsFileCount} .cs files");
        stdout.WriteLine();

        if (scanResult.Changes.Count == 0)
        {
            stdout.WriteLine("No changes needed.");
            return 0;
        }

        if (options.DryRun)
        {
            stdout.WriteLine("Changes (dry-run):");
        }
        else
        {
            stdout.WriteLine("Applying changes:");
        }

        int totalReplacements = 0;

        foreach (var fileChange in scanResult.Changes)
        {
            stdout.WriteLine($"  {Path.GetFileName(fileChange.FilePath)}:");

            foreach (var lineChange in fileChange.LineChanges)
            {
                if (options.Verbose)
                {
                    stdout.WriteLine($"    - Line {lineChange.LineNumber}: {Truncate(lineChange.Before, 40)} → {Truncate(lineChange.After, 40)}");
                }
                totalReplacements++;
            }

            if (!options.DryRun)
            {
                ApplyChanges(fileChange, options.Backup);
            }

            stdout.WriteLine();
        }

        if (options.DryRun)
        {
            stdout.WriteLine($"{scanResult.Changes.Count} files would be modified ({totalReplacements} replacements)");
            stdout.WriteLine("Run without --dry-run to apply changes.");
        }
        else
        {
            stdout.WriteLine($"{scanResult.Changes.Count} files modified ({totalReplacements} replacements)");
        }

        return 0;
    }

    /// <summary>
    /// Scans a directory for files that need migration.
    /// </summary>
    public static ScanResult ScanDirectory(string directory, bool verbose, TextWriter? stdout = null)
    {
        var changes = new List<FileChange>();
        int csprojCount = 0;
        int csFileCount = 0;

        // Scan .csproj files
        foreach (var csprojPath in Directory.EnumerateFiles(directory, "*.csproj", SearchOption.AllDirectories))
        {
            csprojCount++;
            var fileChange = ScanCsprojFile(csprojPath);
            if (fileChange is not null && fileChange.LineChanges.Count > 0)
            {
                changes.Add(fileChange);
            }
        }

        // Scan .cs files
        foreach (var csPath in Directory.EnumerateFiles(directory, "*.cs", SearchOption.AllDirectories))
        {
            // Skip generated files
            if (csPath.Contains("/obj/") || csPath.Contains("\\obj\\") ||
                csPath.Contains("/bin/") || csPath.Contains("\\bin\\"))
            {
                continue;
            }

            csFileCount++;
            var fileChange = ScanCsFile(csPath);
            if (fileChange is not null && fileChange.LineChanges.Count > 0)
            {
                changes.Add(fileChange);
            }
        }

        return new ScanResult(changes, csprojCount, csFileCount);
    }

    /// <summary>
    /// Scans a .csproj file for Atlas package references.
    /// </summary>
    public static FileChange? ScanCsprojFile(string filePath)
    {
        var content = File.ReadAllText(filePath);
        var lineChanges = new List<LineChange>();
        var lines = File.ReadAllLines(filePath);

        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            var lineNumber = i + 1;

            foreach (var (pattern, replacement) in CsprojReplacements)
            {
                if (pattern.IsMatch(line))
                {
                    var newLine = pattern.Replace(line, replacement);
                    if (newLine != line)
                    {
                        lineChanges.Add(new LineChange(lineNumber, line.Trim(), newLine.Trim()));
                    }
                }
            }
        }

        return lineChanges.Count > 0 ? new FileChange(filePath, lineChanges) : null;
    }

    /// <summary>
    /// Scans a .cs file for Atlas using statements and type references.
    /// </summary>
    public static FileChange? ScanCsFile(string filePath)
    {
        var lines = File.ReadAllLines(filePath);
        var lineChanges = new List<LineChange>();

        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            var lineNumber = i + 1;
            var originalLine = line;
            var modified = false;

            // Check using replacements first
            foreach (var (pattern, replacement) in UsingReplacements)
            {
                if (line.Contains(pattern))
                {
                    line = line.Replace(pattern, replacement);
                    modified = true;
                }
            }

            // Check type replacements
            foreach (var (pattern, replacement) in TypeReplacements)
            {
                if (line.Contains(pattern))
                {
                    line = line.Replace(pattern, replacement);
                    modified = true;
                }
            }

            if (modified)
            {
                lineChanges.Add(new LineChange(lineNumber, originalLine.Trim(), line.Trim()));
            }
        }

        return lineChanges.Count > 0 ? new FileChange(filePath, lineChanges) : null;
    }

    /// <summary>
    /// Applies changes to a file.
    /// </summary>
    public static void ApplyChanges(FileChange fileChange, bool backup)
    {
        var content = File.ReadAllText(fileChange.FilePath);
        var originalContent = content;

        if (fileChange.FilePath.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var (pattern, replacement) in CsprojReplacements)
            {
                content = pattern.Replace(content, replacement);
            }
        }
        else if (fileChange.FilePath.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
        {
            foreach (var (pattern, replacement) in UsingReplacements)
            {
                content = content.Replace(pattern, replacement);
            }

            foreach (var (pattern, replacement) in TypeReplacements)
            {
                content = content.Replace(pattern, replacement);
            }
        }

        if (content != originalContent)
        {
            if (backup)
            {
                File.Copy(fileChange.FilePath, fileChange.FilePath + ".bak", overwrite: true);
            }

            File.WriteAllText(fileChange.FilePath, content);
        }
    }

    private static string Truncate(string value, int maxLength)
    {
        if (value.Length <= maxLength)
            return value;
        return value[..(maxLength - 3)] + "...";
    }

    /// <summary>
    /// Gets the help text for migrate-atlas command.
    /// </summary>
    public static string GetHelpText() => """
        pharos migrate-atlas - Migrate Atlas test suites to Pharos

        Usage: pharos migrate-atlas <directory> [options]

        Arguments:
          <directory>    Directory containing .csproj files to migrate

        Options:
          --dry-run      Preview changes without modifying files
          --verbose      Show detailed progress
          --backup       Create .bak files before modifying (default)
          --no-backup    Don't create backup files

        What it changes:
          - Package references: Pixnop.Atlas → Zaldaryon.Pharos.XUnit
          - Using statements: Atlas.Api → Zaldaryon.Pharos.Server
          - Type references: IWorldSession → EmbeddedServerHost, etc.
          - Attributes: [AtlasScenario] → [ServerScenario], etc.

        Examples:
          pharos migrate-atlas ./MyTestProject --dry-run
          pharos migrate-atlas ./MyTestProject --verbose
          pharos migrate-atlas ./MyTestProject --no-backup
        """;
}
