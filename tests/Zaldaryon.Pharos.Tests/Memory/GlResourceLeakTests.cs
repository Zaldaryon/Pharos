using Xunit;
using Zaldaryon.Pharos.Assertions;
using Zaldaryon.Pharos.Graphics;
using Zaldaryon.Pharos.Memory;

namespace Zaldaryon.Pharos.Tests.Memory;

/// <summary>
/// Tests for GL resource leak detection using synthetic GlCommandRecord data.
/// All tests are pure-logic and headless-safe (no native libraries required).
/// </summary>
public sealed class GlResourceLeakTests
{
    // -------------------------------------------------------------------------
    // GlLeakReport record tests
    // -------------------------------------------------------------------------

    [Fact]
    public void GlLeakReport_Empty_HasNoLeaks()
    {
        GlLeakReport report = GlLeakReport.Empty;

        Assert.Equal(0, report.BufferLeaks);
        Assert.Equal(0, report.TextureLeaks);
        Assert.Equal(0, report.VAOLeaks);
        Assert.Equal(0L, report.UnmanagedGrowthBytes);
        Assert.False(report.HasLeaks);
        Assert.Equal(0, report.TotalResourceLeaks);
    }

    [Fact]
    public void GlLeakReport_WithBufferLeaks_HasLeaksTrue()
    {
        GlLeakReport report = new() { BufferLeaks = 5 };

        Assert.True(report.HasLeaks);
        Assert.Equal(5, report.TotalResourceLeaks);
    }

    [Fact]
    public void GlLeakReport_WithTextureLeaks_HasLeaksTrue()
    {
        GlLeakReport report = new() { TextureLeaks = 3 };

        Assert.True(report.HasLeaks);
        Assert.Equal(3, report.TotalResourceLeaks);
    }

    [Fact]
    public void GlLeakReport_WithVAOLeaks_HasLeaksTrue()
    {
        GlLeakReport report = new() { VAOLeaks = 2 };

        Assert.True(report.HasLeaks);
        Assert.Equal(2, report.TotalResourceLeaks);
    }

    [Fact]
    public void GlLeakReport_TotalResourceLeaks_SumsAllCategories()
    {
        GlLeakReport report = new()
        {
            BufferLeaks = 3,
            TextureLeaks = 5,
            VAOLeaks = 2,
        };

        Assert.Equal(10, report.TotalResourceLeaks);
    }

    [Fact]
    public void GlLeakReport_UnmanagedGrowth_DoesNotAffectHasLeaks()
    {
        GlLeakReport report = new() { UnmanagedGrowthBytes = 1024 * 1024 };

        Assert.False(report.HasLeaks);
        Assert.Equal(0, report.TotalResourceLeaks);
    }

    // -------------------------------------------------------------------------
    // UnmanagedMemoryTracker tests
    // -------------------------------------------------------------------------

    [Fact]
    public void UnmanagedMemoryTracker_NotTracking_ReturnsZeroGrowth()
    {
        UnmanagedMemoryTracker tracker = new();

        Assert.False(tracker.IsTracking);
        Assert.Equal(0, tracker.GetGrowthBytes());
    }

    [Fact]
    public void UnmanagedMemoryTracker_StartTracking_SetsIsTrackingTrue()
    {
        UnmanagedMemoryTracker tracker = new();

        tracker.StartTracking();

        Assert.True(tracker.IsTracking);
        Assert.True(tracker.BaselineBytes > 0);
    }

    [Fact]
    public void UnmanagedMemoryTracker_Reset_ClearsState()
    {
        UnmanagedMemoryTracker tracker = UnmanagedMemoryTracker.StartNew();
        Assert.True(tracker.IsTracking);

        tracker.Reset();

        Assert.False(tracker.IsTracking);
        Assert.Equal(0, tracker.BaselineBytes);
    }

    [Fact]
    public void UnmanagedMemoryTracker_StartNew_ReturnsTrackingInstance()
    {
        UnmanagedMemoryTracker tracker = UnmanagedMemoryTracker.StartNew();

        Assert.True(tracker.IsTracking);
        Assert.True(tracker.BaselineBytes > 0);
    }

    [Fact]
    public void UnmanagedMemoryTracker_GetCurrentBytes_ReturnsPositiveValue()
    {
        UnmanagedMemoryTracker tracker = new();

        long currentBytes = tracker.GetCurrentBytes();

        Assert.True(currentBytes > 0);
    }

    [Fact]
    public void UnmanagedMemoryTracker_GetGrowthBytes_ReturnsNonNegativeAfterAllocation()
    {
        UnmanagedMemoryTracker tracker = UnmanagedMemoryTracker.StartNew();

        // Allocate some memory
        byte[] data = new byte[1024 * 1024]; // 1 MB
        _ = data.Length; // Prevent optimization

        long growth = tracker.GetGrowthBytes();

        // Growth should be measurable (at least close to the allocation)
        Assert.True(growth >= 0, $"Expected non-negative growth, got {growth}");
    }

    // -------------------------------------------------------------------------
    // GlResourceLeakDetector tests (using synthetic data)
    // -------------------------------------------------------------------------

    [Fact]
    public void GlResourceLeakDetector_NotTracking_ReturnsEmptyReport()
    {
        GlResourceLeakDetector detector = new();

        GlLeakReport report = detector.GetLeakReport(GlCommandRecord.Empty);

        Assert.Equal(GlLeakReport.Empty, report);
    }

    [Fact]
    public void GlResourceLeakDetector_NoLeaks_ReturnsCleanReport()
    {
        GlResourceLeakDetector detector = new();

        GlCommandRecord baseline = new()
        {
            BufferAllocations = 10,
            BufferDeletions = 5,
            VertexArrayAllocations = 3,
        };
        GlCommandRecord current = new()
        {
            BufferAllocations = 20,
            BufferDeletions = 15,  // Same net = no new leaks
            VertexArrayAllocations = 6,  // +3 allocations but we have no delete tracking
        };

        detector.StartBaseline(baseline, 1000);
        GlLeakReport report = detector.GetLeakReport(current, 1000);

        // Net buffers: (20-15) - (10-5) = 5 - 5 = 0
        Assert.Equal(0, report.BufferLeaks);
    }

    [Fact]
    public void GlResourceLeakDetector_WithBufferLeaks_ReportsCorrectCount()
    {
        GlResourceLeakDetector detector = new();

        GlCommandRecord baseline = new()
        {
            BufferAllocations = 10,
            BufferDeletions = 8,  // Net: 2
        };
        GlCommandRecord current = new()
        {
            BufferAllocations = 25,
            BufferDeletions = 18,  // Net: 7
        };

        detector.StartBaseline(baseline, 0);
        GlLeakReport report = detector.GetLeakReport(current, 0);

        // Delta: (25-18) - (10-8) = 7 - 2 = 5 leaked buffers
        Assert.Equal(5, report.BufferLeaks);
        Assert.True(report.HasLeaks);
    }

    [Fact]
    public void GlResourceLeakDetector_WithVAOLeaks_ReportsCorrectCount()
    {
        GlResourceLeakDetector detector = new();

        GlCommandRecord baseline = new()
        {
            VertexArrayAllocations = 5,
        };
        GlCommandRecord current = new()
        {
            VertexArrayAllocations = 12,
        };

        detector.StartBaseline(baseline, 0);
        GlLeakReport report = detector.GetLeakReport(current, 0);

        // VAO deletions not tracked, so leak = 12 - 5 = 7
        Assert.Equal(7, report.VAOLeaks);
        Assert.True(report.HasLeaks);
    }

    [Fact]
    public void GlResourceLeakDetector_WithMemoryGrowth_ReportsGrowthBytes()
    {
        GlResourceLeakDetector detector = new();

        detector.StartBaseline(GlCommandRecord.Empty, 1000);
        GlLeakReport report = detector.GetLeakReport(GlCommandRecord.Empty, 5000);

        Assert.Equal(4000, report.UnmanagedGrowthBytes);
    }

    [Fact]
    public void GlResourceLeakDetector_Reset_ClearsState()
    {
        GlResourceLeakDetector detector = new();
        detector.StartBaseline(GlCommandRecord.Empty, 0);
        Assert.True(detector.IsTracking);

        detector.Reset();

        Assert.False(detector.IsTracking);
        Assert.Equal(GlCommandRecord.Empty, detector.Baseline);
    }

    [Fact]
    public void GlResourceLeakDetector_NegativeDelta_ReportsZeroLeaks()
    {
        GlResourceLeakDetector detector = new();

        // More deletions than allocations in the window = cleanup happened
        GlCommandRecord baseline = new()
        {
            BufferAllocations = 20,
            BufferDeletions = 10,  // Net: 10 outstanding
        };
        GlCommandRecord current = new()
        {
            BufferAllocations = 22,
            BufferDeletions = 20,  // Net: 2 outstanding
        };

        detector.StartBaseline(baseline, 0);
        GlLeakReport report = detector.GetLeakReport(current, 0);

        // Delta: (22-20) - (20-10) = 2 - 10 = -8, clamped to 0
        Assert.Equal(0, report.BufferLeaks);
        Assert.False(report.HasLeaks);
    }

    // -------------------------------------------------------------------------
    // PharosAssert.NoGlLeaks tests
    // -------------------------------------------------------------------------

    [Fact]
    public void NoGlLeaks_WithCleanReport_DoesNotThrow()
    {
        GlLeakReport report = GlLeakReport.Empty;

        PharosAssert.NoGlLeaks(report);  // Should not throw
    }

    [Fact]
    public void NoGlLeaks_WithLeaks_ThrowsWithDetails()
    {
        GlLeakReport report = new()
        {
            BufferLeaks = 3,
            VAOLeaks = 2,
        };

        var ex = Assert.Throws<PharosAssertException>(() => PharosAssert.NoGlLeaks(report));
        Assert.Contains("Buffers: 3", ex.Message);
        Assert.Contains("VAOs: 2", ex.Message);
        Assert.Contains("5 leaked resources", ex.Message);
    }

    [Fact]
    public void NoGlLeaks_NullReport_ThrowsArgumentNull()
    {
        Assert.Throws<ArgumentNullException>(() => PharosAssert.NoGlLeaks(null!));
    }

    // -------------------------------------------------------------------------
    // PharosAssert.MemoryGrowthBelow tests
    // -------------------------------------------------------------------------

    [Fact]
    public void MemoryGrowthBelow_WithinThreshold_DoesNotThrow()
    {
        PharosAssert.MemoryGrowthBelow(500, 1000);  // Should not throw
    }

    [Fact]
    public void MemoryGrowthBelow_ExactlyAtThreshold_DoesNotThrow()
    {
        PharosAssert.MemoryGrowthBelow(1000, 1000);  // Should not throw
    }

    [Fact]
    public void MemoryGrowthBelow_AboveThreshold_ThrowsWithFormattedSize()
    {
        var ex = Assert.Throws<PharosAssertException>(() =>
            PharosAssert.MemoryGrowthBelow(2 * 1024 * 1024, 1024 * 1024));

        Assert.Contains("Memory growth exceeds threshold", ex.Message);
        Assert.Contains("2.00 MB", ex.Message);
        Assert.Contains("1.00 MB", ex.Message);
    }

    [Fact]
    public void MemoryGrowthBelow_NegativeGrowth_DoesNotThrow()
    {
        PharosAssert.MemoryGrowthBelow(-500, 1000);  // Shrinkage is fine
    }

    // -------------------------------------------------------------------------
    // PharosAssert.GlLeaksBelow tests
    // -------------------------------------------------------------------------

    [Fact]
    public void GlLeaksBelow_AllWithinThresholds_DoesNotThrow()
    {
        GlLeakReport report = new()
        {
            BufferLeaks = 1,
            TextureLeaks = 2,
            VAOLeaks = 1,
        };

        PharosAssert.GlLeaksBelow(report, maxBufferLeaks: 5, maxTextureLeaks: 5, maxVaoLeaks: 5);
    }

    [Fact]
    public void GlLeaksBelow_BuffersExceed_ThrowsWithDetail()
    {
        GlLeakReport report = new() { BufferLeaks = 10 };

        var ex = Assert.Throws<PharosAssertException>(() =>
            PharosAssert.GlLeaksBelow(report, maxBufferLeaks: 5, maxTextureLeaks: 10, maxVaoLeaks: 10));

        Assert.Contains("Buffers: 10 > 5", ex.Message);
    }

    [Fact]
    public void GlLeaksBelow_MultipleViolations_ReportsAll()
    {
        GlLeakReport report = new()
        {
            BufferLeaks = 10,
            TextureLeaks = 8,
            VAOLeaks = 6,
        };

        var ex = Assert.Throws<PharosAssertException>(() =>
            PharosAssert.GlLeaksBelow(report, maxBufferLeaks: 5, maxTextureLeaks: 5, maxVaoLeaks: 5));

        Assert.Contains("Buffers: 10 > 5", ex.Message);
        Assert.Contains("Textures: 8 > 5", ex.Message);
        Assert.Contains("VAOs: 6 > 5", ex.Message);
    }
}
