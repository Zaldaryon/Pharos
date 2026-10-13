using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.Server;
using Vintagestory.API.Util;
using Vintagestory.Server;

namespace Zaldaryon.Pharos.Server;

/// <summary>
/// A connected player's entity state and player data beyond position and inventories: the entity's
/// attribute trees and stats, what vanilla behaviours keep outside them, the spawn point and the
/// other fields of its world data, its mod data and its runtime privileges.
/// </summary>
/// <remarks>
/// Everything is held as bytes or plain values, and a restore builds fresh objects from them, so
/// nothing of the snapshot ends up in live state.
/// </remarks>
internal sealed class PlayerEntityState
{
    /// <summary>
    /// Watched keys the tree restore leaves alone. <c>positionVersionNumber</c> and
    /// <c>onHurtCounter</c> count up for the life of the entity and must never go back: the server
    /// drops client position packets older than the position version, and the client plays a hurt
    /// only when the counter changes. <c>stats</c> is rewritten from the restored stats instead.
    /// </summary>
    private static readonly HashSet<string> s_liveWatchedKeys = ["positionVersionNumber", "onHurtCounter", "stats"];

    /// <summary>Attribute keys the restore leaves alone: the client drops position packets whose tick is not above the last one.</summary>
    private static readonly HashSet<string> s_liveAttributeKeys = ["tick", "tickDiff"];

    private byte[] _watched = [];
    private byte[] _attributes = [];
    private bool _alive;
    private StatCategory[] _stats = [];
    private KeyValuePair<string, float>[]? _maxHealthModifiers;
    private byte[]? _spawn;
    private WorldFields _fields = null!;
    private Dictionary<string, byte[]> _modData = [];

    /// <summary>The player's runtime privileges, which the save never holds.</summary>
    public string[] RuntimePrivileges { get; private set; } = [];

    /// <summary>Captures <paramref name="player"/>. Runs on the game thread.</summary>
    public static PlayerEntityState From(IServerPlayer player)
    {
        EntityPlayer entity = player.Entity;

        // What Entity.ToBytes does before it writes the trees: behaviours flush what they keep
        // outside them, such as the health behaviour's damage over time.
        foreach (EntityBehavior behavior in entity.SidedProperties?.Behaviors ?? [])
        {
            behavior.ToBytes(false);
        }

        ServerWorldPlayerData data = (ServerWorldPlayerData)player.WorldData;
        return new PlayerEntityState
        {
            _watched = entity.WatchedAttributes.ToBytes(),
            _attributes = entity.Attributes.ToBytes(),
            _alive = entity.Alive,
            _stats = entity.Stats.Select(s => StatCategory.From(s.Key, s.Value)).ToArray(),
            _maxHealthModifiers = HealthInternals.MaxHealthModifiers(entity)?.ToArray(),
            _spawn = data.SpawnPosition is { } spawn ? SerializerUtil.Serialize(spawn) : null,
            _fields = WorldFields.From(data, player),
            _modData = StateRestore.Copy(data.ModData),
            RuntimePrivileges = player is ServerPlayer { serverdata: { } serverData } ? [.. PlayerDataInternals.RuntimePrivileges(serverData)] : [],
        };
    }

    /// <summary>
    /// Puts the player's entity state and world data back. Runs on the game thread. Returns whether
    /// its player data changed, so the caller sends it to the clients.
    /// </summary>
    public bool RestoreInto(IServerPlayer player, WorldRestoreFilter filter)
    {
        EntityPlayer entity = player.Entity;

        // The vanilla respawn: alive again, with what behaviours do on a revive. The trees are
        // written over what it set.
        if (_alive && !entity.Alive) entity.Revive();

        TreeAttribute watched = TreeAttribute.CreateFromBytes(_watched);
        StateRestore.TreeInPlace(entity.WatchedAttributes, watched, key => !s_liveWatchedKeys.Contains(key) && filter.RestoresPlayerAttribute(key));
        TreeAttribute attributes = TreeAttribute.CreateFromBytes(_attributes);
        StateRestore.TreeInPlace(entity.Attributes, attributes, key => !s_liveAttributeKeys.Contains(key) && filter.RestoresPlayerAttribute(key));

        if (filter.RestoresPlayerAttribute("stats")) RestoreStats(entity);
        HealthInternals.Restore(entity, _maxHealthModifiers, filter.RestoresPlayerAttribute("health"), filter.RestoresPlayerAttribute(HealthInternals.EffectsKey));
        BreatheInternals.Refresh(entity);

        entity.Alive = _alive;

        // What a load does: every modified listener runs once, and the whole tree goes to the
        // clients with the next physics tick.
        foreach (TreeModifiedListener listener in entity.WatchedAttributes.OnModified.ToList())
        {
            listener?.listener?.Invoke();
        }

        entity.WatchedAttributes.MarkAllDirty();

        return RestoreWorldData(player, filter);
    }

    private void RestoreStats(EntityPlayer entity)
    {
        IDictionary<string, EntityFloatStats> live = StatsInternals.FloatStats(entity.Stats);
        HashSet<string> saved = _stats.Select(s => s.Category).ToHashSet();
        foreach (string category in live.Keys.ToList())
        {
            if (!saved.Contains(category)) live.Remove(category);
        }

        foreach (StatCategory category in _stats)
        {
            if (!live.TryGetValue(category.Category, out EntityFloatStats? stats) || stats == null)
            {
                live[category.Category] = stats = new EntityFloatStats();
            }

            category.RestoreInto(stats);
        }

        // The tree copy the clients read, with every value, as EntityStats.Set writes it.
        if (live.Count > 0) entity.Stats.ToTreeAttributes(entity.WatchedAttributes, forClient: true);
        else entity.WatchedAttributes.RemoveAttribute("stats");
    }

    private bool RestoreWorldData(IServerPlayer player, WorldRestoreFilter filter)
    {
        ServerWorldPlayerData data = (ServerWorldPlayerData)player.WorldData;

        byte[]? spawnNow = data.SpawnPosition is { } spawn ? SerializerUtil.Serialize(spawn) : null;
        bool changed = (spawnNow == null) != (_spawn == null) || (spawnNow != null && !spawnNow.AsSpan().SequenceEqual(_spawn));
        if (changed) data.SpawnPosition = _spawn == null ? null : SerializerUtil.Deserialize<PlayerSpawnPos>(_spawn);

        changed |= _fields.RestoreInto(data, player);

        data.ModData ??= [];
        StateRestore.ModDataInPlace(data.ModData, _modData, filter.RestoresModData);
        return changed;
    }

    /// <summary>The fields of a player's world data that its player data packet carries, and a few more.</summary>
    private sealed record WorldFields(
        float MoveSpeed, float PickingRange, float PreviousPickingRange, bool FreeMove, bool NoClip, int Deaths,
        bool RenderMetaBlocks, int HotbarSlot, EnumFreeMovAxisLock PlaneLock, bool AreaSelection, bool DidSelectSkin)
    {
        public static WorldFields From(ServerWorldPlayerData data, IServerPlayer player) => new(
            data.MoveSpeedMultiplier, data.PickingRange, data.PreviousPickingRange, data.FreeMove, data.NoClip, data.Deaths,
            data.RenderMetaBlocks, player.InventoryManager.ActiveHotbarSlotNumber, data.FreeMovePlaneLock, data.AreaSelectionMode, data.DidSelectSkin);

        /// <summary>Writes the fields back and returns whether any changed.</summary>
        public bool RestoreInto(ServerWorldPlayerData data, IServerPlayer player)
        {
            if (From(data, player) == this) return false;

            data.MoveSpeedMultiplier = MoveSpeed;
            data.PickingRange = PickingRange;
            data.PreviousPickingRange = PreviousPickingRange;
            data.FreeMove = FreeMove;
            ((IWorldPlayerData)data).NoClip = NoClip;
            data.Deaths = Deaths;
            data.RenderMetaBlocks = RenderMetaBlocks;
            player.InventoryManager.ActiveHotbarSlotNumber = HotbarSlot;
            data.FreeMovePlaneLock = PlaneLock;
            data.AreaSelectionMode = AreaSelection;
            data.DidSelectSkin = DidSelectSkin;
            return true;
        }
    }

    /// <summary>One stat category exactly: its blend type and every value, persistent or not.</summary>
    private sealed record StatCategory(string Category, EnumStatBlendType BlendType, (string Code, float Value, float Weight, bool Persistent)[] Values)
    {
        public static StatCategory From(string category, EntityFloatStats stats) => new(
            category,
            stats.BlendType,
            stats.ValuesByKey.Select(v => (v.Key, v.Value.Value, v.Value.Weight, v.Value.Persistent)).ToArray());

        public void RestoreInto(EntityFloatStats stats)
        {
            stats.BlendType = BlendType;
            stats.ValuesByKey.Clear();
            foreach ((string code, float value, float weight, bool persistent) in Values)
            {
                stats.ValuesByKey[code] = new EntityStat<float> { Value = value, Weight = weight, Persistent = persistent };
            }
        }
    }

    private static class StatsInternals
    {
        [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "floatStats")]
        public static extern ref IDictionary<string, EntityFloatStats> FloatStats(EntityStats stats);
    }

    /// <summary>The server data fields the game keeps internal.</summary>
    internal static class PlayerDataInternals
    {
        [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "RuntimePrivileges")]
        public static extern ref HashSet<string> RuntimePrivileges(ServerPlayerData data);
    }

    /// <summary>
    /// What the essentials mod's health behaviour keeps outside the tree: its max health modifiers
    /// and its damage over time effects. Reached by reflection, as Pharos does not reference the mod.
    /// </summary>
    private static class HealthInternals
    {
        private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

        public static Dictionary<string, float>? MaxHealthModifiers(Entity entity) =>
            entity.GetBehavior("health") is { } health ? Field(health.GetType())?.GetValue(health) as Dictionary<string, float> : null;

        /// <summary>The watched key the behaviour keeps its damage over time effects under.</summary>
        public const string EffectsKey = "damageovertime-activeeffects";

        /// <summary>
        /// Puts back the modifiers when the <c>health</c> key is restored, and reloads the effects
        /// from the restored tree when their key is.
        /// </summary>
        public static void Restore(Entity entity, KeyValuePair<string, float>[]? modifiers, bool restoreModifiers, bool restoreEffects)
        {
            if (entity.GetBehavior("health") is not { } health) return;

            if (restoreModifiers) Field(health.GetType())?.SetValue(health, modifiers == null ? null : new Dictionary<string, float>(modifiers));
            if (!restoreEffects) return;

            byte[]? effects = entity.WatchedAttributes.GetBytes(EffectsKey);
            if (effects != null && health.GetType().GetMethod("ActiveDoTEffectsFromBytes", Instance) is { } load)
            {
                load.Invoke(health, [effects]);
            }
            else if (health.GetType().GetProperty("ActiveDoTEffects", Instance)?.GetValue(health) is IList list)
            {
                list.Clear();
            }
        }

        private static FieldInfo? Field(Type? type)
        {
            for (; type != null; type = type.BaseType)
            {
                if (type.GetField("maxHealthModifiers", Instance | BindingFlags.DeclaredOnly) is { } field) return field;
            }

            return null;
        }
    }

    /// <summary>The breathe behaviour caches the oxygen it last wrote; reading it refreshes the cache.</summary>
    private static class BreatheInternals
    {
        public static void Refresh(Entity entity)
        {
            if (entity.GetBehavior("breathe") is { } breathe) _ = breathe.GetType().GetProperty("Oxygen", BindingFlags.Instance | BindingFlags.Public)?.GetValue(breathe);
        }
    }
}
