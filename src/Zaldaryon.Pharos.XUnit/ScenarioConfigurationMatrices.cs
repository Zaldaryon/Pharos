using System.Reflection;
using Xunit.Sdk;
using Zaldaryon.Pharos.Scenarios;

namespace Zaldaryon.Pharos.XUnit;

/// <summary>Runs a server theory once per world configuration value.</summary>
/// <remarks>
/// The method takes one <see cref="string"/> argument. Values that are valid JSON scalars keep
/// their JSON type; other values, such as <c>off</c>, are strings.
/// <code>
/// [ServerTheory, WorldConfigMatrix("creatureHostility", "aggressive", "passive", "off")]
/// public Task Hostility_IsConfigured(string value) { ... }
/// </code>
/// </remarks>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class WorldConfigMatrixAttribute : DataAttribute, IScenarioConfigurationMatrix
{
    /// <summary>Creates one row for each world configuration value.</summary>
    public WorldConfigMatrixAttribute(string key, params string[] values)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        Key = key;
        Values = Array.AsReadOnly((string[])values.Clone());
    }

    /// <inheritdoc />
    public ScenarioConfigurationMatrixKind Kind => ScenarioConfigurationMatrixKind.World;

    /// <summary>The world configuration key.</summary>
    public string Key { get; }

    /// <summary>The values supplied as the theory's string argument.</summary>
    public IReadOnlyList<string> Values { get; }

    /// <inheritdoc />
    public override IEnumerable<object[]> GetData(MethodInfo testMethod)
    {
        Validate(testMethod, Values, nameof(WorldConfigMatrixAttribute));
        return Values.Select(value => new object[] { value });
    }

    internal static void Validate(MethodInfo method, IReadOnlyList<string> values, string attribute)
    {
        ArgumentNullException.ThrowIfNull(method);
        if (values.Count == 0) throw new ArgumentException($"[{attribute}] needs at least one value.");
        if (values.Any(string.IsNullOrWhiteSpace)) throw new ArgumentException($"[{attribute}] values cannot be empty.");
        if (method.GetParameters() is not [{ ParameterType: var type }] || type != typeof(string))
            throw new ArgumentException($"[{attribute}] requires a theory method with one string parameter.");
    }
}

/// <summary>Runs a server theory once per server configuration value.</summary>
/// <remarks>
/// The method takes one <see cref="string"/> argument. Values are converted to the type of the
/// matching <c>ServerConfig</c> property or field before the server boots.
/// </remarks>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class ServerConfigMatrixAttribute : DataAttribute, IScenarioConfigurationMatrix
{
    /// <summary>Creates one row for each server configuration value.</summary>
    public ServerConfigMatrixAttribute(string key, params string[] values)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        Key = key;
        Values = Array.AsReadOnly((string[])values.Clone());
    }

    /// <inheritdoc />
    public ScenarioConfigurationMatrixKind Kind => ScenarioConfigurationMatrixKind.Server;

    /// <summary>The property or field name on <c>Vintagestory.Server.ServerConfig</c>.</summary>
    public string Key { get; }

    /// <summary>The values supplied as the theory's string argument.</summary>
    public IReadOnlyList<string> Values { get; }

    /// <inheritdoc />
    public override IEnumerable<object[]> GetData(MethodInfo testMethod)
    {
        WorldConfigMatrixAttribute.Validate(testMethod, Values, nameof(ServerConfigMatrixAttribute));
        return Values.Select(value => new object[] { value });
    }
}

/// <summary>Runs a server theory once per mod configuration fixture.</summary>
/// <remarks>
/// The first argument is the filename written under <c>ModConfig</c>. Each remaining path names a
/// fixture copied into that destination before boot. The method receives the selected source path.
/// </remarks>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class ModConfigMatrixAttribute : DataAttribute, IScenarioConfigurationMatrix
{
    /// <summary>Creates one row for each config fixture copied to <paramref name="destination"/>.</summary>
    public ModConfigMatrixAttribute(string destination, params string[] values)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destination);
        if (Path.IsPathRooted(destination) || destination.Contains('/') || destination.Contains('\\') || destination is "." or "..")
            throw new ArgumentException("A mod config matrix destination must be a filename inside ModConfig, such as 'mymod.json'.", nameof(destination));
        Key = destination;
        Values = Array.AsReadOnly((string[])values.Clone());
    }

    /// <inheritdoc />
    public ScenarioConfigurationMatrixKind Kind => ScenarioConfigurationMatrixKind.Mod;

    /// <summary>The destination filename inside <c>ModConfig</c>.</summary>
    public string Key { get; }

    /// <summary>The source config fixtures supplied as the theory's string argument.</summary>
    public IReadOnlyList<string> Values { get; }

    /// <inheritdoc />
    public override IEnumerable<object[]> GetData(MethodInfo testMethod)
    {
        WorldConfigMatrixAttribute.Validate(testMethod, Values, nameof(ModConfigMatrixAttribute));
        return Values.Select(value => new object[] { value });
    }
}
