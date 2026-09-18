using Xunit;
using Zaldaryon.Pharos.Assertions;
using Zaldaryon.Pharos.Graphics;

namespace Zaldaryon.Pharos.Tests;

/// <summary>
/// Unit tests for <see cref="RenderScaleInspector"/>, <see cref="RenderScaleSnapshot"/>,
/// and FSR-related <see cref="PharosAssert"/> methods.
/// These tests use synthetic snapshots to validate FSR render scale pipeline assertions
/// without requiring a GPU context.
/// </summary>
[Collection("RenderScaleInspector")]
public sealed class RenderScaleInspectorTests : IDisposable
{
    private readonly RenderScaleInspector _inspector = new();

    public void Dispose()
    {
        _inspector.Disable();
    }

    // -------------------------------------------------------------------------
    // RenderScaleInspector tests
    // -------------------------------------------------------------------------

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
    public void Snapshot_WhenDisabled_ReturnsDefaultValues()
    {
        RenderScaleSnapshot snapshot = _inspector.Snapshot();

        Assert.Equal(1.0f, snapshot.RenderScale);
        Assert.False(snapshot.FsrEnabled);
        Assert.False(snapshot.EasuShaderDispatched);
        Assert.False(snapshot.RcasShaderDispatched);
    }

    [Fact]
    public void Configure_SetsRenderScaleAndDimensions()
    {
        _inspector.Configure(0.75f, 1920, 1080, fsrEnabled: true);
        _inspector.Enable();

        RenderScaleSnapshot snapshot = _inspector.Snapshot();

        Assert.Equal(0.75f, snapshot.RenderScale);
        Assert.Equal(1920, snapshot.DisplayWidth);
        Assert.Equal(1080, snapshot.DisplayHeight);
        Assert.Equal(1440, snapshot.PreUpscaleWidth);  // 1920 * 0.75
        Assert.Equal(810, snapshot.PreUpscaleHeight);  // 1080 * 0.75
        Assert.True(snapshot.FsrEnabled);
    }

    [Fact]
    public void Configure_ClampsRenderScaleToValidRange()
    {
        _inspector.Configure(0.3f, 1920, 1080);
        Assert.Equal(0.5f, _inspector.Snapshot().RenderScale);

        _inspector.Configure(1.5f, 1920, 1080);
        Assert.Equal(1.0f, _inspector.Snapshot().RenderScale);
    }

    [Fact]
    public void SimulateEasuDispatch_SetsEasuDispatched()
    {
        _inspector.Configure(0.5f, 1920, 1080);
        _inspector.Enable();

        _inspector.SimulateEasuDispatch();
        RenderScaleSnapshot snapshot = _inspector.Snapshot();

        Assert.True(snapshot.EasuShaderDispatched);
        Assert.False(snapshot.RcasShaderDispatched);
    }

    [Fact]
    public void SimulateRcasDispatch_SetsRcasDispatched()
    {
        _inspector.Configure(0.5f, 1920, 1080);
        _inspector.Enable();

        _inspector.SimulateRcasDispatch();
        RenderScaleSnapshot snapshot = _inspector.Snapshot();

        Assert.False(snapshot.EasuShaderDispatched);
        Assert.True(snapshot.RcasShaderDispatched);
    }

    [Fact]
    public void Reset_ClearsDispatchCounters()
    {
        _inspector.Configure(0.5f, 1920, 1080);
        _inspector.Enable();

        _inspector.SimulateEasuDispatch();
        _inspector.SimulateRcasDispatch();
        _inspector.Reset();

        RenderScaleSnapshot snapshot = _inspector.Snapshot();

        Assert.False(snapshot.EasuShaderDispatched);
        Assert.False(snapshot.RcasShaderDispatched);
    }

    // -------------------------------------------------------------------------
    // RenderScaleSnapshot tests
    // -------------------------------------------------------------------------

    [Fact]
    public void Snapshot_Empty_HasDefaultValues()
    {
        RenderScaleSnapshot empty = RenderScaleSnapshot.Empty;

        Assert.Equal(1.0f, empty.RenderScale);
        Assert.False(empty.FsrEnabled);
        Assert.Equal(0, empty.PreUpscaleWidth);
        Assert.Equal(0, empty.DisplayWidth);
    }

    [Fact]
    public void Snapshot_CreateSynthetic_ComputesDimensionsCorrectly()
    {
        RenderScaleSnapshot snapshot = RenderScaleSnapshot.CreateSynthetic(
            renderScale: 0.5f,
            displayWidth: 1920,
            displayHeight: 1080);

        Assert.Equal(0.5f, snapshot.RenderScale);
        Assert.Equal(960, snapshot.PreUpscaleWidth);   // 1920 * 0.5
        Assert.Equal(540, snapshot.PreUpscaleHeight);  // 1080 * 0.5
        Assert.Equal(1920, snapshot.DisplayWidth);
        Assert.Equal(1080, snapshot.DisplayHeight);
        Assert.True(snapshot.FsrEnabled);
        Assert.True(snapshot.EasuShaderDispatched);
        Assert.True(snapshot.RcasShaderDispatched);
    }

    [Fact]
    public void Snapshot_FsrPipelineComplete_TrueWhenAllConditionsMet()
    {
        RenderScaleSnapshot complete = RenderScaleSnapshot.CreateSynthetic(0.5f, 1920, 1080);
        Assert.True(complete.FsrPipelineComplete);

        RenderScaleSnapshot noEasu = RenderScaleSnapshot.CreateSynthetic(
            0.5f, 1920, 1080, easuDispatched: false);
        Assert.False(noEasu.FsrPipelineComplete);

        RenderScaleSnapshot disabled = RenderScaleSnapshot.CreateSynthetic(
            0.5f, 1920, 1080, fsrEnabled: false);
        Assert.False(disabled.FsrPipelineComplete);
    }

    [Fact]
    public void Snapshot_IsFallbackMode_TrueWhenFsrDisabledOrNativeScale()
    {
        RenderScaleSnapshot disabled = RenderScaleSnapshot.CreateSynthetic(
            0.5f, 1920, 1080, fsrEnabled: false);
        Assert.True(disabled.IsFallbackMode);

        RenderScaleSnapshot native = RenderScaleSnapshot.CreateSynthetic(
            1.0f, 1920, 1080, fsrEnabled: true);
        Assert.True(native.IsFallbackMode);

        RenderScaleSnapshot active = RenderScaleSnapshot.CreateSynthetic(
            0.75f, 1920, 1080, fsrEnabled: true);
        Assert.False(active.IsFallbackMode);
    }

    [Fact]
    public void Snapshot_UpscaleRatio_ComputedCorrectly()
    {
        RenderScaleSnapshot snapshot = RenderScaleSnapshot.CreateSynthetic(0.5f, 1920, 1080);

        // 1920 / 960 = 2.0
        Assert.Equal(2.0f, snapshot.UpscaleRatio);
    }

    [Fact]
    public void Snapshot_ExpectedDimensions_MatchRenderScale()
    {
        var snapshot = new RenderScaleSnapshot
        {
            RenderScale = 0.75f,
            DisplayWidth = 1920,
            DisplayHeight = 1080,
        };

        Assert.Equal(1440, snapshot.ExpectedPreUpscaleWidth);
        Assert.Equal(810, snapshot.ExpectedPreUpscaleHeight);
    }

    // -------------------------------------------------------------------------
    // PharosAssert FSR assertion tests
    // -------------------------------------------------------------------------

    [Fact]
    public void PharosAssert_RenderScaleActive_PassesWhenValid()
    {
        RenderScaleSnapshot snapshot = RenderScaleSnapshot.CreateSynthetic(0.5f, 1920, 1080);

        // Should not throw
        PharosAssert.RenderScaleActive(snapshot, expectedScale: 0.5f);
    }

    [Fact]
    public void PharosAssert_RenderScaleActive_PassesWithinTolerance()
    {
        RenderScaleSnapshot snapshot = RenderScaleSnapshot.CreateSynthetic(0.505f, 1920, 1080);

        // Should not throw with default tolerance of 0.01
        PharosAssert.RenderScaleActive(snapshot, expectedScale: 0.5f);
    }

    [Fact]
    public void PharosAssert_RenderScaleActive_ThrowsWhenScaleMismatch()
    {
        RenderScaleSnapshot snapshot = RenderScaleSnapshot.CreateSynthetic(0.5f, 1920, 1080);

        var ex = Assert.Throws<PharosAssertException>(() =>
            PharosAssert.RenderScaleActive(snapshot, expectedScale: 0.75f));

        Assert.Contains("Expected render scale 0.75", ex.Message);
    }

    [Fact]
    public void PharosAssert_RenderScaleActive_ThrowsWhenExpectedScaleOutOfRange()
    {
        RenderScaleSnapshot snapshot = RenderScaleSnapshot.CreateSynthetic(0.5f, 1920, 1080);

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            PharosAssert.RenderScaleActive(snapshot, expectedScale: 0.3f));

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            PharosAssert.RenderScaleActive(snapshot, expectedScale: 1.5f));
    }

    [Fact]
    public void PharosAssert_FsrPipelineExecuted_PassesWhenComplete()
    {
        RenderScaleSnapshot snapshot = RenderScaleSnapshot.CreateSynthetic(0.5f, 1920, 1080);

        // Should not throw
        PharosAssert.FsrPipelineExecuted(snapshot);
    }

    [Fact]
    public void PharosAssert_FsrPipelineExecuted_ThrowsWhenFsrDisabled()
    {
        RenderScaleSnapshot snapshot = RenderScaleSnapshot.CreateSynthetic(
            0.5f, 1920, 1080, fsrEnabled: false);

        var ex = Assert.Throws<PharosAssertException>(() =>
            PharosAssert.FsrPipelineExecuted(snapshot));

        Assert.Contains("FSR is not enabled", ex.Message);
    }

    [Fact]
    public void PharosAssert_FsrPipelineExecuted_ThrowsWhenEasuNotDispatched()
    {
        RenderScaleSnapshot snapshot = RenderScaleSnapshot.CreateSynthetic(
            0.5f, 1920, 1080, easuDispatched: false);

        var ex = Assert.Throws<PharosAssertException>(() =>
            PharosAssert.FsrPipelineExecuted(snapshot));

        Assert.Contains("EASU", ex.Message);
    }

    [Fact]
    public void PharosAssert_FsrPipelineExecuted_ThrowsWhenRcasNotDispatched()
    {
        RenderScaleSnapshot snapshot = RenderScaleSnapshot.CreateSynthetic(
            0.5f, 1920, 1080, rcasDispatched: false);

        var ex = Assert.Throws<PharosAssertException>(() =>
            PharosAssert.FsrPipelineExecuted(snapshot));

        Assert.Contains("RCAS", ex.Message);
    }

    [Fact]
    public void PharosAssert_FsrFallbackMode_PassesWhenDisabled()
    {
        RenderScaleSnapshot snapshot = RenderScaleSnapshot.CreateSynthetic(
            0.5f, 1920, 1080, fsrEnabled: false, easuDispatched: false, rcasDispatched: false);

        // Should not throw
        PharosAssert.FsrFallbackMode(snapshot);
    }

    [Fact]
    public void PharosAssert_FsrFallbackMode_PassesWhenNativeScale()
    {
        RenderScaleSnapshot snapshot = RenderScaleSnapshot.CreateSynthetic(
            1.0f, 1920, 1080, fsrEnabled: true, easuDispatched: false, rcasDispatched: false);

        // Should not throw
        PharosAssert.FsrFallbackMode(snapshot);
    }

    [Fact]
    public void PharosAssert_FsrFallbackMode_ThrowsWhenFsrActive()
    {
        RenderScaleSnapshot snapshot = RenderScaleSnapshot.CreateSynthetic(0.75f, 1920, 1080);

        var ex = Assert.Throws<PharosAssertException>(() =>
            PharosAssert.FsrFallbackMode(snapshot));

        Assert.Contains("FSR fallback/native mode", ex.Message);
    }

    [Fact]
    public void PharosAssert_OutputResolutionMatches_PassesWhenCorrect()
    {
        RenderScaleSnapshot snapshot = RenderScaleSnapshot.CreateSynthetic(0.5f, 1920, 1080);

        // Should not throw
        PharosAssert.OutputResolutionMatches(snapshot, targetWidth: 1920, targetHeight: 1080);
    }

    [Fact]
    public void PharosAssert_OutputResolutionMatches_ThrowsWhenWidthMismatch()
    {
        RenderScaleSnapshot snapshot = RenderScaleSnapshot.CreateSynthetic(0.5f, 1920, 1080);

        var ex = Assert.Throws<PharosAssertException>(() =>
            PharosAssert.OutputResolutionMatches(snapshot, targetWidth: 2560, targetHeight: 1080));

        Assert.Contains("Display width 1920", ex.Message);
    }

    [Fact]
    public void PharosAssert_OutputResolutionMatches_ThrowsWhenHeightMismatch()
    {
        RenderScaleSnapshot snapshot = RenderScaleSnapshot.CreateSynthetic(0.5f, 1920, 1080);

        var ex = Assert.Throws<PharosAssertException>(() =>
            PharosAssert.OutputResolutionMatches(snapshot, targetWidth: 1920, targetHeight: 1440));

        Assert.Contains("Display height 1080", ex.Message);
    }
}

/// <summary>
/// Collection definition to serialize tests that use static _active field in RenderScaleInspector.
/// </summary>
[CollectionDefinition("RenderScaleInspector")]
public class RenderScaleInspectorCollection : ICollectionFixture<RenderScaleInspectorFixture>
{
}

public class RenderScaleInspectorFixture : IDisposable
{
    public void Dispose()
    {
        // Cleanup handled per-test
    }
}
