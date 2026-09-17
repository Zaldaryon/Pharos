using System;
using OpenTK.Graphics.OpenGL;
using OpenTK.Mathematics;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.Desktop;
using OpenTK.Windowing.GraphicsLibraryFramework;
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
    public int Width { get; }
    public int Height { get; }

    public bool IsVisible => !_disposed && NativeWindow.IsVisible;
    public bool IsFocused => !_disposed && NativeWindow.IsFocused;

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
}
