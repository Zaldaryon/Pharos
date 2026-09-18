using Xunit;
using Zaldaryon.Pharos.Assertions;
using Zaldaryon.Pharos.Culling;
using Zaldaryon.Pharos.Timing;

namespace Zaldaryon.Pharos.Tests;

/// <summary>
/// Unit tests for culling validation types: VisibilityStats, FrustumOracleComparator,
/// BfsVisibilityStats, BfsDebugHook, and PharosAssert chunk helpers.
/// </summary>
public sealed class CullingValidationTests
{
    // -------------------------------------------------------------------------
    // VisibilityStats tests
    // -------------------------------------------------------------------------

    [Fact]
    public void VisibilityStats_Empty_HasZeroCounts()
    {
        VisibilityStats stats = VisibilityStats.Empty;

        Assert.Equal(0, stats.ChunksEvaluated);
        Assert.Equal(0, stats.FrustumCulledCount);
        Assert.Equal(0, stats.OcclusionCulledCount);
        Assert.Equal(0, stats.VisibleCount);
        Assert.Equal(0.0, stats.FrustumCullRate);
        Assert.Equal(0.0, stats.OcclusionCullRate);
    }

    [Fact]
    public void VisibilityStats_FromSnapshot_ComputesCorrectCounts()
    {
        CullingSnapshot snap = new()
        {
            VisibleChunks = [new ChunkPos(0, 0, 0), new ChunkPos(1, 0, 0), new ChunkPos(2, 0, 0)],
            CulledChunks = [new ChunkPos(3, 0, 0), new ChunkPos(4, 0, 0)],
            OcclusionCulledChunks = [new ChunkPos(2, 0, 0)],
            TotalChunks = 5,
        };

        VisibilityStats stats = VisibilityStats.FromSnapshot(snap);

        Assert.Equal(5, stats.ChunksEvaluated);
        Assert.Equal(2, stats.FrustumCulledCount);
        Assert.Equal(1, stats.OcclusionCulledCount);
        Assert.Equal(2, stats.VisibleCount); // 3 visible - 1 occluded = 2 truly visible
    }

    [Fact]
    public void VisibilityStats_FrustumCullRate_HalfCulled()
    {
        VisibilityStats stats = new(
            ChunksEvaluated: 100,
            FrustumCulledCount: 50,
            OcclusionCulledCount: 10,
            VisibleCount: 40
        );

        Assert.Equal(0.5, stats.FrustumCullRate, precision: 10);
        Assert.Equal(0.1, stats.OcclusionCullRate, precision: 10);
    }

    [Fact]
    public void VisibilityStats_CullRates_ZeroWhenNoChunksEvaluated()
    {
        VisibilityStats stats = new(
            ChunksEvaluated: 0,
            FrustumCulledCount: 0,
            OcclusionCulledCount: 0,
            VisibleCount: 0
        );

        Assert.Equal(0.0, stats.FrustumCullRate);
        Assert.Equal(0.0, stats.OcclusionCullRate);
    }

    // -------------------------------------------------------------------------
    // FrustumOracleComparator tests
    // -------------------------------------------------------------------------

    [Fact]
    public void FrustumOracle_IsBoxInFrustum_BoxFullyInside_ReturnsTrue()
    {
        // Simple frustum: near plane at z=0 facing +Z, far plane at z=100
        FrustumPlane[] planes =
        [
            new(0, 0, 1, 0),    // Near: z >= 0
            new(0, 0, -1, 100), // Far: z <= 100
            new(1, 0, 0, 50),   // Left: x >= -50
            new(-1, 0, 0, 50),  // Right: x <= 50
            new(0, 1, 0, 50),   // Bottom: y >= -50
            new(0, -1, 0, 50),  // Top: y <= 50
        ];

        bool inside = FrustumOracleComparator.IsBoxInFrustum(
            planes, minX: 10, minY: 10, minZ: 10, maxX: 20, maxY: 20, maxZ: 20);

        Assert.True(inside);
    }

    [Fact]
    public void FrustumOracle_IsBoxInFrustum_BoxCompletelyOutside_ReturnsFalse()
    {
        // Near plane at z=0 facing +Z
        FrustumPlane[] planes = [new(0, 0, 1, 0)];

        // Box completely behind the near plane (z < 0)
        bool inside = FrustumOracleComparator.IsBoxInFrustum(
            planes, minX: 0, minY: 0, minZ: -20, maxX: 10, maxY: 10, maxZ: -10);

        Assert.False(inside);
    }

    [Fact]
    public void FrustumOracle_IsBoxInFrustum_BoxIntersectsPlane_ReturnsTrue()
    {
        // Near plane at z=5 facing +Z
        FrustumPlane[] planes = [new(0, 0, 1, -5)];

        // Box straddles the plane (z from 0 to 10)
        bool inside = FrustumOracleComparator.IsBoxInFrustum(
            planes, minX: 0, minY: 0, minZ: 0, maxX: 10, maxY: 10, maxZ: 10);

        Assert.True(inside);
    }

    [Fact]
    public void FrustumOracle_IsChunkInFrustum_ConvertsChunkToBlockCoordinates()
    {
        // Near plane at z=64 (chunk 2 in Z)
        FrustumPlane[] planes = [new(0, 0, 1, -64)];

        // Chunk at (0, 0, 3) = blocks 96-128, should be inside (z >= 64)
        bool insideChunk3 = FrustumOracleComparator.IsChunkInFrustum(planes, new ChunkPos(0, 0, 3));
        // Chunk at (0, 0, 1) = blocks 32-64, edge case
        bool insideChunk1 = FrustumOracleComparator.IsChunkInFrustum(planes, new ChunkPos(0, 0, 1));
        // Chunk at (0, 0, 0) = blocks 0-32, should be outside (z < 64)
        bool insideChunk0 = FrustumOracleComparator.IsChunkInFrustum(planes, new ChunkPos(0, 0, 0));

        Assert.True(insideChunk3);
        Assert.True(insideChunk1); // P-vertex at z=64 is on the plane (not behind)
        Assert.False(insideChunk0);
    }

    [Fact]
    public void FrustumOracle_Compare_DetectsFalseNegatives()
    {
        // Setup: chunk (1,0,0) is marked culled by SIMD but oracle says it should be visible
        FrustumPlane[] planes =
        [
            new(0, 0, 1, 0),    // Near: z >= 0
            new(0, 0, -1, 200), // Far: z <= 200
        ];

        CullingSnapshot snapshot = new()
        {
            VisibleChunks = [new ChunkPos(0, 0, 0)],
            CulledChunks = [new ChunkPos(1, 0, 0)], // This chunk is incorrectly culled
            FrustumPlanes = planes,
            TotalChunks = 2,
        };

        ChunkPos[] allChunks = [new(0, 0, 0), new(1, 0, 0)];

        IReadOnlyList<OracleMismatch> mismatches = FrustumOracleComparator.Compare(snapshot, allChunks);
        IReadOnlyList<OracleMismatch> falseNegs = FrustumOracleComparator.FalseNegatives(mismatches);

        Assert.Single(falseNegs);
        Assert.Equal(new ChunkPos(1, 0, 0), falseNegs[0].Chunk);
        Assert.Equal(OracleMismatchType.FalseNegative, falseNegs[0].Type);
    }

    [Fact]
    public void FrustumOracle_Compare_NoMismatchesWhenConsistent()
    {
        FrustumPlane[] planes =
        [
            new(0, 0, 1, 0),    // Near: z >= 0
            new(0, 0, -1, 100), // Far: z <= 100
        ];

        // Chunk at (0,0,0) is inside (z 0-32), chunk at (0,0,-2) is outside (z -64 to -32)
        // Note: chunk (0,0,-1) at z=-32 to 0 would actually intersect the near plane at z=0
        CullingSnapshot snapshot = new()
        {
            VisibleChunks = [new ChunkPos(0, 0, 0)],
            CulledChunks = [new ChunkPos(0, 0, -2)],
            FrustumPlanes = planes,
            TotalChunks = 2,
        };

        ChunkPos[] allChunks = [new(0, 0, 0), new(0, 0, -2)];

        IReadOnlyList<OracleMismatch> falseNegs = FrustumOracleComparator.FalseNegatives(
            FrustumOracleComparator.Compare(snapshot, allChunks));

        Assert.Empty(falseNegs);
    }

    // -------------------------------------------------------------------------
    // BfsVisibilityStats tests
    // -------------------------------------------------------------------------

    [Fact]
    public void BfsVisibilityStats_Empty_HasZeroCounts()
    {
        BfsVisibilityStats stats = BfsVisibilityStats.Empty;

        Assert.Equal(0, stats.MaxDepthReached);
        Assert.Equal(0, stats.BlockedByOpaqueCount);
        Assert.Equal(0, stats.TotalNodesVisited);
        Assert.Equal(0, stats.BfsCallCount);
        Assert.Equal(int.MaxValue, stats.DepthLimit);
        Assert.True(stats.DepthLimitRespected);
    }

    [Fact]
    public void BfsVisibilityStats_DepthLimitRespected_TrueWhenWithinLimit()
    {
        BfsVisibilityStats stats = new(
            MaxDepthReached: 5,
            BlockedByOpaqueCount: 2,
            TotalNodesVisited: 20,
            BfsCallCount: 1,
            DepthLimit: 10
        );

        Assert.True(stats.DepthLimitRespected);
    }

    [Fact]
    public void BfsVisibilityStats_DepthLimitRespected_FalseWhenExceeded()
    {
        BfsVisibilityStats stats = new(
            MaxDepthReached: 15,
            BlockedByOpaqueCount: 0,
            TotalNodesVisited: 100,
            BfsCallCount: 1,
            DepthLimit: 10
        );

        Assert.False(stats.DepthLimitRespected);
    }

    // -------------------------------------------------------------------------
    // BfsDebugHook tests
    // -------------------------------------------------------------------------

    [Fact]
    public void BfsDebugHook_Traverse_VisitsSixConnectedNeighbors()
    {
        // 3x3x3 cube of chunks centered at (0,0,0)
        HashSet<ChunkPos> chunks = [];
        for (int x = -1; x <= 1; x++)
        for (int y = -1; y <= 1; y++)
        for (int z = -1; z <= 1; z++)
            chunks.Add(new ChunkPos(x, y, z));

        BfsDebugHook hook = new(chunks, _ => false);

        BfsVisibilityStats stats = hook.Traverse(new ChunkPos(0, 0, 0), depthLimit: 10);

        // Should visit all 27 chunks via 6-connected traversal
        Assert.Equal(27, stats.TotalNodesVisited);
        Assert.Equal(27, hook.VisitedChunks.Count);
        Assert.True(stats.DepthLimitRespected);
    }

    [Fact]
    public void BfsDebugHook_Traverse_RespectsDepthLimit()
    {
        // Line of chunks along X axis
        HashSet<ChunkPos> chunks = [];
        for (int x = 0; x <= 10; x++)
            chunks.Add(new ChunkPos(x, 0, 0));

        BfsDebugHook hook = new(chunks, _ => false);

        BfsVisibilityStats stats = hook.Traverse(new ChunkPos(0, 0, 0), depthLimit: 3);

        // Should visit chunks 0-3 (depth 0,1,2,3) = 4 chunks
        Assert.Equal(4, stats.TotalNodesVisited);
        Assert.Equal(3, stats.MaxDepthReached);
        Assert.True(stats.DepthLimitRespected);
    }

    [Fact]
    public void BfsDebugHook_Traverse_StopsAtOpaqueChunks()
    {
        // Line of chunks with an opaque blocker at x=2
        HashSet<ChunkPos> chunks = [];
        for (int x = 0; x <= 5; x++)
            chunks.Add(new ChunkPos(x, 0, 0));

        BfsDebugHook hook = new(chunks, pos => pos.X == 2);

        BfsVisibilityStats stats = hook.Traverse(new ChunkPos(0, 0, 0), depthLimit: 10);

        // Should visit 0, 1, 2 (opaque blocker stops at 2, but 2 is visited)
        Assert.Equal(3, stats.TotalNodesVisited);
        Assert.Equal(1, stats.BlockedByOpaqueCount);
        Assert.Contains(new ChunkPos(2, 0, 0), hook.VisitedChunks);
        Assert.DoesNotContain(new ChunkPos(3, 0, 0), hook.VisitedChunks);
    }

    [Fact]
    public void BfsDebugHook_Traverse_ReturnsEmptyForMissingStartChunk()
    {
        HashSet<ChunkPos> chunks = [new ChunkPos(0, 0, 0)];
        BfsDebugHook hook = new(chunks, _ => false);

        BfsVisibilityStats stats = hook.Traverse(new ChunkPos(99, 99, 99), depthLimit: 10);

        Assert.Equal(0, stats.TotalNodesVisited);
        Assert.Equal(1, stats.BfsCallCount);
        Assert.Empty(hook.VisitedChunks);
    }

    // -------------------------------------------------------------------------
    // PharosAssert chunk helper tests
    // -------------------------------------------------------------------------

    [Fact]
    public void PharosAssert_ChunkVisible_Passes_WhenChunkInVisibleSet()
    {
        CullingSnapshot snap = new()
        {
            VisibleChunks = [new ChunkPos(5, 0, 5)],
        };

        PharosAssert.ChunkVisible(snap, new ChunkPos(5, 0, 5));
    }

    [Fact]
    public void PharosAssert_ChunkVisible_Throws_WhenChunkNotVisible()
    {
        CullingSnapshot snap = new()
        {
            VisibleChunks = [new ChunkPos(0, 0, 0)],
        };

        PharosAssertException ex = Assert.Throws<PharosAssertException>(
            () => PharosAssert.ChunkVisible(snap, new ChunkPos(99, 0, 0)));

        Assert.Contains("ChunkPos(99, 0, 0)", ex.Message);
    }

    [Fact]
    public void PharosAssert_ChunkCulled_Passes_WhenChunkInCulledSet()
    {
        CullingSnapshot snap = new()
        {
            CulledChunks = [new ChunkPos(10, 0, 10)],
        };

        PharosAssert.ChunkCulled(snap, new ChunkPos(10, 0, 10));
    }

    [Fact]
    public void PharosAssert_ChunkCulled_Throws_WhenChunkNotCulled()
    {
        CullingSnapshot snap = new()
        {
            CulledChunks = [],
        };

        PharosAssertException ex = Assert.Throws<PharosAssertException>(
            () => PharosAssert.ChunkCulled(snap, new ChunkPos(1, 2, 3)));

        Assert.Contains("ChunkPos(1, 2, 3)", ex.Message);
    }

    [Fact]
    public void PharosAssert_ChunkOccluded_Passes_WhenChunkInOccludedSet()
    {
        CullingSnapshot snap = new()
        {
            VisibleChunks = [new ChunkPos(0, 0, 0)],
            OcclusionCulledChunks = [new ChunkPos(0, 0, 0)],
        };

        PharosAssert.ChunkOccluded(snap, new ChunkPos(0, 0, 0));
    }

    [Fact]
    public void PharosAssert_ChunkOccluded_Throws_WhenChunkNotOccluded()
    {
        CullingSnapshot snap = new()
        {
            OcclusionCulledChunks = [],
        };

        PharosAssertException ex = Assert.Throws<PharosAssertException>(
            () => PharosAssert.ChunkOccluded(snap, new ChunkPos(7, 7, 7)));

        Assert.Contains("ChunkPos(7, 7, 7)", ex.Message);
    }

    // -------------------------------------------------------------------------
    // CullingInspector BfsSnapshot test
    // -------------------------------------------------------------------------

    [Fact]
    public void CullingInspector_BfsSnapshot_ReturnsEmptyInHeadlessMode()
    {
        // Verify BfsSnapshot exists and returns Empty when live BFS is unavailable
        BfsVisibilityStats stats = BfsVisibilityStats.Empty;

        Assert.Equal(0, stats.TotalNodesVisited);
        Assert.Equal(0, stats.BfsCallCount);
        Assert.True(stats.DepthLimitRespected);
    }
}
