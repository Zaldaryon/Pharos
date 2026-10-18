using System.Reflection;
using Xunit;
using Xunit.Sdk;
using Xunit.v3;
using Zaldaryon.Pharos.Core;

namespace Zaldaryon.Pharos.XUnit.V3;

/// <summary>
/// Runs a theory once per client settings profile, passed as its ClientSettingsProfile parameter.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class ClientSettingsMatrixAttribute(params string[] presets) : DataAttribute
{
    /// <summary>The presets to run, by name, or settings written key:value; empty for every preset.</summary>
    public IReadOnlyList<string> PresetNames { get; } = presets;

    /// <inheritdoc />
    public override ValueTask<IReadOnlyCollection<ITheoryDataRow>> GetData(MethodInfo testMethod, DisposalTracker disposalTracker)
    {
        IEnumerable<ClientSettingsProfile> profiles = PresetNames.Count == 0
            ? ClientSettingsProfile.Presets.Values
            : PresetNames.Select(Profile);

        IReadOnlyCollection<ITheoryDataRow> rows = profiles.Select(p => (ITheoryDataRow)new TheoryDataRow(p)).ToList();
        return new ValueTask<IReadOnlyCollection<ITheoryDataRow>>(rows);
    }

    /// <inheritdoc />
    public override bool SupportsDiscoveryEnumeration() => true;

    internal static ClientSettingsProfile Profile(string entry)
    {
        int colon = entry.IndexOf(':');
        if (colon < 0) return ClientSettingsProfile.Preset(entry);
        string key = entry[..colon].Trim();
        string value = entry[(colon + 1)..].Trim();
        if (key.Length == 0 || value.Length == 0) throw new ArgumentException($"'{entry}' is not a setting written key:value, such as guiScale:1.5.", nameof(entry));
        return ClientSettingsProfile.Of(entry, (key, value));
    }
}
