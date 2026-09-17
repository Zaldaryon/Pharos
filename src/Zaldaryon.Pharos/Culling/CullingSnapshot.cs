using System.Collections.Generic;
using Zaldaryon.Pharos.Timing;

namespace Zaldaryon.Pharos.Culling;

/// <summary>
/// Immutable snapshot of per-frame frustum culling decisions captured on demand.
/// Call <see cref="CullingInspector.Snapshot"/> to obtain one.
/// </summary>
public sealed record CullingSnapshot
{
    /// <summary>
    /// Chunk positions that passed both the frustum test and the view-distance range check.
    /// </summary>
    public IReadOnlyList<ChunkPos> VisibleChunks { get; init; } = [];

    /// <summary>
    /// Chunk positions that failed the frustum or view-distance test.
    /// </summary>
    public IReadOnlyList<ChunkPos> CulledChunks { get; init; } = [];

    /// <summary>
    /// Chunk positions that passed the frustum test but are marked invisible by the
    /// occlusion culler (cave culling). Subset of <see cref="VisibleChunks"/>.
    /// </summary>
    public IReadOnlyList<ChunkPos> OcclusionCulledChunks { get; init; } = [];

    /// <summary>
    /// The six frustum plane equations at the time of the snapshot.
    /// Index order: Near 0, Left 1, Right 2, Top 3, Bottom 4, Far 5.
    /// </summary>
    public IReadOnlyList<FrustumPlane> FrustumPlanes { get; init; } = [];

    /// <summary>Number of frustum tests performed during this snapshot.</summary>
    public int TestCount { get; init; }

    /// <summary>Total loaded chunks in the world map at the time of the snapshot.</summary>
    public int TotalChunks { get; init; }

    /// <summary>
    /// Fraction of loaded chunks culled by frustum or view distance.
    /// Zero when TotalChunks is zero.
    /// </summary>
    public double CullingRatio => TotalChunks > 0
        ? (double)CulledChunks.Count / TotalChunks
        : 0.0;

    public static readonly CullingSnapshot Empty = new();
}
