using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.Client.NoObf;
using Zaldaryon.Pharos.Core;

namespace Zaldaryon.Pharos.Inspection;

/// <summary>A detached snapshot of one ambient modifier's weighted values.</summary>
public sealed record AmbientModifierSnapshot(
    string Code,
    float FogDensityValue, float FogDensityWeight,
    float FogMinValue, float FogMinWeight,
    float[] FogColorValue, float FogColorWeight,
    float[] AmbientColorValue, float AmbientColorWeight,
    float CloudDensityValue, float CloudDensityWeight,
    float CloudBrightnessValue, float CloudBrightnessWeight,
    float SceneBrightnessValue, float SceneBrightnessWeight,
    float FogBrightnessValue, float FogBrightnessWeight,
    float LerpSpeedValue, float LerpSpeedWeight);

/// <summary>The blended ambient values the client renders this frame.</summary>
public sealed record AmbientSnapshot(
    float[] FogColor,
    float[] AmbientColor,
    float FogDensity,
    float FogMin,
    float FlatFogDensity,
    float FlatFogYOffset,
    float CloudBrightness,
    float CloudDensity,
    float SceneBrightness,
    float FogBrightness,
    IReadOnlyDictionary<string, AmbientModifierSnapshot> Modifiers);

/// <summary>
/// Reads the client's blended ambient state: fog, ambient colour, cloud and scene brightness,
/// plus every active modifier with its weights. Values are detached and dispatched on the client thread.
/// </summary>
public sealed class AmbientDriver
{
    private readonly HeadlessClient _client;

    internal AmbientDriver(HeadlessClient client) => _client = client;

    /// <summary>Reads the current blended ambient state on the client thread.</summary>
    public AmbientSnapshot Snapshot() => _client.RunOnClientThread(() =>
    {
        IAmbientManager ambient = _client.Client.api.Ambient;
        var modifiers = new Dictionary<string, AmbientModifierSnapshot>(StringComparer.Ordinal);
        foreach (var (code, mod) in ambient.CurrentModifiers)
        {
            modifiers[code] = new AmbientModifierSnapshot(
                code,
                mod.FogDensity?.Value ?? 0f, mod.FogDensity?.Weight ?? 0f,
                mod.FogMin?.Value ?? 0f, mod.FogMin?.Weight ?? 0f,
                mod.FogColor?.Value?.ToArray() ?? new float[3], mod.FogColor?.Weight ?? 0f,
                mod.AmbientColor?.Value?.ToArray() ?? new float[3], mod.AmbientColor?.Weight ?? 0f,
                mod.CloudDensity?.Value ?? 0f, mod.CloudDensity?.Weight ?? 0f,
                mod.CloudBrightness?.Value ?? 0f, mod.CloudBrightness?.Weight ?? 0f,
                mod.SceneBrightness?.Value ?? 0f, mod.SceneBrightness?.Weight ?? 0f,
                mod.FogBrightness?.Value ?? 0f, mod.FogBrightness?.Weight ?? 0f,
                mod.LerpSpeed?.Value ?? 0f, mod.LerpSpeed?.Weight ?? 0f);
        }
        return new AmbientSnapshot(
            new[] { ambient.BlendedFogColor.X, ambient.BlendedFogColor.Y, ambient.BlendedFogColor.Z, ambient.BlendedFogColor.W },
            new[] { ambient.BlendedAmbientColor.X, ambient.BlendedAmbientColor.Y, ambient.BlendedAmbientColor.Z },
            ambient.BlendedFogDensity,
            ambient.BlendedFogMin,
            ambient.BlendedFlatFogDensity,
            ambient.BlendedFlatFogYOffset + _client.Client.World.SeaLevel,
            ambient.BlendedCloudBrightness,
            ambient.BlendedCloudDensity,
            ambient.BlendedSceneBrightness,
            ambient.BlendedFogBrightness,
            modifiers);
    });

    /// <summary>
    /// Steps frames until the blended fog density stops changing, or throws after maxFrames.
    /// </summary>
    public async Task<AmbientSnapshot> WaitForBlendAsync(int maxFrames, CancellationToken ct = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxFrames);
        AmbientSnapshot previous = Snapshot();
        for (int i = 0; i < maxFrames; i++)
        {
            await _client.StepAsync(ct).ConfigureAwait(false);
            AmbientSnapshot current = Snapshot();
            if (Math.Abs(current.FogDensity - previous.FogDensity) < 1e-6f &&
                Math.Abs(current.FogMin - previous.FogMin) < 1e-6f &&
                Math.Abs(current.FlatFogDensity - previous.FlatFogDensity) < 1e-6f &&
                Math.Abs(current.FlatFogYOffset - previous.FlatFogYOffset) < 1e-6f &&
                Math.Abs(current.CloudBrightness - previous.CloudBrightness) < 1e-6f &&
                Math.Abs(current.CloudDensity - previous.CloudDensity) < 1e-6f &&
                Math.Abs(current.SceneBrightness - previous.SceneBrightness) < 1e-6f &&
                Math.Abs(current.FogBrightness - previous.FogBrightness) < 1e-6f &&
                current.FogColor.SequenceEqual(previous.FogColor) &&
                current.AmbientColor.SequenceEqual(previous.AmbientColor))
            {
                return current;
            }
            previous = current;
        }
        throw new TimeoutException($"The ambient blend did not settle within {maxFrames} frames.");
    }
}