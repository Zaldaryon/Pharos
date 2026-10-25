using System.Collections.Generic;
using System.Reflection;
using Vintagestory.API.Client;
using Vintagestory.Client.NoObf;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Zaldaryon.Pharos.Core;

namespace Zaldaryon.Pharos.Inspection;

/// <summary>A lightning flash seen by the client, with its position and the frame it appeared.</summary>
public sealed record LightningFlashSnapshot(float X, float Y, float Z, long Frame);

/// <summary>The weather state the client renders: precipitation, wind, clouds and particles.</summary>
public sealed record WeatherSnapshot(
    float PrecipitationIntensity,
    int PrecipitationType,
    int BlendedPrecipitationType,
    float[] WindSpeed,
    int WeatherParticlesAlive,
    IReadOnlyList<LightningFlashSnapshot> LightningFlashes);

/// <summary>
/// Reads the client's weather render state: precipitation intensity and type, wind, alive weather
/// particles and lightning flashes. Dispatched on the client thread.
/// </summary>
public sealed class ClientWeatherDriver
{
    private readonly HeadlessClient _client;
    private readonly List<LightningFlashSnapshot> _flashes = new();
    private long _lastFlashFrame;

    internal ClientWeatherDriver(HeadlessClient client) => _client = client;

    /// <summary>Reads the current weather render state on the client thread.</summary>
    public WeatherSnapshot Snapshot() => _client.RunOnClientThread(() =>
    {
        Type? wsType = AppDomain.CurrentDomain.GetAssemblies()
            .FirstOrDefault(a => a.GetName().Name == "VSEssentials")?
            .GetType("Vintagestory.GameContent.WeatherSystemClient", throwOnError: false);
        object? ws = null;
        if (wsType != null)
        {
            MethodInfo? getModSystem = typeof(Vintagestory.API.Common.IModLoader).GetMethods()
                .FirstOrDefault(m => m.Name == "GetModSystem" && m.IsGenericMethodDefinition);
            if (getModSystem != null)
                ws = getModSystem.MakeGenericMethod(wsType).Invoke(_client.Client.api.ModLoader, [false]);
        }
        object? data = ws?.GetType().GetProperty("BlendedWeatherData")?.GetValue(ws);
        int alive = 0;
        try
        {
            var pmField = typeof(ClientMain).GetField("particleManager", BindingFlags.Instance | BindingFlags.NonPublic);
            if (pmField?.GetValue(_client.Client) is ParticleManager pm)
                alive = pm.ParticlesAlive(EnumParticleModel.Quad);
        }
        catch { }
        float[] wind = new float[3];
        if (data != null)
        {
            var ws2 = data.GetType().GetProperty("curWindSpeed")?.GetValue(data);
            if (ws2 is Vec3f v) { wind[0] = v.X; wind[1] = v.Y; wind[2] = v.Z; }
        }
        float prec = data != null ? Convert.ToSingle(data.GetType().GetProperty("PrecIntensity")?.GetValue(data) ?? 0f) : 0f;
        int precType = data != null ? Convert.ToInt32(data.GetType().GetProperty("nowPrecType")?.GetValue(data) ?? 0) : -1;
        int blendedPrec = data != null ? Convert.ToInt32(data.GetType().GetProperty("BlendedPrecType")?.GetValue(data) ?? 0) : -1;
        return new WeatherSnapshot(prec, precType, blendedPrec, wind, alive, _flashes.ToArray());
    });
}