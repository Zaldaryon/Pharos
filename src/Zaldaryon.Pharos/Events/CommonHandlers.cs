using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;

namespace Zaldaryon.Pharos.Events;

/// <summary>The handlers for the events both sides have, on <see cref="IEventAPI"/>.</summary>
/// <remarks>
/// Each handler is one delegate instance kept for the log's life, so removing it finds it: the
/// client keeps some of these events in lists, which remove by equality.
/// </remarks>
internal sealed class CommonHandlers
{
    private readonly EntityDelegate _spawned;
    private readonly EntityDeathDelegate _died;
    private readonly EntityDespawnDelegate _despawned;
    private readonly EntityMountDelegate _mounted;
    private readonly EntityMountDelegate _unmounted;
    private readonly MapRegionLoadedDelegate _region;

    public CommonHandlers(GameEventLog log)
    {
        _spawned = entity => log.Add(() => new EntitySpawned(entity));
        _died = (entity, source) => log.Add(() => new EntityDied(entity, source));
        _despawned = (entity, reason) => log.Add(() => new EntityDespawned(entity, reason));
        _mounted = (entity, seat) => log.Add(() => new EntityMounted(entity, seat));
        _unmounted = (entity, seat) => log.Add(() => new EntityUnmounted(entity, seat));
        _region = (coord, region) => log.Add(() => new MapRegionLoaded(coord.X, coord.Y, region));
    }

    /// <summary>Which of the events are listened to.</summary>
    public readonly record struct Selection(bool Spawned, bool Died, bool Despawned, bool Mounted, bool Unmounted, bool Region);

    public void Subscribe(IEventAPI api, Selection on)
    {
        if (on.Spawned) api.OnEntitySpawn += _spawned;
        if (on.Died) api.OnEntityDeath += _died;
        if (on.Despawned) api.OnEntityDespawn += _despawned;
        if (on.Mounted) api.EntityMounted += _mounted;
        if (on.Unmounted) api.EntityUnmounted += _unmounted;
        if (on.Region) api.MapRegionLoaded += _region;
    }

    public void Unsubscribe(IEventAPI api, Selection on)
    {
        if (on.Spawned) api.OnEntitySpawn -= _spawned;
        if (on.Died) api.OnEntityDeath -= _died;
        if (on.Despawned) api.OnEntityDespawn -= _despawned;
        if (on.Mounted) api.EntityMounted -= _mounted;
        if (on.Unmounted) api.EntityUnmounted -= _unmounted;
        if (on.Region) api.MapRegionLoaded -= _region;
    }
}
