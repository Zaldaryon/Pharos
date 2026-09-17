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
