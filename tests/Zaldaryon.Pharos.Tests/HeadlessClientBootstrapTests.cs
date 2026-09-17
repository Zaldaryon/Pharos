using OpenTK.Graphics.OpenGL;
using Xunit;
using Zaldaryon.Pharos.Bootstrap;
using Zaldaryon.Pharos.Core;
using Zaldaryon.Pharos.Platform;

namespace Zaldaryon.Pharos.Tests;

[CollectionDefinition("Sequential", DisableParallelization = true)]
public class SequentialTestCollection { }

[Collection("Sequential")]
public class HeadlessClientBootstrapTests
{
    static HeadlessClientBootstrapTests()
    {
        HeadlessPlatformResolver.Initialize();
    }

    [Fact]
    public void HeadlessWindow_Should_CreateOffscreenGlContextAndCompleteFramebuffer()
    {
        HeadlessClientOptions options = new()
        {
            Width = 1280,
            Height = 720
        };

        using HeadlessWindow window = new(options);

        Assert.NotNull(window.NativeWindow);
        Assert.False(window.NativeWindow.IsVisible, "Offscreen GLFW window must not be visible");

        Assert.NotNull(window.Framebuffer);
        Assert.True(window.Framebuffer.FboId > 0, "Offscreen FBO must be created with a valid ID");
        Assert.True(window.Framebuffer.ColorTextureId > 0, "Color texture must be attached");
        Assert.True(window.Framebuffer.DepthStencilRboId > 0, "Depth/stencil renderbuffer must be attached");
        Assert.Equal(1280, window.Framebuffer.Width);
        Assert.Equal(720, window.Framebuffer.Height);

        FramebufferErrorCode status = GL.CheckFramebufferStatus(FramebufferTarget.Framebuffer);
        Assert.Equal(FramebufferErrorCode.FramebufferComplete, status);
    }

    [Fact]
    public void HeadlessClientBootstrap_Should_InitializeClientMainInHeadlessMode()
    {
        HeadlessClientOptions options = new()
        {
            Width = 1280,
            Height = 720,
            DisableAudio = true
        };

        using HeadlessClient headlessClient = HeadlessClientBootstrap.Boot(options);

        Assert.NotNull(headlessClient);
        Assert.NotNull(headlessClient.Client);
        Assert.NotNull(headlessClient.Platform);
        Assert.NotNull(headlessClient.ScreenManager);
        Assert.NotNull(headlessClient.RunningGameScreen);
        Assert.NotNull(headlessClient.Window);
        Assert.NotNull(headlessClient.Framebuffer);

        Assert.False(headlessClient.Window.NativeWindow.IsVisible, "Window must remain invisible in headless mode");
        Assert.True(headlessClient.Framebuffer.FboId > 0, "FBO must be valid");

        // Verify that core ClientMain subsystems reached initialized state
        Assert.NotNull(headlessClient.Client.Platform);
        Assert.NotNull(headlessClient.Client.MainCamera);
        Assert.NotNull(headlessClient.Client.BlockAtlasManager);
        Assert.NotNull(headlessClient.Client.ItemAtlasManager);
        Assert.NotNull(headlessClient.Client.EntityAtlasManager);
        Assert.NotNull(headlessClient.Client.WorldMap);
    }

    [Fact]
    public void HeadlessClient_Should_ClearAndReadPixelsFromOffscreenFbo()
    {
        HeadlessClientOptions options = new()
        {
            Width = 1280,
            Height = 720
        };

        using HeadlessClient headlessClient = HeadlessClientBootstrap.Boot(options);

        // Bind offscreen FBO and clear to solid blue (0.0, 0.0, 1.0, 1.0)
        headlessClient.Framebuffer.Bind();
        GL.ClearColor(0f, 0f, 1f, 1f);
        GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);

        // Read back pixel at (0, 0)
        byte[] pixel = new byte[4];
        GL.ReadPixels(0, 0, 1, 1, PixelFormat.Rgba, PixelType.UnsignedByte, pixel);

        Assert.Equal(0, pixel[0]);    // R
        Assert.Equal(0, pixel[1]);    // G
        Assert.Equal(255, pixel[2]);  // B
        Assert.Equal(255, pixel[3]);  // A
    }

    [Fact]
    public void HeadlessClient_Should_MaintainIsolatedDataPath()
    {
        string customDataPath = Path.Combine(Path.GetTempPath(), "pharos-custom-test-" + Guid.NewGuid().ToString("N")[..8]);

        HeadlessClientOptions options = new()
        {
            DataPath = customDataPath
        };

        using (HeadlessClient headlessClient = HeadlessClientBootstrap.Boot(options))
        {
            Assert.Equal(customDataPath, Vintagestory.API.Config.GamePaths.DataPath);
            Assert.True(Directory.Exists(customDataPath), "Isolated DataPath must be created on disk");
        }

        if (Directory.Exists(customDataPath))
        {
            Directory.Delete(customDataPath, recursive: true);
        }

        Assert.False(Directory.Exists(customDataPath), "DataPath must be cleaned up on disposal");
    }
}
