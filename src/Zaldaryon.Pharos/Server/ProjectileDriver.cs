using System.Reflection;
using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Zaldaryon.Pharos.XUnit;

namespace Zaldaryon.Pharos.Server;

/// <summary>Where a projectile was at one tick.</summary>
/// <param name="Tick">The server tick the sample was taken at.</param>
/// <param name="Position">Where the projectile was.</param>
public sealed record ProjectileSample(int Tick, Vec3d Position);

/// <summary>What a projectile hit.</summary>
/// <param name="Tick">The server tick the hit happened at.</param>
/// <param name="Position">Where it hit.</param>
/// <param name="Entity">The entity it hit, or null when it hit a block.</param>
/// <param name="Block">The block it hit, or null when it hit an entity.</param>
/// <param name="Face">The face of the block it hit, when the game exposes one.</param>
/// <param name="Damage">The damage the projectile carries.</param>
/// <param name="DamageType">The kind of damage the projectile deals.</param>
/// <param name="StackCollectable">Whether the projectile's stack can be picked up.</param>
/// <param name="StackBroken">Whether the projectile's stack was broken.</param>
public sealed record ProjectileHit(
    int Tick,
    Vec3d Position,
    Entity? Entity,
    BlockPos? Block,
    BlockFacing? Face,
    float Damage,
    EnumDamageType DamageType,
    bool StackCollectable,
    bool StackBroken);

/// <summary>One projectile, its path and what it hit.</summary>
public sealed class ProjectileRecord
{
    private readonly List<ProjectileSample> _path = [];

    public ProjectileRecord(long entityId, string entityCode)
    {
        EntityId = entityId;
        EntityCode = entityCode;
    }

    /// <summary>The projectile's entity id.</summary>
    public long EntityId { get; }

    /// <summary>The projectile's code, such as <c>arrow-copper</c>.</summary>
    public string EntityCode { get; }

    /// <summary>Where the projectile was, tick by tick.</summary>
    public IReadOnlyList<ProjectileSample> Path => _path;

    /// <summary>What the projectile hit, or null when it hit nothing yet.</summary>
    public ProjectileHit? Hit { get; internal set; }

    internal void Sample(int tick, Vec3d position)
    {
        // The projectile is seen twice in a tick, once by the flight hook and once by the scan.
        if (_path.Count > 0 && _path[^1].Tick == tick) return;
        _path.Add(new ProjectileSample(tick, position));
    }
}

/// <summary>What the projectiles did while a log was open.</summary>
public sealed class ProjectileLog : IDisposable
{
    private readonly ProjectileDriver _driver;
    private readonly Dictionary<long, ProjectileRecord> _projectiles = [];
    private bool _disposed;

    internal ProjectileLog(ProjectileDriver driver, BlockPos center, int radius)
    {
        _driver = driver;
        Center = center;
        Radius = radius;
    }

    /// <summary>The middle of the region the log watches.</summary>
    public BlockPos Center { get; }

    /// <summary>How many blocks around the middle the log watches.</summary>
    public int Radius { get; }

    /// <summary>The projectiles the log saw, in the order they appeared.</summary>
    public IReadOnlyList<ProjectileRecord> Projectiles
    {
        get
        {
            lock (_projectiles) return [.. _projectiles.Values];
        }
    }

    /// <summary>The last projectile the log saw.</summary>
    public ProjectileRecord? Last
    {
        get
        {
            lock (_projectiles)
            {
                ProjectileRecord? last = null;
                foreach (ProjectileRecord record in _projectiles.Values) last = record;
                return last;
            }
        }
    }

    /// <summary>Stops recording.</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _driver.Stop(this);
    }

    internal bool Watches(Vec3d position)
    {
        BlockPos center = Center;
        return Math.Abs(position.X - center.X) <= Radius
            && Math.Abs(position.Y - center.Y) <= Radius
            && Math.Abs(position.Z - center.Z) <= Radius;
    }

    internal void Seen(Entity entity, int tick)
    {
        lock (_projectiles)
        {
            if (!_projectiles.TryGetValue(entity.EntityId, out ProjectileRecord? record))
            {
                record = new ProjectileRecord(entity.EntityId, entity.Code?.ToString() ?? "");
                _projectiles[entity.EntityId] = record;
            }

            record.Sample(tick, entity.Pos.XYZ);
        }
    }

    internal void Hit(Entity projectile, ProjectileHit hit)
    {
        lock (_projectiles)
        {
            if (!_projectiles.TryGetValue(projectile.EntityId, out ProjectileRecord? record)) return;
            record.Hit = hit;
        }
    }
}

/// <summary>The server's projectiles: how they fly, what they hit and what happened to their stack.</summary>
public sealed class ProjectileDriver
{
    private static readonly List<ProjectileLog> s_logs = [];
    private static bool s_installed;

    private readonly EmbeddedServerHost _host;

    internal ProjectileDriver(EmbeddedServerHost host)
    {
        _host = host;
        RunningServer.Host = host;
    }

    /// <summary>Records the projectiles that fly in a region, until the log is disposed.</summary>
    /// <param name="center">The middle of the region.</param>
    /// <param name="radius">How many blocks around the middle the region reaches.</param>
    public ProjectileLog Track(BlockPos center, int radius = 32)
    {
        ArgumentNullException.ThrowIfNull(center);
        ArgumentOutOfRangeException.ThrowIfNegative(radius);

        Install();
        ProjectileLog log = new(this, center, radius);
        lock (s_logs) s_logs.Add(log);
        return log;
    }

    internal void Stop(ProjectileLog log)
    {
        lock (s_logs) s_logs.Remove(log);
    }

    /// <summary>
    /// Fires the weapon in a hotbar slot at a point, the way a player does: the shooter looks at
    /// the point, the button is held for the charge and released, and the weapon spawns its
    /// projectile.
    /// </summary>
    /// <param name="shooter">The player who fires.</param>
    /// <param name="weaponSlot">The hotbar slot the weapon sits in.</param>
    /// <param name="at">Where the shot goes.</param>
    /// <param name="charge">How long the button is held, in seconds of game time.</param>
    /// <param name="ct">Cancels the wait.</param>
    /// <returns>The projectile that flew, or null when the weapon fired none.</returns>
    public async Task<ProjectileRecord?> FireAsync(
        ServerTestPlayer shooter,
        int weaponSlot,
        Vec3d at,
        float charge = 1f,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(shooter);
        ArgumentOutOfRangeException.ThrowIfNegative(weaponSlot);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(charge);

        await shooter.Bot.SelectSlotAsync(weaponSlot, ct: ct).ConfigureAwait(false);
        Aim(shooter, at);

        // The game measures the charge in the time that passed, not in ticks, so the hold lasts as
        // many ticks as the server needs to reach the charge in game time.
        int ticks = TicksFor(charge);

        BotResult result = await shooter.Bot.UseHeldAsync(ticks, at.AsBlockPos, ct: ct).ConfigureAwait(false);
        if (!result.Accepted) return null;

        Scan();
        ProjectileLog? log = null;
        lock (s_logs) log = s_logs.LastOrDefault();
        return log?.Last;
    }

    /// <summary>
    /// Waits until a projectile hits something, and returns what it hit.
    /// </summary>
    /// <param name="projectile">The projectile to wait for.</param>
    /// <param name="maxTicks">The most ticks to wait.</param>
    /// <param name="ct">Cancels the wait.</param>
    /// <exception cref="TimeoutException">Thrown when the projectile hit nothing in time.</exception>
    public async Task<ProjectileHit> WaitForImpactAsync(ProjectileRecord projectile, int maxTicks = 200, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(projectile);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxTicks);

        for (int tick = 0; tick < maxTicks; tick++)
        {
            ct.ThrowIfCancellationRequested();
            ProjectileHit? hit = projectile.Hit;
            if (hit != null) return hit;
            _host.Tick();
            Scan();
        }

        throw new TimeoutException($"The projectile hit nothing within {maxTicks} ticks.");
    }

    private void Aim(ServerTestPlayer shooter, Vec3d at)
    {
        _host.RunOnGameThread(() =>
        {
            EntityAgent? entity = shooter.Entity;
            if (entity == null) return;

            Vec3d eye = entity.Pos.XYZ.Add(0, entity.LocalEyePos.Y, 0);
            Vec3d delta = at - eye;
            double length = delta.Length();
            if (length < 0.001) return;

            entity.Pos.Pitch = (float)Math.Asin(delta.Y / length);
            entity.Pos.Yaw = (float)Math.Atan2(-delta.X, -delta.Z);
        });
    }

    private int TicksFor(float seconds)
    {
        long before = _host.Api.World.ElapsedMilliseconds;
        _host.Tick();
        long after = _host.Api.World.ElapsedMilliseconds;
        double millisecondsPerTick = Math.Max(1d, after - before);
        return Math.Max(1, (int)Math.Ceiling(seconds * 1000d / millisecondsPerTick));
    }

    private void Scan()
    {
        List<ProjectileLog> logs;
        lock (s_logs) logs = [.. s_logs];
        if (logs.Count == 0) return;

        int tick = _host.TickCount;
        _host.RunOnGameThread(() =>
        {
            foreach (ProjectileLog log in logs)
            {
                BlockPos from = log.Center.AddCopy(-log.Radius, -log.Radius, -log.Radius);
                BlockPos to = log.Center.AddCopy(log.Radius, log.Radius, log.Radius);
                foreach (Entity entity in _host.Api.World.GetEntitiesInsideCuboid(from, to))
                {
                    if (entity is not IProjectile) continue;
                    log.Seen(entity, tick);
                }
            }
        });
    }

    private static void Install()
    {
        if (s_installed) return;
        s_installed = true;

        Harmony harmony = new("zaldaryon.pharos.projectiles");

        Type? baseType = FindType("EntityProjectileBase");
        Patch(harmony, baseType, "Initialize", nameof(AfterSpawn));
        Patch(harmony, baseType, "ImpactOnEntity", nameof(AfterImpact));

        Type? arrowType = FindType("EntityProjectile");
        Patch(harmony, arrowType, "OnGameTick", nameof(AfterTick));
        Patch(harmony, arrowType, "OnCollided", nameof(AfterCollided));

        Patch(harmony, FindType("EntityThrownStone"), "OnGameTick", nameof(AfterTick));
    }

    private static void Patch(Harmony harmony, Type? type, string method, string postfix)
    {
        if (type == null) return;

        MethodInfo? target = type.GetMethod(
            method,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (target == null) return;

        harmony.Patch(target, postfix: new HarmonyMethod(typeof(ProjectileDriver), postfix));
    }

    private static Type? FindType(string name)
    {
        foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            Type? type = assembly.GetType("Vintagestory.GameContent." + name);
            if (type != null) return type;
        }

        return null;
    }

    private static void AfterSpawn(Entity __instance)
    {
        if (__instance is not IProjectile) return;

        Vec3d position = __instance.Pos.XYZ;
        lock (s_logs)
        {
            foreach (ProjectileLog log in s_logs)
            {
                if (log.Watches(position)) log.Seen(__instance, TickOf(__instance));
            }
        }
    }

    private static void AfterTick(Entity __instance)
    {
        if (__instance is not IProjectile) return;

        Vec3d position = __instance.Pos.XYZ;
        lock (s_logs)
        {
            foreach (ProjectileLog log in s_logs)
            {
                if (log.Watches(position)) log.Seen(__instance, TickOf(__instance));
            }
        }
    }

    private static void AfterCollided(Entity __instance)
    {
        if (__instance is not IProjectile projectile) return;

        Vec3d position = __instance.Pos.XYZ;
        BlockPos block = position.AsBlockPos;
        lock (s_logs)
        {
            foreach (ProjectileLog log in s_logs)
            {
                if (!log.Watches(position)) continue;
                if (log.Last?.Hit != null) continue;

                log.Hit(__instance, new ProjectileHit(
                    TickOf(__instance),
                    position,
                    null,
                    block,
                    null,
                    projectile.Damage,
                    projectile.DamageType,
                    projectile.Collectible,
                    projectile.ProjectileStack == null));
            }
        }
    }

    private static void AfterImpact(Entity __instance, Entity target)
    {
        if (__instance is not IProjectile projectile) return;

        Vec3d position = __instance.Pos.XYZ;
        lock (s_logs)
        {
            foreach (ProjectileLog log in s_logs)
            {
                if (!log.Watches(position)) continue;

                log.Hit(__instance, new ProjectileHit(
                    TickOf(__instance),
                    position,
                    target,
                    null,
                    null,
                    projectile.Damage,
                    projectile.DamageType,
                    projectile.Collectible,
                    projectile.ProjectileStack == null));
            }
        }
    }

    private static int TickOf(Entity entity) => RunningServer.TickCount;
}
