using System.Reflection;
using Xunit;
using Xunit.Sdk;
using Xunit.v3;
using Zaldaryon.Pharos.Scenarios;

namespace Zaldaryon.Pharos.XUnit.V3;

/// <summary>Runs an xUnit v3 server theory once per world configuration value.</summary>
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
    public override ValueTask<IReadOnlyCollection<ITheoryDataRow>> GetData(MethodInfo testMethod, DisposalTracker disposalTracker)
    {
        ConfigMatrixValidation.Validate(testMethod, Values, nameof(WorldConfigMatrixAttribute));
        return Rows(Values);
    }

    /// <inheritdoc />
    public override bool SupportsDiscoveryEnumeration() => true;

    internal static ValueTask<IReadOnlyCollection<ITheoryDataRow>> Rows(IReadOnlyList<string> values) =>
        new((IReadOnlyCollection<ITheoryDataRow>)values.Select(value => (ITheoryDataRow)new TheoryDataRow(value)).ToList());
}

/// <summary>Runs an xUnit v3 server theory once per server configuration value.</summary>
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
    public override ValueTask<IReadOnlyCollection<ITheoryDataRow>> GetData(MethodInfo testMethod, DisposalTracker disposalTracker)
    {
        ConfigMatrixValidation.Validate(testMethod, Values, nameof(ServerConfigMatrixAttribute));
        return WorldConfigMatrixAttribute.Rows(Values);
    }

    /// <inheritdoc />
    public override bool SupportsDiscoveryEnumeration() => true;
}

/// <summary>Runs an xUnit v3 server theory once per mod configuration fixture.</summary>
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
    public override ValueTask<IReadOnlyCollection<ITheoryDataRow>> GetData(MethodInfo testMethod, DisposalTracker disposalTracker)
    {
        ConfigMatrixValidation.Validate(testMethod, Values, nameof(ModConfigMatrixAttribute));
        return WorldConfigMatrixAttribute.Rows(Values);
    }

    /// <inheritdoc />
    public override bool SupportsDiscoveryEnumeration() => true;
}

internal static class ConfigMatrixValidation
{
    public static void Validate(MethodInfo method, IReadOnlyList<string> values, string attribute)
    {
        ArgumentNullException.ThrowIfNull(method);
        if (values.Count == 0) throw new ArgumentException($"[{attribute}] needs at least one value.");
        if (values.Any(string.IsNullOrWhiteSpace)) throw new ArgumentException($"[{attribute}] values cannot be empty.");
        if (method.GetParameters() is not [{ ParameterType: var type }] || type != typeof(string))
            throw new ArgumentException($"[{attribute}] requires a theory method with one string parameter.");
    }
}
