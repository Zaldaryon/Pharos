using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Common;
using Zaldaryon.Pharos.Server;

namespace Zaldaryon.Pharos.Scenarios;

/// <summary>The configuration axis supplied by a Pharos scenario data attribute.</summary>
public enum ScenarioConfigurationMatrixKind
{
    /// <summary>A value in <see cref="ServerWorldOptions.WorldConfigurationJson"/>.</summary>
    World,
    /// <summary>A value in the server's <c>serverconfig.json</c>.</summary>
    Server,
    /// <summary>A source file copied into the server's <c>ModConfig</c> directory.</summary>
    Mod,
}

/// <summary>A xUnit data attribute whose selected row also configures its Pharos scenario.</summary>
public interface IScenarioConfigurationMatrix
{
    /// <summary>The configuration area this matrix selects.</summary>
    ScenarioConfigurationMatrixKind Kind { get; }

    /// <summary>A world/server key, or the destination filename for a mod config.</summary>
    string Key { get; }

    /// <summary>The values returned as the theory's sole string argument.</summary>
    IReadOnlyList<string> Values { get; }
}

internal sealed record ScenarioModConfigFile(string Source, string Destination, EnumAppSide Side);

/// <summary>The effective configuration selected for one scenario row.</summary>
internal sealed class ScenarioConfiguration
{
    private readonly SortedDictionary<string, JToken> _worldValues = new(StringComparer.OrdinalIgnoreCase);
    private readonly SortedDictionary<string, JToken> _serverValues = new(StringComparer.OrdinalIgnoreCase);

    public static ScenarioConfiguration Empty { get; } = new([], []);

    public ScenarioConfiguration(
        IEnumerable<KeyValuePair<string, JToken>> worldValues,
        IEnumerable<KeyValuePair<string, JToken>> serverValues,
        IEnumerable<ScenarioModConfigFile>? modFiles = null)
    {
        foreach ((string key, JToken value) in worldValues) _worldValues[key] = value.DeepClone();
        foreach ((string key, JToken value) in serverValues) _serverValues[key] = value.DeepClone();
        ModFiles = modFiles?.ToArray() ?? [];
    }

    public IReadOnlyList<ScenarioModConfigFile> ModFiles { get; }

    /// <summary>A stable identity of the selected values, for host pooling.</summary>
    public string Key
    {
        get
        {
            StringBuilder key = new();
            foreach ((string name, JToken value) in _worldValues) key.Append("world.").Append(name).Append('=').Append(value.ToString(Formatting.None)).Append(';');
            foreach ((string name, JToken value) in _serverValues) key.Append("server.").Append(name).Append('=').Append(value.ToString(Formatting.None)).Append(';');
            foreach (ScenarioModConfigFile file in ModFiles) key.Append("mod.").Append(file.Destination).Append('<').Append(file.Source).Append(';');
            return key.ToString();
        }
    }

    /// <summary>The partial JSON object staged as the server's <c>serverconfig.json</c>.</summary>
    public string? ServerConfigJson
    {
        get
        {
            if (_serverValues.Count == 0) return null;
            JObject json = new() { ["ConfigVersion"] = "1.0" };
            foreach ((string key, JToken value) in _serverValues) json[key] = value.DeepClone();
            return json.ToString(Formatting.None);
        }
    }

    /// <summary>Adds selected world values to the existing world recipe without discarding it.</summary>
    public ServerWorldOptions ApplyWorldValues(ServerWorldOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (_worldValues.Count == 0) return options;

        JObject json = string.IsNullOrWhiteSpace(options.WorldConfigurationJson)
            ? new JObject()
            : JObject.Parse(options.WorldConfigurationJson);
        foreach ((string key, JToken value) in _worldValues)
        {
            JProperty? existing = json.Properties().FirstOrDefault(property =>
                property.Name.Equals(key, StringComparison.OrdinalIgnoreCase));
            if (existing == null) json[key] = value.DeepClone();
            else existing.Value = value.DeepClone();
        }

        return options with { WorldConfigurationJson = json.ToString(Formatting.None) };
    }

    /// <summary>Writes the partial server config before the server reads it during boot.</summary>
    /// <remarks>
    /// The file carries <c>ConfigVersion</c> 1.0, which makes the game fill in its default player
    /// roles. A config without them makes the game stop with "no such group exists".
    /// </remarks>
    public void WriteServerConfig(string dataPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataPath);
        if (ServerConfigJson is not { } json) return;
        File.WriteAllText(Path.Combine(dataPath, "serverconfig.json"), json);
    }

    /// <summary>The values to include in a failure artifact or per-row run manifest.</summary>
    public IReadOnlyDictionary<string, string>? ForRunManifest(ServerWorldOptions world, DataFileSet files)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(files);

        Dictionary<string, string> values = new(StringComparer.Ordinal);
        ServerWorldOptions effective = ApplyWorldValues(world);
        if (!string.IsNullOrWhiteSpace(effective.WorldConfigurationJson))
        {
            foreach (JProperty property in JObject.Parse(effective.WorldConfigurationJson).Properties())
                values["world." + property.Name] = Display(property.Value);
        }

        foreach ((string key, JToken value) in _serverValues)
            values["server." + key] = Display(value);

        foreach ((string key, string value) in files.ForRunManifest()) values[key] = value;

        return values.Count == 0 ? null : values;
    }

    /// <summary>Parses an attribute matrix value as a JSON scalar, or as a string when it is not JSON.</summary>
    internal static JToken ParseMatrixScalar(string value, string parameterName)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException("A configuration matrix value cannot be empty.", parameterName);

        try
        {
            JToken token = JToken.Parse(value);
            if (token is not JValue) throw new ArgumentException("Configuration matrix values must be JSON scalars or plain strings.", parameterName);
            return token;
        }
        catch (JsonReaderException)
        {
            return new JValue(value);
        }
    }

    private static string Display(JToken token) => token.Type switch
    {
        JTokenType.String => token.Value<string>() ?? "",
        JTokenType.Boolean => token.Value<bool>() ? "true" : "false",
        JTokenType.Integer or JTokenType.Float => ((JValue)token).Value is IFormattable formattable
            ? formattable.ToString(null, System.Globalization.CultureInfo.InvariantCulture)
            : token.ToString(Formatting.None),
        JTokenType.Null => "null",
        _ => token.ToString(Formatting.None),
    };
}
