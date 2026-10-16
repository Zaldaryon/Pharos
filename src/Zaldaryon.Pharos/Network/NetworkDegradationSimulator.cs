using System;
using System.Collections.Generic;

namespace Zaldaryon.Pharos.Network;

/// <summary>
/// Simulates network degradation by applying latency, packet drop, jitter, and corruption
/// to packet flows. Uses seeded random for deterministic test behavior.
/// </summary>
public sealed class NetworkDegradationSimulator
{
    private readonly object _lock = new();
    private readonly List<DegradationEvent> _events = new();
    private Random _random;
    private DegradedNetworkProfile _profile = DegradedNetworkProfile.None;
    private int _seed;
    private long _packetsProcessed;
    private long _packetsDropped;
    private long _packetsCorrupted;
    private long _totalLatencyApplied;
    private readonly double[] _busyUntilMs = new double[2];

    /// <summary>
    /// Creates a new NetworkDegradationSimulator with the default seed.
    /// </summary>
    public NetworkDegradationSimulator() : this(42) { }

    /// <summary>
    /// Creates a new NetworkDegradationSimulator with a specific seed for deterministic behavior.
    /// </summary>
    public NetworkDegradationSimulator(int seed)
    {
        _seed = seed;
        _random = new Random(seed);
    }

    /// <summary>
    /// Current degradation profile.
    /// </summary>
    public DegradedNetworkProfile Profile
    {
        get { lock (_lock) return _profile; }
    }

    /// <summary>
    /// Whether the simulator has any active degradation configured.
    /// </summary>
    public bool IsActive
    {
        get { lock (_lock) return _profile.HasDegradation; }
    }

    /// <summary>
    /// Current random seed used for deterministic behavior.
    /// </summary>
    public int Seed
    {
        get { lock (_lock) return _seed; }
    }

    /// <summary>
    /// Total packets processed by the simulator.
    /// </summary>
    public long PacketsProcessed
    {
        get { lock (_lock) return _packetsProcessed; }
    }

    /// <summary>
    /// Total packets dropped by the simulator.
    /// </summary>
    public long PacketsDropped
    {
        get { lock (_lock) return _packetsDropped; }
    }

    /// <summary>
    /// Total packets corrupted by the simulator.
    /// </summary>
    public long PacketsCorrupted
    {
        get { lock (_lock) return _packetsCorrupted; }
    }

    /// <summary>
    /// Total latency applied in milliseconds.
    /// </summary>
    public long TotalLatencyApplied
    {
        get { lock (_lock) return _totalLatencyApplied; }
    }

    /// <summary>
    /// History of degradation events.
    /// </summary>
    public IReadOnlyList<DegradationEvent> Events
    {
        get { lock (_lock) return _events.ToList().AsReadOnly(); }
    }

    /// <summary>
    /// Configures the degradation profile. Can be changed per-test without restarting.
    /// </summary>
    public void Configure(DegradedNetworkProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        if (!profile.IsValid)
        {
            throw new ArgumentException("Profile parameters are out of valid range.", nameof(profile));
        }

        lock (_lock)
        {
            _profile = profile;
            _events.Add(new DegradationEvent(
                DegradationEventType.ProfileChanged,
                $"Profile changed: Latency={profile.LatencyMs}ms, Drop={profile.PacketDropRate:P0}, Jitter={profile.JitterMs}ms, Corruption={profile.CorruptionRate:P0}"));
        }
    }

    /// <summary>
    /// Configures the random seed and resets the random generator for deterministic behavior.
    /// </summary>
    public void SetSeed(int seed)
    {
        lock (_lock)
        {
            _seed = seed;
            _random = new Random(seed);
            _events.Add(new DegradationEvent(
                DegradationEventType.SeedChanged,
                $"Seed changed to {seed}"));
        }
    }

    /// <summary>
    /// Resets the simulator to initial state with no degradation and clears statistics.
    /// </summary>
    public void Reset()
    {
        lock (_lock)
        {
            _profile = DegradedNetworkProfile.None;
            _random = new Random(_seed);
            _packetsProcessed = 0;
            _packetsDropped = 0;
            _packetsCorrupted = 0;
            _totalLatencyApplied = 0;
            _events.Clear();
            _events.Add(new DegradationEvent(DegradationEventType.Reset, "Simulator reset"));
        }
    }

    /// <summary>
    /// Determines if a packet should be dropped based on the current drop rate.
    /// Uses seeded random for deterministic results.
    /// </summary>
    public bool ShouldDropPacket()
    {
        lock (_lock)
        {
            _packetsProcessed++;

            if (_profile.PacketDropRate <= 0f)
            {
                return false;
            }

            double roll = _random.NextDouble();
            bool shouldDrop = roll < _profile.PacketDropRate;

            if (shouldDrop)
            {
                _packetsDropped++;
                _events.Add(new DegradationEvent(
                    DegradationEventType.PacketDropped,
                    $"Packet dropped (roll={roll:F4}, threshold={_profile.PacketDropRate:F4})"));
            }

            return shouldDrop;
        }
    }

    /// <summary>
    /// Determines if a packet should be corrupted based on the current corruption rate.
    /// Uses seeded random for deterministic results.
    /// </summary>
    public bool ShouldCorruptPacket()
    {
        lock (_lock)
        {
            if (_profile.CorruptionRate <= 0f)
            {
                return false;
            }

            double roll = _random.NextDouble();
            bool shouldCorrupt = roll < _profile.CorruptionRate;

            if (shouldCorrupt)
            {
                _packetsCorrupted++;
                _events.Add(new DegradationEvent(
                    DegradationEventType.PacketCorrupted,
                    $"Packet corrupted (roll={roll:F4}, threshold={_profile.CorruptionRate:F4})"));
            }

            return shouldCorrupt;
        }
    }

    /// <summary>
    /// Gets the effective latency for a packet, including base latency and jitter.
    /// </summary>
    public int GetEffectiveLatency()
    {
        lock (_lock)
        {
            int baseLatency = _profile.LatencyMs;
            int jitter = 0;

            if (_profile.JitterMs > 0)
            {
                // Jitter is uniformly distributed between -JitterMs and +JitterMs
                jitter = _random.Next(-_profile.JitterMs, _profile.JitterMs + 1);
            }

            int effectiveLatency = Math.Max(0, baseLatency + jitter);
            _totalLatencyApplied += effectiveLatency;

            return effectiveLatency;
        }
    }

    /// <summary>Whether the profile applies to packets in <paramref name="direction"/>.</summary>
    public bool Applies(PacketDirection direction)
    {
        lock (_lock) return AppliesTo(direction);
    }

    // Callers without a direction, such as the first version of the API, are in both.
    private bool AppliesTo(PacketDirection? direction) =>
        _profile.Direction is not { } only || direction is null || only == direction;

    /// <summary>
    /// The time a packet of <paramref name="bytes"/> leaves the link in <paramref name="direction"/>, in link milliseconds. With a
    /// bandwidth cap, packets queue behind the ones before them and each takes <c>bytes * 8 / BandwidthKbps</c> milliseconds. Without a
    /// cap, or in the other direction, the packet leaves at <paramref name="nowMs"/>. It never draws from the random generator.
    /// </summary>
    public double Departure(PacketDirection direction, int bytes, double nowMs)
    {
        lock (_lock)
        {
            if (_profile.BandwidthKbps <= 0 || !Applies(direction)) return nowMs;

            int slot = (int)direction;
            double start = Math.Max(nowMs, _busyUntilMs[slot]);
            _busyUntilMs[slot] = start + bytes * 8.0 / _profile.BandwidthKbps;
            return _busyUntilMs[slot];
        }
    }

    /// <summary>The latency one packet in <paramref name="direction"/> pays; 0 when the profile does not apply to it.</summary>
    public int GetEffectiveLatency(PacketDirection direction)
    {
        lock (_lock) return AppliesTo(direction) ? GetEffectiveLatencyInternal() : 0;
    }

    /// <summary>
    /// Processes a packet through the degradation simulator, returning the result.
    /// </summary>
    public PacketDegradationResult ProcessPacket() => ProcessPacket(null);

    /// <summary>
    /// Processes a packet in <paramref name="direction"/> through the degradation simulator, returning the result. A profile for one
    /// direction leaves the other alone.
    /// </summary>
    public PacketDegradationResult ProcessPacket(PacketDirection? direction)
    {
        lock (_lock)
        {
            if (!_profile.HasDegradation || !AppliesTo(direction))
            {
                _packetsProcessed++;
                return new PacketDegradationResult(
                    Dropped: false,
                    Corrupted: false,
                    LatencyMs: 0);
            }

            // Check drop first (don't need to calculate latency for dropped packets)
            if (ShouldDropPacketInternal())
            {
                return new PacketDegradationResult(
                    Dropped: true,
                    Corrupted: false,
                    LatencyMs: 0);
            }

            bool corrupted = ShouldCorruptPacketInternal();
            int latency = GetEffectiveLatencyInternal();

            return new PacketDegradationResult(
                Dropped: false,
                Corrupted: corrupted,
                LatencyMs: latency);
        }
    }

    // Internal methods that don't acquire lock (called from within lock)
    private bool ShouldDropPacketInternal()
    {
        _packetsProcessed++;

        if (_profile.PacketDropRate <= 0f)
        {
            return false;
        }

        double roll = _random.NextDouble();
        bool shouldDrop = roll < _profile.PacketDropRate;

        if (shouldDrop)
        {
            _packetsDropped++;
            _events.Add(new DegradationEvent(
                DegradationEventType.PacketDropped,
                $"Packet dropped (roll={roll:F4}, threshold={_profile.PacketDropRate:F4})"));
        }

        return shouldDrop;
    }

    private bool ShouldCorruptPacketInternal()
    {
        if (_profile.CorruptionRate <= 0f)
        {
            return false;
        }

        double roll = _random.NextDouble();
        bool shouldCorrupt = roll < _profile.CorruptionRate;

        if (shouldCorrupt)
        {
            _packetsCorrupted++;
            _events.Add(new DegradationEvent(
                DegradationEventType.PacketCorrupted,
                $"Packet corrupted (roll={roll:F4}, threshold={_profile.CorruptionRate:F4})"));
        }

        return shouldCorrupt;
    }

    private int GetEffectiveLatencyInternal()
    {
        int baseLatency = _profile.LatencyMs;
        int jitter = 0;

        if (_profile.JitterMs > 0)
        {
            jitter = _random.Next(-_profile.JitterMs, _profile.JitterMs + 1);
        }

        int effectiveLatency = Math.Max(0, baseLatency + jitter);
        _totalLatencyApplied += effectiveLatency;

        return effectiveLatency;
    }

    /// <summary>
    /// Gets a statistics snapshot of the simulator's current state.
    /// </summary>
    public DegradationStatistics GetStatistics()
    {
        lock (_lock)
        {
            return new DegradationStatistics(
                PacketsProcessed: _packetsProcessed,
                PacketsDropped: _packetsDropped,
                PacketsCorrupted: _packetsCorrupted,
                TotalLatencyApplied: _totalLatencyApplied,
                DropRate: _packetsProcessed > 0 ? (float)_packetsDropped / _packetsProcessed : 0f,
                CorruptionRate: _packetsProcessed > 0 ? (float)_packetsCorrupted / _packetsProcessed : 0f,
                AverageLatency: _packetsProcessed > 0 ? (float)_totalLatencyApplied / _packetsProcessed : 0f);
        }
    }
}

/// <summary>
/// Records a degradation event for debugging and verification.
/// </summary>
public sealed record DegradationEvent(
    DegradationEventType Type,
    string Message);

/// <summary>
/// Types of degradation events.
/// </summary>
public enum DegradationEventType
{
    ProfileChanged,
    SeedChanged,
    Reset,
    PacketDropped,
    PacketCorrupted
}

/// <summary>
/// Result of processing a packet through the degradation simulator.
/// </summary>
public sealed record PacketDegradationResult(
    bool Dropped,
    bool Corrupted,
    int LatencyMs);

/// <summary>
/// Statistics snapshot of the degradation simulator.
/// </summary>
public sealed record DegradationStatistics(
    long PacketsProcessed,
    long PacketsDropped,
    long PacketsCorrupted,
    long TotalLatencyApplied,
    float DropRate,
    float CorruptionRate,
    float AverageLatency);
