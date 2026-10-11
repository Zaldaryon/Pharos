using Vintagestory.Common;
using Xunit;
using Zaldaryon.Pharos.Network;

namespace Zaldaryon.Pharos.Tests.Network;

/// <summary>The client's UDP socket on a <see cref="LoopbackLink"/>, without a game.</summary>
public class LinkedUdpNetClientTests
{
    [Fact]
    public void ADatagramTheServerSentOverTcp_IsReadLikeAnyOther()
    {
        LinkedUdpNetClient udp = new(new LoopbackLink(new PacketRecorder(), new NetworkDegradationSimulator()));
        DummyNetwork network = new();
        network.Start();
        udp.SetNetwork(network);
        Packet_UdpPacket packet = new() { Id = 7 };

        udp.EnqueuePacket(packet);

        Assert.Same(packet, Assert.Single(udp.ReadMessage()));
        Assert.Null(udp.ReadMessage());
    }
}
