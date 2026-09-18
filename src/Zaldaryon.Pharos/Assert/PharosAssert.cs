using Zaldaryon.Pharos.Culling;
using Zaldaryon.Pharos.Graphics;
using Zaldaryon.Pharos.Timing;

namespace Zaldaryon.Pharos.Assertions;

/// <summary>
/// Static assertion methods for validating indirect draw batching efficiency
/// and fallback mode behavior in Pharos test scenarios.
/// </summary>
public static class PharosAssert
{
    /// <summary>
    /// Asserts that draw calls were collapsed efficiently via indirect drawing.
    /// </summary>
    /// <param name="stats">Indirect draw statistics snapshot.</param>
    /// <param name="minOriginalDrawCalls">Minimum expected direct draw calls that would have occurred without batching.</param>
    /// <param name="maxDispatchedIndirectCalls">Maximum allowed indirect dispatch calls after batching.</param>
    /// <exception cref="PharosAssertException">Thrown when batching efficiency requirements are not met.</exception>
    public static void DrawCallsCollapsed(IndirectDrawStats stats, int minOriginalDrawCalls, int maxDispatchedIndirectCalls)
    {
        ArgumentNullException.ThrowIfNull(stats);

        if (stats.DirectDrawCalls < minOriginalDrawCalls)
        {
            throw new PharosAssertException(
                $"Expected at least {minOriginalDrawCalls} direct draw calls for batching baseline, but got {stats.DirectDrawCalls}.");
        }

        if (stats.IndirectDispatchCount > maxDispatchedIndirectCalls)
        {
            throw new PharosAssertException(
                $"Expected at most {maxDispatchedIndirectCalls} indirect dispatch calls after batching, but got {stats.IndirectDispatchCount}.");
        }
    }

    /// <summary>
    /// Asserts that draw calls were collapsed efficiently via indirect drawing,
    /// using only the GlCommandProxy snapshot without full IndirectDrawStats.
    /// </summary>
    /// <param name="directDrawCalls">Number of direct draw calls recorded (the fallback/baseline).</param>
    /// <param name="indirectDispatchCalls">Number of indirect dispatch calls recorded.</param>
    /// <param name="minOriginalDrawCalls">Minimum expected direct draw calls baseline.</param>
    /// <param name="maxDispatchedIndirectCalls">Maximum allowed indirect dispatch calls.</param>
    /// <exception cref="PharosAssertException">Thrown when batching efficiency requirements are not met.</exception>
    public static void DrawCallsCollapsed(int directDrawCalls, int indirectDispatchCalls, int minOriginalDrawCalls, int maxDispatchedIndirectCalls)
    {
        if (directDrawCalls < minOriginalDrawCalls)
        {
            throw new PharosAssertException(
                $"Expected at least {minOriginalDrawCalls} direct draw calls for batching baseline, but got {directDrawCalls}.");
        }

        if (indirectDispatchCalls > maxDispatchedIndirectCalls)
        {
            throw new PharosAssertException(
                $"Expected at most {maxDispatchedIndirectCalls} indirect dispatch calls after batching, but got {indirectDispatchCalls}.");
        }
    }

    /// <summary>
    /// Asserts that indirect drawing is disabled and the renderer is operating in fallback mode.
    /// Verifies that no indirect dispatches occurred and at least some direct draw calls were made.
    /// </summary>
    /// <param name="stats">Indirect draw statistics snapshot.</param>
    /// <param name="minDirectDrawCalls">Minimum expected direct draw calls in fallback mode.</param>
    /// <exception cref="PharosAssertException">Thrown when indirect draws were detected or direct draws are below minimum.</exception>
    public static void IndirectDrawDisabled(IndirectDrawStats stats, int minDirectDrawCalls = 1)
    {
        ArgumentNullException.ThrowIfNull(stats);

        if (stats.IndirectDispatchCount > 0)
        {
            throw new PharosAssertException(
                $"Expected no indirect dispatch calls in fallback mode, but got {stats.IndirectDispatchCount}.");
        }

        if (stats.DirectDrawCalls < minDirectDrawCalls)
        {
            throw new PharosAssertException(
                $"Expected at least {minDirectDrawCalls} direct draw calls in fallback mode, but got {stats.DirectDrawCalls}.");
        }
    }

    /// <summary>
    /// Asserts that the collapse ratio meets a minimum threshold.
    /// </summary>
    /// <param name="stats">Indirect draw statistics snapshot.</param>
    /// <param name="minCollapseRatio">Minimum required collapse ratio (DirectDrawCalls / IndirectDispatchCount).</param>
    /// <exception cref="PharosAssertException">Thrown when collapse ratio is below the threshold.</exception>
    public static void CollapseRatioAtLeast(IndirectDrawStats stats, double minCollapseRatio)
    {
        ArgumentNullException.ThrowIfNull(stats);

        if (stats.CollapseRatio < minCollapseRatio)
        {
            throw new PharosAssertException(
                $"Expected collapse ratio of at least {minCollapseRatio:F2}, but got {stats.CollapseRatio:F2}.");
        }
    }

    // -------------------------------------------------------------------------
    // Chunk visibility assertions
    // -------------------------------------------------------------------------

    /// <summary>
    /// Asserts that a chunk is visible (passed frustum and view distance tests).
    /// </summary>
    /// <param name="snapshot">Culling snapshot to query.</param>
    /// <param name="chunk">Chunk position to verify.</param>
    /// <exception cref="PharosAssertException">Thrown when the chunk is not in VisibleChunks.</exception>
    public static void ChunkVisible(CullingSnapshot snapshot, ChunkPos chunk)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        if (!snapshot.VisibleChunks.Contains(chunk))
        {
            throw new PharosAssertException(
                $"Expected chunk {chunk} to be visible, but it was not in VisibleChunks (culled or not loaded).");
        }
    }

    /// <summary>
    /// Asserts that a chunk is culled (failed frustum or view distance tests).
    /// </summary>
    /// <param name="snapshot">Culling snapshot to query.</param>
    /// <param name="chunk">Chunk position to verify.</param>
    /// <exception cref="PharosAssertException">Thrown when the chunk is not in CulledChunks.</exception>
    public static void ChunkCulled(CullingSnapshot snapshot, ChunkPos chunk)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        if (!snapshot.CulledChunks.Contains(chunk))
        {
            throw new PharosAssertException(
                $"Expected chunk {chunk} to be culled, but it was not in CulledChunks (visible or not loaded).");
        }
    }

    /// <summary>
    /// Asserts that a chunk is occlusion-culled (passed frustum test but marked
    /// invisible by the occlusion culler).
    /// </summary>
    /// <param name="snapshot">Culling snapshot to query.</param>
    /// <param name="chunk">Chunk position to verify.</param>
    /// <exception cref="PharosAssertException">Thrown when the chunk is not in OcclusionCulledChunks.</exception>
    public static void ChunkOccluded(CullingSnapshot snapshot, ChunkPos chunk)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        if (!snapshot.OcclusionCulledChunks.Contains(chunk))
        {
            throw new PharosAssertException(
                $"Expected chunk {chunk} to be occlusion-culled, but it was not in OcclusionCulledChunks.");
        }
    }

    // -------------------------------------------------------------------------
    // Mesh face reduction assertions
    // -------------------------------------------------------------------------

    /// <summary>
    /// Asserts that mesh face count was reduced by at least the specified percentage
    /// after greedy meshing optimization.
    /// </summary>
    /// <param name="before">Mesh snapshot before greedy meshing.</param>
    /// <param name="after">Mesh snapshot after greedy meshing.</param>
    /// <param name="minReductionPercent">Minimum expected reduction percentage (0-100).</param>
    /// <exception cref="PharosAssertException">Thrown when reduction is below threshold.</exception>
    public static void MeshFaceCountReduced(MeshSnapshot before, MeshSnapshot after, int minReductionPercent)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);

        if (minReductionPercent < 0 || minReductionPercent > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(minReductionPercent),
                "Reduction percentage must be between 0 and 100.");
        }

        if (!before.IsValid)
        {
            throw new PharosAssertException($"Before snapshot is invalid: {before.ErrorMessage}");
        }

        if (!after.IsValid)
        {
            throw new PharosAssertException($"After snapshot is invalid: {after.ErrorMessage}");
        }

        if (before.FaceCount == 0)
        {
            throw new PharosAssertException("Before snapshot has zero faces; cannot compute reduction.");
        }

        int reduced = before.FaceCount - after.FaceCount;
        double actualPercent = (double)reduced / before.FaceCount * 100.0;

        if (actualPercent < minReductionPercent)
        {
            throw new PharosAssertException(
                $"Expected at least {minReductionPercent}% face reduction, but got {actualPercent:F1}% " +
                $"(before: {before.FaceCount}, after: {after.FaceCount}).");
        }
    }

    /// <summary>
    /// Asserts that all UV coordinates in the snapshot are within the valid [0,1] range
    /// (with optional tolerance for floating-point precision).
    /// </summary>
    /// <param name="snapshot">Mesh snapshot to validate.</param>
    /// <param name="tolerance">Tolerance for coordinates slightly outside [0,1] (default: 0.001f).</param>
    /// <exception cref="PharosAssertException">Thrown when UV coordinates are out of range.</exception>
    public static void UvCoordinatesValid(MeshSnapshot snapshot, float tolerance = 0.001f)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        if (!snapshot.IsValid)
        {
            throw new PharosAssertException($"Snapshot is invalid: {snapshot.ErrorMessage}");
        }

        if (snapshot.UvSamples.Count == 0)
        {
            return; // No UV data to validate
        }

        float min = 0f - tolerance;
        float max = 1f + tolerance;

        for (int i = 0; i < snapshot.UvSamples.Count; i++)
        {
            UvSample uv = snapshot.UvSamples[i];

            if (uv.U < min || uv.U > max)
            {
                throw new PharosAssertException(
                    $"UV coordinate at index {i} has U={uv.U:F4} outside valid range [{-tolerance:F3}, {1 + tolerance:F3}].");
            }

            if (uv.V < min || uv.V > max)
            {
                throw new PharosAssertException(
                    $"UV coordinate at index {i} has V={uv.V:F4} outside valid range [{-tolerance:F3}, {1 + tolerance:F3}].");
            }
        }
    }
}
