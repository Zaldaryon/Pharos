using Xunit;
using Zaldaryon.Pharos.Fixtures;

namespace Zaldaryon.Pharos.Tests.Fixtures;

/// <summary>
/// Tests for ChunkFixtureGenerator and ChunkSlicePreview.
/// All tests are pure logic tests that don't require native libraries or GPU.
/// </summary>
public class ChunkFixtureGeneratorTests
{
    #region Solid Pattern Tests

    [Fact]
    public void Solid_FillsAllBlocksWithSpecifiedCode()
    {
        var fixture = ChunkFixtureGenerator.Solid("game:rock-granite");

        Assert.Equal(ChunkFixture.BlockCount, fixture.BlockCodes.Count);
        Assert.All(fixture.BlockCodes.Values, code => Assert.Equal("game:rock-granite", code));
    }

    [Fact]
    public void Solid_ThrowsOnNullOrEmptyBlockCode()
    {
        Assert.Throws<ArgumentNullException>(() => ChunkFixtureGenerator.Solid(null!));
        Assert.Throws<ArgumentException>(() => ChunkFixtureGenerator.Solid(""));
    }

    [Fact]
    public void Empty_FillsAllBlocksWithAir()
    {
        var fixture = ChunkFixtureGenerator.Empty();

        Assert.Equal(ChunkFixture.BlockCount, fixture.BlockCodes.Count);
        Assert.All(fixture.BlockCodes.Values, code => Assert.Equal(ChunkFixtureGenerator.AirBlockCode, code));
    }

    #endregion

    #region Checkerboard Pattern Tests

    [Fact]
    public void Checkerboard_AlternatesBlocksCorrectly()
    {
        var fixture = ChunkFixtureGenerator.Checkerboard("game:rock-granite", "game:air");

        // Test a few specific coordinates
        Assert.Equal("game:rock-granite", ChunkFixtureGenerator.GetBlockCode(fixture, 0, 0, 0)); // 0+0+0=0 (even)
        Assert.Equal("game:air", ChunkFixtureGenerator.GetBlockCode(fixture, 1, 0, 0)); // 1+0+0=1 (odd)
        Assert.Equal("game:air", ChunkFixtureGenerator.GetBlockCode(fixture, 0, 1, 0)); // 0+1+0=1 (odd)
        Assert.Equal("game:rock-granite", ChunkFixtureGenerator.GetBlockCode(fixture, 1, 1, 0)); // 1+1+0=2 (even)
    }

    [Fact]
    public void Checkerboard_HasEqualDistribution()
    {
        var fixture = ChunkFixtureGenerator.Checkerboard("game:rock-granite", "game:air");

        int block1Count = 0;
        int block2Count = 0;

        foreach (var code in fixture.BlockCodes.Values)
        {
            if (code == "game:rock-granite") block1Count++;
            else if (code == "game:air") block2Count++;
        }

        // In a 32x32x32 cube, checkerboard should have exactly half of each
        Assert.Equal(ChunkFixture.BlockCount / 2, block1Count);
        Assert.Equal(ChunkFixture.BlockCount / 2, block2Count);
    }

    [Fact]
    public void Checkerboard_ThrowsOnNullOrEmptyBlockCodes()
    {
        Assert.Throws<ArgumentNullException>(() => ChunkFixtureGenerator.Checkerboard(null!, "game:air"));
        Assert.Throws<ArgumentException>(() => ChunkFixtureGenerator.Checkerboard("game:rock", ""));
    }

    #endregion

    #region Random Pattern Tests

    [Fact]
    public void Random_UsesAllBlockCodes()
    {
        string[] blocks = ["game:rock-granite", "game:air", "game:soil-medium"];
        var fixture = ChunkFixtureGenerator.Random(blocks, seed: 42);

        var usedCodes = fixture.BlockCodes.Values.ToHashSet();

        // With 32768 blocks and 3 options, all should appear
        Assert.Equal(3, usedCodes.Count);
        Assert.Contains("game:rock-granite", usedCodes);
        Assert.Contains("game:air", usedCodes);
        Assert.Contains("game:soil-medium", usedCodes);
    }

    [Fact]
    public void Random_SameSeedProducesSameResult()
    {
        string[] blocks = ["game:rock-granite", "game:air"];

        var fixture1 = ChunkFixtureGenerator.Random(blocks, seed: 12345);
        var fixture2 = ChunkFixtureGenerator.Random(blocks, seed: 12345);

        // Compare a sample of block codes
        for (int i = 0; i < 100; i++)
        {
            Assert.Equal(fixture1.BlockCodes[i], fixture2.BlockCodes[i]);
        }
    }

    [Fact]
    public void Random_DifferentSeedsProduceDifferentResults()
    {
        string[] blocks = ["game:rock-granite", "game:air"];

        var fixture1 = ChunkFixtureGenerator.Random(blocks, seed: 1);
        var fixture2 = ChunkFixtureGenerator.Random(blocks, seed: 2);

        // At least some blocks should differ
        int differences = 0;
        for (int i = 0; i < 100; i++)
        {
            if (fixture1.BlockCodes[i] != fixture2.BlockCodes[i])
                differences++;
        }

        Assert.True(differences > 0, "Different seeds should produce different results");
    }

    [Fact]
    public void Random_ThrowsOnEmptyArray()
    {
        Assert.Throws<ArgumentNullException>(() => ChunkFixtureGenerator.Random(null!, 0));
        Assert.Throws<ArgumentException>(() => ChunkFixtureGenerator.Random([], 0));
    }

    #endregion

    #region Layered Pattern Tests

    [Fact]
    public void Layered_CreatesCorrectLayers()
    {
        var layers = new List<(int y, string blockCode)>
        {
            (0, "game:rock-granite"),
            (8, "game:soil-medium"),
            (16, "game:air")
        };

        var fixture = ChunkFixtureGenerator.Layered(layers);

        // Check bottom layer (Y 0-7)
        Assert.Equal("game:rock-granite", ChunkFixtureGenerator.GetBlockCode(fixture, 0, 0, 0));
        Assert.Equal("game:rock-granite", ChunkFixtureGenerator.GetBlockCode(fixture, 0, 7, 0));

        // Check middle layer (Y 8-15)
        Assert.Equal("game:soil-medium", ChunkFixtureGenerator.GetBlockCode(fixture, 0, 8, 0));
        Assert.Equal("game:soil-medium", ChunkFixtureGenerator.GetBlockCode(fixture, 0, 15, 0));

        // Check top layer (Y 16-31)
        Assert.Equal("game:air", ChunkFixtureGenerator.GetBlockCode(fixture, 0, 16, 0));
        Assert.Equal("game:air", ChunkFixtureGenerator.GetBlockCode(fixture, 0, 31, 0));
    }

    [Fact]
    public void Layered_HandlesUnsortedInput()
    {
        // Provide layers in reverse order
        var layers = new List<(int y, string blockCode)>
        {
            (16, "game:air"),
            (0, "game:rock-granite"),
            (8, "game:soil-medium")
        };

        var fixture = ChunkFixtureGenerator.Layered(layers);

        // Should still produce correct result
        Assert.Equal("game:rock-granite", ChunkFixtureGenerator.GetBlockCode(fixture, 0, 4, 0));
        Assert.Equal("game:soil-medium", ChunkFixtureGenerator.GetBlockCode(fixture, 0, 12, 0));
        Assert.Equal("game:air", ChunkFixtureGenerator.GetBlockCode(fixture, 0, 24, 0));
    }

    [Fact]
    public void Layered_ThrowsOnEmptyList()
    {
        Assert.Throws<ArgumentNullException>(() => ChunkFixtureGenerator.Layered(null!));
        Assert.Throws<ArgumentException>(() => ChunkFixtureGenerator.Layered(new List<(int, string)>()));
    }

    #endregion

    #region FromYMap Tests

    [Fact]
    public void FromYMap_SetsSingleLayerCorrectly()
    {
        var yToBlock = new Dictionary<int, string>
        {
            [5] = "game:rock-granite"
        };

        var fixture = ChunkFixtureGenerator.FromYMap(yToBlock);

        // Y=5 should be granite
        Assert.Equal("game:rock-granite", ChunkFixtureGenerator.GetBlockCode(fixture, 0, 5, 0));
        Assert.Equal("game:rock-granite", ChunkFixtureGenerator.GetBlockCode(fixture, 31, 5, 31));

        // Other Y levels should be air
        Assert.Equal(ChunkFixtureGenerator.AirBlockCode, ChunkFixtureGenerator.GetBlockCode(fixture, 0, 0, 0));
        Assert.Equal(ChunkFixtureGenerator.AirBlockCode, ChunkFixtureGenerator.GetBlockCode(fixture, 0, 10, 0));
    }

    [Fact]
    public void FromYMap_IgnoresOutOfBoundsYLevels()
    {
        var yToBlock = new Dictionary<int, string>
        {
            [-1] = "game:rock-granite",
            [32] = "game:rock-basalt",
            [15] = "game:soil-medium"
        };

        // Should not throw
        var fixture = ChunkFixtureGenerator.FromYMap(yToBlock);

        // Only Y=15 should be set
        Assert.Equal("game:soil-medium", ChunkFixtureGenerator.GetBlockCode(fixture, 0, 15, 0));
    }

    #endregion

    #region IsSolid Tests

    [Fact]
    public void IsSolid_ReturnsFalseForAir()
    {
        var fixture = ChunkFixtureGenerator.Empty();

        Assert.False(ChunkFixtureGenerator.IsSolid(fixture, 0, 0, 0));
        Assert.False(ChunkFixtureGenerator.IsSolid(fixture, 15, 15, 15));
    }

    [Fact]
    public void IsSolid_ReturnsTrueForNonAir()
    {
        var fixture = ChunkFixtureGenerator.Solid("game:rock-granite");

        Assert.True(ChunkFixtureGenerator.IsSolid(fixture, 0, 0, 0));
        Assert.True(ChunkFixtureGenerator.IsSolid(fixture, 31, 31, 31));
    }

    #endregion

    #region ASCII Slice Preview Tests

    [Fact]
    public void RenderAscii_EmptyChunk_AllAirChars()
    {
        var fixture = ChunkFixtureGenerator.Empty();
        var ascii = ChunkSlicePreview.RenderAscii(fixture, y: 0);

        // Should be 32 lines of 32 dots
        var lines = ascii.Split(Environment.NewLine);
        Assert.Equal(32, lines.Length);
        Assert.All(lines, line =>
        {
            Assert.Equal(32, line.Length);
            Assert.All(line, c => Assert.Equal('.', c));
        });
    }

    [Fact]
    public void RenderAscii_SolidChunk_AllSolidChars()
    {
        var fixture = ChunkFixtureGenerator.Solid("game:rock-granite");
        var ascii = ChunkSlicePreview.RenderAscii(fixture, y: 0);

        var lines = ascii.Split(Environment.NewLine);
        Assert.Equal(32, lines.Length);
        Assert.All(lines, line =>
        {
            Assert.Equal(32, line.Length);
            Assert.All(line, c => Assert.Equal('#', c));
        });
    }

    [Fact]
    public void RenderAscii_CheckerboardSlice_HasPattern()
    {
        var fixture = ChunkFixtureGenerator.Checkerboard("game:rock-granite", "game:air");
        var ascii = ChunkSlicePreview.RenderAscii(fixture, y: 0);

        // At y=0, checkerboard should alternate based on x+z
        // First row (z=0): x=0 -> even (#), x=1 -> odd (.), etc.
        var lines = ascii.Split(Environment.NewLine);
        Assert.Equal('#', lines[0][0]); // x=0, z=0, y=0 -> 0 (even)
        Assert.Equal('.', lines[0][1]); // x=1, z=0, y=0 -> 1 (odd)
        Assert.Equal('.', lines[1][0]); // x=0, z=1, y=0 -> 1 (odd)
        Assert.Equal('#', lines[1][1]); // x=1, z=1, y=0 -> 2 (even)
    }

    [Fact]
    public void RenderAscii_CustomChars_UsesProvidedChars()
    {
        var fixture = ChunkFixtureGenerator.Solid("game:rock-granite");
        var ascii = ChunkSlicePreview.RenderAscii(fixture, y: 0, solidChar: 'X', airChar: 'O');

        Assert.Contains('X', ascii);
        Assert.DoesNotContain('#', ascii);
    }

    [Fact]
    public void RenderAscii_ThrowsOnInvalidYLevel()
    {
        var fixture = ChunkFixtureGenerator.Empty();

        Assert.Throws<ArgumentOutOfRangeException>(() => ChunkSlicePreview.RenderAscii(fixture, y: -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => ChunkSlicePreview.RenderAscii(fixture, y: 32));
    }

    [Fact]
    public void RenderAsciiSlice_XYAxis_RendersVerticalSlice()
    {
        // Create a layer at Y=0
        var fixture = ChunkFixtureGenerator.FromYMap(new Dictionary<int, string> { [0] = "game:rock-granite" });
        var ascii = ChunkSlicePreview.RenderAsciiSlice(fixture, ChunkSliceAxis.XY, sliceIndex: 0);

        var lines = ascii.Split(Environment.NewLine);
        Assert.Equal(32, lines.Length);

        // Last line (Y=0) should be all solid
        Assert.All(lines[31], c => Assert.Equal('#', c));

        // First line (Y=31) should be all air
        Assert.All(lines[0], c => Assert.Equal('.', c));
    }

    #endregion

    #region PNG Slice Preview Tests

    [Fact]
    public void RenderPng_ReturnsCorrectByteCount()
    {
        var fixture = ChunkFixtureGenerator.Empty();
        var palette = ChunkSlicePreview.CreateDefaultPalette();

        var pixels = ChunkSlicePreview.RenderPng(fixture, y: 0, palette);

        // 32 x 32 x 4 (RGBA) = 4096 bytes
        Assert.Equal(4096, pixels.Length);
    }

    [Fact]
    public void RenderPng_EmptyChunk_AllTransparent()
    {
        var fixture = ChunkFixtureGenerator.Empty();
        var palette = ChunkSlicePreview.CreateDefaultPalette();

        var pixels = ChunkSlicePreview.RenderPng(fixture, y: 0, palette);

        // All alpha values should be 0 (transparent)
        for (int i = 3; i < pixels.Length; i += 4)
        {
            Assert.Equal(0, pixels[i]);
        }
    }

    [Fact]
    public void RenderPng_SolidChunk_AllOpaque()
    {
        var fixture = ChunkFixtureGenerator.Solid("game:rock-granite");
        var palette = ChunkSlicePreview.CreateDefaultPalette();

        var pixels = ChunkSlicePreview.RenderPng(fixture, y: 0, palette);

        // All alpha values should be 255 (opaque)
        for (int i = 3; i < pixels.Length; i += 4)
        {
            Assert.Equal(255, pixels[i]);
        }
    }

    [Fact]
    public void RenderPng_UsesCorrectColorFromPalette()
    {
        var fixture = ChunkFixtureGenerator.Solid("game:rock-granite");
        var palette = new Dictionary<string, (byte r, byte g, byte b)>
        {
            ["game:rock-granite"] = (100, 150, 200)
        };

        var pixels = ChunkSlicePreview.RenderPng(fixture, y: 0, palette);

        // First pixel should be our color
        Assert.Equal(100, pixels[0]); // R
        Assert.Equal(150, pixels[1]); // G
        Assert.Equal(200, pixels[2]); // B
        Assert.Equal(255, pixels[3]); // A
    }

    [Fact]
    public void RenderPng_UnknownBlock_UsesMagenta()
    {
        var fixture = ChunkFixtureGenerator.Solid("game:unknown-block");
        var palette = new Dictionary<string, (byte r, byte g, byte b)>(); // Empty palette

        var pixels = ChunkSlicePreview.RenderPng(fixture, y: 0, palette);

        // Should use magenta (255, 0, 255)
        Assert.Equal(255, pixels[0]); // R
        Assert.Equal(0, pixels[1]);   // G
        Assert.Equal(255, pixels[2]); // B
        Assert.Equal(255, pixels[3]); // A
    }

    [Fact]
    public void RenderPng_ThrowsOnInvalidYLevel()
    {
        var fixture = ChunkFixtureGenerator.Empty();
        var palette = ChunkSlicePreview.CreateDefaultPalette();

        Assert.Throws<ArgumentOutOfRangeException>(() => ChunkSlicePreview.RenderPng(fixture, y: -1, palette));
        Assert.Throws<ArgumentOutOfRangeException>(() => ChunkSlicePreview.RenderPng(fixture, y: 32, palette));
    }

    #endregion

    #region Color Counting Tests

    [Fact]
    public void CountColors_CountsCorrectly()
    {
        var fixture = ChunkFixtureGenerator.Checkerboard("game:rock-granite", "game:air");
        var palette = new Dictionary<string, (byte r, byte g, byte b)>
        {
            ["game:rock-granite"] = (128, 128, 128),
            ["game:air"] = (0, 0, 0)
        };

        var pixels = ChunkSlicePreview.RenderPng(fixture, y: 0, palette);
        var counts = ChunkSlicePreview.CountColors(pixels);

        // At y=0, checkerboard on XZ plane should have 512 of each color
        // (16x32 of each in alternating pattern)
        Assert.Equal(512, counts[(128, 128, 128)]); // Granite (solid)
        Assert.Equal(512, counts[(0, 0, 0)]); // Air (transparent)
    }

    [Fact]
    public void CountSolidPixels_CountsNonTransparent()
    {
        var fixture = ChunkFixtureGenerator.Checkerboard("game:rock-granite", "game:air");
        var palette = ChunkSlicePreview.CreateDefaultPalette();

        var pixels = ChunkSlicePreview.RenderPng(fixture, y: 0, palette);
        int solidCount = ChunkSlicePreview.CountSolidPixels(pixels);

        // Half should be solid
        Assert.Equal(512, solidCount);
    }

    [Fact]
    public void CountSolidChars_CountsSolidCharacters()
    {
        var fixture = ChunkFixtureGenerator.Checkerboard("game:rock-granite", "game:air");
        var ascii = ChunkSlicePreview.RenderAscii(fixture, y: 0);

        int count = ChunkSlicePreview.CountSolidChars(ascii);

        // Half of 32x32 = 512
        Assert.Equal(512, count);
    }

    [Fact]
    public void CountColors_ThrowsOnInvalidPixelArray()
    {
        // Not a multiple of 4
        Assert.Throws<ArgumentException>(() => ChunkSlicePreview.CountColors(new byte[5]));
        Assert.Throws<ArgumentNullException>(() => ChunkSlicePreview.CountColors(null!));
    }

    #endregion

    #region Default Palette Tests

    [Fact]
    public void CreateDefaultPalette_ContainsCommonBlocks()
    {
        var palette = ChunkSlicePreview.CreateDefaultPalette();

        Assert.True(palette.ContainsKey("game:air"));
        Assert.True(palette.ContainsKey("game:rock-granite"));
        Assert.True(palette.ContainsKey("game:soil-medium"));
        Assert.True(palette.ContainsKey("game:water-still-7"));
    }

    [Fact]
    public void CreateDefaultPalette_IsCaseInsensitive()
    {
        var palette = ChunkSlicePreview.CreateDefaultPalette();

        Assert.True(palette.ContainsKey("GAME:AIR"));
        Assert.True(palette.ContainsKey("Game:Rock-Granite"));
    }

    #endregion

    #region Slice Axis Tests

    [Fact]
    public void ChunkSliceAxis_HasAllExpectedValues()
    {
        var values = Enum.GetValues<ChunkSliceAxis>();

        Assert.Equal(3, values.Length);
        Assert.Contains(ChunkSliceAxis.XZ, values);
        Assert.Contains(ChunkSliceAxis.XY, values);
        Assert.Contains(ChunkSliceAxis.YZ, values);
    }

    [Theory]
    [InlineData(ChunkSliceAxis.XZ)]
    [InlineData(ChunkSliceAxis.XY)]
    [InlineData(ChunkSliceAxis.YZ)]
    public void RenderAsciiSlice_AllAxes_Produce32x32Output(ChunkSliceAxis axis)
    {
        var fixture = ChunkFixtureGenerator.Solid("game:rock-granite");
        var ascii = ChunkSlicePreview.RenderAsciiSlice(fixture, axis, sliceIndex: 16);

        var lines = ascii.Split(Environment.NewLine);
        Assert.Equal(32, lines.Length);
        Assert.All(lines, line => Assert.Equal(32, line.Length));
    }

    [Theory]
    [InlineData(ChunkSliceAxis.XZ)]
    [InlineData(ChunkSliceAxis.XY)]
    [InlineData(ChunkSliceAxis.YZ)]
    public void RenderPngSlice_AllAxes_Produce4096Bytes(ChunkSliceAxis axis)
    {
        var fixture = ChunkFixtureGenerator.Solid("game:rock-granite");
        var palette = ChunkSlicePreview.CreateDefaultPalette();

        var pixels = ChunkSlicePreview.RenderPngSlice(fixture, axis, sliceIndex: 16, palette);

        Assert.Equal(4096, pixels.Length);
    }

    #endregion
}
