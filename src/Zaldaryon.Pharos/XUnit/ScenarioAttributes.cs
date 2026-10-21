using System.Globalization;
using System.Reflection;
using Newtonsoft.Json.Linq;
using Zaldaryon.Pharos.Core;
using Zaldaryon.Pharos.Scenarios;
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
        ServerWorldAttribute? world = ScenarioTestInfo.Current?.Method?.GetCustomAttribute<ServerWorldAttribute>(inherit: true)
            ?? ServerWorld(testClass);
        ServerWorldOptions options = new() { WorldName = "Pharos-" + testClass.Name };
        if (world == null) return options;

        return options with
        {
            Seed = world.Seed == 0 ? options.Seed : world.Seed.ToString(CultureInfo.InvariantCulture),
            PlayStyle = world.PlayStyle,
            WorldType = world.WorldType,
            WorldConfigurationJson = world.WorldConfigurationJson ?? options.WorldConfigurationJson,
            SaveFile = world.SaveFile,
            Preset = world.Preset,
        };
    }

    /// <summary>The configuration values declared for the current class and theory row.</summary>
    public static ScenarioConfiguration Configuration(Type testClass)
    {
        SortedDictionary<string, JToken> world = new(StringComparer.OrdinalIgnoreCase);
        SortedDictionary<string, JToken> server = new(StringComparer.OrdinalIgnoreCase);

        List<Type> hierarchy = [];
        for (Type? type = testClass; type != null && type != typeof(object); type = type.BaseType) hierarchy.Add(type);
        hierarchy.Reverse();
        foreach (Type type in hierarchy)
        {
            ApplyWorldLayer(type.GetCustomAttributes<WorldConfigAttribute>(inherit: false), world, type.Name);
            ApplyServerLayer(type.GetCustomAttributes<ServerConfigAttribute>(inherit: false), server, type.Name);
        }

        MethodInfo? method = ScenarioTestInfo.Current?.Method;
        if (method != null)
        {
            ApplyWorldLayer(method.GetCustomAttributes<WorldConfigAttribute>(inherit: false), world, method.Name);
            ApplyServerLayer(method.GetCustomAttributes<ServerConfigAttribute>(inherit: false), server, method.Name);
        }

        List<ScenarioModConfigFile> modFiles = [];
        IScenarioConfigurationMatrix? matrix = SingleMatrix(method?.GetCustomAttributes(inherit: false).OfType<IScenarioConfigurationMatrix>() ?? []);
        if (matrix != null)
        {
            string selected = SelectedMatrixValue(matrix, ScenarioTestInfo.Current);
            switch (matrix.Kind)
            {
                case ScenarioConfigurationMatrixKind.World:
                    world[matrix.Key] = ScenarioConfiguration.ParseMatrixScalar(selected, nameof(matrix));
                    break;
                case ScenarioConfigurationMatrixKind.Server:
                    AddServerValue(server, matrix.Key, ScenarioConfiguration.ParseMatrixScalar(selected, nameof(matrix)), matrix.Key);
                    break;
                case ScenarioConfigurationMatrixKind.Mod:
                    modFiles.Add(new ScenarioModConfigFile(selected, "ModConfig/" + matrix.Key, Vintagestory.API.Common.EnumAppSide.Server));
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(matrix), matrix.Kind, "Unknown scenario configuration matrix kind.");
            }
        }

        return new ScenarioConfiguration(world, server, modFiles);
    }

    /// <summary>Records the resolved world, server and seeded-file values for the current row.</summary>
    public static void RecordConfiguration(ScenarioConfiguration configuration, ServerWorldOptions world, DataFileSet files)
    {
        IReadOnlyDictionary<string, string>? manifest = configuration.ForRunManifest(world, files);
        if (ScenarioTestInfo.Current is { } test) test.Configuration = manifest;
        ScenarioAttemptState.Current?.SetConfiguration(manifest);
    }

    /// <summary>
    /// The data files the class and the running test seed, for the sides a scenario has. See
    /// <see cref="DataFilesAttribute"/>.
    /// </summary>
    public static DataFileSet DataFiles(Type testClass, params Vintagestory.API.Common.EnumAppSide[] sides) =>
        DataFiles(testClass, ScenarioConfiguration.Empty, sides);

    /// <summary>The class, method and matrix-selected files for the current scenario.</summary>
    public static DataFileSet DataFiles(Type testClass, ScenarioConfiguration configuration, params Vintagestory.API.Common.EnumAppSide[] sides)
    {
        // Each class is a level of its own, from the base down, so a derived class can replace
        // its base's file; the test comes last.
        List<IEnumerable<DataFilesAttribute>> levels = [];
        for (Type? type = testClass; type != null && type != typeof(object); type = type.BaseType)
        {
            levels.Insert(0, type.GetCustomAttributes<DataFilesAttribute>(inherit: false));
        }

        List<DataFilesAttribute> methodFiles = [.. ScenarioTestInfo.Current?.Method?.GetCustomAttributes<DataFilesAttribute>(inherit: true) ?? []];
        methodFiles.AddRange(configuration.ModFiles.Select(file => new DataFilesAttribute(file.Source)
        {
            To = file.Destination,
            Side = file.Side,
        }));
        levels.Add(methodFiles);
        return DataFileSet.Create(levels, sides);
    }

    /// <summary>The only configuration matrix of a scenario, or null when it has none.</summary>
    /// <exception cref="ArgumentException">The scenario declares more than one matrix.</exception>
    internal static IScenarioConfigurationMatrix? SingleMatrix(IEnumerable<IScenarioConfigurationMatrix> matrices)
    {
        IScenarioConfigurationMatrix[] list = [.. matrices];
        if (list.Length > 1)
            throw new ArgumentException("A scenario theory can use only one Pharos configuration matrix because xUnit does not combine data attributes.");
        return list.Length == 1 ? list[0] : null;
    }

    private static void ApplyWorldLayer(IEnumerable<WorldConfigAttribute> attributes, SortedDictionary<string, JToken> target, string level)
    {
        HashSet<string> keys = new(StringComparer.OrdinalIgnoreCase);
        foreach (WorldConfigAttribute attribute in attributes)
        {
            if (!keys.Add(attribute.Key)) throw new InvalidOperationException($"Two [WorldConfig] attributes set '{attribute.Key}' on {level}.");
            target[attribute.Key] = JToken.Parse(attribute.ValueJson);
        }
    }

    private static void ApplyServerLayer(IEnumerable<ServerConfigAttribute> attributes, SortedDictionary<string, JToken> target, string level)
    {
        HashSet<string> keys = new(StringComparer.OrdinalIgnoreCase);
        foreach (ServerConfigAttribute attribute in attributes)
        {
            if (!keys.Add(attribute.Key)) throw new InvalidOperationException($"Two [ServerConfig] attributes set '{attribute.Key}' on {level}.");
            AddServerValue(target, attribute.Key, JToken.Parse(attribute.ValueJson), level);
        }
    }

    private static void AddServerValue(SortedDictionary<string, JToken> target, string key, JToken value, string level)
    {
        PropertyInfo? property = typeof(Vintagestory.Server.ServerConfig).GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .FirstOrDefault(candidate => candidate.Name.Equals(key, StringComparison.OrdinalIgnoreCase)
                && candidate.GetIndexParameters().Length == 0 && candidate.CanWrite);
        if (property == null)
            throw new ArgumentException($"Server config key '{key}' on {level} is not a writable ServerConfig property.", nameof(key));

        try
        {
            object? converted = value.ToObject(property.PropertyType);
            target[property.Name] = converted == null ? JValue.CreateNull() : JToken.FromObject(converted);
        }
        catch (Exception ex) when (ex is Newtonsoft.Json.JsonException or ArgumentException or FormatException or OverflowException or InvalidCastException)
        {
            throw new ArgumentException($"Server config value for '{property.Name}' cannot be converted to {property.PropertyType.Name}.", nameof(value), ex);
        }
    }

    private static string SelectedMatrixValue(IScenarioConfigurationMatrix matrix, ScenarioTestInfo? test)
    {
        if (matrix.Values.Count == 0) throw new ArgumentException($"The {matrix.GetType().Name} needs at least one value.");
        if (test?.MethodArguments is not [string selected])
            throw new ArgumentException($"The {matrix.GetType().Name} requires a scenario theory with one string argument.");
        if (!matrix.Values.Contains(selected, StringComparer.Ordinal))
            throw new ArgumentException($"The scenario row value '{selected}' is not declared by {matrix.GetType().Name}.");
        return selected;
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
    public static void StageMods(IEnumerable<string> modPaths, string modsDirectory) =>
        StageMods(modPaths, modsDirectory, Environment.GetEnvironmentVariable(OverrideVariable));

    /// <summary>Stages mods, replacing declared paths with a build from <paramref name="overrideRoot"/>.</summary>
    /// <param name="modPaths">The paths declared on the mod attributes.</param>
    /// <param name="modsDirectory">Where the mods are staged.</param>
    /// <param name="overrideRoot">A directory of alternative builds, or null.</param>
    internal static void StageMods(IEnumerable<string> modPaths, string modsDirectory, string? overrideRoot)
    {
        Directory.CreateDirectory(modsDirectory);

        foreach (string path in modPaths)
        {
            string fullPath = Override(path, overrideRoot) ?? Path.GetFullPath(path);
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

    /// <summary>The environment variable that swaps a declared mod build for another one.</summary>
    public const string OverrideVariable = "PHAROS_MOD_OVERRIDE";

    /// <summary>
    /// The build of a declared mod that <paramref name="overrideRoot"/> points at, if any: a file,
    /// folder or zip of the same name in that directory is staged instead of the declared path.
    /// <c>pharos compare</c> uses it to run the same tests against a second build.
    /// </summary>
    /// <param name="path">A path declared on a mod attribute.</param>
    /// <param name="overrideRoot">The directory of alternative builds, or null.</param>
    /// <returns>The replacement path, or null when the declared path stands.</returns>
    private static string? Override(string path, string? overrideRoot)
    {
        if (string.IsNullOrWhiteSpace(overrideRoot) || !Directory.Exists(overrideRoot)) return null;
        string name = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (string.IsNullOrEmpty(name)) return null;
        string candidate = Path.Combine(overrideRoot, name);
        return File.Exists(candidate) || Directory.Exists(candidate) ? candidate : null;
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
