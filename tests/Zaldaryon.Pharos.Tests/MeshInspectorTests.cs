using Xunit;
using Zaldaryon.Pharos.Assertions;
using Zaldaryon.Pharos.Graphics;

namespace Zaldaryon.Pharos.Tests;

/// <summary>
/// Unit tests for MeshSnapshot, MeshInspector, ChiseledBlockLodInspector,
/// and the mesh-related PharosAssert methods.
/// All tests are headless-safe using synthetic MeshSnapshot data.
/// </summary>
public sealed class MeshInspectorTests
{
    // -------------------------------------------------------------------------
    // MeshSnapshot tests
    // -------------------------------------------------------------------------

    [Fact]
    public void MeshSnapshot_Empty_IsNotValid()
    {
        Assert.False(MeshSnapshot.Empty.IsValid);
    }

    [Fact]
    public void MeshSnapshot_CreateSynthetic_ComputesFaceAndQuadCount()
    {
        var snapshot = MeshSnapshot.CreateSynthetic(vertexCount: 100, indexCount: 300);

        Assert.True(snapshot.IsValid);
        Assert.Equal(100, snapshot.VertexCount);
        Assert.Equal(300, snapshot.IndexCount);
        Assert.Equal(100, snapshot.FaceCount);  // 300 / 3
        Assert.Equal(50, snapshot.QuadCount);   // 100 / 2
    }

    [Fact]
    public void MeshSnapshot_CreateSynthetic_WithUvSamples()
    {
        var uvs = new[] { new UvSample(0.5f, 0.5f), new UvSample(0.0f, 1.0f) };
        var snapshot = MeshSnapshot.CreateSynthetic(100, 300, uvs);

        Assert.Equal(2, snapshot.UvSamples.Count);
        Assert.Equal(0.5f, snapshot.UvSamples[0].U);
    }

    [Fact]
    public void MeshSnapshot_CreateSynthetic_ZeroIndexCount_UseVertexCount()
    {
        var snapshot = MeshSnapshot.CreateSynthetic(vertexCount: 90, indexCount: 0);

        Assert.Equal(30, snapshot.FaceCount);  // 90 / 3
        Assert.Equal(15, snapshot.QuadCount);  // 30 / 2
    }

    // -------------------------------------------------------------------------
    // ChiseledBlockLodInspector tests
    // -------------------------------------------------------------------------

    [Theory]
    [InlineData(0f, 0)]
    [InlineData(16f, 0)]
    [InlineData(31.9f, 0)]
    [InlineData(32f, 1)]
    [InlineData(48f, 1)]
    [InlineData(63.9f, 1)]
    [InlineData(64f, 2)]
    [InlineData(96f, 2)]
    [InlineData(127.9f, 2)]
    [InlineData(128f, 3)]
    [InlineData(1000f, 3)]
    public void GetLodLevel_ReturnsCorrectLevel(float distance, int expectedLod)
    {
        int actualLod = ChiseledBlockLodInspector.GetLodLevel(distance);
        Assert.Equal(expectedLod, actualLod);
    }

    [Fact]
    public void GetLodLevel_NegativeDistance_ReturnLod0()
    {
        Assert.Equal(0, ChiseledBlockLodInspector.GetLodLevel(-10f));
    }

    [Theory]
    [InlineData(0, 0f)]
    [InlineData(1, 32f)]
    [InlineData(2, 64f)]
    [InlineData(3, 128f)]
    public void GetThresholdForLevel_ReturnsCorrectThreshold(int level, float expectedThreshold)
    {
        Assert.Equal(expectedThreshold, ChiseledBlockLodInspector.GetThresholdForLevel(level));
    }

    [Fact]
    public void GetThresholdForLevel_InvalidLevel_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ChiseledBlockLodInspector.GetThresholdForLevel(4));
        Assert.Throws<ArgumentOutOfRangeException>(() => ChiseledBlockLodInspector.GetThresholdForLevel(-1));
    }

    [Theory]
    [InlineData(31.5f, true)]
    [InlineData(32.5f, true)]
    [InlineData(64.0f, true)]
    [InlineData(50f, false)]
    public void IsNearThreshold_DetectsEdgeCases(float distance, bool expected)
    {
        Assert.Equal(expected, ChiseledBlockLodInspector.IsNearThreshold(distance, hysteresis: 1f));
    }

    // -------------------------------------------------------------------------
    // PharosAssert.MeshFaceCountReduced tests
    // -------------------------------------------------------------------------

    [Fact]
    public void MeshFaceCountReduced_PassesWhenReductionMeetsThreshold()
    {
        var before = MeshSnapshot.CreateSynthetic(1000, 3000); // 1000 faces
        var after = MeshSnapshot.CreateSynthetic(400, 1200);   // 400 faces = 60% reduction

        // Should not throw
        PharosAssert.MeshFaceCountReduced(before, after, minReductionPercent: 50);
    }

    [Fact]
    public void MeshFaceCountReduced_ThrowsWhenReductionBelowThreshold()
    {
        var before = MeshSnapshot.CreateSynthetic(1000, 3000); // 1000 faces
        var after = MeshSnapshot.CreateSynthetic(900, 2700);   // 900 faces = 10% reduction

        var ex = Assert.Throws<PharosAssertException>(() =>
            PharosAssert.MeshFaceCountReduced(before, after, minReductionPercent: 50));

        Assert.Contains("at least 50%", ex.Message);
        Assert.Contains("10.0%", ex.Message);
    }

    [Fact]
    public void MeshFaceCountReduced_ThrowsOnInvalidBeforeSnapshot()
    {
        var before = MeshSnapshot.Empty;
        var after = MeshSnapshot.CreateSynthetic(100, 300);

        var ex = Assert.Throws<PharosAssertException>(() =>
            PharosAssert.MeshFaceCountReduced(before, after, 50));

        Assert.Contains("Before snapshot is invalid", ex.Message);
    }

    [Fact]
    public void MeshFaceCountReduced_ThrowsOnInvalidAfterSnapshot()
    {
        var before = MeshSnapshot.CreateSynthetic(100, 300);
        var after = MeshSnapshot.Empty;

        var ex = Assert.Throws<PharosAssertException>(() =>
            PharosAssert.MeshFaceCountReduced(before, after, 50));

        Assert.Contains("After snapshot is invalid", ex.Message);
    }

    [Fact]
    public void MeshFaceCountReduced_ThrowsOnZeroBeforeFaces()
    {
        var before = MeshSnapshot.CreateSynthetic(0, 0);
        var after = MeshSnapshot.CreateSynthetic(100, 300);

        var ex = Assert.Throws<PharosAssertException>(() =>
            PharosAssert.MeshFaceCountReduced(before, after, 50));

        Assert.Contains("zero faces", ex.Message);
    }

    [Fact]
    public void MeshFaceCountReduced_ThrowsOnInvalidPercentage()
    {
        var before = MeshSnapshot.CreateSynthetic(1000, 3000);
        var after = MeshSnapshot.CreateSynthetic(500, 1500);

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            PharosAssert.MeshFaceCountReduced(before, after, -1));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            PharosAssert.MeshFaceCountReduced(before, after, 101));
    }

    // -------------------------------------------------------------------------
    // PharosAssert.UvCoordinatesValid tests
    // -------------------------------------------------------------------------

    [Fact]
    public void UvCoordinatesValid_PassesWithValidUvs()
    {
        var uvs = new[] { new UvSample(0f, 0f), new UvSample(0.5f, 0.5f), new UvSample(1f, 1f) };
        var snapshot = MeshSnapshot.CreateSynthetic(100, 300, uvs);

        // Should not throw
        PharosAssert.UvCoordinatesValid(snapshot);
    }

    [Fact]
    public void UvCoordinatesValid_PassesWithinTolerance()
    {
        var uvs = new[] { new UvSample(-0.0005f, 1.0005f) }; // Slightly outside [0,1]
        var snapshot = MeshSnapshot.CreateSynthetic(100, 300, uvs);

        // Should not throw with default 0.001 tolerance
        PharosAssert.UvCoordinatesValid(snapshot, tolerance: 0.001f);
    }

    [Fact]
    public void UvCoordinatesValid_ThrowsOnUOutOfRange()
    {
        var uvs = new[] { new UvSample(1.5f, 0.5f) };
        var snapshot = MeshSnapshot.CreateSynthetic(100, 300, uvs);

        var ex = Assert.Throws<PharosAssertException>(() =>
            PharosAssert.UvCoordinatesValid(snapshot));

        Assert.Contains("U=1.5", ex.Message);
    }

    [Fact]
    public void UvCoordinatesValid_ThrowsOnVOutOfRange()
    {
        var uvs = new[] { new UvSample(0.5f, -0.5f) };
        var snapshot = MeshSnapshot.CreateSynthetic(100, 300, uvs);

        var ex = Assert.Throws<PharosAssertException>(() =>
            PharosAssert.UvCoordinatesValid(snapshot));

        Assert.Contains("V=-0.5", ex.Message);
    }

    [Fact]
    public void UvCoordinatesValid_PassesWithEmptyUvSamples()
    {
        var snapshot = MeshSnapshot.CreateSynthetic(100, 300); // No UV samples

        // Should not throw - nothing to validate
        PharosAssert.UvCoordinatesValid(snapshot);
    }

    [Fact]
    public void UvCoordinatesValid_ThrowsOnInvalidSnapshot()
    {
        var ex = Assert.Throws<PharosAssertException>(() =>
            PharosAssert.UvCoordinatesValid(MeshSnapshot.Empty));

        Assert.Contains("invalid", ex.Message);
    }

    // -------------------------------------------------------------------------
    // MeshInspector tests (headless-safe - tests null handling)
    // -------------------------------------------------------------------------

    [Fact]
    public void MeshInspector_Snapshot_NullChunk_ReturnsInvalid()
    {
        var inspector = new MeshInspector();
        var snapshot = inspector.Snapshot((Vintagestory.Client.NoObf.ClientChunk?)null);

        Assert.False(snapshot.IsValid);
        Assert.Contains("null", snapshot.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void MeshInspector_Snapshot_NullMeshData_ReturnsInvalid()
    {
        var inspector = new MeshInspector();
        var snapshot = inspector.Snapshot((Vintagestory.API.Client.MeshData?)null);

        Assert.False(snapshot.IsValid);
        Assert.Contains("null", snapshot.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }
}
