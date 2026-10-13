using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.Server;
using Xunit;
using Zaldaryon.Pharos.Assertions;
using Zaldaryon.Pharos.Bootstrap;
using Zaldaryon.Pharos.XUnit;

namespace Zaldaryon.Pharos.Tests.Server;

[Collection("Sequential")]
[ServerWorld(seed: 4242, Isolation = WorldIsolation.Restart)]
public class HeadlessPlayerTests : ServerScenarioBase
{
    [ServerScenario]
    public async Task CreateTestPlayerAsync_JoinsAPlayingPlayerWithEntityAndInventories()
    {
        List<string> joined = [];
        List<string> nowPlaying = [];
        Host!.RunOnGameThread(() =>
        {
            Api!.Event.PlayerJoin += p => joined.Add(p.PlayerName);
            Api.Event.PlayerNowPlaying += p => nowPlaying.Add(p.PlayerName);
        });

        ServerTestPlayer player = await CreateTestPlayerAsync("Alice");

        Assert.True(player.IsConnected);
        Assert.Equal("Alice", player.PlayerName);
        Assert.NotNull(player.Entity);
        Assert.True(player.Player!.InventoryManager.Inventories.Count > 0);

        ConnectedClient client = Assert.Single(Server!.Clients.Values, c => c.PlayerName == "Alice");
        Assert.Equal(EnumClientState.Playing, client.State);
        Assert.Contains(Server.AllOnlinePlayers, p => p.PlayerName == "Alice");

        // The server ran its own join sequence, so mods see the player join and start playing.
        Assert.Contains("Alice", joined);
        Assert.Contains("Alice", nowPlaying);

        // The server streams the world to a playing client.
        await Host.TickUntilAsync(() => player.ReceivedPacketCount > 0, maxTicks: 300);
        Assert.True(player.ReceivedPacketCount > 0);
    }

    [ServerScenario]
    public async Task CreateTestPlayerAsync_JoinsSeveralPlayersSideBySide()
    {
        ServerTestPlayer alice = await CreateTestPlayerAsync("Alice");
        ServerTestPlayer bob = await CreateTestPlayerAsync("Bob");
        ServerTestPlayer carol = await CreateTestPlayerAsync("Carol");

        Assert.All([alice, bob, carol], p => Assert.True(p.IsConnected));
        Assert.Equal(3, Server!.Clients.Values.Count(c => c.State == EnumClientState.Playing));
        Assert.Equal(3, Host!.TestPlayers.Count);
        Assert.Equal(3, new[] { alice, bob, carol }.Select(p => p.Entity!.EntityId).Distinct().Count());
    }

    [ServerScenario]
    public async Task GiveItem_PutsTheItemInTheRealInventory()
    {
        ServerTestPlayer player = await CreateTestPlayerAsync("Alice");

        player.GiveItem("game:stick", 5);

        Assert.True(player.HasItem("game:stick", 5));
        Assert.False(player.HasItem("game:stick", 6));
    }

    [ServerScenario]
    public async Task SayAsync_ReachesTheServerChatHandlerAsAPlayerMessage()
    {
        ServerTestPlayer player = await CreateTestPlayerAsync("Alice");
        List<(string Name, string Message)> chat = [];
        Host!.RunOnGameThread(() => Api!.Event.PlayerChat += (IServerPlayer byPlayer, int channelId, ref string message, ref string data, Vintagestory.API.Datastructures.BoolRef consumed) =>
            chat.Add((byPlayer.PlayerName, message)));

        await player.SayAsync("hello from a headless player");

        Assert.Contains(chat, c => c.Name == "Alice" && c.Message.Contains("hello from a headless player"));
    }

    [ServerScenario]
    public async Task Disconnect_LeavesTheServerAndTheNameCanRejoin()
    {
        ServerTestPlayer first = await CreateTestPlayerAsync("Alice");
        first.Disconnect();

        Assert.False(first.IsConnected);
        Assert.DoesNotContain(Server!.Clients.Values, c => c.PlayerName == "Alice");

        ServerTestPlayer again = await CreateTestPlayerAsync("Alice");
        Assert.True(again.IsConnected);
        Assert.Single(Server.Clients.Values, c => c.PlayerName == "Alice");
    }

    [ServerScenario]
    public async Task CreateTestPlayerAsync_RejectsADuplicateName()
    {
        await CreateTestPlayerAsync("Alice");

        await Assert.ThrowsAsync<InvalidOperationException>(() => CreateTestPlayerAsync("Alice"));
    }

    [ServerScenario]
    public async Task MountAndUnmountAsync_UseTheEntityMountSystem()
    {
        ServerTestPlayer player = await CreateTestPlayerAsync("Rider");
        Host!.RunOnGameThread(() => player.Player!.WorldData.CurrentGameMode = EnumGameMode.Survival);
        string boatCode = Host.RunOnGameThread(() => Api!.World.EntityTypes
            .Select(type => type.Code.ToString())
            .First(code => code.Contains(":boat-raft-", StringComparison.Ordinal)));
        Entity boat = this.SpawnEntity(boatCode, player.Entity!.Pos.XYZ.AddCopy(2, 0, 0));

        Assert.True(await player.MountAsync(boat), $"The boat {boatCode} did not accept the player");
        Assert.NotNull(player.MountedOn);
        Assert.Equal(boat.EntityId, player.MountedOn!.Entity.EntityId);

        Assert.True(await player.UnmountAsync());
        Assert.Null(player.MountedOn);
    }

    [ServerScenario]
    public async Task SleepAsync_UsesTheBedHandlerAndNightSkip()
    {
        ServerTestPlayer player = await CreateTestPlayerAsync("Sleeper");
        Host!.RunOnGameThread(() =>
        {
            player.Player!.WorldData.CurrentGameMode = EnumGameMode.Survival;
            object? tiredness = player.Entity!.GetBehavior("tiredness");
            Assert.NotNull(tiredness);
            tiredness!.GetType().GetProperty("Tiredness")!.SetValue(tiredness, 12f);
        });

        BlockPos bedPos = player.Entity!.Pos.AsBlockPos.AddCopy(2, 0, 0);
        this.SetBlock(bedPos, "game:bed-wood-head-north");
        Assert.IsAssignableFrom<IMountableSeat>(this.GetBlockEntity(bedPos));
        Host.Calendar.SetTime(22);
        double before = Host.Calendar.TotalHours;

        Assert.True(await player.SleepAsync(bedPos), "The real bed interaction did not mount the player");
        Assert.NotNull(player.MountedOn);
        Assert.True((bool)player.Entity!.GetBehavior("tiredness")!.GetType().GetProperty("IsSleeping")!.GetValue(
            player.Entity.GetBehavior("tiredness"))!);

        await Host.WaitForNightSkipAsync(TimeSpan.FromSeconds(30));
        Assert.True(Host.Calendar.TotalHours > before + 0.1, "The calendar did not advance while all players slept");
    }
}

[Collection("Sequential")]
public class EngineClientWithHeadlessPlayerTests : ClientServerScenarioBase
{
    protected override HeadlessClientOptions ClientOptions => new()
    {
        BootMode = ClientBootMode.Engine,
        Width = 640,
        Height = 360,
    };

    [ClientServerScenario]
    public async Task HeadlessPlayer_IsAnotherPlayerForTheRenderingClient()
    {
        ServerTestPlayer other = await CreateTestPlayerAsync("Bob");

        // Bring Bob next to the rendering client so the server tracks him for it.
        var mine = Client!.Client.EntityPlayer.Pos;
        await other.TeleportTo(mine.X + 2, mine.Y, mine.Z + 2);

        bool seen = await StepUntilAsync(
            () => Client.Client.PlayersByUid.ContainsKey(other.PlayerUID)
                && Client.Client.PlayersByUid[other.PlayerUID].Entity != null,
            maxFrames: 1200);

        Assert.True(seen, "The rendering client never received the other player and its entity");
        Assert.Equal(2, Server!.Clients.Values.Count(c => c.State == EnumClientState.Playing));
    }

    [ClientServerScenario]
    public async Task HeadlessPlayer_BreakingABlockUpdatesTheRenderingClient()
    {
        await Session!.StepFramesAsync(60);
        BlockPos blockPos = Client!.RunOnClientThread(() =>
        {
            EntityPos pos = Client.Client.EntityPlayer.Pos;
            return new BlockPos((int)Math.Floor(pos.X) + 2, (int)Math.Floor(pos.Y) - 1, (int)Math.Floor(pos.Z), 0);
        });
        int graniteId = ServerHost!.RunOnGameThread(() => ServerHost.Server.Api.World
            .GetBlock(new AssetLocation("game:rock-granite"))!.BlockId);
        ServerHost.RunOnGameThread(() => ServerHost.Server.Api.World.BlockAccessor.SetBlock(graniteId, blockPos));

        bool blockArrived = await StepUntilAsync(
            () => Client.RunOnClientThread(() => Client.Client.World.BlockAccessor.GetBlock(blockPos).BlockId == graniteId),
            maxFrames: 300);
        Assert.True(blockArrived, "The rendering client never received the block before Bob broke it");

        ServerTestPlayer bob = await CreateTestPlayerAsync("Bob");
        Vec3d alicePosition = Client.RunOnClientThread(() => Client.Client.EntityPlayer.Pos.XYZ.Clone());
        await bob.TeleportTo(alicePosition.X, alicePosition.Y, alicePosition.Z);
        ServerHost.RunOnGameThread(() => bob.Player!.WorldData.CurrentGameMode = EnumGameMode.Creative);

        BotResult result = await bob.Bot.BreakBlockAsync(blockPos);
        Assert.True(result.Accepted, result.Error?.Code ?? result.Reason);

        bool clientSawBreak = await Session.StepUntilAsync(
            () => Client.RunOnClientThread(() => Client.Client.World.BlockAccessor.GetBlock(blockPos).BlockId == 0),
            maxFrames: 300);
        Assert.True(clientSawBreak, "The server broke the block, but Alice's rendering client did not receive the update");
        Assert.Equal(0, ServerHost.RunOnGameThread(() => ServerHost.Server.Api.World.BlockAccessor.GetBlock(blockPos).BlockId));
        ClientServerAssert.ClientServerBlockSynced(Session, blockPos);
    }

    [ClientServerScenario]
    public async Task BedInteraction_MountsClientAndSneakUnmounts()
    {
        await Session!.StepFramesAsync(60);
        BlockPos bedPos = Client!.RunOnClientThread(() => Client.Client.EntityPlayer!.Pos.AsBlockPos.AddCopy(2, 0, 0));
        string playerName = Client!.RunOnClientThread(() => Client.Client.player.PlayerName);
        ServerHost!.RunOnGameThread(() =>
        {
            IServerPlayer player = ServerHost.Server.GetClientByPlayername(playerName)!.Player;
            player.WorldData.CurrentGameMode = EnumGameMode.Survival;
            object tiredness = player.Entity!.GetBehavior("tiredness")!;
            tiredness.GetType().GetProperty("Tiredness")!.SetValue(tiredness, 12f);
            Block bed = ServerHost.Server.Api.World.GetBlock(new AssetLocation("game:bed-wood-head-north"))!;
            ServerHost.Server.Api.World.BlockAccessor.SetBlock(bed.BlockId, bedPos);
        });
        Client.RunOnClientThread(() =>
        {
            object tiredness = Client.Client.EntityPlayer.GetBehavior("tiredness")!;
            tiredness.GetType().GetProperty("Tiredness")!.SetValue(tiredness, 12f);
        });
        bool replicated = await Session.StepUntilAsync(() => Client.RunOnClientThread(() =>
            Client.Client.World.BlockAccessor.GetBlockEntity(bedPos) is IMountableSeat), maxFrames: 300);
        Assert.True(replicated, "The client did not receive the bed and its seat");

        Assert.True(await Session.Blocks.UseAsync(bedPos), "The client did not use the bed");
        bool mounted = await Session.StepUntilAsync(() =>
            Session.Player.MountedOn is not null
            && ServerHost.RunOnGameThread(() => ServerHost.Server.GetClientByPlayername(playerName)?.Entityplayer?.MountedOn is not null),
            maxFrames: 120);
        Assert.True(mounted, "The bed did not mount and sync the rendering client through the block interaction packet");

        Client.Controls.Press(Zaldaryon.Pharos.Player.PlayerAction.Sneak);
        try
        {
            await Session.StepFramesAsync(10);
            Assert.Null(Session.Player.MountedOn);
            Assert.Null(ServerHost.RunOnGameThread(() => ServerHost.Server.GetClientByPlayername(playerName)?.Entityplayer?.MountedOn));
        }
        finally
        {
            Client.Controls.Release(Zaldaryon.Pharos.Player.PlayerAction.Sneak);
        }

        Vec3d boatPosition = Client.RunOnClientThread(() => Client.Client.EntityPlayer.Pos.XYZ.AddCopy(-3, 0, 0));
        string boatCode = ServerHost.RunOnGameThread(() => ServerHost.Server.Api.World.EntityTypes
            .Select(type => type.Code.ToString())
            .First(code => code.Contains(":boat-raft-", StringComparison.Ordinal)));
        Entity boat = ServerHost.RunOnGameThread(() =>
        {
            EntityProperties type = ServerHost.Server.Api.World.GetEntityType(new AssetLocation(boatCode))!;
            Entity entity = ServerHost.Server.Api.World.ClassRegistry.CreateEntity(type);
            entity.Pos.SetPos(boatPosition);
            ServerHost.Server.Api.World.SpawnEntity(entity);
            return entity;
        });
        bool boatReplicated = await Session.StepUntilAsync(
            () => Client.RunOnClientThread(() => Client.Client.World.GetEntityById(boat.EntityId) is not null),
            maxFrames: 300);
        Assert.True(boatReplicated, $"The client did not receive {boatCode}");

        IMountableSeat boatSeat = ServerHost.RunOnGameThread(() =>
        {
            IServerPlayer player = ServerHost.Server.GetClientByPlayername(playerName)!.Player;
            IMountableSeat seat = boat.GetInterface<IMountable>()!.Seats.First();
            Assert.True(player.Entity!.TryMount(seat));
            return seat;
        });
        bool boatMounted = await Session.StepUntilAsync(
            () => Session.Player.MountedOn?.Entity?.EntityId == boat.EntityId,
            maxFrames: 120);
        Assert.True(boatMounted, "The boat mount did not sync to the rendering client");

        Client.Controls.Press(Zaldaryon.Pharos.Player.PlayerAction.Forward);
        try
        {
            await Session.StepFramesAsync(10);
            Assert.True(ServerHost.RunOnGameThread(() => boatSeat.Controls.Forward),
                "The client's forward control did not reach the mounted boat seat");
        }
        finally
        {
            Client.Controls.Release(Zaldaryon.Pharos.Player.PlayerAction.Forward);
            await Session.StepFramesAsync(2);
        }
    }
}
