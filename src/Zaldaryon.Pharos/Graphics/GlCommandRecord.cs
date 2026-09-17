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

    /// <summary>Total glGenBuffer and glGenBuffers calls recorded.</summary>
    public int BufferAllocations { get; init; }

    /// <summary>Total glDeleteBuffer and glDeleteBuffers calls recorded.</summary>
    public int BufferDeletions { get; init; }

    /// <summary>Total glGenVertexArray and glGenVertexArrays calls recorded.</summary>
    public int VertexArrayAllocations { get; init; }

    /// <summary>Total GL errors recorded via glGetError calls (populated when ErrorTracking is enabled).</summary>
    public int Errors { get; init; }

    /// <summary>Total of all draw-related calls: DrawCalls + MultiDrawCalls + IndirectDrawCalls.</summary>
    public int TotalDrawCalls => DrawCalls + MultiDrawCalls + IndirectDrawCalls;

    public static readonly GlCommandRecord Empty = new();
}
