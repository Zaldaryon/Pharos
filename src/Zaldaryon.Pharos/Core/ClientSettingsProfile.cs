using Vintagestory.Client;

namespace Zaldaryon.Pharos.Core;

/// <summary>
/// A named set of client settings, by their keys in <c>clientsettings.json</c>, such as
/// <c>viewDistance</c> or <c>ssaoquality</c>.
/// </summary>
/// <param name="Name">The name tests and reports show, such as <c>low</c>.</param>
/// <param name="Values">The settings, by key. Values are converted to each setting's type.</param>
public sealed record ClientSettingsProfile(string Name, IReadOnlyDictionary<string, object> Values)
{
    /// <summary>
    /// The game's own graphics presets, by name: <c>minimum</c>, <c>pathetic</c>, <c>ultralow</c>,
    /// <c>verylow</c>, <c>low</c>, <c>medium</c>, <c>high</c>, <c>veryhigh</c>, <c>ultrahigh</c>,
    /// <c>glorious</c> and <c>maximum</c>, with the settings the graphics menu sets for each.
    /// </summary>
    public static IReadOnlyDictionary<string, ClientSettingsProfile> Presets { get; } = GraphicsPreset.Presets
        .Where(p => p.Langcode != "preset-custom")
        .ToDictionary(p => p.Langcode["preset-".Length..], FromPreset, StringComparer.OrdinalIgnoreCase);

    /// <summary>The preset called <paramref name="name"/>; see <see cref="Presets"/>.</summary>
    /// <exception cref="ArgumentException">There is no such preset.</exception>
    public static ClientSettingsProfile Preset(string name) =>
        Presets.TryGetValue(name, out ClientSettingsProfile? profile)
            ? profile
            : throw new ArgumentException($"No graphics preset called '{name}'. Known: {string.Join(", ", Presets.Keys)}", nameof(name));

    /// <summary>A profile of the given settings.</summary>
    public static ClientSettingsProfile Of(string name, params (string Key, object Value)[] values) =>
        new(name, values.ToDictionary(v => v.Key, v => v.Value, StringComparer.OrdinalIgnoreCase));

    /// <summary>This profile with <paramref name="values"/> added or replaced.</summary>
    public ClientSettingsProfile With(params (string Key, object Value)[] values)
    {
        Dictionary<string, object> merged = new(Values, StringComparer.OrdinalIgnoreCase);
        foreach ((string key, object value) in values) merged[key] = value;
        return this with { Values = merged };
    }

    /// <inheritdoc />
    public override string ToString() => Name;

    // The settings the graphics menu applies when a preset is picked.
    private static ClientSettingsProfile FromPreset(GraphicsPreset p) => Of(
        p.Langcode["preset-".Length..],
        ("graphicsPresetId", p.PresetId),
        ("viewDistance", p.ViewDistance),
        ("smoothShadows", p.SmoothLight),
        ("fxaa", p.FXAA),
        ("ssaoquality", p.SSAO),
        ("wavingStuff", p.WavingFoliage),
        ("liquidFoamAndShinyEffect", p.LiquidFoamEffect),
        ("bloom", p.Bloom),
        ("godRays", p.GodRays ? 1 : 0),
        ("shadowMapQuality", p.ShadowMapQuality),
        ("particleLevel", p.ParticleLevel),
        ("maxDynamicLights", p.DynamicLights),
        ("ssaa", p.Resolution),
        ("lodBiasFar", p.LodBiasFar));
}
