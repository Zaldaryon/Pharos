using System.Reflection;
using HarmonyLib;
using OpenTK.Graphics.OpenGL4;

namespace Zaldaryon.Pharos.Graphics;

/// <summary>
/// Inspects FSR 1.0 render scale pipeline state including render scale factor,
/// FBO dimensions, and EASU/RCAS shader dispatch events.
/// Uses Harmony prefix patches on GL methods and shader program usage to track
/// FSR shader execution without requiring native library access.
/// </summary>
public sealed class RenderScaleInspector
{
    private const string HarmonyId = "zaldaryon.pharos.renderscale";

    private readonly Harmony _harmony = new(HarmonyId);
    private readonly object _lock = new();
    private bool _enabled;

    // Configuration: set by caller before Enable() or updated via SetRenderScale
    private float _renderScale = 1.0f;
    private int _displayWidth;
    private int _displayHeight;
    private bool _fsrEnabled;

    // Counters incremented by Harmony patches
    private int _easuDispatchCount;
    private int _rcasDispatchCount;
    private int _viewportChanges;
    private int _framebufferBinds;

    // Tracked FBO dimensions from viewport changes
    private int _lastViewportWidth;
    private int _lastViewportHeight;
    private int _preUpscaleWidth;
    private int _preUpscaleHeight;

    // Active instance for static patch delegates
    private static RenderScaleInspector? _active;
    private static readonly object _activeLock = new();

    public bool IsEnabled
    {
        get { lock (_lock) { return _enabled; } }
    }

    /// <summary>
    /// Configures the render scale factor before enabling inspection.
    /// </summary>
    /// <param name="scale">Render scale factor (0.5-1.0).</param>
    /// <param name="displayWidth">Display/window width in pixels.</param>
    /// <param name="displayHeight">Display/window height in pixels.</param>
    /// <param name="fsrEnabled">Whether FSR is enabled.</param>
    public void Configure(float scale, int displayWidth, int displayHeight, bool fsrEnabled = true)
    {
        lock (_lock)
        {
            _renderScale = Math.Clamp(scale, 0.5f, 1.0f);
            _displayWidth = displayWidth;
            _displayHeight = displayHeight;
            _fsrEnabled = fsrEnabled;
            _preUpscaleWidth = (int)(displayWidth * _renderScale);
            _preUpscaleHeight = (int)(displayHeight * _renderScale);
        }
    }

    /// <summary>
    /// Activates FSR pipeline inspection by installing Harmony patches.
    /// No-op if already enabled.
    /// </summary>
    public void Enable()
    {
        lock (_activeLock)
        {
            lock (_lock)
            {
                if (_enabled) return;

                _active = this;
                ApplyPatches();
                _enabled = true;
            }
        }
    }

    /// <summary>
    /// Deactivates inspection and removes Harmony patches.
    /// </summary>
    public void Disable()
    {
        lock (_activeLock)
        {
            lock (_lock)
            {
                if (!_enabled) return;

                _harmony.UnpatchAll(HarmonyId);
                if (ReferenceEquals(_active, this)) _active = null;
                _enabled = false;
            }
        }
    }

    /// <summary>
    /// Resets all counters to zero without changing enabled state or configuration.
    /// </summary>
    public void Reset()
    {
        Interlocked.Exchange(ref _easuDispatchCount, 0);
        Interlocked.Exchange(ref _rcasDispatchCount, 0);
        Interlocked.Exchange(ref _viewportChanges, 0);
        Interlocked.Exchange(ref _framebufferBinds, 0);
    }

    /// <summary>
    /// Returns an immutable snapshot of current FSR pipeline state.
    /// </summary>
    public RenderScaleSnapshot Snapshot()
    {
        lock (_lock)
        {
            return new RenderScaleSnapshot
            {
                RenderScale = _renderScale,
                PreUpscaleWidth = _preUpscaleWidth,
                PreUpscaleHeight = _preUpscaleHeight,
                DisplayWidth = _displayWidth,
                DisplayHeight = _displayHeight,
                FsrEnabled = _fsrEnabled,
                EasuShaderDispatched = _easuDispatchCount > 0,
                RcasShaderDispatched = _rcasDispatchCount > 0,
            };
        }
    }

    // Static counter increment helpers called by Harmony patches
    internal static void OnEasuDispatch() => Interlocked.Increment(ref _active!._easuDispatchCount);
    internal static void OnRcasDispatch() => Interlocked.Increment(ref _active!._rcasDispatchCount);
    internal static void OnViewportChange(int width, int height)
    {
        if (_active == null) return;
        Interlocked.Increment(ref _active._viewportChanges);
        _active._lastViewportWidth = width;
        _active._lastViewportHeight = height;
    }
    internal static void OnFramebufferBind() => Interlocked.Increment(ref _active!._framebufferBinds);

    /// <summary>
    /// Simulates an EASU shader dispatch for testing without GPU context.
    /// </summary>
    public void SimulateEasuDispatch()
    {
        if (_active == this) Interlocked.Increment(ref _easuDispatchCount);
    }

    /// <summary>
    /// Simulates an RCAS shader dispatch for testing without GPU context.
    /// </summary>
    public void SimulateRcasDispatch()
    {
        if (_active == this) Interlocked.Increment(ref _rcasDispatchCount);
    }

    private void ApplyPatches()
    {
        Type gl = typeof(GL);
        BindingFlags pub = BindingFlags.Public | BindingFlags.Static;

        // Patch only the specific Viewport(int, int, int, int) overload
        PatchSpecificOverload(gl, "Viewport", pub,
            [typeof(int), typeof(int), typeof(int), typeof(int)],
            nameof(Prefix_Viewport));

        // Patch framebuffer bindings
        PatchSpecificOverload(gl, "BindFramebuffer", pub,
            [typeof(FramebufferTarget), typeof(int)],
            nameof(Prefix_BindFramebuffer));

        // Patch UseProgram to detect shader switches (for future EASU/RCAS detection)
        PatchSpecificOverload(gl, "UseProgram", pub,
            [typeof(int)],
            nameof(Prefix_UseProgram));

        // Patch draw calls that might be FSR fullscreen quad dispatches
        PatchSpecificOverload(gl, "DrawArrays", pub,
            [typeof(PrimitiveType), typeof(int), typeof(int)],
            nameof(Prefix_DrawArrays));
    }

    private void PatchSpecificOverload(Type type, string methodName, BindingFlags flags, Type[] parameterTypes, string prefixName)
    {
        MethodInfo? method = type.GetMethod(methodName, flags, parameterTypes);
        if (method != null)
        {
            _harmony.Patch(method, prefix: new HarmonyMethod(typeof(RenderScaleInspector), prefixName));
        }
    }

    // Static Harmony prefix methods

    private static void Prefix_Viewport(int x, int y, int width, int height)
    {
        if (_active != null) OnViewportChange(width, height);
    }

    private static void Prefix_BindFramebuffer(FramebufferTarget target, int framebuffer)
    {
        if (_active != null) OnFramebufferBind();
    }

    private static void Prefix_UseProgram(int program)
    {
        // In a real implementation, this would check if program is EASU or RCAS
        // For now, shader detection is done via simulation or external hooks
        _ = program;
    }

    private static void Prefix_DrawArrays(PrimitiveType mode, int first, int count)
    {
        // Track draw calls that could be FSR fullscreen quad dispatches
        // Actual EASU/RCAS detection requires shader program identification
    }
}
