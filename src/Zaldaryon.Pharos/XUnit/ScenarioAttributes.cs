using System.Globalization;
using System.Reflection;
using Zaldaryon.Pharos.Core;
using Zaldaryon.Pharos.Server;

namespace Zaldaryon.Pharos.XUnit;

/// <summary>
/// Reads the scenario attributes of a test class and stages the mods they list.
/// </summary>
internal static class ScenarioAttributes
{
    /// <summary>
    /// The client settings from every <see cref="ClientSettingAttribute"/> on the class and its
    /// base classes, or null when there are none.
    /// </summary>
    public static ClientSettingsProfile? ClientSettings(Type testClass)
    {
        ClientSettingAttribute[] settings = [.. testClass.GetCustomAttributes<ClientSettingAttribute>(inherit: true)];
        return settings.Length == 0
            ? null
            : ClientSettingsProfile.Of(testClass.Name, [.. settings.Select(s => (s.Key, s.Value))]);
    }

    public static ServerWorldAttribute? ServerWorld(Type testClass) =>
        testClass.GetCustomAttribute<ServerWorldAttribute>(inherit: true);

    /// <summary>
    /// The world described by the class-level <see cref="ServerWorldAttribute"/>, or the
    /// <see cref="ServerWorldOptions"/> defaults. Each test class gets its own world name, so
    /// saves from different classes never collide.
    /// </summary>
    public static ServerWorldOptions WorldOptions(Type testClass)
    {
        ServerWorldAttribute? world = ServerWorld(testClass);
        ServerWorldOptions options = new() { WorldName = "Pharos-" + testClass.Name };
        if (world == null) return options;

        return options with
        {
            Seed = world.Seed == 0 ? options.Seed : world.Seed.ToString(CultureInfo.InvariantCulture),
            PlayStyle = world.PlayStyle,
            WorldType = world.WorldType,
            WorldConfigurationJson = world.WorldConfigurationJson ?? options.WorldConfigurationJson,
        };
    }

    /// <summary>
    /// Mod paths from every <see cref="ServerModsAttribute"/> on the class, its base classes and
    /// its assembly.
    /// </summary>
    public static IReadOnlyList<string> ServerMods(Type testClass) =>
        Collect<ServerModsAttribute>(testClass, a => a.ModPaths);

    /// <summary>
    /// Mod paths from every <see cref="PharosModsAttribute"/> on the class, its base classes and
    /// its assembly.
    /// </summary>
    public static IReadOnlyList<string> ClientMods(Type testClass) =>
        Collect<PharosModsAttribute>(testClass, a => a.ModPaths);

    /// <summary>
    /// Copies each mod (a folder, a .zip or a .dll) into <paramref name="modsDirectory"/>, where
    /// the game's mod loader picks it up.
    /// </summary>
    /// <exception cref="FileNotFoundException">A listed mod does not exist.</exception>
    public static void StageMods(IEnumerable<string> modPaths, string modsDirectory)
    {
        Directory.CreateDirectory(modsDirectory);

        foreach (string path in modPaths)
        {
            string fullPath = Path.GetFullPath(path);
            if (Directory.Exists(fullPath))
            {
                CopyDirectory(fullPath, Path.Combine(modsDirectory, new DirectoryInfo(fullPath).Name));
            }
            else if (File.Exists(fullPath))
            {
                File.Copy(fullPath, Path.Combine(modsDirectory, Path.GetFileName(fullPath)), overwrite: true);
            }
            else
            {
                throw new FileNotFoundException($"Mod path does not exist: '{path}' (resolved to '{fullPath}').", fullPath);
            }
        }
    }

    private static IReadOnlyList<string> Collect<T>(Type testClass, Func<T, IEnumerable<string>> paths) where T : Attribute
    {
        IEnumerable<T> attributes = testClass.Assembly.GetCustomAttributes<T>()
            .Concat(testClass.GetCustomAttributes<T>(inherit: true));
        return attributes.SelectMany(paths).Distinct(StringComparer.Ordinal).ToList();
    }

    private static void CopyDirectory(string source, string target)
    {
        foreach (string file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            string destination = Path.Combine(target, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(file, destination, overwrite: true);
        }
    }
}
