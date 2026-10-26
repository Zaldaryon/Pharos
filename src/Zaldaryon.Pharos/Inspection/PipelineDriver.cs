using System.Reflection;
using OpenTK.Graphics.OpenGL;
using SkiaSharp;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.Client.NoObf;
using Zaldaryon.Pharos.Core;

namespace Zaldaryon.Pharos.Inspection;

/// <summary>A snapshot of a framebuffer's metadata.</summary>
public sealed record FramebufferInfo(
    EnumFrameBuffer Buffer,
    int Width,
    int Height,
    int ColorTextureId,
    int DepthTextureId,
    bool IsBound);

/// <summary>A captured framebuffer with pixel data.</summary>
public sealed record FramebufferCapture(
    EnumFrameBuffer Buffer,
    int Width,
    int Height,
    byte[] Pixels,
    bool HasAlpha)
{
    /// <summary>Saves the capture to a PNG file.</summary>
    public void SaveToPng(string path)
    {
        using var bitmap = new SKBitmap(new SKImageInfo(Width, Height, SKColorType.Rgba8888, HasAlpha ? SKAlphaType.Unpremul : SKAlphaType.Opaque));
        System.Runtime.InteropServices.Marshal.Copy(Pixels, 0, bitmap.GetPixels(), Pixels.Length);

        // Flip vertically (OpenGL is bottom-up)
        using var flipped = new SKBitmap(Width, Height, bitmap.ColorType, bitmap.AlphaType);
        using var canvas = new SKCanvas(flipped);
        canvas.Scale(1, -1, 0, Height / 2f);
        canvas.DrawBitmap(bitmap, 0, 0);

        using var stream = System.IO.File.OpenWrite(path);
        flipped.Encode(stream, SKEncodedImageFormat.Png, 100);
    }

    /// <summary>Gets pixel color at specified coordinates.</summary>
    public (byte R, byte G, byte B, byte A) GetPixel(int x, int y)
    {
        if (x < 0 || x >= Width || y < 0 || y >= Height)
            throw new ArgumentOutOfRangeException($"Pixel ({x}, {y}) out of bounds ({Width}x{Height})");

        // Flip Y coordinate (OpenGL is bottom-up)
        int flippedY = Height - 1 - y;
        int offset = (flippedY * Width + x) * 4;
        return (Pixels[offset], Pixels[offset + 1], Pixels[offset + 2], Pixels[offset + 3]);
    }
}

/// <summary>Information about a render stage in the pipeline.</summary>
public sealed record RenderStageInfo(string Name, int Order, bool IsEnabled);

/// <summary>
/// Reads and inspects the client's render pipeline: framebuffers, stages, and captures.
/// </summary>
public sealed class PipelineDriver
{
    private readonly HeadlessClient _client;

    internal PipelineDriver(HeadlessClient client) => _client = client;

    /// <summary>Lists all available framebuffer slots and their current state.</summary>
    public IReadOnlyList<FramebufferInfo> Framebuffers() => _client.RunOnClientThread(() =>
    {
        var result = new List<FramebufferInfo>();
        var platform = _client.Platform;
        var framebuffers = platform.FrameBuffers;
        var currentFb = platform.CurrentFrameBuffer;

        foreach (EnumFrameBuffer buffer in Enum.GetValues<EnumFrameBuffer>())
        {
            int index = (int)buffer;
            if (index >= 0 && index < framebuffers.Count)
            {
                var fb = framebuffers[index];
                if (fb != null)
                {
                    result.Add(new FramebufferInfo(
                        buffer,
                        fb.Width,
                        fb.Height,
                        fb.ColorTextureIds?.Length > 0 ? fb.ColorTextureIds[0] : 0,
                        fb.DepthTextureId,
                        currentFb == fb));
                }
            }
        }

        return result;
    });

    /// <summary>Gets information about a specific framebuffer.</summary>
    public FramebufferInfo? GetFramebuffer(EnumFrameBuffer buffer) => _client.RunOnClientThread(() =>
    {
        var platform = _client.Platform;
        var framebuffers = platform.FrameBuffers;
        int index = (int)buffer;

        if (index < 0 || index >= framebuffers.Count)
            return null;

        var fb = framebuffers[index];
        if (fb == null)
            return null;

        return new FramebufferInfo(
            buffer,
            fb.Width,
            fb.Height,
            fb.ColorTextureIds?.Length > 0 ? fb.ColorTextureIds[0] : 0,
            fb.DepthTextureId,
            platform.CurrentFrameBuffer == fb);
    });

    /// <summary>
    /// Captures pixel data from a framebuffer.
    /// Must be called at an appropriate point in the render loop when the framebuffer has content.
    /// </summary>
    public FramebufferCapture? Capture(EnumFrameBuffer buffer, bool withAlpha = false) => _client.RunOnClientThread(() =>
    {
        var platform = _client.Platform;
        var framebuffers = platform.FrameBuffers;
        int index = (int)buffer;

        if (index < 0 || index >= framebuffers.Count)
            return null;

        var fb = framebuffers[index];
        if (fb == null || fb.Width <= 0 || fb.Height <= 0)
            return null;

        // Save current framebuffer binding
        var previousFb = platform.CurrentFrameBuffer;

        try
        {
            // Bind the target framebuffer
            platform.LoadFrameBuffer(buffer);

            // Allocate buffer for pixel data (RGBA)
            var pixels = new byte[fb.Width * fb.Height * 4];

            // Read pixels from GPU
            GL.ReadPixels(0, 0, fb.Width, fb.Height,
                PixelFormat.Rgba, PixelType.UnsignedByte, pixels);

            return new FramebufferCapture(buffer, fb.Width, fb.Height, pixels, withAlpha);
        }
        finally
        {
            // Restore previous framebuffer
            if (previousFb != null)
            {
                platform.CurrentFrameBuffer = previousFb;
            }
        }
    });

    /// <summary>
    /// Captures the current screen (primary framebuffer or backbuffer).
    /// </summary>
    public FramebufferCapture CaptureScreen(bool withAlpha = false) => _client.RunOnClientThread(() =>
    {
        var platform = _client.Platform;
        var size = platform.WindowSize;

        // Bind to default framebuffer (screen)
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);

        var pixels = new byte[size.Width * size.Height * 4];
        GL.ReadPixels(0, 0, size.Width, size.Height,
            PixelFormat.Rgba, PixelType.UnsignedByte, pixels);

        return new FramebufferCapture(EnumFrameBuffer.Primary, size.Width, size.Height, pixels, withAlpha);
    });

    /// <summary>Lists the active render stages in order.</summary>
    public IReadOnlyList<RenderStageInfo> RenderStages() => _client.RunOnClientThread(() =>
    {
        var result = new List<RenderStageInfo>();

        // Get render stages from client systems
        var game = _client.Client;
        var systems = game?.clientSystems;
        if (systems == null) return result;

        int order = 0;
        foreach (var system in systems)
        {
            if (system != null)
            {
                result.Add(new RenderStageInfo(
                    system.GetType().Name,
                    order++,
                    true)); // Systems are enabled if they exist in the list
            }
        }

        return result;
    });

    /// <summary>Gets the current shadow map quality setting.</summary>
    public int ShadowMapQuality => _client.RunOnClientThread(() =>
    {
        return ClientSettings.ShadowMapQuality;
    });

    /// <summary>Gets the current SSAO quality setting.</summary>
    public int SsaoQuality => _client.RunOnClientThread(() =>
    {
        return ClientSettings.SSAOQuality;
    });

    /// <summary>Gets the current bloom setting.</summary>
    public bool BloomEnabled => _client.RunOnClientThread(() =>
    {
        return ClientSettings.Bloom;
    });

    /// <summary>Whether god rays are enabled.</summary>
    public bool GodRaysEnabled => _client.RunOnClientThread(() =>
    {
        // GodRays is a bool in some versions, int in others
        return ClientSettings.ShadowMapQuality > 0; // Approximation: god rays require shadow maps
    });

    /// <summary>Whether FXAA is enabled.</summary>
    public bool FxaaEnabled => _client.RunOnClientThread(() =>
    {
        return ClientSettings.FXAA;
    });
}
