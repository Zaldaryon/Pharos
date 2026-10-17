using Vintagestory.API.MathTools;
using Vintagestory.Common;
using Xunit;
using Zaldaryon.Pharos.Network;
using Zaldaryon.Pharos.Player;
using Zaldaryon.Pharos.XUnit;

namespace Zaldaryon.Pharos.Tests.Server;

/// <summary>A recording of the engine client's walk, replayed into a headless player on the real server.</summary>
[Collection("Sequential")]
public class ServerReplayTests : ClientServerScenarioBase
{
    [ClientServerScenario]
    public async Task ARecordedWalk_MovesAReplayedPlayerToItsEnd()
    {
        await Session!.StepFramesAsync(30);
        Client!.PacketRecorder.Start();
        await Session.HoldAsync(PlayerAction.Forward, 60);
        Client.PacketRecorder.Stop();

        RecordedSession recording = RecordedSession.From(Client.PacketRecorder);
        Vec3d end = LastRecordedPosition(recording);

        TrafficReplayResult result = await ServerHost!.Replay.ClientTrafficAsync(recording, asPlayer: "Alice");

        Assert.True(result.Delivered > 0, "The replay delivered nothing");
        Assert.True(result.Frames > 0, "The replay stepped no frames");
        ServerTestPlayer alice = ServerHost.TestPlayers.Single(p => p.PlayerName == "Alice");
        Vec3d at = ServerHost.RunOnGameThread(() => alice.Entity!.Pos.XYZ.Clone());
        double distance = Math.Sqrt(Math.Pow(at.X - end.X, 2) + Math.Pow(at.Y - end.Y, 2) + Math.Pow(at.Z - end.Z, 2));
        Assert.True(distance < 0.5, $"The replayed player stopped {distance:0.00} blocks from the recorded end");
    }

    private static Vec3d LastRecordedPosition(RecordedSession recording)
    {
        RecordedPacket last = recording.Packets.Last(p => p.Direction == PacketDirection.Outbound && p.Transport == PacketTransport.Udp && p.PacketId == 2);
        byte[] bytes = last.GetPayloadBytes();
        Packet_UdpPacket udp = Packet_UdpPacketSerializer.DeserializeBuffer(bytes, bytes.Length, new Packet_UdpPacket());
        return new Vec3d(
            CollectibleNet.DeserializeDoublePrecise(udp.EntityPosition.X),
            CollectibleNet.DeserializeDoublePrecise(udp.EntityPosition.Y),
            CollectibleNet.DeserializeDoublePrecise(udp.EntityPosition.Z));
    }
}
