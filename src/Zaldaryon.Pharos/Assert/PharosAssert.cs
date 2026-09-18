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
                $"Expected render scale {expectedScale:F2} (±{tolerance:F2}), but got {snapshot.RenderScale:F2} " +
                $"(difference: {diff:F3}).");
        }

        // Verify pre-upscale dimensions are consistent with the actual render scale (not expected)
        // This accounts for tolerance in the scale value itself
        int expectedWidth = (int)(snapshot.DisplayWidth * snapshot.RenderScale);
        int expectedHeight = (int)(snapshot.DisplayHeight * snapshot.RenderScale);

        if (snapshot.PreUpscaleWidth != expectedWidth)
        {
            throw new PharosAssertException(
                $"Pre-upscale width {snapshot.PreUpscaleWidth} does not match expected {expectedWidth} " +
                $"(DisplayWidth {snapshot.DisplayWidth} × actual scale {snapshot.RenderScale:F3}).");
        }

        if (snapshot.PreUpscaleHeight != expectedHeight)
        {
            throw new PharosAssertException(
                $"Pre-upscale height {snapshot.PreUpscaleHeight} does not match expected {expectedHeight} " +
                $"(DisplayHeight {snapshot.DisplayHeight} × actual scale {snapshot.RenderScale:F3}).");
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
                $"Expected FSR fallback/native mode, but FSR is enabled with render scale {snapshot.RenderScale:F2}.");
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
            $"{prefix}Golden image match failed. " +
            $"MeanDiff={result.MeanDiff:F4} (tolerance={result.Tolerance:F4}), " +
            $"MaxDiff={result.MaxDiff:F4}, " +
            $"DiffPixels={result.DiffPixelCount}/{result.TotalPixels} ({result.DiffPixelFraction:P1}).{heatmapInfo}");
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
                >= 1024 * 1024 * 1024 => $"{bytes / (1024.0 * 1024.0 * 1024.0):F2} GB",
                >= 1024 * 1024 => $"{bytes / (1024.0 * 1024.0):F2} MB",
                >= 1024 => $"{bytes / 1024.0:F2} KB",
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
}
