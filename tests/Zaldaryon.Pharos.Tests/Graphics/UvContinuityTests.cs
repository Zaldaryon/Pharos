using Xunit;
using Zaldaryon.Pharos.Assertions;
using Zaldaryon.Pharos.Graphics;

namespace Zaldaryon.Pharos.Tests.Graphics;

/// <summary>
/// Tests for UV continuity assertion and quad edge adjacency extraction.
/// All tests use synthetic MeshSnapshot data and are headless-safe.
/// </summary>
public sealed class UvContinuityTests
{
    // -------------------------------------------------------------------------
    // QuadEdge record tests
    // -------------------------------------------------------------------------

    [Fact]
    public void QuadEdge_IsValid_TrueForPositiveIndices()
    {
        QuadEdge edge = new(QuadAIndex: 0, QuadBIndex: 1, SharedAxis: 0, SharedValue: 1.0f);

        Assert.True(edge.IsValid);
    }

    [Fact]
    public void QuadEdge_IsValid_FalseForNegativeIndices()
    {
        QuadEdge edge = new(QuadAIndex: -1, QuadBIndex: 1, SharedAxis: 0, SharedValue: 1.0f);

        Assert.False(edge.IsValid);
    }

    [Fact]
    public void QuadEdge_AxisName_ReturnsCorrectNames()
    {
        Assert.Equal("X", new QuadEdge(0, 1, 0, 0f).AxisName);
        Assert.Equal("Y", new QuadEdge(0, 1, 1, 0f).AxisName);
        Assert.Equal("Z", new QuadEdge(0, 1, 2, 0f).AxisName);
        Assert.Equal("Unknown", new QuadEdge(0, 1, 99, 0f).AxisName);
    }

    // -------------------------------------------------------------------------
    // MeshSnapshot with edges tests
    // -------------------------------------------------------------------------

    [Fact]
    public void MeshSnapshot_CreateSyntheticWithEdges_PopulatesAllFields()
    {
        var uvs = new List<UvSample> { new(0f, 0f), new(1f, 0f), new(1f, 1f), new(0f, 1f) };
        var positions = new List<VertexPosition>
        {
            new(0f, 0f, 0f), new(1f, 0f, 0f), new(1f, 1f, 0f), new(0f, 1f, 0f)
        };
        var edges = new List<QuadEdge> { new(0, 1, 0, 1.0f) };

        MeshSnapshot snapshot = MeshSnapshot.CreateSyntheticWithEdges(4, 6, uvs, positions, edges);

        Assert.True(snapshot.IsValid);
        Assert.Equal(4, snapshot.UvSamples.Count);
        Assert.Equal(4, snapshot.VertexPositions.Count);
        Assert.Single(snapshot.QuadEdges);
    }

    // -------------------------------------------------------------------------
    // ExtractQuadAdjacency tests
    // -------------------------------------------------------------------------

    [Fact]
    public void ExtractQuadAdjacency_EmptySnapshot_ReturnsEmptyList()
    {
        MeshSnapshot snapshot = MeshSnapshot.Empty;

        var edges = MeshInspector.ExtractQuadAdjacency(snapshot);

        Assert.Empty(edges);
    }

    [Fact]
    public void ExtractQuadAdjacency_SingleQuad_ReturnsEmptyList()
    {
        var positions = CreateQuadPositions(0f, 0f, 0f);
        MeshSnapshot snapshot = MeshSnapshot.CreateSyntheticWithEdges(4, 6, [], positions, []);

        var edges = MeshInspector.ExtractQuadAdjacency(snapshot);

        Assert.Empty(edges);
    }

    [Fact]
    public void ExtractQuadAdjacency_TwoAdjacentQuads_FindsSharedEdge()
    {
        // Two quads sharing an edge at X=1
        var positions = new List<VertexPosition>();
        positions.AddRange(CreateQuadPositions(0f, 0f, 0f)); // Quad 0: (0,0)-(1,1)
        positions.AddRange(CreateQuadPositions(1f, 0f, 0f)); // Quad 1: (1,0)-(2,1)

        MeshSnapshot snapshot = MeshSnapshot.CreateSyntheticWithEdges(8, 12, [], positions, []);

        var edges = MeshInspector.ExtractQuadAdjacency(snapshot);

        Assert.Single(edges);
        Assert.Equal(0, edges[0].QuadAIndex);
        Assert.Equal(1, edges[0].QuadBIndex);
    }

    [Fact]
    public void ExtractQuadAdjacency_NonAdjacentQuads_ReturnsEmptyList()
    {
        // Two quads with a gap between them
        var positions = new List<VertexPosition>();
        positions.AddRange(CreateQuadPositions(0f, 0f, 0f)); // Quad 0: (0,0)-(1,1)
        positions.AddRange(CreateQuadPositions(5f, 0f, 0f)); // Quad 1: (5,0)-(6,1) - not adjacent

        MeshSnapshot snapshot = MeshSnapshot.CreateSyntheticWithEdges(8, 12, [], positions, []);

        var edges = MeshInspector.ExtractQuadAdjacency(snapshot);

        Assert.Empty(edges);
    }

    // -------------------------------------------------------------------------
    // UvContinuous assertion - passing cases
    // -------------------------------------------------------------------------

    [Fact]
    public void UvContinuous_NoEdges_PassesWithoutError()
    {
        MeshSnapshot snapshot = MeshSnapshot.CreateSyntheticWithEdges(4, 6, [], [], []);

        PharosAssert.UvContinuous(snapshot);
        // No exception means pass
    }

    [Fact]
    public void UvContinuous_ContinuousUvs_PassesWithoutError()
    {
        // Two adjacent quads with continuous UVs along the shared edge
        var positions = new List<VertexPosition>();
        positions.AddRange(CreateQuadPositions(0f, 0f, 0f)); // Quad 0
        positions.AddRange(CreateQuadPositions(1f, 0f, 0f)); // Quad 1

        // UVs that are continuous: quad 0's right edge matches quad 1's left edge
        var uvs = new List<UvSample>
        {
            // Quad 0: (0,0), (1,0), (1,1), (0,1)
            new(0.0f, 0.0f), new(0.5f, 0.0f), new(0.5f, 1.0f), new(0.0f, 1.0f),
            // Quad 1: (1,0), (2,0), (2,1), (1,1) - left edge UVs match quad 0's right edge
            new(0.5f, 0.0f), new(1.0f, 0.0f), new(1.0f, 1.0f), new(0.5f, 1.0f)
        };

        var edges = new List<QuadEdge> { new(0, 1, 0, 1.0f) };
        MeshSnapshot snapshot = MeshSnapshot.CreateSyntheticWithEdges(8, 12, uvs, positions, edges);

        PharosAssert.UvContinuous(snapshot);
        // No exception means pass
    }

    [Fact]
    public void UvContinuous_WithinTolerance_PassesWithoutError()
    {
        var positions = new List<VertexPosition>();
        positions.AddRange(CreateQuadPositions(0f, 0f, 0f));
        positions.AddRange(CreateQuadPositions(1f, 0f, 0f));

        // UVs with tiny discontinuity within tolerance
        var uvs = new List<UvSample>
        {
            new(0.0f, 0.0f), new(0.5f, 0.0f), new(0.5f, 1.0f), new(0.0f, 1.0f),
            new(0.5001f, 0.0f), new(1.0f, 0.0f), new(1.0f, 1.0f), new(0.5001f, 1.0f) // Within 0.001 tolerance
        };

        var edges = new List<QuadEdge> { new(0, 1, 0, 1.0f) };
        MeshSnapshot snapshot = MeshSnapshot.CreateSyntheticWithEdges(8, 12, uvs, positions, edges);

        PharosAssert.UvContinuous(snapshot, tolerance: 0.001f);
        // No exception means pass
    }

    // -------------------------------------------------------------------------
    // UvContinuous assertion - failing cases
    // -------------------------------------------------------------------------

    [Fact]
    public void UvContinuous_InvalidSnapshot_ThrowsException()
    {
        MeshSnapshot snapshot = MeshSnapshot.Empty;

        Assert.Throws<PharosAssertException>(() => PharosAssert.UvContinuous(snapshot));
    }

    [Fact]
    public void UvContinuous_DiscontinuousUvs_ThrowsException()
    {
        var positions = new List<VertexPosition>();
        positions.AddRange(CreateQuadPositions(0f, 0f, 0f));
        positions.AddRange(CreateQuadPositions(1f, 0f, 0f));

        // UVs with clear discontinuity - different atlas regions
        var uvs = new List<UvSample>
        {
            // Quad 0 uses one part of atlas
            new(0.0f, 0.0f), new(0.25f, 0.0f), new(0.25f, 0.25f), new(0.0f, 0.25f),
            // Quad 1 uses different part - UV seam at shared edge
            new(0.75f, 0.75f), new(1.0f, 0.75f), new(1.0f, 1.0f), new(0.75f, 1.0f)
        };

        var edges = new List<QuadEdge> { new(0, 1, 0, 1.0f) };
        MeshSnapshot snapshot = MeshSnapshot.CreateSyntheticWithEdges(8, 12, uvs, positions, edges);

        PharosAssertException ex = Assert.Throws<PharosAssertException>(
            () => PharosAssert.UvContinuous(snapshot));

        Assert.Contains("UV discontinuity", ex.Message);
        Assert.Contains("quads 0-1", ex.Message);
    }

    [Fact]
    public void UvContinuous_AboveTolerance_ThrowsException()
    {
        var positions = new List<VertexPosition>();
        positions.AddRange(CreateQuadPositions(0f, 0f, 0f));
        positions.AddRange(CreateQuadPositions(1f, 0f, 0f));

        // UVs with discontinuity slightly above tolerance
        var uvs = new List<UvSample>
        {
            new(0.0f, 0.0f), new(0.5f, 0.0f), new(0.5f, 1.0f), new(0.0f, 1.0f),
            new(0.51f, 0.0f), new(1.0f, 0.0f), new(1.0f, 1.0f), new(0.51f, 1.0f) // 0.01 gap
        };

        var edges = new List<QuadEdge> { new(0, 1, 0, 1.0f) };
        MeshSnapshot snapshot = MeshSnapshot.CreateSyntheticWithEdges(8, 12, uvs, positions, edges);

        Assert.Throws<PharosAssertException>(
            () => PharosAssert.UvContinuous(snapshot, tolerance: 0.001f));
    }

    // -------------------------------------------------------------------------
    // UvContinuityResult tests
    // -------------------------------------------------------------------------

    [Fact]
    public void UvContinuityResult_Empty_IsContinuous()
    {
        var result = UvContinuityResult.Empty;

        Assert.True(result.IsContinuous);
        Assert.Equal(0f, result.MaxDiscontinuity);
    }

    [Fact]
    public void UvContinuityResult_WithDiscontinuity_RecordsValues()
    {
        var edge = new QuadEdge(0, 1, 0, 1.0f);
        var result = new UvContinuityResult(edge, IsContinuous: false, MaxDiscontinuity: 0.1f);

        Assert.False(result.IsContinuous);
        Assert.Equal(0.1f, result.MaxDiscontinuity);
        Assert.Equal(edge, result.Edge);
    }

    // -------------------------------------------------------------------------
    // VertexPosition tests
    // -------------------------------------------------------------------------

    [Fact]
    public void VertexPosition_DistanceSquaredTo_ComputesCorrectly()
    {
        var a = new VertexPosition(0f, 0f, 0f);
        var b = new VertexPosition(1f, 0f, 0f);

        float distSq = a.DistanceSquaredTo(b);

        Assert.Equal(1f, distSq);
    }

    [Fact]
    public void VertexPosition_DistanceSquaredTo_DiagonalDistance()
    {
        var a = new VertexPosition(0f, 0f, 0f);
        var b = new VertexPosition(1f, 1f, 1f);

        float distSq = a.DistanceSquaredTo(b);

        Assert.Equal(3f, distSq, precision: 5);
    }

    // -------------------------------------------------------------------------
    // Helper methods
    // -------------------------------------------------------------------------

    /// <summary>
    /// Creates a unit quad (4 vertices) at the given offset.
    /// Quad vertices: (x,y,z), (x+1,y,z), (x+1,y+1,z), (x,y+1,z)
    /// </summary>
    private static List<VertexPosition> CreateQuadPositions(float x, float y, float z)
    {
        return new List<VertexPosition>
        {
            new(x, y, z),
            new(x + 1, y, z),
            new(x + 1, y + 1, z),
            new(x, y + 1, z)
        };
    }
}
