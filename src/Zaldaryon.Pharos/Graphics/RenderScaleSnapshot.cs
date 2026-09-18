namespace Zaldaryon.Pharos.Graphics;

/// <summary>
/// Immutable snapshot of FSR 1.0 render scale pipeline state.
/// Captures render scale factor, FBO dimensions, and shader dispatch status.
/// </summary>
public sealed record RenderScaleSnapshot
{
    /// <summary>Current render scale factor (0.5-1.0, where 1.0 is native resolution).</summary>
    public float RenderScale { get; init; }

    /// <summary>Width of the pre-upscale framebuffer in pixels.</summary>
    public int PreUpscaleWidth { get; init; }

    /// <summary>Height of the pre-upscale framebuffer in pixels.</summary>
    public int PreUpscaleHeight { get; init; }

    /// <summary>Width of the final display/window in pixels.</summary>
    public int DisplayWidth { get; init; }

    /// <summary>Height of the final display/window in pixels.</summary>
    public int DisplayHeight { get; init; }

    /// <summary>Whether FSR upscaling is currently enabled.</summary>
    public bool FsrEnabled { get; init; }

    /// <summary>Whether the EASU (Edge-Adaptive Spatial Upsampling) shader pass was dispatched.</summary>
    public bool EasuShaderDispatched { get; init; }

    /// <summary>Whether the RCAS (Robust Contrast Adaptive Sharpening) shader pass was dispatched.</summary>
    public bool RcasShaderDispatched { get; init; }

    /// <summary>
    /// True if both FSR shader passes (EASU and RCAS) were executed during the capture window.
    /// </summary>
    public bool FsrPipelineComplete => FsrEnabled && EasuShaderDispatched && RcasShaderDispatched;

    /// <summary>
    /// True if FSR is disabled and the renderer is operating in bilinear/native resolution mode.
    /// </summary>
    public bool IsFallbackMode => !FsrEnabled || Math.Abs(RenderScale - 1.0f) < 0.001f;

    /// <summary>
    /// Computed upscale ratio from pre-upscale to display resolution (width-based).
    /// Returns 1.0 if display width is 0 or pre-upscale equals display.
    /// </summary>
    public float UpscaleRatio => PreUpscaleWidth > 0 && DisplayWidth > 0
        ? (float)DisplayWidth / PreUpscaleWidth
        : 1.0f;

    /// <summary>
    /// Expected pre-upscale width based on DisplayWidth and RenderScale.
    /// </summary>
    public int ExpectedPreUpscaleWidth => (int)(DisplayWidth * RenderScale);

    /// <summary>
    /// Expected pre-upscale height based on DisplayHeight and RenderScale.
    /// </summary>
    public int ExpectedPreUpscaleHeight => (int)(DisplayHeight * RenderScale);

    /// <summary>Empty snapshot sentinel for disabled/uninitialized state.</summary>
    public static readonly RenderScaleSnapshot Empty = new()
    {
        RenderScale = 1.0f,
        FsrEnabled = false,
    };

    /// <summary>
    /// Creates a synthetic snapshot for testing without GPU context.
    /// </summary>
    /// <param name="renderScale">Render scale factor (0.5-1.0).</param>
    /// <param name="displayWidth">Display/window width.</param>
    /// <param name="displayHeight">Display/window height.</param>
    /// <param name="fsrEnabled">Whether FSR is enabled.</param>
    /// <param name="easuDispatched">Whether EASU pass was dispatched.</param>
    /// <param name="rcasDispatched">Whether RCAS pass was dispatched.</param>
    /// <returns>A valid RenderScaleSnapshot with computed pre-upscale dimensions.</returns>
    public static RenderScaleSnapshot CreateSynthetic(
        float renderScale,
        int displayWidth,
        int displayHeight,
        bool fsrEnabled = true,
        bool easuDispatched = true,
        bool rcasDispatched = true)
    {
        return new RenderScaleSnapshot
        {
            RenderScale = renderScale,
            PreUpscaleWidth = (int)(displayWidth * renderScale),
            PreUpscaleHeight = (int)(displayHeight * renderScale),
            DisplayWidth = displayWidth,
            DisplayHeight = displayHeight,
            FsrEnabled = fsrEnabled,
            EasuShaderDispatched = easuDispatched,
            RcasShaderDispatched = rcasDispatched,
        };
    }
}
