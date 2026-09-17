using System.IO;
using Xunit;
using Zaldaryon.Pharos.Core;

namespace Zaldaryon.Pharos.Tests;

/// <summary>
/// Unit tests for <see cref="FramebufferSnapshot"/> and <see cref="FramebufferComparisonResult"/>.
/// None of these tests require a live OpenGL context; they exercise the data and comparison
/// logic using in-memory RGBA byte arrays.
/// </summary>
public sealed class FramebufferCaptureTests
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

    // -------------------------------------------------------------------------
    // FramebufferSnapshot construction and property tests
    // -------------------------------------------------------------------------

    [Fact]
    public void FramebufferSnapshot_Dimensions_MatchInput()
    {
        byte[] raw = MakeRgba(8, 6, 128, 0, 0, 255);
        var snap = new FramebufferSnapshot(raw, 8, 6);

        Assert.Equal(8, snap.Width);
        Assert.Equal(6, snap.Height);
        Assert.Equal(8 * 6 * 4, snap.RawRgba.Length);
    }

    [Fact]
    public void FramebufferSnapshot_GetPixel_ReturnsCorrectRgba()
    {
        // 2×1 image: pixel 0 = (10, 20, 30, 255), pixel 1 = (40, 50, 60, 200)
        byte[] raw = new byte[]
        {
            10, 20, 30, 255,   // pixel at (0,0)
            40, 50, 60, 200,   // pixel at (1,0)
        };
        var snap = new FramebufferSnapshot(raw, 2, 1);

        var (r0, g0, b0, a0) = snap.GetPixel(0, 0);
        var (r1, g1, b1, a1) = snap.GetPixel(1, 0);

        Assert.Equal(10, r0);
        Assert.Equal(20, g0);
        Assert.Equal(30, b0);
        Assert.Equal(255, a0);

        Assert.Equal(40, r1);
        Assert.Equal(50, g1);
        Assert.Equal(60, b1);
        Assert.Equal(200, a1);
    }

    // -------------------------------------------------------------------------
    // Comparison tests
    // -------------------------------------------------------------------------

    [Fact]
    public void FramebufferSnapshot_Compare_IdenticalImages_ReturnsSimilar()
    {
        byte[] raw = MakeRgba(4, 4, 128, 64, 32, 255);
        var snap = new FramebufferSnapshot(raw, 4, 4);

        FramebufferComparisonResult result = snap.Compare(snap, tolerance: 0.0f);

        Assert.True(result.IsSimilar);
        Assert.Equal(0f, result.MeanDiff);
        Assert.Equal(0f, result.MaxDiff);
        Assert.Equal(0, result.DiffPixelCount);
    }

    [Fact]
    public void FramebufferSnapshot_Compare_AllBlackVsAllWhite_ReturnsNotSimilar()
    {
        byte[] black = MakeRgba(4, 4, 0, 0, 0, 0);
        byte[] white = MakeRgba(4, 4, 255, 255, 255, 255);

        var snapBlack = new FramebufferSnapshot(black, 4, 4);
        var snapWhite = new FramebufferSnapshot(white, 4, 4);

        FramebufferComparisonResult result = snapBlack.Compare(snapWhite, tolerance: 0.5f);

        Assert.False(result.IsSimilar);
        Assert.Equal(1.0f, result.MeanDiff, precision: 4);
        Assert.Equal(1.0f, result.MaxDiff, precision: 4);
        Assert.Equal(16, result.TotalPixels);
        Assert.Equal(16, result.DiffPixelCount);
    }

    [Fact]
    public void FramebufferSnapshot_Compare_SizeMismatch_ThrowsArgumentException()
    {
        var snap4x4 = new FramebufferSnapshot(MakeRgba(4, 4, 0, 0, 0, 255), 4, 4);
        var snap8x8 = new FramebufferSnapshot(MakeRgba(8, 8, 0, 0, 0, 255), 8, 8);

        Assert.Throws<ArgumentException>(() => snap4x4.Compare(snap8x8));
    }

    [Fact]
    public void FramebufferComparisonResult_IsSimilar_TrueWhenMeanDiffBelowTolerance()
    {
        var result = new FramebufferComparisonResult
        {
            Tolerance = 0.05f,
            MeanDiff = 0.03f,
            MaxDiff = 0.06f,
            DiffPixelCount = 1,
            TotalPixels = 100,
        };

        Assert.True(result.IsSimilar);
    }

    [Fact]
    public void FramebufferSnapshot_Compare_ByteArray_MatchesSnapshotCompare()
    {
        byte[] rawA = MakeRgba(4, 4, 100, 100, 100, 255);
        byte[] rawB = MakeRgba(4, 4, 110, 90, 100, 255);

        var snapA = new FramebufferSnapshot(rawA, 4, 4);
        var snapB = new FramebufferSnapshot(rawB, 4, 4);

        FramebufferComparisonResult fromSnapshot = snapA.Compare(snapB, tolerance: 0.1f);
        FramebufferComparisonResult fromByteArray = snapA.Compare(rawB, tolerance: 0.1f);

        Assert.Equal(fromSnapshot.MeanDiff, fromByteArray.MeanDiff);
        Assert.Equal(fromSnapshot.MaxDiff, fromByteArray.MaxDiff);
        Assert.Equal(fromSnapshot.DiffPixelCount, fromByteArray.DiffPixelCount);
        Assert.Equal(fromSnapshot.IsSimilar, fromByteArray.IsSimilar);
    }

    [Fact]
    public void FramebufferSnapshot_SaveToPng_AndLoad_RoundTrips()
    {
        // Skip if SkiaSharp native library is not available in this test environment.
        try
        {
            byte[] raw = MakeRgba(4, 4, 200, 100, 50, 255);
            var original = new FramebufferSnapshot(raw, 4, 4);

            string tmp = Path.Combine(Path.GetTempPath(), $"pharos_test_{Guid.NewGuid():N}.png");
            try
            {
                original.SaveToPng(tmp);
                var loaded = FramebufferSnapshot.FromFile(tmp);

                Assert.Equal(original.Width, loaded.Width);
                Assert.Equal(original.Height, loaded.Height);

                FramebufferComparisonResult cmp = original.Compare(loaded, tolerance: 0.0f);
                Assert.True(cmp.IsSimilar, $"Round-trip produced diff: {cmp}");
            }
            finally
            {
                if (File.Exists(tmp)) File.Delete(tmp);
            }
        }
        catch (Exception ex) when (
            ex is DllNotFoundException ||
            ex is TypeInitializationException ||
            ex.GetType().Name == "PlatformNotSupportedException")
        {
            // SkiaSharp native library not available; skip gracefully.
            return;
        }
    }

    [Fact]
    public void FramebufferSnapshot_Compare_ToleranceRespected_BelowThreshold()
    {
        // Two images differing by 1/255 ≈ 0.004 per channel on every pixel.
        byte[] rawA = MakeRgba(4, 4, 128, 128, 128, 255);
        byte[] rawB = MakeRgba(4, 4, 129, 127, 128, 255);

        var snapA = new FramebufferSnapshot(rawA, 4, 4);
        var snapB = new FramebufferSnapshot(rawB, 4, 4);

        // Tolerance of 0.1 should easily accommodate a ~0.004 mean diff.
        FramebufferComparisonResult result = snapA.Compare(snapB, tolerance: 0.1f);

        Assert.True(result.IsSimilar, $"Expected similar at tolerance 0.1, got: {result}");
    }

    [Fact]
    public void FramebufferSnapshot_Compare_ToleranceRespected_AboveThreshold()
    {
        // Same near-identical pair as above.
        byte[] rawA = MakeRgba(4, 4, 128, 128, 128, 255);
        byte[] rawB = MakeRgba(4, 4, 129, 127, 128, 255);

        var snapA = new FramebufferSnapshot(rawA, 4, 4);
        var snapB = new FramebufferSnapshot(rawB, 4, 4);

        // Tolerance of 0.0001 is below the ~0.004 mean diff, so images should not be similar.
        FramebufferComparisonResult result = snapA.Compare(snapB, tolerance: 0.0001f);

        Assert.False(result.IsSimilar, $"Expected not similar at tolerance 0.0001, got: {result}");
    }
}
