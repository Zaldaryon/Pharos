using System.Reflection;
using Xunit;
using Zaldaryon.Pharos.Assertions;
using Zaldaryon.Pharos.Network;

namespace Zaldaryon.Pharos.Tests.Network;

/// <summary>
/// Headless-safe tests for <see cref="PacketTracer"/> packet capture and assertion functionality.
/// Tests validate tracing lifecycle, packet capture, and assertion methods
/// without requiring a live server/client boot.
/// </summary>
public class PacketTracerTests
{
    private const BindingFlags PublicInstance = BindingFlags.Instance | BindingFlags.Public;

    #region Tracing Lifecycle Tests

    [Fact]
    public void PacketTracer_IsNotTracingByDefault()
    {
        var tracer = new PacketTracer();
        Assert.False(tracer.IsTracing);
    }

    [Fact]
    public void PacketTracer_StartTracing_SetsIsTracing()
    {
        var tracer = new PacketTracer();
        tracer.StartTracing();
        Assert.True(tracer.IsTracing);
    }

    [Fact]
    public void PacketTracer_StopTracing_ClearsIsTracing()
    {
        var tracer = new PacketTracer();
        tracer.StartTracing();
        tracer.StopTracing();
        Assert.False(tracer.IsTracing);
    }

    [Fact]
    public void PacketTracer_StartTracing_ClearsPreviousCaptures()
    {
        var tracer = new PacketTracer();

        tracer.StartTracing();
        tracer.RecordClientOutbound(1);
        tracer.RecordClientOutbound(2);
        tracer.StopTracing();
        Assert.Equal(2, tracer.CapturedPacketCount);

        tracer.StartTracing(); // Should clear
        Assert.Equal(0, tracer.CapturedPacketCount);
    }

    #endregion

    #region Packet Recording Tests

    [Fact]
    public void PacketTracer_RecordClientOutbound_IncreasesCount()
    {
        var tracer = new PacketTracer();
        tracer.StartTracing();

        tracer.RecordClientOutbound(1);
        Assert.Equal(1, tracer.ClientOutboundCount);

        tracer.RecordClientOutbound(2);
        Assert.Equal(2, tracer.ClientOutboundCount);
    }

    [Fact]
    public void PacketTracer_RecordServerOutbound_IncreasesCount()
    {
        var tracer = new PacketTracer();
        tracer.StartTracing();

        tracer.RecordServerOutbound(100);
        Assert.Equal(1, tracer.ServerOutboundCount);

        tracer.RecordServerOutbound(101);
        Assert.Equal(2, tracer.ServerOutboundCount);
    }

    [Fact]
    public void PacketTracer_RecordPacket_ClientDirection_RecordsCorrectly()
    {
        var tracer = new PacketTracer();
        tracer.StartTracing();

        tracer.RecordPacket(42, PacketDirection.Outbound);

        Assert.Equal(1, tracer.ClientOutboundCount);
        Assert.Equal(0, tracer.ServerOutboundCount);
    }

    [Fact]
    public void PacketTracer_RecordPacket_ServerDirection_RecordsCorrectly()
    {
        var tracer = new PacketTracer();
        tracer.StartTracing();

        tracer.RecordPacket(42, PacketDirection.Inbound);

        Assert.Equal(0, tracer.ClientOutboundCount);
        Assert.Equal(1, tracer.ServerOutboundCount);
    }

    [Fact]
    public void PacketTracer_DoesNotRecordWhenNotTracing()
    {
        var tracer = new PacketTracer();

        tracer.RecordClientOutbound(1);
        tracer.RecordServerOutbound(2);

        Assert.Equal(0, tracer.CapturedPacketCount);
    }

    [Fact]
    public void PacketTracer_CapturedPacketCount_IsTotalOfBothDirections()
    {
        var tracer = new PacketTracer();
        tracer.StartTracing();

        tracer.RecordClientOutbound(1);
        tracer.RecordClientOutbound(2);
        tracer.RecordServerOutbound(100);

        Assert.Equal(3, tracer.CapturedPacketCount);
        Assert.Equal(2, tracer.ClientOutboundCount);
        Assert.Equal(1, tracer.ServerOutboundCount);
    }

    #endregion

    #region Assertion Tests

    [Fact]
    public void AssertPacketSent_WhenPacketExists_DoesNotThrow()
    {
        var tracer = new PacketTracer();
        tracer.StartTracing();
        tracer.RecordClientOutbound(42);
        tracer.StopTracing();

        tracer.AssertPacketSent(42); // Should not throw
    }

    [Fact]
    public void AssertPacketSent_WhenPacketNotExists_ThrowsPharosAssertException()
    {
        var tracer = new PacketTracer();
        tracer.StartTracing();
        tracer.RecordClientOutbound(1);
        tracer.StopTracing();

        Assert.Throws<PharosAssertException>(() => tracer.AssertPacketSent(999));
    }

    [Fact]
    public void AssertClientSentPacket_WhenClientSent_DoesNotThrow()
    {
        var tracer = new PacketTracer();
        tracer.StartTracing();
        tracer.RecordClientOutbound(33);
        tracer.StopTracing();

        tracer.AssertClientSentPacket(33); // Should not throw
    }

    [Fact]
    public void AssertClientSentPacket_WhenOnlyServerSent_ThrowsPharosAssertException()
    {
        var tracer = new PacketTracer();
        tracer.StartTracing();
        tracer.RecordServerOutbound(33);
        tracer.StopTracing();

        Assert.Throws<PharosAssertException>(() => tracer.AssertClientSentPacket(33));
    }

    [Fact]
    public void AssertServerSentPacket_WhenServerSent_DoesNotThrow()
    {
        var tracer = new PacketTracer();
        tracer.StartTracing();
        tracer.RecordServerOutbound(100);
        tracer.StopTracing();

        tracer.AssertServerSentPacket(100); // Should not throw
    }

    [Fact]
    public void AssertServerSentPacket_WhenOnlyClientSent_ThrowsPharosAssertException()
    {
        var tracer = new PacketTracer();
        tracer.StartTracing();
        tracer.RecordClientOutbound(100);
        tracer.StopTracing();

        Assert.Throws<PharosAssertException>(() => tracer.AssertServerSentPacket(100));
    }

    [Fact]
    public void AssertPacketNotSent_WhenPacketNotSent_DoesNotThrow()
    {
        var tracer = new PacketTracer();
        tracer.StartTracing();
        tracer.RecordClientOutbound(1);
        tracer.StopTracing();

        tracer.AssertPacketNotSent(999); // Should not throw
    }

    [Fact]
    public void AssertPacketNotSent_WhenPacketWasSent_ThrowsPharosAssertException()
    {
        var tracer = new PacketTracer();
        tracer.StartTracing();
        tracer.RecordClientOutbound(42);
        tracer.StopTracing();

        Assert.Throws<PharosAssertException>(() => tracer.AssertPacketNotSent(42));
    }

    [Fact]
    public void AssertMinPacketCount_WhenEnoughPackets_DoesNotThrow()
    {
        var tracer = new PacketTracer();
        tracer.StartTracing();
        tracer.RecordClientOutbound(1);
        tracer.RecordClientOutbound(2);
        tracer.RecordClientOutbound(3);
        tracer.StopTracing();

        tracer.AssertMinPacketCount(3); // Should not throw
        tracer.AssertMinPacketCount(2); // Should not throw
    }

    [Fact]
    public void AssertMinPacketCount_WhenNotEnoughPackets_ThrowsPharosAssertException()
    {
        var tracer = new PacketTracer();
        tracer.StartTracing();
        tracer.RecordClientOutbound(1);
        tracer.StopTracing();

        Assert.Throws<PharosAssertException>(() => tracer.AssertMinPacketCount(5));
    }

    [Fact]
    public void AssertMinPacketCount_NegativeCount_ThrowsArgumentOutOfRangeException()
    {
        var tracer = new PacketTracer();
        Assert.Throws<ArgumentOutOfRangeException>(() => tracer.AssertMinPacketCount(-1));
    }

    #endregion

    #region Query Methods Tests

    [Fact]
    public void GetCapturedPackets_ReturnsAllPacketsOrdered()
    {
        var tracer = new PacketTracer();
        tracer.StartTracing();
        tracer.RecordClientOutbound(1);
        Thread.Sleep(1); // Ensure different timestamps
        tracer.RecordServerOutbound(2);
        tracer.StopTracing();

        var packets = tracer.GetCapturedPackets();

        Assert.Equal(2, packets.Count);
        Assert.True(packets[0].Timestamp <= packets[1].Timestamp);
    }

    [Fact]
    public void GetPacketsById_ReturnsOnlyMatchingPackets()
    {
        var tracer = new PacketTracer();
        tracer.StartTracing();
        tracer.RecordClientOutbound(1);
        tracer.RecordClientOutbound(42);
        tracer.RecordServerOutbound(42);
        tracer.RecordClientOutbound(2);
        tracer.StopTracing();

        var packets = tracer.GetPacketsById(42);

        Assert.Equal(2, packets.Count);
        Assert.All(packets, p => Assert.Equal(42, p.PacketId));
    }

    [Fact]
    public void GetUniquePacketIds_ReturnsDistinctIds()
    {
        var tracer = new PacketTracer();
        tracer.StartTracing();
        tracer.RecordClientOutbound(1);
        tracer.RecordClientOutbound(2);
        tracer.RecordClientOutbound(1); // Duplicate
        tracer.RecordServerOutbound(3);
        tracer.StopTracing();

        var ids = tracer.GetUniquePacketIds();

        Assert.Equal(3, ids.Count);
        Assert.Contains(1, ids);
        Assert.Contains(2, ids);
        Assert.Contains(3, ids);
    }

    [Fact]
    public void GetPacketCountsByType_ReturnsCorrectCounts()
    {
        var tracer = new PacketTracer();
        tracer.StartTracing();
        tracer.RecordClientOutbound(1);
        tracer.RecordClientOutbound(1);
        tracer.RecordClientOutbound(2);
        tracer.RecordServerOutbound(1);
        tracer.StopTracing();

        var counts = tracer.GetPacketCountsByType();

        Assert.Equal(3, counts[1]); // Three packets with ID 1
        Assert.Equal(1, counts[2]); // One packet with ID 2
    }

    [Fact]
    public void Clear_RemovesAllCapturedPackets()
    {
        var tracer = new PacketTracer();
        tracer.StartTracing();
        tracer.RecordClientOutbound(1);
        tracer.RecordServerOutbound(2);

        tracer.Clear();

        Assert.Equal(0, tracer.CapturedPacketCount);
        Assert.True(tracer.IsTracing); // Clear should not stop tracing
    }

    #endregion

    #region CapturedPacket Record Tests

    [Fact]
    public void CapturedPacket_HasExpectedProperties()
    {
        var packet = new CapturedPacket(
            PacketId: 42,
            Direction: PacketDirection.Outbound,
            Timestamp: DateTime.UtcNow,
            PayloadSize: 100);

        Assert.Equal(42, packet.PacketId);
        Assert.Equal(PacketDirection.Outbound, packet.Direction);
        Assert.Equal(100, packet.PayloadSize);
    }

    [Fact]
    public void CapturedPacket_RecordEquality_Works()
    {
        var timestamp = DateTime.UtcNow;
        var packet1 = new CapturedPacket(42, PacketDirection.Outbound, timestamp, 100);
        var packet2 = new CapturedPacket(42, PacketDirection.Outbound, timestamp, 100);

        Assert.Equal(packet1, packet2);
    }

    #endregion
}
