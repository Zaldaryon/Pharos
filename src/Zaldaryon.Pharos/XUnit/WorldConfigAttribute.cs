using Newtonsoft.Json;

namespace Zaldaryon.Pharos.XUnit;

/// <summary>Sets one world configuration value before a server scenario boots.</summary>
/// <remarks>
/// Put it on a scenario class or method. Method values override class values with the same key.
/// The value is converted to the type declared by the game's <c>WorldConfigurationAttribute</c>.
/// </remarks>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true, Inherited = true)]
public sealed class WorldConfigAttribute : Attribute
{
    /// <summary>Sets a string world value, such as <c>temporalStorms</c> to <c>off</c>.</summary>
    public WorldConfigAttribute(string key, string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(value);
        Key = key;
        ValueJson = JsonConvert.SerializeObject(value, Formatting.None);
    }

    /// <summary>Sets a boolean world value.</summary>
    public WorldConfigAttribute(string key, bool value) : this(key, JsonConvert.SerializeObject(value, Formatting.None), alreadyJson: true) { }

    /// <summary>Sets an integer world value.</summary>
    public WorldConfigAttribute(string key, int value) : this(key, JsonConvert.SerializeObject(value, Formatting.None), alreadyJson: true) { }

    /// <summary>Sets a double-precision world value.</summary>
    public WorldConfigAttribute(string key, double value) : this(key, JsonConvert.SerializeObject(value, Formatting.None), alreadyJson: true) { }

    private WorldConfigAttribute(string key, string valueJson, bool alreadyJson)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        Key = key;
        ValueJson = valueJson;
    }

    /// <summary>The key in the world's configuration tree.</summary>
    public string Key { get; }

    internal string ValueJson { get; }
}
