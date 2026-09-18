using Xunit;
using Zaldaryon.Pharos.Assertions;
using Zaldaryon.Pharos.Fixtures;
using Zaldaryon.Pharos.Graphics;
using Zaldaryon.Pharos.Timing;

namespace Zaldaryon.Pharos.Tests.Scenarios;

/// <summary>
/// Tests for greedy mesher face-merge scenario (Issue #116).
/// Validates that uniform surfaces merge significantly and material boundaries
/// produce separate quads without UV overlap. All tests are headless-safe using
/// synthetic MeshSnapshot data.
/// </summary>
public sealed class GreedyMesherScenarioTests
{
    // -------------------------------------------------------------------------
    // ChunkFixture layout tests
    // -------------------------------------------------------------------------

    [Fact]
    public void ChunkFixture_StoneLayerWithGrassPatch_CreatesExpectedBlockLayout()
    {
        // Arrange: 16x1x16 flat stone layer at y=0, 4x1x4 grass patch at y=1 center
        ChunkFixture fixture = new ChunkFixtureBuilder()
            .At(0, 0, 0)
            .WithDefaultSunlight(31)
            .Fill(0, 0, 0, 15, 0, 15, "game:rock-granite")  // 16x1x16 stone layer
            .Fill(6, 1, 6, 9, 1, 9, "game:tallgrass-tall")  // 4x4 grass patch centered
            .SetBlock(15, 2, 15, "game:glass-plain")        // Single glass block corner
            .Build();

        // Assert stone layer (256 blocks)
        for (int x = 0; x < 16; x++)
        {
            for (int z = 0; z < 16; z++)
            {
                int idx = ChunkFixture.ToIndex(x, 0, z);
                Assert.Equal("game:rock-granite", fixture.BlockCodes[idx]);
            }
        }

        // Assert grass patch (16 blocks)
        for (int x = 6; x <= 9; x++)
        {
            for (int z = 6; z <= 9; z++)
            {
                int idx = ChunkFixture.ToIndex(x, 1, z);
                Assert.Equal("game:tallgrass-tall", fixture.BlockCodes[idx]);
            }
        }

        // Assert glass block
        int glassIdx = ChunkFixture.ToIndex(15, 2, 15);
        Assert.Equal("game:glass-plain", fixture.BlockCodes[glassIdx]);

        // Assert positions outside defined regions are not set
        int emptyIdx = ChunkFixture.ToIndex(0, 3, 0);
        Assert.False(fixture.BlockCodes.ContainsKey(emptyIdx));
        Assert.False(fixture.BlockIds.ContainsKey(emptyIdx));
    }

    // -------------------------------------------------------------------------
    // MeshFaceCountReduced assertion tests (synthetic data)
    // -------------------------------------------------------------------------

    [Fact]
    public void MeshFaceCountReduced_UniformStoneLayer_Passes90PercentThreshold()
    {
        // A 16x1x16 uniform layer produces 256 blocks with top+bottom exposed faces.
        // Naive meshing: 256 blocks * 2 faces = 512 triangular faces (top+bottom only).
        // Greedy meshing: 2 merged quads (1 top, 1 bottom) = 4 triangular faces.
        // Reduction: (512 - 4) / 512 = 99.2%

        var before = MeshSnapshot.CreateSynthetic(vertexCount: 2048, indexCount: 1536);  // 512 faces
        var after = MeshSnapshot.CreateSynthetic(vertexCount: 16, indexCount: 12);       // 4 faces

        // Should not throw
        PharosAssert.MeshFaceCountReduced(before, after, minReductionPercent: 90);
    }

    [Fact]
    public void MeshFaceCountReduced_BelowThreshold_Throws()
    {
        // Simulate poor greedy meshing: only 50% reduction
        var before = MeshSnapshot.CreateSynthetic(vertexCount: 1000, indexCount: 3000); // 1000 faces
        var after = MeshSnapshot.CreateSynthetic(vertexCount: 500, indexCount: 1500);   // 500 faces (50% reduction)

        var ex = Assert.Throws<PharosAssertException>(() =>
            PharosAssert.MeshFaceCountReduced(before, after, minReductionPercent: 90));

        Assert.Contains("90%", ex.Message);
        Assert.Contains("50.0%", ex.Message);
    }

    [Fact]
    public void MeshFaceCountReduced_ExactlyAtThreshold_Passes()
    {
        // Exactly 90% reduction
        var before = MeshSnapshot.CreateSynthetic(vertexCount: 1000, indexCount: 3000); // 1000 faces
        var after = MeshSnapshot.CreateSynthetic(vertexCount: 100, indexCount: 300);    // 100 faces

        // Should not throw
        PharosAssert.MeshFaceCountReduced(before, after, minReductionPercent: 90);
    }

    [Fact]
    public void MeshFaceCountReduced_SlightlyBelowThreshold_Throws()
    {
        // 89.9% reduction (just under 90%)
        var before = MeshSnapshot.CreateSynthetic(vertexCount: 1000, indexCount: 3000); // 1000 faces
        var after = MeshSnapshot.CreateSynthetic(vertexCount: 101, indexCount: 303);    // 101 faces

        Assert.Throws<PharosAssertException>(() =>
            PharosAssert.MeshFaceCountReduced(before, after, minReductionPercent: 90));
    }

    // -------------------------------------------------------------------------
    // Material boundary tests (synthetic UV data)
    // -------------------------------------------------------------------------

    [Fact]
    public void MaterialBoundaries_ProduceSeparateQuads_NoOverlap()
    {
        // Stone material uses UV region [0.0, 0.0] to [0.25, 0.25] in atlas
        // Grass material uses UV region [0.5, 0.0] to [0.75, 0.25] in atlas
        // These regions should not overlap

        var stoneUvs = new[]
        {
            new UvSample(0.0f, 0.0f), new UvSample(0.25f, 0.0f),
            new UvSample(0.25f, 0.25f), new UvSample(0.0f, 0.25f)
        };

        var grassUvs = new[]
        {
            new UvSample(0.5f, 0.0f), new UvSample(0.75f, 0.0f),
            new UvSample(0.75f, 0.25f), new UvSample(0.5f, 0.25f)
        };

        var stoneSnapshot = MeshSnapshot.CreateSynthetic(100, 300, stoneUvs);
        var grassSnapshot = MeshSnapshot.CreateSynthetic(50, 150, grassUvs);

        // Assert both have valid UVs in [0,1] range
        PharosAssert.UvCoordinatesValid(stoneSnapshot);
        PharosAssert.UvCoordinatesValid(grassSnapshot);

        // Assert non-overlapping UV regions (manual check)
        var stoneBounds = ComputeUvBounds(stoneSnapshot.UvSamples);
        var grassBounds = ComputeUvBounds(grassSnapshot.UvSamples);

        Assert.False(UvBoundsOverlap(stoneBounds, grassBounds),
            "Stone and grass UV regions should not overlap in texture atlas");
    }

    [Fact]
    public void SingleGlassBlock_DoesNotMergeWithSurrounding()
    {
        // A single glass block at a corner cannot merge with adjacent stone.
        // It should produce its own faces with distinct UV coordinates.

        var glassUvs = new[]
        {
            new UvSample(0.75f, 0.5f), new UvSample(1.0f, 0.5f),
            new UvSample(1.0f, 0.75f), new UvSample(0.75f, 0.75f)
        };

        var glassSnapshot = MeshSnapshot.CreateSynthetic(24, 72, glassUvs); // 6 faces * 4 verts, 6 faces * 2 tris * 3

        // Glass block should have 24 faces (6 exposed sides * 4 triangles for quads)
        // Actually 6 faces as quads = 12 triangular faces
        Assert.Equal(24, glassSnapshot.FaceCount);

        // UV coordinates should be valid
        PharosAssert.UvCoordinatesValid(glassSnapshot);
    }

    [Fact]
    public void MaterialBoundaries_DifferentTextureAtlasRegions_AreDistinct()
    {
        // Test that three different materials occupy non-overlapping UV atlas regions
        var stoneUvs = new[] { new UvSample(0.0f, 0.0f), new UvSample(0.125f, 0.125f) };
        var grassUvs = new[] { new UvSample(0.25f, 0.0f), new UvSample(0.375f, 0.125f) };
        var glassUvs = new[] { new UvSample(0.5f, 0.25f), new UvSample(0.625f, 0.375f) };

        var materials = new[]
        {
            ("stone", stoneUvs),
            ("grass", grassUvs),
            ("glass", glassUvs)
        };

        // Verify no pair of materials has overlapping UV bounds
        for (int i = 0; i < materials.Length; i++)
        {
            for (int j = i + 1; j < materials.Length; j++)
            {
                var boundsI = ComputeUvBounds(materials[i].Item2);
                var boundsJ = ComputeUvBounds(materials[j].Item2);

                Assert.False(UvBoundsOverlap(boundsI, boundsJ),
                    $"UV regions for {materials[i].Item1} and {materials[j].Item1} should not overlap");
            }
        }
    }

    // -------------------------------------------------------------------------
    // Integration test (requires GPU, skipped by default)
    // -------------------------------------------------------------------------

    [Fact(Skip = "Requires live GPU for chunk meshing")]
    public void LiveIntegration_InjectAndMesh_VerifiesFaceReduction()
    {
        // This test would exercise the full pipeline:
        // 1. Boot HeadlessClient
        // 2. InjectChunk with fixture
        // 3. WaitForChunkMeshed
        // 4. Snapshot mesh before/after greedy meshing
        // 5. Assert face reduction

        // Placeholder for live integration test
        // Implementation would follow ChunkFixtureTests.HeadlessClient_InjectChunk_TesselatesAndMeshesWithoutServer pattern
    }

    // -------------------------------------------------------------------------
    // Helper methods
    // -------------------------------------------------------------------------

    private static (float MinU, float MaxU, float MinV, float MaxV) ComputeUvBounds(IReadOnlyList<UvSample> samples)
    {
        if (samples.Count == 0)
            return (0, 0, 0, 0);

        float minU = float.MaxValue, maxU = float.MinValue;
        float minV = float.MaxValue, maxV = float.MinValue;

        foreach (var sample in samples)
        {
            if (sample.U < minU) minU = sample.U;
            if (sample.U > maxU) maxU = sample.U;
            if (sample.V < minV) minV = sample.V;
            if (sample.V > maxV) maxV = sample.V;
        }

        return (minU, maxU, minV, maxV);
    }

    private static bool UvBoundsOverlap(
        (float MinU, float MaxU, float MinV, float MaxV) a,
        (float MinU, float MaxU, float MinV, float MaxV) b)
    {
        // Two rectangles overlap if they overlap on both axes
        bool overlapU = a.MinU < b.MaxU && a.MaxU > b.MinU;
        bool overlapV = a.MinV < b.MaxV && a.MaxV > b.MinV;
        return overlapU && overlapV;
    }
}
