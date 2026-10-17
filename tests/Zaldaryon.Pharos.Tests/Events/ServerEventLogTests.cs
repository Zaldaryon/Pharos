using System.Reflection;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Xunit;
using Zaldaryon.Pharos.Assertions;
using Zaldaryon.Pharos.Events;
using Zaldaryon.Pharos.Server;
using Zaldaryon.Pharos.XUnit;

namespace Zaldaryon.Pharos.Tests.Events;

/// <summary><see cref="ServerEventLog"/> on a live server.</summary>
[Collection("Sequential")]
[ServerWorld(seed: 4242, playStyle: "creativebuilding", worldType: "superflat")]
public class ServerEventLogTests : ServerScenarioBase
{
    [ServerScenario]
    public async Task Entities_AreRecordedInFiringOrder()
    {
        ServerTestPlayer alice = await CreateTestPlayerAsync("Alice");
        Vec3d near = alice.Entity!.Pos.XYZ.AddCopy(2, 0, 0);
        using ServerEventLog log = Host!.Events.Record(ServerEvents.Entities);

        Entity first = this.SpawnEntity("game:chicken-hen", near);
        Host.RunOnGameThread(() => first.Die(EnumDespawnReason.Death, new DamageSource { Source = EnumDamageSource.Void }));
        Entity second = this.SpawnEntity("game:chicken-hen", near);
        Host.RunOnGameThread(() => second.Die(EnumDespawnReason.Removed));
        EntityDespawned despawned = await log.WaitForAsync<EntityDespawned>(e => e.EntityId == second.EntityId, maxTicks: 200);

        Assert.Equal(EnumDespawnReason.Removed, despawned.Reason?.Reason);
        PharosAssert.EventOrder(log,
            EventMatch.Of<EntitySpawned>(e => e.EntityId == first.EntityId),
            EventMatch.Of<EntityDied>(e => e.EntityId == first.EntityId && e.Source?.Source == EnumDamageSource.Void),
            EventMatch.Of<EntitySpawned>(e => e.EntityId == second.EntityId),
            despawned.GetType());
        Assert.Equal("chicken-hen", log.EntityDied.Single(e => e.EntityId == first.EntityId).Code?.Path);
        IReadOnlyList<GameEventRecord> all = log.All;
        Assert.Equal(Enumerable.Range(0, all.Count).Select(i => (long)i), all.Select(r => r.Index));
        Assert.True(all.Zip(all.Skip(1)).All(p => p.First.Step <= p.Second.Step), "Steps went back");
        Assert.All(all, r => Assert.Equal(EnumAppSide.Server, r.Side));
    }

    [ServerScenario]
    public async Task Filter_RecordsOnlyTheChosenEvents()
    {
        ServerTestPlayer alice = await CreateTestPlayerAsync("Alice");
        using ServerEventLog log = Host!.Events.Record(ServerEvents.Blocks);

        this.SpawnEntity("game:chicken-hen", alice.Entity!.Pos.XYZ.AddCopy(2, 0, 0));
        Host.Ticks(5);

        Assert.Equal(0, log.Count);
        Assert.Equal(ServerEvents.Blocks, log.Events);
    }

    [ServerScenario]
    public async Task Dispose_TakesTheHandlersOut()
    {
        ServerTestPlayer alice = await CreateTestPlayerAsync("Alice");
        int spawnBefore = Handlers(Server!.ModEventManager, "OnEntitySpawn");
        int deathBefore = Handlers(Api!.Event, "OnEntityDeath");

        ServerEventLog log = Host!.Events.Record();
        Assert.Equal(spawnBefore + 1, Handlers(Server.ModEventManager, "OnEntitySpawn"));
        Assert.Equal(deathBefore + 1, Handlers(Api.Event, "OnEntityDeath"));
        Assert.True(log.IsRecording);
        log.Dispose();

        Assert.False(log.IsRecording);
        Assert.Equal(spawnBefore, Handlers(Server.ModEventManager, "OnEntitySpawn"));
        Assert.Equal(deathBefore, Handlers(Api.Event, "OnEntityDeath"));
        this.SpawnEntity("game:chicken-hen", alice.Entity!.Pos.XYZ.AddCopy(2, 0, 0));
        Host.Ticks(2);
        Assert.Equal(0, log.Count);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => log.WaitForAsync<EntitySpawned>());
    }

    [ServerScenario]
    public async Task GameModeChanged_CarriesTheNewMode()
    {
        ServerTestPlayer alice = await CreateTestPlayerAsync("Alice");
        using ServerEventLog log = Host!.Events.Record(ServerEvents.Players);

        await ExecuteSuccess("/gamemode Alice survival");

        GameModeChanged changed = await log.WaitForAsync<GameModeChanged>(maxTicks: 20);
        Assert.Equal("Alice", changed.Player.PlayerName);
        Assert.Equal(EnumGameMode.Survival, changed.Mode);
    }

    [ServerScenario]
    public async Task PlayerDied_FollowsThePlayersEntityDying()
    {
        ServerTestPlayer alice = await CreateTestPlayerAsync("Alice");
        using ServerEventLog log = Host!.Events.Record(ServerEvents.Players | ServerEvents.EntityDied);

        Host.RunOnGameThread(() => alice.Entity!.Die(EnumDespawnReason.Death, new DamageSource { Source = EnumDamageSource.Void, Type = EnumDamageType.Injury }));
        PlayerDied died = await log.WaitForAsync<PlayerDied>(maxTicks: 100);

        Assert.Equal("Alice", died.Player.PlayerName);
        Assert.Equal(EnumDamageSource.Void, died.Source?.Source);
        PharosAssert.EventOrder(log, EventMatch.Of<EntityDied>(e => e.Entity == alice.Entity), typeof(PlayerDied));
    }

    [ServerScenario]
    public async Task WorldSaved_FiresWhenTheWorldIsSaved()
    {
        using ServerEventLog log = Host!.Events.Record(ServerEvents.WorldSaved);

        string file = Path.Combine(Path.GetTempPath(), $"pharos-events-{Guid.NewGuid():N}.vcdbs");
        try
        {
            await Host.SaveWorldAsync(file);
        }
        finally
        {
            File.Delete(file);
        }

        Assert.Single(log.WorldSaved);
    }

    [ServerScenario]
    public async Task EntitySpawnAttempted_RecordsTheAttemptAndLetsItThrough()
    {
        ServerTestPlayer alice = await CreateTestPlayerAsync("Alice");
        Vec3d pos = alice.Entity!.Pos.XYZ.AddCopy(3, 0, 0);
        using ServerEventLog log = Host!.Events.Record(ServerEvents.EntitySpawnAttempted);

        bool allowed = Host.RunOnGameThread(() =>
        {
            EntityProperties hen = Api!.World.GetEntityType(new AssetLocation("game:chicken-hen"))!;
            return Api.Event.TriggerTrySpawnEntity(Api.World.BlockAccessor, ref hen, pos, 42);
        });

        Assert.True(allowed);
        EntitySpawnAttempted attempt = Assert.Single(log.EntitySpawnAttempted);
        Assert.Equal("game:chicken-hen", attempt.Code?.ToString());
        Assert.Equal(42, attempt.HerdId);
        Assert.Equal(pos, attempt.Position);
        Assert.True(attempt.OnGameThread);
    }

    [ServerScenario]
    public async Task MountsAndInteractions_FiredThroughTheGamesTriggers_AreRecorded()
    {
        ServerTestPlayer alice = await CreateTestPlayerAsync("Alice");
        EntityAgent hen = Assert.IsAssignableFrom<EntityAgent>(this.SpawnEntity("game:chicken-hen", alice.Entity!.Pos.XYZ.AddCopy(2, 0, 0)));
        alice.GiveItem("game:stick", 2);
        using ServerEventLog log = Host!.Events.Record(ServerEvents.Entities | ServerEvents.Players);

        Host.RunOnGameThread(() =>
        {
            // A rider that is not a player: vanilla's own mount handler reads a player's seat.
            Api!.Event.TriggerEntityMounted(hen, null);
            Api.Event.TriggerEntityUnmounted(hen, null);
            EnumHandling handling = EnumHandling.PassThrough;
            ItemSlot slot = alice.Player!.InventoryManager.ActiveHotbarSlot;
            Server!.EventManager.TriggerPlayerInteractEntity(hen, alice.Player, slot, new Vec3d(0.1, 0.5, 0.2), 1, ref handling);
        });

        EntityMounted mounted = Assert.Single(log.EntityMounted);
        Assert.Same(hen, mounted.Rider);
        Assert.Equal(hen.EntityId, mounted.EntityId);
        Assert.Null(mounted.Seat);
        Assert.Same(hen, Assert.Single(log.EntityUnmounted).Entity);
        EntityInteracted interacted = Assert.Single(log.EntityInteracted);
        Assert.Same(hen, interacted.Entity);
        Assert.Equal("Alice", interacted.Player.PlayerName);
        Assert.Equal(EnumInteractMode.Interact, interacted.Mode);
        Assert.Equal(new Vec3d(0.1, 0.5, 0.2), interacted.HitPosition);
        PharosAssert.EventOrder(log, typeof(EntityMounted), typeof(EntityUnmounted), typeof(EntityInteracted));
    }

    [ServerScenario]
    public void CloseAll_ClosesTheLogsLeftOpen()
    {
        ServerEventLog open = Host!.Events.Record();
        int before = Handlers(Server!.ModEventManager, "DidPlaceBlock");

        Assert.Equal(1, LeftOpenLogs.Close(Host, null));

        Assert.False(open.IsRecording);
        Assert.Equal(before - 1, Handlers(Server.ModEventManager, "DidPlaceBlock"));
        Assert.Equal(0, Host.Events.OpenCount);
    }

    // How many handlers an event's backing field holds, read on the game thread.
    private int Handlers(object owner, string eventName) => Host!.RunOnGameThread(() =>
    {
        FieldInfo field = owner.GetType().GetField(eventName, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
            ?? owner.GetType().BaseType!.GetField(eventName, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)
            ?? throw new MissingFieldException(owner.GetType().Name, eventName);
        return (field.GetValue(owner) as Delegate)?.GetInvocationList().Length ?? 0;
    });
}

/// <summary>Chunk events, in a class of their own: the test loads chunks far from the world's baseline.</summary>
[Collection("Sequential")]
[ServerWorld(seed: 4242, playStyle: "creativebuilding", worldType: "superflat", Isolation = Isolation.Restart)]
public class ServerChunkEventLogTests : ServerScenarioBase
{
    [ServerScenario]
    public async Task ChunkColumns_LoadedThenUnloaded_AreRecorded()
    {
        int chunkSize = GlobalConstantsChunkSize;
        BlockPos spawn = Host!.RunOnGameThread(() => Api!.World.DefaultSpawnPosition.AsBlockPos);
        int x = spawn.X / chunkSize + 40, z = spawn.Z / chunkSize + 40;
        using ServerEventLog log = Host.Events.Record(ServerEvents.Chunks);

        Host.RunOnGameThread(() => Api!.WorldManager.LoadChunkColumnPriority(x, z));
        ChunkColumnLoaded loaded = await log.WaitForAsync<ChunkColumnLoaded>(c => c.ChunkX == x && c.ChunkZ == z, maxTicks: 1200);
        Assert.True(loaded.ChunkCount > 0);
        int regionSize = Host.RunOnGameThread(() => Api!.WorldManager.RegionSize) / chunkSize;
        MapRegionLoaded region = await log.WaitForAsync<MapRegionLoaded>(r => r.RegionX == x / regionSize && r.RegionZ == z / regionSize, maxTicks: 100);
        Assert.NotNull(region.Region);

        long mark = log.Mark();
        Host.RunOnGameThread(() => Api!.WorldManager.UnloadChunkColumn(x, z));
        ChunkColumnUnloaded unloaded = await log.WaitForAsync<ChunkColumnUnloaded>(c => c.ChunkX == x && c.ChunkZ == z, since: mark, maxTicks: 10);

        Assert.True(unloaded.Index > loaded.Index);
        PharosAssert.EventOrder(log, EventMatch.Of<ChunkColumnLoaded>(c => c.ChunkX == x), EventMatch.Of<ChunkColumnUnloaded>(c => c.ChunkX == x));
    }

    private static int GlobalConstantsChunkSize => Vintagestory.API.Config.GlobalConstants.ChunkSize;
}

/// <summary>The scenario bases close the logs a test leaves open, when it ends.</summary>
[Collection("Sequential")]
public class EventLogTeardownTests
{
    private static ServerEventLog? s_serverLog;
    private static ServerEventLog? s_pairServerLog;
    private static ClientEventLog? s_clientLog;

    [Fact]
    [Trait(PharosTraits.Category, PharosTraits.Live)]
    public async Task AServerScenario_ClosesTheLogsItsTestLeftOpen()
    {
        s_serverLog = null;
        Tests.XUnit.ScenarioRunnerHarness.RunResult result = await Tests.XUnit.ScenarioRunnerHarness.RunAsync(typeof(LeavesServerLogOpen), nameof(LeavesServerLogOpen.Records));

        Assert.Single(result.Passed);
        Assert.NotNull(s_serverLog);
        Assert.True(s_serverLog!.IsDisposed, "The log was not disposed");
        Assert.False(s_serverLog.IsRecording);
    }

    [Fact]
    [Trait(PharosTraits.Category, PharosTraits.Live)]
    public async Task AClientServerScenario_ClosesTheLogsItsTestLeftOpen()
    {
        s_pairServerLog = null;
        s_clientLog = null;
        Tests.XUnit.ScenarioRunnerHarness.RunResult result = await Tests.XUnit.ScenarioRunnerHarness.RunAsync(typeof(LeavesPairLogsOpen), nameof(LeavesPairLogsOpen.Records));

        Assert.Single(result.Passed);
        Assert.True(s_pairServerLog!.IsDisposed, "The server log was not disposed");
        Assert.True(s_clientLog!.IsDisposed, "The client log was not disposed");
    }

#pragma warning disable xUnit1000 // Samples are private so the real test run does not discover them.
    private sealed class LeavesServerLogOpen : ServerScenarioBase
    {
        [ServerScenario]
        public void Records()
        {
            s_serverLog = Host!.Events.Record();
            Assert.True(s_serverLog.IsRecording);
        }
    }

    private sealed class LeavesPairLogsOpen : ClientServerScenarioBase
    {
        protected override Bootstrap.HeadlessClientOptions ClientOptions => new() { BootMode = Bootstrap.ClientBootMode.Engine, Width = 640, Height = 360 };

        [ClientServerScenario]
        public void Records()
        {
            s_pairServerLog = ServerHost!.Events.Record();
            s_clientLog = Client!.Events.Record();
        }
    }
#pragma warning restore xUnit1000
}
