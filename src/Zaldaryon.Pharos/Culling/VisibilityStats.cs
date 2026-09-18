namespace Zaldaryon.Pharos.Culling;

/// <summary>
/// Per-frame visibility statistics derived from a <see cref="CullingSnapshot"/>.
/// Provides computed cull rates with safe zero-guards.
/// </summary>
public readonly record struct VisibilityStats(
    int ChunksEvaluated,
    int FrustumCulledCount,
    int OcclusionCulledCount,
    int VisibleCount)
{
    /// <summary>
    /// Fraction of evaluated chunks culled by frustum testing.
    /// Zero when no chunks were evaluated.
    /// </summary>
    public double FrustumCullRate => ChunksEvaluated > 0
        ? (double)FrustumCulledCount / ChunksEvaluated
        : 0.0;

    /// <summary>
    /// Fraction of evaluated chunks culled by occlusion testing.
    /// Zero when no chunks were evaluated.
    /// </summary>
    public double OcclusionCullRate => ChunksEvaluated > 0
        ? (double)OcclusionCulledCount / ChunksEvaluated
        : 0.0;

    /// <summary>
    /// Constructs VisibilityStats from a CullingSnapshot.
    /// </summary>
    public static VisibilityStats FromSnapshot(CullingSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        return new VisibilityStats(
            ChunksEvaluated: snapshot.TotalChunks,
            FrustumCulledCount: snapshot.CulledChunks.Count,
            OcclusionCulledCount: snapshot.OcclusionCulledChunks.Count,
            VisibleCount: snapshot.VisibleChunks.Count - snapshot.OcclusionCulledChunks.Count
        );
    }

    /// <summary>
    /// Empty statistics with all counts at zero.
    /// </summary>
    public static readonly VisibilityStats Empty = new(0, 0, 0, 0);
}
