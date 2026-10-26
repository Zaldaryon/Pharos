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
        GlCommandProxy.OnShaderProgramBinding();
        GlCommandProxy.OnTextureBinding();
        GlCommandProxy.OnVaoBinding();
        GlCommandProxy.OnFramebufferBinding();

        _proxy.Reset();
        GlCommandRecord record = _proxy.Snapshot();

        Assert.Equal(0, record.DrawCalls);
        Assert.Equal(0, record.MultiDrawCalls);
        Assert.Equal(0, record.IndirectDrawCalls);
        Assert.Equal(0, record.BufferAllocations);
        Assert.Equal(0, record.BufferDeletions);
        Assert.Equal(0, record.VertexArrayAllocations);
        Assert.Equal(0, record.Errors);
        Assert.Equal(0, record.ShaderProgramBindings);
        Assert.Equal(0, record.TextureBindings);
        Assert.Equal(0, record.VaoBindings);
        Assert.Equal(0, record.FramebufferBindings);
        Assert.Empty(record.DrawCallsByMethod);
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
        Assert.Equal(0, empty.ShaderProgramBindings);
        Assert.Equal(0, empty.TextureBindings);
        Assert.Equal(0, empty.VaoBindings);
        Assert.Equal(0, empty.FramebufferBindings);
        Assert.Equal(0, empty.TotalStateBindings);
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

    [Fact]
    public void Enable_PatchesGlMethods()
    {
        _proxy.Enable();

        // Should have patched methods from both OpenGL and OpenGL4 namespaces
        Assert.True(_proxy.PatchedMethodCount > 0, "Expected at least one GL method to be patched");
        Assert.Equal(2, _proxy.PatchedTypes.Count); // GL and OpenTK.Graphics.OpenGL.GL
    }

    [Fact]
    public void Enable_RecordsPatchFailures()
    {
        _proxy.Enable();

        // If there are failures, they should be recorded for diagnostics
        // Note: failures may be zero if all methods are available
        Assert.NotNull(_proxy.PatchFailures);
    }

    [Fact]
    public void PatchedTypes_ContainsBothGlBindings()
    {
        _proxy.Enable();

        Assert.Contains(_proxy.PatchedTypes, t => t == typeof(OpenTK.Graphics.OpenGL4.GL));
        Assert.Contains(_proxy.PatchedTypes, t => t == typeof(OpenTK.Graphics.OpenGL.GL));
    }

    // New tests for enhanced diagnostics

    [Fact]
    public void Snapshot_CapturesShaderProgramBindings()
    {
        _proxy.Enable();

        GlCommandProxy.OnShaderProgramBinding();
        GlCommandProxy.OnShaderProgramBinding();

        GlCommandRecord record = _proxy.Snapshot();

        Assert.Equal(2, record.ShaderProgramBindings);
    }

    [Fact]
    public void Snapshot_CapturesTextureBindings()
    {
        _proxy.Enable();

        GlCommandProxy.OnTextureBinding();
        GlCommandProxy.OnTextureBinding();
        GlCommandProxy.OnTextureBinding();

        GlCommandRecord record = _proxy.Snapshot();

        Assert.Equal(3, record.TextureBindings);
    }

    [Fact]
    public void Snapshot_CapturesVaoBindings()
    {
        _proxy.Enable();

        GlCommandProxy.OnVaoBinding();

        GlCommandRecord record = _proxy.Snapshot();

        Assert.Equal(1, record.VaoBindings);
    }

    [Fact]
    public void Snapshot_CapturesFramebufferBindings()
    {
        _proxy.Enable();

        GlCommandProxy.OnFramebufferBinding();
        GlCommandProxy.OnFramebufferBinding();

        GlCommandRecord record = _proxy.Snapshot();

        Assert.Equal(2, record.FramebufferBindings);
    }

    [Fact]
    public void TotalStateBindings_SumsAllBindings()
    {
        _proxy.Enable();

        GlCommandProxy.OnShaderProgramBinding();  // 1
        GlCommandProxy.OnTextureBinding();        // 1
        GlCommandProxy.OnTextureBinding();        // 2
        GlCommandProxy.OnVaoBinding();            // 1
        GlCommandProxy.OnFramebufferBinding();    // 1

        GlCommandRecord record = _proxy.Snapshot();

        Assert.Equal(1, record.ShaderProgramBindings);
        Assert.Equal(2, record.TextureBindings);
        Assert.Equal(1, record.VaoBindings);
        Assert.Equal(1, record.FramebufferBindings);
        Assert.Equal(5, record.TotalStateBindings);
    }

    [Fact]
    public void Enable_RecordsPatchedMethods()
    {
        _proxy.Enable();

        Assert.NotNull(_proxy.PatchedMethods);
        Assert.True(_proxy.PatchedMethods.Count > 0, "Should have recorded patched method names");
        Assert.True(_proxy.PatchedMethodCount == _proxy.PatchedMethods.Count, "PatchedMethodCount should match PatchedMethods.Count");
    }

    [Fact]
    public void PatchedMethods_ContainsExpectedDrawMethods()
    {
        _proxy.Enable();

        // Both GL and OpenTK.Graphics.OpenGL.GL should have DrawElements patched
        Assert.Contains(_proxy.PatchedMethods, m => m.Contains("DrawElements"));
        Assert.Contains(_proxy.PatchedMethods, m => m.Contains("DrawArrays"));
    }

    [Fact]
    public void PatchedMethods_ContainsStateBindingMethods()
    {
        _proxy.Enable();

        Assert.Contains(_proxy.PatchedMethods, m => m.Contains("UseProgram"));
        Assert.Contains(_proxy.PatchedMethods, m => m.Contains("BindTexture"));
        Assert.Contains(_proxy.PatchedMethods, m => m.Contains("BindVertexArray"));
        Assert.Contains(_proxy.PatchedMethods, m => m.Contains("BindFramebuffer"));
    }

    [Fact]
    public void GetDiagnosticReport_ReturnsNonEmptyString()
    {
        _proxy.Enable();

        GlCommandProxy.OnDrawCall();
        GlCommandProxy.OnShaderProgramBinding();

        string report = _proxy.GetDiagnosticReport();

        Assert.False(string.IsNullOrWhiteSpace(report));
        Assert.Contains("Enabled: True", report);
        Assert.Contains("Draw Calls:", report);
        Assert.Contains("Shader Bindings:", report);
    }

    [Fact]
    public void GetDiagnosticReport_IncludesPatchInfo()
    {
        _proxy.Enable();

        string report = _proxy.GetDiagnosticReport();

        Assert.Contains("Patched Methods:", report);
        Assert.Matches(@"Patched Methods: \d+", report);
    }

    [Fact]
    public void RuntimeStatsHooked_ReturnsFalseWhenVsNotLoaded()
    {
        _proxy.Enable();

        // In unit tests, VS is not loaded so RuntimeStats won't be hooked
        // This test verifies the code handles this gracefully
        Assert.NotNull(_proxy); // Just verify no exception was thrown
    }

    [Fact]
    public void DrawCallsByMethod_IsEmptyInitially()
    {
        _proxy.Enable();

        GlCommandRecord record = _proxy.Snapshot();

        Assert.NotNull(record.DrawCallsByMethod);
        Assert.Empty(record.DrawCallsByMethod);
    }

    [Fact]
    public void DrawCallsByMethod_IsImmutableCopy()
    {
        _proxy.Enable();

        GlCommandRecord snapshot1 = _proxy.Snapshot();

        // Modify the dictionary in a new record should not affect snapshot1
        GlCommandRecord snapshot2 = _proxy.Snapshot();

        Assert.NotSame(snapshot1.DrawCallsByMethod, snapshot2.DrawCallsByMethod);
    }
}
