using System.Diagnostics;
using System.Reflection;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.Desktop;
using Vintagestory.Client;
using Vintagestory.Client.NoObf;
using Zaldaryon.Pharos.Platform;
using Zaldaryon.Pharos.Bootstrap;

namespace Zaldaryon.Pharos.Timing;

/// <summary>
/// Steps the engine via the full platform loop (<c>ClientPlatformWindows.window_RenderFrame</c>)
/// instead of just <c>ScreenManager.Render</c>, exercising frame pacing, background FPS limits,
/// and any OptiTime patches on platform-loop paths.
/// </summary>
/// <remarks>
/// <para>
/// The default <see cref="DeterministicFrameController"/> steps via <c>ScreenManager.Render</c>,
/// which is deterministic and fast but bypasses the platform's frame timing logic. This stepper
/// invokes the same callback the vanilla game window uses, so the full frame timing path runs.
/// </para>
/// <para>
/// Use <see cref="IsSupported"/> to check whether platform-loop stepping is available. When not
/// supported, <see cref="Step"/> returns <see cref="PlatformStepResult.Unsupported"/>.
/// </para>
/// </remarks>
public sealed class PlatformLoopStepper
{
    private static readonly FieldInfo? s_renderFrameMethod =
        typeof(ClientPlatformWindows).GetField("window_RenderFrame", BindingFlags.Instance | BindingFlags.NonPublic)
        ?? typeof(ClientPlatformWindows).GetField("_renderFrameHandler", BindingFlags.Instance | BindingFlags.NonPublic);

    private static readonly MethodInfo? s_onRenderFrame =
        typeof(ClientPlatformWindows).GetMethod("window_RenderFrame", BindingFlags.Instance | BindingFlags.NonPublic, null, [typeof(FrameEventArgs)], null);

    private static readonly PropertyInfo? s_frameStopwatch =
        typeof(ClientPlatformWindows).GetProperty("frameStopWatch", BindingFlags.Instance | BindingFlags.NonPublic)
        ?? typeof(ClientPlatformWindows).GetProperty("FrameStopWatch", BindingFlags.Instance | BindingFlags.NonPublic);

    private static readonly FieldInfo? s_frameStopwatchField =
        typeof(ClientPlatformWindows).GetField("frameStopWatch", BindingFlags.Instance | BindingFlags.NonPublic);

    private readonly ClientPlatformWindows _platform;
    private readonly ScreenManager _screenManager;
    private readonly HeadlessWindow _window;
    private readonly Action<FrameEventArgs>? _renderFrameCallback;
    private readonly Stopwatch? _frameStopwatch;

    private long _platformFrameCount;
    private double _totalPlatformTime;

    /// <summary>
    /// Creates a platform-loop stepper for the given client components.
    /// </summary>
    public PlatformLoopStepper(ClientPlatformWindows platform, ScreenManager screenManager, HeadlessWindow window)
    {
        _platform = platform ?? throw new ArgumentNullException(nameof(platform));
        _screenManager = screenManager ?? throw new ArgumentNullException(nameof(screenManager));
        _window = window ?? throw new ArgumentNullException(nameof(window));

        // Try to get the render frame callback
        if (s_onRenderFrame != null)
        {
            _renderFrameCallback = args => s_onRenderFrame.Invoke(_platform, [args]);
        }

        // Try to get the frame stopwatch for measuring
        if (s_frameStopwatchField != null)
        {
            _frameStopwatch = s_frameStopwatchField.GetValue(_platform) as Stopwatch;
        }
        else if (s_frameStopwatch != null)
        {
            _frameStopwatch = s_frameStopwatch.GetValue(_platform) as Stopwatch;
        }
    }

    /// <summary>
    /// Whether platform-loop stepping is supported with the loaded Vintage Story version.
    /// </summary>
    public bool IsSupported => _renderFrameCallback != null;

    /// <summary>
    /// Why platform-loop stepping is not supported, or null when it is.
    /// </summary>
    public string? UnsupportedReason
    {
        get
        {
            if (_renderFrameCallback != null) return null;
            return s_onRenderFrame == null
                ? "ClientPlatformWindows.window_RenderFrame method not found; the VS version may be incompatible"
                : "Could not create render frame callback";
        }
    }

    /// <summary>
    /// Total frames stepped via the platform loop since this stepper was created.
    /// </summary>
    public long PlatformFrameCount => _platformFrameCount;

    /// <summary>
    /// Total elapsed time (in seconds) reported by platform frames.
    /// </summary>
    public double TotalPlatformTime => _totalPlatformTime;

    /// <summary>
    /// Steps one frame via the full platform loop.
    /// </summary>
    /// <param name="dt">Delta time for this frame.</param>
    /// <returns>Result indicating success, what was exercised, or why it failed.</returns>
    public PlatformStepResult Step(float dt = 1f / 60f)
    {
        if (!IsSupported)
        {
            return PlatformStepResult.Unsupported(UnsupportedReason ?? "Platform loop stepping is not supported");
        }

        try
        {
            // Make GL context current
            _window.NativeWindow.MakeCurrent();

            // Process GLFW events
            _window.NativeWindow.ProcessEvents(0.0);

            // Capture frame timing state before
            Stopwatch? stopwatch = _frameStopwatch;
            long ticksBefore = stopwatch?.ElapsedTicks ?? 0;
            bool wasRunning = stopwatch?.IsRunning ?? false;

            // Ensure stopwatch is running (vanilla expects this)
            if (stopwatch != null && !wasRunning)
            {
                stopwatch.Start();
            }

            // Call the platform's render frame handler
            FrameEventArgs args = new(dt);
            _renderFrameCallback!(args);

            // Capture frame timing state after
            long ticksAfter = stopwatch?.ElapsedTicks ?? 0;
            double frameDuration = stopwatch != null
                ? (double)(ticksAfter - ticksBefore) / Stopwatch.Frequency
                : dt;

            _platformFrameCount++;
            _totalPlatformTime += frameDuration;

            // Swap buffers (vanilla does this at end of render frame)
            ((GameWindow)(object)_window.NativeWindow).SwapBuffers();

            return PlatformStepResult.Success(
                frameNumber: _platformFrameCount,
                measuredDuration: frameDuration,
                requestedDt: dt,
                framePacingActive: IsPacingActive(),
                vsyncMode: GetVsyncMode()
            );
        }
        catch (Exception ex)
        {
            return PlatformStepResult.Failed(ex);
        }
    }

    /// <summary>
    /// Steps multiple frames via the platform loop.
    /// </summary>
    public IReadOnlyList<PlatformStepResult> StepFrames(int count, float dt = 1f / 60f)
    {
        if (count < 0) throw new ArgumentOutOfRangeException(nameof(count));

        List<PlatformStepResult> results = new(count);
        for (int i = 0; i < count; i++)
        {
            results.Add(Step(dt));
            if (!results[^1].Succeeded) break;
        }
        return results;
    }

    /// <summary>
    /// Returns a snapshot of the current frame pacing state.
    /// </summary>
    public FramePacingSnapshot GetPacingState()
    {
        return new FramePacingSnapshot(
            VsyncMode: GetVsyncMode(),
            MaxFps: _platform.MaxFps,
            IsPacingActive: IsPacingActive(),
            FrameCount: _platformFrameCount,
            TotalTime: _totalPlatformTime
        );
    }

    private bool IsPacingActive()
    {
        // Frame pacing is active when VSync is off and MaxFps is limited
        int vsyncMode = ClientSettings.VsyncMode;
        float maxFps = _platform.MaxFps;
        return vsyncMode != 1 && maxFps > 10f && maxFps < 241f;
    }

    private int GetVsyncMode()
    {
        try
        {
            return ClientSettings.VsyncMode;
        }
        catch
        {
            return -1; // Unknown
        }
    }
}

/// <summary>
/// Result of a platform-loop step operation.
/// </summary>
public sealed class PlatformStepResult
{
    private PlatformStepResult(
        bool succeeded,
        bool supported,
        long frameNumber,
        double measuredDuration,
        float requestedDt,
        bool framePacingActive,
        int vsyncMode,
        string? reason,
        Exception? exception)
    {
        Succeeded = succeeded;
        Supported = supported;
        FrameNumber = frameNumber;
        MeasuredDuration = measuredDuration;
        RequestedDt = requestedDt;
        FramePacingActive = framePacingActive;
        VsyncMode = vsyncMode;
        UnsupportedReason = reason;
        Exception = exception;
    }

    /// <summary>Whether the step completed successfully.</summary>
    public bool Succeeded { get; }

    /// <summary>Whether platform-loop stepping is supported at all.</summary>
    public bool Supported { get; }

    /// <summary>The frame number (1-based) within this stepper's lifetime.</summary>
    public long FrameNumber { get; }

    /// <summary>Actual measured duration of the frame in seconds.</summary>
    public double MeasuredDuration { get; }

    /// <summary>Requested delta time.</summary>
    public float RequestedDt { get; }

    /// <summary>Whether frame pacing (MaxFps limiting) was active.</summary>
    public bool FramePacingActive { get; }

    /// <summary>VSync mode: 0=off, 1=on, -1=unknown.</summary>
    public int VsyncMode { get; }

    /// <summary>Why the operation was not supported, when <see cref="Supported"/> is false.</summary>
    public string? UnsupportedReason { get; }

    /// <summary>Exception that caused failure, when <see cref="Succeeded"/> is false.</summary>
    public Exception? Exception { get; }

    internal static PlatformStepResult Success(
        long frameNumber,
        double measuredDuration,
        float requestedDt,
        bool framePacingActive,
        int vsyncMode) =>
        new(true, true, frameNumber, measuredDuration, requestedDt, framePacingActive, vsyncMode, null, null);

    internal static PlatformStepResult Unsupported(string reason) =>
        new(false, false, 0, 0, 0, false, -1, reason, null);

    internal static PlatformStepResult Failed(Exception exception) =>
        new(false, true, 0, 0, 0, false, -1, null, exception);
}

/// <summary>
/// Snapshot of the frame pacing state.
/// </summary>
/// <param name="VsyncMode">VSync mode: 0=off, 1=on.</param>
/// <param name="MaxFps">Maximum FPS setting.</param>
/// <param name="IsPacingActive">Whether frame pacing is currently active.</param>
/// <param name="FrameCount">Total frames stepped via platform loop.</param>
/// <param name="TotalTime">Total elapsed time in seconds.</param>
public sealed record FramePacingSnapshot(
    int VsyncMode,
    float MaxFps,
    bool IsPacingActive,
    long FrameCount,
    double TotalTime
);
