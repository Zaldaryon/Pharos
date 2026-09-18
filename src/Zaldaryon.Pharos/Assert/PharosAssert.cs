using Zaldaryon.Pharos.Graphics;

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
}
