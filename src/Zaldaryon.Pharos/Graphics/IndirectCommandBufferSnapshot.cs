namespace Zaldaryon.Pharos.Graphics;

/// <summary>
/// Immutable snapshot of a GL_DRAW_INDIRECT_BUFFER binding event.
/// Captures the buffer ID, byte size, and estimated command count at bind time.
/// </summary>
public sealed record IndirectCommandBufferSnapshot
{
    /// <summary>OpenGL buffer object ID bound to GL_DRAW_INDIRECT_BUFFER.</summary>
    public int BufferId { get; init; }

    /// <summary>Size of the buffer in bytes at bind time (0 if unavailable).</summary>
    public long BufferSizeBytes { get; init; }

    /// <summary>
    /// Estimated number of DrawElementsIndirectCommand structures in the buffer.
    /// Computed as BufferSizeBytes / 20 (sizeof(DrawElementsIndirectCommand)).
    /// </summary>
    public int EstimatedCommandCount => BufferSizeBytes >= 20 ? (int)(BufferSizeBytes / 20) : 0;

    /// <summary>Timestamp when the binding was captured (Environment.TickCount64).</summary>
    public long TimestampTicks { get; init; }

    public static readonly IndirectCommandBufferSnapshot Empty = new();
}
