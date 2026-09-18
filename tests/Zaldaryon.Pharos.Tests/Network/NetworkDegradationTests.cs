using Xunit;
using Zaldaryon.Pharos.Network;

namespace Zaldaryon.Pharos.Tests.Network;

/// <summary>
/// Pure-logic tests for NetworkDegradationSimulator and DegradedNetworkProfile.
/// These tests are headless-safe and verify deterministic behavior via seeded random.
/// </summary>
public sealed class NetworkDegradationTests
{
    // -------------------------------------------------------------------------
    // DegradedNetworkProfile tests
    // -------------------------------------------------------------------------

    [Fact]
    public void DegradedNetworkProfile_None_HasNoActiveDegradation()
    {
        var profile = DegradedNetworkProfile.None;

        Assert.Equal(0, profile.LatencyMs);
        Assert.Equal(0f, profile.PacketDropRate);
        Assert.Equal(0, profile.JitterMs);
        Assert.Equal(0f, profile.CorruptionRate);
        Assert.False(profile.HasDegradation);
        Assert.True(profile.IsValid);
    }

    [Fact]
    public void DegradedNetworkProfile_PoorMobile_HasExpectedValues()
    {
        var profile = DegradedNetworkProfile.PoorMobile;

        Assert.Equal(200, profile.LatencyMs);
        Assert.Equal(0.05f, profile.PacketDropRate);
        Assert.Equal(50, profile.JitterMs);
        Assert.Equal(0f, profile.CorruptionRate);
        Assert.True(profile.HasDegradation);
        Assert.True(profile.IsValid);
    }

    [Fact]
    public void DegradedNetworkProfile_Satellite_HasExpectedValues()
    {
        var profile = DegradedNetworkProfile.Satellite;

        Assert.Equal(600, profile.LatencyMs);
        Assert.Equal(0.01f, profile.PacketDropRate);
        Assert.Equal(100, profile.JitterMs);
        Assert.Equal(0f, profile.CorruptionRate);
        Assert.True(profile.HasDegradation);
        Assert.True(profile.IsValid);
    }

    [Fact]
    public void DegradedNetworkProfile_HighPacketLoss_HasExpectedValues()
    {
        var profile = DegradedNetworkProfile.HighPacketLoss;

        Assert.Equal(50, profile.LatencyMs);
        Assert.Equal(0.20f, profile.PacketDropRate);
        Assert.Equal(0, profile.JitterMs);
        Assert.Equal(0f, profile.CorruptionRate);
        Assert.True(profile.HasDegradation);
        Assert.True(profile.IsValid);
    }

    [Fact]
    public void DegradedNetworkProfile_CustomValues_WorksCorrectly()
    {
        var profile = new DegradedNetworkProfile(
            LatencyMs: 100,
            PacketDropRate: 0.15f,
            JitterMs: 25,
            CorruptionRate: 0.02f);

        Assert.Equal(100, profile.LatencyMs);
        Assert.Equal(0.15f, profile.PacketDropRate);
        Assert.Equal(25, profile.JitterMs);
        Assert.Equal(0.02f, profile.CorruptionRate);
        Assert.True(profile.HasDegradation);
        Assert.True(profile.IsValid);
    }

    [Fact]
    public void DegradedNetworkProfile_InvalidDropRate_IsNotValid()
    {
        var profile = new DegradedNetworkProfile(PacketDropRate: 1.5f);

        Assert.False(profile.IsValid);
    }

    [Fact]
    public void DegradedNetworkProfile_NegativeLatency_IsNotValid()
    {
        var profile = new DegradedNetworkProfile(LatencyMs: -10);

        Assert.False(profile.IsValid);
    }

    [Fact]
    public void DegradedNetworkProfile_NegativeDropRate_IsNotValid()
    {
        var profile = new DegradedNetworkProfile(PacketDropRate: -0.1f);

        Assert.False(profile.IsValid);
    }

    // -------------------------------------------------------------------------
    // NetworkDegradationSimulator configuration tests
    // -------------------------------------------------------------------------

    [Fact]
    public void NetworkDegradationSimulator_DefaultState_IsNotActive()
    {
        var simulator = new NetworkDegradationSimulator();

        Assert.False(simulator.IsActive);
        Assert.Equal(DegradedNetworkProfile.None, simulator.Profile);
    }

    [Fact]
    public void NetworkDegradationSimulator_Configure_SetsProfile()
    {
        var simulator = new NetworkDegradationSimulator();
        var profile = DegradedNetworkProfile.PoorMobile;

        simulator.Configure(profile);

        Assert.True(simulator.IsActive);
        Assert.Equal(profile, simulator.Profile);
    }

    [Fact]
    public void NetworkDegradationSimulator_Configure_InvalidProfile_Throws()
    {
        var simulator = new NetworkDegradationSimulator();
        var invalidProfile = new DegradedNetworkProfile(PacketDropRate: 2.0f);

        Assert.Throws<ArgumentException>(() => simulator.Configure(invalidProfile));
    }

    [Fact]
    public void NetworkDegradationSimulator_Reset_ClearsProfile()
    {
        var simulator = new NetworkDegradationSimulator();
        simulator.Configure(DegradedNetworkProfile.PoorMobile);

        simulator.Reset();

        Assert.False(simulator.IsActive);
        Assert.Equal(0, simulator.PacketsProcessed);
        Assert.Equal(0, simulator.PacketsDropped);
    }

    [Fact]
    public void NetworkDegradationSimulator_Configure_CanChangePerTest()
    {
        var simulator = new NetworkDegradationSimulator();

        simulator.Configure(DegradedNetworkProfile.PoorMobile);
        Assert.Equal(200, simulator.Profile.LatencyMs);

        simulator.Configure(DegradedNetworkProfile.Satellite);
        Assert.Equal(600, simulator.Profile.LatencyMs);

        simulator.Configure(DegradedNetworkProfile.None);
        Assert.False(simulator.IsActive);
    }

    // -------------------------------------------------------------------------
    // Deterministic seeded random tests
    // -------------------------------------------------------------------------

    [Fact]
    public void NetworkDegradationSimulator_SameSeeed_ProducesSameResults()
    {
        const int seed = 12345;
        var profile = new DegradedNetworkProfile(PacketDropRate: 0.5f);

        var simulator1 = new NetworkDegradationSimulator(seed);
        simulator1.Configure(profile);
        var results1 = new List<bool>();
        for (int i = 0; i < 100; i++)
        {
            results1.Add(simulator1.ShouldDropPacket());
        }

        var simulator2 = new NetworkDegradationSimulator(seed);
        simulator2.Configure(profile);
        var results2 = new List<bool>();
        for (int i = 0; i < 100; i++)
        {
            results2.Add(simulator2.ShouldDropPacket());
        }

        Assert.Equal(results1, results2);
    }

    [Fact]
    public void NetworkDegradationSimulator_DifferentSeed_ProducesDifferentResults()
    {
        var profile = new DegradedNetworkProfile(PacketDropRate: 0.5f);

        var simulator1 = new NetworkDegradationSimulator(100);
        simulator1.Configure(profile);
        var results1 = new List<bool>();
        for (int i = 0; i < 100; i++)
        {
            results1.Add(simulator1.ShouldDropPacket());
        }

        var simulator2 = new NetworkDegradationSimulator(200);
        simulator2.Configure(profile);
        var results2 = new List<bool>();
        for (int i = 0; i < 100; i++)
        {
            results2.Add(simulator2.ShouldDropPacket());
        }

        Assert.NotEqual(results1, results2);
    }

    [Fact]
    public void NetworkDegradationSimulator_SetSeed_ResetsRandomSequence()
    {
        const int seed = 42;
        var profile = new DegradedNetworkProfile(PacketDropRate: 0.5f);

        var simulator = new NetworkDegradationSimulator(seed);
        simulator.Configure(profile);

        var initialResults = new List<bool>();
        for (int i = 0; i < 50; i++)
        {
            initialResults.Add(simulator.ShouldDropPacket());
        }

        simulator.SetSeed(seed);

        var resetResults = new List<bool>();
        for (int i = 0; i < 50; i++)
        {
            resetResults.Add(simulator.ShouldDropPacket());
        }

        Assert.Equal(initialResults, resetResults);
    }

    // -------------------------------------------------------------------------
    // Packet drop rate verification tests
    // -------------------------------------------------------------------------

    [Fact]
    public void NetworkDegradationSimulator_ZeroDropRate_NeverDrops()
    {
        var simulator = new NetworkDegradationSimulator();
        simulator.Configure(new DegradedNetworkProfile(PacketDropRate: 0f));

        for (int i = 0; i < 1000; i++)
        {
            Assert.False(simulator.ShouldDropPacket());
        }

        Assert.Equal(0, simulator.PacketsDropped);
    }

    [Fact]
    public void NetworkDegradationSimulator_FullDropRate_AlwaysDrops()
    {
        var simulator = new NetworkDegradationSimulator();
        simulator.Configure(new DegradedNetworkProfile(PacketDropRate: 1f));

        for (int i = 0; i < 100; i++)
        {
            Assert.True(simulator.ShouldDropPacket());
        }

        Assert.Equal(100, simulator.PacketsDropped);
    }

    [Fact]
    public void NetworkDegradationSimulator_HalfDropRate_DropsApproximatelyHalf()
    {
        var simulator = new NetworkDegradationSimulator(42);
        simulator.Configure(new DegradedNetworkProfile(PacketDropRate: 0.5f));

        int dropped = 0;
        const int total = 10000;

        for (int i = 0; i < total; i++)
        {
            if (simulator.ShouldDropPacket()) dropped++;
        }

        // Allow for 5% variance
        double dropRate = (double)dropped / total;
        Assert.InRange(dropRate, 0.45, 0.55);
    }

    [Fact]
    public void NetworkDegradationSimulator_LowDropRate_DropsFewPackets()
    {
        var simulator = new NetworkDegradationSimulator(42);
        simulator.Configure(new DegradedNetworkProfile(PacketDropRate: 0.05f));

        int dropped = 0;
        const int total = 10000;

        for (int i = 0; i < total; i++)
        {
            if (simulator.ShouldDropPacket()) dropped++;
        }

        double dropRate = (double)dropped / total;
        Assert.InRange(dropRate, 0.03, 0.07);
    }

    // -------------------------------------------------------------------------
    // Latency calculation tests
    // -------------------------------------------------------------------------

    [Fact]
    public void NetworkDegradationSimulator_LatencyOnly_ReturnsExactLatency()
    {
        var simulator = new NetworkDegradationSimulator();
        simulator.Configure(new DegradedNetworkProfile(LatencyMs: 100));

        int latency = simulator.GetEffectiveLatency();

        Assert.Equal(100, latency);
    }

    [Fact]
    public void NetworkDegradationSimulator_LatencyWithJitter_ReturnsVariedLatency()
    {
        var simulator = new NetworkDegradationSimulator(42);
        simulator.Configure(new DegradedNetworkProfile(LatencyMs: 100, JitterMs: 50));

        var latencies = new List<int>();
        for (int i = 0; i < 100; i++)
        {
            latencies.Add(simulator.GetEffectiveLatency());
        }

        // Should have variation due to jitter
        Assert.True(latencies.Min() < latencies.Max());
        // Latency should be in range [50, 150] (100 +/- 50)
        Assert.True(latencies.All(l => l >= 50 && l <= 150));
    }

    [Fact]
    public void NetworkDegradationSimulator_LatencyNeverNegative()
    {
        var simulator = new NetworkDegradationSimulator(42);
        simulator.Configure(new DegradedNetworkProfile(LatencyMs: 10, JitterMs: 50));

        for (int i = 0; i < 1000; i++)
        {
            int latency = simulator.GetEffectiveLatency();
            Assert.True(latency >= 0, $"Latency should never be negative, got {latency}");
        }
    }

    [Fact]
    public void NetworkDegradationSimulator_JitterOnlyNoBase_CanBeZero()
    {
        var simulator = new NetworkDegradationSimulator(42);
        simulator.Configure(new DegradedNetworkProfile(LatencyMs: 0, JitterMs: 50));

        var latencies = new List<int>();
        for (int i = 0; i < 1000; i++)
        {
            latencies.Add(simulator.GetEffectiveLatency());
        }

        // With no base latency and jitter of 50, values should range from 0 to 50
        Assert.True(latencies.Min() == 0);
        Assert.True(latencies.Max() <= 50);
    }

    // -------------------------------------------------------------------------
    // Corruption rate verification tests
    // -------------------------------------------------------------------------

    [Fact]
    public void NetworkDegradationSimulator_ZeroCorruptionRate_NeverCorrupts()
    {
        var simulator = new NetworkDegradationSimulator();
        simulator.Configure(new DegradedNetworkProfile(CorruptionRate: 0f));

        for (int i = 0; i < 1000; i++)
        {
            Assert.False(simulator.ShouldCorruptPacket());
        }

        Assert.Equal(0, simulator.PacketsCorrupted);
    }

    [Fact]
    public void NetworkDegradationSimulator_FullCorruptionRate_AlwaysCorrupts()
    {
        var simulator = new NetworkDegradationSimulator();
        simulator.Configure(new DegradedNetworkProfile(CorruptionRate: 1f));

        for (int i = 0; i < 100; i++)
        {
            Assert.True(simulator.ShouldCorruptPacket());
        }

        Assert.Equal(100, simulator.PacketsCorrupted);
    }

    // -------------------------------------------------------------------------
    // ProcessPacket combined tests
    // -------------------------------------------------------------------------

    [Fact]
    public void NetworkDegradationSimulator_ProcessPacket_NoDegradation_ReturnsClean()
    {
        var simulator = new NetworkDegradationSimulator();
        simulator.Configure(DegradedNetworkProfile.None);

        var result = simulator.ProcessPacket();

        Assert.False(result.Dropped);
        Assert.False(result.Corrupted);
        Assert.Equal(0, result.LatencyMs);
    }

    [Fact]
    public void NetworkDegradationSimulator_ProcessPacket_WithLatency_ReturnsLatency()
    {
        var simulator = new NetworkDegradationSimulator();
        simulator.Configure(new DegradedNetworkProfile(LatencyMs: 150));

        var result = simulator.ProcessPacket();

        Assert.False(result.Dropped);
        Assert.False(result.Corrupted);
        Assert.Equal(150, result.LatencyMs);
    }

    [Fact]
    public void NetworkDegradationSimulator_ProcessPacket_DroppedPacket_HasNoLatency()
    {
        var simulator = new NetworkDegradationSimulator();
        simulator.Configure(new DegradedNetworkProfile(
            LatencyMs: 100,
            PacketDropRate: 1f)); // Always drop

        var result = simulator.ProcessPacket();

        Assert.True(result.Dropped);
        Assert.Equal(0, result.LatencyMs); // Dropped packets don't get latency applied
    }

    // -------------------------------------------------------------------------
    // Statistics tests
    // -------------------------------------------------------------------------

    [Fact]
    public void NetworkDegradationSimulator_GetStatistics_ReturnsCorrectValues()
    {
        var simulator = new NetworkDegradationSimulator();
        simulator.Configure(new DegradedNetworkProfile(
            LatencyMs: 100,
            PacketDropRate: 1f)); // Always drop

        for (int i = 0; i < 10; i++)
        {
            simulator.ShouldDropPacket();
        }

        var stats = simulator.GetStatistics();

        Assert.Equal(10, stats.PacketsProcessed);
        Assert.Equal(10, stats.PacketsDropped);
        Assert.Equal(1f, stats.DropRate);
    }

    [Fact]
    public void NetworkDegradationSimulator_TotalLatencyApplied_Accumulates()
    {
        var simulator = new NetworkDegradationSimulator();
        simulator.Configure(new DegradedNetworkProfile(LatencyMs: 50));

        for (int i = 0; i < 10; i++)
        {
            simulator.GetEffectiveLatency();
        }

        Assert.Equal(500, simulator.TotalLatencyApplied);
    }

    // -------------------------------------------------------------------------
    // Event logging tests
    // -------------------------------------------------------------------------

    [Fact]
    public void NetworkDegradationSimulator_Events_LogsProfileChanges()
    {
        var simulator = new NetworkDegradationSimulator();

        simulator.Configure(DegradedNetworkProfile.PoorMobile);

        var events = simulator.Events;
        Assert.Contains(events, e => e.Type == DegradationEventType.ProfileChanged);
    }

    [Fact]
    public void NetworkDegradationSimulator_Events_LogsSeedChanges()
    {
        var simulator = new NetworkDegradationSimulator();

        simulator.SetSeed(999);

        var events = simulator.Events;
        Assert.Contains(events, e => e.Type == DegradationEventType.SeedChanged);
    }

    [Fact]
    public void NetworkDegradationSimulator_Events_LogsPacketDrops()
    {
        var simulator = new NetworkDegradationSimulator();
        simulator.Configure(new DegradedNetworkProfile(PacketDropRate: 1f));

        simulator.ShouldDropPacket();

        var events = simulator.Events;
        Assert.Contains(events, e => e.Type == DegradationEventType.PacketDropped);
    }

    [Fact]
    public void NetworkDegradationSimulator_Reset_ClearsEvents()
    {
        var simulator = new NetworkDegradationSimulator();
        simulator.Configure(DegradedNetworkProfile.PoorMobile);

        simulator.Reset();

        var events = simulator.Events;
        Assert.Single(events); // Only the reset event
        Assert.Equal(DegradationEventType.Reset, events[0].Type);
    }
}
