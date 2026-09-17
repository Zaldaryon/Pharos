using OpenTK.Graphics.OpenGL4;
using Xunit;
using Zaldaryon.Pharos.Graphics;

namespace Zaldaryon.Pharos.Tests;

/// <summary>
/// Unit tests for <see cref="GlCommandProxy"/> and <see cref="GlCommandRecord"/>.
/// These tests exercise the proxy directly without a live headless client because
/// the GL Harmony patches operate on static methods and can be installed and removed
/// in isolation.
/// </summary>
public sealed class GlCommandProxyTests : IDisposable
{
    private readonly GlCommandProxy _proxy = new();

    public void Dispose()
    {
        _proxy.Disable();
    }

    [Fact]
    public void Proxy_IsDisabledByDefault()
    {
        Assert.False(_proxy.IsEnabled);
    }

    [Fact]
    public void Snapshot_WhenDisabled_ReturnsZeroCounts()
    {
        GlCommandRecord record = _proxy.Snapshot();

        Assert.Equal(0, record.DrawCalls);
        Assert.Equal(0, record.MultiDrawCalls);
        Assert.Equal(0, record.IndirectDrawCalls);
        Assert.Equal(0, record.BufferAllocations);
        Assert.Equal(0, record.BufferDeletions);
        Assert.Equal(0, record.VertexArrayAllocations);
        Assert.Equal(0, record.Errors);
        Assert.Equal(0, record.TotalDrawCalls);
    }

    [Fact]
    public void Enable_SetsIsEnabledTrue()
    {
        _proxy.Enable();

        Assert.True(_proxy.IsEnabled);
    }

    [Fact]
    public void Disable_AfterEnable_SetsIsEnabledFalse()
    {
        _proxy.Enable();
        _proxy.Disable();

        Assert.False(_proxy.IsEnabled);
    }

    [Fact]
    public void Enable_IsIdempotent()
    {
        _proxy.Enable();
        _proxy.Enable(); // second call should not throw

        Assert.True(_proxy.IsEnabled);
    }

    [Fact]
    public void Disable_WhenAlreadyDisabled_DoesNotThrow()
    {
        // Should not throw
        _proxy.Disable();

        Assert.False(_proxy.IsEnabled);
    }

    [Fact]
    public void Reset_ZerosAllCounters()
    {
        _proxy.Enable();

        // Simulate counts by calling internal helpers directly
        GlCommandProxy.OnDrawCall();
        GlCommandProxy.OnDrawCall();
        GlCommandProxy.OnMultiDrawCall();
        GlCommandProxy.OnIndirectDrawCall();
        GlCommandProxy.OnBufferAllocated();
        GlCommandProxy.OnBufferDeleted();
        GlCommandProxy.OnVertexArrayAllocated();
        GlCommandProxy.OnError();

        _proxy.Reset();
        GlCommandRecord record = _proxy.Snapshot();

        Assert.Equal(0, record.DrawCalls);
        Assert.Equal(0, record.MultiDrawCalls);
        Assert.Equal(0, record.IndirectDrawCalls);
        Assert.Equal(0, record.BufferAllocations);
        Assert.Equal(0, record.BufferDeletions);
        Assert.Equal(0, record.VertexArrayAllocations);
        Assert.Equal(0, record.Errors);
    }

    [Fact]
    public void Snapshot_CapturesDrawCallCounts()
    {
        _proxy.Enable();

        GlCommandProxy.OnDrawCall();
        GlCommandProxy.OnDrawCall();
        GlCommandProxy.OnDrawCall();

        GlCommandRecord record = _proxy.Snapshot();

        Assert.Equal(3, record.DrawCalls);
    }

    [Fact]
    public void Snapshot_CapturesMultiDrawCounts()
    {
        _proxy.Enable();

        GlCommandProxy.OnMultiDrawCall();
        GlCommandProxy.OnMultiDrawCall();

        GlCommandRecord record = _proxy.Snapshot();

        Assert.Equal(2, record.MultiDrawCalls);
    }

    [Fact]
    public void Snapshot_CapturesIndirectDrawCounts()
    {
        _proxy.Enable();

        GlCommandProxy.OnIndirectDrawCall();

        GlCommandRecord record = _proxy.Snapshot();

        Assert.Equal(1, record.IndirectDrawCalls);
    }

    [Fact]
    public void Snapshot_CapturesBufferAllocationAndDeletion()
    {
        _proxy.Enable();

        GlCommandProxy.OnBufferAllocated();
        GlCommandProxy.OnBufferAllocated();
        GlCommandProxy.OnBufferAllocated();
        GlCommandProxy.OnBufferDeleted();

        GlCommandRecord record = _proxy.Snapshot();

        Assert.Equal(3, record.BufferAllocations);
        Assert.Equal(1, record.BufferDeletions);
    }

    [Fact]
    public void Snapshot_CapturesVertexArrayAllocations()
    {
        _proxy.Enable();

        GlCommandProxy.OnVertexArrayAllocated();
        GlCommandProxy.OnVertexArrayAllocated();

        GlCommandRecord record = _proxy.Snapshot();

        Assert.Equal(2, record.VertexArrayAllocations);
    }

    [Fact]
    public void Snapshot_CapturesErrors()
    {
        _proxy.Enable();

        GlCommandProxy.OnError();
        GlCommandProxy.OnError();

        GlCommandRecord record = _proxy.Snapshot();

        Assert.Equal(2, record.Errors);
    }

    [Fact]
    public void TotalDrawCalls_SumsAllDrawVariants()
    {
        _proxy.Enable();

        GlCommandProxy.OnDrawCall();          // 1
        GlCommandProxy.OnDrawCall();          // 2
        GlCommandProxy.OnMultiDrawCall();     // 1
        GlCommandProxy.OnIndirectDrawCall();  // 1

        GlCommandRecord record = _proxy.Snapshot();

        Assert.Equal(2, record.DrawCalls);
        Assert.Equal(1, record.MultiDrawCalls);
        Assert.Equal(1, record.IndirectDrawCalls);
        Assert.Equal(4, record.TotalDrawCalls);
    }

    [Fact]
    public void Snapshot_AfterDisable_StillReturnsLastCounts()
    {
        _proxy.Enable();

        GlCommandProxy.OnDrawCall();
        GlCommandProxy.OnDrawCall();

        _proxy.Disable();

        GlCommandRecord record = _proxy.Snapshot();

        // Counters are not cleared on Disable
        Assert.Equal(2, record.DrawCalls);
    }

    [Fact]
    public void GlCommandRecord_Empty_HasAllZeros()
    {
        GlCommandRecord empty = GlCommandRecord.Empty;

        Assert.Equal(0, empty.DrawCalls);
        Assert.Equal(0, empty.MultiDrawCalls);
        Assert.Equal(0, empty.IndirectDrawCalls);
        Assert.Equal(0, empty.BufferAllocations);
        Assert.Equal(0, empty.BufferDeletions);
        Assert.Equal(0, empty.VertexArrayAllocations);
        Assert.Equal(0, empty.Errors);
        Assert.Equal(0, empty.TotalDrawCalls);
    }

    [Fact]
    public void GlCommandRecord_IsImmutable_AfterSnapshot()
    {
        _proxy.Enable();

        GlCommandProxy.OnDrawCall();
        GlCommandRecord snapshot1 = _proxy.Snapshot();

        GlCommandProxy.OnDrawCall();
        GlCommandRecord snapshot2 = _proxy.Snapshot();

        // snapshot1 must not be mutated by the second draw call
        Assert.Equal(1, snapshot1.DrawCalls);
        Assert.Equal(2, snapshot2.DrawCalls);
    }
}
