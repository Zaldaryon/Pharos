namespace Zaldaryon.Pharos.Visual;

/// <summary>
/// Pure C# perceptual difference calculator for comparing RGBA pixel arrays.
/// No SkiaSharp or native library dependencies - suitable for headless testing.
/// </summary>
public static class PerceptualDiffCalculator
{
    /// <summary>
    /// Calculates perceptual difference metrics between two RGBA byte arrays.
    /// Both arrays must have identical dimensions (width × height × 4 bytes).
    /// </summary>
    /// <param name="actual">The actual RGBA pixel data from the captured framebuffer.</param>
    /// <param name="golden">The expected RGBA pixel data from the golden reference.</param>
    /// <param name="width">Width of both images in pixels.</param>
    /// <param name="height">Height of both images in pixels.</param>
    /// <param name="tolerance">Normalized tolerance threshold (0–1) for IsSimilar calculation.</param>
    /// <returns>A PerceptualDiffResult with computed difference metrics.</returns>
    /// <exception cref="ArgumentNullException">Thrown when actual or golden is null.</exception>
    /// <exception cref="ArgumentException">Thrown when array lengths do not match expected size.</exception>
    public static PerceptualDiffResult CalcDiff(
        byte[] actual,
        byte[] golden,
        int width,
        int height,
        float tolerance = 0.02f)
    {
        ArgumentNullException.ThrowIfNull(actual);
        ArgumentNullException.ThrowIfNull(golden);

        int expectedLength = width * height * 4;
        if (actual.Length != expectedLength)
        {
            throw new ArgumentException(
                $"Actual array length {actual.Length} does not match width×height×4 = {expectedLength}.",
                nameof(actual));
        }

        if (golden.Length != expectedLength)
        {
            throw new ArgumentException(
                $"Golden array length {golden.Length} does not match width×height×4 = {expectedLength}.",
                nameof(golden));
        }

        int totalPixels = width * height;
        if (totalPixels == 0)
        {
            return new PerceptualDiffResult(
                IsSimilar: true,
                MaxDiff: 0f,
                MeanDiff: 0f,
                DiffPixelCount: 0,
                TotalPixels: 0,
                Tolerance: tolerance);
        }

        float toleranceByte = tolerance * 255f;
        long sumDiff = 0;
        int maxDiffRaw = 0;
        int diffPixelCount = 0;

        // Process all pixels, comparing RGBA channels
        for (int i = 0; i < actual.Length; i += 4)
        {
            int maxPixelDiff = 0;

            // Compare each channel (R, G, B, A)
            for (int c = 0; c < 4; c++)
            {
                int d = Math.Abs((int)actual[i + c] - (int)golden[i + c]);
                sumDiff += d;
                if (d > maxDiffRaw) maxDiffRaw = d;
                if (d > maxPixelDiff) maxPixelDiff = d;
            }

            // Count this pixel as different if any channel exceeds tolerance
            if (maxPixelDiff > toleranceByte)
            {
                diffPixelCount++;
            }
        }

        // Normalize metrics to 0–1 range
        float meanDiff = (float)sumDiff / (totalPixels * 4 * 255f);
        float maxDiff = maxDiffRaw / 255f;
        bool isSimilar = meanDiff <= tolerance;

        return new PerceptualDiffResult(
            IsSimilar: isSimilar,
            MaxDiff: maxDiff,
            MeanDiff: meanDiff,
            DiffPixelCount: diffPixelCount,
            TotalPixels: totalPixels,
            Tolerance: tolerance);
    }

    /// <summary>
    /// Generates a difference heatmap as RGBA pixel data.
    /// Red indicates high difference, green indicates low/no difference.
    /// </summary>
    /// <param name="actual">The actual RGBA pixel data from the captured framebuffer.</param>
    /// <param name="golden">The expected RGBA pixel data from the golden reference.</param>
    /// <param name="width">Width of both images in pixels.</param>
    /// <param name="height">Height of both images in pixels.</param>
    /// <returns>RGBA heatmap pixel array where red = high diff, green = low diff.</returns>
    /// <exception cref="ArgumentNullException">Thrown when actual or golden is null.</exception>
    /// <exception cref="ArgumentException">Thrown when array lengths do not match expected size.</exception>
    public static byte[] GenerateHeatmap(byte[] actual, byte[] golden, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(actual);
        ArgumentNullException.ThrowIfNull(golden);

        int expectedLength = width * height * 4;
        if (actual.Length != expectedLength)
        {
            throw new ArgumentException(
                $"Actual array length {actual.Length} does not match width×height×4 = {expectedLength}.",
                nameof(actual));
        }

        if (golden.Length != expectedLength)
        {
            throw new ArgumentException(
                $"Golden array length {golden.Length} does not match width×height×4 = {expectedLength}.",
                nameof(golden));
        }

        byte[] heatmap = new byte[expectedLength];

        for (int i = 0; i < actual.Length; i += 4)
        {
            // Calculate the maximum channel difference for this pixel
            int maxChannelDiff = 0;
            for (int c = 0; c < 4; c++)
            {
                int d = Math.Abs((int)actual[i + c] - (int)golden[i + c]);
                if (d > maxChannelDiff) maxChannelDiff = d;
            }

            // Normalize to 0–255 (maxChannelDiff is already 0–255)
            byte intensity = (byte)maxChannelDiff;

            // Red channel increases with difference
            heatmap[i] = intensity;
            // Green channel decreases with difference
            heatmap[i + 1] = (byte)(255 - intensity);
            // Blue channel stays low
            heatmap[i + 2] = 0;
            // Full alpha
            heatmap[i + 3] = 255;
        }

        return heatmap;
    }
}
