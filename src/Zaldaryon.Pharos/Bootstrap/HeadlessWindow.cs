using System;
using OpenTK.Graphics.OpenGL;
using OpenTK.Mathematics;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.Desktop;
using OpenTK.Windowing.GraphicsLibraryFramework;
// ErrorCode exists in both OpenTK.Graphics.OpenGL and the GLFW bindings imported above.
using GlfwErrorCode = OpenTK.Windowing.GraphicsLibraryFramework.ErrorCode;
using Vintagestory.Client.NoObf;
using Zaldaryon.Pharos.Core;
using Zaldaryon.Pharos.Platform;

namespace Zaldaryon.Pharos.Bootstrap;

/// <summary>
/// Manages an offscreen GLFW window with hidden display attributes and an attached FBO.
/// </summary>
public sealed class HeadlessWindow : IDisposable
{
    private bool _disposed;

    public GameWindowNative NativeWindow { get; }
    public HeadlessFramebuffer Framebuffer { get; }
    public GlRendererInfo? RendererInfo { get; }
    /// <summary>The window's width when the client booted. See <see cref="CurrentWidth"/>.</summary>
    public int Width { get; }

    /// <summary>The window's height when the client booted. See <see cref="CurrentHeight"/>.</summary>
    public int Height { get; }

    /// <summary>The client this window belongs to, set when the client is created.</summary>
    internal HeadlessClient? Owner
    {
        get => _owner;
        set
        {
            _owner = value;
            _bootGuiScale = ClientSettings.GUIScale;
            _bootScreenSize = (ClientSettings.ScreenWidth, ClientSettings.ScreenHeight);
        }
    }

    private HeadlessClient? _owner;
    private float _bootGuiScale;
    private (int Width, int Height) _bootScreenSize;

    /// <summary>The window's width now, as the game sees it, after any <see cref="ResizeAsync"/>.</summary>
    public int CurrentWidth => Owner?.Platform.WindowSize.Width ?? Width;

    /// <summary>The window's height now, as the game sees it, after any <see cref="ResizeAsync"/>.</summary>
    public int CurrentHeight => Owner?.Platform.WindowSize.Height ?? Height;

    public bool IsVisible => !_disposed && NativeWindow.IsVisible;
    public bool IsFocused => !_disposed && NativeWindow.IsFocused;

    /// <summary>
    /// The most recent error GLFW reported, or null when it has reported none.
    /// </summary>
    /// <remarks>
    /// GLFW reports advisory conditions as errors, so this is diagnostic information rather than
    /// a failure signal. It exists because the callback that used to be in place threw, which
    /// turned a Wayland window-icon warning into a dead test host.
    /// </remarks>
    public static string? LastGlfwError { get; private set; }

    public IntPtr Win32WindowHandle
    {
        get
        {
            if (!OperatingSystem.IsWindows() || _disposed)
            {
                return IntPtr.Zero;
            }

            unsafe
            {
                Window* windowPtr = NativeWindow.WindowPtr;
                return windowPtr != null ? GLFW.GetWin32Window(windowPtr) : IntPtr.Zero;
            }
        }
    }

    public HeadlessWindow(HeadlessClientOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (options.Width <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "Width must be greater than zero.");
        }

        if (options.Height <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "Height must be greater than zero.");
        }

        Width = options.Width;
        Height = options.Height;

        // Ensure GLFW can run on any test thread
        GLFWProvider.CheckForMainThread = false;

        // OpenTK installs a default GLFW error callback that throws. Under a Wayland session
        // GLFW selects the Wayland backend and reports "The platform does not support setting
        // the window icon" as an error, which the default callback turns into an unhandled
        // exception that kills the test host outright. A headless offscreen window cannot act on
        // a window-manager decoration warning, so record it and keep going. Recorded rather than
        // discarded so a test can assert on it, and so CI on an X11-only runner would still show
        // the message if it ever appeared there.
        GLFWProvider.SetErrorCallback((GlfwErrorCode code, string description) =>
            LastGlfwError = $"{code}: {description}");
        LastGlfwError = null;

        // Configure Mesa software rasterization (llvmpipe) and validate virtual display server on Linux
        if (options.ConfigureMesaEnvironment && (OperatingSystem.IsLinux() || options.ForceSoftwareRendering))
        {
            LinuxHeadlessEnvironment.ConfigureMesaEnvironment(options);
        }

        if (OperatingSystem.IsLinux())
        {
            LinuxHeadlessEnvironment.ValidateDisplayServer(options.LinuxDisplay);
        }

        // Initialize GLFW with offscreen and non-activating hints
        if (!GLFW.Init())
        {
            throw new InvalidOperationException("Failed to initialize GLFW.");
        }

        GLFW.WindowHint(WindowHintBool.Visible, false);
        GLFW.WindowHint(WindowHintBool.Focused, false);
        GLFW.WindowHint(WindowHintBool.FocusOnShow, false);
        GLFW.WindowHint(WindowHintBool.Resizable, false);
        GLFW.WindowHint(WindowHintBool.Decorated, false);
        GLFW.WindowHint(WindowHintBool.Floating, false);

        NativeWindowSettings nativeSettings = new()
        {
            Title = "Pharos Headless Client",
            ClientSize = new Vector2i(Width, Height),
            StartVisible = false,
            StartFocused = false,
            WindowState = WindowState.Normal,
            WindowBorder = WindowBorder.Hidden,
            APIVersion = new Version(4, 3),
            Flags = ContextFlags.Default
        };

        GameWindowSettings gameSettings = GameWindowSettings.Default;

        NativeWindow = new GameWindowNative(gameSettings, nativeSettings);

        // On Windows, strip taskbar registration and enforce tool window style
        if (OperatingSystem.IsWindows())
        {
            unsafe
            {
                Window* windowPtr = NativeWindow.WindowPtr;
                if (windowPtr != null)
                {
                    IntPtr hwnd = GLFW.GetWin32Window(windowPtr);
                    Win32WindowInterop.SuppressTaskbarAndFocus(hwnd);
                }
            }
        }

        // Ensure OpenGL context is active and current on this thread
        NativeWindow.MakeCurrent();
        GL.LoadBindings(new GLFWBindingsContext());

        // Query driver and renderer capabilities
        RendererInfo = LinuxHeadlessEnvironment.QueryRendererInfo();

        // Create the offscreen framebuffer
        Framebuffer = new HeadlessFramebuffer(Width, Height);
        Framebuffer.Bind();
    }

    public void MakeCurrent()
    {
        if (_disposed) return;
        NativeWindow.MakeCurrent();
        Framebuffer.Bind();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        try
        {
            NativeWindow.MakeCurrent();
        }
        catch
        {
            // Ignore context activation errors during teardown
        }

        try
        {
            Framebuffer.Dispose();
        }
        catch
        {
            // Ignore framebuffer disposal errors during teardown
        }

        try
        {
            NativeWindow.Dispose();
        }
        catch
        {
            // Ignore window disposal errors during teardown
        }
    }

    /// <summary>
    /// Resizes the window as a player dragging its edge does: the game rebuilds its framebuffers
    /// and the next frame lays the dialogs out again. Runs that frame. Engine mode only.
    /// </summary>
    /// <remarks>
    /// The engine-side size is what changes; <see cref="Framebuffer"/>, used by fixture-mode
    /// clients, keeps the boot size. The game remembers the size in its client settings, as it
    /// does for a player. A scenario class puts the boot size back after each test.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">The size is below the game's smallest window, 600x400.</exception>
    /// <exception cref="NotSupportedException">The client is not in engine mode.</exception>
    public async Task ResizeAsync(int width, int height, CancellationToken ct = default)
    {
        UI.WindowLayout.Checked(new UI.WindowLayout(width, height));
        HeadlessClient client = EngineOwner();
        client.RunOnClientThread(() =>
        {
            client.Platform.SetWindowSize(width, height);
            if (client.Platform.WindowSize.Width != width || client.Platform.WindowSize.Height != height)
            {
                throw new InvalidOperationException(
                    $"The window was resized to {client.Platform.WindowSize.Width}x{client.Platform.WindowSize.Height}, not {width}x{height}.");
            }
        });

        // Dialogs lay themselves out again in the frame after: their bounds follow the window then.
        await client.Frame(ct: ct).ConfigureAwait(false);
    }

    /// <inheritdoc cref="ResizeAsync(int, int, CancellationToken)"/>
    public Task ResizeAsync(UI.WindowLayout size, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(size);
        return ResizeAsync(size.Width, size.Height, ct);
    }

    /// <summary>
    /// Resizes the window and sets the GUI scale of <paramref name="layout"/>, runs a frame, and
    /// puts the size and the scale back when the result is disposed.
    /// </summary>
    public async Task<IAsyncDisposable> UseAsync(UI.WindowLayout layout, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(layout);
        UI.WindowLayout.Checked(layout);
        HeadlessClient client = EngineOwner();
        (int width, int height) previous = (CurrentWidth, CurrentHeight);
        IDisposable scale = client.Settings.Apply(ClientSettingsProfile.Of(layout.ToString(), ("guiScale", layout.GuiScale)));
        try
        {
            await ResizeAsync(layout.Width, layout.Height, ct).ConfigureAwait(false);
        }
        catch
        {
            scale.Dispose();
            throw;
        }

        return new LayoutRestore(this, client, previous, scale);
    }

    /// <summary>
    /// Puts the window back to its boot size, between tests. The boot size may be below the game's
    /// smallest window, which a player's resize would enlarge, so it is applied as the boot did.
    /// </summary>
    internal void RestoreBootSize()
    {
        if (Owner is not { IsEngineMode: true, IsDisposed: false } client || _disposed) return;
        client.RunOnClientThread(() =>
        {
            ApplySize(client, Width, Height);

            // What a resize and a scale change left in the client's settings.
            ClientSettings.ScreenWidth = _bootScreenSize.Width;
            ClientSettings.ScreenHeight = _bootScreenSize.Height;
            if (ClientSettings.GUIScale != _bootGuiScale) ClientSettings.GUIScale = _bootGuiScale;
        });
    }

    // What SetWindowSize does, without the game's 600x400 floor. Client thread.
    private void ApplySize(HeadlessClient client, int width, int height)
    {
        if (client.Platform.WindowSize.Width == width && client.Platform.WindowSize.Height == height) return;
        NativeWindow.ClientSize = new Vector2i(width, height);
        client.Platform.RebuildFrameBuffers();
        client.Platform.WindowSize.Width = width;
        client.Platform.WindowSize.Height = height;
        client.Platform.TriggerWindowResized(width, height);
    }

    private HeadlessClient EngineOwner()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(HeadlessWindow));
        HeadlessClient client = Owner ?? throw new InvalidOperationException("The window belongs to no client yet.");
        if (!client.IsEngineMode) throw new NotSupportedException("Only an engine-mode client's window is resized.");
        return client;
    }

    private sealed class LayoutRestore(HeadlessWindow window, HeadlessClient client, (int Width, int Height) size, IDisposable scale) : IAsyncDisposable
    {
        private bool _disposed;

        public async ValueTask DisposeAsync()
        {
            if (_disposed) return;
            _disposed = true;
            if (client.IsDisposed) return;

            // The scale is put back even when the size cannot be, and its own failure does not
            // hide the size's.
            try
            {
                client.RunOnClientThread(() => window.ApplySize(client, size.Width, size.Height));
            }
            catch
            {
                try
                {
                    scale.Dispose();
                }
                catch
                {
                }

                throw;
            }

            scale.Dispose();
            await client.Frame().ConfigureAwait(false);
        }
    }
}
