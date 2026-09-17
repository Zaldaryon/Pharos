namespace Zaldaryon.Pharos.Memory;

/// <summary>
/// Immutable snapshot of the <c>MeshDataRecycler</c> pool state and hit/miss statistics.
/// Call <see cref="MemoryInspector.PoolSnapshot"/> to capture one.
/// </summary>
public sealed record MeshPoolSnapshot
{
    /// <summary>Number of small-size MeshData objects currently available in the pool.</summary>
    public int SmallPoolSize { get; init; }

    /// <summary>Number of medium-size MeshData objects currently available in the pool.</summary>
    public int MediumPoolSize { get; init; }

    /// <summary>Number of large-size MeshData objects currently available in the pool.</summary>
    public int LargePoolSize { get; init; }

    /// <summary>Number of disposed MeshData objects queued for recycling but not yet processed.</summary>
    public int PendingRecycleCount { get; init; }

    /// <summary>Total available objects across all pool size classes.</summary>
    public int TotalPoolSize => SmallPoolSize + MediumPoolSize + LargePoolSize;

    /// <summary>
    /// Number of <c>GetOrCreateMesh</c> calls that found and returned a recycled object
    /// since <see cref="MemoryInspector.Enable"/> or the last <see cref="MemoryInspector.Reset"/>.
    /// </summary>
    public long Hits { get; init; }

    /// <summary>
    /// Number of <c>GetOrCreateMesh</c> calls that had to allocate a new <c>MeshData</c>
    /// since <see cref="MemoryInspector.Enable"/> or the last <see cref="MemoryInspector.Reset"/>.
    /// </summary>
    public long Misses { get; init; }

    /// <summary>
    /// Fraction of pool lookups that returned a recycled object.
    /// Zero when no lookups have been recorded.
    /// </summary>
    public double HitRate => Hits + Misses > 0 ? (double)Hits / (Hits + Misses) : 0.0;

    public static readonly MeshPoolSnapshot Empty = new();
}
