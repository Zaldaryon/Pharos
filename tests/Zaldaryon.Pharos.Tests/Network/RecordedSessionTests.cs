using Xunit;
using Zaldaryon.Pharos.Network;

namespace Zaldaryon.Pharos.Tests.Network;

/// <summary>Loading a recording: the format from before frames were kept, the format now, and the order check.</summary>
public class RecordedSessionTests
{
    [Fact]
    public void AnOlderRecording_LoadsWithoutFrames()
    {
        const string older = """
            [
              { "packetId": 2, "direction": "inbound", "timestampMs": 10, "payloadBase64": "AQI=", "packetTypeName": "Test" }
            ]
            """;

        RecordedSession session = RecordedSession.FromJson(older);

        RecordedPacket packet = Assert.Single(session.Packets);
        Assert.Equal(-1, packet.Frame);
        Assert.Equal(PacketTransport.Tcp, packet.Transport);
        Assert.False(session.HasFrames);
        Assert.Equal(new byte[] { 1, 2 }, packet.GetPayloadBytes());
    }

    [Fact]
    public void FramesAndTransports_RoundTripThroughTheRecorder()
    {
        PacketRecorder recorder = new();
        recorder.Start();
        recorder.Record(2, PacketDirection.Outbound, [1, 2], frame: 7, PacketTransport.Udp, "Position");
        recorder.Record(5, PacketDirection.Inbound, [3], frame: 7, PacketTransport.Tcp);
        recorder.Stop();

        RecordedSession session = RecordedSession.FromJson(recorder.ToJson());

        Assert.True(session.HasFrames);
        Assert.Equal(new[] { 7L, 7L }, session.Packets.Select(p => p.Frame));
        Assert.Equal(PacketTransport.Udp, session.Packets[0].Transport);
        Assert.Equal("Position", session.Packets[0].PacketTypeName);
    }

    [Fact]
    public void FramesThatGoBackwards_AreRefused()
    {
        PacketRecorder recorder = new();
        recorder.Start();
        recorder.Record(2, PacketDirection.Outbound, [1], frame: 9, PacketTransport.Udp);
        recorder.Record(2, PacketDirection.Outbound, [1], frame: 4, PacketTransport.Udp);
        recorder.Stop();

        Assert.Throws<InvalidOperationException>(() => RecordedSession.FromJson(recorder.ToJson()));
    }
}
