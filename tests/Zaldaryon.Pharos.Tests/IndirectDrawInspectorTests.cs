using Xunit;
using Zaldaryon.Pharos.Assertions;
using Zaldaryon.Pharos.Core;
using Zaldaryon.Pharos.Graphics;

namespace Zaldaryon.Pharos.Tests;

/// <summary>
/// Unit tests for <see cref="IndirectDrawInspector"/>, <see cref="IndirectDrawStats"/>,
/// <see cref="IndirectCommandBufferSnapshot"/>, and <see cref="PharosAssert"/> batching assertions.
/// These tests exercise the inspector directly without a live headless client because
/// the Harmony patches operate on static methods and can be installed and removed in isolation.
/// </summary>
[Collection("IndirectDrawInspector")]
public sealed class IndirectDrawInspectorTests : IDisposable
{
    private readonly IndirectDrawInspector _inspector = new();

    public void Dispose()
    {
        _inspector.Disable();
    }

    [Fact]
    public void Inspector_IsDisabledByDefault()
    {
        Assert.False(_inspector.IsEnabled);
    }

    [Fact]
    public void Enable_SetsIsEnabledTrue()
    {
        _inspector.Enable();

        Assert.True(_inspector.IsEnabled);
    }

    [Fact]
    public void Disable_AfterEnable_SetsIsEnabledFalse()
    {
        _inspector.Enable();
        _inspector.Disable();

        Assert.False(_inspector.IsEnabled);
    }

    [Fact]
    public void Snapshot_WhenDisabled_ReturnsEmpty()
    {
        IndirectDrawStats stats = _inspector.Snapshot();

        Assert.Equal(0, stats.IndirectDispatchCount);
        Assert.Equal(0, stats.TotalCommandCount);
        Assert.Equal(0, stats.DirectDrawCalls);
        Assert.Equal(0L, stats.BufferBytesTotal);
    }

    [Fact]
    public void Snapshot_CapturesIndirectDispatchCount()
    {
        _inspector.Enable();

        IndirectDrawInspector.OnIndirectDispatch(10);
        IndirectDrawInspector.OnIndirectDispatch(5);

        IndirectDrawStats stats = _inspector.Snapshot();

        Assert.Equal(2, stats.IndirectDispatchCount);
    }

    [Fact]
    public void Snapshot_CapturesTotalCommandCount()
    {
        _inspector.Enable();

        IndirectDrawInspector.OnIndirectDispatch(10);
        IndirectDrawInspector.OnIndirectDispatch(5);

        IndirectDrawStats stats = _inspector.Snapshot();

        Assert.Equal(15, stats.TotalCommandCount);
    }

    [Fact]
    public void Snapshot_CapturesDirectDrawCalls()
    {
        _inspector.Enable();

        IndirectDrawInspector.OnDirectDrawCall();
        IndirectDrawInspector.OnDirectDrawCall();
        IndirectDrawInspector.OnDirectDrawCall();

        IndirectDrawStats stats = _inspector.Snapshot();

        Assert.Equal(3, stats.DirectDrawCalls);
    }

    [Fact]
    public void BufferBindings_TracksMultipleBindings()
    {
        _inspector.Enable();

        IndirectDrawInspector.OnBufferBind(1, 200);
        IndirectDrawInspector.OnBufferBind(2, 400);

        var bindings = _inspector.GetBufferBindings();

        Assert.Equal(2, bindings.Count);
        Assert.Equal(1, bindings[0].BufferId);
        Assert.Equal(200L, bindings[0].BufferSizeBytes);
        Assert.Equal(2, bindings[1].BufferId);
        Assert.Equal(400L, bindings[1].BufferSizeBytes);
    }

    [Fact]
    public void Reset_ClearsAllCounters()
    {
        _inspector.Enable();

        IndirectDrawInspector.OnIndirectDispatch(10);
        IndirectDrawInspector.OnDirectDrawCall();
        IndirectDrawInspector.OnBufferBind(1, 100);

        _inspector.Reset();
        IndirectDrawStats stats = _inspector.Snapshot();

        Assert.Equal(0, stats.IndirectDispatchCount);
        Assert.Equal(0, stats.TotalCommandCount);
        Assert.Equal(0, stats.DirectDrawCalls);
        Assert.Equal(0L, stats.BufferBytesTotal);
        Assert.Empty(_inspector.GetBufferBindings());
    }

    [Fact]
    public void IndirectDrawStats_CollapseRatio_ComputesCorrectly()
    {
        _inspector.Enable();

        // Simulate 100 direct draw calls collapsed into 2 indirect dispatches
        for (int i = 0; i < 100; i++)
        {
            IndirectDrawInspector.OnDirectDrawCall();
        }
        IndirectDrawInspector.OnIndirectDispatch(50);
        IndirectDrawInspector.OnIndirectDispatch(50);

        IndirectDrawStats stats = _inspector.Snapshot();

        Assert.Equal(100, stats.DirectDrawCalls);
        Assert.Equal(2, stats.IndirectDispatchCount);
        Assert.Equal(50.0, stats.CollapseRatio);
    }

    [Fact]
    public void IndirectDrawStats_CollapseRatio_ReturnsZero_WhenNoIndirectDispatches()
    {
        IndirectDrawStats stats = new() { DirectDrawCalls = 10, IndirectDispatchCount = 0 };

        Assert.Equal(0.0, stats.CollapseRatio);
    }

    [Fact]
    public void IndirectDrawStats_HasIndirectDraws_ReturnsTrue_WhenIndirectDispatchesExist()
    {
        IndirectDrawStats stats = new() { IndirectDispatchCount = 1 };

        Assert.True(stats.HasIndirectDraws);
    }

    [Fact]
    public void IndirectDrawStats_IsFallbackMode_ReturnsTrue_WhenOnlyDirectDraws()
    {
        IndirectDrawStats stats = new() { DirectDrawCalls = 10, IndirectDispatchCount = 0 };

        Assert.True(stats.IsFallbackMode);
    }

    [Fact]
    public void PharosAssert_DrawCallsCollapsed_PassesWhenValid()
    {
        IndirectDrawStats stats = new()
        {
            DirectDrawCalls = 100,
            IndirectDispatchCount = 2,
        };

        // Should not throw
        PharosAssert.DrawCallsCollapsed(stats, minOriginalDrawCalls: 50, maxDispatchedIndirectCalls: 5);
    }

    [Fact]
    public void PharosAssert_DrawCallsCollapsed_ThrowsWhenBelowMinOriginal()
    {
        IndirectDrawStats stats = new()
        {
            DirectDrawCalls = 10,
            IndirectDispatchCount = 2,
        };

        var ex = Assert.Throws<PharosAssertException>(() =>
            PharosAssert.DrawCallsCollapsed(stats, minOriginalDrawCalls: 50, maxDispatchedIndirectCalls: 5));

        Assert.Contains("at least 50 direct draw calls", ex.Message);
    }

    [Fact]
    public void PharosAssert_DrawCallsCollapsed_ThrowsWhenAboveMaxDispatched()
    {
        IndirectDrawStats stats = new()
        {
            DirectDrawCalls = 100,
            IndirectDispatchCount = 10,
        };

        var ex = Assert.Throws<PharosAssertException>(() =>
            PharosAssert.DrawCallsCollapsed(stats, minOriginalDrawCalls: 50, maxDispatchedIndirectCalls: 5));

        Assert.Contains("at most 5 indirect dispatch calls", ex.Message);
    }

    [Fact]
    public void PharosAssert_IndirectDrawDisabled_PassesWhenNoIndirect()
    {
        IndirectDrawStats stats = new()
        {
            DirectDrawCalls = 50,
            IndirectDispatchCount = 0,
        };

        // Should not throw
        PharosAssert.IndirectDrawDisabled(stats, minDirectDrawCalls: 10);
    }

    [Fact]
    public void PharosAssert_IndirectDrawDisabled_ThrowsWhenIndirectUsed()
    {
        IndirectDrawStats stats = new()
        {
            DirectDrawCalls = 50,
            IndirectDispatchCount = 1,
        };

        var ex = Assert.Throws<PharosAssertException>(() =>
            PharosAssert.IndirectDrawDisabled(stats, minDirectDrawCalls: 10));

        Assert.Contains("no indirect dispatch calls", ex.Message);
    }

    [Fact]
    public void IndirectCommandBufferSnapshot_EstimatedCommandCount_ComputesCorrectly()
    {
        IndirectCommandBufferSnapshot snapshot = new()
        {
            BufferId = 1,
            BufferSizeBytes = 200, // 200 bytes / 20 bytes per command = 10 commands
        };

        Assert.Equal(10, snapshot.EstimatedCommandCount);
    }

    [Fact]
    public void IndirectCommandBufferSnapshot_EstimatedCommandCount_ReturnsZero_WhenTooSmall()
    {
        IndirectCommandBufferSnapshot snapshot = new()
        {
            BufferId = 1,
            BufferSizeBytes = 19, // Less than one command
        };

        Assert.Equal(0, snapshot.EstimatedCommandCount);
    }
}

/// <summary>
/// Collection definition to serialize tests that use static _active field in IndirectDrawInspector.
/// </summary>
[CollectionDefinition("IndirectDrawInspector")]
public class IndirectDrawInspectorCollection : ICollectionFixture<IndirectDrawInspectorFixture>
{
}

public class IndirectDrawInspectorFixture : IDisposable
{
    public void Dispose()
    {
        // Cleanup handled per-test
    }
}
