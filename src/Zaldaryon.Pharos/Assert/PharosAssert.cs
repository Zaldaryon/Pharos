using static System.FormattableString;
using Zaldaryon.Pharos.Culling;
using Zaldaryon.Pharos.Graphics;
using Zaldaryon.Pharos.Memory;
using Zaldaryon.Pharos.Timing;
using Zaldaryon.Pharos.Visual;

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
                Invariant($"Expected collapse ratio of at least {minCollapseRatio:F2}, but got {stats.CollapseRatio:F2}."));
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
                Invariant($"Expected at least {minReductionPercent}% face reduction, but got {actualPercent:F1}% ") +
                $"({before.FaceCount}, after: {after.FaceCount}).");
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
                    Invariant($"UV coordinate at index {i} has U={uv.U:F4} outside valid range [{-tolerance:F3}, {1 + tolerance:F3}]."));
            }

            if (uv.V < min || uv.V > max)
            {
                throw new PharosAssertException(
                    Invariant($"UV coordinate at index {i} has V={uv.V:F4} outside valid range [{-tolerance:F3}, {1 + tolerance:F3}]."));
            }
        }
    }

    /// <summary>
    /// Asserts that UV coordinates are continuous across adjacent merged quad seams.
    /// Checks that vertices sharing an edge have matching UV coordinates within tolerance.
    /// </summary>
    /// <param name="snapshot">Mesh snapshot with QuadEdges populated.</param>
    /// <param name="tolerance">Maximum allowed UV discontinuity across edges (default: 0.001f).</param>
    /// <exception cref="PharosAssertException">Thrown when UV seams are detected.</exception>
    public static void UvContinuous(MeshSnapshot snapshot, float tolerance = 0.001f)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        if (!snapshot.IsValid)
        {
            throw new PharosAssertException($"Snapshot is invalid: {snapshot.ErrorMessage}");
        }

        if (snapshot.QuadEdges.Count == 0)
        {
            return; // No edges to check
        }

        if (snapshot.UvSamples.Count == 0)
        {
            return; // No UV data to validate
        }

        const int verticesPerQuad = 4;
        List<(QuadEdge edge, float maxDelta)> discontinuities = [];

        foreach (QuadEdge edge in snapshot.QuadEdges)
        {
            float maxDelta = CheckEdgeUvContinuity(snapshot, edge, verticesPerQuad, tolerance);
            if (maxDelta > tolerance)
            {
                discontinuities.Add((edge, maxDelta));
            }
        }

        if (discontinuities.Count > 0)
        {
            var worst = discontinuities.OrderByDescending(d => d.maxDelta).First();
            throw new PharosAssertException(
                Invariant($"UV discontinuity detected at {discontinuities.Count} edge(s). ") +
                Invariant($"Worst: quads {worst.edge.QuadAIndex}-{worst.edge.QuadBIndex} on {worst.edge.AxisName} axis, ") +
                Invariant($"delta={worst.maxDelta:F6} (tolerance={tolerance:F6})."));
        }
    }

    private static float CheckEdgeUvContinuity(MeshSnapshot snapshot, QuadEdge edge, int verticesPerQuad, float posTolerance)
    {
        int baseA = edge.QuadAIndex * verticesPerQuad;
        int baseB = edge.QuadBIndex * verticesPerQuad;
        float maxDelta = 0f;
        float posToleranceSq = posTolerance * posTolerance;

        // Find matching vertices between the two quads and compare their UVs
        for (int i = 0; i < verticesPerQuad; i++)
        {
            int idxA = baseA + i;
            if (idxA >= snapshot.VertexPositions.Count || idxA >= snapshot.UvSamples.Count)
                continue;

            VertexPosition posA = snapshot.VertexPositions[idxA];

            for (int j = 0; j < verticesPerQuad; j++)
            {
                int idxB = baseB + j;
                if (idxB >= snapshot.VertexPositions.Count || idxB >= snapshot.UvSamples.Count)
                    continue;

                VertexPosition posB = snapshot.VertexPositions[idxB];

                // If positions match, compare UVs
                if (posA.DistanceSquaredTo(posB) < posToleranceSq)
                {
                    UvSample uvA = snapshot.UvSamples[idxA];
                    UvSample uvB = snapshot.UvSamples[idxB];

                    float deltaU = Math.Abs(uvA.U - uvB.U);
                    float deltaV = Math.Abs(uvA.V - uvB.V);
                    float delta = Math.Max(deltaU, deltaV);

                    maxDelta = Math.Max(maxDelta, delta);
                }
            }
        }

        return maxDelta;
    }

    // -------------------------------------------------------------------------
    // FSR render scale assertions
    // -------------------------------------------------------------------------

    /// <summary>
    /// Asserts that the render scale is active and matches the expected value within tolerance.
    /// </summary>
    /// <param name="snapshot">Render scale snapshot to validate.</param>
    /// <param name="expectedScale">Expected render scale factor (0.5-1.0).</param>
    /// <param name="tolerance">Tolerance for scale comparison (default: 0.01f).</param>
    /// <exception cref="PharosAssertException">Thrown when render scale does not match expected value.</exception>
    public static void RenderScaleActive(RenderScaleSnapshot snapshot, float expectedScale, float tolerance = 0.01f)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        if (expectedScale < 0.5f || expectedScale > 1.0f)
        {
            throw new ArgumentOutOfRangeException(nameof(expectedScale),
                "Expected scale must be between 0.5 and 1.0.");
        }

        float diff = Math.Abs(snapshot.RenderScale - expectedScale);
        if (diff > tolerance)
        {
            throw new PharosAssertException(
                Invariant($"Expected render scale {expectedScale:F2} (±{tolerance:F2}), but got {snapshot.RenderScale:F2} ") +
                Invariant($"(difference: {diff:F3})."));
        }

        // Verify pre-upscale dimensions are consistent with the actual render scale (not expected)
        // This accounts for tolerance in the scale value itself
        int expectedWidth = (int)(snapshot.DisplayWidth * snapshot.RenderScale);
        int expectedHeight = (int)(snapshot.DisplayHeight * snapshot.RenderScale);

        if (snapshot.PreUpscaleWidth != expectedWidth)
        {
            throw new PharosAssertException(
                Invariant($"Pre-upscale width {snapshot.PreUpscaleWidth} does not match expected {expectedWidth} ") +
                Invariant($"(DisplayWidth {snapshot.DisplayWidth} × actual scale {snapshot.RenderScale:F3})."));
        }

        if (snapshot.PreUpscaleHeight != expectedHeight)
        {
            throw new PharosAssertException(
                Invariant($"Pre-upscale height {snapshot.PreUpscaleHeight} does not match expected {expectedHeight} ") +
                Invariant($"(DisplayHeight {snapshot.DisplayHeight} × actual scale {snapshot.RenderScale:F3})."));
        }
    }

    /// <summary>
    /// Asserts that the FSR pipeline executed completely (both EASU and RCAS passes).
    /// </summary>
    /// <param name="snapshot">Render scale snapshot to validate.</param>
    /// <exception cref="PharosAssertException">Thrown when FSR pipeline did not execute completely.</exception>
    public static void FsrPipelineExecuted(RenderScaleSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        if (!snapshot.FsrEnabled)
        {
            throw new PharosAssertException(
                "FSR is not enabled. Cannot verify pipeline execution when FSR is disabled.");
        }

        if (!snapshot.EasuShaderDispatched)
        {
            throw new PharosAssertException(
                "EASU (Edge-Adaptive Spatial Upsampling) shader was not dispatched. " +
                "FSR pipeline is incomplete.");
        }

        if (!snapshot.RcasShaderDispatched)
        {
            throw new PharosAssertException(
                "RCAS (Robust Contrast Adaptive Sharpening) shader was not dispatched. " +
                "FSR pipeline is incomplete.");
        }
    }

    /// <summary>
    /// Asserts that FSR is disabled and the renderer is in fallback/native resolution mode.
    /// </summary>
    /// <param name="snapshot">Render scale snapshot to validate.</param>
    /// <exception cref="PharosAssertException">Thrown when FSR is enabled or render scale is not native.</exception>
    public static void FsrFallbackMode(RenderScaleSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        if (!snapshot.IsFallbackMode)
        {
            throw new PharosAssertException(
                Invariant($"Expected FSR fallback/native mode, but FSR is enabled with render scale {snapshot.RenderScale:F2}."));
        }

        if (snapshot.EasuShaderDispatched || snapshot.RcasShaderDispatched)
        {
            throw new PharosAssertException(
                "FSR shaders were dispatched in fallback mode. " +
                $"EASU dispatched: {snapshot.EasuShaderDispatched}, RCAS dispatched: {snapshot.RcasShaderDispatched}.");
        }
    }

    /// <summary>
    /// Asserts that the upscaled output resolution matches the target window dimensions.
    /// </summary>
    /// <param name="snapshot">Render scale snapshot to validate.</param>
    /// <param name="targetWidth">Expected final output width.</param>
    /// <param name="targetHeight">Expected final output height.</param>
    /// <exception cref="PharosAssertException">Thrown when output dimensions do not match target.</exception>
    public static void OutputResolutionMatches(RenderScaleSnapshot snapshot, int targetWidth, int targetHeight)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        if (snapshot.DisplayWidth != targetWidth)
        {
            throw new PharosAssertException(
                $"Display width {snapshot.DisplayWidth} does not match target {targetWidth}.");
        }

        if (snapshot.DisplayHeight != targetHeight)
        {
            throw new PharosAssertException(
                $"Display height {snapshot.DisplayHeight} does not match target {targetHeight}.");
        }
    }

    // -------------------------------------------------------------------------
    // Mod compatibility assertions
    // -------------------------------------------------------------------------

    /// <summary>
    /// Asserts that a subsystem is disabled in the feature registry.
    /// </summary>
    /// <param name="registry">The subsystem feature registry to check.</param>
    /// <param name="subsystemName">The subsystem name to verify is disabled.</param>
    /// <exception cref="PharosAssertException">Thrown when the subsystem is not disabled.</exception>
    public static void SubsystemDisabled(Mods.SubsystemFeatureRegistry registry, string subsystemName)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentException.ThrowIfNullOrWhiteSpace(subsystemName);

        if (!registry.IsFeatureRegistered(subsystemName))
        {
            throw new PharosAssertException(
                $"Subsystem '{subsystemName}' is not registered in the feature registry.");
        }

        if (registry.IsFeatureEnabled(subsystemName))
        {
            throw new PharosAssertException(
                $"Expected subsystem '{subsystemName}' to be disabled, but it is currently enabled.");
        }
    }

    /// <summary>
    /// Asserts that a subsystem is enabled in the feature registry.
    /// </summary>
    /// <param name="registry">The subsystem feature registry to check.</param>
    /// <param name="subsystemName">The subsystem name to verify is enabled.</param>
    /// <exception cref="PharosAssertException">Thrown when the subsystem is not enabled.</exception>
    public static void SubsystemEnabled(Mods.SubsystemFeatureRegistry registry, string subsystemName)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentException.ThrowIfNullOrWhiteSpace(subsystemName);

        if (!registry.IsFeatureRegistered(subsystemName))
        {
            throw new PharosAssertException(
                $"Subsystem '{subsystemName}' is not registered in the feature registry.");
        }

        if (!registry.IsFeatureEnabled(subsystemName))
        {
            string? reason = registry.GetDisableReason(subsystemName);
            string reasonPart = reason != null ? $" Reason: {reason}" : "";
            throw new PharosAssertException(
                $"Expected subsystem '{subsystemName}' to be enabled, but it is disabled.{reasonPart}");
        }
    }

    /// <summary>
    /// Asserts that the mod compatibility snapshot has no conflicts.
    /// </summary>
    /// <param name="snapshot">The mod compatibility snapshot to validate.</param>
    /// <exception cref="PharosAssertException">Thrown when conflicts are detected.</exception>
    public static void ModCompatibilityClean(Mods.ModCompatibilitySnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        if (snapshot.HasConflicts)
        {
            string disabled = snapshot.DisabledSubsystems.Count > 0
                ? $" Disabled subsystems: {string.Join(", ", snapshot.DisabledSubsystems)}."
                : "";
            string mods = snapshot.ActiveMockMods.Count > 0
                ? $" Active mods: {string.Join(", ", snapshot.ActiveMockMods)}."
                : "";
            throw new PharosAssertException(
                $"Expected clean mod compatibility (no conflicts), but conflicts were detected.{disabled}{mods}");
        }

        if (snapshot.DisabledSubsystems.Count > 0)
        {
            throw new PharosAssertException(
                $"Expected clean mod compatibility, but {snapshot.DisabledSubsystems.Count} subsystem(s) are disabled: " +
                $"{string.Join(", ", snapshot.DisabledSubsystems)}.");
        }
    }

    /// <summary>
    /// Asserts that a specific mod is detected in the compatibility snapshot.
    /// </summary>
    /// <param name="snapshot">The mod compatibility snapshot to check.</param>
    /// <param name="modId">The mod ID to verify is present.</param>
    /// <exception cref="PharosAssertException">Thrown when the mod is not detected.</exception>
    public static void ModDetected(Mods.ModCompatibilitySnapshot snapshot, string modId)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentException.ThrowIfNullOrWhiteSpace(modId);

        if (!snapshot.ActiveMockMods.Contains(modId, StringComparer.OrdinalIgnoreCase))
        {
            throw new PharosAssertException(
                $"Expected mod '{modId}' to be detected, but it was not in ActiveMockMods. " +
                $"Active mods: [{string.Join(", ", snapshot.ActiveMockMods)}].");
        }
    }

    /// <summary>
    /// Asserts that conflicts exist and at least the specified number of subsystems are disabled.
    /// </summary>
    /// <param name="snapshot">The mod compatibility snapshot to validate.</param>
    /// <param name="minDisabledSubsystems">Minimum number of disabled subsystems expected.</param>
    /// <exception cref="PharosAssertException">Thrown when conflict requirements are not met.</exception>
    public static void ModConflictsExist(Mods.ModCompatibilitySnapshot snapshot, int minDisabledSubsystems = 1)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        if (!snapshot.HasConflicts)
        {
            throw new PharosAssertException(
                "Expected mod conflicts to exist, but snapshot reports HasConflicts=false.");
        }

        if (snapshot.DisabledSubsystems.Count < minDisabledSubsystems)
        {
            throw new PharosAssertException(
                $"Expected at least {minDisabledSubsystems} disabled subsystem(s), but only " +
                $"{snapshot.DisabledSubsystems.Count} found: [{string.Join(", ", snapshot.DisabledSubsystems)}].");
        }
    }

    // -------------------------------------------------------------------------
    // Golden image assertions
    // -------------------------------------------------------------------------

    /// <summary>
    /// Asserts that a perceptual diff result indicates images are similar within tolerance.
    /// </summary>
    /// <param name="result">The perceptual diff result to validate.</param>
    /// <param name="message">Optional custom message for the assertion failure.</param>
    /// <exception cref="PharosAssertException">Thrown when images are not similar.</exception>
    public static void GoldenImageMatch(PerceptualDiffResult result, string message = "")
    {
        if (result.IsSimilar)
        {
            return;
        }

        string prefix = string.IsNullOrEmpty(message) ? "" : $"{message} ";
        string heatmapInfo = result.HeatmapPath is not null
            ? $" Heatmap saved to: {result.HeatmapPath}"
            : "";

        throw new PharosAssertException(
            Invariant($"{prefix}Golden image match failed. ") +
            Invariant($"MeanDiff={result.MeanDiff:F4} (tolerance={result.Tolerance:F4}), ") +
            Invariant($"MaxDiff={result.MaxDiff:F4}, ") +
            Invariant($"DiffPixels={result.DiffPixelCount}/{result.TotalPixels} ({result.DiffPixelFraction:P1}).{heatmapInfo}"));
    }

    // -------------------------------------------------------------------------
    // GL resource leak assertions
    // -------------------------------------------------------------------------

    /// <summary>
    /// Asserts that no OpenGL resource leaks were detected.
    /// </summary>
    /// <param name="report">The GL leak report to validate.</param>
    /// <exception cref="PharosAssertException">Thrown when resource leaks are detected.</exception>
    public static void NoGlLeaks(GlLeakReport report)
    {
        ArgumentNullException.ThrowIfNull(report);

        if (!report.HasLeaks)
        {
            return;
        }

        List<string> leaks = [];
        if (report.BufferLeaks > 0) leaks.Add($"Buffers: {report.BufferLeaks}");
        if (report.TextureLeaks > 0) leaks.Add($"Textures: {report.TextureLeaks}");
        if (report.VAOLeaks > 0) leaks.Add($"VAOs: {report.VAOLeaks}");

        throw new PharosAssertException(
            $"OpenGL resource leaks detected. {string.Join(", ", leaks)}. " +
            $"Total: {report.TotalResourceLeaks} leaked resources.");
    }

    /// <summary>
    /// Asserts that memory growth is below the specified threshold.
    /// </summary>
    /// <param name="actualGrowthBytes">The actual memory growth in bytes.</param>
    /// <param name="maxAllowedBytes">The maximum allowed memory growth in bytes.</param>
    /// <exception cref="PharosAssertException">Thrown when memory growth exceeds the threshold.</exception>
    public static void MemoryGrowthBelow(long actualGrowthBytes, long maxAllowedBytes)
    {
        if (actualGrowthBytes <= maxAllowedBytes)
        {
            return;
        }

        string FormatBytes(long bytes)
        {
            return bytes switch
            {
                >= 1024 * 1024 * 1024 => Invariant($"{bytes / (1024.0 * 1024.0 * 1024.0):F2} GB"),
                >= 1024 * 1024 => Invariant($"{bytes / (1024.0 * 1024.0):F2} MB"),
                >= 1024 => Invariant($"{bytes / 1024.0:F2} KB"),
                _ => $"{bytes} bytes"
            };
        }

        throw new PharosAssertException(
            $"Memory growth exceeds threshold. " +
            $"Actual: {FormatBytes(actualGrowthBytes)}, Max allowed: {FormatBytes(maxAllowedBytes)}.");
    }

    /// <summary>
    /// Asserts that GL resource leaks are within the specified thresholds.
    /// </summary>
    /// <param name="report">The GL leak report to validate.</param>
    /// <param name="maxBufferLeaks">Maximum allowed buffer leaks.</param>
    /// <param name="maxTextureLeaks">Maximum allowed texture leaks.</param>
    /// <param name="maxVaoLeaks">Maximum allowed VAO leaks.</param>
    /// <exception cref="PharosAssertException">Thrown when any leak count exceeds its threshold.</exception>
    public static void GlLeaksBelow(GlLeakReport report, int maxBufferLeaks, int maxTextureLeaks, int maxVaoLeaks)
    {
        ArgumentNullException.ThrowIfNull(report);

        List<string> violations = [];

        if (report.BufferLeaks > maxBufferLeaks)
            violations.Add($"Buffers: {report.BufferLeaks} > {maxBufferLeaks}");
        if (report.TextureLeaks > maxTextureLeaks)
            violations.Add($"Textures: {report.TextureLeaks} > {maxTextureLeaks}");
        if (report.VAOLeaks > maxVaoLeaks)
            violations.Add($"VAOs: {report.VAOLeaks} > {maxVaoLeaks}");

        if (violations.Count > 0)
        {
            throw new PharosAssertException(
                $"GL resource leaks exceed thresholds. {string.Join(", ", violations)}.");
        }
    }

    // -------------------------------------------------------------------------
    // Managed object leak assertions
    // -------------------------------------------------------------------------

    /// <summary>
    /// Asserts that no managed object leaks were detected.
    /// </summary>
    /// <param name="report">The managed leak report to validate.</param>
    /// <exception cref="PharosAssertException">Thrown when managed object leaks are detected.</exception>
    public static void NoManagedLeaks(ManagedLeakReport report)
    {
        ArgumentNullException.ThrowIfNull(report);

        if (!report.HasLeaks)
        {
            return;
        }

        List<string> leaks = [];
        if (report.UnreturnedMeshParts > 0)
            leaks.Add($"MeshParts: {report.UnreturnedMeshParts}");
        if (report.UnrecycledMeshData > 0)
            leaks.Add($"MeshData: {report.UnrecycledMeshData}");

        throw new PharosAssertException(
            $"Managed object leaks detected. {string.Join(", ", leaks)}. " +
            $"Total: {report.TotalLeaks} leaked objects.");
    }

    /// <summary>
    /// Asserts that managed object leaks are within the specified thresholds.
    /// </summary>
    /// <param name="report">The managed leak report to validate.</param>
    /// <param name="maxMeshPartLeaks">Maximum allowed mesh part leaks.</param>
    /// <param name="maxMeshDataLeaks">Maximum allowed MeshData leaks.</param>
    /// <exception cref="PharosAssertException">Thrown when any leak count exceeds its threshold.</exception>
    public static void ManagedLeaksBelow(ManagedLeakReport report, int maxMeshPartLeaks, int maxMeshDataLeaks)
    {
        ArgumentNullException.ThrowIfNull(report);

        List<string> violations = [];

        if (report.UnreturnedMeshParts > maxMeshPartLeaks)
            violations.Add($"MeshParts: {report.UnreturnedMeshParts} > {maxMeshPartLeaks}");
        if (report.UnrecycledMeshData > maxMeshDataLeaks)
            violations.Add($"MeshData: {report.UnrecycledMeshData} > {maxMeshDataLeaks}");

        if (violations.Count > 0)
        {
            throw new PharosAssertException(
                $"Managed object leaks exceed thresholds. {string.Join(", ", violations)}.");
        }
    }

    // -------------------------------------------------------------------------
    // Animation LOD assertions
    // -------------------------------------------------------------------------

    /// <summary>
    /// Asserts that animation LOD throttling is active based on tier tick counts.
    /// Verifies that far-distance entities are ticked less frequently than near-distance entities.
    /// </summary>
    /// <param name="result">The animation LOD tier result to validate.</param>
    /// <param name="maxFarRatio">Maximum allowed ratio of far ticks to near ticks (default: 0.25).
    /// Lower values indicate more aggressive throttling.</param>
    /// <exception cref="PharosAssertException">Thrown when throttling is insufficient or not detected.</exception>
    public static void AnimationLodThrottled(World.AnimationLodTierResult result, float maxFarRatio = 0.25f)
    {
        ArgumentNullException.ThrowIfNull(result);

        if (result.IsEmpty)
        {
            throw new PharosAssertException(
                "Animation LOD result is empty (no ticks recorded). Cannot verify throttling.");
        }

        if (result.NearTickCount == 0)
        {
            throw new PharosAssertException(
                "Near-tier tick count is zero. Cannot calculate throttle ratio.");
        }

        if (!result.ThrottleDetected)
        {
            throw new PharosAssertException(
                Invariant($"Animation LOD throttling not detected. ") +
                Invariant($"FarTickCount ({result.FarTickCount}) should be less than half of NearTickCount ({result.NearTickCount}). ") +
                Invariant($"Ratio: {result.NearToFarRatio:F3}"));
        }

        if (result.NearToFarRatio > maxFarRatio)
        {
            throw new PharosAssertException(
                Invariant($"Animation LOD throttling is insufficient. ") +
                Invariant($"Far/Near ratio {result.NearToFarRatio:F3} exceeds maximum {maxFarRatio:F3}. ") +
                Invariant($"Near={result.NearTickCount}, Mid={result.MidTickCount}, Far={result.FarTickCount}."));
        }
    }

    /// <summary>
    /// Asserts that animation LOD throttling is NOT active (uniform tick distribution).
    /// Useful for testing scenarios where throttling should be disabled.
    /// </summary>
    /// <param name="result">The animation LOD tier result to validate.</param>
    /// <param name="minFarRatio">Minimum expected ratio of far ticks to near ticks (default: 0.9).
    /// Higher values indicate less throttling.</param>
    /// <exception cref="PharosAssertException">Thrown when unexpected throttling is detected.</exception>
    public static void AnimationLodNotThrottled(World.AnimationLodTierResult result, float minFarRatio = 0.9f)
    {
        ArgumentNullException.ThrowIfNull(result);

        if (result.IsEmpty)
        {
            return; // Empty result has no throttling
        }

        if (result.NearTickCount == 0)
        {
            return; // Cannot determine throttling without near ticks
        }

        if (result.ThrottleDetected)
        {
            throw new PharosAssertException(
                Invariant($"Unexpected animation LOD throttling detected. ") +
                Invariant($"FarTickCount ({result.FarTickCount}) is less than half of NearTickCount ({result.NearTickCount}). ") +
                Invariant($"Ratio: {result.NearToFarRatio:F3}"));
        }

        if (result.NearToFarRatio < minFarRatio)
        {
            throw new PharosAssertException(
                Invariant($"Animation distribution is not uniform. ") +
                Invariant($"Far/Near ratio {result.NearToFarRatio:F3} is below minimum {minFarRatio:F3}. ") +
                Invariant($"Near={result.NearTickCount}, Mid={result.MidTickCount}, Far={result.FarTickCount}."));
        }
    }

    /// <summary>
    /// Asserts that the animation tier result has minimum tick counts for all tiers.
    /// </summary>
    /// <param name="result">The animation LOD tier result to validate.</param>
    /// <param name="minNearTicks">Minimum expected near-tier ticks.</param>
    /// <param name="minMidTicks">Minimum expected mid-tier ticks.</param>
    /// <param name="minFarTicks">Minimum expected far-tier ticks.</param>
    /// <exception cref="PharosAssertException">Thrown when tick counts are below minimums.</exception>
    public static void AnimationTicksAtLeast(
        World.AnimationLodTierResult result,
        int minNearTicks,
        int minMidTicks,
        int minFarTicks)
    {
        ArgumentNullException.ThrowIfNull(result);

        List<string> violations = [];

        if (result.NearTickCount < minNearTicks)
            violations.Add($"Near: {result.NearTickCount} < {minNearTicks}");
        if (result.MidTickCount < minMidTicks)
            violations.Add($"Mid: {result.MidTickCount} < {minMidTicks}");
        if (result.FarTickCount < minFarTicks)
            violations.Add($"Far: {result.FarTickCount} < {minFarTicks}");

        if (violations.Count > 0)
        {
            throw new PharosAssertException(
                $"Animation tick counts below minimum. {string.Join(", ", violations)}.");
        }
    }

    // -------------------------------------------------------------------------
    // Chunk IO timing assertions
    // -------------------------------------------------------------------------

    /// <summary>
    /// Asserts that parallel chunk IO achieved at least the specified speedup over serial IO.
    /// </summary>
    /// <param name="poolReport">Timing report from parallel/pooled chunk loading.</param>
    /// <param name="serialReport">Timing report from serial chunk loading (baseline).</param>
    /// <param name="minSpeedup">Minimum required speedup factor (default: 1.4x).</param>
    /// <exception cref="PharosAssertException">Thrown when speedup is insufficient.</exception>
    public static void ChunkIoSpeedupAtLeast(
        Server.ChunkIoTimingReport poolReport,
        Server.ChunkIoTimingReport serialReport,
        double minSpeedup = 1.4)
    {
        ArgumentNullException.ThrowIfNull(poolReport);
        ArgumentNullException.ThrowIfNull(serialReport);

        if (!poolReport.HasData)
        {
            throw new PharosAssertException(
                "Pool report has no chunk load data. Cannot calculate speedup.");
        }

        if (!serialReport.HasData)
        {
            throw new PharosAssertException(
                "Serial report has no chunk load data. Cannot calculate speedup.");
        }

        if (serialReport.TotalTimeMs == 0)
        {
            throw new PharosAssertException(
                "Serial report has zero total time. Cannot calculate speedup ratio.");
        }

        double actualSpeedup = (double)serialReport.TotalTimeMs / poolReport.TotalTimeMs;

        if (actualSpeedup < minSpeedup)
        {
            throw new PharosAssertException(
                Invariant($"Chunk IO speedup {actualSpeedup:F2}x is below minimum {minSpeedup:F2}x. ") +
                Invariant($"Pool: {poolReport.ChunksLoaded} chunks in {poolReport.TotalTimeMs}ms ") +
                Invariant($"({poolReport.AverageTimePerChunkMs:F2}ms/chunk). ") +
                Invariant($"Serial: {serialReport.ChunksLoaded} chunks in {serialReport.TotalTimeMs}ms ") +
                Invariant($"({serialReport.AverageTimePerChunkMs:F2}ms/chunk)."));
        }
    }

    /// <summary>
    /// Asserts that chunk IO completed within the specified time budget.
    /// </summary>
    /// <param name="report">Timing report to validate.</param>
    /// <param name="maxTotalTimeMs">Maximum allowed total time in milliseconds.</param>
    /// <exception cref="PharosAssertException">Thrown when time budget is exceeded.</exception>
    public static void ChunkIoWithinBudget(Server.ChunkIoTimingReport report, long maxTotalTimeMs)
    {
        ArgumentNullException.ThrowIfNull(report);

        if (report.TotalTimeMs > maxTotalTimeMs)
        {
            throw new PharosAssertException(
                Invariant($"Chunk IO exceeded time budget. ") +
                Invariant($"Actual: {report.TotalTimeMs}ms, Budget: {maxTotalTimeMs}ms ") +
                Invariant($"({report.ChunksLoaded} chunks, {report.AverageTimePerChunkMs:F2}ms/chunk)."));
        }
    }

    /// <summary>
    /// Asserts that chunk IO loaded at least the expected number of chunks.
    /// </summary>
    /// <param name="report">Timing report to validate.</param>
    /// <param name="minChunks">Minimum expected chunk count.</param>
    /// <exception cref="PharosAssertException">Thrown when chunk count is below minimum.</exception>
    public static void ChunkIoLoadedAtLeast(Server.ChunkIoTimingReport report, int minChunks)
    {
        ArgumentNullException.ThrowIfNull(report);

        if (report.ChunksLoaded < minChunks)
        {
            throw new PharosAssertException(
                $"Chunk IO loaded {report.ChunksLoaded} chunks, but expected at least {minChunks}.");
        }
    }

    /// <summary>
    /// Asserts that average chunk load time is within the specified threshold.
    /// </summary>
    /// <param name="report">Timing report to validate.</param>
    /// <param name="maxAvgTimeMs">Maximum allowed average time per chunk in milliseconds.</param>
    /// <exception cref="PharosAssertException">Thrown when average time exceeds threshold.</exception>
    public static void ChunkIoAverageTimeBelow(Server.ChunkIoTimingReport report, double maxAvgTimeMs)
    {
        ArgumentNullException.ThrowIfNull(report);

        if (!report.HasData)
        {
            return; // No data to validate
        }

        if (report.AverageTimePerChunkMs > maxAvgTimeMs)
        {
            throw new PharosAssertException(
                Invariant($"Average chunk load time {report.AverageTimePerChunkMs:F2}ms ") +
                Invariant($"exceeds maximum {maxAvgTimeMs:F2}ms ") +
                Invariant($"({report.ChunksLoaded} chunks, {report.TotalTimeMs}ms total)."));
        }
    }
}
