using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.Server;
using Xunit;
using Zaldaryon.Pharos.Assertions;
using Zaldaryon.Pharos.Bootstrap;
using Zaldaryon.Pharos.Network;
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
    public async Task MountAsync_BlockPositionUsesTheSeatAtThatPosition()
    {
        ServerTestPlayer player = await CreateTestPlayerAsync("SeatRider");
        BlockPos bedPos = player.Entity!.Pos.AsBlockPos.AddCopy(2, 0, 0);
        this.SetBlock(bedPos, "game:bed-wood-head-north");

        Assert.True(await player.MountAsync(bedPos), "The player did not mount the bed seat at the supplied position");
        Assert.Same(this.GetBlockEntity(bedPos), player.MountedOn);
        Assert.True(await player.UnmountAsync());
        Assert.Null(player.MountedOn);
    }

    [ServerScenario]
    public async Task SleepAsync_UsesTheBedHandlerAndNightSkip()
    {
        ServerTestPlayer player = await CreateTestPlayerAsync("Sleeper");
        ServerTestPlayer creative = await CreateTestPlayerAsync("CreativeSleeper");
        Host!.RunOnGameThread(() =>
        {
            player.Player!.WorldData.CurrentGameMode = EnumGameMode.Survival;
            creative.Player!.WorldData.CurrentGameMode = EnumGameMode.Creative;
            foreach (ServerTestPlayer testPlayer in new[] { player, creative })
            {
                object? tiredness = testPlayer.Entity!.GetBehavior("tiredness");
                Assert.NotNull(tiredness);
                tiredness!.GetType().GetProperty("Tiredness")!.SetValue(tiredness, 12f);
            }
        });

        BlockPos bedPos = player.Entity!.Pos.AsBlockPos.AddCopy(2, 0, 0);
        BlockPos creativeBedPos = bedPos.AddCopy(4, 0, 0);
        this.SetBlock(bedPos, "game:bed-wood-head-north");
        BlockPos feetPos = bedPos.AddCopy(BlockFacing.NORTH);
        this.SetBlock(feetPos, "game:bed-wood-feet-north");
        this.SetBlock(creativeBedPos, "game:bed-wood-head-north");
        Assert.IsAssignableFrom<IMountableSeat>(this.GetBlockEntity(bedPos));
        Assert.Null(this.GetBlockEntity(feetPos));
        Assert.IsAssignableFrom<IMountableSeat>(this.GetBlockEntity(creativeBedPos));
        Host.Calendar.SetTime(22);
        double before = Host.Calendar.TotalHours;

        Assert.True(await player.SleepAsync(feetPos), "The real bed interaction through the feet block did not mount the player");
        Assert.Same(this.GetBlockEntity(bedPos), player.MountedOn);
        Assert.NotNull(player.MountedOn);
        Assert.True((bool)player.Entity!.GetBehavior("tiredness")!.GetType().GetProperty("IsSleeping")!.GetValue(
            player.Entity.GetBehavior("tiredness"))!);

        await Assert.ThrowsAsync<TimeoutException>(() => Host.WaitForNightSkipTicksAsync(maxTicks: 5));
        await Assert.ThrowsAsync<TimeoutException>(() => Host.WaitForNightSkipAsync(timeout: TimeSpan.FromMilliseconds(1)));

        double whileAwake = Host.Calendar.TotalHours;
        int changedEligibilityAndTime = 0;
        long eligibilityListener = Host.RunOnGameThread(() => Api!.Event.RegisterGameTickListener(_ =>
        {
            if (Interlocked.Exchange(ref changedEligibilityAndTime, 1) == 0)
            {
                Host.Calendar.Advance(TimeSpan.FromMinutes(6));
                creative.Player!.WorldData.CurrentGameMode = EnumGameMode.Spectator;
            }
        }, 0));
        try
        {
            await Assert.ThrowsAsync<TimeoutException>(() => Host.WaitForNightSkipTicksAsync(maxTicks: 1));
        }
        finally
        {
            Host.RunOnGameThread(() => Api!.Event.UnregisterGameTickListener(eligibilityListener));
        }
        Assert.Equal(1, Volatile.Read(ref changedEligibilityAndTime));
        Assert.True(Host.Calendar.TotalHours >= whileAwake + 0.1, "The test did not advance the calendar while a Creative player was awake");
        Host.RunOnGameThread(() => creative.Player!.WorldData.CurrentGameMode = EnumGameMode.Creative);

        Assert.True(await creative.SleepAsync(creativeBedPos), "The Creative player's real bed interaction did not mount the player");
        await Host.WaitForNightSkipTicksAsync(maxTicks: 120);
        Assert.True(Host.Calendar.TotalHours > before + 0.1, "The calendar did not advance while all players slept");
    }

    [ServerScenario]
    public async Task WaitForNightSkipTicks_ObservesCalendarAdvanceOnFinalTick()
    {
        ServerTestPlayer player = await CreateTestPlayerAsync("LastTickSleeper");
        Host!.RunOnGameThread(() =>
        {
            player.Player!.WorldData.CurrentGameMode = EnumGameMode.Survival;
            object? tiredness = player.Entity!.GetBehavior("tiredness");
            Assert.NotNull(tiredness);
            tiredness!.GetType().GetProperty("Tiredness")!.SetValue(tiredness, 12f);
        });

        BlockPos bedPos = player.Entity!.Pos.AsBlockPos.AddCopy(2, 0, 0);
        this.SetBlock(bedPos, "game:bed-wood-head-north");
        Host.Calendar.SetTime(22);
        Assert.True(await player.SleepAsync(bedPos));
        double before = Host.Calendar.TotalHours;

        int advanced = 0;
        long listener = Host.RunOnGameThread(() => Api!.Event.RegisterGameTickListener(_ =>
        {
            if (Interlocked.Exchange(ref advanced, 1) == 0)
                Host.Calendar.Advance(TimeSpan.FromMinutes(6));
        }, 0));
        try
        {
            await Host.WaitForNightSkipTicksAsync(maxTicks: 1);
        }
        finally
        {
            Host.RunOnGameThread(() => Api!.Event.UnregisterGameTickListener(listener));
        }

        Assert.Equal(1, Volatile.Read(ref advanced));
        Assert.True(Host.Calendar.TotalHours >= before + 0.1, "The night-skip wait missed the calendar advance from its final server tick");
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
    }

    [ClientServerScenario]
    public async Task BoatInteraction_MountsSteersAndMovesTheBoat()
    {
        await Session!.StepFramesAsync(60);
        Vec3d playerPosition = Client!.RunOnClientThread(() => Client.Client.EntityPlayer.Pos.XYZ.Clone());
        string playerName = Client.RunOnClientThread(() => Client.Client.player.PlayerName);
        Entity boat = ServerHost!.RunOnGameThread(() =>
        {
            ICoreServerAPI api = (ICoreServerAPI)ServerHost.Server.Api;
            int waterId = api.World.GetBlock(new AssetLocation("game:water-still-7"))!.BlockId;
            int waterY = (int)Math.Floor(playerPosition.Y);
            int centerX = (int)Math.Floor(playerPosition.X);
            int centerZ = (int)Math.Floor(playerPosition.Z) + 2;
            for (int x = centerX - 1; x <= centerX + 1; x++)
            for (int z = centerZ - 1; z <= centerZ + 1; z++)
            {
                api.World.BlockAccessor.SetBlock(waterId, new BlockPos(x, waterY, z));
            }

            IServerPlayer player = ServerHost.Server.GetClientByPlayername(playerName)!.Player;
            ItemSlot slot = player.InventoryManager.ActiveHotbarSlot;
            slot.Itemstack = null;
            slot.MarkDirty();

            string boatCode = api.World.EntityTypes
                .Select(type => type.Code.ToString())
                .First(code => code.Contains(":boat-raft-", StringComparison.Ordinal));
            EntityProperties type = api.World.GetEntityType(new AssetLocation(boatCode))!;
            Entity entity = ServerHost.Server.Api.World.ClassRegistry.CreateEntity(type);
            entity.Pos.SetPos(centerX + 0.5, waterY + 0.45, centerZ + 0.5);
            api.World.SpawnEntity(entity);
            return entity;
        });

        bool boatReplicated = await Session.StepUntilAsync(
            () => Client.RunOnClientThread(() => Client.Client.World.GetEntityById(boat.EntityId) is not null),
            maxFrames: 300);
        Assert.True(boatReplicated, "The client did not receive the boat");
        bool withinInteractionRange = ServerHost.RunOnGameThread(() =>
            ServerHost.Server.GetClientByPlayername(playerName)!.Player.IsInInteractionRangeOf(boat));
        Assert.True(withinInteractionRange, "The boat is outside the server's interaction range");
        bool emptyHandReplicated = await Session.StepUntilAsync(
            () => Client.RunOnClientThread(() => Client.Client.player.InventoryManager.ActiveHotbarSlot.Itemstack is null),
            maxFrames: 120);
        Assert.True(emptyHandReplicated, "The client did not receive the empty active slot needed to use the boat");
        Assert.True(await Session.Entities.AimAtAsync(boat), "The rendering client could not select the boat");
        string selectionBeforeClick = Client.RunOnClientThread(() =>
        {
            EntitySelection? entitySelection = Client.Client.EntitySelection;
            BlockSelection? blockSelection = Client.Client.BlockSelection;
            return $"mouse grabbed {Client.Client.MouseGrabbed}; entity {entitySelection?.Entity?.EntityId} box {entitySelection?.SelectionBoxIndex} hit {entitySelection?.HitPosition}; block {blockSelection?.Position}";
        });
        List<string> observedInteractions = [];
        string clientPlayerUid = Client.RunOnClientThread(() => Client.Client.player.PlayerUID);
        int? selectedSeat = null;
        int? entityInteractionMode = null;
        IServerEventAPI events = (IServerEventAPI)ServerHost.Server.Api.Event;
        OnInteractDelegate handler = (Entity interacted, IPlayer byPlayer, ItemSlot slot, Vec3d hit, int mode, ref EnumHandling handling) =>
        {
            observedInteractions.Add($"{byPlayer.PlayerName}/{byPlayer.PlayerUID}: entity {interacted.EntityId}, mode {mode}, seat {byPlayer.CurrentEntitySelection?.SelectionBoxIndex}");
            if (byPlayer.PlayerName == playerName && interacted.EntityId == boat.EntityId)
            {
                selectedSeat = byPlayer.CurrentEntitySelection?.SelectionBoxIndex;
                entityInteractionMode = mode;
            }
        };
        Client.PacketRecorder.Start();
        ServerHost.RunOnGameThread(() => events.OnPlayerInteractEntity += handler);
        Client.NetworkDegradation.Configure(new DegradedNetworkProfile(LatencyMs: 200));
        int framesBeforeUse = Session.FrameCount;
        bool boatMounted = false;
        try
        {
            bool interactionDispatched = await Session.Entities.UseAsync(boat);
            Assert.True(interactionDispatched,
                $"The rendering client did not receive entity interaction confirmation (client {clientPlayerUid}, events {string.Join("; ", observedInteractions)})");
            Assert.True(Session.FrameCount - framesBeforeUse >= 12,
                "The entity interaction returned before its 200 ms client-to-server delay elapsed");
            boatMounted = await Session.StepUntilAsync(
                () => Session.Player.MountedOn?.Entity?.EntityId == boat.EntityId
                    && ServerHost.RunOnGameThread(() => ServerHost.Server.GetClientByPlayername(playerName)?.Entityplayer?.MountedOn?.Entity?.EntityId == boat.EntityId),
                maxFrames: 120);
        }
        finally
        {
            Client.NetworkDegradation.Configure(DegradedNetworkProfile.None);
            Client.PacketRecorder.Stop();
            ServerHost.RunOnGameThread(() => events.OnPlayerInteractEntity -= handler);
        }
        int[] outboundPackets = Client.PacketRecorder.GetRecordedPackets()
            .Where(packet => packet.Direction == PacketDirection.Outbound)
            .Select(packet => packet.PacketId)
            .ToArray();
        Assert.Contains(17, outboundPackets);
        Assert.True(
            observedInteractions.Any(interaction => interaction.Contains($"entity {boat.EntityId}", StringComparison.Ordinal)),
            $"The server did not dispatch an entity interaction (client selection: {selectionBeforeClick}; outbound packets: {string.Join(",", outboundPackets)}; events {string.Join("; ", observedInteractions)})");
        Assert.Equal((int)EnumInteractMode.Interact, entityInteractionMode);
        Assert.True(boatMounted, $"The real boat interaction did not mount and sync the rendering client (selection: {selectionBeforeClick}; server seat {selectedSeat}; outbound packets: {string.Join(",", outboundPackets)}; events {string.Join("; ", observedInteractions)})");

        ServerHost.RunOnGameThread(() =>
        {
            IServerPlayer player = ServerHost.Server.GetClientByPlayername(playerName)!.Player;
            Item paddle = ServerHost.Server.Api.World.Items.First(item => item.Attributes?.IsTrue("paddlingTool") == true);
            ItemSlot slot = player.InventoryManager.ActiveHotbarSlot;
            slot.Itemstack = new ItemStack(paddle);
            slot.MarkDirty();
        });
        bool paddleReplicated = await Session.StepUntilAsync(
            () => Client.RunOnClientThread(() => Client.Client.player.InventoryManager.ActiveHotbarSlot.Itemstack?.Collectible.Attributes?.IsTrue("paddlingTool") == true),
            maxFrames: 120);
        Assert.True(paddleReplicated, "The client's active slot did not receive the paddle");

        IMountableSeat boatSeat = ServerHost.RunOnGameThread(() =>
            ServerHost.Server.GetClientByPlayername(playerName)?.Entityplayer?.MountedOn
            ?? throw new InvalidOperationException("The server player is not mounted on a boat seat"));
        Vec3d boatStart = ServerHost.RunOnGameThread(() => boat.Pos.XYZ.Clone());
        Client.Controls.Press(Zaldaryon.Pharos.Player.PlayerAction.Forward);
        bool forwardReachedSeat = false;
        bool boatMovedHorizontally = false;
        try
        {
            forwardReachedSeat = await Session.StepUntilAsync(
                () => ServerHost.RunOnGameThread(() => boatSeat.Controls.Forward),
                maxFrames: 120);
            boatMovedHorizontally = await Session.StepUntilAsync(
                () => ServerHost.RunOnGameThread(() =>
                {
                    double dx = boat.Pos.X - boatStart.X;
                    double dz = boat.Pos.Z - boatStart.Z;
                    return Math.Sqrt(dx * dx + dz * dz) > 0.1;
                }),
                maxFrames: 120);
        }
        finally
        {
            Client.Controls.Release(Zaldaryon.Pharos.Player.PlayerAction.Forward);
            await Session.StepFramesAsync(2);
        }

        double horizontalDistance = ServerHost.RunOnGameThread(() =>
        {
            double dx = boat.Pos.X - boatStart.X;
            double dz = boat.Pos.Z - boatStart.Z;
            return Math.Sqrt(dx * dx + dz * dz);
        });
        Assert.True(forwardReachedSeat, "The client's forward control did not reach the mounted boat seat");
        Assert.True(boatMovedHorizontally, $"The boat did not move horizontally while the client's forward control was held; moved {horizontalDistance:0.###} blocks");
        Assert.True(horizontalDistance > 0.1, $"The boat did not move horizontally under the real client control; moved {horizontalDistance:0.###} blocks");
    }
}
