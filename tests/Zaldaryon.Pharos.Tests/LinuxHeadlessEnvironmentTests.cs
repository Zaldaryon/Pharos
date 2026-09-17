using System;
using Xunit;
using Zaldaryon.Pharos.Bootstrap;
using Zaldaryon.Pharos.Core;
using Zaldaryon.Pharos.Platform;

namespace Zaldaryon.Pharos.Tests;

[Collection("Sequential")]
public class LinuxHeadlessEnvironmentTests
{
    static LinuxHeadlessEnvironmentTests()
    {
        HeadlessPlatformResolver.Initialize();
    }

    [Fact]
    public void ConfigureMesaEnvironment_Should_SetExpectedEnvironmentVariables()
    {
        HeadlessClientOptions options = new()
        {
            ForceSoftwareRendering = true,
            MesaGlVersionOverride = "4.5",
            MesaGlslVersionOverride = "450"
        };

        LinuxHeadlessEnvironment.ConfigureMesaEnvironment(options);

        Assert.Equal("1", Environment.GetEnvironmentVariable("LIBGL_ALWAYS_SOFTWARE"));
        Assert.Equal("llvmpipe", Environment.GetEnvironmentVariable("GALLIUM_DRIVER"));
        Assert.Equal("llvmpipe", Environment.GetEnvironmentVariable("MESA_LOADER_DRIVER_OVERRIDE"));
        Assert.Equal("0", Environment.GetEnvironmentVariable("LIBGL_ALWAYS_INDIRECT"));
        Assert.Equal("4.5", Environment.GetEnvironmentVariable("MESA_GL_VERSION_OVERRIDE"));
        Assert.Equal("450", Environment.GetEnvironmentVariable("MESA_GLSL_VERSION_OVERRIDE"));
    }

    [Theory]
    [InlineData("llvmpipe (LLVM 15.0.7, 256 bits)", true)]
    [InlineData("Mesa Gallium driver with llvmpipe", true)]
    [InlineData("Gallium 0.4 on softpipe", true)]
    [InlineData("swrast", true)]
    [InlineData("Software Rasterizer", true)]
    [InlineData("Microsoft Basic Render Driver", true)]
    [InlineData("NVIDIA GeForce RTX 4090/PCIe/SSE2", false)]
    [InlineData("AMD Radeon RX 7900 XTX", false)]
    [InlineData("Intel(R) Arc(TM) A770 Graphics", false)]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("   ", false)]
    public void IsSoftwareRenderer_Should_CorrectlyIdentifySoftwareRasterizers(string? renderer, bool expected)
    {
        bool actual = LinuxHeadlessEnvironment.IsSoftwareRenderer(renderer);
        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData("4.5 (Core Profile) Mesa 23.2.1-1ubuntu3.1", 4, 5)]
    [InlineData("4.3.0 - Build 31.0.101.4575", 4, 3)]
    [InlineData("4.6.0 NVIDIA 535.104.05", 4, 6)]
    [InlineData("OpenGL ES 3.2 Mesa 23.0", 3, 2)]
    [InlineData("", 0, 0)]
    [InlineData(null, 0, 0)]
    [InlineData("no-numbers-here", 0, 0)]
    public void ParseGlVersion_Should_ExtractMajorAndMinorVersion(string? versionString, int expectedMajor, int expectedMinor)
    {
        Version version = LinuxHeadlessEnvironment.ParseGlVersion(versionString);
        Assert.Equal(expectedMajor, version.Major);
        Assert.Equal(expectedMinor, version.Minor);
    }

    [Fact]
    public void GlRendererInfo_SupportsCoreVersion_Should_ValidateMinimumThresholds()
    {
        GlRendererInfo info = new(
            Vendor: "Mesa",
            Renderer: "llvmpipe (LLVM 15.0.7, 256 bits)",
            Version: "4.5 (Core Profile) Mesa 23.2.1",
            ShadingLanguageVersion: "4.50",
            ParsedVersion: new Version(4, 5),
            IsSoftwareRenderer: true);

        Assert.True(info.SupportsCoreVersion(4, 3));
        Assert.True(info.SupportsCoreVersion(4, 5));
        Assert.False(info.SupportsCoreVersion(4, 6));
        Assert.True(info.IsSoftwareRenderer);
    }

    [Fact]
    public void ValidateDisplayServer_Should_ApplyDisplayOverrideWhenSpecified()
    {
        string? originalDisplay = Environment.GetEnvironmentVariable("DISPLAY");
        try
        {
            string testDisplay = ":88";
            if (OperatingSystem.IsLinux())
            {
                LinuxHeadlessEnvironment.ValidateDisplayServer(testDisplay);
                Assert.Equal(testDisplay, Environment.GetEnvironmentVariable("DISPLAY"));
            }
            else
            {
                LinuxHeadlessEnvironment.ValidateDisplayServer(testDisplay);
                // On non-Linux, ValidateDisplayServer does not mutate DISPLAY
                Assert.Equal(originalDisplay, Environment.GetEnvironmentVariable("DISPLAY"));
            }
        }
        finally
        {
            Environment.SetEnvironmentVariable("DISPLAY", originalDisplay);
        }
    }

    [Fact]
    public void ValidateDisplayServer_Should_ThrowWhenNoDisplayServerAvailableOnLinux()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        string? originalDisplay = Environment.GetEnvironmentVariable("DISPLAY");
        string? originalWayland = Environment.GetEnvironmentVariable("WAYLAND_DISPLAY");

        try
        {
            Environment.SetEnvironmentVariable("DISPLAY", null);
            Environment.SetEnvironmentVariable("WAYLAND_DISPLAY", null);

            Assert.Throws<InvalidOperationException>(() => LinuxHeadlessEnvironment.ValidateDisplayServer());
        }
        finally
        {
            Environment.SetEnvironmentVariable("DISPLAY", originalDisplay);
            Environment.SetEnvironmentVariable("WAYLAND_DISPLAY", originalWayland);
        }
    }

    [Fact]
    public void HeadlessClient_Should_ExposeRendererInfoWithValidOpenGLContext()
    {
        HeadlessClientOptions options = new()
        {
            Width = 1280,
            Height = 720,
            DisableAudio = true
        };

        using HeadlessClient client = HeadlessClientBootstrap.Boot(options);

        Assert.NotNull(client.RendererInfo);
        Assert.NotNull(client.Window.RendererInfo);
        Assert.False(string.IsNullOrWhiteSpace(client.RendererInfo.Vendor));
        Assert.False(string.IsNullOrWhiteSpace(client.RendererInfo.Renderer));
        Assert.False(string.IsNullOrWhiteSpace(client.RendererInfo.Version));
        Assert.True(client.RendererInfo.SupportsCoreVersion(4, 3), $"OpenGL context must support at least 4.3 core, found {client.RendererInfo.Version}");
    }
}
