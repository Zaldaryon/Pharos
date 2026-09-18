namespace Zaldaryon.Pharos.Visual;

/// <summary>
/// Immutable result of a perceptual difference calculation between two images.
/// Contains metrics for image similarity evaluation and optional heatmap path.
/// </summary>
/// <param name="IsSimilar">True when the mean difference is within the configured tolerance.</param>
/// <param name="MaxDiff">The maximum single-channel difference found across all pixels, normalized to 0–1.</param>
/// <param name="MeanDiff">The mean per-channel difference across all pixels, normalized to 0–1.</param>
/// <param name="DiffPixelCount">Number of pixels where any channel difference exceeded the tolerance threshold.</param>
/// <param name="TotalPixels">Total number of pixels in the compared images.</param>
/// <param name="Tolerance">The tolerance threshold used for this comparison (0–1, normalized).</param>
/// <param name="HeatmapPath">Optional path to a generated difference heatmap PNG file.</param>
public readonly record struct PerceptualDiffResult(
    bool IsSimilar,
    float MaxDiff,
    float MeanDiff,
    int DiffPixelCount,
    int TotalPixels,
    float Tolerance,
    string? HeatmapPath = null)
{
    /// <summary>
    /// Fraction of pixels that differed beyond tolerance (0–1).
    /// </summary>
    public float DiffPixelFraction => TotalPixels == 0 ? 0f : (float)DiffPixelCount / TotalPixels;

    public override string ToString() =>
        FormattableString.Invariant($"IsSimilar={IsSimilar} MeanDiff={MeanDiff:F4} MaxDiff={MaxDiff:F4} ") +
        FormattableString.Invariant($"DiffPixels={DiffPixelCount}/{TotalPixels} Tolerance={Tolerance:F4}") +
        (HeatmapPath is not null ? $" Heatmap={HeatmapPath}" : "");
}
