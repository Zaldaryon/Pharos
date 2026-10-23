using ProtoBuf;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Zaldaryon.Pharos.Assertions;
using Zaldaryon.Pharos.Network;
using Zaldaryon.Pharos.Server;
using Zaldaryon.Pharos.XUnit;
using Xunit;

namespace Zaldaryon.Pharos.Tests.Server;

[ServerMods("TestMods/pharosnetmod")]
public sealed class MultiplayerDepthTests : ServerScenarioBase
{
    [ServerScenario]
    public async Task ModMessages_UseRealTcpAndUdpHandlers_WithLookAlikeTypes()
    {
        var alice = await CreateTestPlayerAsync("DepthAlice");
        using var log = Server.ModNetwork.Record();
        await alice.ModNetwork.SendAsync("pharosnet", new SyncPacket { Version = 359, Text = "bot" });
        var received = Assert.Single(log.Received<SyncPacket>("pharosnet"));
        var sent = Assert.Single(log.Sent<SyncAck>("pharosnet"));
        Assert.Equal(359, sent.Message.Version);
        Assert.Equal("bot", received.Message.Text);
        Assert.Equal(alice.PlayerUID, sent.Envelope.PlayerUID);
        Assert.Equal(alice.PlayerUID, received.Envelope.PlayerUID);
        Assert.True(received.Envelope.Sequence < sent.Envelope.Sequence);
        Assert.Equal(received.Envelope.Tick, sent.Envelope.Tick);
        Assert.True(received.Envelope.Size > 0);
        await alice.ModNetwork.SendAsync("pharosnetudp", new Ping { N = 42 });
        Assert.Equal(42, Assert.Single(log.Received<Ping>("pharosnetudp")).Message.N);
        Assert.True(Assert.Single(log.Received<Ping>("pharosnetudp")).Envelope.Udp);
        Assert.DoesNotContain(log.Messages(), m => !m.Udp && m.Direction == ModMessageDirection.Received && m.Sequence > received.Envelope.Sequence);
        alice.ModNetwork.QueueSend("pharosnet", new SyncPacket { Version = 360 });
        await alice.ModNetwork.SendAsync("pharosnet", new SyncPacket { Version = 361 });
        Assert.Contains(log.Received<SyncPacket>(), m => m.Message.Version == 361);
    }

    [ServerScenario]
    public async Task WorkerSends_KeepIdentityAfterDisconnect_AndScopesStopOnRestart()
    {
        var bot = await CreateTestPlayerAsync("WorkerBot");
        var channel = Server.RunOnGameThread(() => Server.Api.Network.RegisterChannel("worker").RegisterMessageType<SyncPacket>());
        using var log = Server.ModNetwork.Record("worker");
        var player = bot.Player!;
        await Task.Run(() => channel.SendPacket(new SyncPacket { Version = 11 }, player));
        Server.RunOnGameThread(() => Server.Server.DisconnectPlayer(Server.Server.Clients[player.ClientId]));
        Assert.Equal(bot.PlayerUID, Assert.Single(log.Sent<SyncPacket>()).Envelope.PlayerUID);
        log.Dispose();
        Assert.Single(log.Messages());
        await RestartWorldAsync(RestartMode.Crash);
        log.Dispose(); // A stopped host must not make a using scope mask the test result.
        Assert.Single(log.Messages());
        Assert.Equal(11, Assert.Single(log.Sent<SyncPacket>()).Message.Version); // "worker" is not registered on the new server.
        var joined = await CreateTestPlayerAsync("RestartedBot");
        using var restarted = Server.ModNetwork.Record("pharosnet");
        await joined.Bot.ModNetwork.SendAsync("pharosnet", new SyncPacket { Version = 12 });
        Assert.Equal(12, Assert.Single(restarted.Received<SyncPacket>()).Message.Version);
    }

    [ServerScenario]
    public async Task StoppedScopes_KeepTypedReads_AndReportPendingOverflow()
    {
        var bot = await CreateTestPlayerAsync("OverflowBot");
        var channel = Server.RunOnGameThread(() => Server.Api.Network.RegisterChannel("flood").RegisterMessageType<SyncPacket>());
        var player = bot.Player!;
        var finished = Server.ModNetwork.Record("flood");
        Server.RunOnGameThread(() => channel.SendPacket(new SyncPacket { Version = 1 }, player));
        finished.Dispose();
        var flooded = Server.ModNetwork.Record("flood");
        try
        {
            await Task.Run(() => { for (int i = 0; i < 10_001; i++) channel.SendPacket(new SyncPacket { Version = 2 }, player); });
            flooded.Dispose();
            Assert.Throws<InvalidOperationException>(() => flooded.Messages());
            Assert.Equal(1, Assert.Single(finished.Sent<SyncPacket>()).Message.Version);
        }
        finally
        {
            Server.ModNetwork.Clear(); // The host is shared by this class's scenarios.
        }
    }

    [ServerScenario]
    public async Task SendAsync_IgnoresOtherTransportsAndEarlierIdenticalPackets()
    {
        var bot = await CreateTestPlayerAsync("DualBot");
        Server.RunOnGameThread(() =>
        {
            Server.Api.Network.RegisterChannel("dual").RegisterMessageType<SyncPacket>();
            Server.Api.Network.RegisterUdpChannel("dual").RegisterMessageType<SyncPacket>();
        });
        using var log = Server.ModNetwork.Record("dual");
        // A delayed identical packet is queued first; the send itself is dropped and must time out.
        foreach ((int version, bool udp) in new[] { (7, false), (8, true) })
        {
            bot.Network.Configure(new DegradedNetworkProfile(LatencyMs: 200));
            bot.ModNetwork.QueueSend("dual", new SyncPacket { Version = version }, udp: false);
            bot.Network.Configure(DegradedNetworkProfile.None);
            using (bot.Network.Simulator.Filter(p => p.Direction == PacketDirection.Outbound))
                await Assert.ThrowsAsync<TimeoutException>(() => bot.ModNetwork.SendAsync("dual", new SyncPacket { Version = version }, udp: udp, maxTicks: 200));
        }
        Assert.Equal(new[] { 7, 8 }, log.Received<SyncPacket>().Select(m => m.Message.Version));
        Assert.DoesNotContain(log.Messages(), m => m.Udp);
        await bot.ModNetwork.SendAsync("dual", new SyncPacket { Version = 9 }, udp: true);
        Assert.True(Assert.Single(log.Received<SyncPacket>(), m => m.Message.Version == 9).Envelope.Udp);

        await bot.ModNetwork.SendAsync("dual", new SyncPacket { Version = 10 }, udp: false);

        // The payload is captured at the call, and a pending send excludes overlapping sends from the same bot.
        bot.Network.Configure(new DegradedNetworkProfile(LatencyMs: 200));
        bot.ModNetwork.QueueSend("dual", new SyncPacket { Version = 11 }, udp: false);
        bot.Network.Configure(DegradedNetworkProfile.None);
        var message = new SyncPacket { Version = 12 };
        Task pending = bot.ModNetwork.SendAsync("dual", message, udp: false);
        message.Version = 13;
        Assert.Throws<InvalidOperationException>(() => bot.ModNetwork.QueueSend("dual", new SyncPacket { Version = 12 }, udp: false));
        await Assert.ThrowsAsync<InvalidOperationException>(() => bot.ModNetwork.SendAsync("dual", new SyncPacket { Version = 12 }, udp: false));
        await pending;
        Assert.Equal(new[] { 7, 8, 9, 10, 11, 12 }, log.Received<SyncPacket>().Select(m => m.Message.Version));
    }

    [ServerScenario]
    public async Task SendAsync_RejectsAQueueSendInProgress_AndWaitsForPendingUdpIngress()
    {
        var bot = await CreateTestPlayerAsync("GuardBot");
        Server.RunOnGameThread(() =>
        {
            Server.Api.Network.RegisterChannel("guard").RegisterMessageType<BlockingPacket>();
            Server.Api.Network.RegisterUdpChannel("guard").RegisterMessageType<SyncPacket>();
        });
        using var log = Server.ModNetwork.Record("guard");

        // QueueSend serializes on the game thread and holds the guard meanwhile, so a SendAsync cannot start beside it.
        var blocking = new BlockingPacket { Value = 5, Blocks = true };
        Task queued = Task.Run(() => bot.ModNetwork.QueueSend("guard", blocking, udp: false));
        Assert.True(blocking.Entered.Wait(TimeSpan.FromSeconds(30)));
        var overlap = await Assert.ThrowsAsync<InvalidOperationException>(() => bot.ModNetwork.SendAsync("guard", new SyncPacket { Version = 1 }, udp: true));
        Assert.Contains("in progress", overlap.Message);
        blocking.Released.Set();
        await queued;
        Assert.Equal(Server.RunOnGameThread(() => Environment.CurrentManagedThreadId), blocking.ThreadId);
        Assert.True(await Server.TryTickUntilAsync(() => log.Received<BlockingPacket>().Any()));

        // An identical packet still waiting in the engine's UDP ingress is handled before the send starts.
        EnqueuePendingUdp(Server, bot, "guard", new SyncPacket { Version = 2 });
        using (bot.Network.Simulator.Filter(p => p.Direction == PacketDirection.Outbound))
            await Assert.ThrowsAsync<TimeoutException>(() => bot.ModNetwork.SendAsync("guard", new SyncPacket { Version = 2 }, udp: true, maxTicks: 200));
        Assert.Equal(5, Assert.Single(log.Received<BlockingPacket>()).Message.Value);
        Assert.Equal(2, Assert.Single(log.Received<SyncPacket>()).Message.Version);
    }

    [ServerScenario]
    public async Task ModRangeFilter_ExcludesHorizontalAndVerticalFarPlayers()
    {
        var near = await CreateTestPlayerAsync("RangeNear");
        var horizontal = await CreateTestPlayerAsync("RangeHorizontal");
        var vertical = await CreateTestPlayerAsync("RangeVertical");
        var center = Server.RunOnGameThread(() => near.Entity!.Pos.XYZ);
        await horizontal.TeleportToAsync(center.X + 200, center.Y, center.Z);
        await vertical.TeleportToAsync(center.X, center.Y + 100, center.Z);
        Assert.True(await Server.TryTickUntilAsync(() => Server.RunOnGameThread(() =>
            horizontal.Entity!.Pos.XYZ.DistanceTo(center) > 100 && vertical.Entity!.Pos.Y > center.Y + 50)));
        Server.RunOnGameThread(() => Server.Api.Network.RegisterChannel("range").RegisterMessageType<SyncPacket>());
        using var log = Server.ModNetwork.Record("range");
        Server.RunOnGameThread(() =>
        {
            var channel = Server.Api.Network.GetChannel("range");
            var recipients = Server.Api.World.AllOnlinePlayers.Where(p => p.Entity.Pos.XYZ.DistanceTo(center) <= 32).Cast<Vintagestory.API.Server.IServerPlayer>().ToArray();
            channel.SendPacket(new SyncPacket { Version = 20 }, recipients);
        });
        Assert.Equal(near.PlayerUID, Assert.Single(log.Sent<SyncPacket>()).Envelope.PlayerUID);
        PharosAssert.OnlyNearbyReceived(log.Sent<SyncPacket>(), center, 32);
        Server.RunOnGameThread(() => Server.Api.Network.GetChannel("range").BroadcastPacket(new SyncPacket { Version = 21 }));
        Assert.Throws<PharosAssertException>(() => PharosAssert.OnlyNearbyReceived(log.Sent<SyncPacket>().Where(m => m.Message.Version == 21), center, 32));
    }

    [ServerScenario]
    public async Task SameTick_HandlesBotsInSubmissionOrder_AndRollsBackFailedCallback()
    {
        var alice = await CreateTestPlayerAsync("BatchAlice");
        var bob = await CreateTestPlayerAsync("BatchBob");
        using var log = Server.ModNetwork.Record("pharosnet");
        int before = Server.TickCount;
        await Server.SameTick(() =>
        {
            alice.ModNetwork.QueueSend("pharosnet", new SyncPacket { Version = 1 });
            bob.ModNetwork.QueueSend("pharosnet", new SyncPacket { Version = 2 });
        });
        Assert.Equal(before + 1, Server.TickCount);
        Assert.Equal(new[] { 1, 2 }, log.Received<SyncPacket>().Select(m => m.Message.Version));
        Assert.Single(log.Received<SyncPacket>().Select(m => m.Envelope.Tick).Distinct());
        await Assert.ThrowsAsync<InvalidOperationException>(() => Server.SameTick(() =>
        {
            alice.ModNetwork.QueueSend("pharosnet", new SyncPacket { Version = 3 });
            throw new InvalidOperationException("abort");
        }));
        await alice.ModNetwork.SendAsync("pharosnet", new SyncPacket { Version = 4 });
        Assert.Equal(new[] { 1, 2, 4 }, log.Received<SyncPacket>().Select(m => m.Message.Version));
        alice.Network.Configure(new DegradedNetworkProfile { LatencyMs = 100 });
        await Assert.ThrowsAsync<InvalidOperationException>(() => Server.SameTick(() =>
            alice.ModNetwork.QueueSend("pharosnet", new SyncPacket { Version = 5 })));
        alice.Network.Configure(DegradedNetworkProfile.None);
    }

    [ServerScenario]
    public async Task Crowds_JoinInLayouts_AndExposeActualServerVisibility()
    {
        Vec3d center = Server.RunOnGameThread(() => Server.DefaultSpawnPosition.XYZ);
        var players = await CreateTestPlayersAsync(12, PlayerLayout.Ring(center, radius: 8));
        Assert.Equal(12, players.Length);
        Assert.Equal(12, players.Select(p => p.PlayerUID).Distinct().Count());
        Assert.All(players, p => Assert.True(p.IsConnected));
        Assert.All(players, p => Assert.InRange(Server.RunOnGameThread(() => p.Entity!.Pos.XYZ.DistanceTo(center)), 7.8, 8.2));
        Assert.All(players, p => Assert.NotEmpty(Server.Visibility.ChunksSentTo(p)));
        Assert.True(await Server.TryTickUntilAsync(() =>
            Server.Visibility.EntitiesKnownTo(players[0]).Any(e => e.EntityId == players[1].Entity!.EntityId), maxTicks: 2000));
        using var log = Server.ModNetwork.Record("pharosnet");
        Assert.True((await Server.Commands.ExecuteAsync("pharosnet sync 9")).Ok);
        Assert.Equal(12, log.Sent<SyncPacket>().Count);
        PharosAssert.OnlyNearbyReceived(log.Sent<SyncPacket>(), center, radius: 16);
        Assert.Throws<PharosAssertException>(() => PharosAssert.OnlyNearbyReceived(log.Sent<SyncPacket>(), center, radius: 1));
    }

    [ServerScenario]
    public async Task Visibility_UsesRealTracking_AndBuiltInDeliveryKeepsEngineSemantics()
    {
        var near = await CreateTestPlayerAsync("VisibleNear");
        var far = await CreateTestPlayerAsync("VisibleFar");
        var center = Server.RunOnGameThread(() => near.Entity!.Pos.XYZ);
        using var clock = Server.UseVirtualClock();
        await far.TeleportToAsync(center.X + 1024, center.Y, center.Z + 1024);
        int cx = (int)Math.Floor(center.X / 32), cy = (int)Math.Floor(center.Y / 32), cz = (int)Math.Floor(center.Z / 32);
        Assert.True(await Server.TryTickUntilAsync(() =>
            Server.Visibility.ChunksSentTo(near).Any(p => p.X == cx && p.Y == cy && p.Z == cz) &&
            !Server.Visibility.ChunksSentTo(far).Any(p => p.X == cx && p.Y == cy && p.Z == cz) &&
            !Server.Visibility.EntitiesKnownTo(near).Any(e => e.EntityId == far.Entity!.EntityId),
            maxTicks: 2000), "The server never updated near/far chunk and entity tracking.");
        near.Received.Clear();
        far.Received.Clear();
        Server.RunOnGameThread(() =>
        {
            Server.Api.World.PlaySoundAt(new AssetLocation("game:sounds/block/planks"), center.X, center.Y, center.Z, null, false, 32, 1);
            Server.Api.World.SpawnParticles(new SimpleParticleProperties(5, 5, ColorUtil.WhiteArgb, center, center.Clone(), new Vec3f(), new Vec3f()));
            // These APIs broadcast globally on 1.22.7. Tracking/radius snapshots must not pretend otherwise.
            Server.Api.Network.BroadcastEntityPacket(near.Entity!.EntityId, 359, new byte[] { 1 });
            Server.Api.Network.BroadcastBlockEntityPacket(center.AsBlockPos, 359, new byte[] { 1 });
        });
        Assert.True(await Server.TryTickUntilAsync(() =>
            near.Received.Particles.Count > 0 && near.Received.Sounds.Count > 0 &&
            far.Received.CountsById.GetValueOrDefault(67) > 0 && far.Received.CountsById.GetValueOrDefault(44) > 0));
        Assert.Empty(far.Received.Sounds);
        Assert.Empty(far.Received.Particles);
        Assert.True(near.Received.CountsById.GetValueOrDefault(67) > 0);
        Assert.True(near.Received.CountsById.GetValueOrDefault(44) > 0);
        far.Sever();
        Assert.True(await Server.TryTickUntilAsync(() => !far.IsConnected));
        Assert.Throws<InvalidOperationException>(() => Server.Visibility.EntitiesKnownTo(far));
        await far.RejoinAsync();
        Assert.NotEmpty(Server.Visibility.ChunksSentTo(far));
        await far.TeleportToAsync(center.X + 2, center.Y, center.Z);
        Assert.True(await Server.TryTickUntilAsync(() => Server.Visibility.EntitiesKnownTo(near).Any(e => e.EntityId == far.Entity!.EntityId)));
    }

    [ServerScenario]
    public async Task CrowdFailure_CleansOnlyNewPlayers_AndNamesDoNotCollide()
    {
        var existing = await CreateTestPlayerAsync("Crowd1-0");
        var center = Server.DefaultSpawnPosition.XYZ;
        var grid = await CreateTestPlayersAsync(4, PlayerLayout.Grid(center, spacing: 2, columns: 2));
        Assert.Equal(4, grid.Length);
        Assert.True(existing.IsConnected);
        await Assert.ThrowsAsync<InvalidOperationException>(() => CreateTestPlayerAsync("Crowd1-0"));
        Assert.True(existing.IsConnected);
        Assert.All(grid, p => Assert.NotEqual(existing.PlayerName, p.PlayerName));
        int before = Server.TestPlayers.Count;
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Server.CreateTestPlayersAsync(2, PlayerLayout.Ring(center, 2), ct: cancelled.Token));
        Assert.Equal(before, Server.TestPlayers.Count);
        await Assert.ThrowsAnyAsync<Exception>(() => Server.CreateTestPlayersAsync(3, PlayerLayout.Ring(center, 2), maxTicks: 1));
        Assert.True(existing.IsConnected);
        Assert.Equal(before, Server.TestPlayers.Count);
        for (int tick = 0; tick < 20; tick++) { Server.Tick(); await Task.Delay(1); }
        Assert.Equal(before, Server.RunOnGameThread(() => Server.Api.World.AllOnlinePlayers.Length));
    }

    [ServerScenario]
    public async Task Recording_IsTypedScopedAndBounded_WithoutDuplicateBroadcasts()
    {
        var alice = await CreateTestPlayerAsync("RouteAlice");
        var bob = await CreateTestPlayerAsync("RouteBob");
        using var outer = Server.ModNetwork.Record("pharosnet");
        using var overflow = Server.ModNetwork.Record("pharosnet", capacity: 1);
        using (var inner = Server.ModNetwork.Record("pharosnet"))
        {
            Assert.True((await Server.Commands.ExecuteAsync("pharosnet sync 10")).Ok);
            Assert.Equal(2, inner.Sent<SyncPacket>().Count);
            Assert.Empty(inner.Sent<SyncAck>()); // Same protobuf field 1 does not mean the same registered type.
        }
        await alice.ModNetwork.SendAsync("pharosnet", new SyncPacket { Version = 11 });
        Assert.Equal(2, outer.Sent<SyncPacket>().Count);
        Assert.Single(outer.Sent<SyncAck>());
        Assert.Throws<InvalidOperationException>(() => overflow.Messages());
        var snapshot = outer.Messages();
        snapshot[0].Data[0] ^= 0xff;
        Assert.Equal(10, outer.Sent<SyncPacket>()[0].Message.Version);
        await Server.TryTickUntilAsync(() => bob.Received.ModPacketsOf<SyncPacket>("pharosnet", 0).Any(p => p.Version == 10));
        Assert.Contains(bob.Received.ModPacketsOf<SyncPacket>("pharosnet", 0), p => p.Version == 10);
        Assert.Throws<ArgumentException>(() => outer.Sent<Ping>());
        Assert.Throws<ArgumentException>(() => alice.ModNetwork.QueueSend("missing", new Ping()));
    }

    [ServerScenario]
    public async Task DirectAndExcludedBroadcasts_HaveExactlyOneRecordPerTcpOrUdpRecipient()
    {
        var alice = await CreateTestPlayerAsync("DirectAlice");
        var bob = await CreateTestPlayerAsync("DirectBob");
        Server.RunOnGameThread(() =>
        {
            Server.Api.Network.RegisterChannel("direct").RegisterMessageType<SyncPacket>().RegisterMessageType<SyncAck>();
            Server.Api.Network.RegisterUdpChannel("directudp").RegisterMessageType<Ping>();
        });
        using var log = Server.ModNetwork.Record();
        Server.RunOnGameThread(() =>
        {
            var tcp = Server.Api.Network.GetChannel("direct");
            tcp.SendPacket(new SyncPacket { Version = 1 }, alice.Player!, bob.Player!);
            tcp.BroadcastPacket(new SyncAck { Version = 2 }, alice.Player!);
            var udp = Server.Api.Network.GetUdpChannel("directudp");
            udp.SendPacket(new Ping { N = 3 }, alice.Player!, bob.Player!);
            udp.BroadcastPacket(new Ping { N = 4 }, bob.Player!);
        });
        Assert.Equal(2, log.Sent<SyncPacket>("direct").Count);
        Assert.Equal(bob.PlayerUID, Assert.Single(log.Sent<SyncAck>("direct")).Envelope.PlayerUID);
        Assert.Equal(3, log.Sent<Ping>("directudp").Count);
        Assert.Equal(alice.PlayerUID, Assert.Single(log.Sent<Ping>("directudp"), p => p.Message.N == 4).Envelope.PlayerUID);
        Server.Ticks(5); // UDP queue/fallback must not create a second observation.
        Assert.Equal(3, log.Sent<Ping>("directudp").Count);
    }

    [ServerScenario]
    public async Task SameTick_ContendsForOneRealChestStack_WithoutDuplication()
    {
        var alice = await CreateTestPlayerAsync("ChestAlice");
        var bob = await CreateTestPlayerAsync("ChestBob");
        BlockPos position = Server.RunOnGameThread(() => alice.Entity!.Pos.AsBlockPos.AddCopy(1, 1, 0));
        await bob.TeleportToAsync(position.X + 1, position.Y, position.Z + 1);
        Server.RunOnGameThread(() =>
        {
            var chest = Server.Api.World.GetBlock(new AssetLocation("game:chest-east")) ?? throw new InvalidOperationException("Vanilla chest is missing.");
            Server.Api.World.BlockAccessor.SetBlock(chest.BlockId, position);
        });
        Assert.True((await alice.Bot.UseBlockAsync(position)).Accepted);
        Assert.True((await bob.Bot.UseBlockAsync(position)).Accepted);
        var container = Server.Inventories.At(position);
        Server.RunOnGameThread(() => Assert.True(container.Fill(0, "game:rock-granite", 7)));
        var before = Server.Inventories.Totals(position, radius: 16);
        int tick = Server.TickCount;
        await Server.SameTick(() =>
        {
            alice.Bot.QueueTakeSlot(position, 0);
            bob.Bot.QueueTakeSlot(position, 0);
        });
        Assert.Equal(tick + 1, Server.TickCount);
        Assert.True(Server.RunOnGameThread(() => container.ItemAt(0) == null));
        int aliceCount = Server.RunOnGameThread(() => CountRocks(alice));
        int bobCount = Server.RunOnGameThread(() => CountRocks(bob));
        Assert.Equal(7, aliceCount + bobCount);
        Assert.Equal(7, aliceCount);
        Assert.Equal(0, bobCount);
        PharosAssert.ItemsConserved(before, Server.Inventories.Totals(position, radius: 16));

        // Refill and reverse submission: winner follows queue order, not join order.
        Server.RunOnGameThread(() => Assert.True(container.Fill(0, "game:rock-granite", 5)));
        var refilled = Server.Inventories.Totals(position, radius: 16);
        await Server.SameTick(() =>
        {
            bob.Bot.QueueTakeSlot(position, 0);
            alice.Bot.QueueTakeSlot(position, 0);
        });
        Assert.Equal(5, Server.RunOnGameThread(() => CountRocks(bob)));
        Assert.Equal(7, Server.RunOnGameThread(() => CountRocks(alice)));
        PharosAssert.ItemsConserved(refilled, Server.Inventories.Totals(position, radius: 16));
        string hotbar = Server.RunOnGameThread(() => bob.Player!.InventoryManager.GetHotbarInventory().InventoryID);
        int sourceSlot = Server.RunOnGameThread(() => Enumerable.Range(0, bob.Player!.InventoryManager.GetHotbarInventory().Count)
            .First(i => bob.Player.InventoryManager.GetHotbarInventory()[i]?.Itemstack?.Collectible.Code.ToString() == "game:rock-granite"));
        string chestId = Server.RunOnGameThread(() => ((IBlockEntityContainer)Server.Api.World.BlockAccessor.GetBlockEntity(position)).Inventory.InventoryID);
        await Server.SameTick(() => bob.Bot.QueueMoveSlot(hotbar, sourceSlot, chestId, 0, quantity: 2));
        Assert.Equal(2, Server.RunOnGameThread(() => container.ItemAt(0)!.StackSize));
        Assert.Equal(3, Server.RunOnGameThread(() => CountRocks(bob)));
        Assert.Equal(7, Server.RunOnGameThread(() => CountRocks(alice)));
        PharosAssert.ItemsConserved(refilled, Server.Inventories.Totals(position, radius: 16));
    }

    [ServerScenario]
    public async Task SameTick_RejectsTicksNestingUdpAndFutureScripts_WithoutLeakingPackets()
    {
        var player = await CreateTestPlayerAsync("StrictBatch");
        using var log = Server.ModNetwork.Record();
        await Assert.ThrowsAsync<InvalidOperationException>(() => Server.SameTick(() => Server.Tick()));
        await Assert.ThrowsAsync<InvalidOperationException>(() => Server.SameTick(() => Server.SameTick(() => { }).GetAwaiter().GetResult()));
        await Assert.ThrowsAsync<InvalidOperationException>(() => Server.SameTick(() => player.ModNetwork.QueueSend("pharosnetudp", new Ping())));
        player.Network.Run(NetworkScript.Begin(DegradedNetworkProfile.None).Then(1000, new DegradedNetworkProfile(LatencyMs: 50)));
        await Assert.ThrowsAsync<InvalidOperationException>(() => Server.SameTick(() => player.ModNetwork.QueueSend("pharosnet", new SyncPacket())));
        player.Network.Simulator.Reset();
        using (player.Network.Simulator.Filter(p => true))
            await Assert.ThrowsAsync<InvalidOperationException>(() => Server.SameTick(() => player.ModNetwork.QueueSend("pharosnet", new SyncPacket())));
        Assert.Empty(log.Messages());
        await player.ModNetwork.SendAsync("pharosnet", new SyncPacket { Version = 99 });
        Assert.Equal(99, Assert.Single(log.Received<SyncPacket>()).Message.Version);
        player.Network.Configure(new DegradedNetworkProfile(LatencyMs: 200));
        player.ModNetwork.QueueSend("pharosnet", new SyncPacket { Version = 100 });
        player.Network.Configure(DegradedNetworkProfile.None);
        await Assert.ThrowsAsync<InvalidOperationException>(() => Server.SameTick(() => player.ModNetwork.QueueSend("pharosnet", new SyncPacket { Version = 101 })));
        Assert.True(await Server.TryTickUntilAsync(() => log.Received<SyncPacket>().Any(m => m.Message.Version == 100)));
        await Server.SameTick(() => player.ModNetwork.QueueSend("pharosnet", new SyncPacket { Version = 102 }));
        Assert.Equal(new[] { 99, 100, 102 }, log.Received<SyncPacket>().Select(m => m.Message.Version));
    }

    // Puts a mod message where the engine's UDP-over-TCP handler leaves it until its UDP tick reads it.
    internal static void EnqueuePendingUdp(EmbeddedServerHost host, ServerTestPlayer bot, string channelName, SyncPacket message) => host.RunOnGameThread(() =>
    {
        var channel = host.ModNetwork.Channel(channelName, udp: true);
        var custom = new Packet_CustomPacket { ChannelId = ModChannels.Id(channel), MessageId = ModNetworkDriver.MessageId(channel, typeof(SyncPacket), null) };
        using var stream = new MemoryStream();
        Serializer.Serialize(stream, message);
        custom.SetData(stream.ToArray());
        host.Server.UdpSockets[1].EnqueuePacket(new Vintagestory.Common.Network.Packets.UdpPacket
        {
            Packet = new Packet_UdpPacket { Id = 6, ChannelPacket = custom },
            Client = host.Server.Clients[bot.Player!.ClientId],
        });
    });

    private static int CountRocks(ServerTestPlayer player) => player.Player!.InventoryManager.Inventories.Values
        .Where(inv => inv.ClassName != Vintagestory.API.Config.GlobalConstants.creativeInvClassName && inv.InventoryID.EndsWith(player.PlayerUID, StringComparison.Ordinal))
        .SelectMany(inv => inv).Where(slot => slot?.Itemstack?.Collectible.Code.ToString() == "game:rock-granite")
        .Sum(slot => slot.StackSize);

    [ProtoContract]
    public sealed class SyncPacket
    {
        [ProtoMember(1)] public int Version { get; set; }
        [ProtoMember(2)] public string Text { get; set; } = "";
    }

    // Serializing blocks until the test releases it.
    [ProtoContract]
    public sealed class BlockingPacket
    {
        public readonly ManualResetEventSlim Entered = new(), Released = new();
        public bool Blocks;
        public int ThreadId;
        private int _value;

        [ProtoMember(1)]
        public int Value
        {
            get
            {
                if (Blocks) { ThreadId = Environment.CurrentManagedThreadId; Entered.Set(); Released.Wait(TimeSpan.FromMinutes(1)); }
                return _value;
            }
            set => _value = value;
        }
    }

    [ProtoContract]
    public sealed class SyncAck
    {
        [ProtoMember(1)] public int Version { get; set; }
    }

    [ProtoContract]
    public sealed class Ping
    {
        [ProtoMember(1)] public int N { get; set; }
    }
}
