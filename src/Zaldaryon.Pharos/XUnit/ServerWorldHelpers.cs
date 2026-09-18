using System;
using System.Threading;
using System.Threading.Tasks;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Zaldaryon.Pharos.Server;

namespace Zaldaryon.Pharos.XUnit;

/// <summary>
/// Provides server-side world state manipulation helpers for server scenario tests.
/// </summary>
/// <remarks>
/// <para>
/// These extension methods and static helpers allow tests to manipulate server world state,
/// spawn entities, set blocks, and wait for asynchronous conditions using deterministic
/// tick-based waits rather than real-time delays.
/// </para>
/// <para>
/// All waiter methods advance simulation ticks deterministically via the <see cref="EmbeddedServerHost"/>
/// tick controller, ensuring reproducible test behavior.
/// </para>
/// </remarks>
public static class ServerWorldHelpers
{
    /// <summary>
    /// Sets a block at the specified position by block code.
    /// </summary>
    /// <param name="scenario">The server scenario base instance.</param>
    /// <param name="pos">The block position.</param>
    /// <param name="blockCode">The block code (e.g., "game:stone").</param>
    /// <exception cref="ArgumentNullException"><paramref name="scenario"/>, <paramref name="pos"/>, or <paramref name="blockCode"/> is null.</exception>
    /// <exception cref="InvalidOperationException">The server API is not available or block code is invalid.</exception>
    public static void SetBlock(this ServerScenarioBase scenario, BlockPos pos, string blockCode)
    {
        ArgumentNullException.ThrowIfNull(scenario);
        ArgumentNullException.ThrowIfNull(pos);
        ArgumentNullException.ThrowIfNull(blockCode);

        ICoreServerAPI? api = GetApi(scenario);
        if (api is null)
        {
            throw new InvalidOperationException("Server API is not available. Ensure the server is running.");
        }

        Block? block = api.World.GetBlock(new AssetLocation(blockCode));
        if (block is null)
        {
            throw new InvalidOperationException($"Block code '{blockCode}' could not be resolved.");
        }

        api.World.BlockAccessor.SetBlock(block.Id, pos);
    }

    /// <summary>
    /// Sets a block at the specified position by block ID.
    /// </summary>
    /// <param name="scenario">The server scenario base instance.</param>
    /// <param name="pos">The block position.</param>
    /// <param name="blockId">The numeric block ID.</param>
    /// <exception cref="ArgumentNullException"><paramref name="scenario"/> or <paramref name="pos"/> is null.</exception>
    /// <exception cref="InvalidOperationException">The server API is not available.</exception>
    public static void SetBlock(this ServerScenarioBase scenario, BlockPos pos, int blockId)
    {
        ArgumentNullException.ThrowIfNull(scenario);
        ArgumentNullException.ThrowIfNull(pos);

        ICoreServerAPI? api = GetApi(scenario);
        if (api is null)
        {
            throw new InvalidOperationException("Server API is not available. Ensure the server is running.");
        }

        api.World.BlockAccessor.SetBlock(blockId, pos);
    }

    /// <summary>
    /// Gets the block at the specified position.
    /// </summary>
    /// <param name="scenario">The server scenario base instance.</param>
    /// <param name="pos">The block position.</param>
    /// <returns>The block at the position, or the air block if not loaded.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="scenario"/> or <paramref name="pos"/> is null.</exception>
    /// <exception cref="InvalidOperationException">The server API is not available.</exception>
    public static Block GetBlock(this ServerScenarioBase scenario, BlockPos pos)
    {
        ArgumentNullException.ThrowIfNull(scenario);
        ArgumentNullException.ThrowIfNull(pos);

        ICoreServerAPI? api = GetApi(scenario);
        if (api is null)
        {
            throw new InvalidOperationException("Server API is not available. Ensure the server is running.");
        }

        return api.World.BlockAccessor.GetBlock(pos);
    }

    /// <summary>
    /// Gets the block entity at the specified position.
    /// </summary>
    /// <param name="scenario">The server scenario base instance.</param>
    /// <param name="pos">The block position.</param>
    /// <returns>The block entity at the position, or null if none exists.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="scenario"/> or <paramref name="pos"/> is null.</exception>
    /// <exception cref="InvalidOperationException">The server API is not available.</exception>
    public static BlockEntity? GetBlockEntity(this ServerScenarioBase scenario, BlockPos pos)
    {
        ArgumentNullException.ThrowIfNull(scenario);
        ArgumentNullException.ThrowIfNull(pos);

        ICoreServerAPI? api = GetApi(scenario);
        if (api is null)
        {
            throw new InvalidOperationException("Server API is not available. Ensure the server is running.");
        }

        return api.World.BlockAccessor.GetBlockEntity(pos);
    }

    /// <summary>
    /// Spawns an entity at the specified position.
    /// </summary>
    /// <param name="scenario">The server scenario base instance.</param>
    /// <param name="entityCode">The entity code (e.g., "game:drifter").</param>
    /// <param name="pos">The spawn position.</param>
    /// <returns>The spawned entity.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="scenario"/>, <paramref name="entityCode"/>, or <paramref name="pos"/> is null.</exception>
    /// <exception cref="InvalidOperationException">The server API is not available or entity code is invalid.</exception>
    public static Entity SpawnEntity(this ServerScenarioBase scenario, string entityCode, Vec3d pos)
    {
        ArgumentNullException.ThrowIfNull(scenario);
        ArgumentNullException.ThrowIfNull(entityCode);
        ArgumentNullException.ThrowIfNull(pos);

        ICoreServerAPI? api = GetApi(scenario);
        if (api is null)
        {
            throw new InvalidOperationException("Server API is not available. Ensure the server is running.");
        }

        EntityProperties? entityType = api.World.GetEntityType(new AssetLocation(entityCode));
        if (entityType is null)
        {
            throw new InvalidOperationException($"Entity code '{entityCode}' could not be resolved.");
        }

        Entity entity = api.World.ClassRegistry.CreateEntity(entityType);
        entity.Pos.SetPos(pos);

        api.World.SpawnEntity(entity);
        return entity;
    }

    /// <summary>
    /// Waits until the specified chunk is loaded, advancing ticks deterministically.
    /// </summary>
    /// <param name="scenario">The server scenario base instance.</param>
    /// <param name="cx">The chunk X coordinate.</param>
    /// <param name="cy">The chunk Y coordinate.</param>
    /// <param name="cz">The chunk Z coordinate.</param>
    /// <param name="maxTicks">Maximum ticks to wait before timing out. Default is 300 (5 seconds at 60 TPS).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task that completes when the chunk is loaded, or throws <see cref="TimeoutException"/> if maxTicks is exceeded.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="scenario"/> is null.</exception>
    /// <exception cref="InvalidOperationException">The server host is not available.</exception>
    /// <exception cref="TimeoutException">The chunk was not loaded within the specified tick limit.</exception>
    public static async Task WaitForChunkLoadedAsync(
        this ServerScenarioBase scenario,
        int cx, int cy, int cz,
        int maxTicks = 300,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(scenario);

        EmbeddedServerHost? host = GetHost(scenario);
        if (host is null)
        {
            throw new InvalidOperationException("Server host is not available. Ensure the server is running.");
        }

        ICoreServerAPI? api = GetApi(scenario);
        if (api is null)
        {
            throw new InvalidOperationException("Server API is not available.");
        }

        bool IsChunkLoaded() => api.World.BlockAccessor.GetChunk(cx, cy, cz) is not null;

        bool success = await host.TickUntilAsync(IsChunkLoaded, maxTicks, ct).ConfigureAwait(false);
        if (!success)
        {
            throw new TimeoutException($"Chunk ({cx}, {cy}, {cz}) was not loaded within {maxTicks} ticks.");
        }
    }

    /// <summary>
    /// Waits until an entity with the specified code spawns within the given radius.
    /// </summary>
    /// <param name="scenario">The server scenario base instance.</param>
    /// <param name="entityCode">The entity code to search for.</param>
    /// <param name="radius">The search radius from the center position.</param>
    /// <param name="aroundPos">The center position to search around.</param>
    /// <param name="maxTicks">Maximum ticks to wait before timing out. Default is 300 (5 seconds at 60 TPS).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The first matching entity found.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="scenario"/>, <paramref name="entityCode"/>, or <paramref name="aroundPos"/> is null.</exception>
    /// <exception cref="InvalidOperationException">The server host is not available.</exception>
    /// <exception cref="TimeoutException">No matching entity was found within the specified tick limit.</exception>
    public static async Task<Entity> WaitForEntitySpawnAsync(
        this ServerScenarioBase scenario,
        string entityCode,
        double radius,
        Vec3d aroundPos,
        int maxTicks = 300,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(scenario);
        ArgumentNullException.ThrowIfNull(entityCode);
        ArgumentNullException.ThrowIfNull(aroundPos);

        EmbeddedServerHost? host = GetHost(scenario);
        if (host is null)
        {
            throw new InvalidOperationException("Server host is not available. Ensure the server is running.");
        }

        ICoreServerAPI? api = GetApi(scenario);
        if (api is null)
        {
            throw new InvalidOperationException("Server API is not available.");
        }

        AssetLocation targetCode = new(entityCode);
        Entity? foundEntity = null;

        bool FindEntity()
        {
            Entity[] entities = api.World.GetEntitiesAround(aroundPos, (float)radius, (float)radius);
            foreach (Entity entity in entities)
            {
                if (entity.Code?.Equals(targetCode) == true)
                {
                    foundEntity = entity;
                    return true;
                }
            }
            return false;
        }

        bool success = await host.TickUntilAsync(FindEntity, maxTicks, ct).ConfigureAwait(false);
        if (!success || foundEntity is null)
        {
            throw new TimeoutException($"Entity '{entityCode}' was not found within radius {radius} of {aroundPos} after {maxTicks} ticks.");
        }

        return foundEntity;
    }

    /// <summary>
    /// Executes a trigger action and waits until the specified condition predicate returns true.
    /// </summary>
    /// <param name="scenario">The server scenario base instance.</param>
    /// <param name="trigger">The action to execute that should cause the condition to become true.</param>
    /// <param name="condition">The predicate to check after each tick.</param>
    /// <param name="maxTicks">Maximum ticks to wait before timing out. Default is 300 (5 seconds at 60 TPS).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A task that completes when the condition is met.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="scenario"/>, <paramref name="trigger"/>, or <paramref name="condition"/> is null.</exception>
    /// <exception cref="InvalidOperationException">The server host is not available.</exception>
    /// <exception cref="TimeoutException">The condition was not met within the specified tick limit.</exception>
    /// <remarks>
    /// This method provides a general-purpose mechanism to wait for any condition after triggering an action.
    /// The trigger is executed once, then ticks advance until the condition returns true.
    /// </remarks>
    public static async Task WaitForConditionAsync(
        this ServerScenarioBase scenario,
        Action trigger,
        Func<bool> condition,
        int maxTicks = 300,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(scenario);
        ArgumentNullException.ThrowIfNull(trigger);
        ArgumentNullException.ThrowIfNull(condition);

        EmbeddedServerHost? host = GetHost(scenario);
        if (host is null)
        {
            throw new InvalidOperationException("Server host is not available. Ensure the server is running.");
        }

        // Execute the trigger
        trigger();

        // Wait for the condition
        bool success = await host.TickUntilAsync(condition, maxTicks, ct).ConfigureAwait(false);
        if (!success)
        {
            throw new TimeoutException($"Condition was not met within {maxTicks} ticks.");
        }
    }

    /// <summary>
    /// Advances the server by exactly one tick.
    /// </summary>
    /// <param name="scenario">The server scenario base instance.</param>
    /// <exception cref="ArgumentNullException"><paramref name="scenario"/> is null.</exception>
    /// <exception cref="InvalidOperationException">The server host is not available.</exception>
    public static void Tick(this ServerScenarioBase scenario)
    {
        ArgumentNullException.ThrowIfNull(scenario);

        EmbeddedServerHost? host = GetHost(scenario);
        if (host is null)
        {
            throw new InvalidOperationException("Server host is not available. Ensure the server is running.");
        }

        host.Tick();
    }

    /// <summary>
    /// Advances the server by the specified number of ticks.
    /// </summary>
    /// <param name="scenario">The server scenario base instance.</param>
    /// <param name="count">The number of ticks to advance.</param>
    /// <exception cref="ArgumentNullException"><paramref name="scenario"/> is null.</exception>
    /// <exception cref="InvalidOperationException">The server host is not available.</exception>
    public static void Ticks(this ServerScenarioBase scenario, int count)
    {
        ArgumentNullException.ThrowIfNull(scenario);

        EmbeddedServerHost? host = GetHost(scenario);
        if (host is null)
        {
            throw new InvalidOperationException("Server host is not available. Ensure the server is running.");
        }

        host.Ticks(count);
    }

    /// <summary>
    /// Advances the server until the predicate returns true or maxTicks is reached.
    /// </summary>
    /// <param name="scenario">The server scenario base instance.</param>
    /// <param name="predicate">The condition to wait for.</param>
    /// <param name="maxTicks">Maximum ticks to wait.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>True if the predicate was satisfied; false if maxTicks was reached.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="scenario"/> or <paramref name="predicate"/> is null.</exception>
    /// <exception cref="InvalidOperationException">The server host is not available.</exception>
    public static Task<bool> TickUntilAsync(
        this ServerScenarioBase scenario,
        Func<bool> predicate,
        int maxTicks = 300,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(scenario);
        ArgumentNullException.ThrowIfNull(predicate);

        EmbeddedServerHost? host = GetHost(scenario);
        if (host is null)
        {
            throw new InvalidOperationException("Server host is not available. Ensure the server is running.");
        }

        return host.TickUntilAsync(predicate, maxTicks, ct);
    }

    /// <summary>
    /// Gets the block ID for a block code.
    /// </summary>
    /// <param name="scenario">The server scenario base instance.</param>
    /// <param name="blockCode">The block code.</param>
    /// <returns>The block ID, or 0 if not found.</returns>
    public static int GetBlockId(this ServerScenarioBase scenario, string blockCode)
    {
        ArgumentNullException.ThrowIfNull(scenario);
        ArgumentNullException.ThrowIfNull(blockCode);

        ICoreServerAPI? api = GetApi(scenario);
        if (api is null) return 0;

        Block? block = api.World.GetBlock(new AssetLocation(blockCode));
        return block?.Id ?? 0;
    }

    /// <summary>
    /// Gets the entity ID for an entity code.
    /// </summary>
    /// <param name="scenario">The server scenario base instance.</param>
    /// <param name="entityCode">The entity code.</param>
    /// <returns>The entity type ID, or 0 if not found.</returns>
    public static int GetEntityId(this ServerScenarioBase scenario, string entityCode)
    {
        ArgumentNullException.ThrowIfNull(scenario);
        ArgumentNullException.ThrowIfNull(entityCode);

        ICoreServerAPI? api = GetApi(scenario);
        if (api is null) return 0;

        EntityProperties? entityType = api.World.GetEntityType(new AssetLocation(entityCode));
        return entityType?.Id ?? 0;
    }

    // Helper to get API from scenario via reflection (since it's protected)
    private static ICoreServerAPI? GetApi(ServerScenarioBase scenario)
    {
        // Use reflection to access the protected Api property
        var prop = typeof(ServerScenarioBase).GetProperty(
            "Api",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

        return prop?.GetValue(scenario) as ICoreServerAPI;
    }

    // Helper to get Host from scenario via reflection (since it's protected)
    private static EmbeddedServerHost? GetHost(ServerScenarioBase scenario)
    {
        // Use reflection to access the protected Host property
        var prop = typeof(ServerScenarioBase).GetProperty(
            "Host",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

        return prop?.GetValue(scenario) as EmbeddedServerHost;
    }
}
