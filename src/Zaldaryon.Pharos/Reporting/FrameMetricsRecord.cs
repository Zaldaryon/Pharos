namespace Zaldaryon.Pharos.Reporting;

/// <summary>
/// Immutable record containing metrics captured for a single frame.
/// Used for diagnostic test reports with frame timing and render statistics.
/// </summary>
public sealed record FrameMetricsRecord
{
    /// <summary>
    /// Zero-based frame index within the test sequence.
    /// </summary>
    public int FrameIndex { get; init; }

    /// <summary>
    /// Frame time in milliseconds.
    /// </summary>
    public float FrameTimeMs { get; init; }

    /// <summary>
    /// Number of direct draw calls (glDrawArrays, glDrawElements, etc.).
    /// </summary>
    public int DrawCalls { get; init; }

    /// <summary>
    /// Number of indirect draw calls (glMultiDrawElementsIndirect, etc.).
    /// </summary>
    public int IndirectDrawCalls { get; init; }

    /// <summary>
    /// Number of chunks that were culled (frustum, occlusion, or view distance).
    /// </summary>
    public int CulledChunks { get; init; }

    /// <summary>
    /// Number of chunks that were visible and rendered.
    /// </summary>
    public int VisibleChunks { get; init; }

    /// <summary>
    /// Memory usage in bytes at the time of capture.
    /// </summary>
    public long MemoryBytes { get; init; }

    /// <summary>
    /// Total chunks processed (CulledChunks + VisibleChunks).
    /// </summary>
    public int TotalChunks => CulledChunks + VisibleChunks;

    /// <summary>
    /// Culling efficiency as a percentage (0-100).
    /// Returns 0 if no chunks were processed.
    /// </summary>
    public float CullingEfficiency => TotalChunks > 0 ? (float)CulledChunks / TotalChunks * 100f : 0f;

    /// <summary>
    /// Empty record with default values.
    /// </summary>
    public static readonly FrameMetricsRecord Empty = new();
}
