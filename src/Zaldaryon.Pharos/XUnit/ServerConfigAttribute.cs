using Newtonsoft.Json;

namespace Zaldaryon.Pharos.XUnit;

/// <summary>Sets one <c>ServerConfig</c> property before a server scenario boots.</summary>
/// <remarks>Put it on a scenario class or method. Method values override class values with the same key.</remarks>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true, Inherited = true)]
public sealed class ServerConfigAttribute : Attribute
{
    /// <summary>Sets a string server config property.</summary>
    public ServerConfigAttribute(string key, string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(value);
        Key = key;
        ValueJson = JsonConvert.SerializeObject(value, Formatting.None);
    }

    /// <summary>Sets a boolean server config property.</summary>
    public ServerConfigAttribute(string key, bool value) : this(key, JsonConvert.SerializeObject(value, Formatting.None), alreadyJson: true) { }

    /// <summary>Sets an integer server config property.</summary>
    public ServerConfigAttribute(string key, int value) : this(key, JsonConvert.SerializeObject(value, Formatting.None), alreadyJson: true) { }

    /// <summary>Sets a single-precision server config property.</summary>
    public ServerConfigAttribute(string key, float value) : this(key, JsonConvert.SerializeObject(value, Formatting.None), alreadyJson: true) { }

    /// <summary>Sets a double-precision server config property.</summary>
    public ServerConfigAttribute(string key, double value) : this(key, JsonConvert.SerializeObject(value, Formatting.None), alreadyJson: true) { }

    private ServerConfigAttribute(string key, string valueJson, bool alreadyJson)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        Key = key;
        ValueJson = valueJson;
    }

    /// <summary>The property or field name on <c>Vintagestory.Server.ServerConfig</c>.</summary>
    public string Key { get; }

    internal string ValueJson { get; }
}
