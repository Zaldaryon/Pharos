using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.Client.NoObf;
using Xunit;
using Zaldaryon.Pharos.Assertions;
using Zaldaryon.Pharos.Bootstrap;
using Zaldaryon.Pharos.Core;
using Zaldaryon.Pharos.Events;
using Zaldaryon.Pharos.Server;
using Zaldaryon.Pharos.XUnit;

namespace Zaldaryon.Pharos.Tests.Events;

/// <summary><see cref="ServerEventLog"/> and <see cref="ClientEventLog"/> with a real client playing.</summary>
[Collection("Sequential")]
public class ClientServerEventLogTests : ClientServerScenarioBase
{
    protected override HeadlessClientOptions ClientOptions => new()
    {
        BootMode = ClientBootMode.Engine,
        Width = 640,
        Height = 360,
    };

    private ICoreServerAPI ServerApi => (ICoreServerAPI)Server!.Api;

    [ClientServerScenario]
    public async Task BlocksPlacedBrokenAndUsed_AreRecordedOnTheServer()
    {
        await Session!.StepFramesAsync(60);
        BlockPos ground = GroundInFront(2);
        using ServerEventLog log = ServerHost!.Events.Record(ServerEvents.Blocks | ServerEvents.Entities);

        Assert.True(await Session.Blocks.PlaceAsync(ground, BlockFacing.UP, "game:rock-granite"), "The block was not placed");

        BlockPlaced placed = Assert.Single(log.BlockPlaced);
        Assert.Equal("game:rock-granite", placed.Stack?.Collectible.Code.ToString());
        Assert.Equal(PlayerName, placed.Player.PlayerName);
        Assert.Equal(ground.UpCopy(), placed.Pos);
        Assert.Equal(0, placed.OldBlockId);

        int granite = ServerHost!.RunOnGameThread(() => ServerApi.World.GetBlock(new AssetLocation("game:rock-granite"))!.BlockId);
        Assert.True(await Session.Blocks.BreakAsync(ground.UpCopy()), "The block was not broken");
        BlockBroken broken = await log.WaitForAsync<BlockBroken>(maxTicks: 60);
        Assert.Equal(granite, broken.OldBlockId);
        Assert.Equal(ground.UpCopy(), broken.Pos);

        BlockPos chestPos = ground.UpCopy();
        ServerHost!.RunOnGameThread(() => ServerApi.World.BlockAccessor.SetBlock(ServerApi.World.GetBlock(new AssetLocation("game:chest-east"))!.BlockId, chestPos));
        Assert.True(await StepUntilAsync(() => Client!.RunOnClientThread(() => Client.Client.World.BlockAccessor.GetBlockEntity(chestPos) != null), maxFrames: 300));
        long mark = log.Mark();
        Assert.True(await Session.Blocks.UseAsync(chestPos), "The client never acted on the use click");
        BlockUsed used = await log.WaitForAsync<BlockUsed>(since: mark, maxTicks: 600);
        Assert.Equal(chestPos, used.Pos);

        PharosAssert.EventOrder(log, typeof(BlockPlaced), typeof(BlockBroken), typeof(BlockUsed));
    }

    [ClientServerScenario]
    public async Task EntitiesTheServerSpawnsAndRemoves_AreRecordedOnTheClient()
    {
        await Session!.StepFramesAsync(30);
        using ClientEventLog log = Client!.Events.Record(ClientEvents.Entities);

        Entity hen = SpawnHenNearPlayer(Client!, ServerHost!);
        EntitySpawned spawned = await log.WaitForAsync<EntitySpawned>(e => e.EntityId == hen.EntityId, maxFrames: 300);

        ServerHost!.RunOnGameThread(() => hen.Die(EnumDespawnReason.Removed));
        EntityDespawned despawned = await log.WaitForAsync<EntityDespawned>(e => e.EntityId == hen.EntityId, maxFrames: 300);

        Assert.Equal(EnumAppSide.Client, spawned.Side);
        Assert.Equal("chicken-hen", spawned.Code?.Path);
        Assert.NotSame(hen, spawned.Entity);
        Assert.True(despawned.Step >= spawned.Step);
        Assert.True(spawned.Step > 0, "The client's records carry its frame count");
    }

    [ClientServerScenario]
    public async Task ClientEventsFiredThroughTheGamesTriggers_AreRecorded()
    {
        await Session!.StepFramesAsync(30);
        using ClientEventLog log = Client!.Events.Record();
        Entity hen = SpawnHenNearPlayer(Client!, ServerHost!);
        EntityAgent clientHen = Assert.IsAssignableFrom<EntityAgent>((await log.WaitForAsync<EntitySpawned>(e => e.EntityId == hen.EntityId, maxFrames: 300)).Entity);
        long mark = log.Mark();

        // A rider that is not a player: vanilla's own mount handler reads a player's seat.
        Client.RunOnClientThread(() =>
        {
            ClientMain game = Client.Client;
            game.api.Event.TriggerEntityDeath(clientHen, new DamageSource { Source = EnumDamageSource.Fall });
            game.api.Event.TriggerEntityMounted(clientHen, null);
            game.api.Event.TriggerEntityUnmounted(clientHen, null);
            ((ClientEventAPI)game.api.Event).TriggerMapregionLoaded(new Vec2i(7, 9), null!);
        });

        List<GameEventRecord> fired = [.. log.All.Where(r => r.Index > mark)];
        EntityDied died = Assert.Single(fired.OfType<EntityDied>());
        Assert.Equal(hen.EntityId, died.EntityId);
        Assert.Equal(EnumDamageSource.Fall, died.Source?.Source);
        EntityMounted mounted = Assert.Single(fired.OfType<EntityMounted>());
        Assert.Same(clientHen, mounted.Rider);
        Assert.Null(mounted.Seat);
        Assert.Equal(hen.EntityId, Assert.Single(fired.OfType<EntityUnmounted>()).EntityId);
        MapRegionLoaded region = Assert.Single(fired.OfType<MapRegionLoaded>(), r => r.RegionX == 7);
        Assert.Equal(9, region.RegionZ);
        Assert.All(fired, r => Assert.Equal(EnumAppSide.Client, r.Side));
        PharosAssert.EventOrder(fired, typeof(EntityDied), typeof(EntityMounted), typeof(EntityUnmounted), EventMatch.Of<MapRegionLoaded>(r => r.RegionX == 7));
    }

    [ClientServerScenario]
    public async Task HotbarSlotChanges_AreRecordedOnTheServer()
    {
        await Session!.StepFramesAsync(30);
        using ServerEventLog log = ServerHost!.Events.Record(ServerEvents.ActiveSlotChanged);

        Assert.True(Client!.Inventory.SelectSlot(3));
        ActiveSlotChanged changed = await log.WaitForAsync<ActiveSlotChanged>(s => s.ToSlot == 3, maxTicks: 120);

        Assert.Equal(PlayerName, changed.Player.PlayerName);
        Assert.NotEqual(3, changed.FromSlot);
    }

    [ClientServerScenario]
    public async Task DeathAndRespawn_AreRecorded_WhileTheWaitStepsTheSession()
    {
        await Session!.StepFramesAsync(30);
        IServerPlayer player = Server!.GetClientByPlayername(PlayerName).Player;
        using ServerEventLog log = ServerHost!.Events.Record(ServerEvents.Players);

        ServerHost.RunOnGameThread(() => player.Entity.Die(EnumDespawnReason.Death, new DamageSource { Source = EnumDamageSource.Void }));
        int frames = Session.FrameCount;
        await log.WaitForAsync<PlayerDied>(maxTicks: 120);
        Assert.True(Session.FrameCount > frames, "The wait did not step the client with the server");
        Assert.True(await StepUntilAsync(() => Client!.RunOnClientThread(() => !Client.Client.EntityPlayer.Alive), maxFrames: 300), "The client never saw the death");

        Client!.RunOnClientThread(Client.Client.Respawn);
        PlayerRespawned respawned = await log.WaitForAsync<PlayerRespawned>(maxTicks: 300);

        Assert.Equal(PlayerName, respawned.Player.PlayerName);
        PharosAssert.EventOrder(log, typeof(PlayerDied), typeof(PlayerRespawned));
        Assert.True(await StepUntilAsync(() => ServerHost.RunOnGameThread(() => player.Entity.Alive), maxFrames: 600), "The player was never revived");
    }

    [ClientServerScenario]
    public async Task WaitForAsync_TimesOut_NamingWhatWasRecorded()
    {
        using ServerEventLog log = ServerHost!.Events.Record(ServerEvents.EntityMounted);

        TimeoutException ex = await Assert.ThrowsAsync<TimeoutException>(() => log.WaitForAsync<EntityMounted>(maxTicks: 5));

        Assert.Contains("No EntityMounted was recorded within 5 ticks", ex.Message);
        Assert.Contains("Nothing was recorded", ex.Message);
    }

    internal static Entity SpawnHenNearPlayer(HeadlessClient client, EmbeddedServerHost host)
    {
        Vec3d near = client.RunOnClientThread(() => client.Client.EntityPlayer.Pos.XYZ.AddCopy(3, 0, 0));
        ICoreServerAPI api = (ICoreServerAPI)host.Server.Api;
        return host.RunOnGameThread(() =>
        {
            Entity entity = api.World.ClassRegistry.CreateEntity(api.World.GetEntityType(new AssetLocation("game:chicken-hen")));
            entity.Pos.SetPos(near);
            api.World.SpawnEntity(entity);
            return entity;
        });
    }

    private BlockPos GroundInFront(int distance) => Client!.RunOnClientThread(() =>
    {
        EntityPos pos = Client.Client.EntityPlayer.Pos;
        return new BlockPos((int)Math.Floor(pos.X) + distance, (int)Math.Floor(pos.Y) - 1, (int)Math.Floor(pos.Z), 0);
    });
}

/// <summary>A client log across a reconnect, in a class of its own: a reconnect ends the pair's reuse.</summary>
[Collection("Sequential")]
[ServerWorld(Isolation = Isolation.Restart)]
public class ClientEventLogReconnectTests : ClientServerScenarioBase
{
    protected override HeadlessClientOptions ClientOptions => new() { BootMode = ClientBootMode.Engine, Width = 640, Height = 360 };

    [ClientServerScenario]
    public async Task AClientLog_FollowsTheClientAcrossAReconnect()
    {
        await Session!.StepFramesAsync(30);
        using ClientEventLog log = Client!.Events.Record(ClientEvents.EntitySpawned);
        int session = Client.SessionNumber;

        await Session.ReconnectAsync();
        Assert.True(Client.SessionNumber > session, "The client did not start a new game session");

        // An entity that reaches the client with the chunks it is in comes as loaded, not spawned.
        await Session.WaitForWorldReadyAsync();
        await Session.StepFramesAsync(60);
        long mark = log.Mark();
        Entity hen = ClientServerEventLogTests.SpawnHenNearPlayer(Client, ServerHost!);

        EntitySpawned spawned = await log.WaitForAsync<EntitySpawned>(e => e.EntityId == hen.EntityId, since: mark, maxFrames: 300);
        Assert.True(log.IsRecording);
        Assert.Equal("chicken-hen", spawned.Code?.Path);
    }
}
