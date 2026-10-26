using System.Reflection;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.Client.NoObf;
using Zaldaryon.Pharos.Core;

namespace Zaldaryon.Pharos.Audio;

/// <summary>A snapshot of one ambient sound source (water, wind, fire, etc.).</summary>
public sealed record AmbientSoundSnapshot(
    string AssetLocation,
    int QuantityNearbyBlocks,
    float Volume,
    float AdjustedVolume,
    EnumSoundType SoundType,
    IReadOnlyList<(int X1, int Y1, int Z1, int X2, int Y2, int Z2)> BoundingBoxes);

/// <summary>A snapshot of all active ambient sounds near the player.</summary>
public sealed record AmbientSoundsSnapshot(IReadOnlyList<AmbientSoundSnapshot> Sounds);

/// <summary>
/// Reads the client's ambient sounds (water, wind, fire, etc.) that play based on nearby blocks.
/// Values are detached and dispatched on the client thread.
/// </summary>
public sealed class AmbientSoundDriver
{
    private static readonly FieldInfo? s_currentAmbientSoundsBySection = typeof(SystemClientTickingBlocks)
        .GetField("currentAmbientSoundsBySection", BindingFlags.Instance | BindingFlags.NonPublic);

    private readonly HeadlessClient _client;

    internal AmbientSoundDriver(HeadlessClient client) => _client = client;

    /// <summary>Reads the current ambient sounds near the player on the client thread.</summary>
    public AmbientSoundsSnapshot Snapshot() => _client.RunOnClientThread(() =>
    {
        // SystemClientTickingBlocks is a ClientSystem, not ModSystem, so use reflection
        var systems = _client.Client.GetType()
            .GetField("clientSystems", BindingFlags.Instance | BindingFlags.NonPublic)?
            .GetValue(_client.Client) as IEnumerable<object>;
        
        var tickingBlocks = systems?.FirstOrDefault(s => s.GetType().Name == "SystemClientTickingBlocks") as SystemClientTickingBlocks;
        if (tickingBlocks == null || s_currentAmbientSoundsBySection == null)
        {
            return new AmbientSoundsSnapshot([]);
        }

        var sectionMap = s_currentAmbientSoundsBySection.GetValue(tickingBlocks)
            as Dictionary<FastVec3i, Dictionary<AssetLocation, AmbientSound>>;
        if (sectionMap == null)
        {
            return new AmbientSoundsSnapshot([]);
        }

        var sounds = new List<AmbientSoundSnapshot>();
        foreach (var section in sectionMap.Values)
        {
            foreach (var ambient in section.Values)
            {
                var boxes = ambient.BoundingBoxes
                    .Select(b => (b.X1, b.Y1, b.Z1, b.X2, b.Y2, b.Z2))
                    .ToList();

                sounds.Add(new AmbientSoundSnapshot(
                    ambient.AssetLoc.ToString(),
                    ambient.QuantityNearbyBlocks,
                    ambient.VolumeMul,
                    ambient.AdjustedVolume,
                    ambient.SoundType,
                    boxes));
            }
        }

        return new AmbientSoundsSnapshot(sounds);
    });

    /// <summary>Whether an ambient sound with the given asset location fragment is playing.</summary>
    public bool IsPlaying(string locationFragment)
    {
        var snapshot = Snapshot();
        return snapshot.Sounds.Any(s =>
            s.AssetLocation.Contains(locationFragment, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Gets the total number of active ambient sounds.</summary>
    public int Count => Snapshot().Sounds.Count;
}
