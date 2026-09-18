using Xunit;
using Zaldaryon.Pharos.Assertions;
using Zaldaryon.Pharos.Visual;

namespace Zaldaryon.Pharos.Tests.Visual;

/// <summary>
/// Unit tests for <see cref="PerceptualDiffCalculator"/>, <see cref="PerceptualDiffResult"/>,
/// and <see cref="PharosAssert.GoldenImageMatch"/>.
/// All tests use synthetic pixel arrays - no SkiaSharp native library required.
/// </summary>
public sealed class GoldenImageTests
{
    // -------------------------------------------------------------------------
    // Helper factories
    // -------------------------------------------------------------------------

    private static byte[] MakeRgba(int width, int height, byte r, byte g, byte b, byte a)
    {
        byte[] data = new byte[width * height * 4];
        for (int i = 0; i < data.Length; i += 4)
        {
            data[i] = r;
            data[i + 1] = g;
            data[i + 2] = b;
            data[i + 3] = a;
        }
        return data;
    }

    private static byte[] MakeGradient(int width, int height)
    {
        byte[] data = new byte[width * height * 4];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int i = (y * width + x) * 4;
                byte intensity = (byte)((x + y) * 255 / (width + height - 2));
                data[i] = intensity;     // R
                data[i + 1] = intensity; // G
                data[i + 2] = intensity; // B
                data[i + 3] = 255;       // A
            }
        }
        return data;
    }

    // -------------------------------------------------------------------------
    // PerceptualDiffCalculator.CalcDiff tests
    // -------------------------------------------------------------------------

    [Fact]
    public void CalcDiff_IdenticalImages_ReturnsSimilarWithZeroDiff()
    {
        byte[] pixels = MakeRgba(4, 4, 128, 64, 32, 255);

        var result = PerceptualDiffCalculator.CalcDiff(pixels, pixels, 4, 4, tolerance: 0.0f);

        Assert.True(result.IsSimilar);
        Assert.Equal(0f, result.MeanDiff);
        Assert.Equal(0f, result.MaxDiff);
        Assert.Equal(0, result.DiffPixelCount);
        Assert.Equal(16, result.TotalPixels);
    }

    [Fact]
    public void CalcDiff_BlackVsWhite_ReturnsNotSimilarWithMaxDiff()
    {
        byte[] black = MakeRgba(4, 4, 0, 0, 0, 0);
        byte[] white = MakeRgba(4, 4, 255, 255, 255, 255);

        var result = PerceptualDiffCalculator.CalcDiff(black, white, 4, 4, tolerance: 0.5f);

        Assert.False(result.IsSimilar);
        Assert.Equal(1.0f, result.MeanDiff, precision: 4);
        Assert.Equal(1.0f, result.MaxDiff, precision: 4);
        Assert.Equal(16, result.DiffPixelCount);
        Assert.Equal(16, result.TotalPixels);
    }

    [Fact]
    public void CalcDiff_SmallDifference_WithinTolerance_ReturnsSimilar()
    {
        // Two images differing by 1/255 ≈ 0.004 per channel
        byte[] imageA = MakeRgba(4, 4, 128, 128, 128, 255);
        byte[] imageB = MakeRgba(4, 4, 129, 127, 128, 255);

        var result = PerceptualDiffCalculator.CalcDiff(imageA, imageB, 4, 4, tolerance: 0.01f);

        Assert.True(result.IsSimilar, $"Expected similar at tolerance 0.01, got: {result}");
        Assert.True(result.MeanDiff < 0.01f);
    }

    [Fact]
    public void CalcDiff_SmallDifference_BelowTolerance_ReturnsNotSimilar()
    {
        byte[] imageA = MakeRgba(4, 4, 128, 128, 128, 255);
        byte[] imageB = MakeRgba(4, 4, 129, 127, 128, 255);

        var result = PerceptualDiffCalculator.CalcDiff(imageA, imageB, 4, 4, tolerance: 0.0001f);

        Assert.False(result.IsSimilar, $"Expected not similar at tolerance 0.0001, got: {result}");
    }

    [Fact]
    public void CalcDiff_ZeroSizeImage_ReturnsSimilarWithZeroTotals()
    {
        byte[] empty = Array.Empty<byte>();

        var result = PerceptualDiffCalculator.CalcDiff(empty, empty, 0, 0, tolerance: 0.02f);

        Assert.True(result.IsSimilar);
        Assert.Equal(0, result.TotalPixels);
        Assert.Equal(0, result.DiffPixelCount);
    }

    [Fact]
    public void CalcDiff_NullActual_ThrowsArgumentNullException()
    {
        byte[] golden = MakeRgba(4, 4, 128, 128, 128, 255);

        Assert.Throws<ArgumentNullException>(() =>
            PerceptualDiffCalculator.CalcDiff(null!, golden, 4, 4));
    }

    [Fact]
    public void CalcDiff_NullGolden_ThrowsArgumentNullException()
    {
        byte[] actual = MakeRgba(4, 4, 128, 128, 128, 255);

        Assert.Throws<ArgumentNullException>(() =>
            PerceptualDiffCalculator.CalcDiff(actual, null!, 4, 4));
    }

    [Fact]
    public void CalcDiff_MismatchedActualLength_ThrowsArgumentException()
    {
        byte[] actual = MakeRgba(2, 2, 128, 128, 128, 255); // 16 bytes
        byte[] golden = MakeRgba(4, 4, 128, 128, 128, 255); // 64 bytes

        var ex = Assert.Throws<ArgumentException>(() =>
            PerceptualDiffCalculator.CalcDiff(actual, golden, 4, 4));
        Assert.Contains("Actual array length", ex.Message);
    }

    [Fact]
    public void CalcDiff_MismatchedGoldenLength_ThrowsArgumentException()
    {
        byte[] actual = MakeRgba(4, 4, 128, 128, 128, 255); // 64 bytes
        byte[] golden = MakeRgba(2, 2, 128, 128, 128, 255); // 16 bytes

        var ex = Assert.Throws<ArgumentException>(() =>
            PerceptualDiffCalculator.CalcDiff(actual, golden, 4, 4));
        Assert.Contains("Golden array length", ex.Message);
    }

    // -------------------------------------------------------------------------
    // PerceptualDiffCalculator.GenerateHeatmap tests
    // -------------------------------------------------------------------------

    [Fact]
    public void GenerateHeatmap_IdenticalImages_AllGreen()
    {
        byte[] pixels = MakeRgba(4, 4, 128, 64, 32, 255);

        byte[] heatmap = PerceptualDiffCalculator.GenerateHeatmap(pixels, pixels, 4, 4);

        Assert.Equal(pixels.Length, heatmap.Length);

        // All pixels should be green (R=0, G=255) when no difference
        for (int i = 0; i < heatmap.Length; i += 4)
        {
            Assert.Equal(0, heatmap[i]);     // R = 0 (no diff)
            Assert.Equal(255, heatmap[i + 1]); // G = 255 (max green)
            Assert.Equal(0, heatmap[i + 2]);   // B = 0
            Assert.Equal(255, heatmap[i + 3]); // A = 255
        }
    }

    [Fact]
    public void GenerateHeatmap_BlackVsWhite_AllRed()
    {
        byte[] black = MakeRgba(4, 4, 0, 0, 0, 0);
        byte[] white = MakeRgba(4, 4, 255, 255, 255, 255);

        byte[] heatmap = PerceptualDiffCalculator.GenerateHeatmap(black, white, 4, 4);

        // All pixels should be red (R=255, G=0) when max difference
        for (int i = 0; i < heatmap.Length; i += 4)
        {
            Assert.Equal(255, heatmap[i]);     // R = 255 (max diff)
            Assert.Equal(0, heatmap[i + 1]);   // G = 0 (min green)
            Assert.Equal(0, heatmap[i + 2]);   // B = 0
            Assert.Equal(255, heatmap[i + 3]); // A = 255
        }
    }

    [Fact]
    public void GenerateHeatmap_PartialDifference_MixedColors()
    {
        // Image A: 128 gray
        // Image B: 178 gray (diff = 50)
        byte[] imageA = MakeRgba(2, 2, 128, 128, 128, 255);
        byte[] imageB = MakeRgba(2, 2, 178, 178, 178, 255);

        byte[] heatmap = PerceptualDiffCalculator.GenerateHeatmap(imageA, imageB, 2, 2);

        // R should be 50 (the max channel diff), G should be 205 (255-50)
        for (int i = 0; i < heatmap.Length; i += 4)
        {
            Assert.Equal(50, heatmap[i]);      // R = diff intensity
            Assert.Equal(205, heatmap[i + 1]); // G = 255 - diff
            Assert.Equal(0, heatmap[i + 2]);   // B = 0
            Assert.Equal(255, heatmap[i + 3]); // A = 255
        }
    }

    [Fact]
    public void GenerateHeatmap_NullActual_ThrowsArgumentNullException()
    {
        byte[] golden = MakeRgba(4, 4, 128, 128, 128, 255);

        Assert.Throws<ArgumentNullException>(() =>
            PerceptualDiffCalculator.GenerateHeatmap(null!, golden, 4, 4));
    }

    // -------------------------------------------------------------------------
    // PerceptualDiffResult tests
    // -------------------------------------------------------------------------

    [Fact]
    public void PerceptualDiffResult_DiffPixelFraction_CalculatesCorrectly()
    {
        var result = new PerceptualDiffResult(
            IsSimilar: false,
            MaxDiff: 0.5f,
            MeanDiff: 0.3f,
            DiffPixelCount: 25,
            TotalPixels: 100,
            Tolerance: 0.1f);

        Assert.Equal(0.25f, result.DiffPixelFraction);
    }

    [Fact]
    public void PerceptualDiffResult_DiffPixelFraction_ZeroPixels_ReturnsZero()
    {
        var result = new PerceptualDiffResult(
            IsSimilar: true,
            MaxDiff: 0f,
            MeanDiff: 0f,
            DiffPixelCount: 0,
            TotalPixels: 0,
            Tolerance: 0.02f);

        Assert.Equal(0f, result.DiffPixelFraction);
    }

    [Fact]
    public void PerceptualDiffResult_ToString_ContainsAllMetrics()
    {
        var result = new PerceptualDiffResult(
            IsSimilar: true,
            MaxDiff: 0.05f,
            MeanDiff: 0.01f,
            DiffPixelCount: 5,
            TotalPixels: 100,
            Tolerance: 0.02f);

        string str = result.ToString();

        Assert.Contains("IsSimilar=True", str);
        Assert.Contains("MeanDiff=", str);
        Assert.Contains("MaxDiff=", str);
        Assert.Contains("DiffPixels=5/100", str);
        Assert.Contains("Tolerance=", str);
    }

    [Fact]
    public void PerceptualDiffResult_ToString_IncludesHeatmapPath_WhenProvided()
    {
        var result = new PerceptualDiffResult(
            IsSimilar: false,
            MaxDiff: 0.5f,
            MeanDiff: 0.3f,
            DiffPixelCount: 50,
            TotalPixels: 100,
            Tolerance: 0.1f,
            HeatmapPath: "/tmp/diff.png");

        string str = result.ToString();

        Assert.Contains("Heatmap=/tmp/diff.png", str);
    }

    // -------------------------------------------------------------------------
    // PharosAssert.GoldenImageMatch tests
    // -------------------------------------------------------------------------

    [Fact]
    public void GoldenImageMatch_SimilarResult_DoesNotThrow()
    {
        var result = new PerceptualDiffResult(
            IsSimilar: true,
            MaxDiff: 0.01f,
            MeanDiff: 0.005f,
            DiffPixelCount: 2,
            TotalPixels: 100,
            Tolerance: 0.02f);

        // Should not throw
        PharosAssert.GoldenImageMatch(result);
    }

    [Fact]
    public void GoldenImageMatch_NotSimilarResult_ThrowsPharosAssertException()
    {
        var result = new PerceptualDiffResult(
            IsSimilar: false,
            MaxDiff: 0.5f,
            MeanDiff: 0.3f,
            DiffPixelCount: 50,
            TotalPixels: 100,
            Tolerance: 0.1f);

        var ex = Assert.Throws<PharosAssertException>(() =>
            PharosAssert.GoldenImageMatch(result));

        Assert.Contains("Golden image match failed", ex.Message);
        Assert.Contains("MeanDiff=", ex.Message);
        Assert.Contains("tolerance=", ex.Message);
    }

    [Fact]
    public void GoldenImageMatch_WithCustomMessage_IncludesMessageInException()
    {
        var result = new PerceptualDiffResult(
            IsSimilar: false,
            MaxDiff: 0.5f,
            MeanDiff: 0.3f,
            DiffPixelCount: 50,
            TotalPixels: 100,
            Tolerance: 0.1f);

        var ex = Assert.Throws<PharosAssertException>(() =>
            PharosAssert.GoldenImageMatch(result, "Custom test context:"));

        Assert.Contains("Custom test context:", ex.Message);
    }

    [Fact]
    public void GoldenImageMatch_WithHeatmapPath_IncludesPathInException()
    {
        var result = new PerceptualDiffResult(
            IsSimilar: false,
            MaxDiff: 0.5f,
            MeanDiff: 0.3f,
            DiffPixelCount: 50,
            TotalPixels: 100,
            Tolerance: 0.1f,
            HeatmapPath: "/output/diff_heatmap.png");

        var ex = Assert.Throws<PharosAssertException>(() =>
            PharosAssert.GoldenImageMatch(result));

        Assert.Contains("/output/diff_heatmap.png", ex.Message);
    }

    // -------------------------------------------------------------------------
    // Integration: CalcDiff -> GoldenImageMatch flow
    // -------------------------------------------------------------------------

    [Fact]
    public void Integration_CalcDiff_GoldenImageMatch_IdenticalImages_Passes()
    {
        byte[] pixels = MakeGradient(8, 8);

        var result = PerceptualDiffCalculator.CalcDiff(pixels, pixels, 8, 8, tolerance: 0.0f);

        // Should not throw
        PharosAssert.GoldenImageMatch(result);
    }

    [Fact]
    public void Integration_CalcDiff_GoldenImageMatch_DifferentImages_Fails()
    {
        byte[] imageA = MakeGradient(8, 8);
        byte[] imageB = MakeRgba(8, 8, 255, 0, 0, 255); // Solid red

        var result = PerceptualDiffCalculator.CalcDiff(imageA, imageB, 8, 8, tolerance: 0.02f);

        Assert.Throws<PharosAssertException>(() =>
            PharosAssert.GoldenImageMatch(result, "Gradient vs solid red"));
    }
}
