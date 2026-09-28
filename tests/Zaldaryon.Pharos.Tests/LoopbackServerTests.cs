using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using Zaldaryon.Pharos.Bootstrap;
using Zaldaryon.Pharos.Core;
using Zaldaryon.Pharos.Platform;
using Zaldaryon.Pharos.Server;

namespace Zaldaryon.Pharos.Tests;

[Collection("Sequential")]
public class LoopbackServerTests
{
    static LoopbackServerTests()
    {
        HeadlessPlatformResolver.Initialize();
    }

    [Fact]
    public void EmbeddedServerHost_Should_BootAndDisposeCleanly()
    {
        ServerWorldOptions options = new()
        {
            WorldName = "PharosUnitTestWorld",
            Seed = "12345",
            PlayStyle = "creativebuilding",
            WorldType = "superflat"
        };

        using EmbeddedServerHost host = EmbeddedServerHost.Boot(options);

        Assert.True(host.IsRunning);
        Assert.NotNull(host.Server);
        Assert.NotNull(host.TcpNetwork);
        Assert.NotNull(host.UdpNetwork);
        Assert.True(Directory.Exists(host.DataPath));

        host.Tick();
        host.Ticks(5);

        Assert.True(host.IsRunning);
    }

    [Fact]
    public async Task EmbeddedServerHost_Tick_ShouldPreserveProfilerAcrossThreads()
    {
        ServerWorldOptions worldOptions = new()
        {
            WorldName = "PharosCrossThreadTickWorld",
            Seed = "24680",
            PlayStyle = "creativebuilding",
            WorldType = "superflat"
        };

        using EmbeddedServerHost server = EmbeddedServerHost.Boot(worldOptions);

        await Task.Run(server.Tick);
        server.Tick();

        Assert.True(server.IsRunning);
        Assert.Equal(2, server.TickCount);
    }

    [Fact(Skip = "Reduced headless client is not fully initialized for ServerReady.StartModsFully; TCP protocol multiplayer is covered by TcpProtocolTestPlayerTests.")]
    public void LoopbackSession_Should_ConnectClientToServerAndStepInLockstep()
    {
        ServerWorldOptions worldOptions = new()
        {
            WorldName = "PharosLockstepWorld",
            Seed = "54321",
            PlayStyle = "creativebuilding",
            WorldType = "superflat"
        };

        using EmbeddedServerHost server = EmbeddedServerHost.Boot(worldOptions);

        HeadlessClientOptions clientOptions = new()
        {
            Width = 1280,
            Height = 720,
            DisableAudio = true
        };

        using HeadlessClient client = HeadlessClientBootstrap.Boot(clientOptions);

        using ClientServerLoopbackSession session = client.ConnectLoopback(server, "TestPilot");

        Assert.NotNull(session);
        Assert.Same(client, session.Client);
        Assert.Same(server, session.NativeServer);
        Assert.NotNull(session.Client.Client.MainNetClient);
        Assert.NotNull(session.Client.Client.UdpNetClient);

        // Advance several frames in lockstep
        session.Step(1f / 60f);
        session.StepFrames(10, 1f / 60f);

        Assert.True(server.IsRunning);
        Assert.False(client.IsDisposed);
    }

    [Fact(Skip = "Reduced headless client is not fully initialized for ServerReady.StartModsFully; TCP protocol multiplayer is covered by TcpProtocolTestPlayerTests.")]
    public async Task LoopbackSession_Should_StepAsynchronouslyInLockstep()
    {
        ServerWorldOptions worldOptions = new()
        {
            WorldName = "PharosAsyncWorld",
            Seed = "98765",
            PlayStyle = "creativebuilding",
            WorldType = "superflat"
        };

        using EmbeddedServerHost server = EmbeddedServerHost.Boot(worldOptions);

        HeadlessClientOptions clientOptions = new()
        {
            Width = 1280,
            Height = 720,
            DisableAudio = true
        };

        using HeadlessClient client = HeadlessClientBootstrap.Boot(clientOptions);

        using ClientServerLoopbackSession session = client.ConnectLoopback(server, "AsyncPilot");

        Assert.NotNull(session.Client.Client.MainNetClient);

        await session.StepAsync(1f / 60f);
        await session.StepFramesAsync(5, 1f / 60f);

        Assert.True(server.IsRunning);
        Assert.False(client.IsDisposed);
    }

    [Fact(Skip = "Native headless client receives the loaded-level handshake but does not send ClientPlaying (packet 29); real TCP protocol-player coverage is in TcpProtocolTestPlayerTests.")]
    public void LoopbackSession_ShouldCompleteNativeJoinAndReachPlayingState()
    {
        ServerWorldOptions worldOptions = new()
        {
            WorldName = "PharosJoinWorld",
            Seed = "13579",
            PlayStyle = "creativebuilding",
            WorldType = "superflat"
        };

        using EmbeddedServerHost server = EmbeddedServerHost.Boot(worldOptions);

        HeadlessClientOptions clientOptions = new()
        {
            Width = 1280,
            Height = 720,
            DisableAudio = true,
            StartNativeClientLifecycle = true
        };

        using HeadlessClient client = HeadlessClientBootstrap.Boot(clientOptions);

        using ClientServerLoopbackSession session = client.ConnectLoopback(server, "JoinPilot");

        DateTime joinDeadline = DateTime.UtcNow + TimeSpan.FromSeconds(60);
        HashSet<string> observedStates = new(StringComparer.Ordinal);
        while (DateTime.UtcNow < joinDeadline && !server.Server.Clients.Values.Any(
                   connectedClient => connectedClient.PlayerName == "JoinPilot"
                       && connectedClient.State.ToString() == "Playing"))
        {
            session.Step();
            foreach (var joinedClient in server.Server.Clients.Values.Where(connectedClient => connectedClient.PlayerName == "JoinPilot"))
            {
                observedStates.Add(joinedClient.State.ToString());
            }
            Thread.Sleep(1);
        }

        Assert.NotNull(client.Client.player);
        var playingClient = server.Server.Clients.Values.SingleOrDefault(
            connectedClient => connectedClient.PlayerName == "JoinPilot"
                && connectedClient.State.ToString() == "Playing");
        Assert.True(playingClient != null, $"JoinPilot did not reach Playing; observed states: {string.Join(", ", observedStates)}; client spawned={client.Client.Spawned}; ready-event fired={client.Client.clientPlayingFired}.");
        Assert.NotNull(playingClient.Entityplayer);
        Assert.True(server.IsRunning);
        Assert.False(client.IsDisposed);
    }

    [Fact(Skip = "Reduced headless client is not fully initialized for ServerReady.StartModsFully; TCP protocol multiplayer is covered by TcpProtocolTestPlayerTests.")]
    public void LoopbackSession_Should_CleanUpOnDisconnectionWithoutResourceLeaks()
    {
        string? dataPath;

        {
            using EmbeddedServerHost server = EmbeddedServerHost.Boot();
            dataPath = server.DataPath;

            using HeadlessClient client = HeadlessClientBootstrap.Boot(new HeadlessClientOptions { DisableAudio = true });
            using ClientServerLoopbackSession session = client.ConnectLoopback(server, "CleanupPilot");

            session.StepFrames(5);
            Assert.NotNull(session.Client.Client.MainNetClient);

            // Rely on IDisposable on session and client to verify disposal sequence without manual Disconnect call
        }

        Assert.False(Directory.Exists(dataPath));
    }
}
