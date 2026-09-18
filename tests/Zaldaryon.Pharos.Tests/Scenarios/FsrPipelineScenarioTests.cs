using Xunit;
using Zaldaryon.Pharos.Assertions;
using Zaldaryon.Pharos.Graphics;

namespace Zaldaryon.Pharos.Tests.Scenarios;

/// <summary>
/// Scenario tests for FSR 1.0 pipeline execution (Issue #120).
/// Validates render scale configuration, EASU/RCAS shader dispatch detection,
/// FBO dimension calculations, and fallback mode behavior.
/// Uses RenderScaleInspector with synthetic snapshots for headless-safe testing.
/// </summary>
[Collection("RenderScaleInspector")]
public sealed class FsrPipelineScenarioTests : IDisposable
{
    private readonly RenderScaleInspector _inspector = new();

    public void Dispose()
    {
        _inspector.Disable();
    }

    // -------------------------------------------------------------------------
    // Configuration: 0.75 scale, 1280x720 window -> internal 960x540
    // -------------------------------------------------------------------------

    private const float TestRenderScale = 0.75f;
    private const int TestDisplayWidth = 1280;
    private const int TestDisplayHeight = 720;
    private const int ExpectedInternalWidth = 960;
    private const int ExpectedInternalHeight = 540;

    // -------------------------------------------------------------------------
    // Render Scale Active Tests
    // -------------------------------------------------------------------------

    [Fact]
    public void FsrPipeline_RenderScaleActive_MatchesConfiguredScale()
    {
        // Arrange: Configure inspector with 0.75 scale, 1280x720 display
        _inspector.Configure(TestRenderScale, TestDisplayWidth, TestDisplayHeight, fsrEnabled: true);
        _inspector.Enable();

        // Act: Get snapshot
        RenderScaleSnapshot snapshot = _inspector.Snapshot();

        // Assert: Render scale is active at configured value
        PharosAssert.RenderScaleActive(snapshot, TestRenderScale);
    }

    [Fact]
    public void FsrPipeline_RenderScaleActive_SyntheticSnapshot()
    {
        // Arrange: Create synthetic snapshot with 0.75 scale
        RenderScaleSnapshot snapshot = RenderScaleSnapshot.CreateSynthetic(
            renderScale: TestRenderScale,
            displayWidth: TestDisplayWidth,
            displayHeight: TestDisplayHeight,
            fsrEnabled: true,
            easuDispatched: true,
            rcasDispatched: true);

        // Act & Assert: Verify scale is active
        PharosAssert.RenderScaleActive(snapshot, TestRenderScale);
        Assert.Equal(TestRenderScale, snapshot.RenderScale);
    }

    // -------------------------------------------------------------------------
    // FSR Pipeline Execution Tests (EASU + RCAS)
    // -------------------------------------------------------------------------

    [Fact]
    public void FsrPipeline_Executed_BothShadersDispatched()
    {
        // Arrange: Configure and enable inspector
        _inspector.Configure(TestRenderScale, TestDisplayWidth, TestDisplayHeight, fsrEnabled: true);
        _inspector.Enable();

        // Simulate both FSR shader passes being dispatched
        _inspector.SimulateEasuDispatch();
        _inspector.SimulateRcasDispatch();

        // Act: Get snapshot
        RenderScaleSnapshot snapshot = _inspector.Snapshot();

        // Assert: Both shaders dispatched, pipeline is complete
        PharosAssert.FsrPipelineExecuted(snapshot);
        Assert.True(snapshot.EasuShaderDispatched, "EASU shader should be dispatched");
        Assert.True(snapshot.RcasShaderDispatched, "RCAS shader should be dispatched");
        Assert.True(snapshot.FsrPipelineComplete, "FSR pipeline should be complete");
    }

    [Fact]
    public void FsrPipeline_Executed_SyntheticFullPipeline()
    {
        // Arrange: Synthetic snapshot with full FSR pipeline execution
        RenderScaleSnapshot snapshot = RenderScaleSnapshot.CreateSynthetic(
            renderScale: TestRenderScale,
            displayWidth: TestDisplayWidth,
            displayHeight: TestDisplayHeight,
            fsrEnabled: true,
            easuDispatched: true,
            rcasDispatched: true);

        // Act & Assert: Pipeline executed completely
        PharosAssert.FsrPipelineExecuted(snapshot);
        Assert.True(snapshot.FsrPipelineComplete);
    }

    [Fact]
    public void FsrPipeline_IncompleteWithoutEasu_Fails()
    {
        // Arrange: FSR enabled but EASU not dispatched
        RenderScaleSnapshot snapshot = RenderScaleSnapshot.CreateSynthetic(
            renderScale: TestRenderScale,
            displayWidth: TestDisplayWidth,
            displayHeight: TestDisplayHeight,
            fsrEnabled: true,
            easuDispatched: false,
            rcasDispatched: true);

        // Act & Assert: Should fail - EASU not dispatched
        Assert.False(snapshot.FsrPipelineComplete);
        Assert.Throws<PharosAssertException>(() => PharosAssert.FsrPipelineExecuted(snapshot));
    }

    [Fact]
    public void FsrPipeline_IncompleteWithoutRcas_Fails()
    {
        // Arrange: FSR enabled but RCAS not dispatched
        RenderScaleSnapshot snapshot = RenderScaleSnapshot.CreateSynthetic(
            renderScale: TestRenderScale,
            displayWidth: TestDisplayWidth,
            displayHeight: TestDisplayHeight,
            fsrEnabled: true,
            easuDispatched: true,
            rcasDispatched: false);

        // Act & Assert: Should fail - RCAS not dispatched
        Assert.False(snapshot.FsrPipelineComplete);
        Assert.Throws<PharosAssertException>(() => PharosAssert.FsrPipelineExecuted(snapshot));
    }

    // -------------------------------------------------------------------------
    // FBO Dimension Tests (Pre-Upscale Width/Height)
    // -------------------------------------------------------------------------

    [Fact]
    public void FsrPipeline_FboDimensions_CorrectPreUpscaleSize()
    {
        // Arrange: Configure with 0.75 scale, 1280x720 display
        _inspector.Configure(TestRenderScale, TestDisplayWidth, TestDisplayHeight, fsrEnabled: true);
        _inspector.Enable();

        // Act: Get snapshot
        RenderScaleSnapshot snapshot = _inspector.Snapshot();

        // Assert: Pre-upscale dimensions should be 960x540
        Assert.Equal(ExpectedInternalWidth, snapshot.PreUpscaleWidth);
        Assert.Equal(ExpectedInternalHeight, snapshot.PreUpscaleHeight);
    }

    [Fact]
    public void FsrPipeline_FboDimensions_SyntheticCorrectSize()
    {
        // Arrange: Synthetic snapshot
        RenderScaleSnapshot snapshot = RenderScaleSnapshot.CreateSynthetic(
            renderScale: TestRenderScale,
            displayWidth: TestDisplayWidth,
            displayHeight: TestDisplayHeight);

        // Assert: Pre-upscale dimensions match expected (960x540)
        Assert.Equal(ExpectedInternalWidth, snapshot.PreUpscaleWidth);
        Assert.Equal(ExpectedInternalHeight, snapshot.PreUpscaleHeight);

        // Verify expected calculations match actual
        Assert.Equal(snapshot.ExpectedPreUpscaleWidth, snapshot.PreUpscaleWidth);
        Assert.Equal(snapshot.ExpectedPreUpscaleHeight, snapshot.PreUpscaleHeight);
    }

    [Fact]
    public void FsrPipeline_FboDimensions_MultipleScaleFactors()
    {
        // Test various scale factors
        (float scale, int expectedWidth, int expectedHeight)[] testCases =
        [
            (0.5f, 640, 360),    // 50% scale
            (0.6f, 768, 432),    // 60% scale
            (0.75f, 960, 540),   // 75% scale (Quality preset)
            (0.85f, 1088, 612),  // 85% scale (Balanced preset)
            (1.0f, 1280, 720),   // Native (no scaling)
        ];

        foreach ((float scale, int expectedWidth, int expectedHeight) in testCases)
        {
            RenderScaleSnapshot snapshot = RenderScaleSnapshot.CreateSynthetic(
                renderScale: scale,
                displayWidth: TestDisplayWidth,
                displayHeight: TestDisplayHeight);

            Assert.Equal(expectedWidth, snapshot.PreUpscaleWidth);
            Assert.Equal(expectedHeight, snapshot.PreUpscaleHeight);
        }
    }

    // -------------------------------------------------------------------------
    // Output Resolution Tests
    // -------------------------------------------------------------------------

    [Fact]
    public void FsrPipeline_OutputResolution_MatchesTarget()
    {
        // Arrange: Configure inspector
        _inspector.Configure(TestRenderScale, TestDisplayWidth, TestDisplayHeight, fsrEnabled: true);
        _inspector.Enable();

        // Act: Get snapshot
        RenderScaleSnapshot snapshot = _inspector.Snapshot();

        // Assert: Output resolution matches target (1280x720)
        PharosAssert.OutputResolutionMatches(snapshot, TestDisplayWidth, TestDisplayHeight);
    }

    [Fact]
    public void FsrPipeline_OutputResolution_SyntheticMatchesTarget()
    {
        // Arrange: Synthetic snapshot with FSR upscaling
        RenderScaleSnapshot snapshot = RenderScaleSnapshot.CreateSynthetic(
            renderScale: TestRenderScale,
            displayWidth: TestDisplayWidth,
            displayHeight: TestDisplayHeight);

        // Assert: Display dimensions match target window size
        PharosAssert.OutputResolutionMatches(snapshot, TestDisplayWidth, TestDisplayHeight);
        Assert.Equal(TestDisplayWidth, snapshot.DisplayWidth);
        Assert.Equal(TestDisplayHeight, snapshot.DisplayHeight);
    }

    [Fact]
    public void FsrPipeline_OutputResolution_WrongDimensionsFails()
    {
        // Arrange: Snapshot with 1280x720 display
        RenderScaleSnapshot snapshot = RenderScaleSnapshot.CreateSynthetic(
            renderScale: TestRenderScale,
            displayWidth: TestDisplayWidth,
            displayHeight: TestDisplayHeight);

        // Assert: Fails when checking wrong dimensions
        Assert.Throws<PharosAssertException>(() =>
            PharosAssert.OutputResolutionMatches(snapshot, 1920, 1080));
    }

    // -------------------------------------------------------------------------
    // FSR Fallback Mode Tests
    // -------------------------------------------------------------------------

    [Fact]
    public void FsrPipeline_FallbackMode_WhenFsrDisabled()
    {
        // Arrange: Configure with FSR disabled
        _inspector.Configure(TestRenderScale, TestDisplayWidth, TestDisplayHeight, fsrEnabled: false);
        _inspector.Enable();

        // Act: Get snapshot
        RenderScaleSnapshot snapshot = _inspector.Snapshot();

        // Assert: Should be in fallback mode
        PharosAssert.FsrFallbackMode(snapshot);
        Assert.True(snapshot.IsFallbackMode);
    }

    [Fact]
    public void FsrPipeline_FallbackMode_SyntheticDisabledFsr()
    {
        // Arrange: Synthetic snapshot with FSR disabled
        RenderScaleSnapshot snapshot = RenderScaleSnapshot.CreateSynthetic(
            renderScale: TestRenderScale,
            displayWidth: TestDisplayWidth,
            displayHeight: TestDisplayHeight,
            fsrEnabled: false,
            easuDispatched: false,
            rcasDispatched: false);

        // Assert: Fallback mode active, no shaders dispatched
        PharosAssert.FsrFallbackMode(snapshot);
        Assert.True(snapshot.IsFallbackMode);
        Assert.False(snapshot.EasuShaderDispatched);
        Assert.False(snapshot.RcasShaderDispatched);
    }

    [Fact]
    public void FsrPipeline_FallbackMode_NativeResolution()
    {
        // Arrange: Native resolution (1.0 scale) - should be fallback regardless of FSR flag
        RenderScaleSnapshot snapshot = RenderScaleSnapshot.CreateSynthetic(
            renderScale: 1.0f,
            displayWidth: TestDisplayWidth,
            displayHeight: TestDisplayHeight,
            fsrEnabled: true,
            easuDispatched: false,
            rcasDispatched: false);

        // Assert: Native resolution triggers fallback mode
        Assert.True(snapshot.IsFallbackMode, "Native 1.0 scale should be fallback mode");
        PharosAssert.FsrFallbackMode(snapshot);
    }

    [Fact]
    public void FsrPipeline_FallbackMode_NotFallbackWhenFsrActive()
    {
        // Arrange: FSR enabled with sub-1.0 scale
        RenderScaleSnapshot snapshot = RenderScaleSnapshot.CreateSynthetic(
            renderScale: TestRenderScale,
            displayWidth: TestDisplayWidth,
            displayHeight: TestDisplayHeight,
            fsrEnabled: true,
            easuDispatched: true,
            rcasDispatched: true);

        // Assert: Should NOT be in fallback mode
        Assert.False(snapshot.IsFallbackMode);
        Assert.Throws<PharosAssertException>(() => PharosAssert.FsrFallbackMode(snapshot));
    }

    // -------------------------------------------------------------------------
    // Upscale Ratio Tests
    // -------------------------------------------------------------------------

    [Fact]
    public void FsrPipeline_UpscaleRatio_CorrectCalculation()
    {
        // Arrange: 0.75 scale means 1.333x upscale ratio
        RenderScaleSnapshot snapshot = RenderScaleSnapshot.CreateSynthetic(
            renderScale: TestRenderScale,
            displayWidth: TestDisplayWidth,
            displayHeight: TestDisplayHeight);

        // Expected ratio: 1280 / 960 = 1.333...
        float expectedRatio = (float)TestDisplayWidth / ExpectedInternalWidth;

        // Assert: Upscale ratio is correct
        Assert.Equal(expectedRatio, snapshot.UpscaleRatio, precision: 3);
    }

    // -------------------------------------------------------------------------
    // Inspector Enable/Disable Lifecycle Tests
    // -------------------------------------------------------------------------

    [Fact]
    public void FsrPipeline_Inspector_EnableDisableCycle()
    {
        // Arrange
        Assert.False(_inspector.IsEnabled);

        // Act: Enable
        _inspector.Configure(TestRenderScale, TestDisplayWidth, TestDisplayHeight);
        _inspector.Enable();
        Assert.True(_inspector.IsEnabled);

        // Take snapshot while enabled
        RenderScaleSnapshot snapshot1 = _inspector.Snapshot();
        Assert.Equal(TestRenderScale, snapshot1.RenderScale);

        // Act: Disable
        _inspector.Disable();
        Assert.False(_inspector.IsEnabled);
    }

    [Fact]
    public void FsrPipeline_Inspector_ResetCounters()
    {
        // Arrange: Enable and simulate dispatches
        _inspector.Configure(TestRenderScale, TestDisplayWidth, TestDisplayHeight, fsrEnabled: true);
        _inspector.Enable();
        _inspector.SimulateEasuDispatch();
        _inspector.SimulateRcasDispatch();

        // Verify dispatches recorded
        RenderScaleSnapshot before = _inspector.Snapshot();
        Assert.True(before.EasuShaderDispatched);
        Assert.True(before.RcasShaderDispatched);

        // Act: Reset counters
        _inspector.Reset();

        // Assert: Counters are reset
        RenderScaleSnapshot after = _inspector.Snapshot();
        Assert.False(after.EasuShaderDispatched);
        Assert.False(after.RcasShaderDispatched);
    }
}
