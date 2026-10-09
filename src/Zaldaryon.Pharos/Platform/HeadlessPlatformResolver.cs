using System.Reflection;
using System.Runtime.InteropServices;
using OpenTK.Windowing.GraphicsLibraryFramework;
using Vintagestory.Common;
using Zaldaryon.Pharos.Bootstrap;

namespace Zaldaryon.Pharos.Platform;

/// <summary>
/// Discovers the Vintage Story game directory and registers assembly and native library resolvers.
/// </summary>
public static class HeadlessPlatformResolver
{
    private static bool _initialized;
    private static readonly object _initLock = new();
    private static string? _resolvedGamePath;

    public static string ResolveGamePath(string? candidatePath = null)
    {
        if (!string.IsNullOrEmpty(candidatePath) && Directory.Exists(candidatePath))
        {
            return Path.GetFullPath(candidatePath);
        }

        if (!string.IsNullOrEmpty(_resolvedGamePath))
        {
            return _resolvedGamePath;
        }

        string? envPath = Environment.GetEnvironmentVariable("VINTAGE_STORY");
        if (!string.IsNullOrEmpty(envPath) && Directory.Exists(envPath))
        {
            _resolvedGamePath = Path.GetFullPath(envPath);
            return _resolvedGamePath;
        }

        string defaultWin = @"C:\Games\VintageStory";
        if (OperatingSystem.IsWindows() && Directory.Exists(defaultWin))
        {
            _resolvedGamePath = defaultWin;
            return _resolvedGamePath;
        }

        if (OperatingSystem.IsLinux())
        {
            string userHome = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            string[] linuxCandidates =
            [
                "/usr/share/vintagestory",
                "/opt/vintagestory",
                Path.Combine(userHome, ".local", "share", "vintagestory"),
                Path.Combine(userHome, ".config", "Vintagestory")
            ];

            foreach (string candidate in linuxCandidates)
            {
                if (Directory.Exists(candidate))
                {
                    _resolvedGamePath = Path.GetFullPath(candidate);
                    return _resolvedGamePath;
                }
            }
        }

        try
        {
            string baseDir = AppContext.BaseDirectory;
            string relativeVanilla = Path.GetFullPath(Path.Combine(baseDir, @"..\..\..\..\..\.vanilla\win-x64\vintagestory"));
            if (Directory.Exists(relativeVanilla))
            {
                _resolvedGamePath = relativeVanilla;
                return _resolvedGamePath;
            }
        }
        catch
        {
            // Fall through if path navigation fails outside git repository tree
        }

        _resolvedGamePath = Path.GetFullPath(AppContext.BaseDirectory);
        return _resolvedGamePath;
    }

    public static void Initialize(string? customGamePath)
    {
        Initialize(new HeadlessClientOptions { GameInstallPath = customGamePath });
    }

    public static void Initialize(HeadlessClientOptions? options = null)
    {
        lock (_initLock)
        {
            if (_initialized)
            {
                return;
            }

            options ??= new HeadlessClientOptions();
            string gamePath = ResolveGamePath(options.GameInstallPath);
            string libDir = Path.Combine(gamePath, "Lib");

            // Add Lib folder to PATH for unmanaged dependency resolution using platform path separator
            string currentPath = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
            if (!currentPath.Contains(libDir, StringComparison.OrdinalIgnoreCase))
            {
                Environment.SetEnvironmentVariable("PATH", $"{libDir}{Path.PathSeparator}{currentPath}");
            }

            // Allow GLFW to run from any test runner thread
            OpenTK.Windowing.Desktop.GLFWProvider.CheckForMainThread = false;

            // Force OpenAL Soft to use the null driver backend in headless/CI environments
            if (options.DisableAudio || options.UseNullAudioDevice)
            {
                Environment.SetEnvironmentVariable("ALSOFT_DRIVERS", "null");
            }

            // On Linux, configure Mesa software rendering (llvmpipe) before GLFW/OpenGL loads
            if (options.ConfigureMesaEnvironment && (OperatingSystem.IsLinux() || options.ForceSoftwareRendering))
            {
                LinuxHeadlessEnvironment.ConfigureMesaEnvironment(options);
            }

            // Register assembly resolver for game and Lib directories
            AppDomain.CurrentDomain.AssemblyResolve += (sender, args) =>
            {
                string simpleName = new AssemblyName(args.Name).Name + ".dll";
                string pathInLib = Path.Combine(libDir, simpleName);
                if (File.Exists(pathInLib))
                {
                    return Assembly.LoadFrom(pathInLib);
                }
                string pathInRoot = Path.Combine(gamePath, simpleName);
                if (File.Exists(pathInRoot))
                {
                    return Assembly.LoadFrom(pathInRoot);
                }
                return null;
            };

            // Register Vintage Story managed assembly resolver as fallback
            AppDomain.CurrentDomain.AssemblyResolve += AssemblyResolver.AssemblyResolve;

            // Register native library resolver for cairo-sharp on Linux
            void TryRegisterNativeResolvers(Assembly assembly)
            {
                string? name = assembly.GetName().Name;
                if (string.Equals(name, "cairo-sharp", StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        NativeLibrary.SetDllImportResolver(assembly, (libraryName, asm, searchPath) =>
                        {
                            if (libraryName is "libcairo-2" or "libcairo-2.dll" or "cairo")
                            {
                                if (OperatingSystem.IsLinux())
                                {
                                    string[] candidates = ["libcairo.so.2", "libcairo.so", "libcairo-2.so"];
                                    foreach (string candidate in candidates)
                                    {
                                        if (NativeLibrary.TryLoad(candidate, asm, searchPath, out IntPtr handle))
                                        {
                                            return handle;
                                        }
                                    }
                                }
                            }
                            return IntPtr.Zero;
                        });
                    }
                    catch (InvalidOperationException)
                    {
                        // Resolver already registered for this assembly
                    }
                }
            }

            foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                TryRegisterNativeResolvers(asm);
            }

            AppDomain.CurrentDomain.AssemblyLoad += (_, args) =>
            {
                TryRegisterNativeResolvers(args.LoadedAssembly);
            };

            // Ensure Lib and Mods directories are staged in AppContext.BaseDirectory for mod compilation
            EnsureStagedBinaries(gamePath);
            CheckGameMatchesBuild(gamePath, AppContext.BaseDirectory);

            _initialized = true;
        }

        InstallGameHooks();
    }

    /// <summary>
    /// Installs Pharos's hooks on game methods before anything in the process boots. A method the
    /// runtime has already compiled into its callers would go on running unpatched there: a client
    /// in fixture mode, or a server, booted first in the process compiles the methods an engine
    /// client's inspectors, measurements, mod network log and translation tracking hook. Each hook
    /// does nothing until it is asked to.
    /// </summary>
    private static void InstallGameHooks()
    {
        Inspection.ClientInspectionPatches.Install();
        Network.ModNetworkPatches.Install();
        Performance.MeasurementHooks.InstallServer();
        Translations.TranslationCapture.Install();
        try
        {
            Graphics.GlResourceHooks.Install();
        }
        catch (InvalidOperationException)
        {
            // Kept for TrackGlResources to report; nothing else needs these hooks to boot.
        }
    }

    /// <summary>
    /// Ensures Lib and Mods folders are accessible from the runtime BaseDirectory so that
    /// mod loading and compilation succeed during server and client bootstrap.
    /// </summary>
    public static void EnsureStagedBinaries(string? customGamePath = null)
    {
        string gamePath = ResolveGamePath(customGamePath);
        string baseDir = AppContext.BaseDirectory;

        // A copy made from another install, or from this one before it was updated, is staged again.
        string fingerprint = $"{Path.GetFullPath(gamePath)}|{InstalledGame.ReadVersion(gamePath) ?? "unknown"}";
        LinkOrCopyDirectory(Path.Combine(gamePath, "Lib"), Path.Combine(baseDir, "Lib"), fingerprint);
        LinkOrCopyDirectory(Path.Combine(gamePath, "Mods"), Path.Combine(baseDir, "Mods"), fingerprint);
        LinkOrCopyDirectory(Path.Combine(gamePath, "assets"), Path.Combine(baseDir, "assets"), fingerprint);
        EnsureAssetsPath(Path.Combine(gamePath, "assets"));
        StageSoftwareGl(Path.Combine(gamePath, "Lib"), baseDir);
    }

    /// <summary>
    /// The game's assemblies sit next to the tests from the build, copied from the install they were
    /// built against, while the rest of the game comes from <paramref name="gamePath"/>. When the two
    /// are different versions, the engine fails in ways that are hard to trace, so that stops here;
    /// <c>PHAROS_ALLOW_GAME_MISMATCH=1</c> turns it into a warning. The same version with a
    /// different API file, as a patched install has, only warns.
    /// </summary>
    internal static void CheckGameMatchesBuild(string gamePath, string baseDir)
    {
        string? installed = InstalledGame.ReadVersion(gamePath);
        string? built = InstalledGame.ReadVersion(baseDir);
        if (installed == null || built == null) return;

        if (installed != built)
        {
            string message = $"The tests were built against Vintage Story {built}, but VINTAGE_STORY is {installed} ({gamePath}). " +
                "Build the tests again against this install, or set PHAROS_ALLOW_GAME_MISMATCH=1 to run anyway.";
            if (Environment.GetEnvironmentVariable("PHAROS_ALLOW_GAME_MISMATCH") != "1") throw new InvalidOperationException(message);
            Console.Error.WriteLine("Pharos: " + message);
            return;
        }

        // An Optimum build patches the API and the engine library together: built against one and
        // run on the other, the patched library meets an API without its patches, or the reverse.
        OptimumInfo? installedOptimum = OptimumInstall.Detect(gamePath);
        OptimumInfo? builtOptimum = OptimumInstall.Detect(baseDir);
        if ((installedOptimum == null) != (builtOptimum == null))
        {
            string message = installedOptimum != null
                ? $"VINTAGE_STORY ({gamePath}) is an Optimum build, but the tests were built against vanilla Vintage Story {built}. Build the tests again against this install, or set PHAROS_ALLOW_GAME_MISMATCH=1 to run anyway."
                : $"The tests were built against an Optimum build, but VINTAGE_STORY ({gamePath}) is vanilla Vintage Story {installed}. Build the tests again against this install, or set PHAROS_ALLOW_GAME_MISMATCH=1 to run anyway.";
            if (Environment.GetEnvironmentVariable("PHAROS_ALLOW_GAME_MISMATCH") != "1") throw new InvalidOperationException(message);
            Console.Error.WriteLine("Pharos: " + message);
            return;
        }

        if (installedOptimum is { LibPatched: false })
        {
            Console.Error.WriteLine($"Pharos: the API in {gamePath} carries Optimum's diagnostics but its engine library has none of Optimum's patches; the install looks half patched.");
        }

        FileInfo installedApi = new(Path.Combine(gamePath, "VintagestoryAPI.dll"));
        FileInfo builtApi = new(Path.Combine(baseDir, "VintagestoryAPI.dll"));
        if (installedApi.Length != builtApi.Length || installedOptimum?.Version != builtOptimum?.Version)
        {
            Console.Error.WriteLine(installedOptimum != null
                ? $"Pharos: the tests were built against another Optimum build than the one in {gamePath} ({OptimumInstall.Describe(installedOptimum)})."
                : $"Pharos: the VintagestoryAPI.dll the tests were built with differs from the one in {gamePath}, though both are {installed}.");
        }
    }

    // Windows finds opengl32.dll next to the test host before anything on PATH, so a software GL
    // that setup put into the game's Lib folder (see .github/actions/setup-vintage-story) is
    // copied beside the tests.
    private static void StageSoftwareGl(string libDir, string baseDir)
    {
        if (!OperatingSystem.IsWindows() || !File.Exists(Path.Combine(libDir, "opengl32.dll"))) return;
        foreach (string name in (string[])["opengl32.dll", "libgallium_wgl.dll", "libglapi.dll", "dxil.dll"])
        {
            string source = Path.Combine(libDir, name), target = Path.Combine(baseDir, name);
            if (!File.Exists(source) || File.Exists(target)) continue;
            try
            {
                File.Copy(source, target);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }
    }

    internal static void LinkOrCopyDirectory(string sourceDir, string targetDir, string fingerprint)
    {
        // With no install found, the game path is the test output itself: nothing to stage.
        if (!Directory.Exists(sourceDir) || SamePath(sourceDir, targetDir)
            || (Directory.Exists(targetDir) && !IsStale(sourceDir, targetDir, fingerprint)))
        {
            return;
        }

        // Several test processes can start in one output folder at once, as `pharos run
        // --parallel` does: one stages, the others wait and find the folder there.
        using FileStream? gate = LockStaging(Path.GetDirectoryName(targetDir)!);
        if (IsStale(sourceDir, targetDir, fingerprint) && !Unstage(targetDir))
        {
            return;
        }

        if (Directory.Exists(targetDir))
        {
            return;
        }

        try
        {
            Directory.CreateSymbolicLink(targetDir, sourceDir);
            return;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            // No symbolic links here: copy.
        }

        // Copied beside it and moved into place, so a process that stops halfway never leaves a
        // folder that looks staged but is not.
        string staging = $"{targetDir}.staging-{Environment.ProcessId}";
        try
        {
            if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true);
            CopyDirectoryRecursive(sourceDir, staging);
            Directory.Move(staging, targetDir);
            File.WriteAllText(MarkerOf(targetDir), fingerprint);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best-effort staging
            try
            {
                if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true);
            }
            catch (Exception cleanup) when (cleanup is IOException or UnauthorizedAccessException)
            {
            }
        }
    }

    // Beside a copied folder: what it was copied from.
    private static string MarkerOf(string targetDir) => targetDir + ".pharos-source";

    /// <summary>
    /// Whether <paramref name="targetDir"/> holds something other than <paramref name="sourceDir"/>:
    /// a link to another folder, a link whose folder is gone, or a copy made from another install
    /// or version. A copy staged before Pharos wrote its marker is left as it is.
    /// </summary>
    internal static bool IsStale(string sourceDir, string targetDir, string fingerprint)
    {
        DirectoryInfo target = new(targetDir);
        if (target.LinkTarget is { } link)
        {
            string resolved = Path.IsPathRooted(link) ? link : Path.Combine(Path.GetDirectoryName(Path.GetFullPath(targetDir))!, link);
            return !SamePath(resolved, sourceDir) || !Directory.Exists(targetDir);
        }

        if (!Directory.Exists(targetDir)) return false;
        string marker = MarkerOf(targetDir);
        return File.Exists(marker) && File.ReadAllText(marker) != fingerprint;
    }

    private static bool SamePath(string a, string b) => string.Equals(
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(a)),
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(b)),
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    // Removes a stale link or copy. A copy another process still holds files of stays, with a
    // warning: its tests run against what it holds.
    private static bool Unstage(string targetDir)
    {
        if (new DirectoryInfo(targetDir).LinkTarget != null)
        {
            try
            {
                Directory.Delete(targetDir);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                File.Delete(targetDir);
            }

            return true;
        }

        string old = $"{targetDir}.old-{Environment.ProcessId}";
        try
        {
            Directory.Move(targetDir, old);
            File.Delete(MarkerOf(targetDir));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"Pharos: {targetDir} was copied from another Vintage Story install and could not be replaced ({ex.Message}). Delete it to stage the current one.");
            return false;
        }

        try
        {
            Directory.Delete(old, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }

        return true;
    }

    // A lock other processes see too: a file only one of them can hold open. Null when it cannot
    // be had within ten minutes, and staging goes ahead without it.
    private static FileStream? LockStaging(string directory)
    {
        string path = Path.Combine(directory, ".pharos-staging.lock");
        System.Diagnostics.Stopwatch waited = System.Diagnostics.Stopwatch.StartNew();
        while (waited.Elapsed < TimeSpan.FromMinutes(10))
        {
            try
            {
                return new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            }
            catch (IOException)
            {
                Thread.Sleep(200);
            }
            catch (UnauthorizedAccessException)
            {
                return null;
            }
        }

        return null;
    }

    private static void CopyDirectoryRecursive(string sourceDir, string targetDir)
    {
        Directory.CreateDirectory(targetDir);
        foreach (string file in Directory.GetFiles(sourceDir))
        {
            string destFile = Path.Combine(targetDir, Path.GetFileName(file));
            File.Copy(file, destFile, overwrite: true);
        }
        foreach (string subDir in Directory.GetDirectories(sourceDir))
        {
            string destSubDir = Path.Combine(targetDir, Path.GetFileName(subDir));
            CopyDirectoryRecursive(subDir, destSubDir);
        }
    }

    /// <summary>
    /// Ensures GamePaths.AssetsPath points to a valid assets directory within the game installation.
    /// </summary>
    public static void EnsureAssetsPath(string? customAssetsPath = null)
    {
        string gamePath = ResolveGamePath();
        string assetsPath = customAssetsPath ?? Path.Combine(gamePath, "assets");
        if (Directory.Exists(assetsPath))
        {
            PropertyInfo? prop = typeof(Vintagestory.API.Config.GamePaths).GetProperty("AssetsPath", BindingFlags.Public | BindingFlags.Static);
            MethodInfo? setter = prop?.GetSetMethod(nonPublic: true);
            setter?.Invoke(null, [assetsPath]);
        }
    }
}
