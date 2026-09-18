using Xunit;
using Zaldaryon.Pharos.Assertions;
using Zaldaryon.Pharos.Memory;

namespace Zaldaryon.Pharos.Tests.Memory;

/// <summary>
/// Tests for managed object leak detection using ManagedLeakTracker.
/// All tests are pure-logic and headless-safe (no native libraries required).
/// </summary>
public sealed class ManagedLeakTrackerTests
{
    // -------------------------------------------------------------------------
    // ManagedLeakReport record tests
    // -------------------------------------------------------------------------

    [Fact]
    public void ManagedLeakReport_Empty_HasNoLeaks()
    {
        ManagedLeakReport report = ManagedLeakReport.Empty;

        Assert.Equal(0, report.UnreturnedMeshParts);
        Assert.Equal(0, report.UnrecycledMeshData);
        Assert.False(report.HasLeaks);
        Assert.Equal(0, report.TotalLeaks);
    }

    [Fact]
    public void ManagedLeakReport_WithMeshPartLeaks_HasLeaksTrue()
    {
        ManagedLeakReport report = new(UnreturnedMeshParts: 5, UnrecycledMeshData: 0);

        Assert.True(report.HasLeaks);
        Assert.Equal(5, report.TotalLeaks);
    }

    [Fact]
    public void ManagedLeakReport_WithMeshDataLeaks_HasLeaksTrue()
    {
        ManagedLeakReport report = new(UnreturnedMeshParts: 0, UnrecycledMeshData: 3);

        Assert.True(report.HasLeaks);
        Assert.Equal(3, report.TotalLeaks);
    }

    [Fact]
    public void ManagedLeakReport_TotalLeaks_SumsAllCategories()
    {
        ManagedLeakReport report = new(UnreturnedMeshParts: 2, UnrecycledMeshData: 4);

        Assert.True(report.HasLeaks);
        Assert.Equal(6, report.TotalLeaks);
    }

    // -------------------------------------------------------------------------
    // ManagedLeakTracker basic tracking tests
    // -------------------------------------------------------------------------

    [Fact]
    public void ManagedLeakTracker_TrackInstance_IncreasesTrackedCount()
    {
        using ManagedLeakTracker tracker = new();
        object instance = new();

        tracker.TrackInstance(instance, "MeshPart");

        Assert.Equal(1, tracker.TrackedCount);
    }

    [Fact]
    public void ManagedLeakTracker_ReleaseInstance_DecreasesTrackedCount()
    {
        using ManagedLeakTracker tracker = new();
        object instance = new();

        tracker.TrackInstance(instance, "MeshPart");
        Assert.Equal(1, tracker.TrackedCount);

        bool released = tracker.ReleaseInstance(instance);

        Assert.True(released);
        Assert.Equal(0, tracker.TrackedCount);
    }

    [Fact]
    public void ManagedLeakTracker_ReleaseInstance_ReturnsFalseForUntracked()
    {
        using ManagedLeakTracker tracker = new();
        object instance = new();

        bool released = tracker.ReleaseInstance(instance);

        Assert.False(released);
    }

    [Fact]
    public void ManagedLeakTracker_TrackMultipleInstances_TracksAll()
    {
        using ManagedLeakTracker tracker = new();
        object mesh1 = new();
        object mesh2 = new();
        object meshData = new();

        tracker.TrackInstance(mesh1, "MeshPart");
        tracker.TrackInstance(mesh2, "MeshPart");
        tracker.TrackInstance(meshData, "MeshData");

        Assert.Equal(3, tracker.TrackedCount);
        Assert.Equal(2, tracker.GetTrackedByLabel("MeshPart").Count);
        Assert.Equal(1, tracker.GetTrackedByLabel("MeshData").Count);
    }

    // -------------------------------------------------------------------------
    // Baseline and leak report tests
    // -------------------------------------------------------------------------

    [Fact]
    public void ManagedLeakTracker_GetLeakReport_WithoutBaseline_ReturnsEmpty()
    {
        using ManagedLeakTracker tracker = new();
        tracker.TrackInstance(new object(), "MeshPart");

        ManagedLeakReport report = tracker.GetLeakReport();

        Assert.False(tracker.IsTracking);
        Assert.Equal(ManagedLeakReport.Empty, report);
    }

    [Fact]
    public void ManagedLeakTracker_GetLeakReport_NoLeaksWhenAllReleased()
    {
        using ManagedLeakTracker tracker = new();

        tracker.StartBaseline();
        object instance = new();
        tracker.TrackInstance(instance, "MeshPart");
        tracker.ReleaseInstance(instance);

        ManagedLeakReport report = tracker.GetLeakReport();

        Assert.False(report.HasLeaks);
        Assert.Equal(0, report.UnreturnedMeshParts);
    }

    [Fact]
    public void ManagedLeakTracker_GetLeakReport_DetectsUnreleasedMeshParts()
    {
        using ManagedLeakTracker tracker = new();

        tracker.StartBaseline();
        tracker.TrackInstance(new object(), "MeshPart");
        tracker.TrackInstance(new object(), "MeshPart");

        ManagedLeakReport report = tracker.GetLeakReport();

        Assert.True(report.HasLeaks);
        Assert.Equal(2, report.UnreturnedMeshParts);
        Assert.Equal(0, report.UnrecycledMeshData);
    }

    [Fact]
    public void ManagedLeakTracker_GetLeakReport_DetectsUnreleasedMeshData()
    {
        using ManagedLeakTracker tracker = new();

        tracker.StartBaseline();
        tracker.TrackInstance(new object(), "MeshData");
        tracker.TrackInstance(new object(), "MeshData");
        tracker.TrackInstance(new object(), "MeshData");

        ManagedLeakReport report = tracker.GetLeakReport();

        Assert.True(report.HasLeaks);
        Assert.Equal(0, report.UnreturnedMeshParts);
        Assert.Equal(3, report.UnrecycledMeshData);
    }

    [Fact]
    public void ManagedLeakTracker_GetLeakReport_DetectsMixedLeaks()
    {
        using ManagedLeakTracker tracker = new();

        tracker.StartBaseline();
        tracker.TrackInstance(new object(), "MeshPart");
        tracker.TrackInstance(new object(), "MeshData");
        tracker.TrackInstance(new object(), "MeshData");

        ManagedLeakReport report = tracker.GetLeakReport();

        Assert.True(report.HasLeaks);
        Assert.Equal(1, report.UnreturnedMeshParts);
        Assert.Equal(2, report.UnrecycledMeshData);
        Assert.Equal(3, report.TotalLeaks);
    }

    [Fact]
    public void ManagedLeakTracker_GetLeakReport_IgnoresPreBaselineInstances()
    {
        using ManagedLeakTracker tracker = new();

        // Track instances before baseline
        tracker.TrackInstance(new object(), "MeshPart");
        tracker.TrackInstance(new object(), "MeshData");

        tracker.StartBaseline();

        // Track new instances after baseline
        tracker.TrackInstance(new object(), "MeshPart");

        ManagedLeakReport report = tracker.GetLeakReport();

        // Only the post-baseline MeshPart should count as a leak
        Assert.True(report.HasLeaks);
        Assert.Equal(1, report.UnreturnedMeshParts);
        Assert.Equal(0, report.UnrecycledMeshData);
    }

    // -------------------------------------------------------------------------
    // Reset tests
    // -------------------------------------------------------------------------

    [Fact]
    public void ManagedLeakTracker_Reset_ClearsAllState()
    {
        using ManagedLeakTracker tracker = new();
        tracker.TrackInstance(new object(), "MeshPart");
        tracker.StartBaseline();

        tracker.Reset();

        Assert.Equal(0, tracker.TrackedCount);
        Assert.False(tracker.IsTracking);
    }

    // -------------------------------------------------------------------------
    // PharosAssert.NoManagedLeaks tests
    // -------------------------------------------------------------------------

    [Fact]
    public void PharosAssert_NoManagedLeaks_PassesForEmptyReport()
    {
        ManagedLeakReport report = ManagedLeakReport.Empty;

        PharosAssert.NoManagedLeaks(report);
        // No exception means pass
    }

    [Fact]
    public void PharosAssert_NoManagedLeaks_ThrowsForMeshPartLeaks()
    {
        ManagedLeakReport report = new(UnreturnedMeshParts: 2, UnrecycledMeshData: 0);

        PharosAssertException ex = Assert.Throws<PharosAssertException>(
            () => PharosAssert.NoManagedLeaks(report));

        Assert.Contains("MeshParts: 2", ex.Message);
    }

    [Fact]
    public void PharosAssert_NoManagedLeaks_ThrowsForMeshDataLeaks()
    {
        ManagedLeakReport report = new(UnreturnedMeshParts: 0, UnrecycledMeshData: 3);

        PharosAssertException ex = Assert.Throws<PharosAssertException>(
            () => PharosAssert.NoManagedLeaks(report));

        Assert.Contains("MeshData: 3", ex.Message);
    }

    [Fact]
    public void PharosAssert_ManagedLeaksBelow_PassesWhenBelowThresholds()
    {
        ManagedLeakReport report = new(UnreturnedMeshParts: 1, UnrecycledMeshData: 2);

        PharosAssert.ManagedLeaksBelow(report, maxMeshPartLeaks: 5, maxMeshDataLeaks: 5);
        // No exception means pass
    }

    [Fact]
    public void PharosAssert_ManagedLeaksBelow_ThrowsWhenAboveThresholds()
    {
        ManagedLeakReport report = new(UnreturnedMeshParts: 10, UnrecycledMeshData: 8);

        PharosAssertException ex = Assert.Throws<PharosAssertException>(
            () => PharosAssert.ManagedLeaksBelow(report, maxMeshPartLeaks: 5, maxMeshDataLeaks: 5));

        Assert.Contains("MeshParts: 10 > 5", ex.Message);
        Assert.Contains("MeshData: 8 > 5", ex.Message);
    }

    // -------------------------------------------------------------------------
    // GetTrackedInstances tests
    // -------------------------------------------------------------------------

    [Fact]
    public void ManagedLeakTracker_GetTrackedInstances_ReturnsAllTracked()
    {
        using ManagedLeakTracker tracker = new();
        object mesh1 = new();
        object meshData1 = new();

        tracker.TrackInstance(mesh1, "MeshPart");
        tracker.TrackInstance(meshData1, "MeshData");

        var tracked = tracker.GetTrackedInstances();

        Assert.Equal(2, tracked.Count);
        Assert.True(tracked.ContainsKey(mesh1));
        Assert.True(tracked.ContainsKey(meshData1));
        Assert.Equal("MeshPart", tracked[mesh1].Label);
        Assert.Equal("MeshData", tracked[meshData1].Label);
    }

    [Fact]
    public void ManagedLeakTracker_GetTrackedByLabel_FiltersCorrectly()
    {
        using ManagedLeakTracker tracker = new();
        object mesh1 = new();
        object mesh2 = new();
        object meshData1 = new();

        tracker.TrackInstance(mesh1, "MeshPart");
        tracker.TrackInstance(mesh2, "MeshPart");
        tracker.TrackInstance(meshData1, "MeshData");

        var meshParts = tracker.GetTrackedByLabel("MeshPart");
        var meshDatas = tracker.GetTrackedByLabel("MeshData");

        Assert.Equal(2, meshParts.Count);
        Assert.Single(meshDatas);
        Assert.Contains(mesh1, meshParts);
        Assert.Contains(mesh2, meshParts);
        Assert.Contains(meshData1, meshDatas);
    }

    // -------------------------------------------------------------------------
    // Thread safety tests
    // -------------------------------------------------------------------------

    [Fact]
    public void ManagedLeakTracker_ConcurrentOperations_DoNotThrow()
    {
        using ManagedLeakTracker tracker = new();
        tracker.StartBaseline();

        var tasks = Enumerable.Range(0, 100).Select(i => Task.Run(() =>
        {
            object instance = new();
            tracker.TrackInstance(instance, i % 2 == 0 ? "MeshPart" : "MeshData");
            Thread.Sleep(1);
            tracker.ReleaseInstance(instance);
        })).ToArray();

        Task.WaitAll(tasks);

        ManagedLeakReport report = tracker.GetLeakReport();
        Assert.False(report.HasLeaks);
    }
}
