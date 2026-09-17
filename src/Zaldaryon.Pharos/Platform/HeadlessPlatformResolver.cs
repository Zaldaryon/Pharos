using System.Reflection;
using System.Runtime.InteropServices;
using OpenTK.Windowing.GraphicsLibraryFramework;
using Vintagestory.Common;

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
        if (Directory.Exists(defaultWin))
        {
            _resolvedGamePath = defaultWin;
            return _resolvedGamePath;
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

    public static void Initialize(string? customGamePath = null)
    {
        lock (_initLock)
        {
            if (_initialized)
            {
                return;
            }

            string gamePath = ResolveGamePath(customGamePath);
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
            Environment.SetEnvironmentVariable("ALSOFT_DRIVERS", "null");

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

            _initialized = true;
        }
    }
}
