using System;
using OpenTK.Graphics.OpenGL;
using Xunit;
using Zaldaryon.Pharos.Bootstrap;
using Zaldaryon.Pharos.Core;
using Zaldaryon.Pharos.Platform;

namespace Zaldaryon.Pharos.Tests;

[Collection("Sequential")]
public class WindowsHeadlessWindowTests
{
    static WindowsHeadlessWindowTests()
    {
        HeadlessPlatformResolver.Initialize();
    }

    [Fact]
    public void Window_Should_BeInvisibleAndUnfocusedOnCreation()
    {
        HeadlessClientOptions options = new()
        {
            Width = 1280,
            Height = 720,
            DisableAudio = true
        };

        using HeadlessClient client = HeadlessClientBootstrap.Boot(options);

        Assert.False(client.Window.IsVisible);
        Assert.False(client.Window.NativeWindow.IsVisible);
        Assert.False(client.Window.IsFocused);
        Assert.Equal(OpenTK.Windowing.Common.WindowBorder.Hidden, client.Window.NativeWindow.WindowBorder);
    }

    [Fact]
    public void Window_Should_SupportConfigurableFboResolution()
    {
        HeadlessClientOptions options = new()
        {
            Width = 800,
            Height = 600,
            DisableAudio = true
        };

        using HeadlessClient client = HeadlessClientBootstrap.Boot(options);

        Assert.Equal(800, client.Framebuffer.Width);
        Assert.Equal(600, client.Framebuffer.Height);
        Assert.Equal(800, client.Window.Width);
        Assert.Equal(600, client.Window.Height);
        Assert.Equal(800, client.Window.NativeWindow.ClientSize.X);
        Assert.Equal(600, client.Window.NativeWindow.ClientSize.Y);
        Assert.Equal(800, client.Platform.WindowSize.Width);
        Assert.Equal(600, client.Platform.WindowSize.Height);
    }

    [Fact]
    public void Window_Should_EnforceWin32ToolWindowAndNoTaskbarOnWindows()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        HeadlessClientOptions options = new()
        {
            Width = 1280,
            Height = 720,
            DisableAudio = true
        };

        using HeadlessClient client = HeadlessClientBootstrap.Boot(options);

        IntPtr hwnd = client.Window.Win32WindowHandle;
        Assert.NotEqual(IntPtr.Zero, hwnd);

        long exStyle = Win32WindowInterop.GetWindowLongPtr(hwnd, Win32WindowInterop.GWL_EXSTYLE).ToInt64();

        // WS_EX_TOOLWINDOW must be present so Windows does not show it on the taskbar or in Alt+Tab
        Assert.True((exStyle & Win32WindowInterop.WS_EX_TOOLWINDOW) != 0, "Window must have WS_EX_TOOLWINDOW style set.");

        // WS_EX_APPWINDOW must not be present so it doesn't force a taskbar button
        Assert.True((exStyle & Win32WindowInterop.WS_EX_APPWINDOW) == 0, "Window must not have WS_EX_APPWINDOW style set.");

        // WS_EX_NOACTIVATE must be present to suppress focus grabbing
        Assert.True((exStyle & Win32WindowInterop.WS_EX_NOACTIVATE) != 0, "Window must have WS_EX_NOACTIVATE style set.");
    }

    [Fact]
    public void Window_Should_ThrowOnInvalidDimensions()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
        {
            using HeadlessWindow win = new(new HeadlessClientOptions { Width = 0, Height = 720 });
        });

        Assert.Throws<ArgumentOutOfRangeException>(() =>
        {
            using HeadlessWindow win = new(new HeadlessClientOptions { Width = 1280, Height = -100 });
        });

        Assert.Throws<ArgumentOutOfRangeException>(() =>
        {
            using HeadlessFramebuffer fbo = new(0, 720);
        });

        Assert.Throws<ArgumentOutOfRangeException>(() =>
        {
            using HeadlessFramebuffer fbo = new(1280, -10);
        });
    }

    [Fact]
    public void Framebuffer_Should_HaveValidAttachmentsAndCompleteStatus()
    {
        HeadlessClientOptions options = new()
        {
            Width = 1280,
            Height = 720,
            DisableAudio = true
        };

        using HeadlessClient client = HeadlessClientBootstrap.Boot(options);

        Assert.NotEqual(0, client.Framebuffer.FboId);
        Assert.NotEqual(0, client.Framebuffer.ColorTextureId);
        Assert.NotEqual(0, client.Framebuffer.DepthStencilRboId);

        client.Framebuffer.Bind();
        FramebufferErrorCode status = GL.CheckFramebufferStatus(FramebufferTarget.Framebuffer);
        Assert.Equal(FramebufferErrorCode.FramebufferComplete, status);
    }

    [Fact]
    public void Window_Should_DisposeCleanlyAndIdempotently()
    {
        HeadlessClientOptions options = new()
        {
            Width = 1280,
            Height = 720,
            DisableAudio = true
        };

        HeadlessWindow window = new(options);
        Assert.True(window.Framebuffer.FboId > 0);

        window.Dispose();
        window.Dispose(); // Multiple disposals must not throw

        Assert.False(window.IsVisible);
        Assert.False(window.IsFocused);
        Assert.Equal(IntPtr.Zero, window.Win32WindowHandle);
    }
}
