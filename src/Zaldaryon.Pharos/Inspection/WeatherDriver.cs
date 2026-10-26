using System.Reflection;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.Client.NoObf;
using Zaldaryon.Pharos.Core;

namespace Zaldaryon.Pharos.Inspection;

/// <summary>A lightning flash seen by the client, with its position and the frame it appeared.</summary>
public sealed record LightningFlashSnapshot(float X, float Y, float Z, long Frame);

/// <summary>A snapshot of the client's weather.</summary>
/// <param name="PrecipitationIntensity">The current particle-render precipitation intensity at the player's altitude.</param>
/// <param name="PrecipitationType">The effective game EnumPrecipitationType value, with Auto resolved using current temperature.</param>
/// <param name="BlendedPrecipitationType">The game EnumPrecipitationType value before resolving Auto.</param>
/// <param name="WindSpeed">A detached copy of the wind vector at the player's position, in X, Y, Z order.</param>
/// <param name="WeatherParticlesAlive">The alive particle count.</param>
/// <param name="LightningFlashes">Observed lightning flashes.</param>
public sealed record WeatherSnapshot(
    float PrecipitationIntensity,
    int PrecipitationType,
    int BlendedPrecipitationType,
    float[] WindSpeed,
    float CloudCoverage,
    int WeatherParticlesAlive,
    IReadOnlyList<LightningFlashSnapshot> LightningFlashes);

/// <summary>
/// Reads the client's weather render state on the client thread.
/// </summary>
public sealed class ClientWeatherDriver
{
    private readonly HeadlessClient _client;
    private readonly List<LightningFlashSnapshot> _flashes = new();
    private long _lastFlashFrame;

    internal ClientWeatherDriver(HeadlessClient client) => _client = client;

    /// <summary>Reads current precipitation and wind from the inputs used by the particle renderer.</summary>
    /// <remarks>
    /// Requires an engine-mode client joined to a world with initialized weather. The intensity
    /// uses current climate rainfall with the game's high-altitude attenuation, not the obsolete
    /// WeatherDataSnapshot.PrecIntensity field. Auto precipitation resolves to snow below the
    /// blended snow threshold and rain otherwise; explicit rain, snow and hail are unchanged.
    /// Wind comes from the block accessor at the player's position. No world or mod objects are
    /// retained across calls, including when the client reconnects.
    /// </remarks>
    /// <exception cref="ObjectDisposedException">The client has been disposed.</exception>
    /// <exception cref="InvalidOperationException">The client or its weather is not ready, or weather inputs are not finite.</exception>
    /// <exception cref="NotSupportedException">The installed game does not provide the required weather contract.</exception>
    public WeatherSnapshot Snapshot()
    {
        ObjectDisposedException.ThrowIf(_client.IsDisposed, _client);
        if (!_client.IsEngineMode)
            throw new InvalidOperationException("Weather inspection requires an engine-mode client joined to a world.");

        return _client.RunOnClientThread(() =>
        {
            ObjectDisposedException.ThrowIf(_client.IsDisposed, _client);
            if (!_client.IsJoined)
                throw new InvalidOperationException("Weather inspection requires a joined client. Join a world before reading weather.");

            ClientMain game = _client.Client;
            ModSystem weather = game.api.ModLoader.GetModSystem("Vintagestory.GameContent.WeatherSystemClient")
                ?? throw new NotSupportedException("The installed game has no WeatherSystemClient. Check Pharos/game compatibility.");
            if (!RequiredField<bool>(weather, "haveLevelFinalize") || !RequiredField<bool>(weather, "playerChunkLoaded"))
                throw new InvalidOperationException("Client weather is not initialized. Join a world and step frames before reading weather.");
            ClimateCondition climate = RequiredField<ClimateCondition>(weather, "clientClimateCond");

            PropertyInfo? property = weather.GetType().GetProperty("BlendedWeatherData", BindingFlags.Public | BindingFlags.Instance);
            if (property?.GetMethod is not { IsStatic: false, IsPublic: true } || property.GetIndexParameters().Length != 0 ||
                property.PropertyType.FullName != "Vintagestory.GameContent.WeatherDataSnapshot")
                throw new NotSupportedException("The installed game changed WeatherSystemClient.BlendedWeatherData. Check Pharos/game compatibility.");
            object data;
            try
            {
                data = property.GetValue(weather)
                    ?? throw new InvalidOperationException("Client BlendedWeatherData is not initialized. Step frames before reading weather.");
            }
            catch (TargetInvocationException e)
            {
                throw new InvalidOperationException("Cannot read client BlendedWeatherData.", e.InnerException ?? e);
            }

            // This enum lives in VSEssentials, not the API assembly referenced by Pharos.
            FieldInfo? typeField = data.GetType().GetField("BlendedPrecType", BindingFlags.Public | BindingFlags.Instance);
            if (typeField?.FieldType is not { IsEnum: true, FullName: "Vintagestory.GameContent.EnumPrecipitationType" } enumType ||
                Enum.GetUnderlyingType(enumType) != typeof(int))
                throw new NotSupportedException("The installed game changed weather member BlendedPrecType. Check Pharos/game compatibility.");
            object blendedType = typeField.GetValue(data)!;
            string? blendedName = Enum.GetName(enumType, blendedType);
            if (blendedName is not ("Auto" or "Rain" or "Snow" or "Hail"))
                throw new NotSupportedException($"The installed game returned unsupported precipitation type {blendedType}. Check Pharos/game compatibility.");
            float snowThreshold = RequiredField<float>(data, "snowThresholdTemp");
            if (!float.IsFinite(climate.Rainfall) || !float.IsFinite(climate.Temperature) || !float.IsFinite(snowThreshold))
                throw new InvalidOperationException("Client precipitation inputs are not finite.");

            var position = game.EntityPlayer.Pos;
            Vec3d windAtPlayer = game.World.BlockAccessor.GetWindSpeedAt(position.XYZ);
            float[] wind = [(float)windAtPlayer.X, (float)windAtPlayer.Y, (float)windAtPlayer.Z];
            if (wind.Any(component => !float.IsFinite(component)))
                throw new InvalidOperationException("Client wind inputs are not finite.");
            float intensity = Math.Max(0, climate.Rainfall - (float)Math.Max(0, (position.Y - game.World.SeaLevel - 5000) / 10000f));
            object effectiveType = blendedType;
            if (blendedName == "Auto")
            {
                if (!Enum.TryParse(enumType, climate.Temperature < snowThreshold ? "Snow" : "Rain", out object? resolvedType) || resolvedType == null)
                    throw new NotSupportedException("The installed game changed automatic precipitation types. Check Pharos/game compatibility.");
                effectiveType = resolvedType;
            }

            int alive = 0;
            try
            {
                var pmField = typeof(ClientMain).GetField("particleManager", BindingFlags.Instance | BindingFlags.NonPublic);
                if (pmField?.GetValue(game) is ParticleManager pm)
                    alive = pm.ParticlesAlive(EnumParticleModel.Quad);
            }
            catch { }

            object? simLightning = weather.GetType().GetField("simLightning", BindingFlags.Public | BindingFlags.Instance)?.GetValue(weather);
            List<object>? flashes = simLightning?.GetType().GetField("lightningFlashes", BindingFlags.Public | BindingFlags.Instance)?.GetValue(simLightning) as List<object>;
            if (flashes != null)
            {
                foreach (object flash in flashes)
                {
                    FieldInfo? originField = flash.GetType().GetField("origin", BindingFlags.Public | BindingFlags.Instance);
                    if (originField?.GetValue(flash) is Vec3d origin &&
                        !_flashes.Any(existing => existing.X == (float)origin.X && existing.Y == (float)origin.Y && existing.Z == (float)origin.Z))
                    {
                        _flashes.Add(new LightningFlashSnapshot((float)origin.X, (float)origin.Y, (float)origin.Z, _client.FrameController.TotalFrames));
                    }
                }
            }
            float cloudCoverage = game.AmbientManager.BlendedCloudDensity;
            return new WeatherSnapshot(intensity, Convert.ToInt32(effectiveType), Convert.ToInt32(blendedType), wind, cloudCoverage, alive, _flashes.ToArray());
        });
    }

    internal static T RequiredField<T>(object source, string name)
    {
        FieldInfo? field = source.GetType().GetField(name, BindingFlags.Public | BindingFlags.Instance);
        if (field == null || field.FieldType != typeof(T))
            throw new NotSupportedException($"The installed game changed weather member '{source.GetType().FullName}.{name}' (expected {typeof(T).FullName}). Check Pharos/game compatibility.");
        return field.GetValue(source) is T value ? value
            : throw new InvalidOperationException($"Client weather member '{name}' is not initialized. Step frames before reading weather.");
    }
}
