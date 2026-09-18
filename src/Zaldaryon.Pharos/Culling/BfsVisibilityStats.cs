namespace Zaldaryon.Pharos.Culling;

/// <summary>
/// Statistics from a BFS visibility traversal, tracking depth limits,
/// opaque blockers, and traversal extent.
/// </summary>
public readonly record struct BfsVisibilityStats(
    int MaxDepthReached,
    int BlockedByOpaqueCount,
    int TotalNodesVisited,
    int BfsCallCount,
    int DepthLimit)
{
    /// <summary>
    /// True when MaxDepthReached does not exceed DepthLimit.
    /// </summary>
    public bool DepthLimitRespected => MaxDepthReached <= DepthLimit;

    /// <summary>
    /// Empty statistics with all counts at zero and limit at int.MaxValue.
    /// </summary>
    public static readonly BfsVisibilityStats Empty = new(0, 0, 0, 0, int.MaxValue);
}
