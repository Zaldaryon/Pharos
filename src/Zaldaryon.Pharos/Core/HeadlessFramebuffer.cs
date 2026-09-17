using OpenTK.Graphics.OpenGL;

namespace Zaldaryon.Pharos.Core;

/// <summary>
/// Encapsulates an offscreen OpenGL Framebuffer Object (FBO) with color and depth-stencil attachments.
/// </summary>
public sealed class HeadlessFramebuffer : IDisposable
{
    private bool _disposed;

    public int FboId { get; }
    public int ColorTextureId { get; }
    public int DepthStencilRboId { get; }
    public int Width { get; }
    public int Height { get; }

    public HeadlessFramebuffer(int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);

        Width = width;
        Height = height;

        // Generate Framebuffer Object
        FboId = GL.GenFramebuffer();
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, FboId);

        // Color texture attachment (RGBA8)
        ColorTextureId = GL.GenTexture();
        GL.BindTexture(TextureTarget.Texture2D, ColorTextureId);
        GL.TexImage2D(
            TextureTarget.Texture2D,
            0,
            PixelInternalFormat.Rgba8,
            width,
            height,
            0,
            PixelFormat.Rgba,
            PixelType.UnsignedByte,
            IntPtr.Zero);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
        GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
        GL.FramebufferTexture2D(
            FramebufferTarget.Framebuffer,
            FramebufferAttachment.ColorAttachment0,
            TextureTarget.Texture2D,
            ColorTextureId,
            0);

        // Depth-stencil renderbuffer attachment
        DepthStencilRboId = GL.GenRenderbuffer();
        GL.BindRenderbuffer(RenderbufferTarget.Renderbuffer, DepthStencilRboId);
        GL.RenderbufferStorage(RenderbufferTarget.Renderbuffer, RenderbufferStorage.Depth24Stencil8, width, height);
        GL.FramebufferRenderbuffer(
            FramebufferTarget.Framebuffer,
            FramebufferAttachment.DepthStencilAttachment,
            RenderbufferTarget.Renderbuffer,
            DepthStencilRboId);

        FramebufferErrorCode status = GL.CheckFramebufferStatus(FramebufferTarget.Framebuffer);
        if (status != FramebufferErrorCode.FramebufferComplete)
        {
            throw new InvalidOperationException($"Failed to create complete offscreen framebuffer: {status}");
        }

        // Set viewport
        GL.Viewport(0, 0, width, height);
    }

    public void Bind()
    {
        if (_disposed) return;
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, FboId);
        GL.Viewport(0, 0, Width, Height);
    }

    public void Unbind()
    {
        GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
    }

    /// <summary>
    /// Reads back the current framebuffer contents as a top-down RGBA pixel snapshot.
    /// Binds this FBO before reading so the method is safe to call at any point after a frame step.
    /// </summary>
    public FramebufferSnapshot Capture()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(HeadlessFramebuffer));

        Bind();

        byte[] pixels = new byte[Width * Height * 4];
        GL.ReadPixels(0, 0, Width, Height, PixelFormat.Rgba, PixelType.UnsignedByte, pixels);

        // GL.ReadPixels returns rows bottom-up; flip to top-down for standard image conventions.
        FlipVertically(pixels, Width, Height);

        return new FramebufferSnapshot(pixels, Width, Height);
    }

    /// <summary>
    /// Captures the current framebuffer and saves it as a PNG file at the given path.
    /// </summary>
    public void CaptureToFile(string path) => Capture().SaveToPng(path);

    /// <summary>
    /// Captures the current framebuffer and compares it against the provided RGBA reference bytes.
    /// The reference must have length Width * Height * 4 in top-down RGBA order.
    /// </summary>
    public FramebufferComparisonResult CompareWith(byte[] reference, float tolerance = 0.01f)
        => Capture().Compare(reference, tolerance);

    /// <summary>
    /// Flips the rows of a flat RGBA byte array in-place (converts bottom-up to top-down or vice versa).
    /// </summary>
    private static void FlipVertically(byte[] rgba, int width, int height)
    {
        int stride = width * 4;
        byte[] row = new byte[stride];
        for (int y = 0; y < height / 2; y++)
        {
            int top = y * stride;
            int bottom = (height - 1 - y) * stride;
            System.Buffer.BlockCopy(rgba, top, row, 0, stride);
            System.Buffer.BlockCopy(rgba, bottom, rgba, top, stride);
            System.Buffer.BlockCopy(row, 0, rgba, bottom, stride);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (ColorTextureId != 0)
        {
            GL.DeleteTexture(ColorTextureId);
        }
        if (DepthStencilRboId != 0)
        {
            GL.DeleteRenderbuffer(DepthStencilRboId);
        }
        if (FboId != 0)
        {
            GL.DeleteFramebuffer(FboId);
        }
    }
}
