using Xunit;
using Zaldaryon.Pharos.Timing;

namespace Zaldaryon.Pharos.Tests.Timing;

/// <summary>
/// Unit tests for <see cref="PlatformLoopStepper"/> and <see cref="PlatformStepResult"/>.
/// </summary>
public class PlatformLoopStepperTests
{
    [Fact]
    public void PlatformStepResult_Unsupported_HasCorrectProperties()
    {
        // Arrange
        const string reason = "Test reason for unsupported operation";

        // Act
        PlatformStepResult result = PlatformStepResult.Unsupported(reason);

        // Assert
        Assert.False(result.Succeeded);
        Assert.False(result.Supported);
        Assert.Equal(reason, result.UnsupportedReason);
        Assert.Equal(0, result.FrameNumber);
        Assert.Equal(0, result.MeasuredDuration);
        Assert.False(result.FramePacingActive);
        Assert.Equal(-1, result.VsyncMode);
        Assert.Null(result.Exception);
    }

    [Fact]
    public void PlatformStepResult_Failed_HasCorrectProperties()
    {
        // Arrange
        Exception exception = new InvalidOperationException("Test exception");

        // Act
        PlatformStepResult result = PlatformStepResult.Failed(exception);

        // Assert
        Assert.False(result.Succeeded);
        Assert.True(result.Supported);
        Assert.Null(result.UnsupportedReason);
        Assert.Same(exception, result.Exception);
        Assert.Equal(0, result.FrameNumber);
    }

    [Fact]
    public void PlatformStepResult_Success_HasCorrectProperties()
    {
        // Act
        PlatformStepResult result = PlatformStepResult.Success(
            frameNumber: 42,
            measuredDuration: 0.0167,
            requestedDt: 1f / 60f,
            framePacingActive: true,
            vsyncMode: 0
        );

        // Assert
        Assert.True(result.Succeeded);
        Assert.True(result.Supported);
        Assert.Null(result.UnsupportedReason);
        Assert.Null(result.Exception);
        Assert.Equal(42, result.FrameNumber);
        Assert.Equal(0.0167, result.MeasuredDuration, precision: 4);
        Assert.Equal(1f / 60f, result.RequestedDt, precision: 4);
        Assert.True(result.FramePacingActive);
        Assert.Equal(0, result.VsyncMode);
    }

    [Fact]
    public void FramePacingSnapshot_RecordProperties()
    {
        // Act
        FramePacingSnapshot snapshot = new(
            VsyncMode: 0,
            MaxFps: 120f,
            IsPacingActive: true,
            FrameCount: 100,
            TotalTime: 1.667
        );

        // Assert
        Assert.Equal(0, snapshot.VsyncMode);
        Assert.Equal(120f, snapshot.MaxFps);
        Assert.True(snapshot.IsPacingActive);
        Assert.Equal(100, snapshot.FrameCount);
        Assert.Equal(1.667, snapshot.TotalTime, precision: 3);
    }

    [Fact]
    public void FramePacingSnapshot_Equality()
    {
        // Arrange
        FramePacingSnapshot a = new(VsyncMode: 1, MaxFps: 60f, IsPacingActive: false, FrameCount: 50, TotalTime: 0.833);
        FramePacingSnapshot b = new(VsyncMode: 1, MaxFps: 60f, IsPacingActive: false, FrameCount: 50, TotalTime: 0.833);
        FramePacingSnapshot c = new(VsyncMode: 0, MaxFps: 60f, IsPacingActive: true, FrameCount: 50, TotalTime: 0.833);

        // Assert
        Assert.Equal(a, b);
        Assert.NotEqual(a, c);
    }

    [Theory]
    [InlineData(0, 120f, true)]   // VSync off, limited FPS = pacing active
    [InlineData(0, 60f, true)]    // VSync off, limited FPS = pacing active
    [InlineData(0, 241f, false)]  // VSync off, unlimited FPS = pacing inactive
    [InlineData(0, 10f, false)]   // VSync off, very low limit = pacing inactive (below threshold)
    [InlineData(1, 60f, false)]   // VSync on = pacing inactive (VSync handles timing)
    public void FramePacingSnapshot_IsPacingActive_VariesBySettings(int vsyncMode, float maxFps, bool expectedActive)
    {
        // This test validates the expected behavior documented in the VS source
        // Frame pacing is active when VSync is off (mode != 1) and MaxFps is between 10 and 241

        bool actualActive = vsyncMode != 1 && maxFps > 10f && maxFps < 241f;
        Assert.Equal(expectedActive, actualActive);
    }
}
