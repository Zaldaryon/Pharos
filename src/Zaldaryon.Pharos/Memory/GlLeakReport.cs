namespace Zaldaryon.Pharos.Memory;

/// <summary>
/// Immutable report of OpenGL resource leaks detected between a baseline and current snapshot.
/// Leaks are computed as allocations - deletions for each resource type.
/// </summary>
public sealed record GlLeakReport
{
    /// <summary>
    /// Number of leaked GL buffers (allocations that were never deleted).
    /// Computed as BufferAllocations - BufferDeletions from the recording window.
    /// </summary>
    public int BufferLeaks { get; init; }

    /// <summary>
    /// Number of leaked textures (allocations that were never deleted).
    /// Computed as TextureAllocations - TextureDeletions from the recording window.
    /// </summary>
    public int TextureLeaks { get; init; }

    /// <summary>
    /// Number of leaked vertex array objects (allocations that were never deleted).
    /// Computed as VertexArrayAllocations - VertexArrayDeletions from the recording window.
    /// </summary>
    public int VAOLeaks { get; init; }

    /// <summary>
    /// Growth in unmanaged memory (bytes) during the recording window.
    /// Negative values indicate memory was reclaimed.
    /// </summary>
    public long UnmanagedGrowthBytes { get; init; }

    /// <summary>
    /// True if any resource leaks were detected (BufferLeaks > 0 || TextureLeaks > 0 || VAOLeaks > 0).
    /// Note: UnmanagedGrowthBytes is not considered a leak for this property.
    /// </summary>
    public bool HasLeaks => BufferLeaks > 0 || TextureLeaks > 0 || VAOLeaks > 0;

    /// <summary>
    /// Total number of leaked GL resources across all categories.
    /// </summary>
    public int TotalResourceLeaks => BufferLeaks + TextureLeaks + VAOLeaks;

    /// <summary>
    /// Empty report with no leaks and zero memory growth.
    /// </summary>
    public static readonly GlLeakReport Empty = new();
}
