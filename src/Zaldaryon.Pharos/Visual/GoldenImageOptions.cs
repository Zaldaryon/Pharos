namespace Zaldaryon.Pharos.Visual;

/// <summary>A rectangular image area in pixel coordinates, with an inclusive top-left and exclusive bottom-right.</summary>
public readonly record struct GoldenImageRect(int X, int Y, int Width, int Height);

/// <summary>Pixel distance used by golden image comparison.</summary>
public enum GoldenImageMetric
{
    /// <summary>Maximum normalized RGB channel difference, combined with normalized alpha difference.</summary>
    Rgb,
    /// <summary>CIEDE2000 color difference in CIELAB, normalized to 0–1, combined with normalized alpha difference.</summary>
    Perceptual,
}

/// <summary>Regions, masks, and error limits for a golden image comparison.</summary>
public sealed record GoldenImageOptions
{
    /// <summary>Areas included in the comparison. Empty means the full image.</summary>
    public IReadOnlyList<GoldenImageRect> Regions { get; init; } = [];

    /// <summary>Areas ignored in the comparison. Masks are removed after regions are combined.</summary>
    public IReadOnlyList<GoldenImageRect> Masks { get; init; } = [];

    /// <summary>Per-pixel error limit. A pixel fails when its metric is greater than this value.</summary>
    public double LocalTolerance { get; init; } = 0.08;

    /// <summary>Maximum fraction of compared pixels that may fail the local tolerance.</summary>
    public double GlobalTolerance { get; init; } = 0.002;

    /// <summary>Distance metric used for each pixel.</summary>
    public GoldenImageMetric Metric { get; init; } = GoldenImageMetric.Perceptual;

    /// <summary>Optional root folder for goldens. Defaults to PHAROS_GOLDENS or ./Goldens.</summary>
    public string? GoldenDirectory { get; init; }
}

/// <summary>Result of comparing images using local and global tolerances.</summary>
public readonly record struct GoldenImageComparison(
    bool IsMatch,
    int ComparedPixels,
    int FailedPixels,
    double FailedFraction,
    double MeanError,
    double MaxError,
    double LocalTolerance,
    double GlobalTolerance,
    GoldenImageMetric Metric);
