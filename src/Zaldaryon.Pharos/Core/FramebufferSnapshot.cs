using SkiaSharp;

namespace Zaldaryon.Pharos.Core;

/// <summary>
/// Immutable snapshot of rendered framebuffer pixels captured from an offscreen FBO.
/// Pixels are stored in top-down RGBA order (row 0 is the top of the image).
/// </summary>
public sealed class FramebufferSnapshot
{
    /// <summary>
    /// Raw RGBA pixel data, top-down, 4 bytes per pixel. Length = Width * Height * 4.
    /// </summary>
    public byte[] RawRgba { get; }

    /// <summary>Width of the captured framebuffer in pixels.</summary>
    public int Width { get; }

    /// <summary>Height of the captured framebuffer in pixels.</summary>
    public int Height { get; }

    /// <summary>
    /// Constructs a snapshot from pre-captured top-down RGBA pixel data.
    /// </summary>
    public FramebufferSnapshot(byte[] rawRgba, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(rawRgba);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);

        if (rawRgba.Length != width * height * 4)
            throw new ArgumentException(
                $"rawRgba length {rawRgba.Length} does not match width×height×4 = {width * height * 4}.",
                nameof(rawRgba));

        RawRgba = rawRgba;
        Width = width;
        Height = height;
    }

    /// <summary>
    /// Returns the RGBA components of the pixel at (x, y) where (0, 0) is the top-left corner.
    /// </summary>
    public (byte R, byte G, byte B, byte A) GetPixel(int x, int y)
    {
        if ((uint)x >= (uint)Width)
            throw new ArgumentOutOfRangeException(nameof(x), x, $"x must be in [0, {Width - 1}].");
        if ((uint)y >= (uint)Height)
            throw new ArgumentOutOfRangeException(nameof(y), y, $"y must be in [0, {Height - 1}].");

        int offset = (y * Width + x) * 4;
        return (RawRgba[offset], RawRgba[offset + 1], RawRgba[offset + 2], RawRgba[offset + 3]);
    }

    /// <summary>
    /// Saves the snapshot as a PNG file at the given path. Creates parent directories if needed.
    /// Uses SkiaSharp for encoding.
    /// </summary>
    public void SaveToPng(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);

        string? dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        var info = new SKImageInfo(Width, Height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        using SKBitmap bmp = new(info);
        unsafe
        {
            fixed (byte* src = RawRgba)
            {
                Buffer.MemoryCopy(src, bmp.GetPixels().ToPointer(), RawRgba.Length, RawRgba.Length);
            }
        }
        using SKData data = bmp.Encode(SKEncodedImageFormat.Png, 100);
        using FileStream fs = File.Create(path);
        data.SaveTo(fs);
    }

    /// <summary>
    /// Loads a FramebufferSnapshot from a PNG file. The image is decoded into top-down RGBA order.
    /// </summary>
    public static FramebufferSnapshot FromFile(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);

        (byte[] pixels, int width, int height) = DecodeRgba(path);
        return new FramebufferSnapshot(pixels, width, height);
    }

    /// <summary>
    /// Decodes an image into top-down RGBA bytes with straight (unpremultiplied) alpha, as
    /// snapshots hold them: a pixel saved with <see cref="SaveToPng"/> reads back unchanged.
    /// </summary>
    internal static (byte[] Pixels, int Width, int Height) DecodeRgba(string path)
    {
        using SKCodec codec = SKCodec.Create(path)
            ?? throw new InvalidOperationException($"Failed to decode image: {path}");

        // Decoding straight into an unpremultiplied RGBA bitmap: the default decode premultiplies.
        SKImageInfo info = codec.Info.WithColorType(SKColorType.Rgba8888).WithAlphaType(SKAlphaType.Unpremul);
        using SKBitmap bmp = SKBitmap.Decode(codec, info)
            ?? throw new InvalidOperationException($"Failed to decode image: {path}");
        byte[] pixels = new byte[bmp.Width * bmp.Height * 4];
        System.Runtime.InteropServices.Marshal.Copy(bmp.GetPixels(), pixels, 0, pixels.Length);
        return (pixels, bmp.Width, bmp.Height);
    }

    /// <summary>
    /// Compares this snapshot against another snapshot at pixel level.
    /// Both snapshots must have identical dimensions.
    /// </summary>
    public FramebufferComparisonResult Compare(FramebufferSnapshot other, float tolerance = 0.01f)
    {
        ArgumentNullException.ThrowIfNull(other);

        if (other.Width != Width || other.Height != Height)
            throw new ArgumentException(
                $"Snapshot size mismatch: this is {Width}×{Height}, other is {other.Width}×{other.Height}.",
                nameof(other));

        return ComputeDiff(RawRgba, other.RawRgba, Width * Height, tolerance);
    }

    /// <summary>
    /// Compares this snapshot against a raw RGBA byte array of identical dimensions.
    /// </summary>
    public FramebufferComparisonResult Compare(byte[] reference, float tolerance = 0.01f)
    {
        ArgumentNullException.ThrowIfNull(reference);

        if (reference.Length != RawRgba.Length)
            throw new ArgumentException(
                $"Reference array length {reference.Length} does not match snapshot length {RawRgba.Length}.",
                nameof(reference));

        return ComputeDiff(RawRgba, reference, Width * Height, tolerance);
    }

    private static FramebufferComparisonResult ComputeDiff(
        byte[] a, byte[] b, int totalPixels, float tolerance)
    {
        float toleranceByte = tolerance * 255f;
        long sumDiff = 0;
        int maxDiffRaw = 0;
        int diffPixelCount = 0;

        for (int i = 0; i < a.Length; i += 4)
        {
            int maxPixelDiff = 0;
            for (int c = 0; c < 4; c++)
            {
                int d = Math.Abs((int)a[i + c] - (int)b[i + c]);
                sumDiff += d;
                if (d > maxDiffRaw) maxDiffRaw = d;
                if (d > maxPixelDiff) maxPixelDiff = d;
            }
            if (maxPixelDiff > toleranceByte) diffPixelCount++;
        }

        float meanDiff = totalPixels == 0 ? 0f : (float)sumDiff / (totalPixels * 4 * 255f);
        float maxDiff = maxDiffRaw / 255f;

        return new FramebufferComparisonResult
        {
            Tolerance = tolerance,
            MeanDiff = meanDiff,
            MaxDiff = maxDiff,
            DiffPixelCount = diffPixelCount,
            TotalPixels = totalPixels,
        };
    }
}
