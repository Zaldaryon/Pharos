namespace Zaldaryon.Pharos.Graphics;

/// <summary>
/// Immutable snapshot of GL command counts captured during a recording window.
/// </summary>
public sealed record GlCommandRecord
{
    /// <summary>Total glDrawArrays and glDrawElements calls recorded.</summary>
    public int DrawCalls { get; init; }

    /// <summary>Total glMultiDrawElements and glMultiDrawArrays calls recorded.</summary>
    public int MultiDrawCalls { get; init; }

    /// <summary>Total glMultiDrawElementsIndirect and glMultiDrawArraysIndirect calls recorded.</summary>
    public int IndirectDrawCalls { get; init; }

    /// <summary>Buffers created (each id of a glGenBuffers call counts).</summary>
    public int BufferAllocations { get; init; }

    /// <summary>Buffers deleted.</summary>
    public int BufferDeletions { get; init; }

    /// <summary>Vertex arrays created.</summary>
    public int VertexArrayAllocations { get; init; }

    /// <summary>Vertex arrays deleted.</summary>
    public int VertexArrayDeletions { get; init; }

    /// <summary>Textures created.</summary>
    public int TextureAllocations { get; init; }

    /// <summary>Textures deleted.</summary>
    public int TextureDeletions { get; init; }

    /// <summary>Framebuffers created.</summary>
    public int FramebufferAllocations { get; init; }

    /// <summary>Framebuffers deleted.</summary>
    public int FramebufferDeletions { get; init; }

    /// <summary>Renderbuffers created.</summary>
    public int RenderbufferAllocations { get; init; }

    /// <summary>Renderbuffers deleted.</summary>
    public int RenderbufferDeletions { get; init; }

    /// <summary>Total GL errors recorded via glGetError calls (populated when ErrorTracking is enabled).</summary>
    public int Errors { get; init; }

    /// <summary>Total of all draw-related calls: DrawCalls + MultiDrawCalls + IndirectDrawCalls.</summary>
    public int TotalDrawCalls => DrawCalls + MultiDrawCalls + IndirectDrawCalls;

    public static readonly GlCommandRecord Empty = new();
}
