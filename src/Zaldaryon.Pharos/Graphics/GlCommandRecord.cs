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

    /// <summary>Shader program bindings (glUseProgram calls).</summary>
    public int ShaderProgramBindings { get; init; }

    /// <summary>Texture bindings (glBindTexture and glActiveTexture calls).</summary>
    public int TextureBindings { get; init; }

    /// <summary>Vertex array object bindings (glBindVertexArray calls).</summary>
    public int VaoBindings { get; init; }

    /// <summary>Framebuffer bindings (glBindFramebuffer calls).</summary>
    public int FramebufferBindings { get; init; }

    /// <summary>
    /// Delta of VS RuntimeStats.drawCallsCount during recording, for cross-verification.
    /// Non-zero indicates the game was rendering; zero with non-zero TotalDrawCalls suggests
    /// the proxy is capturing calls that VS doesn't count.
    /// </summary>
    public int RuntimeStatsDelta { get; init; }

    /// <summary>
    /// Breakdown of draw calls by method name for detailed analysis.
    /// Keys are method names like "DrawElements", "MultiDrawElements", etc.
    /// </summary>
    public IReadOnlyDictionary<string, int> DrawCallsByMethod { get; init; } = new Dictionary<string, int>();

    /// <summary>Total of all draw-related calls: DrawCalls + MultiDrawCalls + IndirectDrawCalls.</summary>
    public int TotalDrawCalls => DrawCalls + MultiDrawCalls + IndirectDrawCalls;

    /// <summary>Total state binding calls for performance analysis.</summary>
    public int TotalStateBindings => ShaderProgramBindings + TextureBindings + VaoBindings + FramebufferBindings;

    public static readonly GlCommandRecord Empty = new();
}
