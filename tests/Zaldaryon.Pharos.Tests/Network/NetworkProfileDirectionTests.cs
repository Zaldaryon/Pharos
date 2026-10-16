using Xunit;
using Zaldaryon.Pharos.Network;

namespace Zaldaryon.Pharos.Tests.Network;

/// <summary>The bandwidth cap and the direction of a degradation profile, checked on the simulator alone: no game runs here.</summary>
public class NetworkProfileDirectionTests
{
    [Fact]
    public void Departure_SpacesPacketsByTheirSize()
    {
        // 8 kbps is one byte a millisecond, so a 100 byte packet takes 100 ms to leave and the next one queues behind it.
        var sim = new NetworkDegradationSimulator(seed: 1);
        sim.Configure(new DegradedNetworkProfile(BandwidthKbps: 8));

        Assert.Equal(100, sim.Departure(PacketDirection.Outbound, 100, nowMs: 0));
        Assert.Equal(200, sim.Departure(PacketDirection.Outbound, 100, nowMs: 0));

        // Once the wire is idle again, a packet leaves when it is sent.
        Assert.Equal(600, sim.Departure(PacketDirection.Outbound, 100, nowMs: 500));
    }

    [Fact]
    public void Departure_WithoutACap_LeavesAtOnce()
    {
        var sim = new NetworkDegradationSimulator(seed: 1);
        sim.Configure(new DegradedNetworkProfile(LatencyMs: 50));

        Assert.Equal(300, sim.Departure(PacketDirection.Outbound, 10_000, nowMs: 300));
        Assert.Equal(300, sim.Departure(PacketDirection.Inbound, 10_000, nowMs: 300));
    }

    [Fact]
    public void Direction_LeavesTheOtherDirectionAlone()
    {
        var sim = new NetworkDegradationSimulator(seed: 1);
        sim.Configure(new DegradedNetworkProfile(LatencyMs: 300, BandwidthKbps: 8, Direction: PacketDirection.Outbound));

        Assert.True(sim.Applies(PacketDirection.Outbound));
        Assert.False(sim.Applies(PacketDirection.Inbound));
        Assert.Equal(300, sim.GetEffectiveLatency(PacketDirection.Outbound));
        Assert.Equal(0, sim.GetEffectiveLatency(PacketDirection.Inbound));
        Assert.Equal(0, sim.Departure(PacketDirection.Inbound, 100, nowMs: 0));
        Assert.Equal(100, sim.Departure(PacketDirection.Outbound, 100, nowMs: 0));
    }

    [Fact]
    public void Direction_ProcessPacketLeavesTheOtherDirectionUndegraded()
    {
        var sim = new NetworkDegradationSimulator(seed: 1);
        sim.Configure(new DegradedNetworkProfile(PacketDropRate: 1f, Direction: PacketDirection.Outbound));

        Assert.True(sim.ProcessPacket(PacketDirection.Outbound).Dropped);
        Assert.False(sim.ProcessPacket(PacketDirection.Inbound).Dropped);
    }

    [Fact]
    public void Bandwidth_DoesNotChangeTheSeededOutcomes()
    {
        // The departures never draw from the generator, so the drops and jitter of a seeded profile are the same with a cap.
        var plain = new NetworkDegradationSimulator(seed: 7);
        var capped = new NetworkDegradationSimulator(seed: 7);
        var profile = new DegradedNetworkProfile(LatencyMs: 10, PacketDropRate: 0.5f, JitterMs: 20);
        plain.Configure(profile);
        capped.Configure(profile with { BandwidthKbps = 8 });

        for (int i = 0; i < 50; i++)
        {
            capped.Departure(PacketDirection.Outbound, 100, nowMs: 0);
            Assert.Equal(plain.ProcessPacket(), capped.ProcessPacket());
        }
    }

    [Fact]
    public void Validation_RejectsANegativeCap()
    {
        Assert.False(new DegradedNetworkProfile(BandwidthKbps: -1).IsValid);
        Assert.True(new DegradedNetworkProfile(BandwidthKbps: 8).HasDegradation);
    }
}

/// <summary>Packet filters on the simulator: they select packets, count their drops, and end with their handle.</summary>
public class NetworkFilterTests
{
    private static readonly NetworkPacketInfo Selected = new(PacketDirection.Outbound, Udp: false, PacketId: 23, Channel: "mymod", MessageId: 2, Length: 10);
    private static readonly NetworkPacketInfo Other = new(PacketDirection.Outbound, Udp: false, PacketId: 23, Channel: "mymod", MessageId: 1, Length: 10);

    [Fact]
    public void Filter_DropsOnlyTheSelectedPackets_AndCountsThem()
    {
        var sim = new NetworkDegradationSimulator(seed: 1);
        using IDisposable filter = sim.Filter(p => p.Channel == "mymod" && p.MessageId == 2);

        Assert.True(sim.HasFilters);
        Assert.True(sim.FilterDrops(Selected));
        Assert.False(sim.FilterDrops(Other));
        Assert.Equal(1, sim.PacketsDropped);
    }

    [Fact]
    public void Filter_EndsWithItsHandle()
    {
        var sim = new NetworkDegradationSimulator(seed: 1);
        IDisposable filter = sim.Filter(_ => true);

        filter.Dispose();
        filter.Dispose();

        Assert.False(sim.HasFilters);
        Assert.False(sim.FilterDrops(Selected));
    }

    [Fact]
    public void Filter_RejectsADropRateOutsideZeroToOne()
    {
        var sim = new NetworkDegradationSimulator(seed: 1);

        Assert.Throws<ArgumentOutOfRangeException>(() => sim.Filter(_ => true, drop: 1.5f));
    }
}

/// <summary>A network script: its steps take effect on the link's clock, and an outage holds TCP and drops datagrams.</summary>
public class NetworkScriptTests
{
    [Fact]
    public void Script_AppliesEachStepAtItsTime()
    {
        var sim = new NetworkDegradationSimulator(seed: 1);
        DegradedNetworkProfile poor = DegradedNetworkProfile.PoorMobile;
        sim.Run(NetworkScript.Begin(DegradedNetworkProfile.None).Then(500, poor).Then(1000, DegradedNetworkProfile.None));

        Assert.Equal(DegradedNetworkProfile.None, sim.Profile);
        sim.Elapse(499);
        Assert.Equal(DegradedNetworkProfile.None, sim.Profile);
        sim.Elapse(1);
        Assert.Equal(poor, sim.Profile);
        sim.Elapse(500);
        Assert.Equal(DegradedNetworkProfile.None, sim.Profile);
    }

    [Fact]
    public void Outage_DropsDatagrams_AndEndsWithItsStep()
    {
        var sim = new NetworkDegradationSimulator(seed: 1);
        sim.Run(NetworkScript.Begin(new DegradedNetworkProfile(Outage: true)).Then(1000, DegradedNetworkProfile.None));

        Assert.True(sim.IsOutage);
        Assert.Equal(1000, sim.OutageEndMs);
        Assert.True(sim.ProcessPacket(PacketDirection.Outbound).Dropped);

        sim.Elapse(1000);
        Assert.False(sim.IsOutage);
        Assert.Equal(double.PositiveInfinity, sim.OutageEndMs);
    }

    [Fact]
    public void Script_RejectsATimeBeforeItStarts()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => NetworkScript.Begin(DegradedNetworkProfile.None).Then(-1, DegradedNetworkProfile.None));
    }
}
