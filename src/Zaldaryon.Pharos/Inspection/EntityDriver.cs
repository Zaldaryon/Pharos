using System.Reflection;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Zaldaryon.Pharos.Core;

namespace Zaldaryon.Pharos.Inspection;

/// <summary>An active animation on an entity.</summary>
public sealed record AnimationSnapshot(string Code, float Weight, float AnimationSpeed, bool Active);

/// <summary>An attached shape element (clothing, gear, etc.).</summary>
public sealed record AttachedShapeSnapshot(string Code, string AttachmentPoint);

/// <summary>The shape elements and textures of an entity.</summary>
public sealed record EntityShapeSnapshot(
    IReadOnlyList<string> Elements,
    IReadOnlyList<AttachedShapeSnapshot> AttachedShapes,
    IReadOnlyList<string> MissingTextures,
    string? CompositeShapeCode);

/// <summary>
/// Reads entity render state: active animations, shapes, attachments and textures.
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
                    var anim = kv.Value;
                    result.Add(new AnimationSnapshot(
                        kv.Key,
                        anim.Weight,
                        anim.AnimationSpeed,
                        true));
                }
            }
        }
        catch { }
        return result;
    });

    /// <summary>Shape elements of the specified entity.</summary>
    public EntityShapeSnapshot Shape(long entityId) => _client.RunOnClientThread(() =>
    {
        var entity = _client.Client.api.World.GetEntityById(entityId);
        if (entity == null)
            return new EntityShapeSnapshot([], [], [], null);

        var elements = new List<string>();
        var attached = new List<AttachedShapeSnapshot>();
        var missing = new List<string>();
        string? compositeCode = null;

        try
        {
            var props = entity.Properties;
            compositeCode = props?.Client?.Shape?.Base?.ToString();

            // Get shape elements from entity properties
            if (props?.Client?.Shape is { } shape)
            {
                // The shape is a CompositeShape, not a loaded Shape with elements
                // We can only report the base path here
            }

            // Check for gear/clothing attachments via reflection on EntityPlayer/EntityAgent
            if (entity is EntityAgent agent)
            {
                var gearInvField = agent.GetType().GetField("GearInventory", BindingFlags.Public | BindingFlags.Instance);
                if (gearInvField?.GetValue(agent) is IInventory gearInventory)
                {
                    foreach (var slot in gearInventory)
                    {
                        if (slot?.Itemstack != null)
                        {
                            var code = slot.Itemstack.Item?.Code?.ToString() ?? slot.Itemstack.Block?.Code?.ToString();
                            if (!string.IsNullOrEmpty(code))
                            {
                                attached.Add(new AttachedShapeSnapshot(code, "gear"));
                            }
                        }
                    }
                }
            }
        }
        catch { }

        return new EntityShapeSnapshot(elements, attached, missing, compositeCode);
    });

    /// <summary>Shape elements of the client player's entity.</summary>
    public EntityShapeSnapshot Shape() => _client.RunOnClientThread(() =>
    {
        var playerId = _client.Client.EntityPlayer?.EntityId ?? 0;
        if (playerId <= 0)
            return new EntityShapeSnapshot([], [], [], null);

        var entity = _client.Client.api.World.GetEntityById(playerId);
        if (entity == null)
            return new EntityShapeSnapshot([], [], [], null);

        var elements = new List<string>();
        var attached = new List<AttachedShapeSnapshot>();
        var missing = new List<string>();
        string? compositeCode = entity.Properties?.Client?.Shape?.Base?.ToString();

        try
        {
            // Check for gear/clothing attachments
            if (entity is EntityAgent agent)
            {
                var gearInvField = agent.GetType().GetField("GearInventory", BindingFlags.Public | BindingFlags.Instance);
                if (gearInvField?.GetValue(agent) is IInventory gearInventory)
                {
                    foreach (var slot in gearInventory)
                    {
                        if (slot?.Itemstack != null)
                        {
                            var code = slot.Itemstack.Item?.Code?.ToString() ?? slot.Itemstack.Block?.Code?.ToString();
                            if (!string.IsNullOrEmpty(code))
                            {
                                attached.Add(new AttachedShapeSnapshot(code, "gear"));
                            }
                        }
                    }
                }
            }
        }
        catch { }

        return new EntityShapeSnapshot(elements, attached, missing, compositeCode);
    });

    /// <summary>Gets animations for a specific entity by ID.</summary>
    public IReadOnlyList<AnimationSnapshot> Animations(long entityId) => _client.RunOnClientThread(() =>
    {
        var result = new List<AnimationSnapshot>();
        try
        {
            var entity = _client.Client.api.World.GetEntityById(entityId);
            if (entity is EntityAgent agent)
            {
                var am = agent.AnimManager;
                if (am != null)
                {
                    foreach (var kv in am.ActiveAnimationsByAnimCode)
                    {
                        var anim = kv.Value;
                        result.Add(new AnimationSnapshot(
                            kv.Key,
                            anim.Weight,
                            anim.AnimationSpeed,
                            true));
                    }
                }
            }
        }
        catch { }
        return result;
    });
}