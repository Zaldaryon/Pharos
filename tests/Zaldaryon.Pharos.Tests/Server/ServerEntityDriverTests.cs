using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;
using Xunit;
using Zaldaryon.Pharos.Events;
using Zaldaryon.Pharos.Server;
using Zaldaryon.Pharos.XUnit;

namespace Zaldaryon.Pharos.Tests.Server;

[Collection("Sequential")]
[ServerWorld(seed: 4242, playStyle: "creativebuilding", worldType: "superflat")]
public class ServerEntityDriverTests : ServerScenarioBase
{
    [ServerScenario]
    public async Task SpawnAsync_ConfiguresTheEntityOnTheGameThread()
    {
        ServerTestPlayer player = await CreateTestPlayerAsync("EntityDriver");
        Vec3d pos = player.Entity!.Pos.XYZ.AddCopy(2, 0, 0);
        bool configuredOnGameThread = false;

        Entity hen = await Host!.Entities.SpawnAsync("game:chicken-hen", pos, entity =>
        {
            configuredOnGameThread = Host.IsOnGameThread;
            entity.WatchedAttributes.SetString("pharos-test", "configured");
        });

        Assert.True(configuredOnGameThread);
        Assert.Equal("configured", Host.RunOnGameThread(() => hen.WatchedAttributes.GetString("pharos-test")));
        Assert.Same(hen, Host.RunOnGameThread(() => Api!.World.GetEntityById(hen.EntityId)));
    }

    [ServerScenario]
    public async Task DeathAndDespawnWaits_ReturnTheMatchingLifecycleEvents()
    {
        ServerTestPlayer player = await CreateTestPlayerAsync("EntityEvents");
        Vec3d near = player.Entity!.Pos.XYZ.AddCopy(2, 0, 0);
        Entity hen = await Host!.Entities.SpawnAsync("game:chicken-hen", near);
        using VirtualClock clock = Host.UseVirtualClock(tickMs: 50);

        long deathListener = 0;
        Host.RunOnGameThread(() => deathListener = Api!.Event.RegisterGameTickListener(_ =>
        {
            Api.Event.UnregisterGameTickListener(deathListener);
            hen.Die(EnumDespawnReason.Death, new DamageSource { Source = EnumDamageSource.Void });
        }, 50));

        EntityDied died = await Host.Entities.WaitForDeathAsync(hen, maxTicks: 20);
        Assert.Equal(hen.EntityId, died.EntityId);
        Assert.Equal(EnumDamageSource.Void, died.Source?.Source);

        Entity removed = await Host.Entities.SpawnAsync("game:chicken-hen", near.AddCopy(3, 0, 0));
        long despawnListener = 0;
        Host.RunOnGameThread(() => despawnListener = Api!.Event.RegisterGameTickListener(_ =>
        {
            Api.Event.UnregisterGameTickListener(despawnListener);
            removed.Die(EnumDespawnReason.Removed);
        }, 50));

        EntityDespawned despawned = await Host.Entities.WaitForDespawnAsync(removed, maxTicks: 20);
        Assert.Equal(removed.EntityId, despawned.EntityId);
        Assert.Equal(EnumDespawnReason.Removed, despawned.Reason?.Reason);
    }

    [ServerScenario]
    public async Task WaitUntilAtAsync_ObservesPositionChangesOnTheGameThread()
    {
        ServerTestPlayer player = await CreateTestPlayerAsync("EntityPosition");
        Vec3d start = player.Entity!.Pos.XYZ.AddCopy(2, 0, 0);
        Entity hen = await Host!.Entities.SpawnAsync("game:chicken-hen", start);
        Vec3d target = start.AddCopy(4, 0, 0);
        using VirtualClock clock = Host.UseVirtualClock(tickMs: 50);

        long listener = 0;
        Host.RunOnGameThread(() => listener = Api!.Event.RegisterGameTickListener(_ =>
        {
            Api.Event.UnregisterGameTickListener(listener);
            hen.Pos.SetPos(target);
        }, 50));

        await Host.Entities.WaitUntilAtAsync(hen, target, radius: 0.01, maxTicks: 20);
        Assert.True(Host.RunOnGameThread(() => hen.Pos.XYZ.DistanceTo(target) <= 0.01));
    }

    [ServerScenario]
    public async Task AiTasks_CanBeInspectedStartedAndStopped()
    {
        ServerTestPlayer player = await CreateTestPlayerAsync("EntityAi");
        Entity rooster = await Host!.Entities.SpawnAsync("game:chicken-rooster", player.Entity!.Pos.XYZ.AddCopy(2, 0, 0));
        using VirtualClock clock = Host.UseVirtualClock(tickMs: 50);
        clock.Advance(TimeSpan.FromMilliseconds(500));

        Assert.Throws<InvalidOperationException>(() => Host.Entities.StartTask(rooster, "seekentity"));

        Host.Entities.StartTask(rooster, "lookaround");
        Assert.Contains("lookaround", Host.Entities.ActiveTasks(rooster));

        Host.Entities.StopTask(rooster, "lookaround");
        Assert.DoesNotContain("lookaround", Host.Entities.ActiveTasks(rooster));
        Assert.Throws<InvalidOperationException>(() => Host.Entities.StartTask(rooster, "task-that-is-not-configured"));
    }

    [ServerScenario]
    public async Task AiTasks_UseRegisteredCodesForTasksFromOtherAssemblies()
    {
        ServerTestPlayer player = await CreateTestPlayerAsync("EntityCustomAi");
        Entity rooster = await Host!.Entities.SpawnAsync("game:chicken-rooster", player.Entity!.Pos.XYZ.AddCopy(2, 0, 0));
        EntityBehaviorTaskAI behavior = Assert.IsType<EntityBehaviorTaskAI>(rooster.GetBehavior("taskai"));
        RegisteredTestTask task = new();
        const string code = "pharos-test-task";

        Host.RunOnGameThread(() =>
        {
            AiTaskRegistry.Register<RegisteredTestTask>(code);
            behavior.TaskManager.AddTask(task);
        });

        AiTaskManager.OnExecuteTaskDelegate veto = (_, _) => false;
        Host.RunOnGameThread(() => behavior.TaskManager.OnExecuteTask += veto);
        try
        {
            Assert.Throws<InvalidOperationException>(() => Host.Entities.StartTask(rooster, code));
            Assert.DoesNotContain(code, Host.Entities.ActiveTasks(rooster));

            Host.RunOnGameThread(() => behavior.TaskManager.OnExecuteTask -= veto);
            Host.Entities.StartTask(rooster, code);
            Assert.Contains(code, Host.Entities.ActiveTasks(rooster));
            Host.Entities.StopTask(rooster, code);
            Assert.DoesNotContain(code, Host.Entities.ActiveTasks(rooster));
        }
        finally
        {
            Host.RunOnGameThread(() => behavior.TaskManager.OnExecuteTask -= veto);
            Host.RunOnGameThread(() =>
            {
                behavior.TaskManager.StopTask(task.Slot);
                behavior.TaskManager.RemoveTask(task);
                AiTaskRegistry.TaskCodes.Remove(typeof(RegisteredTestTask));
                AiTaskRegistry.TaskTypes.Remove(code);
            });
        }
    }

    [ServerScenario]
    public async Task PathfindingAndSpawnRules_UseTheGameWorld()
    {
        ServerTestPlayer player = await CreateTestPlayerAsync("EntityPath");
        Vec3d origin = player.Entity!.Pos.XYZ.AddCopy(0, 0, 0);
        PathfindResult path = await Host!.Entities.PathfindAsync(
            origin,
            origin.AddCopy(3, 0, 0),
            "game:chicken-hen",
            maxTicks: 600);

        Assert.True(path.Found, path.Reason);
        Assert.NotEmpty(path.Waypoints);

        BlockPos sameCell = origin.AsBlockPos;
        Vec3d sameBlockFrom = new(sameCell.X + 0.2, sameCell.Y + 0.2, sameCell.Z + 0.2);
        Vec3d sameBlockTo = new(sameCell.X + 0.4, sameCell.Y + 0.4, sameCell.Z + 0.4);
        Assert.NotEqual(sameBlockFrom, sameBlockTo);
        Assert.Equal(sameBlockFrom.AsBlockPos, sameBlockTo.AsBlockPos);
        PathfindResult alreadyThere = await Host.Entities.PathfindAsync(sameBlockFrom, sameBlockTo, "game:chicken-hen");
        Assert.True(alreadyThere.Found, alreadyThere.Reason);
        Assert.Single(alreadyThere.Waypoints);
        Assert.Equal(sameBlockTo.X, alreadyThere.Waypoints[0].X);
        Assert.Equal(sameBlockTo.Y, alreadyThere.Waypoints[0].Y);
        Assert.Equal(sameBlockTo.Z, alreadyThere.Waypoints[0].Z);

        SpawnCheck check = Host.Entities.CanSpawnAt("game:chicken-rooster", new BlockPos(0, 0, 0));
        Assert.False(check.Allowed);
        Assert.Contains("Y position", check.Reason);

        EnumAICreatureType creatureType = Host.RunOnGameThread(() =>
        {
            EntityProperties type = Api!.World.GetEntityType(new AssetLocation("game:chicken-hen"))!;
            return ServerEntityDriver.ResolvePathfinderCreatureType(type);
        });
        Assert.Equal(EnumAICreatureType.LandCreature, creatureType);
    }

    [ServerScenario]
    public async Task SpawnRules_ReportLightAndSurfaceFailures()
    {
        ServerTestPlayer player = await CreateTestPlayerAsync("SpawnConditions");
        const string code = "game:chicken-rooster";
        BlockPos position = Host!.RunOnGameThread(() => player.Entity!.Pos.AsBlockPos);

        Host.Calendar.SetTime(12);
        int daylight = Host.RunOnGameThread(() => Api!.World.BlockAccessor.GetLightLevel(position, EnumLightLevelType.MaxTimeOfDayLight));
        Assert.True(daylight > 5, $"Expected daytime light above the locust limit, got {daylight}.");
        SpawnCheck light = Host.Entities.CanSpawnAt("game:locust-corrupt-sawblade", position);
        Assert.False(light.Allowed);
        Assert.Contains("Light level", light.Reason);

        Host.Calendar.SetTime(12);
        BlockPos aboveSurface = Host.RunOnGameThread(() =>
        {
            var mapChunk = Host.Server.WorldMap.GetMapChunk(position.X >> 5, position.Z >> 5)!;
            ushort[] heightMap = mapChunk.WorldGenTerrainHeightMap!;
            int surfaceY = heightMap[(position.Z & 31) * 32 + (position.X & 31)] + 1;
            return new BlockPos(position.X, surfaceY + 1, position.Z);
        });
        SpawnCheck surface = Host.Entities.CanSpawnAt(code, aboveSurface);
        Assert.False(surface.Allowed);
        Assert.Contains("surface", surface.Reason);

        BlockPos climateSample = ServerEntityDriver.GetClimateSamplePosition(new BlockPos(10, 150, 20), seaLevel: 100);
        Assert.Equal(10, climateSample.X);
        Assert.Equal(109, climateSample.Y);
        Assert.Equal(20, climateSample.Z);

        BlockPos spawn = new(10, 20, 30);
        BlockPos rejectedHabitat = spawn.AddCopy(0, 0, -2);
        List<BlockPos> checkedHabitat = [];
        BlockPos? unsuitable = ServerEntityDriver.FindUnsuitableHabitatPosition(
            EnumHabitat.Underwater,
            habitatSize: 2,
            spawn,
            candidate =>
            {
                checkedHabitat.Add(candidate);
                return !candidate.Equals(rejectedHabitat);
            });
        Assert.Equal(rejectedHabitat, unsuitable);
        Assert.Equal(new[] { rejectedHabitat }, checkedHabitat);

        List<BlockPos> allHabitat = [];
        Assert.Null(ServerEntityDriver.FindUnsuitableHabitatPosition(
            EnumHabitat.Sea,
            habitatSize: 2,
            spawn,
            candidate =>
            {
                allHabitat.Add(candidate);
                return true;
            }));
        Assert.Equal(
            new[]
            {
                spawn.AddCopy(0, 0, -2),
                spawn.AddCopy(2, 0, 0),
                spawn.AddCopy(-2, 0, 0),
                spawn.AddCopy(0, 0, 2)
            },
            allHabitat);

        Assert.Null(ServerEntityDriver.FindUnsuitableHabitatPosition(
            EnumHabitat.Land,
            habitatSize: 2,
            spawn,
            _ => false));
    }

    private sealed class RegisteredTestTask : IAiTask
    {
        public string? Id => null;
        public int Slot => 7;
        public float Priority => 1;
        public float PriorityForCancel => 0;
        public string ProfilerName { get; set; } = "pharos-test-task";
        public bool ShouldExecute() => true;
        public void StartExecute() { }
        public bool ContinueExecute(float dt) => true;
        public void FinishExecute(bool cancelled) { }
        public void AfterInitialize() { }
        public void OnStateChanged(EnumEntityState beforeState) { }
        public bool Notify(string key, object data) => false;
        public void OnEntityLoaded() { }
        public void OnEntitySpawn() { }
        public void OnEntityDespawn(EntityDespawnData reason) { }
        public void OnEntityHurt(DamageSource source, float damage) { }
        public bool CanContinueExecute() => true;
    }
}
