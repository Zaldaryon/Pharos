using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace Zaldaryon.Pharos.Cli;

/// <summary>
/// Runs <c>pharos</c> from a folder that holds the tool beside the user's own game install.
/// </summary>
/// <remarks>
/// <para>
/// The tool ships none of the game's files. The game, though, expects its assemblies in the folder
/// of the program that runs it: it looks for <c>VintagestoryAPI.dll</c> there to compile code
/// mods, and resolves its references through the runtime's list of trusted assemblies. So the
/// launcher builds, once per tool version and game install, a folder with the tool's own files
/// and links to every assembly and native library of the install, flattened as a test project's
/// output has them, with no <c>deps.json</c>, which puts all of them on that list. Then it runs
/// the tool again from there.
/// </para>
/// <para>
/// The folder lives under <c>PHAROS_CLI_CACHE</c>, or the user's local application data. Links
/// are symbolic where the system allows them, copies otherwise.
/// </para>
/// </remarks>
internal static class Launcher
{
    /// <summary>Set in the child process: it runs from the staged folder.</summary>
    public const string StagedVariable = "PHAROS_CLI_STAGED";

    private const string Marker = ".pharos-staged";

    public static bool IsStaged => Environment.GetEnvironmentVariable(StagedVariable) == "1";

    /// <summary>The game install from <c>--game</c> or <c>VINTAGE_STORY</c>, or null.</summary>
    public static string? GamePath(string[] args)
    {
        int index = Array.IndexOf(args, "--game");
        string? path = index >= 0 && index + 1 < args.Length ? args[index + 1] : Environment.GetEnvironmentVariable("VINTAGE_STORY");
        return string.IsNullOrEmpty(path) ? null : Path.GetFullPath(path);
    }

    /// <summary>Stages the tool beside <paramref name="game"/> and runs it from there; returns its exit code.</summary>
    public static int RunStaged(string game, string[] args)
    {
        string staged = Stage(game);

        ProcessStartInfo start = new(DotnetHost()) { UseShellExecute = false };
        start.ArgumentList.Add("exec");
        start.ArgumentList.Add(Path.Combine(staged, Path.GetFileName(typeof(Launcher).Assembly.Location)));
        foreach (string arg in args) start.ArgumentList.Add(arg);
        start.Environment[StagedVariable] = "1";
        start.Environment["VINTAGE_STORY"] = game;

        using Process child = Process.Start(start) ?? throw new InvalidOperationException("Could not start the staged pharos.");

        // A signal to the launcher reaches the game too: a CI runner that stops `pharos` must
        // not leave a server and a client running.
        using IDisposable forwarding = ForwardSignals(child);
        child.WaitForExit();
        return child.ExitCode;
    }

    /// <summary>The staged folder for <paramref name="game"/>, built if it is missing or stale.</summary>
    internal static string Stage(string game)
    {
        string api = Path.Combine(game, "VintagestoryAPI.dll");
        if (!File.Exists(api)) throw new FileNotFoundException($"'{game}' is not a Vintage Story install: it has no VintagestoryAPI.dll.", api);

        string tool = AppContext.BaseDirectory;
        string key = Key(tool, game);
        string root = Environment.GetEnvironmentVariable("PHAROS_CLI_CACHE") is { Length: > 0 } cache
            ? Path.GetFullPath(cache)
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolderOption.Create), "pharos", "cli");
        string staged = Path.Combine(root, key);
        if (File.Exists(Path.Combine(staged, Marker))) return staged;

        Directory.CreateDirectory(root);
        RemoveLeftovers(root, staged);

        // Built aside and moved into place, so a concurrent run never sees half a folder.
        string building = staged + ".building-" + Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture);
        if (Directory.Exists(building)) Directory.Delete(building, recursive: true);
        Directory.CreateDirectory(building);

        foreach (string file in GameFiles(game))
        {
            Link(file, Path.Combine(building, Path.GetFileName(file)));
        }

        // The tool's own files win over the game's: its Newtonsoft.Json is the same version.
        foreach (string file in Directory.EnumerateFiles(tool))
        {
            if (file.EndsWith(".deps.json", StringComparison.OrdinalIgnoreCase)) continue;
            string target = Path.Combine(building, Path.GetFileName(file));
            if (File.Exists(target) || IsLink(target)) File.Delete(target);
            File.Copy(file, target);
        }

        // The game's folders, linked here rather than by the game at run time: a run stopped
        // half way through copying them would leave a broken folder behind.
        foreach (string folder in new[] { "Lib", "Mods", "assets" })
        {
            string source = Path.Combine(game, folder);
            if (!Directory.Exists(source)) continue;

            try
            {
                Directory.CreateSymbolicLink(Path.Combine(building, folder), source);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
            {
                // No symbolic links here: the game copies the folder when it first runs.
            }
        }

        File.WriteAllText(Path.Combine(building, Marker), $"game: {game}{Environment.NewLine}tool: {tool}{Environment.NewLine}", new UTF8Encoding(false));

        try
        {
            Directory.Move(building, staged);
        }
        catch (IOException) when (File.Exists(Path.Combine(staged, Marker)))
        {
            // Another run staged it first.
            Directory.Delete(building, recursive: true);
        }

        return staged;
    }

    /// <summary>
    /// Names the staged folder after every file it is made of: the tool's own and the install's,
    /// by name, size and time, so a rebuilt tool or an updated install gets a fresh folder.
    /// </summary>
    internal static string Key(string tool, string game)
    {
        StringBuilder text = new();
        text.Append(game).Append('|');
        foreach (string file in Directory.EnumerateFiles(tool).Concat(GameFiles(game)).Order(StringComparer.Ordinal))
        {
            FileInfo info = new(file);
            text.Append(file).Append(':').Append(info.Length).Append(':').Append(info.LastWriteTimeUtc.Ticks).Append('|');
        }

        return Hash(text.ToString());
    }

    /// <summary>
    /// Clears what earlier runs left: a folder for this key without its marker, and folders
    /// another run was building but did not finish, an hour or more ago.
    /// </summary>
    private static void RemoveLeftovers(string root, string staged)
    {
        try
        {
            if (Directory.Exists(staged)) Directory.Delete(staged, recursive: true);

            foreach (string building in Directory.EnumerateDirectories(root, "*.building-*"))
            {
                if (Directory.GetLastWriteTimeUtc(building) < DateTime.UtcNow.AddHours(-1)) Directory.Delete(building, recursive: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Another run may be using them; a later run cleans up.
        }
    }

    /// <summary>The install's assemblies, their symbols and its native libraries, from its root and <c>Lib</c>.</summary>
    private static IEnumerable<string> GameFiles(string game)
    {
        foreach (string file in Directory.EnumerateFiles(game))
        {
            if (file.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) || file.EndsWith(".pdb", StringComparison.OrdinalIgnoreCase)) yield return file;
        }

        string lib = Path.Combine(game, "Lib");
        if (!Directory.Exists(lib)) yield break;

        foreach (string file in Directory.EnumerateFiles(lib))
        {
            yield return file;
        }
    }

    private static IDisposable ForwardSignals(Process child)
    {
        void Stop()
        {
            try
            {
                if (!child.HasExited) child.Kill(entireProcessTree: true);
            }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                // It has exited already.
            }
        }

        if (OperatingSystem.IsWindows())
        {
            ConsoleCancelEventHandler handler = (_, _) => Stop();
            Console.CancelKeyPress += handler;
            return new Disposer(() => Console.CancelKeyPress -= handler);
        }

        PosixSignalRegistration[] registrations =
        [
            .. new[] { PosixSignal.SIGINT, PosixSignal.SIGTERM, PosixSignal.SIGQUIT }.Select(signal => PosixSignalRegistration.Create(signal, _ => Stop())),
        ];
        return new Disposer(() =>
        {
            foreach (PosixSignalRegistration registration in registrations) registration.Dispose();
        });
    }

    private sealed class Disposer(Action dispose) : IDisposable
    {
        public void Dispose() => dispose();
    }

    private static void Link(string source, string target)
    {
        try
        {
            File.CreateSymbolicLink(target, source);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            File.Copy(source, target, overwrite: true);
        }
    }

    private static bool IsLink(string path) => new FileInfo(path).LinkTarget != null;

    private static string Hash(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)))[..16].ToLowerInvariant();

    /// <summary>The dotnet host that runs this process.</summary>
    private static string DotnetHost()
    {
        string executable = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "dotnet.exe" : "dotnet";
        if (Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") is { Length: > 0 } host && File.Exists(host)) return host;

        // The shared runtime lives under <dotnet root>/shared/Microsoft.NETCore.App/<version>/.
        string fromRuntime = Path.GetFullPath(Path.Combine(RuntimeEnvironment.GetRuntimeDirectory(), "..", "..", "..", executable));
        if (File.Exists(fromRuntime)) return fromRuntime;

        if (Environment.ProcessPath is { } process && Path.GetFileName(process).Equals(executable, StringComparison.OrdinalIgnoreCase)) return process;

        return executable;
    }
}
