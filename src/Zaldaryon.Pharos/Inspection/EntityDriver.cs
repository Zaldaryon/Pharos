using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Zaldaryon.Pharos.Core;

namespace Zaldaryon.Pharos.Inspection;

/// <summary>An active animation on an entity.</summary>
public sealed record AnimationSnapshot(string Code, float Weight, bool Active);

/// <summary>The shape elements and textures of an entity.</summary>
public sealed record EntityShapeSnapshot(IReadOnlyList<string> Elements, IReadOnlyList<string> AttachedShapes, IReadOnlyList<string> MissingTextures);

/// <summary>
/// Reads entity render state: active animations.
/// </summary>
public sealed class EntityDriver
{
    private readonly HeadlessClient _client;

    internal EntityDriver(HeadlessClient client) => _client = client;

    /// <summary>Active animations on the client player's entity.</summary>
    public IReadOnlyList<AnimationSnapshot> Animations() => _client.RunOnClientThread(() =>
    {
        var result = new List<AnimationSnapshot>();
        try
        {
            var am = _client.Client.EntityPlayer?.AnimManager;
            if (am != null)
            {
                foreach (var kv in am.ActiveAnimationsByAnimCode)
                {
                    result.Add(new AnimationSnapshot(kv.Key, kv.Value.Weight, true));
                }
            }
        }
        catch { }
        return result;
    });

    /// <summary>Shape elements of the client player's entity (best-effort).</summary>
    public EntityShapeSnapshot Shape() => _client.RunOnClientThread(() =>
        new EntityShapeSnapshot(Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>()));
}