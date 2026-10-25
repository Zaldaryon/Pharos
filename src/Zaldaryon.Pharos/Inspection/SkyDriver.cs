using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Zaldaryon.Pharos.Core;

namespace Zaldaryon.Pharos.Inspection;

/// <summary>The sun or moon as the client renders it.</summary>
public sealed record CelestialSnapshot(float X, float Y, float Z, float[] Color);

/// <summary>The sky colour the client renders at zenith and horizon.</summary>
public sealed record SkySnapshot(
    CelestialSnapshot Sun,
    CelestialSnapshot Moon,
    float[] ZenithColor,
    float[] HorizonColor,
    float DayLightStrength,
    float MoonLightStrength,
    float SunsetMod);

/// <summary>
/// Reads the client's sky render state: sun and moon positions and colours, sky colour at zenith
/// and horizon, and daylight strength. Dispatched on the client thread.
/// </summary>
public sealed class SkyDriver
{
    private readonly HeadlessClient _client;

    internal SkyDriver(HeadlessClient client) => _client = client;

    /// <summary>Reads the current sky render state on the client thread.</summary>
    public SkySnapshot Snapshot() => _client.RunOnClientThread(() =>
    {
        var cal = _client.Client.Calendar;
        var sun = cal.SunPositionNormalized;
        var moon = cal.MoonPosition;
        var ambient = _client.Client.AmbientManager;
        return new SkySnapshot(
            new CelestialSnapshot(sun.X, sun.Y, sun.Z, new[] { 1f, 1f, 1f }),
            new CelestialSnapshot(moon.X, moon.Y, moon.Z, new[] { 1f, 1f, 1f }),
            new[] { ambient.BlendedFogColor.X, ambient.BlendedFogColor.Y, ambient.BlendedFogColor.Z, ambient.BlendedFogColor.W },
            new[] { ambient.BlendedAmbientColor.X, ambient.BlendedAmbientColor.Y, ambient.BlendedAmbientColor.Z },
            cal.DayLightStrength,
            cal.MoonLightStrength,
            _client.Client.Calendar.SunsetMod);
    });
}