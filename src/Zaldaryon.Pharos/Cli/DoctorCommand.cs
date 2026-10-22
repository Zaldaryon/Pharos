using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Xml.Linq;
using Zaldaryon.Pharos.Core;
using Zaldaryon.Pharos.Platform;

namespace Zaldaryon.Pharos.Cli;

/// <summary>How one doctor check ended.</summary>
public enum DoctorStatus
{
    /// <summary>The check passed.</summary>
    Ok,

    /// <summary>The check found something to fix, but not something that stops a run.</summary>
    Warn,

    /// <summary>The check found something that stops a run.</summary>
    Fail,
}

/// <summary>What one doctor check found.</summary>
/// <param name="Name">What was checked.</param>
/// <param name="Status">How the check ended.</param>
/// <param name="Message">What it found.</param>
/// <param name="Hint">What to do about it, when there is something to do.</param>
public sealed record DoctorCheck(string Name, DoctorStatus Status, string Message, string? Hint = null);

/// <summary>The options of <c>pharos doctor</c>.</summary>
/// <param name="GamePath">The game install to check; null for <c>VINTAGE_STORY</c> or a default.</param>
/// <param name="ProjectPath">The test project to check; null for the current folder.</param>
/// <param name="Json">Whether to write the report as JSON.</param>
/// <param name="Fix">Whether to write the files that are safe to write.</param>
public sealed record DoctorOptions(string? GamePath = null, string? ProjectPath = null, bool Json = false, bool Fix = false);

/// <summary>What <c>pharos doctor</c> found.</summary>
/// <param name="GamePath">The game install it checked, or null.</param>
/// <param name="GameVersion">The version of that install, or null.</param>
/// <param name="Checks">The checks, in the order they ran.</param>
public sealed record DoctorReport(string? GamePath, string? GameVersion, IReadOnlyList<DoctorCheck> Checks)
{
    /// <summary>Whether no check failed.</summary>
    public bool AllGood => Checks.All(check => check.Status != DoctorStatus.Fail);

    /// <summary>0 when no check failed, 1 otherwise.</summary>
    public int ExitCode => AllGood ? 0 : 1;

    /// <summary>Whether a check warned.</summary>
    public bool HasWarnings => Checks.Any(check => check.Status == DoctorStatus.Warn);
}

/// <summary>
/// <c>pharos doctor</c>: checks the game install, the runtime, the display, the natives, the test
/// project and the loopback ports, with a fix hint for each problem.
/// </summary>
public static class DoctorCommand
{
    /// <summary>The help for <c>pharos doctor</c>.</summary>
    public const string HelpText = """
        pharos doctor - check that this machine can run a Pharos suite

        Usage: pharos doctor [options]

        Options:
          --game <path>        The Vintage Story install; defaults to VINTAGE_STORY or a default
          --project <path>     The test project or its folder; defaults to the current folder
          --json               Write the report as JSON
          --fix                Write the safe fixes: pharos-headless.sh and pharos.runsettings
          -h, --help           Show this help

        Exit codes: 0 no check failed, 1 a check failed, 2 doctor could not run.

        Examples:
          pharos doctor
          pharos doctor --json
          pharos doctor --project tests/MyMod.Tests --fix
        """;

    /// <summary>Runs <c>pharos doctor</c> with the arguments that follow <c>doctor</c>.</summary>
    public static int Run(IReadOnlyList<string> args, TextWriter stdout, TextWriter stderr)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(stdout);
        ArgumentNullException.ThrowIfNull(stderr);

        if (args.Contains("-h") || args.Contains("--help"))
        {
            stdout.WriteLine(HelpText);
            return 0;
        }

        try
        {
            DoctorOptions options = Parse(args);
            DoctorReport report = Inspect(options);
            List<string> fixes = options.Fix ? Fix(options, report) : [];
            if (options.Json)
            {
                stdout.WriteLine(JsonSerializer.Serialize(new
                {
                    schemaVersion = 1,
                    allGood = report.AllGood,
                    exitCode = report.ExitCode,
                    gamePath = report.GamePath,
                    gameVersion = report.GameVersion,
                    checks = report.Checks.Select(check => new
                    {
                        name = check.Name,
                        status = check.Status.ToString().ToLowerInvariant(),
                        message = check.Message,
                        hint = check.Hint,
                    }),
                }, new JsonSerializerOptions
                {
                    WriteIndented = true,
                    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
                }));
            }
            else
            {
                Print(report, fixes, stdout);
            }
            return report.ExitCode;
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
        {
            stderr.WriteLine($"pharos doctor: {ex.Message}");
            return 2;
        }
    }

    /// <summary>Runs every check without printing anything.</summary>
    /// <param name="options">What to check.</param>
    public static DoctorReport Inspect(DoctorOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        List<DoctorCheck> checks = [];
        (string? gamePath, string? gameVersion) = Install(options.GamePath, checks);
        checks.Add(Runtime(gameVersion));
        checks.Add(Graphics());
        checks.Add(Natives(gamePath));
        checks.Add(Project(options.ProjectPath, gamePath));
        checks.Add(Ports());
        return new DoctorReport(gamePath, gameVersion, checks);
    }

    private static DoctorOptions Parse(IReadOnlyList<string> args)
    {
        DoctorOptions options = new();
        for (int i = 0; i < args.Count; i++)
        {
            string arg = args[i];
            string Value() => i + 1 < args.Count ? args[++i] : throw new ArgumentException($"{arg} needs a value.");
            switch (arg)
            {
                case "--game":
                    options = options with { GamePath = Value() };
                    break;
                case "--project":
                    options = options with { ProjectPath = Value() };
                    break;
                case "--json":
                    options = options with { Json = true };
                    break;
                case "--fix":
                    options = options with { Fix = true };
                    break;
                default:
                    throw new ArgumentException($"Unknown option '{arg}'.");
            }
        }
        return options;
    }

    private static void Print(DoctorReport report, IReadOnlyList<string> fixes, TextWriter stdout)
    {
        foreach (DoctorCheck check in report.Checks)
        {
            string status = check.Status switch
            {
                DoctorStatus.Ok => "ok",
                DoctorStatus.Warn => "warn",
                _ => "FAIL",
            };
            stdout.WriteLine(string.Create(CultureInfo.InvariantCulture, $"{status,-4} {check.Name}: {check.Message}"));
            if (check.Hint != null && check.Status != DoctorStatus.Ok)
            {
                stdout.WriteLine("     " + check.Hint);
            }
        }
        foreach (string fix in fixes) stdout.WriteLine("fixed: " + fix);
    }

    private static (string? Path, string? Version) Install(string? explicitPath, List<DoctorCheck> checks)
    {
        List<string> candidates = [];
        if (!string.IsNullOrWhiteSpace(explicitPath)) candidates.Add(explicitPath);
        else if (Environment.GetEnvironmentVariable("VINTAGE_STORY") is { Length: > 0 } fromEnvironment) candidates.Add(fromEnvironment);
        else candidates.AddRange(DefaultInstallPaths());

        string? found = candidates.FirstOrDefault(candidate => File.Exists(Path.Combine(candidate, "VintagestoryAPI.dll")));
        if (found == null)
        {
            string shown = explicitPath ?? Environment.GetEnvironmentVariable("VINTAGE_STORY") ?? string.Join(", ", candidates);
            checks.Add(new DoctorCheck("Vintage Story", DoctorStatus.Fail,
                $"no install found at {shown}",
                "Set VINTAGE_STORY to the folder holding VintagestoryAPI.dll, for example /opt/vintagestory."));
            return (null, null);
        }

        string path = Path.GetFullPath(found);
        string? version;
        try
        {
            version = InstalledGame.ReadVersion(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or BadImageFormatException)
        {
            version = null;
        }

        string[] required = ["VintagestoryAPI.dll", "VintagestoryLib.dll"];
        string[] missing = required.Where(name => !File.Exists(Path.Combine(path, name))).ToArray();
        if (missing.Length > 0)
        {
            checks.Add(new DoctorCheck("Vintage Story", DoctorStatus.Fail,
                $"{path} is missing {string.Join(", ", missing)}",
                "Point VINTAGE_STORY at the folder the game installs its DLLs in."));
            return (path, version);
        }

        string kind = HasOptimum(path) ? "Optimum" : "vanilla";
        checks.Add(new DoctorCheck("Vintage Story", DoctorStatus.Ok,
            $"{(version ?? "unknown version")} at {path} ({kind})"));
        return (path, version);
    }

    private static DoctorCheck Runtime(string? gameVersion)
    {
        int expected = gameVersion?.StartsWith("1.21", StringComparison.Ordinal) == true ? 8 : 10;
        int actual = Environment.Version.Major;
        string description = $".NET {Environment.Version}";
        if (actual == expected)
        {
            return new DoctorCheck(".NET", DoctorStatus.Ok,
                $"{description}{(gameVersion == null ? "" : $", which matches the game {gameVersion}")}");
        }
        if (actual < expected)
        {
            return new DoctorCheck(".NET", DoctorStatus.Fail,
                $"{description} is older than the .NET {expected} the game {(gameVersion ?? "1.22.x")} needs",
                $"Install the .NET {expected} runtime and run the tests with it.");
        }
        return new DoctorCheck(".NET", DoctorStatus.Warn,
            $"{description} is newer than the .NET {expected} the game {(gameVersion ?? "1.22.x")} needs",
            $"Keep the .NET {expected} runtime installed; the newer one may roll forward.");
    }

    private static DoctorCheck Graphics()
    {
        if (!OperatingSystem.IsLinux())
        {
            return new DoctorCheck("OpenGL", DoctorStatus.Ok,
                "the graphics driver supplies OpenGL; run a client scenario to confirm a 3.3 context");
        }

        string? display = Environment.GetEnvironmentVariable("DISPLAY");
        string? wayland = Environment.GetEnvironmentVariable("WAYLAND_DISPLAY");
        string? xvfb = FindOnPath("xvfb-run");
        if (!string.IsNullOrWhiteSpace(display) || !string.IsNullOrWhiteSpace(wayland))
        {
            return new DoctorCheck("OpenGL", DoctorStatus.Ok,
                $"display {display ?? wayland}; Mesa supplies the GL context");
        }
        if (xvfb != null)
        {
            return new DoctorCheck("OpenGL", DoctorStatus.Ok,
                $"no display; xvfb-run at {xvfb} can supply one");
        }
        return new DoctorCheck("OpenGL", DoctorStatus.Fail,
            "no display and no xvfb-run",
            "Install xvfb (for example apt install xvfb) or set DISPLAY to an X server.");
    }

    private static DoctorCheck Natives(string? gamePath)
    {
        if (gamePath == null)
        {
            return new DoctorCheck("Natives", DoctorStatus.Warn,
                "not checked without a game install",
                "Set VINTAGE_STORY and run doctor again.");
        }

        string lib = Path.Combine(gamePath, "Lib");
        if (!Directory.Exists(lib))
        {
            return new DoctorCheck("Natives", DoctorStatus.Fail,
                $"{lib} does not exist",
                "The install is incomplete; let the game install its Lib folder.");
        }

        (string Name, string[] Patterns)[] groups =
        [
            ("OpenAL", ["libopenal", "openal32"]),
            ("Cairo", ["libcairo", "cairo-sharp"]),
            ("GLFW", ["libglfw", "glfw3"]),
            ("SkiaSharp", ["libSkiaSharp", "SkiaSharp"]),
            ("Harmony", ["0Harmony"]),
            ("protobuf-net", ["protobuf-net"]),
        ];
        string[] files = Directory.EnumerateFiles(lib).Select(Path.GetFileName).OfType<string>().ToArray();
        List<string> missing = [];
        foreach ((string name, string[] patterns) in groups)
        {
            if (!files.Any(file => patterns.Any(pattern => file.Contains(pattern, StringComparison.OrdinalIgnoreCase))))
            {
                missing.Add(name);
            }
        }

        if (missing.Count == 0)
        {
            return new DoctorCheck("Natives", DoctorStatus.Ok, "OpenAL, Cairo, GLFW, SkiaSharp, Harmony and protobuf-net");
        }
        return new DoctorCheck("Natives", DoctorStatus.Warn,
            $"missing from {lib}: {string.Join(", ", missing)}",
            "The game may find them elsewhere; a client scenario reports the one it cannot load.");
    }

    private static DoctorCheck Project(string? projectPath, string? gamePath)
    {
        string? csproj = FindProject(projectPath);
        if (csproj == null)
        {
            return new DoctorCheck("Project", DoctorStatus.Ok, "no project to check");
        }

        XDocument document;
        try
        {
            document = XDocument.Load(csproj);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Xml.XmlException)
        {
            return new DoctorCheck("Project", DoctorStatus.Warn, $"{Path.GetFileName(csproj)} could not be read: {ex.Message}");
        }

        List<XElement> references = document.Descendants().Where(element => element.Name.LocalName == "PackageReference").ToList();
        bool pharos = references.Any(reference =>
            ((string?)reference.Attribute("Include"))?.StartsWith("Zaldaryon.Pharos", StringComparison.OrdinalIgnoreCase) == true);
        if (!pharos)
        {
            return new DoctorCheck("Project", DoctorStatus.Warn,
                $"{Path.GetFileName(csproj)} references no Zaldaryon.Pharos package",
                "Add Zaldaryon.Pharos.XUnit, or run pharos init to make a test project.");
        }

        if (gamePath != null)
        {
            foreach (XElement reference in document.Descendants().Where(element => element.Name.LocalName == "Reference"))
            {
                string? hint = (string?)reference.Element(reference.Name.Namespace + "HintPath");
                if (hint == null || !hint.Contains("VintagestoryAPI.dll", StringComparison.OrdinalIgnoreCase)) continue;
                string hintDirectory = Path.GetDirectoryName(Path.GetFullPath(Path.Combine(Path.GetDirectoryName(csproj)!, hint)))!;
                if (!string.Equals(hintDirectory.TrimEnd(Path.DirectorySeparatorChar), gamePath.TrimEnd(Path.DirectorySeparatorChar), StringComparison.Ordinal))
                {
                    return new DoctorCheck("Project", DoctorStatus.Warn,
                        $"{Path.GetFileName(csproj)} references the game at {hintDirectory}, the install is {gamePath}",
                        "Point the VintagestoryAPI reference at the install, or set VintageStoryPath to it.");
                }
            }
        }

        return new DoctorCheck("Project", DoctorStatus.Ok, Path.GetFileName(csproj));
    }

    private static DoctorCheck Ports()
    {
        try
        {
            for (int i = 0; i < 4; i++)
            {
                using TcpListener listener = new(IPAddress.Loopback, 0);
                listener.Start();
                listener.Stop();
            }
            return new DoctorCheck("Ports", DoctorStatus.Ok, "four loopback ports could be bound");
        }
        catch (SocketException ex)
        {
            return new DoctorCheck("Ports", DoctorStatus.Fail,
                $"a loopback port could not be bound: {ex.Message}",
                "Check the firewall and the ephemeral port range.");
        }
    }

    private static List<string> Fix(DoctorOptions options, DoctorReport report)
    {
        List<string> fixes = [];
        string directory = Directory.GetCurrentDirectory();

        if (OperatingSystem.IsLinux() && report.Checks.FirstOrDefault(check => check.Name == "OpenGL") is { Status: not DoctorStatus.Ok })
        {
            string script = Path.Combine(directory, "pharos-headless.sh");
            if (!File.Exists(script))
            {
                File.WriteAllText(script, "#!/bin/sh\nexport LIBGL_ALWAYS_SOFTWARE=1\nexec xvfb-run -a \"$@\"\n");
                if (OperatingSystem.IsLinux()) File.SetUnixFileMode(script, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
                fixes.Add(script);
            }
        }

        string? csproj = FindProject(options.ProjectPath);
        if (csproj != null && report.GamePath != null)
        {
            string runsettings = Path.Combine(Path.GetDirectoryName(csproj)!, "pharos.runsettings");
            if (!File.Exists(runsettings))
            {
                File.WriteAllText(runsettings, $"""
                    <?xml version="1.0" encoding="utf-8"?>
                    <RunSettings>
                      <RunConfiguration>
                        <EnvironmentVariables>
                          <VINTAGE_STORY>{System.Security.SecurityElement.Escape(report.GamePath)}</VINTAGE_STORY>
                          <LIBGL_ALWAYS_SOFTWARE>1</LIBGL_ALWAYS_SOFTWARE>
                        </EnvironmentVariables>
                      </RunConfiguration>
                    </RunSettings>
                    """);
                fixes.Add(runsettings);
            }
        }

        return fixes;
    }

    private static string? FindProject(string? projectPath)
    {
        if (!string.IsNullOrWhiteSpace(projectPath))
        {
            if (File.Exists(projectPath) && projectPath.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase)) return Path.GetFullPath(projectPath);
            if (Directory.Exists(projectPath)) return Directory.EnumerateFiles(projectPath, "*.csproj").FirstOrDefault();
            return null;
        }

        string? current = Directory.EnumerateFiles(Directory.GetCurrentDirectory(), "*.csproj").FirstOrDefault();
        return current;
    }

    private static IEnumerable<string> DefaultInstallPaths()
    {
        yield return "/usr/share/vintagestory";
        yield return "/opt/vintagestory";
        yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share", "vintagestory");
        yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config", "Vintagestory");
        if (OperatingSystem.IsWindows()) yield return @"C:\Games\VintageStory";
    }

    private static bool HasOptimum(string gamePath)
    {
        string mods = Path.Combine(gamePath, "Mods");
        if (!Directory.Exists(mods)) return false;
        return Directory.EnumerateFileSystemEntries(mods).Any(entry =>
            Path.GetFileName(entry).Contains("optimum", StringComparison.OrdinalIgnoreCase));
    }

    private static string? FindOnPath(string file)
    {
        string? path = Environment.GetEnvironmentVariable("PATH");
        if (path == null) return null;
        foreach (string directory in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            string candidate = Path.Combine(directory, file);
            if (File.Exists(candidate)) return candidate;
            if (OperatingSystem.IsWindows() && File.Exists(candidate + ".exe")) return candidate + ".exe";
        }
        return null;
    }
}
