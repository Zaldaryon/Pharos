using System.Runtime.InteropServices;
using Zaldaryon.Pharos.Core;

namespace Zaldaryon.Pharos.Visual;

internal readonly record struct ResolvedGolden(string ComparisonPath, string UpdatePath, bool UsedGenericFallback);

internal static class GoldenImageStore
{
    internal const string UpdateVariable = "PHAROS_UPDATE_GOLDENS";
    internal const string RootVariable = "PHAROS_GOLDENS";
    private static readonly AsyncLocal<bool?> s_updateOverride = new();
    internal static bool? UpdateOverride { get => s_updateOverride.Value; set => s_updateOverride.Value = value; }

    internal static ResolvedGolden Resolve(string name, string? root = null, string? platform = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        string normalized = name.Replace('\\', '/');
        if (Path.IsPathRooted(name) || normalized.Split('/').Any(p => p is ".." or "." or ""))
            throw new ArgumentException("Golden image names must be relative and cannot traverse directories.", nameof(name));
        char[] portableInvalidChars = ['<', '>', ':', '"', '|', '?', '*'];
        if (normalized.Split('/').Any(part => part.IndexOfAny(portableInvalidChars) >= 0 || part.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0))
            throw new ArgumentException("Golden image names must use portable filename characters.", nameof(name));
        if (normalized.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) normalized = normalized[..^4];
        if (normalized.Length == 0) throw new ArgumentException("Golden image name cannot be empty.", nameof(name));

        root ??= Environment.GetEnvironmentVariable(RootVariable);
        root = Path.GetFullPath(string.IsNullOrWhiteSpace(root) ? Path.Combine(Environment.CurrentDirectory, "Goldens") : root);
        platform ??= CurrentPlatform;
        if (string.IsNullOrWhiteSpace(platform) || platform.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || platform.IndexOfAny(portableInvalidChars) >= 0 || platform.Contains('/') || platform.Contains('\\'))
            throw new ArgumentException("Platform name must be a valid filename component.", nameof(platform));

        string stem = Path.Combine(root, normalized.Replace('/', Path.DirectorySeparatorChar));
        string exact = stem + "." + platform + ".png";
        string generic = stem + ".png";
        if (File.Exists(exact)) return new(exact, exact, false);
        if (File.Exists(generic)) return new(generic, exact, true);
        return new(exact, exact, false);
    }

    internal static string CurrentPlatform => RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "windows"
        : RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? "macos"
        : RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ? "linux"
        : "unknown";

    internal static bool UpdateEnabled => UpdateOverride ?? Environment.GetEnvironmentVariable(UpdateVariable) == "1";

    internal static void SaveAtomically(FramebufferSnapshot snapshot, string path)
    {
        string full = Path.GetFullPath(path);
        string directory = Path.GetDirectoryName(full)!;
        Directory.CreateDirectory(directory);
        string temporary = Path.Combine(directory, "." + Path.GetFileName(full) + "." + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            snapshot.SaveToPng(temporary);
            File.Move(temporary, full, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }
}
