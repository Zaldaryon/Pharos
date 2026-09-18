using Zaldaryon.Pharos.Timing;

namespace Zaldaryon.Pharos.Culling;

/// <summary>
/// Debug hook for BFS visibility traversal simulation. Performs 6-connected
/// neighbor traversal with depth limits and opaque chunk blocking for test scenarios.
/// </summary>
public sealed class BfsDebugHook
{
    private readonly HashSet<ChunkPos> _chunks;
    private readonly Func<ChunkPos, bool> _isOpaque;
    private readonly HashSet<ChunkPos> _visited = [];

    /// <summary>
    /// Chunks visited during the last Traverse call.
    /// </summary>
    public IReadOnlySet<ChunkPos> VisitedChunks => _visited;

    /// <summary>
    /// Creates a BFS debug hook with the given chunk set and opacity predicate.
    /// </summary>
    /// <param name="chunks">Set of chunks that exist in the simulated world.</param>
    /// <param name="isOpaque">Predicate returning true for opaque (blocking) chunks.</param>
    public BfsDebugHook(IEnumerable<ChunkPos> chunks, Func<ChunkPos, bool> isOpaque)
    {
        ArgumentNullException.ThrowIfNull(chunks);
        ArgumentNullException.ThrowIfNull(isOpaque);

        _chunks = [.. chunks];
        _isOpaque = isOpaque;
    }

    /// <summary>
    /// Performs BFS traversal from the start position up to depthLimit.
    /// Returns statistics about the traversal.
    /// </summary>
    public BfsVisibilityStats Traverse(ChunkPos start, int depthLimit)
    {
        _visited.Clear();

        if (!_chunks.Contains(start))
        {
            return new BfsVisibilityStats(0, 0, 0, 1, depthLimit);
        }

        Queue<(ChunkPos pos, int depth)> queue = [];
        queue.Enqueue((start, 0));
        _visited.Add(start);

        int maxDepthReached = 0;
        int blockedCount = 0;

        while (queue.Count > 0)
        {
            (ChunkPos current, int depth) = queue.Dequeue();
            maxDepthReached = Math.Max(maxDepthReached, depth);

            if (depth >= depthLimit)
            {
                continue;
            }

            // 6-connected neighbors
            ChunkPos[] neighbors =
            [
                new(current.X + 1, current.Y, current.Z),
                new(current.X - 1, current.Y, current.Z),
                new(current.X, current.Y + 1, current.Z),
                new(current.X, current.Y - 1, current.Z),
                new(current.X, current.Y, current.Z + 1),
                new(current.X, current.Y, current.Z - 1),
            ];

            foreach (ChunkPos neighbor in neighbors)
            {
                if (!_chunks.Contains(neighbor) || _visited.Contains(neighbor))
                {
                    continue;
                }

                _visited.Add(neighbor);

                if (_isOpaque(neighbor))
                {
                    blockedCount++;
                    // Opaque chunks block further traversal but are still counted as visited
                    continue;
                }

                queue.Enqueue((neighbor, depth + 1));
            }
        }

        return new BfsVisibilityStats(
            MaxDepthReached: maxDepthReached,
            BlockedByOpaqueCount: blockedCount,
            TotalNodesVisited: _visited.Count,
            BfsCallCount: 1,
            DepthLimit: depthLimit
        );
    }
}
