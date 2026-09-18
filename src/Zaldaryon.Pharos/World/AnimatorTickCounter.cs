using System.Collections.Concurrent;

namespace Zaldaryon.Pharos.World;

/// <summary>
/// Tracks animation tick counts per entity for observability of LOD-based
/// animation throttling behavior. Thread-safe for concurrent tick tracking.
/// </summary>
public sealed class AnimatorTickCounter
{
    private readonly ConcurrentDictionary<long, int> _tickCounts = new();
    private readonly object _lock = new();

    /// <summary>
    /// Gets the number of unique entities currently being tracked.
    /// </summary>
    public int TrackedEntityCount => _tickCounts.Count;

    /// <summary>
    /// Records an animation tick for the specified entity.
    /// </summary>
    /// <param name="entityId">The entity ID that received an animation tick.</param>
    public void TrackTick(long entityId)
    {
        _tickCounts.AddOrUpdate(entityId, 1, (_, count) => count + 1);
    }

    /// <summary>
    /// Records multiple animation ticks for the specified entity.
    /// </summary>
    /// <param name="entityId">The entity ID that received animation ticks.</param>
    /// <param name="tickCount">Number of ticks to record.</param>
    public void TrackTicks(long entityId, int tickCount)
    {
        if (tickCount <= 0) return;
        _tickCounts.AddOrUpdate(entityId, tickCount, (_, count) => count + tickCount);
    }

    /// <summary>
    /// Gets the tick count for a specific entity.
    /// </summary>
    /// <param name="entityId">The entity ID to query.</param>
    /// <returns>The number of ticks recorded, or 0 if the entity is not tracked.</returns>
    public int GetTickCount(long entityId)
    {
        return _tickCounts.TryGetValue(entityId, out int count) ? count : 0;
    }

    /// <summary>
    /// Gets all tick counts as an immutable dictionary.
    /// </summary>
    /// <returns>A read-only dictionary mapping entity IDs to tick counts.</returns>
    public IReadOnlyDictionary<long, int> GetAllCounts()
    {
        lock (_lock)
        {
            return new Dictionary<long, int>(_tickCounts);
        }
    }

    /// <summary>
    /// Resets all tick counts, clearing tracked entities.
    /// </summary>
    public void Reset()
    {
        _tickCounts.Clear();
    }

    /// <summary>
    /// Removes a specific entity from tracking.
    /// </summary>
    /// <param name="entityId">The entity ID to remove.</param>
    /// <returns>True if the entity was removed, false if it was not being tracked.</returns>
    public bool RemoveEntity(long entityId)
    {
        return _tickCounts.TryRemove(entityId, out _);
    }

    /// <summary>
    /// Gets the total tick count across all tracked entities.
    /// </summary>
    public int TotalTickCount
    {
        get
        {
            int total = 0;
            foreach (var kvp in _tickCounts)
            {
                total += kvp.Value;
            }
            return total;
        }
    }

    /// <summary>
    /// Gets the average tick count per entity, or 0 if no entities are tracked.
    /// </summary>
    public double AverageTickCount => TrackedEntityCount == 0 ? 0.0 : (double)TotalTickCount / TrackedEntityCount;

    /// <summary>
    /// Builds an <see cref="AnimationLodTierResult"/> from entity tick counts
    /// using the provided tier classification function.
    /// </summary>
    /// <param name="getTier">
    /// Function that maps an entity ID to its distance tier:
    /// 0 = Near, 1 = Mid, 2 = Far. Unknown tiers are ignored.
    /// </param>
    /// <returns>A tier result with aggregated tick counts per tier.</returns>
    public AnimationLodTierResult BuildTierResult(Func<long, int> getTier)
    {
        ArgumentNullException.ThrowIfNull(getTier);

        int nearTicks = 0;
        int midTicks = 0;
        int farTicks = 0;

        foreach (var kvp in _tickCounts)
        {
            int tier = getTier(kvp.Key);
            switch (tier)
            {
                case 0:
                    nearTicks += kvp.Value;
                    break;
                case 1:
                    midTicks += kvp.Value;
                    break;
                case 2:
                    farTicks += kvp.Value;
                    break;
            }
        }

        return new AnimationLodTierResult(nearTicks, midTicks, farTicks);
    }

    /// <summary>
    /// Builds an <see cref="AnimationLodTierResult"/> from pre-classified entity lists.
    /// </summary>
    /// <param name="nearEntityIds">Entity IDs classified as near distance.</param>
    /// <param name="midEntityIds">Entity IDs classified as mid distance.</param>
    /// <param name="farEntityIds">Entity IDs classified as far distance.</param>
    /// <returns>A tier result with aggregated tick counts per tier.</returns>
    public AnimationLodTierResult BuildTierResult(
        IEnumerable<long> nearEntityIds,
        IEnumerable<long> midEntityIds,
        IEnumerable<long> farEntityIds)
    {
        ArgumentNullException.ThrowIfNull(nearEntityIds);
        ArgumentNullException.ThrowIfNull(midEntityIds);
        ArgumentNullException.ThrowIfNull(farEntityIds);

        int nearTicks = SumTicksFor(nearEntityIds);
        int midTicks = SumTicksFor(midEntityIds);
        int farTicks = SumTicksFor(farEntityIds);

        return new AnimationLodTierResult(nearTicks, midTicks, farTicks);
    }

    /// <summary>
    /// Sums tick counts for the specified entity IDs.
    /// </summary>
    private int SumTicksFor(IEnumerable<long> entityIds)
    {
        int total = 0;
        foreach (long id in entityIds)
        {
            if (_tickCounts.TryGetValue(id, out int count))
            {
                total += count;
            }
        }
        return total;
    }

    /// <summary>
    /// Creates a synthetic counter pre-populated with tick data for testing.
    /// </summary>
    /// <param name="entityTickCounts">Entity ID to tick count mappings.</param>
    /// <returns>A new counter with the specified tick counts.</returns>
    public static AnimatorTickCounter CreateSynthetic(IEnumerable<(long entityId, int tickCount)> entityTickCounts)
    {
        ArgumentNullException.ThrowIfNull(entityTickCounts);

        var counter = new AnimatorTickCounter();
        foreach (var (entityId, tickCount) in entityTickCounts)
        {
            if (tickCount > 0)
            {
                counter._tickCounts[entityId] = tickCount;
            }
        }
        return counter;
    }
}
