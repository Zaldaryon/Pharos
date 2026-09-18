namespace Zaldaryon.Pharos.Network;

/// <summary>
/// Defines network degradation parameters for simulating adverse network conditions.
/// </summary>
/// <param name="LatencyMs">Fixed latency to add to packet delivery in milliseconds. Default 0.</param>
/// <param name="PacketDropRate">Probability of dropping a packet (0.0 to 1.0). Default 0.</param>
/// <param name="JitterMs">Maximum random variance to add to latency in milliseconds. Default 0.</param>
/// <param name="CorruptionRate">Probability of corrupting a packet (0.0 to 1.0). Default 0.</param>
public sealed record DegradedNetworkProfile(
    int LatencyMs = 0,
    float PacketDropRate = 0f,
    int JitterMs = 0,
    float CorruptionRate = 0f)
{
    /// <summary>
    /// A profile with no degradation (perfect network).
    /// </summary>
    public static DegradedNetworkProfile None { get; } = new();

    /// <summary>
    /// A profile simulating a poor mobile connection (200ms latency, 5% drop, 50ms jitter).
    /// </summary>
    public static DegradedNetworkProfile PoorMobile { get; } = new(
        LatencyMs: 200,
        PacketDropRate: 0.05f,
        JitterMs: 50);

    /// <summary>
    /// A profile simulating satellite internet (600ms latency, 1% drop, 100ms jitter).
    /// </summary>
    public static DegradedNetworkProfile Satellite { get; } = new(
        LatencyMs: 600,
        PacketDropRate: 0.01f,
        JitterMs: 100);

    /// <summary>
    /// A profile simulating high packet loss (50ms latency, 20% drop).
    /// </summary>
    public static DegradedNetworkProfile HighPacketLoss { get; } = new(
        LatencyMs: 50,
        PacketDropRate: 0.20f);

    /// <summary>
    /// Validates that all parameters are within acceptable ranges.
    /// </summary>
    public bool IsValid =>
        LatencyMs >= 0 &&
        PacketDropRate >= 0f && PacketDropRate <= 1f &&
        JitterMs >= 0 &&
        CorruptionRate >= 0f && CorruptionRate <= 1f;

    /// <summary>
    /// Returns whether this profile has any active degradation.
    /// </summary>
    public bool HasDegradation =>
        LatencyMs > 0 || PacketDropRate > 0f || JitterMs > 0 || CorruptionRate > 0f;
}
