using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Vintagestory.API.Server;
using Vintagestory.Server;
using Xunit;
using Zaldaryon.Pharos.Platform;
using Zaldaryon.Pharos.Server;

namespace Zaldaryon.Pharos.Tests.Server;

[Collection("Sequential")]
public sealed class TcpProtocolTestPlayerTests
{
    private static readonly TimeSpan PhaseTimeout = TimeSpan.FromSeconds(20);

    static TcpProtocolTestPlayerTests() => HeadlessPlatformResolver.Initialize();

    [Fact]
    public void TwoIndependentTcpPlayers_JoinMoveObserveAndDisconnect()
    {
        using EmbeddedServerHost host = EmbeddedServerHost.Boot(new ServerWorldOptions
        {
            WorldName = "PharosTcpProtocolPlayers",
            Seed = "tcp-protocol-players",
            PlayStyle = "creativebuilding",
            WorldType = "standard"
        });

        int port = host.StartNativeTestListeners();
        Assert.NotNull(host.Server.MainSockets[0]);
        Assert.IsType<DummyTcpNetServer>(host.Server.MainSockets[0]);
        Assert.NotNull(host.Server.UdpSockets[0]);

        using TcpProtocolTestPlayer first = new("PharosBotA", "pharos-" + Guid.NewGuid().ToString("N"));
        using TcpProtocolTestPlayer second = new("PharosBotB", "pharos-" + Guid.NewGuid().ToString("N"));
        TcpProtocolTestPlayer[] players = [first, second];
        Dictionary<TcpProtocolTestPlayer, List<Packet_Server>> tcpPackets = players.ToDictionary(p => p, _ => new List<Packet_Server>());
        Dictionary<TcpProtocolTestPlayer, List<Packet_UdpPacket>> udpPackets = players.ToDictionary(p => p, _ => new List<Packet_UdpPacket>());

        first.Connect("127.0.0.1", port);
        second.Connect("127.0.0.1", port);
        WaitFor(host, players, tcpPackets, udpPackets,
            () => players.All(p => p.ConnectionCompleted), "TCP connect callbacks");
        Assert.All(players, p => Assert.Null(p.ConnectFailure));

        first.SendLoginTokenQuery();
        second.SendLoginTokenQuery();
        WaitFor(host, players, tcpPackets, udpPackets,
            () => players.All(p => tcpPackets[p].Any(packet => packet.Id == 77 && !string.IsNullOrEmpty(packet.Token?.Token))),
            "login challenge packet 77");

        string firstUdpToken = tcpPackets[first].Last(packet => packet.Id == 77).Token!.Token;
        string secondUdpToken = tcpPackets[second].Last(packet => packet.Id == 77).Token!.Token;
        first.RequestTcpPositionFallback();
        second.RequestTcpPositionFallback();
        first.ConnectUdp("127.0.0.1", port, firstUdpToken);
        second.ConnectUdp("127.0.0.1", port, secondUdpToken);
        first.SendIdentification();
        second.SendIdentification();

        WaitFor(host, players, tcpPackets, udpPackets,
            () => players.All(p => tcpPackets[p].Any(packet => packet.Id == 73)), "server ready packet 73");

        first.RequestJoin();
        second.RequestJoin();
        WaitFor(host, players, tcpPackets, udpPackets,
            () => players.All(p => tcpPackets[p].Any(packet => packet.Id == 6)), "level finalize packet 6");

        first.SignalClientLoaded();
        second.SignalClientLoaded();
        Pump(host, players, tcpPackets, udpPackets, 3);
        first.SignalClientPlaying();
        second.SignalClientPlaying();

        PlayerSnapshot[] joined = WaitForPlayers(host, players, tcpPackets, udpPackets,
            state => state.Length == 2 && state.All(p => p.State == "Playing" && p.EntityId != 0),
            "both server clients in Playing with player entities");
        Assert.Equal(2, joined.Select(p => p.ClientId).Distinct().Count());
        Assert.Equal(2, joined.Select(p => p.EntityId).Distinct().Count());
        Assert.All(joined, p => Assert.True(p.ServerDidReceiveUdp, $"{p.Name} did not complete UDP endpoint association."));
        Assert.All(joined, p => Assert.True(p.SpawnChunkLoaded, $"{p.Name}'s generated spawn chunk is not loaded."));

        first.SetForward(true);
        second.SetForward(false);
        PlayerSnapshot[] controls = WaitForPlayers(host, players, tcpPackets, udpPackets,
            state => state.Length == 2 && state.Single(p => p.Name == first.PlayerName).Forward && !state.Single(p => p.Name == second.PlayerName).Forward,
            "independent MoveKeyChange packet 21 controls");

        PlayerSnapshot firstStart = controls.Single(p => p.Name == first.PlayerName);
        PlayerSnapshot secondStart = controls.Single(p => p.Name == second.PlayerName);
        for (int step = 1; step <= 12; step++)
        {
            first.SendPositionTcp(TcpProtocolTestPlayer.Position(
                firstStart.EntityId, firstStart.X + (step * 0.1), firstStart.Y, firstStart.Z, step, firstStart.Controls));
            second.SendPositionUdp(TcpProtocolTestPlayer.Position(
                secondStart.EntityId, secondStart.X, secondStart.Y, secondStart.Z + (step * 0.1), step, secondStart.Controls));
            Pump(host, players, tcpPackets, udpPackets, 2);
        }

        PlayerSnapshot[] moved = WaitForPlayers(host, players, tcpPackets, udpPackets,
            state => state.Length == 2 &&
                     Math.Abs(state.Single(p => p.Name == first.PlayerName).X - firstStart.X) > 1.0 &&
                     Math.Abs(state.Single(p => p.Name == second.PlayerName).Z - secondStart.Z) > 1.0,
            "server-observed independent entity position updates (TCP fallback 35/UDP 2 and UDP 2)");

        PlayerSnapshot firstEnd = moved.Single(p => p.Name == first.PlayerName);
        PlayerSnapshot secondEnd = moved.Single(p => p.Name == second.PlayerName);
        Assert.InRange(Math.Abs(firstEnd.Z - firstStart.Z), 0, 0.1);
        Assert.InRange(Math.Abs(secondEnd.X - secondStart.X), 0, 0.1);
        Assert.True(tcpPackets[first].Any(p => p.Id == 79 && p.UdpPacket?.Id == 5 && p.UdpPacket.EntityPosition?.EntityId == secondStart.EntityId),
            Diagnostic("first observer did not receive the second player's relayed position packet", first, second, tcpPackets));
        Assert.True(tcpPackets[second].Any(p => p.Id == 79 && p.UdpPacket?.Id == 5 && p.UdpPacket.EntityPosition?.EntityId == firstEnd.EntityId),
            Diagnostic("second observer did not receive the first player's relayed position packet", first, second, tcpPackets));

        first.Leave();
        second.Leave();
        WaitForPlayers(host, players, tcpPackets, udpPackets,
            state => state.Length == 0 || state.All(p => p.State == "Offline"),
            "both TCP clients disconnecting");
    }

    private static void WaitFor(
        EmbeddedServerHost host,
        TcpProtocolTestPlayer[] players,
        Dictionary<TcpProtocolTestPlayer, List<Packet_Server>> tcpPackets,
        Dictionary<TcpProtocolTestPlayer, List<Packet_UdpPacket>> udpPackets,
        System.Func<bool> condition,
        string phase)
    {
        Stopwatch timer = Stopwatch.StartNew();
        while (!condition() && timer.Elapsed < PhaseTimeout)
        {
            Pump(host, players, tcpPackets, udpPackets, 1);
        }

        Assert.True(condition(), Diagnostic($"Timed out during {phase} after {timer.Elapsed}.", players, tcpPackets));
    }

    private static PlayerSnapshot[] WaitForPlayers(
        EmbeddedServerHost host,
        TcpProtocolTestPlayer[] players,
        Dictionary<TcpProtocolTestPlayer, List<Packet_Server>> tcpPackets,
        Dictionary<TcpProtocolTestPlayer, List<Packet_UdpPacket>> udpPackets,
        System.Func<PlayerSnapshot[], bool> condition,
        string phase)
    {
        Stopwatch timer = Stopwatch.StartNew();
        PlayerSnapshot[] current = [];
        while (timer.Elapsed < PhaseTimeout)
        {
            current = CapturePlayers(host, players, tcpPackets, udpPackets);
            if (condition(current)) return current;
            Thread.Sleep(10);
        }

        Assert.Fail(Diagnostic($"Timed out during {phase} after {timer.Elapsed}. Current server players: {Format(current)}.", players, tcpPackets));
        return current;
    }

    private static PlayerSnapshot[] CapturePlayers(
        EmbeddedServerHost host,
        TcpProtocolTestPlayer[] players,
        Dictionary<TcpProtocolTestPlayer, List<Packet_Server>> tcpPackets,
        Dictionary<TcpProtocolTestPlayer, List<Packet_UdpPacket>> udpPackets)
    {
        Task<PlayerSnapshot[]> read = host.RunOnGameThreadAsync(() => host.Server.Clients.Values
            .Where(client => client.Player is not null && players.Any(player => player.PlayerName == client.PlayerName))
            .Select(client => new PlayerSnapshot(
                client.PlayerName,
                client.Id,
                client.State.ToString(),
                client.Entityplayer?.EntityId ?? 0,
                client.Entityplayer?.Pos.X ?? 0,
                client.Entityplayer?.Pos.Y ?? 0,
                client.Entityplayer?.Pos.Z ?? 0,
                client.Entityplayer?.Controls.Forward ?? false,
                client.Entityplayer?.Controls.ToInt() ?? 0,
                client.ServerDidReceiveUdp,
                host.Server.Api is ICoreServerAPI api && api.World.BlockAccessor.GetChunk(
                    (int)Math.Floor((client.Entityplayer?.Pos.X ?? 0) / 32),
                    (int)Math.Floor((client.Entityplayer?.Pos.Y ?? 0) / 32),
                    (int)Math.Floor((client.Entityplayer?.Pos.Z ?? 0) / 32)) is not null))
            .ToArray());

        Pump(host, players, tcpPackets, udpPackets, 1);
        Assert.True(read.Wait(TimeSpan.FromSeconds(2)), "Game-thread snapshot did not complete within two seconds.");
        return read.GetAwaiter().GetResult();
    }

    private static void Pump(
        EmbeddedServerHost host,
        TcpProtocolTestPlayer[] players,
        Dictionary<TcpProtocolTestPlayer, List<Packet_Server>> tcpPackets,
        Dictionary<TcpProtocolTestPlayer, List<Packet_UdpPacket>> udpPackets,
        int ticks)
    {
        for (int i = 0; i < ticks; i++)
        {
            host.Tick();
            foreach (TcpProtocolTestPlayer player in players)
            {
                tcpPackets[player].AddRange(player.DrainTcpPackets());
                udpPackets[player].AddRange(player.DrainUdpPackets());
            }
            Thread.Sleep(12);
        }
    }

    private static string Diagnostic(string message, TcpProtocolTestPlayer[] players, Dictionary<TcpProtocolTestPlayer, List<Packet_Server>> packets)
    {
        return message + " " + string.Join("; ", players.Select(player =>
            $"{player.PlayerName}: connect={player.ConnectionResult ?? "pending"}, failure={player.ConnectFailure ?? "none"}, disconnect={player.DisconnectError?.Message ?? "none"}, serverPacketIds=[{string.Join(',', packets[player].Select(packet => packet.Id).TakeLast(16))}]"));
    }

    private static string Diagnostic(string message, TcpProtocolTestPlayer first, TcpProtocolTestPlayer second, Dictionary<TcpProtocolTestPlayer, List<Packet_Server>> packets) =>
        Diagnostic(message, [first, second], packets);

    private static string Format(IEnumerable<PlayerSnapshot> players) => string.Join("; ", players.Select(p =>
        $"{p.Name}(client={p.ClientId}, state={p.State}, entity={p.EntityId}, pos={p.X:F3}/{p.Y:F3}/{p.Z:F3}, forward={p.Forward}, udp={p.ServerDidReceiveUdp})"));

    private sealed record PlayerSnapshot(
        string Name,
        int ClientId,
        string State,
        long EntityId,
        double X,
        double Y,
        double Z,
        bool Forward,
        int Controls,
        bool ServerDidReceiveUdp,
        bool SpawnChunkLoaded);
}
