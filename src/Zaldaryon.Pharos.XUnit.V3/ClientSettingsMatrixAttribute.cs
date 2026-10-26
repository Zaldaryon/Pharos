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

    /// <summary>
    /// Presets that should be skipped, with their reason. Use "presetName:reason" format.
    /// </summary>
    public string[] SkipPresets { get; set; } = [];

    /// <inheritdoc />
    public override ValueTask<IReadOnlyCollection<ITheoryDataRow>> GetData(MethodInfo testMethod, DisposalTracker disposalTracker)
    {
        IEnumerable<ClientSettingsProfile> profiles = PresetNames.Count == 0
            ? ClientSettingsProfile.Presets.Values
            : PresetNames.Select(Profile);

        Dictionary<string, string> skipReasons = ParseSkipPresets();

        IReadOnlyCollection<ITheoryDataRow> rows = profiles.Select(p =>
        {
            skipReasons.TryGetValue(p.Name, out string? skipReason);
            return (ITheoryDataRow)new TheoryDataRow<ClientSettingsProfile>(p)
            {
                TestDisplayName = p.Name,
                Skip = skipReason
            };
        }).ToList();

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

    private Dictionary<string, string> ParseSkipPresets()
    {
        Dictionary<string, string> result = new(StringComparer.OrdinalIgnoreCase);
        foreach (string entry in SkipPresets)
        {
            int colonIndex = entry.IndexOf(':');
            if (colonIndex <= 0) continue;
            string key = entry[..colonIndex].Trim();
            string reason = entry[(colonIndex + 1)..].Trim();
            if (key.Length > 0 && reason.Length > 0)
            {
                result[key] = reason;
            }
        }
        return result;
    }
}
