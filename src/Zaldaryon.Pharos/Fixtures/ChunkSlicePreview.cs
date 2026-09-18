using System;
using System.Collections.Generic;
using System.Text;

namespace Zaldaryon.Pharos.Fixtures;

/// <summary>
/// Renders chunk fixture slices as ASCII art or raw RGBA pixel data for debugging and visualization.
/// All rendering is software-based and works without a GPU.
/// </summary>
public static class ChunkSlicePreview
{
    /// <summary>
    /// Default color for air blocks (transparent).
    /// </summary>
    public static readonly (byte r, byte g, byte b) AirColor = (0, 0, 0);

    /// <summary>
    /// Default color for unknown blocks (magenta for visibility).
    /// </summary>
    public static readonly (byte r, byte g, byte b) UnknownColor = (255, 0, 255);

    /// <summary>
    /// Renders a horizontal XZ slice of the chunk at the specified Y level as ASCII art.
    /// </summary>
    /// <param name="fixture">The chunk fixture to render.</param>
    /// <param name="y">The Y level to slice (0-31).</param>
    /// <param name="solidChar">Character to use for solid (non-air) blocks.</param>
    /// <param name="airChar">Character to use for air blocks.</param>
    /// <returns>A 32-line string representing the XZ slice, with Z rows from 0 (top) to 31 (bottom).</returns>
    public static string RenderAscii(ChunkFixture fixture, int y, char solidChar = '#', char airChar = '.')
    {
        ArgumentNullException.ThrowIfNull(fixture);
        ValidateYLevel(y);

        return RenderAsciiSlice(fixture, ChunkSliceAxis.XZ, y, solidChar, airChar);
    }

    /// <summary>
    /// Renders a slice of the chunk along the specified axis as ASCII art.
    /// </summary>
    /// <param name="fixture">The chunk fixture to render.</param>
    /// <param name="axis">The axis along which to slice.</param>
    /// <param name="sliceIndex">The position along the perpendicular axis (0-31).</param>
    /// <param name="solidChar">Character to use for solid (non-air) blocks.</param>
    /// <param name="airChar">Character to use for air blocks.</param>
    /// <returns>A 32-line string representing the slice.</returns>
    public static string RenderAsciiSlice(ChunkFixture fixture, ChunkSliceAxis axis, int sliceIndex, char solidChar = '#', char airChar = '.')
    {
        ArgumentNullException.ThrowIfNull(fixture);
        ValidateSliceIndex(sliceIndex);

        var sb = new StringBuilder();

        switch (axis)
        {
            case ChunkSliceAxis.XZ:
                // Horizontal slice at Y = sliceIndex, showing X (cols) × Z (rows)
                for (int z = 0; z < ChunkFixture.ChunkSize; z++)
                {
                    for (int x = 0; x < ChunkFixture.ChunkSize; x++)
                    {
                        bool isSolid = ChunkFixtureGenerator.IsSolid(fixture, x, sliceIndex, z);
                        sb.Append(isSolid ? solidChar : airChar);
                    }
                    if (z < ChunkFixture.ChunkSize - 1)
                    {
                        sb.AppendLine();
                    }
                }
                break;

            case ChunkSliceAxis.XY:
                // Vertical slice at Z = sliceIndex, showing X (cols) × Y (rows, bottom to top)
                for (int y = ChunkFixture.ChunkSize - 1; y >= 0; y--)
                {
                    for (int x = 0; x < ChunkFixture.ChunkSize; x++)
                    {
                        bool isSolid = ChunkFixtureGenerator.IsSolid(fixture, x, y, sliceIndex);
                        sb.Append(isSolid ? solidChar : airChar);
                    }
                    if (y > 0)
                    {
                        sb.AppendLine();
                    }
                }
                break;

            case ChunkSliceAxis.YZ:
                // Vertical slice at X = sliceIndex, showing Z (cols) × Y (rows, bottom to top)
                for (int y = ChunkFixture.ChunkSize - 1; y >= 0; y--)
                {
                    for (int z = 0; z < ChunkFixture.ChunkSize; z++)
                    {
                        bool isSolid = ChunkFixtureGenerator.IsSolid(fixture, sliceIndex, y, z);
                        sb.Append(isSolid ? solidChar : airChar);
                    }
                    if (y > 0)
                    {
                        sb.AppendLine();
                    }
                }
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(axis), axis, "Unknown slice axis.");
        }

        return sb.ToString();
    }

    /// <summary>
    /// Renders a horizontal XZ slice of the chunk at the specified Y level as raw RGBA pixel data.
    /// Returns a 32×32 pixel image as a flat byte array (32 * 32 * 4 = 4096 bytes).
    /// </summary>
    /// <param name="fixture">The chunk fixture to render.</param>
    /// <param name="y">The Y level to slice (0-31).</param>
    /// <param name="palette">Dictionary mapping block codes to RGB colors.</param>
    /// <returns>Raw RGBA pixel data (4096 bytes) for a 32×32 image.</returns>
    public static byte[] RenderPng(ChunkFixture fixture, int y, IDictionary<string, (byte r, byte g, byte b)> palette)
    {
        ArgumentNullException.ThrowIfNull(fixture);
        ValidateYLevel(y);
        ArgumentNullException.ThrowIfNull(palette);

        return RenderPngSlice(fixture, ChunkSliceAxis.XZ, y, palette);
    }

    /// <summary>
    /// Renders a slice of the chunk along the specified axis as raw RGBA pixel data.
    /// Returns a 32×32 pixel image as a flat byte array (32 * 32 * 4 = 4096 bytes).
    /// </summary>
    /// <param name="fixture">The chunk fixture to render.</param>
    /// <param name="axis">The axis along which to slice.</param>
    /// <param name="sliceIndex">The position along the perpendicular axis (0-31).</param>
    /// <param name="palette">Dictionary mapping block codes to RGB colors.</param>
    /// <returns>Raw RGBA pixel data (4096 bytes) for a 32×32 image.</returns>
    public static byte[] RenderPngSlice(ChunkFixture fixture, ChunkSliceAxis axis, int sliceIndex, IDictionary<string, (byte r, byte g, byte b)> palette)
    {
        ArgumentNullException.ThrowIfNull(fixture);
        ValidateSliceIndex(sliceIndex);
        ArgumentNullException.ThrowIfNull(palette);

        const int size = ChunkFixture.ChunkSize;
        const int pixelCount = size * size;
        const int bytesPerPixel = 4; // RGBA
        byte[] pixels = new byte[pixelCount * bytesPerPixel];

        int pixelIndex = 0;

        switch (axis)
        {
            case ChunkSliceAxis.XZ:
                // Horizontal slice at Y = sliceIndex
                for (int z = 0; z < size; z++)
                {
                    for (int x = 0; x < size; x++)
                    {
                        var color = GetBlockColor(fixture, x, sliceIndex, z, palette);
                        WritePixel(pixels, pixelIndex++, color);
                    }
                }
                break;

            case ChunkSliceAxis.XY:
                // Vertical slice at Z = sliceIndex (Y inverted for image coordinates)
                for (int y = size - 1; y >= 0; y--)
                {
                    for (int x = 0; x < size; x++)
                    {
                        var color = GetBlockColor(fixture, x, y, sliceIndex, palette);
                        WritePixel(pixels, pixelIndex++, color);
                    }
                }
                break;

            case ChunkSliceAxis.YZ:
                // Vertical slice at X = sliceIndex (Y inverted for image coordinates)
                for (int y = size - 1; y >= 0; y--)
                {
                    for (int z = 0; z < size; z++)
                    {
                        var color = GetBlockColor(fixture, sliceIndex, y, z, palette);
                        WritePixel(pixels, pixelIndex++, color);
                    }
                }
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(axis), axis, "Unknown slice axis.");
        }

        return pixels;
    }

    /// <summary>
    /// Creates a default palette with common Vintage Story block colors.
    /// </summary>
    /// <returns>A dictionary mapping common block codes to RGB colors.</returns>
    public static Dictionary<string, (byte r, byte g, byte b)> CreateDefaultPalette()
    {
        return new Dictionary<string, (byte r, byte g, byte b)>(StringComparer.OrdinalIgnoreCase)
        {
            ["game:air"] = (0, 0, 0),
            ["game:rock-granite"] = (128, 128, 128),
            ["game:rock-basalt"] = (64, 64, 64),
            ["game:rock-limestone"] = (200, 200, 180),
            ["game:rock-sandstone"] = (194, 178, 128),
            ["game:soil-low"] = (139, 90, 43),
            ["game:soil-medium"] = (101, 67, 33),
            ["game:soil-high"] = (72, 60, 50),
            ["game:gravel-granite"] = (180, 180, 180),
            ["game:sand-plain"] = (237, 201, 175),
            ["game:water-still-7"] = (64, 164, 223),
            ["game:lava-still-7"] = (255, 100, 0),
            ["game:log-oak-ud"] = (133, 94, 66),
            ["game:leaves-oak"] = (34, 139, 34),
            ["game:grass-free"] = (86, 152, 23),
            ["game:tallgrass-tall"] = (124, 252, 0),
            ["game:ore-coal"] = (32, 32, 32),
            ["game:ore-copper"] = (184, 115, 51),
            ["game:ore-tin"] = (211, 212, 213),
            ["game:ore-iron"] = (139, 69, 19),
            ["game:snow"] = (255, 250, 250),
            ["game:ice"] = (200, 233, 233),
        };
    }

    /// <summary>
    /// Counts the occurrences of each unique color in the rendered pixel data.
    /// Useful for testing that the correct blocks are present in a slice.
    /// </summary>
    /// <param name="pixels">Raw RGBA pixel data from RenderPng.</param>
    /// <returns>Dictionary mapping (r,g,b) colors to their pixel counts.</returns>
    public static Dictionary<(byte r, byte g, byte b), int> CountColors(byte[] pixels)
    {
        ArgumentNullException.ThrowIfNull(pixels);
        if (pixels.Length % 4 != 0)
        {
            throw new ArgumentException("Pixel array length must be a multiple of 4 (RGBA).", nameof(pixels));
        }

        var counts = new Dictionary<(byte r, byte g, byte b), int>();
        for (int i = 0; i < pixels.Length; i += 4)
        {
            var color = (pixels[i], pixels[i + 1], pixels[i + 2]);
            counts.TryGetValue(color, out int count);
            counts[color] = count + 1;
        }
        return counts;
    }

    /// <summary>
    /// Counts how many pixels in the slice are solid (non-air, non-transparent).
    /// </summary>
    /// <param name="pixels">Raw RGBA pixel data from RenderPng.</param>
    /// <returns>The number of non-transparent pixels.</returns>
    public static int CountSolidPixels(byte[] pixels)
    {
        ArgumentNullException.ThrowIfNull(pixels);
        if (pixels.Length % 4 != 0)
        {
            throw new ArgumentException("Pixel array length must be a multiple of 4 (RGBA).", nameof(pixels));
        }

        int count = 0;
        for (int i = 0; i < pixels.Length; i += 4)
        {
            // Check alpha channel - if not fully transparent, it's solid
            if (pixels[i + 3] > 0)
            {
                count++;
            }
        }
        return count;
    }

    /// <summary>
    /// Counts how many characters in ASCII output match the solid character.
    /// </summary>
    /// <param name="ascii">ASCII output from RenderAscii.</param>
    /// <param name="solidChar">The solid character used in rendering.</param>
    /// <returns>Count of solid characters.</returns>
    public static int CountSolidChars(string ascii, char solidChar = '#')
    {
        ArgumentNullException.ThrowIfNull(ascii);
        int count = 0;
        foreach (char c in ascii)
        {
            if (c == solidChar)
            {
                count++;
            }
        }
        return count;
    }

    private static (byte r, byte g, byte b, byte a) GetBlockColor(
        ChunkFixture fixture, int x, int y, int z,
        IDictionary<string, (byte r, byte g, byte b)> palette)
    {
        var blockCode = ChunkFixtureGenerator.GetBlockCode(fixture, x, y, z);

        if (string.Equals(blockCode, ChunkFixtureGenerator.AirBlockCode, StringComparison.OrdinalIgnoreCase))
        {
            return (AirColor.r, AirColor.g, AirColor.b, 0); // Transparent
        }

        if (palette.TryGetValue(blockCode, out var color))
        {
            return (color.r, color.g, color.b, 255); // Opaque
        }

        return (UnknownColor.r, UnknownColor.g, UnknownColor.b, 255); // Unknown block - magenta
    }

    private static void WritePixel(byte[] pixels, int index, (byte r, byte g, byte b, byte a) color)
    {
        int offset = index * 4;
        pixels[offset] = color.r;
        pixels[offset + 1] = color.g;
        pixels[offset + 2] = color.b;
        pixels[offset + 3] = color.a;
    }

    private static void ValidateYLevel(int y)
    {
        if (y < 0 || y >= ChunkFixture.ChunkSize)
        {
            throw new ArgumentOutOfRangeException(nameof(y), y, $"Y level must be in range 0..{ChunkFixture.ChunkSize - 1}.");
        }
    }

    private static void ValidateSliceIndex(int index)
    {
        if (index < 0 || index >= ChunkFixture.ChunkSize)
        {
            throw new ArgumentOutOfRangeException(nameof(index), index, $"Slice index must be in range 0..{ChunkFixture.ChunkSize - 1}.");
        }
    }
}
