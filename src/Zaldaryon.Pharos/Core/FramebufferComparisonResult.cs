namespace Zaldaryon.Pharos.Core;

/// <summary>
/// Immutable result of a pixel-level comparison between two framebuffer snapshots.
/// </summary>
public readonly struct FramebufferComparisonResult
{
    /// <summary>
    /// The tolerance threshold used for this comparison (0–1, normalized).
    /// </summary>
    public float Tolerance { get; init; }

    /// <summary>
    /// The maximum single-channel difference found across all pixels, normalized to 0–1.
    /// </summary>
    public float MaxDiff { get; init; }

    /// <summary>
    /// The mean per-channel difference across all pixels, normalized to 0–1.
    /// </summary>
    public float MeanDiff { get; init; }

    /// <summary>
    /// Number of pixels where any channel difference exceeded the tolerance threshold.
    /// </summary>
    public int DiffPixelCount { get; init; }

    /// <summary>
    /// Total number of pixels in the compared images.
    /// </summary>
    public int TotalPixels { get; init; }

    /// <summary>
    /// True when the mean per-channel difference is within the configured tolerance.
    /// </summary>
    public bool IsSimilar => MeanDiff <= Tolerance;

    /// <summary>
    /// Fraction of pixels that differed beyond tolerance (0–1).
    /// </summary>
    public float DiffPixelFraction => TotalPixels == 0 ? 0f : (float)DiffPixelCount / TotalPixels;

    public override string ToString() =>
        $"IsSimilar={IsSimilar} MeanDiff={MeanDiff:F4} MaxDiff={MaxDiff:F4} " +
        $"DiffPixels={DiffPixelCount}/{TotalPixels} Tolerance={Tolerance:F4}";
}
