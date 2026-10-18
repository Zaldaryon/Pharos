using System.Reflection;
using Xunit.Sdk;
using Zaldaryon.Pharos.Core;

namespace Zaldaryon.Pharos.XUnit;

/// <summary>
/// Runs a theory once per client settings profile, passed as its
/// <see cref="ClientSettingsProfile"/> parameter.
/// </summary>
/// <remarks>
/// Name the game's graphics presets (see <see cref="ClientSettingsProfile.Presets"/>), single
/// settings written <c>key:value</c> (such as <c>guiScale:1.5</c>), or none for every preset. The test applies the profile with <c>Client.Settings.Apply(profile)</c>,
/// which puts the previous settings back when disposed.
/// <code>
/// [Theory, ClientSettingsMatrix("minimum", "high")]
/// public async Task WorldRenders(ClientSettingsProfile profile)
/// {
///     using IDisposable _ = Client!.Settings.Apply(profile);
///     ...
/// }
/// </code>
/// </remarks>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class ClientSettingsMatrixAttribute(params string[] presets) : DataAttribute
{
    /// <summary>The presets to run, by name, or settings written key:value; empty for every preset.</summary>
    public IReadOnlyList<string> PresetNames { get; } = presets;

    /// <inheritdoc />
    public override IEnumerable<object[]> GetData(MethodInfo testMethod)
    {
        IEnumerable<ClientSettingsProfile> profiles = PresetNames.Count == 0
            ? ClientSettingsProfile.Presets.Values
            : PresetNames.Select(Profile);

        return profiles.Select(p => new object[] { p });
    }

    // A preset by name, or a single setting written "key:value", such as "guiScale:1.5".
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
