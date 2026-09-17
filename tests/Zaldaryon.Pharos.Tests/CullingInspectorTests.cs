using Xunit;
using Zaldaryon.Pharos.Culling;
using Zaldaryon.Pharos.Timing;

namespace Zaldaryon.Pharos.Tests;

/// <summary>
/// Unit tests for <see cref="CullingInspector"/>, <see cref="CullingSnapshot"/>, and <see cref="FrustumPlane"/>.
/// </summary>
public sealed class CullingInspectorTests
{
    // -------------------------------------------------------------------------
    // FrustumPlane struct tests
    // -------------------------------------------------------------------------

    [Fact]
    public void FrustumPlane_DistanceOf_PointOnPlane_ReturnsZero()
    {
        FrustumPlane plane = new(0, 0, 1, 0);

        double distance = plane.DistanceOf(5, 3, 0);

        Assert.Equal(0.0, distance, precision: 10);
    }

    [Fact]
    public void FrustumPlane_DistanceOf_PointInFront_ReturnsPositive()
    {
        FrustumPlane plane = new(0, 0, 1, 0);

        double distance = plane.DistanceOf(0, 0, 5);

        Assert.True(distance > 0, $"Expected positive distance, got {distance}");
    }

    [Fact]
    public void FrustumPlane_DistanceOf_PointBehind_ReturnsNegative()
    {
        FrustumPlane plane = new(0, 0, 1, 0);

        double distance = plane.DistanceOf(0, 0, -3);

        Assert.True(distance < 0, $"Expected negative distance, got {distance}");
    }

    [Fact]
    public void FrustumPlane_Equality_SameValues_AreEqual()
    {
        FrustumPlane a = new(1.0, 0.0, 0.0, 5.0);
        FrustumPlane b = new(1.0, 0.0, 0.0, 5.0);

        Assert.Equal(a, b);
    }

    [Fact]
    public void FrustumPlane_Equality_DifferentValues_AreNotEqual()
    {
        FrustumPlane a = new(1.0, 0.0, 0.0, 5.0);
        FrustumPlane b = new(0.0, 1.0, 0.0, 5.0);

        Assert.NotEqual(a, b);
    }

    // -------------------------------------------------------------------------
    // CullingSnapshot record tests
    // -------------------------------------------------------------------------

    [Fact]
    public void CullingSnapshot_Empty_HasZeroCounts()
    {
        CullingSnapshot snap = CullingSnapshot.Empty;

        Assert.Empty(snap.VisibleChunks);
        Assert.Empty(snap.CulledChunks);
        Assert.Empty(snap.OcclusionCulledChunks);
        Assert.Empty(snap.FrustumPlanes);
        Assert.Equal(0, snap.TestCount);
        Assert.Equal(0, snap.TotalChunks);
        Assert.Equal(0.0, snap.CullingRatio);
    }

    [Fact]
    public void CullingSnapshot_CullingRatio_IsZeroWhenNoChunks()
    {
        CullingSnapshot snap = new()
        {
            VisibleChunks = [],
            CulledChunks = [],
            TotalChunks = 0,
        };

        Assert.Equal(0.0, snap.CullingRatio);
    }

    [Fact]
    public void CullingSnapshot_CullingRatio_HalfCulled()
    {
        CullingSnapshot snap = new()
        {
            VisibleChunks = [new ChunkPos(0, 0, 0), new ChunkPos(1, 0, 0)],
            CulledChunks = [new ChunkPos(2, 0, 0), new ChunkPos(3, 0, 0)],
            TotalChunks = 4,
        };

        Assert.Equal(0.5, snap.CullingRatio, precision: 10);
    }

    [Fact]
    public void CullingSnapshot_CullingRatio_AllVisible()
    {
        CullingSnapshot snap = new()
        {
            VisibleChunks = [new ChunkPos(0, 0, 0)],
            CulledChunks = [],
            TotalChunks = 1,
        };

        Assert.Empty(snap.CulledChunks);
        Assert.Equal(0.0, snap.CullingRatio);
    }

    [Fact]
    public void CullingSnapshot_CullingRatio_AllCulled()
    {
        CullingSnapshot snap = new()
        {
            VisibleChunks = [],
            CulledChunks = [new ChunkPos(0, 0, 0), new ChunkPos(1, 0, 0)],
            TotalChunks = 2,
        };

        Assert.Empty(snap.VisibleChunks);
        Assert.Equal(1.0, snap.CullingRatio, precision: 10);
    }

    [Fact]
    public void CullingSnapshot_IsImmutable_AfterConstruction()
    {
        List<ChunkPos> visible = [new ChunkPos(0, 0, 0)];
        CullingSnapshot snap = new() { VisibleChunks = visible, TotalChunks = 1 };

        Assert.Single(snap.VisibleChunks);
    }

    [Fact]
    public void CullingSnapshot_FrustumPlanes_CanContainSixPlanes()
    {
        FrustumPlane[] planes =
        [
            new(0, 0, 1, 0),   // Near
            new(-1, 0, 0, 10), // Left
            new(1, 0, 0, 10),  // Right
            new(0, 1, 0, 10),  // Top
            new(0, -1, 0, 10), // Bottom
            new(0, 0, -1, 100) // Far
        ];

        CullingSnapshot snap = new() { FrustumPlanes = planes };

        Assert.Equal(6, snap.FrustumPlanes.Count);
        for (int i = 0; i < 6; i++)
        {
            Assert.Equal(planes[i], snap.FrustumPlanes[i]);
        }
    }

    [Fact]
    public void CullingSnapshot_TestCount_ReflectsAllTestedChunks()
    {
        CullingSnapshot snap = new()
        {
            VisibleChunks = [new ChunkPos(0, 0, 1), new ChunkPos(1, 0, 1)],
            CulledChunks = [new ChunkPos(0, 0, -5)],
            TestCount = 3,
            TotalChunks = 3,
        };

        Assert.Equal(3, snap.TestCount);
        Assert.Equal(3, snap.TotalChunks);
        Assert.Equal(2, snap.VisibleChunks.Count);
        Assert.Single(snap.CulledChunks);
    }

    // -------------------------------------------------------------------------
    // CullingInspector without a live client (no-crash baseline)
    // -------------------------------------------------------------------------

    [Fact]
    public void CullingInspector_Snapshot_WithNullWorldMap_ReturnsEmptySnapshot()
    {
        CullingSnapshot empty = CullingSnapshot.Empty;

        Assert.Equal(0, empty.TestCount);
        Assert.Equal(0, empty.TotalChunks);
        Assert.Empty(empty.VisibleChunks);
        Assert.Empty(empty.CulledChunks);
    }

    // -------------------------------------------------------------------------
    // Integration tests
    // -------------------------------------------------------------------------

    [Fact]
    public void HeadlessClient_HasNonNullCullingProperty()
    {
        System.Reflection.PropertyInfo? prop = typeof(Core.HeadlessClient)
            .GetProperty("Culling");

        Assert.NotNull(prop);
        Assert.Equal(typeof(CullingInspector), prop!.PropertyType);
    }

    [Fact]
    public void CullingSnapshot_OcclusionCulledChunks_IsSubsetOfVisibleChunks()
    {
        List<ChunkPos> visible = [new ChunkPos(0, 0, 0), new ChunkPos(1, 0, 0), new ChunkPos(2, 0, 0)];
        List<ChunkPos> occluded = [new ChunkPos(0, 0, 0)];

        CullingSnapshot snap = new()
        {
            VisibleChunks = visible,
            OcclusionCulledChunks = occluded,
            TotalChunks = 3,
        };

        foreach (ChunkPos p in snap.OcclusionCulledChunks)
        {
            Assert.Contains(p, snap.VisibleChunks);
        }
    }
}
