using OpenTK.Graphics.OpenGL;
using OpenTK.Mathematics;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.Desktop;
using OpenTK.Windowing.GraphicsLibraryFramework;
using Vintagestory.Client.NoObf;
using Zaldaryon.Pharos.Core;

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

    public HeadlessWindow(HeadlessClientOptions options)
    {
        Width = options.Width;
        Height = options.Height;

        // Ensure GLFW can run on any test thread
        GLFWProvider.CheckForMainThread = false;

        // Initialize GLFW with offscreen/invisible hints
        if (!GLFW.Init())
        {
            throw new InvalidOperationException("Failed to initialize GLFW.");
        }

        GLFW.WindowHint(WindowHintBool.Visible, false);
        GLFW.WindowHint(WindowHintBool.Focused, false);
        GLFW.WindowHint(WindowHintBool.Resizable, false);

        NativeWindowSettings nativeSettings = new()
        {
            Title = "Pharos Headless Client",
            ClientSize = new Vector2i(Width, Height),
            StartVisible = false,
            StartFocused = false,
            WindowState = WindowState.Normal,
            APIVersion = new Version(4, 3),
            Flags = ContextFlags.Default
        };

        GameWindowSettings gameSettings = GameWindowSettings.Default;

        NativeWindow = new GameWindowNative(gameSettings, nativeSettings);

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

        Framebuffer.Dispose();
        NativeWindow.Dispose();
    }
}
