using System.Reflection;
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

    [Fact]
    public void BothDirectUdpAndInlineTcpPackets_AreDeliveredTogether()
    {
        LoopbackLink link = new(new PacketRecorder(), new NetworkDegradationSimulator());
        LinkedUdpNetClient udp = new(link);
        DummyNetwork network = new();
        network.Start();
        udp.SetNetwork(network);

        Packet_UdpPacket directPacket = new() { Id = 10 };
        EnqueueDirectUdp(network, directPacket);

        Packet_UdpPacket inlinePacket = new() { Id = 20 };
        udp.EnqueuePacket(inlinePacket);

        List<Packet_UdpPacket> delivered = [.. udp.ReadMessage()];
        Assert.Equal(2, delivered.Count);
        Assert.Contains(directPacket, delivered);
        Assert.Contains(inlinePacket, delivered);
        Assert.Null(udp.ReadMessage());
    }

    [Fact]
    public void ConfiguredDegradation_DelaysDirectUdpWhileInlineTcpDelivers()
    {
        NetworkDegradationSimulator degradation = new();
        degradation.Configure(new DegradedNetworkProfile(LatencyMs: 500));
        LoopbackLink link = new(new PacketRecorder(), degradation);
        LinkedUdpNetClient udp = new(link);
        DummyNetwork network = new();
        network.Start();
        udp.SetNetwork(network);

        Packet_UdpPacket directPacket = new() { Id = 100 };
        EnqueueDirectUdp(network, directPacket);

        Packet_UdpPacket inlinePacket = new() { Id = 200 };
        udp.EnqueuePacket(inlinePacket);

        // Direct UDP has 500ms latency configured on the link and clock has not advanced,
        // while inline UDP was already routed over TCP and enqueued directly.
        Packet_UdpPacket read = Assert.Single(udp.ReadMessage());
        Assert.Same(inlinePacket, read);

        // Once the link clock advances beyond latency, direct UDP is delivered.
        link.Advance(600, _ => { }, _ => { });
        Packet_UdpPacket delayedDirect = Assert.Single(udp.ReadMessage());
        Assert.Same(directPacket, delayedDirect);
    }

    [Fact]
    public void SeveredLink_ClearsAndDoesNotDeliverEnqueuedPackets()
    {
        LoopbackLink link = new(new PacketRecorder(), new NetworkDegradationSimulator());
        LinkedUdpNetClient udp = new(link);
        DummyNetwork network = new();
        network.Start();
        udp.SetNetwork(network);

        udp.EnqueuePacket(new Packet_UdpPacket { Id = 1 });
        link.Sever("connection severed");

        // Stale enqueued packets are cleared and not delivered after sever.
        Assert.Null(udp.ReadMessage());

        // Subsequent enqueued packets on severed link are dropped.
        udp.EnqueuePacket(new Packet_UdpPacket { Id = 2 });
        Assert.Null(udp.ReadMessage());
    }

    [Fact]
    public void HeldServerTraffic_ClearsAndDoesNotDeliverEnqueuedPackets()
    {
        LoopbackLink link = new(new PacketRecorder(), new NetworkDegradationSimulator());
        LinkedUdpNetClient udp = new(link);
        DummyNetwork network = new();
        network.Start();
        udp.SetNetwork(network);

        udp.EnqueuePacket(new Packet_UdpPacket { Id = 3 });
        link.HoldServerTraffic = true;

        // While server traffic is held, enqueued packets are cleared and not delivered.
        Assert.Null(udp.ReadMessage());

        // Subsequent enqueued packets while held are ignored.
        udp.EnqueuePacket(new Packet_UdpPacket { Id = 4 });
        Assert.Null(udp.ReadMessage());
    }

    private static void EnqueueDirectUdp(DummyNetwork network, Packet_UdpPacket packet)
    {
        object clientLock = typeof(DummyNetwork).GetField("ClientReceiveBufferLock", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)!.GetValue(network)!;
        object buffer = typeof(DummyNetwork).GetField("ClientReceiveBuffer", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)!.GetValue(network)!;
        lock (clientLock)
        {
            if (buffer is Queue<Packet_UdpPacket> q)
            {
                q.Enqueue(packet);
            }
            else
            {
                buffer.GetType().GetMethod("Enqueue")?.Invoke(buffer, [packet]);
            }
        }
    }
}
