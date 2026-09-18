using Xunit;
using Zaldaryon.Pharos.Assertions;
using Zaldaryon.Pharos.Graphics;

namespace Zaldaryon.Pharos.Tests.Scenarios;

/// <summary>
/// Scenario tests for indirect draw collapse functionality (Issue #119).
/// Validates that draw calls are efficiently collapsed into indirect dispatches
/// and that the fallback path is correctly detected when indirect drawing is disabled.
/// Uses IndirectDrawInspector with synthetic data simulation.
/// </summary>
[Collection("IndirectDrawInspector")]
public sealed class IndirectDrawScenarioTests : IDisposable
{
    private readonly IndirectDrawInspector _inspector = new();

    public void Dispose()
    {
        _inspector.Disable();
    }

    // -------------------------------------------------------------------------
    // Synthetic Collapse Tests
    // -------------------------------------------------------------------------

    [Fact]
    public void IndirectDraw_SyntheticCollapse_MeetsThresholds()
    {
        // Arrange: Enable inspector and simulate 25 direct draws + 3 indirect dispatches
        _inspector.Enable();

        // Simulate 25 direct draw calls (the "original" draw calls that would be made)
        for (int i = 0; i < 25; i++)
        {
            IndirectDrawInspector.OnDirectDrawCall();
        }

        // Simulate 3 indirect dispatches that batch the draws
        IndirectDrawInspector.OnIndirectDispatch(10); // Batches 10 commands
        IndirectDrawInspector.OnIndirectDispatch(10); // Batches 10 commands
        IndirectDrawInspector.OnIndirectDispatch(5);  // Batches 5 commands

        // Act: Take snapshot
        IndirectDrawStats stats = _inspector.Snapshot();

        // Assert: Should meet collapse thresholds
        // minOriginalDrawCalls: 20, maxDispatchedIndirectCalls: 5
        PharosAssert.DrawCallsCollapsed(stats, minOriginalDrawCalls: 20, maxDispatchedIndirectCalls: 5);

        // Additional verification
        Assert.Equal(25, stats.DirectDrawCalls);
        Assert.Equal(3, stats.IndirectDispatchCount);
        Assert.Equal(25, stats.TotalCommandCount);
    }

    [Fact]
    public void IndirectDraw_CollapseRatio_AtLeast75Percent()
    {
        // Arrange: 100 direct draws collapsed into 4 indirect dispatches
        // Collapse ratio = 100 / 4 = 25.0 (way above 0.75)
        _inspector.Enable();

        for (int i = 0; i < 100; i++)
        {
            IndirectDrawInspector.OnDirectDrawCall();
        }

        IndirectDrawInspector.OnIndirectDispatch(25);
        IndirectDrawInspector.OnIndirectDispatch(25);
        IndirectDrawInspector.OnIndirectDispatch(25);
        IndirectDrawInspector.OnIndirectDispatch(25);

        // Act
        IndirectDrawStats stats = _inspector.Snapshot();

        // Assert: Collapse ratio should be at least 0.75
        // CollapseRatio = DirectDrawCalls / IndirectDispatchCount = 100 / 4 = 25.0
        PharosAssert.CollapseRatioAtLeast(stats, minCollapseRatio: 0.75);

        // Verify the actual ratio
        Assert.True(stats.CollapseRatio >= 0.75,
            $"Expected collapse ratio >= 0.75, got {stats.CollapseRatio:F2}");
        Assert.Equal(25.0, stats.CollapseRatio);
    }

    [Fact]
    public void IndirectDraw_FallbackMode_NoIndirectDispatches()
    {
        // Arrange: 30 direct draws with 0 indirect dispatches (fallback/disabled mode)
        _inspector.Enable();

        for (int i = 0; i < 30; i++)
        {
            IndirectDrawInspector.OnDirectDrawCall();
        }
        // No indirect dispatches - simulating fallback mode

        // Act
        IndirectDrawStats stats = _inspector.Snapshot();

        // Assert: Should pass IndirectDrawDisabled check
        PharosAssert.IndirectDrawDisabled(stats, minDirectDrawCalls: 10);

        // Additional verification
        Assert.Equal(30, stats.DirectDrawCalls);
        Assert.Equal(0, stats.IndirectDispatchCount);
        Assert.True(stats.IsFallbackMode);
        Assert.False(stats.HasIndirectDraws);
    }

    // -------------------------------------------------------------------------
    // Negative/Threshold Tests
    // -------------------------------------------------------------------------

    [Fact]
    public void IndirectDraw_BelowMinOriginal_Throws()
    {
        // Arrange: Only 10 direct draws (below minOriginalDrawCalls: 20)
        _inspector.Enable();

        for (int i = 0; i < 10; i++)
        {
            IndirectDrawInspector.OnDirectDrawCall();
        }
        IndirectDrawInspector.OnIndirectDispatch(5);
        IndirectDrawInspector.OnIndirectDispatch(5);

        // Act & Assert: Should throw because direct draw count is below minimum
        IndirectDrawStats stats = _inspector.Snapshot();

        var ex = Assert.Throws<PharosAssertException>(() =>
            PharosAssert.DrawCallsCollapsed(stats, minOriginalDrawCalls: 20, maxDispatchedIndirectCalls: 5));

        Assert.Contains("at least 20 direct draw calls", ex.Message);
    }

    [Fact]
    public void IndirectDraw_AboveMaxDispatched_Throws()
    {
        // Arrange: 50 direct draws but 10 indirect dispatches (above maxDispatchedIndirectCalls: 5)
        _inspector.Enable();

        for (int i = 0; i < 50; i++)
        {
            IndirectDrawInspector.OnDirectDrawCall();
        }

        // 10 indirect dispatches - more than the maximum allowed
        for (int i = 0; i < 10; i++)
        {
            IndirectDrawInspector.OnIndirectDispatch(5);
        }

        // Act & Assert: Should throw because indirect dispatch count exceeds maximum
        IndirectDrawStats stats = _inspector.Snapshot();

        var ex = Assert.Throws<PharosAssertException>(() =>
            PharosAssert.DrawCallsCollapsed(stats, minOriginalDrawCalls: 20, maxDispatchedIndirectCalls: 5));

        Assert.Contains("at most 5 indirect dispatch calls", ex.Message);
    }

    // -------------------------------------------------------------------------
    // Live GPU Test (Skipped)
    // -------------------------------------------------------------------------

    [Fact(Skip = "Requires live GPU")]
    public void LiveGpu_InjectChunks_CollapsesDrawCalls()
    {
        // This test would exercise the full pipeline:
        // 1. Boot HeadlessClient with GPU
        // 2. Inject chunks via ChunkFixture
        // 3. Enable IndirectDrawInspector
        // 4. Render frame(s)
        // 5. Take snapshot and verify collapse thresholds
        //
        // Placeholder for live integration test that requires GPU access.
        // Implementation would follow the pattern from HeadlessClientBootstrapTests.

        throw new NotImplementedException("Live GPU test requires headless client with GPU access.");
    }

    // -------------------------------------------------------------------------
    // Additional Scenario Tests
    // -------------------------------------------------------------------------

    [Fact]
    public void IndirectDraw_Reset_ClearsAllStats()
    {
        // Arrange
        _inspector.Enable();
        IndirectDrawInspector.OnDirectDrawCall();
        IndirectDrawInspector.OnIndirectDispatch(5);

        // Verify stats are non-zero
        var before = _inspector.Snapshot();
        Assert.Equal(1, before.DirectDrawCalls);
        Assert.Equal(1, before.IndirectDispatchCount);

        // Act: Reset
        _inspector.Reset();

        // Assert: All stats should be zero
        var after = _inspector.Snapshot();
        Assert.Equal(0, after.DirectDrawCalls);
        Assert.Equal(0, after.IndirectDispatchCount);
        Assert.Equal(0, after.TotalCommandCount);
    }

    [Fact]
    public void IndirectDraw_BufferBinding_TracksStats()
    {
        // Arrange
        _inspector.Enable();

        // Simulate buffer bindings
        IndirectDrawInspector.OnBufferBind(bufferId: 1, sizeBytes: 1024);
        IndirectDrawInspector.OnBufferBind(bufferId: 2, sizeBytes: 2048);
        IndirectDrawInspector.OnBufferBind(bufferId: 3, sizeBytes: 512);

        // Act
        var stats = _inspector.Snapshot();
        var bindings = _inspector.GetBufferBindings();

        // Assert
        Assert.Equal(3584L, stats.BufferBytesTotal); // 1024 + 2048 + 512
        Assert.Equal(3, bindings.Count);
        Assert.Equal(1, bindings[0].BufferId);
        Assert.Equal(1024L, bindings[0].BufferSizeBytes);
    }

    [Fact]
    public void IndirectDraw_LowCollapseRatio_Throws()
    {
        // Arrange: 10 direct draws with 10 indirect dispatches = ratio of 1.0
        _inspector.Enable();

        for (int i = 0; i < 10; i++)
        {
            IndirectDrawInspector.OnDirectDrawCall();
        }
        for (int i = 0; i < 10; i++)
        {
            IndirectDrawInspector.OnIndirectDispatch(1);
        }

        // Act
        var stats = _inspector.Snapshot();

        // Assert: Should throw because ratio (1.0) is below minimum (5.0)
        var ex = Assert.Throws<PharosAssertException>(() =>
            PharosAssert.CollapseRatioAtLeast(stats, minCollapseRatio: 5.0));

        Assert.Contains("collapse ratio", ex.Message.ToLower());
    }

    [Fact]
    public void IndirectDraw_InstanceTracking_AggregatesCorrectly()
    {
        // Arrange
        _inspector.Enable();

        // Simulate dispatches with instance counts
        IndirectDrawInspector.OnIndirectDispatchWithInstances(drawcount: 10, instanceCount: 100);
        IndirectDrawInspector.OnIndirectDispatchWithInstances(drawcount: 5, instanceCount: 50);

        // Act
        var stats = _inspector.Snapshot();

        // Assert
        Assert.Equal(2, stats.IndirectDispatchCount);
        Assert.Equal(15, stats.TotalCommandCount); // 10 + 5
        Assert.Equal(150, stats.TotalInstanceCount); // 100 + 50
    }
}
