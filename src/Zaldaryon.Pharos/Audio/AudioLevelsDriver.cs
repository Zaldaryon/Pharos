using Vintagestory.API.Common;
using Vintagestory.Client.NoObf;

namespace Zaldaryon.Pharos.Audio;

/// <summary>A snapshot of all audio level settings.</summary>
public sealed record AudioLevelsSnapshot(
    int Master,
    int Sound,
    int Entity,
    int Ambient,
    int Weather,
    int Music)
{
    /// <summary>Gets the level for a specific sound type.</summary>
    public int ForType(EnumSoundType type) => type switch
    {
        EnumSoundType.Sound => Sound,
        EnumSoundType.Music => Music,
        EnumSoundType.Ambient => Ambient,
        EnumSoundType.Weather => Weather,
        EnumSoundType.Entity => Entity,
        EnumSoundType.AmbientGlitchunaffected => Ambient,
        _ => Sound
    };

    /// <summary>Gets the effective level for a sound type, accounting for master volume.</summary>
    public float EffectiveLevel(EnumSoundType type) => Master / 100f * ForType(type) / 100f;
}

/// <summary>
/// Reads and modifies the client's audio level settings.
/// </summary>
public sealed class AudioLevelsDriver
{
    /// <summary>Reads the current audio level settings.</summary>
    public static AudioLevelsSnapshot Snapshot() => new(
        ClientSettings.MasterSoundLevel,
        ClientSettings.SoundLevel,
        ClientSettings.EntitySoundLevel,
        ClientSettings.AmbientSoundLevel,
        ClientSettings.WeatherSoundLevel,
        ClientSettings.MusicLevel);

    /// <summary>Gets or sets the master sound level (0-100).</summary>
    public int Master
    {
        get => ClientSettings.MasterSoundLevel;
        set => ClientSettings.MasterSoundLevel = value;
    }

    /// <summary>Gets or sets the general sound level (0-100).</summary>
    public int Sound
    {
        get => ClientSettings.SoundLevel;
        set => ClientSettings.SoundLevel = value;
    }

    /// <summary>Gets or sets the entity sound level (0-100).</summary>
    public int Entity
    {
        get => ClientSettings.EntitySoundLevel;
        set => ClientSettings.EntitySoundLevel = value;
    }

    /// <summary>Gets or sets the ambient sound level (0-100).</summary>
    public int Ambient
    {
        get => ClientSettings.AmbientSoundLevel;
        set => ClientSettings.AmbientSoundLevel = value;
    }

    /// <summary>Gets or sets the weather sound level (0-100).</summary>
    public int Weather
    {
        get => ClientSettings.WeatherSoundLevel;
        set => ClientSettings.WeatherSoundLevel = value;
    }

    /// <summary>Gets or sets the music level (0-100).</summary>
    public int MusicLevel
    {
        get => ClientSettings.MusicLevel;
        set => ClientSettings.MusicLevel = value;
    }
}
