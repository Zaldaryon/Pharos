using SkiaSharp;
using Zaldaryon.Pharos.Assertions;
using Zaldaryon.Pharos.Core;

namespace Zaldaryon.Pharos.Visual;

/// <summary>
/// Compares captured framebuffer snapshots against golden reference images.
/// Uses SkiaSharp for PNG I/O with graceful fallback when native library is unavailable.
/// </summary>
public static class GoldenImageAssertion
{
    /// <summary>
    /// Asserts that the captured framebuffer matches the golden reference image within tolerance.
    /// </summary>
    /// <param name="actual">The captured framebuffer snapshot to compare.</param>
    /// <param name="goldenPath">Path to the golden reference PNG file.</param>
    /// <param name="perceptualTolerance">Normalized tolerance threshold (0–1). Default: 0.02 (2%).</param>
    /// <param name="heatmapOutputPath">Optional path to save a difference heatmap PNG on failure.</param>
    /// <returns>The perceptual diff result with comparison metrics.</returns>
    /// <exception cref="PharosAssertException">Thrown when images differ beyond tolerance.</exception>
    public static PerceptualDiffResult Assert(
        FramebufferSnapshot actual,
        string goldenPath,
        float perceptualTolerance = 0.02f,
        string? heatmapOutputPath = null)
    {
        ArgumentNullException.ThrowIfNull(actual);
        ArgumentException.ThrowIfNullOrEmpty(goldenPath);

        if (perceptualTolerance < 0f || perceptualTolerance > 1f)
        {
            throw new ArgumentOutOfRangeException(
                nameof(perceptualTolerance),
                perceptualTolerance,
                "Tolerance must be between 0 and 1.");
        }

        // Load golden image with SkiaSharp fallback
        byte[] goldenPixels;
        int goldenWidth, goldenHeight;

        try
        {
            var golden = LoadGoldenImage(goldenPath);
            goldenPixels = golden.Pixels;
            goldenWidth = golden.Width;
            goldenHeight = golden.Height;
        }
        catch (Exception ex) when (IsSkiaSharpUnavailable(ex))
        {
            // SkiaSharp native library not available - return synthetic failure result
            return new PerceptualDiffResult(
                IsSimilar: false,
                MaxDiff: 1f,
                MeanDiff: 1f,
                DiffPixelCount: actual.Width * actual.Height,
                TotalPixels: actual.Width * actual.Height,
                Tolerance: perceptualTolerance,
                HeatmapPath: null);
        }

        // Validate dimensions match
        if (actual.Width != goldenWidth || actual.Height != goldenHeight)
        {
            throw new PharosAssertException(
                $"Image dimensions mismatch: actual is {actual.Width}×{actual.Height}, " +
                $"golden is {goldenWidth}×{goldenHeight}.");
        }

        // Calculate perceptual difference
        var result = PerceptualDiffCalculator.CalcDiff(
            actual.RawRgba,
            goldenPixels,
            actual.Width,
            actual.Height,
            perceptualTolerance);

        // Generate and save heatmap if path provided and images differ
        string? savedHeatmapPath = null;
        if (!result.IsSimilar && heatmapOutputPath is not null)
        {
            try
            {
                var heatmapPixels = PerceptualDiffCalculator.GenerateHeatmap(
                    actual.RawRgba,
                    goldenPixels,
                    actual.Width,
                    actual.Height);

                SaveHeatmapPng(heatmapPixels, actual.Width, actual.Height, heatmapOutputPath);
                savedHeatmapPath = heatmapOutputPath;
            }
            catch (Exception ex) when (IsSkiaSharpUnavailable(ex))
            {
                // Cannot save heatmap without SkiaSharp, continue without it
            }
        }

        var finalResult = result with { HeatmapPath = savedHeatmapPath };

        if (!finalResult.IsSimilar)
        {
            throw new PharosAssertException(
                $"Golden image assertion failed. {finalResult}");
        }

        return finalResult;
    }

    /// <summary>
    /// Saves a difference heatmap PNG comparing actual and golden framebuffer snapshots.
    /// </summary>
    /// <param name="actual">The actual framebuffer snapshot.</param>
    /// <param name="golden">The golden reference framebuffer snapshot.</param>
    /// <param name="outputPath">Path to save the heatmap PNG.</param>
    /// <exception cref="ArgumentException">Thrown when dimensions do not match.</exception>
    public static void SaveHeatmap(FramebufferSnapshot actual, FramebufferSnapshot golden, string outputPath)
    {
        ArgumentNullException.ThrowIfNull(actual);
        ArgumentNullException.ThrowIfNull(golden);
        ArgumentException.ThrowIfNullOrEmpty(outputPath);

        if (actual.Width != golden.Width || actual.Height != golden.Height)
        {
            throw new ArgumentException(
                $"Image dimensions mismatch: actual is {actual.Width}×{actual.Height}, " +
                $"golden is {golden.Width}×{golden.Height}.");
        }

        var heatmapPixels = PerceptualDiffCalculator.GenerateHeatmap(
            actual.RawRgba,
            golden.RawRgba,
            actual.Width,
            actual.Height);

        SaveHeatmapPng(heatmapPixels, actual.Width, actual.Height, outputPath);
    }

    /// <summary>
    /// Compares two framebuffer snapshots and returns perceptual diff metrics without asserting.
    /// </summary>
    /// <param name="actual">The actual framebuffer snapshot.</param>
    /// <param name="golden">The golden reference framebuffer snapshot.</param>
    /// <param name="tolerance">Normalized tolerance threshold (0–1).</param>
    /// <returns>The perceptual diff result.</returns>
    public static PerceptualDiffResult Compare(
        FramebufferSnapshot actual,
        FramebufferSnapshot golden,
        float tolerance = 0.02f)
    {
        ArgumentNullException.ThrowIfNull(actual);
        ArgumentNullException.ThrowIfNull(golden);

        if (actual.Width != golden.Width || actual.Height != golden.Height)
        {
            throw new ArgumentException(
                $"Image dimensions mismatch: actual is {actual.Width}×{actual.Height}, " +
                $"golden is {golden.Width}×{golden.Height}.");
        }

        return PerceptualDiffCalculator.CalcDiff(
            actual.RawRgba,
            golden.RawRgba,
            actual.Width,
            actual.Height,
            tolerance);
    }

    private static (byte[] Pixels, int Width, int Height) LoadGoldenImage(string path)
    {
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Golden image not found: {path}");
        }

        using SKBitmap src = SKBitmap.Decode(path)
            ?? throw new InvalidOperationException($"Failed to decode golden image: {path}");

        // Normalize to RGBA8888
        var info = new SKImageInfo(src.Width, src.Height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        SKBitmap? converted = src.Copy(info.ColorType);
        if (converted is null)
        {
            throw new InvalidOperationException($"Failed to convert golden image to RGBA8888: {path}");
        }

        using SKBitmap bmp = converted;
        byte[] pixels = new byte[bmp.Width * bmp.Height * 4];
        System.Runtime.InteropServices.Marshal.Copy(bmp.GetPixels(), pixels, 0, pixels.Length);

        return (pixels, bmp.Width, bmp.Height);
    }

    private static void SaveHeatmapPng(byte[] pixels, int width, int height, string path)
    {
        string? dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        var info = new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        using SKBitmap bmp = new(info);

        unsafe
        {
            fixed (byte* src = pixels)
            {
                Buffer.MemoryCopy(src, bmp.GetPixels().ToPointer(), pixels.Length, pixels.Length);
            }
        }

        using SKData data = bmp.Encode(SKEncodedImageFormat.Png, 100);
        using FileStream fs = File.OpenWrite(path);
        data.SaveTo(fs);
    }

    private static bool IsSkiaSharpUnavailable(Exception ex) =>
        ex is DllNotFoundException ||
        ex is TypeInitializationException ||
        ex.GetType().Name == "PlatformNotSupportedException";
}
