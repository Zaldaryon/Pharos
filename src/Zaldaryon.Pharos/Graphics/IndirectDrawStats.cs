namespace Zaldaryon.Pharos.Graphics;

/// <summary>
/// Immutable snapshot of indirect draw statistics captured during a recording window.
/// Tracks indirect dispatch counts, command totals, and direct draw fallback calls.
/// </summary>
public sealed record IndirectDrawStats
{
    /// <summary>Number of glMultiDrawElementsIndirect/glMultiDrawArraysIndirect calls dispatched.</summary>
    public int IndirectDispatchCount { get; init; }

    /// <summary>Total indirect commands submitted across all dispatches (sum of drawcount parameters).</summary>
    public int TotalCommandCount { get; init; }

    /// <summary>Total instance count across all indirect commands (estimated from API parameters).</summary>
    public int TotalInstanceCount { get; init; }

    /// <summary>Total bytes of indirect command buffer memory bound during the recording window.</summary>
    public long BufferBytesTotal { get; init; }

    /// <summary>Number of direct draw calls (DrawArrays, DrawElements) recorded as fallback.</summary>
    public int DirectDrawCalls { get; init; }

    /// <summary>
    /// Draw call collapse ratio: DirectDrawCalls / IndirectDispatchCount.
    /// Returns 0 if IndirectDispatchCount is 0.
    /// Higher values indicate more effective batching.
    /// </summary>
    public double CollapseRatio => IndirectDispatchCount > 0
        ? (double)DirectDrawCalls / IndirectDispatchCount
        : 0.0;

    /// <summary>Returns true if any indirect draw dispatches occurred.</summary>
    public bool HasIndirectDraws => IndirectDispatchCount > 0;

    /// <summary>Returns true if no indirect dispatches occurred, only direct draw calls.</summary>
    public bool IsFallbackMode => IndirectDispatchCount == 0 && DirectDrawCalls > 0;

    public static readonly IndirectDrawStats Empty = new();
}
