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

            _initialized = true;
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

        LinkOrCopyDirectory(Path.Combine(gamePath, "Lib"), Path.Combine(baseDir, "Lib"));
        LinkOrCopyDirectory(Path.Combine(gamePath, "Mods"), Path.Combine(baseDir, "Mods"));
    }

    private static void LinkOrCopyDirectory(string sourceDir, string targetDir)
    {
        if (!Directory.Exists(sourceDir) || Directory.Exists(targetDir))
        {
            return;
        }

        try
        {
            Directory.CreateSymbolicLink(targetDir, sourceDir);
        }
        catch
        {
            try
            {
                Directory.CreateDirectory(targetDir);
                foreach (string file in Directory.GetFiles(sourceDir))
                {
                    string destFile = Path.Combine(targetDir, Path.GetFileName(file));
                    File.Copy(file, destFile, overwrite: true);
                }
            }
            catch
            {
                // Best-effort staging
            }
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
