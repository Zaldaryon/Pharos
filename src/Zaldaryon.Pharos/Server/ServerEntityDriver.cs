using System.Collections;
using System.Reflection;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Zaldaryon.Pharos.Events;

namespace Zaldaryon.Pharos.Server;

/// <summary>Controls and inspects entities in an embedded server.</summary>
public sealed class ServerEntityDriver
{
    private const string AiBehaviorCode = "taskai";
    private const string PathfindingSystemName = "Vintagestory.Essentials.PathfindingAsync";
    private readonly EmbeddedServerHost _host;

    internal ServerEntityDriver(EmbeddedServerHost host) => _host = host;

    /// <summary>Creates and spawns an entity on the server game thread.</summary>
    /// <param name="entityCode">The entity code, such as <c>game:chicken-hen</c>.</param>
    /// <param name="pos">The spawn position.</param>
    /// <param name="configure">An optional action applied after creation and before spawning.</param>
    public Task<Entity> SpawnAsync(string entityCode, BlockPos pos, Action<Entity>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(pos);
        return SpawnAsync(entityCode, pos.ToVec3d(), configure);
    }

    /// <summary>Creates and spawns an entity on the server game thread.</summary>
    /// <param name="entityCode">The entity code, such as <c>game:chicken-hen</c>.</param>
    /// <param name="pos">The spawn position.</param>
    /// <param name="configure">An optional action applied after creation and before spawning.</param>
    /// <exception cref="ArgumentException"><paramref name="entityCode"/> is empty or whitespace.</exception>
    /// <exception cref="InvalidOperationException">The entity code is unknown or the server API is unavailable.</exception>
    public Task<Entity> SpawnAsync(string entityCode, Vec3d pos, Action<Entity>? configure = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(entityCode);
        ArgumentNullException.ThrowIfNull(pos);

        Entity entity = _host.RunOnGameThread(() =>
        {
            ICoreServerAPI api = ServerApi;
            EntityProperties properties = api.World.GetEntityType(new AssetLocation(entityCode))
                ?? throw new InvalidOperationException($"Entity code '{entityCode}' could not be resolved.");
            Entity created = api.World.ClassRegistry.CreateEntity(properties);
            created.Pos.SetPos(pos);
            configure?.Invoke(created);
            api.World.SpawnEntity(created);
            return created;
        });

        return Task.FromResult(entity);
    }

    /// <summary>Waits for the server to record the entity's death.</summary>
    /// <param name="entity">The entity to observe.</param>
    /// <param name="maxTicks">Maximum server ticks to wait.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <exception cref="InvalidOperationException">The entity had already died before recording began.</exception>
    /// <exception cref="TimeoutException">The entity did not die within the tick limit.</exception>
    public async Task<EntityDied> WaitForDeathAsync(Entity entity, int maxTicks = 300, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(entity);
        using ServerEventLog log = _host.Events.Record(ServerEvents.EntityDied);
        EntityDied? recorded = log.EntityDied.FirstOrDefault(e => e.EntityId == entity.EntityId);
        if (recorded is not null) return recorded;

        if (!_host.RunOnGameThread(() => entity.Alive))
        {
            recorded = log.EntityDied.FirstOrDefault(e => e.EntityId == entity.EntityId);
            if (recorded is not null) return recorded;
            throw new InvalidOperationException($"Entity {entity.EntityId} was already dead before the death wait started.");
        }

        return await log.WaitForAsync<EntityDied>(e => e.EntityId == entity.EntityId, maxTicks: maxTicks, ct: ct).ConfigureAwait(false);
    }

    /// <summary>Waits for the server to record the entity leaving the world.</summary>
    /// <param name="entity">The entity to observe.</param>
    /// <param name="maxTicks">Maximum server ticks to wait.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <exception cref="TimeoutException">The entity did not despawn within the tick limit.</exception>
    public async Task<EntityDespawned> WaitForDespawnAsync(Entity entity, int maxTicks = 300, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(entity);
        using ServerEventLog log = _host.Events.Record(ServerEvents.EntityDespawned);
        EntityDespawned? recorded = log.EntityDespawned.FirstOrDefault(e => e.EntityId == entity.EntityId);
        if (recorded is not null) return recorded;

        return await log.WaitForAsync<EntityDespawned>(e => e.EntityId == entity.EntityId, maxTicks: maxTicks, ct: ct).ConfigureAwait(false);
    }

    /// <summary>Waits until the entity's server position is within a radius of a target.</summary>
    /// <param name="entity">The entity to observe.</param>
    /// <param name="pos">The target position.</param>
    /// <param name="radius">The inclusive distance from the target.</param>
    /// <param name="maxTicks">Maximum server ticks to wait.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="radius"/> is negative or not finite.</exception>
    /// <exception cref="TimeoutException">The entity did not reach the target within the tick limit.</exception>
    public async Task WaitUntilAtAsync(Entity entity, Vec3d pos, double radius, int maxTicks = 300, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(entity);
        ArgumentNullException.ThrowIfNull(pos);
        if (!double.IsFinite(radius) || radius < 0) throw new ArgumentOutOfRangeException(nameof(radius), radius, "Radius must be finite and non-negative.");

        bool reached = await _host.TickUntilAsync(
            () => _host.RunOnGameThread(() => entity.Pos.XYZ.DistanceTo(pos) <= radius), maxTicks, ct).ConfigureAwait(false);
        if (!reached)
        {
            Vec3d current = _host.RunOnGameThread(() => entity.Pos.XYZ);
            throw new TimeoutException($"Entity {entity.EntityId} stayed {current.DistanceTo(pos):F2} blocks from {pos} after {maxTicks} ticks.");
        }
    }

    /// <summary>Returns the identifiers of AI tasks active on the entity's task AI behavior.</summary>
    /// <exception cref="InvalidOperationException">The entity has no task AI behavior or its installed game API differs.</exception>
    public IReadOnlyList<string> ActiveTasks(Entity entity)
    {
        ArgumentNullException.ThrowIfNull(entity);
        return _host.RunOnGameThread(() =>
        {
            object manager = GetTaskManager(entity);
            object? active = RequiredProperty(manager.GetType(), "ActiveTasksBySlot").GetValue(manager);
            if (active is not IEnumerable tasks) throw ApiMismatch("ActiveTasksBySlot is not enumerable");

            List<string> ids = [];
            foreach (object? task in tasks)
            {
                if (task is null) continue;
                ids.Add(TaskIdentity(task));
            }
            return (IReadOnlyList<string>)ids.AsReadOnly();
        });
    }

    /// <summary>Starts a configured AI task on the server game thread when its game predicate allows it.</summary>
    /// <param name="entity">The entity whose task manager owns the task.</param>
    /// <param name="code">The task identifier configured on the entity.</param>
    /// <exception cref="InvalidOperationException">The entity has no task AI behavior, the task is missing or ambiguous, or a readiness or execution callback refuses it.</exception>
    public void StartTask(Entity entity, string code)
    {
        ArgumentNullException.ThrowIfNull(entity);
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        _host.RunOnGameThread(() =>
        {
            object manager = GetTaskManager(entity);
            object task = FindUniqueTask(manager, code, entity.EntityId);
            bool canExecute = (bool)(RequiredMethod(task.GetType(), "ShouldExecute").Invoke(task, null)
                ?? throw ApiMismatch($"{task.GetType().FullName}.ShouldExecute returned null"));
            if (!canExecute)
            {
                throw new InvalidOperationException($"AI task '{code}' is not ready to execute on entity {entity.EntityId}.");
            }
            PropertyInfo slot = RequiredProperty(task.GetType(), "Slot");
            int taskSlot = (int)(slot.GetValue(task) ?? throw ApiMismatch($"{task.GetType().FullName}.Slot is null"));
            MethodInfo execute = manager.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public)
                .FirstOrDefault(method => method.Name == "ExecuteTask"
                    && method.GetParameters() is [{ } first, { ParameterType: var second }]
                    && first.ParameterType.IsInstanceOfType(task)
                    && second == typeof(int))
                ?? throw ApiMismatch($"{manager.GetType().FullName}.ExecuteTask(task, slot) is unavailable");
            execute.Invoke(manager, [task, taskSlot]);

            object? active = RequiredProperty(manager.GetType(), "ActiveTasksBySlot").GetValue(manager);
            if (active is not IEnumerable activeTasks) throw ApiMismatch("ActiveTasksBySlot is not enumerable");
            int index = 0;
            bool started = false;
            foreach (object? activeTask in activeTasks)
            {
                if (index++ == taskSlot && ReferenceEquals(activeTask, task))
                {
                    started = true;
                    break;
                }
            }
            if (!started)
                throw new InvalidOperationException($"AI task '{code}' was rejected by the game's task execution callbacks on entity {entity.EntityId}.");
        });
    }

    /// <summary>Stops an active configured AI task on the server game thread.</summary>
    /// <param name="entity">The entity whose task manager owns the task.</param>
    /// <param name="code">The task identifier configured on the entity.</param>
    /// <exception cref="InvalidOperationException">The entity has no task AI behavior or the task is not configured.</exception>
    public void StopTask(Entity entity, string code)
    {
        ArgumentNullException.ThrowIfNull(entity);
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        _host.RunOnGameThread(() =>
        {
            object manager = GetTaskManager(entity);
            object? active = RequiredProperty(manager.GetType(), "ActiveTasksBySlot").GetValue(manager);
            if (active is not IEnumerable tasks) throw ApiMismatch("ActiveTasksBySlot is not enumerable");

            int slot = 0;
            int matchingSlot = -1;
            foreach (object? task in tasks)
            {
                if (task is not null && TaskMatches(task, code))
                {
                    if (matchingSlot >= 0) throw AmbiguousTask(code, entity.EntityId);
                    matchingSlot = slot;
                }
                slot++;
            }
            if (matchingSlot < 0) throw new InvalidOperationException($"AI task '{code}' is not active on entity {entity.EntityId}.");
            RequiredMethod(manager.GetType(), "StopTask", typeof(int)).Invoke(manager, [matchingSlot]);
        });
    }

    /// <summary>Asks Vintage Story's asynchronous pathfinder for a route between two positions.</summary>
    /// <param name="from">The starting position.</param>
    /// <param name="to">The target position.</param>
    /// <param name="entityCode">The entity type whose collision box the pathfinder uses.</param>
    /// <param name="maxTicks">Maximum server ticks to wait for the game pathfinder.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The game's waypoints, or a result with a reason when no route was found.</returns>
    /// <exception cref="InvalidOperationException">The entity type or game's pathfinding system is unavailable.</exception>
    /// <exception cref="TimeoutException">The game's pathfinding worker did not finish within the tick limit.</exception>
    public async Task<PathfindResult> PathfindAsync(Vec3d from, Vec3d to, string entityCode, int maxTicks = 600, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(from);
        ArgumentNullException.ThrowIfNull(to);
        ArgumentException.ThrowIfNullOrWhiteSpace(entityCode);

        PathfinderTask? request = _host.RunOnGameThread(() =>
        {
            ICoreServerAPI api = ServerApi;
            EntityProperties properties = api.World.GetEntityType(new AssetLocation(entityCode))
                ?? throw new InvalidOperationException($"Entity code '{entityCode}' could not be resolved.");
            object system = api.ModLoader.GetModSystem(PathfindingSystemName)
                ?? throw new InvalidOperationException($"Vintage Story pathfinding system '{PathfindingSystemName}' is not loaded.");
            if (from.AsBlockPos.Equals(to.AsBlockPos)) return null;
            EnumAICreatureType creatureType = ResolvePathfinderCreatureType(properties);
            MethodInfo enqueue = RequiredMethod(system.GetType(), "EnqueuePathfinderTask", typeof(PathfinderTask));
            PathfinderTask task = new(
                from.AsBlockPos,
                to.AsBlockPos,
                modeMinFleeDistance: 0,
                maxFallHeight: 3,
                stepHeight: 0.6f,
                collisionBox: properties.SpawnCollisionBox,
                searchDepth: 9999,
                creatureType: creatureType);
            enqueue.Invoke(system, [task]);
            return task;
        });

        if (request is null)
            return new PathfindResult(Array.AsReadOnly(new[] { to.Clone() }), null);

        bool finished = await _host.TickUntilAsync(() => request.Finished, maxTicks, ct).ConfigureAwait(false);
        if (!finished) throw new TimeoutException($"Pathfinding from {from} to {to} did not finish within {maxTicks} ticks.");

        Vec3d[] waypoints = request.waypoints?.Select(point => point.Clone()).ToArray() ?? [];
        return waypoints.Length == 0
            ? new PathfindResult(Array.Empty<Vec3d>(), "The game pathfinder found no route.")
            : new PathfindResult(Array.AsReadOnly(waypoints), null);
    }

    /// <summary>Checks local runtime spawn conditions at one position.</summary>
    /// <remarks>
    /// This evaluates the entity's configured runtime light, climate, forestation, Y range,
    /// surface requirement, allowed inside block, support block, and aquatic habitat size. The
    /// climate check uses the game's sea-level reference altitude. It does not run a natural
    /// spawn round, chance roll, group cap, companion selection, collision search, or
    /// player-distance check.
    /// </remarks>
    /// <param name="entityCode">The entity type code.</param>
    /// <param name="pos">The position to inspect.</param>
    /// <exception cref="InvalidOperationException">The entity type has no runtime spawn conditions.</exception>
    public SpawnCheck CanSpawnAt(string entityCode, BlockPos pos)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(entityCode);
        ArgumentNullException.ThrowIfNull(pos);
        return _host.RunOnGameThread(() =>
        {
            ICoreServerAPI api = ServerApi;
            EntityProperties properties = api.World.GetEntityType(new AssetLocation(entityCode))
                ?? throw new InvalidOperationException($"Entity code '{entityCode}' could not be resolved.");
            RuntimeSpawnConditions conditions = properties.Server?.SpawnConditions?.Runtime
                ?? throw new InvalidOperationException($"Entity '{entityCode}' has no runtime spawn conditions.");
            IBlockAccessor blocks = api.World.BlockAccessor;
            int mapSizeY = _host.Server.WorldMap.MapSizeY;
            BlockPos spawn = pos.AddCopy(0, 0, 0);

            if (spawn.dimension != 0) return SpawnCheck.Reject("Natural entity spawning only runs in dimension 0.");
            if (spawn.Y <= 0 || spawn.Y >= mapSizeY) return SpawnCheck.Reject("The Y position is outside the world.");

            int light = blocks.GetLightLevel(spawn, conditions.LightLevelType);
            if (light < conditions.MinLightLevel || light > conditions.MaxLightLevel)
                return SpawnCheck.Reject($"Light level {light} is outside the configured range {conditions.MinLightLevel} to {conditions.MaxLightLevel}.");

            if (conditions.TryOnlySurface)
            {
                ushort[]? heightMap = _host.Server.WorldMap.GetMapChunk(spawn.X >> 5, spawn.Z >> 5)?.WorldGenTerrainHeightMap;
                if (heightMap is null || heightMap.Length != 32 * 32)
                    return SpawnCheck.Reject("Terrain height is not available for this position.");

                int surfaceY = heightMap[(spawn.Z & 31) * 32 + (spawn.X & 31)] + 1;
                if (spawn.Y != surfaceY)
                    return SpawnCheck.Reject($"The entity requires the surface at Y={surfaceY}, but the position is Y={spawn.Y}.");
            }

            double normalizedY = spawn.Y > api.World.SeaLevel
                ? 1.0 + (double)(spawn.Y - api.World.SeaLevel) / (mapSizeY - api.World.SeaLevel)
                : (double)spawn.Y / api.World.SeaLevel;
            if (conditions.MinY > normalizedY || conditions.MaxY < normalizedY)
                return SpawnCheck.Reject($"The Y position {normalizedY:F2} is outside the configured range {conditions.MinY:F2} to {conditions.MaxY:F2}.");

            BlockPos climateSample = GetClimateSamplePosition(spawn, api.World.SeaLevel);
            ClimateCondition climate = blocks.GetClimateAt(climateSample, conditions.ClimateValueMode, _host.Calendar.TotalDays);
            ClimateCondition localClimate = blocks.GetClimateAt(spawn, conditions.ClimateValueMode, _host.Calendar.TotalDays);
            if (!conditions.MatchesClimate(climate)) return SpawnCheck.Reject("The position's temperature or rainfall is outside the configured range.");
            if (!conditions.MatchesForestation(localClimate)) return SpawnCheck.Reject("The position's forest or shrub density is outside the configured range.");

            Block inside = blocks.GetBlock(spawn);
            if (!conditions.CanSpawnInside(inside)) return SpawnCheck.Reject($"The entity cannot spawn inside block '{inside.Code}'.");

            BlockPos groundPos = spawn.AddCopy(0, -1, 0);
            Block ground = blocks.GetBlock(groundPos, 1);
            if (!ground.CanCreatureSpawnOn(blocks, groundPos, properties, conditions))
                return SpawnCheck.Reject($"Block '{ground.Code}' does not allow this entity to spawn on it.");

            BlockPos? missingHabitat = FindUnsuitableHabitatPosition(
                properties.Habitat,
                conditions.HabitatSize,
                spawn,
                candidate => conditions.CanSpawnInside(blocks.GetBlock(candidate)));
            if (missingHabitat is not null)
            {
                Block habitatBlock = blocks.GetBlock(missingHabitat);
                return SpawnCheck.Reject($"The entity requires suitable habitat at {missingHabitat}, but found block '{habitatBlock.Code}'.");
            }

            return SpawnCheck.Allow();
        });
    }

    private ICoreServerAPI ServerApi => _host.Server.Api as ICoreServerAPI
        ?? throw new InvalidOperationException("Server API is not available.");

    private static object GetTaskManager(Entity entity)
    {
        object behavior = entity.GetBehavior(AiBehaviorCode)
            ?? throw new InvalidOperationException($"Entity {entity.EntityId} has no '{AiBehaviorCode}' behavior.");
        FieldInfo? field = behavior.GetType().GetField("TaskManager", BindingFlags.Instance | BindingFlags.Public);
        return field?.GetValue(behavior)
            ?? throw ApiMismatch($"{behavior.GetType().FullName}.TaskManager is unavailable");
    }

    private static object FindUniqueTask(object manager, string id, long entityId)
    {
        object? all = RequiredProperty(manager.GetType(), "AllTasks").GetValue(manager);
        if (all is not IEnumerable tasks) throw ApiMismatch("AllTasks is not enumerable");

        List<object> matches = [];
        foreach (object? task in tasks)
        {
            if (task is not null && TaskMatches(task, id)) matches.Add(task);
        }
        return matches.Count switch
        {
            1 => matches[0],
            0 => throw new InvalidOperationException($"AI task '{id}' is not configured on entity {entityId}."),
            _ => throw AmbiguousTask(id, entityId)
        };
    }

    private static InvalidOperationException AmbiguousTask(string code, long entityId) =>
        new($"AI task '{code}' identifies multiple tasks on entity {entityId}. Add unique 'id' values to the entity's task configuration.");

    private static bool TaskMatches(object task, string id) =>
        string.Equals(ReadTaskId(task), id, StringComparison.Ordinal)
        || string.Equals(TaskCode(task), id, StringComparison.Ordinal);

    private static string TaskIdentity(object task) => ReadTaskId(task) ?? TaskCode(task);

    private static string? ReadTaskId(object task)
    {
        PropertyInfo? property = task.GetType().GetProperty("Id", BindingFlags.Instance | BindingFlags.Public)
            ?? task.GetType().GetInterfaces()
                .Select(type => type.GetProperty("Id"))
                .FirstOrDefault(candidate => candidate is not null);
        if (property is null) throw ApiMismatch($"AI task {task.GetType().FullName} has no Id property");
        return property.GetValue(task) as string;
    }

    internal static string TaskCode(object task)
    {
        Type? registry = AppDomain.CurrentDomain.GetAssemblies()
            .Select(assembly => assembly.GetType("Vintagestory.GameContent.AiTaskRegistry", throwOnError: false))
            .FirstOrDefault(type => type is not null);
        FieldInfo? codesField = registry?.GetField("TaskCodes", BindingFlags.Static | BindingFlags.Public);
        if (codesField?.GetValue(null) is IDictionary codes && codes[task.GetType()] is string code) return code;
        return task.GetType().Name;
    }

    internal static EnumAICreatureType ResolvePathfinderCreatureType(EntityProperties properties)
    {
        ArgumentNullException.ThrowIfNull(properties);
        JsonObject? taskAi = properties.Server?.BehaviorsAsJsonObj?
            .FirstOrDefault(behavior => behavior["code"].AsString() == AiBehaviorCode);
        string configured = taskAi?["aiCreatureType"].AsString("Default") ?? "Default";
        return Enum.TryParse(configured, out EnumAICreatureType creatureType)
            ? creatureType
            : EnumAICreatureType.Default;
    }

    internal static BlockPos GetClimateSamplePosition(BlockPos spawn, int seaLevel) =>
        spawn.AddCopy(0, (int)(seaLevel * 1.09) - spawn.Y, 0);

    internal static BlockPos? FindUnsuitableHabitatPosition(
        EnumHabitat habitat,
        int habitatSize,
        BlockPos spawn,
        System.Func<BlockPos, bool> canSpawnInside)
    {
        ArgumentNullException.ThrowIfNull(spawn);
        ArgumentNullException.ThrowIfNull(canSpawnInside);
        if (habitatSize <= 0 || habitat is not (EnumHabitat.Underwater or EnumHabitat.Sea)) return null;

        BlockPos[] candidates =
        [
            spawn.AddCopy(0, 0, -habitatSize),
            spawn.AddCopy(habitatSize, 0, 0),
            spawn.AddCopy(-habitatSize, 0, 0),
            spawn.AddCopy(0, 0, habitatSize)
        ];
        return candidates.FirstOrDefault(candidate => !canSpawnInside(candidate));
    }

    private static PropertyInfo RequiredProperty(Type type, string name) =>
        type.GetProperty(name, BindingFlags.Instance | BindingFlags.Public)
        ?? throw ApiMismatch($"{type.FullName}.{name} is unavailable");

    private static MethodInfo RequiredMethod(Type type, string name, params Type[] parameters) =>
        type.GetMethod(name, BindingFlags.Instance | BindingFlags.Public, binder: null, types: parameters, modifiers: null)
        ?? throw ApiMismatch($"{type.FullName}.{name} is unavailable");

    private static InvalidOperationException ApiMismatch(string detail) =>
        new($"The installed Vintage Story entity API changed: {detail}.");
}

/// <summary>The result of asking the game's pathfinder for a route.</summary>
/// <param name="Waypoints">The route returned by the game, if one was found.</param>
/// <param name="Reason">Why no route was returned, or null when one was found.</param>
public sealed record PathfindResult(IReadOnlyList<Vec3d> Waypoints, string? Reason)
{
    /// <summary>Whether the game returned at least one waypoint.</summary>
    public bool Found => Waypoints.Count > 0;
}

/// <summary>The result of checking local runtime spawn conditions at one position.</summary>
/// <param name="Allowed">Whether the evaluated conditions allow this position.</param>
/// <param name="Reason">The first failed condition, or null when all evaluated conditions pass.</param>
public sealed record SpawnCheck(bool Allowed, string? Reason)
{
    internal static SpawnCheck Allow() => new(true, null);
    internal static SpawnCheck Reject(string reason) => new(false, reason);
}
