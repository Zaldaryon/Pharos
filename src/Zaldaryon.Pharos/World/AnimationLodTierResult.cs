namespace Zaldaryon.Pharos.World;

/// <summary>
/// Immutable result of animation LOD tier analysis, tracking tick counts
/// at different distance tiers and detecting throttling behavior.
/// </summary>
/// <param name="NearTickCount">Number of animation ticks recorded for near-distance entities.</param>
/// <param name="MidTickCount">Number of animation ticks recorded for mid-distance entities.</param>
/// <param name="FarTickCount">Number of animation ticks recorded for far-distance entities.</param>
public sealed record AnimationLodTierResult(
    int NearTickCount,
    int MidTickCount,
    int FarTickCount)
{
    /// <summary>
    /// Ratio of far ticks to near ticks. Returns 0 if NearTickCount is 0.
    /// Values closer to 0 indicate more aggressive throttling.
    /// </summary>
    public double NearToFarRatio => NearTickCount == 0 ? 0.0 : (double)FarTickCount / NearTickCount;

    /// <summary>
    /// True if throttling is detected (FarTickCount is less than half of NearTickCount).
    /// This indicates that distant entities are being animated less frequently.
    /// </summary>
    public bool ThrottleDetected => NearTickCount > 0 && FarTickCount < NearTickCount * 0.5;

    /// <summary>
    /// Total tick count across all tiers.
    /// </summary>
    public int TotalTickCount => NearTickCount + MidTickCount + FarTickCount;

    /// <summary>
    /// True if no ticks were recorded across any tier.
    /// </summary>
    public bool IsEmpty => TotalTickCount == 0;

    /// <summary>
    /// Returns the difference between near and far tick counts.
    /// Positive values indicate throttling is occurring.
    /// </summary>
    public int ThrottleDelta => NearTickCount - FarTickCount;

    /// <summary>
    /// Creates an empty result with zero ticks in all tiers.
    /// </summary>
    public static AnimationLodTierResult Empty => new(0, 0, 0);

    /// <summary>
    /// Creates a result representing uniform tick distribution (no throttling).
    /// </summary>
    /// <param name="tickCount">The tick count to use for all tiers.</param>
    public static AnimationLodTierResult Uniform(int tickCount) => new(tickCount, tickCount, tickCount);

    /// <summary>
    /// Creates a result representing aggressive throttling where far entities
    /// receive significantly fewer ticks than near entities.
    /// </summary>
    /// <param name="nearTicks">Tick count for near entities (highest).</param>
    /// <param name="midDivisor">Divisor to apply for mid-tier (default: 2).</param>
    /// <param name="farDivisor">Divisor to apply for far-tier (default: 4).</param>
    public static AnimationLodTierResult Throttled(int nearTicks, int midDivisor = 2, int farDivisor = 4) =>
        new(nearTicks, nearTicks / midDivisor, nearTicks / farDivisor);

    public override string ToString() =>
        FormattableString.Invariant(
            $"AnimationLodTierResult(Near={NearTickCount}, Mid={MidTickCount}, Far={FarTickCount}, Ratio={NearToFarRatio:F3}, Throttled={ThrottleDetected})");
}
