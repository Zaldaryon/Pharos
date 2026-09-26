using System;
using System.IO;
using System.Threading.Tasks;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.Client.NoObf;
using Xunit;
using Zaldaryon.Pharos.Bootstrap;
using Zaldaryon.Pharos.Core;
using Zaldaryon.Pharos.Fixtures;
using Zaldaryon.Pharos.Platform;
using Zaldaryon.Pharos.Timing;

namespace Zaldaryon.Pharos.Tests;

[Collection("Sequential")]
public sealed class ChunkFixtureTests
{
    static ChunkFixtureTests()
    {
        HeadlessPlatformResolver.Initialize();
    }
    [Fact]
    public void ChunkFixture_CoordinateIndexing_RoundtripsSuccessfully()
    {
        for (int x = 0; x < 32; x += 7)
        {
            for (int y = 0; y < 32; y += 7)
            {
                for (int z = 0; z < 32; z += 7)
                {
                    int index = ChunkFixture.ToIndex(x, y, z);
                    var (rx, ry, rz) = ChunkFixture.FromIndex(index);
                    Assert.Equal(x, rx);
                    Assert.Equal(y, ry);
                    Assert.Equal(z, rz);
                }
            }
        }
    }

    [Fact]
    public void ChunkFixture_InvalidCoordinates_ThrowsArgumentOutOfRangeException()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ChunkFixture.ToIndex(-1, 0, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => ChunkFixture.ToIndex(0, 32, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => ChunkFixture.ToIndex(0, 0, -5));
        Assert.Throws<ArgumentOutOfRangeException>(() => ChunkFixture.FromIndex(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => ChunkFixture.FromIndex(32768));
    }

    [Fact]
    public void ChunkFixtureBuilder_ConstructsExpectedFixtureState()
    {
        ChunkPos pos = new(2, 4, 6);
        ChunkFixture fixture = new ChunkFixtureBuilder()
            .At(pos)
            .WithDefaultSunlight(28)
            .SetBlock(0, 0, 0, "game:rock-granite")
            .SetBlock(1, 2, 3, 42)
            .Fill(5, 5, 5, 7, 7, 7, "game:dirt")
            .SetSunlight(1, 2, 3, 15)
            .SetBlocklight(1, 2, 3, 10)
            .Build();

        Assert.Equal(pos, fixture.Position);
        Assert.Equal(28, fixture.DefaultSunlight);

        int idx0 = ChunkFixture.ToIndex(0, 0, 0);
        Assert.Equal("game:rock-granite", fixture.BlockCodes[idx0]);

        int idx1 = ChunkFixture.ToIndex(1, 2, 3);
        Assert.Equal(42, fixture.BlockIds[idx1]);
        Assert.Equal((byte)15, fixture.CustomSunlight[idx1]);
        Assert.Equal((byte)10, fixture.CustomBlocklight[idx1]);

        // 3x3x3 fill = 27 blocks
        for (int x = 5; x <= 7; x++)
        {
            for (int y = 5; y <= 7; y++)
            {
                for (int z = 5; z <= 7; z++)
                {
                    int fillIdx = ChunkFixture.ToIndex(x, y, z);
                    Assert.Equal("game:dirt", fixture.BlockCodes[fillIdx]);
                }
            }
        }
    }

    [Fact]
    public async Task ChunkFixtureSerializer_RoundtripsJsonAndFileCorrectly()
    {
        ChunkPos pos = new(3, 1, 5);
        ChunkFixture original = new ChunkFixtureBuilder()
            .At(pos)
            .WithDefaultSunlight(31)
            .SetBlock(10, 10, 10, "game:torch-basic")
            .SetBlock(11, 11, 11, 99)
            .SetSunlight(10, 10, 10, 20)
            .SetBlocklight(10, 10, 10, 14)
            .Fill(0, 0, 0, 31, 0, 31, "game:stone")
            .Build();

        string json = ChunkFixtureSerializer.ToJson(original);
        Assert.Contains("\"chunkX\": 3", json);
        Assert.Contains("\"chunkY\": 1", json);
        Assert.Contains("\"chunkZ\": 5", json);
        Assert.Contains("game:torch-basic", json);

        ChunkFixture restored = ChunkFixtureSerializer.FromJson(json);
        Assert.Equal(original.Position, restored.Position);
        Assert.Equal(original.DefaultSunlight, restored.DefaultSunlight);

        int torchIdx = ChunkFixture.ToIndex(10, 10, 10);
        Assert.Equal("game:torch-basic", restored.BlockCodes[torchIdx]);
        Assert.Equal((byte)20, restored.CustomSunlight[torchIdx]);
        Assert.Equal((byte)14, restored.CustomBlocklight[torchIdx]);

        int idIdx = ChunkFixture.ToIndex(11, 11, 11);
        Assert.Equal(99, restored.BlockIds[idIdx]);

        // Test File IO
        string tempFile = Path.Combine(Path.GetTempPath(), $"pharos_fixture_{Guid.NewGuid():N}.json");
        try
        {
            await ChunkFixtureSerializer.SaveToFileAsync(original, tempFile);
            Assert.True(File.Exists(tempFile));

            ChunkFixture fromFile = await ChunkFixtureSerializer.LoadFromFileAsync(tempFile);
            Assert.Equal(original.Position, fromFile.Position);
            Assert.Equal("game:torch-basic", fromFile.BlockCodes[torchIdx]);
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
        }
    }

    [Fact]
    public void HeadlessClient_InitializeMockWorld_PreparesWorldMapWithoutServer()
    {
        HeadlessClientOptions options = new()
        {
            Width = 1280,
            Height = 720,
            DisableAudio = true
        };

        using HeadlessClient client = HeadlessClientBootstrap.Boot(options);

        client.InitializeMockWorld(new Vec3i(512, 256, 512), defaultSunlight: 30);

        Assert.Equal(512, client.Client.WorldMap.MapSizeX);
        Assert.Equal(256, client.Client.WorldMap.MapSizeY);
        Assert.Equal(512, client.Client.WorldMap.MapSizeZ);
        Assert.Equal(30, client.Client.WorldMap.SunBrightness);
        var emptyField = typeof(ClientWorldMap).GetField("EmptyChunk", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
        Assert.NotNull(emptyField?.GetValue(client.Client.WorldMap));
        Assert.NotNull(client.Client.WorldMap.BlockLightLevels);
        Assert.NotNull(client.Client.WorldMap.SunLightLevels);
    }

    [Fact]
    public async Task HeadlessClient_InjectChunk_TesselatesAndMeshesWithoutServer()
    {
        HeadlessClientOptions options = new()
        {
            Width = 1280,
            Height = 720,
            DisableAudio = true
        };

        using HeadlessClient client = HeadlessClientBootstrap.Boot(options);

        ChunkPos targetChunk = new(10, 2, 10);
        ChunkFixture fixture = new ChunkFixtureBuilder()
            .At(targetChunk)
            .WithDefaultSunlight(31)
            .Fill(0, 0, 0, 31, 1, 31, 1) // Layer of solid blocks with ID 1
            .SetBlock(16, 2, 16, 2)
            .Build();

        ClientChunk injectedChunk = client.InjectChunk(fixture, triggerTesselation: true);
        Assert.NotNull(injectedChunk);
        Assert.False(injectedChunk.Empty);

        IWorldChunk? retrieved = client.Client.WorldMap.GetChunk(targetChunk.X, targetChunk.Y, targetChunk.Z);
        Assert.Same(injectedChunk, retrieved);

        int checkIdx = ChunkFixture.ToIndex(16, 2, 16);
        Assert.Equal(2, injectedChunk.Data[checkIdx]);

        // Await chunk meshing through deterministic frame steps
        bool meshed = await client.WaitForChunkMeshed(targetChunk, maxFrames: 10, dt: 1f / 60f);
        Assert.True(meshed);

        Assert.True(client.FrameController.IsChunkMeshed(targetChunk));
    }

    /// <summary>
    /// Fixture mode populates a chunk with no live server, and does it with bounded work.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This test used to assert a 500ms wall-clock budget, which no run could satisfy
    /// repeatably: it measures a one-time engine bootstrap, so its result tracks machine load
    /// rather than the code. It failed on an idle box and on a loaded one, and it failed
    /// identically on an unmodified checkout. Its own comment claimed "well under 100
    /// milliseconds" while the assertion allowed 500.
    /// </para>
    /// <para>
    /// Performance regression is measured by the benchmark suite against the baselines in
    /// <c>pharos-baselines.json</c> (ROADMAP #61), not by a wall clock inside a unit test.
    /// Comparing against a real server boot would be load-insensitive, but it would cost
    /// roughly 12 seconds per run and would contradict the point of this class, whose other
    /// tests all assert that fixture mode needs no server. What is asserted here instead is
    /// the part that is deterministic: the fixture lands intact, and the per-chunk work stays
    /// proportional to the ids the fixture references.
    /// </para>
    /// </remarks>
    [Fact]
    public void FixtureMode_PopulatesChunkWithoutBootingAServer()
    {
        HeadlessClientOptions options = new()
        {
            Width = 1280,
            Height = 720,
            DisableAudio = true
        };

        using HeadlessClient client = HeadlessClientBootstrap.Boot(options);

        client.InitializeMockWorld();

        ChunkFixture fixture = new ChunkFixtureBuilder()
            .At(5, 1, 5)
            .Fill(0, 0, 0, 31, 2, 31, 1)
            .Build();

        ClientChunk injected = client.InjectChunk(fixture, triggerTesselation: false);

        // The slab must read back in full. A palette trimmed to the wrong BlockList.Count
        // rewrites every id at or above that count to air, which erases the fixture silently
        // and leaves the chunk unmeshable.
        int filled = 0;
        for (int y = 0; y < ChunkFixture.ChunkSize; y++)
        {
            for (int z = 0; z < ChunkFixture.ChunkSize; z++)
            {
                for (int x = 0; x < ChunkFixture.ChunkSize; x++)
                {
                    byte expected = y <= 2 ? (byte)1 : (byte)0;
                    Assert.Equal(expected, injected.Data[ChunkFixture.ToIndex(x, y, z)]);
                    if (expected != 0) filled++;
                }
            }
        }

        Assert.Equal(ChunkFixture.ChunkSize * 3 * ChunkFixture.ChunkSize, filled);

        // FixtureBlockRegistry sizes the block list to the ids the fixture references, so
        // that ClearPaletteOutsideMaxValue keeps the injected blocks. Sizing it to the
        // 10,000-slot BlockList capacity instead would allocate thousands of int[7] subid
        // arrays on a path that runs once per injected chunk, with no timing test able to
        // notice. Bounding the allocation is deterministic; timing it is not.
        Assert.Equal(2, client.Client.Blocks.Count);
        int[][]? subids = client.Client.FastBlockTextureSubidsByBlockAndFace;
        Assert.NotNull(subids);
        Assert.True(
            subids.Length <= 64,
            $"subid array grew to {subids.Length} slots for a fixture that references 2 block ids");
        Assert.NotNull(subids[0]);
        Assert.NotNull(subids[1]);
    }

    [Fact]
    public void ChunkFixtureSerializer_DeserializesBoxesCorrectly()
    {
        string json = """
        {
          "chunkX": 4,
          "chunkY": 2,
          "chunkZ": 8,
          "defaultSunlight": 25,
          "boxes": [
            {
              "minX": 0,
              "minY": 0,
              "minZ": 0,
              "maxX": 2,
              "maxY": 2,
              "maxZ": 2,
              "id": 42
            },
            {
              "minX": 5,
              "minY": 5,
              "minZ": 5,
              "maxX": 6,
              "maxY": 6,
              "maxZ": 6,
              "code": "game:gravel"
            }
          ]
        }
        """;

        ChunkFixture fixture = ChunkFixtureSerializer.FromJson(json);
        Assert.Equal(new ChunkPos(4, 2, 8), fixture.Position);
        Assert.Equal(25, fixture.DefaultSunlight);

        int idx0 = ChunkFixture.ToIndex(0, 0, 0);
        int idx2 = ChunkFixture.ToIndex(2, 2, 2);
        Assert.Equal(42, fixture.BlockIds[idx0]);
        Assert.Equal(42, fixture.BlockIds[idx2]);

        int idxGravel = ChunkFixture.ToIndex(5, 5, 5);
        Assert.Equal("game:gravel", fixture.BlockCodes[idxGravel]);
    }

    [Fact]
    public void HeadlessClient_InjectChunks_InjectsMultipleAndPreservesMetadata()
    {
        HeadlessClientOptions options = new()
        {
            Width = 1280,
            Height = 720,
            DisableAudio = true
        };

        using HeadlessClient client = HeadlessClientBootstrap.Boot(options);

        client.InitializeMockWorld();

        ChunkFixture f1 = new ChunkFixtureBuilder()
            .At(1, 0, 1)
            .SetBlock(0, 0, 0, 10)
            .Build();

        ChunkFixture f2 = new ChunkFixtureBuilder()
            .At(2, 0, 2)
            .SetBlock(1, 1, 1, 20)
            .Build();

        var chunks = client.InjectChunks(new[] { f1, f2 }, triggerTesselation: false);

        Assert.Equal(2, chunks.Count);
        Assert.NotNull(client.Client.WorldMap.GetChunk(1, 0, 1));
        Assert.NotNull(client.Client.WorldMap.GetChunk(2, 0, 2));

        Assert.NotNull(chunks[0].LightPositions);
        Assert.NotNull(chunks[1].LightPositions);
    }

    [Fact]
    public void HeadlessClient_InjectChunk_OverwriteReplacesExistingCleanly()
    {
        HeadlessClientOptions options = new()
        {
            Width = 1280,
            Height = 720,
            DisableAudio = true
        };

        using HeadlessClient client = HeadlessClientBootstrap.Boot(options);

        client.InitializeMockWorld();

        ChunkPos target = new(3, 1, 3);
        ChunkFixture first = new ChunkFixtureBuilder()
            .At(target)
            .SetBlock(0, 0, 0, 1)
            .Build();

        ClientChunk chunk1 = client.InjectChunk(first, triggerTesselation: false);
        Assert.Same(chunk1, client.Client.WorldMap.GetChunk(target.X, target.Y, target.Z));

        ChunkFixture second = new ChunkFixtureBuilder()
            .At(target)
            .SetBlock(0, 0, 0, 2)
            .Build();

        ClientChunk chunk2 = client.InjectChunk(second, triggerTesselation: false);
        Assert.Same(chunk2, client.Client.WorldMap.GetChunk(target.X, target.Y, target.Z));
        Assert.NotSame(chunk1, chunk2);
        Assert.Equal(2, chunk2.Data[ChunkFixture.ToIndex(0, 0, 0)]);
    }
}
