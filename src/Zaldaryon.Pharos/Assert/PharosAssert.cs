using static System.FormattableString;
using System.Globalization;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.Common;
using Zaldaryon.Pharos.Core;
using Zaldaryon.Pharos.Culling;
using Zaldaryon.Pharos.Graphics;
using Zaldaryon.Pharos.Memory;
using Zaldaryon.Pharos.Mods;
using Zaldaryon.Pharos.Server;
using Zaldaryon.Pharos.Timing;
using Zaldaryon.Pharos.Visual;
using Zaldaryon.Pharos.XUnit;

namespace Zaldaryon.Pharos.Assertions;

/// <summary>
/// Static assertion methods for validating indirect draw batching efficiency
/// and fallback mode behavior in Pharos test scenarios.
/// </summary>
public static class PharosAssert
{
    /// <summary>
    /// Asserts that a nonempty server broadcast reached only players within an inclusive 3D radius,
    /// using recipient positions at the game-thread observation boundary. This checks exclusion, not delivery to every nearby player.
    /// </summary>
    public static void OnlyNearbyReceived<T>(IEnumerable<ServerModMessage<T>> log, Vec3d center, double radius) =>
        OnlyNearbyReceived(log?.Select(m => m.Envelope) ?? throw new ArgumentNullException(nameof(log)), center, radius);

    /// <summary>Checks nonempty sent-message evidence against captured recipient positions.</summary>
    public static void OnlyNearbyReceived(IEnumerable<ServerModMessage> log, Vec3d center, double radius)
    {
        ArgumentNullException.ThrowIfNull(log);
        ArgumentNullException.ThrowIfNull(center);
        if (!double.IsFinite(radius) || radius < 0 || !double.IsFinite(radius * radius))
            throw new ArgumentOutOfRangeException(nameof(radius));
        if (!double.IsFinite(center.X) || !double.IsFinite(center.Y) || !double.IsFinite(center.Z))
            throw new ArgumentException("Broadcast center must be finite.", nameof(center));
        int count = 0;
        foreach (var message in log)
        {
            count++;
            if (message.Direction != Zaldaryon.Pharos.Network.ModMessageDirection.Sent)
                throw new PharosAssertException("OnlyNearbyReceived requires sent-message evidence, not incoming messages.");
            var pos = message.Position;
            if (pos == null || !double.IsFinite(pos.X) || !double.IsFinite(pos.Y) || !double.IsFinite(pos.Z))
                throw new PharosAssertException($"Recipient {message.PlayerUID} has no finite captured position.");
            double distanceSquared = pos.SquareDistanceTo(center);
            if (!double.IsFinite(distanceSquared) || distanceSquared > radius * radius)
                throw new PharosAssertException($"Recipient {message.PlayerUID} was outside radius {radius} at tick {message.Tick} (position {pos}).");
        }
        if (count == 0) throw new PharosAssertException("No sent messages were recorded; a missing broadcast cannot pass a range assertion.");
    }

    /// <summary>Block-position convenience overload; measures from the exact coordinates, without an implicit center offset.</summary>
    public static void OnlyNearbyReceived<T>(IEnumerable<ServerModMessage<T>> log, BlockPos center, double radius) =>
        OnlyNearbyReceived(log, center?.ToVec3d() ?? throw new ArgumentNullException(nameof(center)), radius);

    /// <summary>Marks the current Pharos scenario as skipped with the supplied reason.</summary>
    /// <param name="reason">Why the scenario cannot run.</param>
    /// <remarks>The scenario runner reports the current test as skipped with this reason.</remarks>
    public static void Skip(string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        throw new ScenarioSkipException(reason);
    }

    /// <summary>
    /// Asserts that the tick work of a mod, as <see cref="ServerProfile.Mods"/> measured it, stayed
    /// within a budget.
    /// </summary>
    /// <param name="profile">The profile from <see cref="ServerProfileDriver.MeasureAsync"/>.</param>
    /// <param name="modId">The mod's id, as its <c>modinfo.json</c> declares it.</param>
    /// <param name="maxMeanMsPerTick">The most milliseconds per tick the mod may cost on average.</param>
    /// <exception cref="PharosAssertException">
    /// The mod has no tick work in the profile, or its mean cost is above the budget.
    /// </exception>
    /// <remarks>
    /// A mod that ran no listener or callback has no entry: that is a failure, not a pass, because a
    /// budget on work that was never measured proves nothing.
    /// </remarks>
    public static void TickCostBelow(ServerProfile profile, string modId, double maxMeanMsPerTick)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentException.ThrowIfNullOrWhiteSpace(modId);
        if (!profile.Mods.TryGetValue(modId, out ServerProfileEntry? entry))
        {
            throw new PharosAssertException(
                $"The profile has no tick work for mod '{modId}': it registered no listener or callback that ran in the measured ticks.");
        }

        if (entry.MeanMsPerTick > maxMeanMsPerTick)
        {
            throw new PharosAssertException(string.Create(CultureInfo.InvariantCulture,
                $"Mod '{modId}' cost {entry.MeanMsPerTick:0.####} ms per tick, above the {maxMeanMsPerTick:0.####} ms budget ({entry.TotalMs:0.###} ms over {entry.Ticks} ticks, {entry.Calls} calls)."));
        }
    }

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
    // Mod loading assertions
    // -------------------------------------------------------------------------

    /// <summary>
    /// Asserts that every patch of <paramref name="modId"/> that the loader was meant to apply
    /// applied. Disabled, side-mismatched, condition-unmet and dependency-missing patches are
    /// skipped by the loader on purpose and do not fail this assertion.
    /// </summary>
    /// <param name="host">The server whose mods to inspect.</param>
    /// <param name="modId">The patch mod, such as <c>mymod</c>.</param>
    /// <exception cref="PharosAssertException">Thrown when a patch was not found or reported an error.</exception>
    public static void AllPatchesApplied(EmbeddedServerHost host, string modId)
    {
        ArgumentNullException.ThrowIfNull(host);
        AllPatchesApplied(host.Mods, modId);
    }

    /// <summary>Asserts that every patch of <paramref name="modId"/> applied. See the server overload.</summary>
    public static void AllPatchesApplied(HeadlessClient client, string modId)
    {
        ArgumentNullException.ThrowIfNull(client);
        AllPatchesApplied(client.Mods, modId);
    }

    /// <summary>Asserts that every patch of <paramref name="modId"/> applied. See the server overload.</summary>
    public static void AllPatchesApplied(ModLoadReport profile, string modId)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentException.ThrowIfNullOrWhiteSpace(modId);

        PatchResult[] failures = profile.Patches
            .Where(patch => string.Equals(patch.SourceMod, modId, StringComparison.OrdinalIgnoreCase))
            .Where(patch => patch.Outcome is PatchOutcome.NotFound or PatchOutcome.Error)
            .ToArray();
        if (failures.Length == 0) return;

        string list = string.Join(Environment.NewLine, failures.Select(patch => "  " + patch));
        throw new PharosAssertException(
            $"{failures.Length} patch(es) of mod '{modId}' did not apply:{Environment.NewLine}{list}");
    }

    /// <summary>
    /// Asserts that <paramref name="first"/> ran before <paramref name="second"/> in
    /// <paramref name="phase"/>. A selector is <c>mod:TypeName</c>, a full type name, or a bare type
    /// name, such as <c>mymod:MySystem</c> or <c>game:RecipeLoader</c>.
    /// </summary>
    /// <param name="host">The server whose run log to inspect.</param>
    /// <param name="first">The system that must run first.</param>
    /// <param name="second">The system that must run after it.</param>
    /// <param name="phase">The phase both systems must have run in.</param>
    /// <exception cref="PharosAssertException">Thrown when either system did not run in the phase, or ran in the wrong order.</exception>
    public static void LoadsBefore(EmbeddedServerHost host, string first, string second, ModRunPhase phase)
    {
        ArgumentNullException.ThrowIfNull(host);
        LoadsBefore(host.Mods, first, second, phase);
    }

    /// <summary>Asserts that one system ran before another. See the server overload.</summary>
    public static void LoadsBefore(HeadlessClient client, string first, string second, ModRunPhase phase)
    {
        ArgumentNullException.ThrowIfNull(client);
        LoadsBefore(client.Mods, first, second, phase);
    }

    /// <summary>Asserts that one system ran before another. See the server overload.</summary>
    public static void LoadsBefore(ModLoadReport report, string first, string second, ModRunPhase phase)
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentException.ThrowIfNullOrWhiteSpace(first);
        ArgumentException.ThrowIfNullOrWhiteSpace(second);

        SystemRun firstRun = FindRun(report, first, phase);
        SystemRun secondRun = FindRun(report, second, phase);
        if (firstRun.Order < secondRun.Order) return;

        throw new PharosAssertException(
            $"Expected '{first}' to run before '{second}' in {phase}, but they ran at positions " +
            $"{firstRun.Order} and {secondRun.Order}.");
    }

    /// <summary>
    /// Asserts that <paramref name="first"/> ran before <paramref name="second"/> in any phase.
    /// See the phase overload for the selector forms.
    /// </summary>
    public static void LoadsBefore(EmbeddedServerHost host, string first, string second)
    {
        ArgumentNullException.ThrowIfNull(host);
        LoadsBefore(host.Mods, first, second);
    }

    /// <summary>Asserts that one system ran before another in any phase. See the server overload.</summary>
    public static void LoadsBefore(HeadlessClient client, string first, string second)
    {
        ArgumentNullException.ThrowIfNull(client);
        LoadsBefore(client.Mods, first, second);
    }

    /// <summary>Asserts that one system ran before another in any phase. See the phase overload.</summary>
    public static void LoadsBefore(ModLoadReport report, string first, string second)
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentException.ThrowIfNullOrWhiteSpace(first);
        ArgumentException.ThrowIfNullOrWhiteSpace(second);

        SystemRun firstRun = FindFirstRun(report, first);
        SystemRun secondRun = FindFirstRun(report, second);
        if (firstRun.Order < secondRun.Order) return;

        throw new PharosAssertException(
            $"Expected '{first}' to run before '{second}', but they ran at positions " +
            $"{firstRun.Order} and {secondRun.Order}.");
    }

    /// <summary>
    /// Asserts that the system of type <paramref name="first"/> ran before <paramref name="second"/>
    /// in <paramref name="phase"/>. The type form avoids a selector clash when two mods have a
    /// system with the same name.
    /// </summary>
    public static void LoadsBefore(EmbeddedServerHost host, Type first, string second, ModRunPhase phase)
    {
        ArgumentNullException.ThrowIfNull(host);
        LoadsBefore(host.Mods, first, second, phase);
    }

    /// <summary>Asserts a typed system ran before another system. See the server overload.</summary>
    public static void LoadsBefore(HeadlessClient client, Type first, string second, ModRunPhase phase)
    {
        ArgumentNullException.ThrowIfNull(client);
        LoadsBefore(client.Mods, first, second, phase);
    }

    /// <summary>Asserts a typed system ran before another system. See the server overload.</summary>
    public static void LoadsBefore(ModLoadReport report, Type first, string second, ModRunPhase phase)
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentNullException.ThrowIfNull(first);
        ArgumentException.ThrowIfNullOrWhiteSpace(second);

        SystemRun firstRun = FindRun(report, first, phase);
        SystemRun secondRun = FindRun(report, second, phase);
        if (firstRun.Order < secondRun.Order) return;

        throw new PharosAssertException(
            $"Expected '{first.Name}' to run before '{second}' in {phase}, but they ran at positions " +
            $"{firstRun.Order} and {secondRun.Order}.");
    }

    /// <summary>Asserts a typed system ran before another system in any phase. See the phase overload.</summary>
    public static void LoadsBefore(EmbeddedServerHost host, Type first, string second)
    {
        ArgumentNullException.ThrowIfNull(host);
        LoadsBefore(host.Mods, first, second);
    }

    /// <summary>Asserts a typed system ran before another system in any phase. See the phase overload.</summary>
    public static void LoadsBefore(HeadlessClient client, Type first, string second)
    {
        ArgumentNullException.ThrowIfNull(client);
        LoadsBefore(client.Mods, first, second);
    }

    /// <summary>Asserts a typed system ran before another system in any phase. See the phase overload.</summary>
    public static void LoadsBefore(ModLoadReport report, Type first, string second)
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentNullException.ThrowIfNull(first);
        ArgumentException.ThrowIfNullOrWhiteSpace(second);

        SystemRun firstRun = FindFirstRun(report, first);
        SystemRun secondRun = FindFirstRun(report, second);
        if (firstRun.Order < secondRun.Order) return;

        throw new PharosAssertException(
            $"Expected '{first.Name}' to run before '{second}', but they ran at positions " +
            $"{firstRun.Order} and {secondRun.Order}.");
    }

    /// <summary>
    /// Asserts that the loader saw no mod error and no missing dependency.
    /// </summary>
    /// <param name="host">The server whose mods to inspect.</param>
    /// <exception cref="PharosAssertException">Thrown when a mod is errored or a dependency is missing.</exception>
    public static void NoModErrors(EmbeddedServerHost host)
    {
        ArgumentNullException.ThrowIfNull(host);
        NoModErrors(host.Mods);
    }

    /// <summary>Asserts that the client's loader saw no mod error. See the server overload.</summary>
    public static void NoModErrors(HeadlessClient client)
    {
        ArgumentNullException.ThrowIfNull(client);
        NoModErrors(client.Mods);
    }

    /// <summary>Asserts that the loader saw no mod error. See the server overload.</summary>
    public static void NoModErrors(ModLoadReport profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        LoadedMod[] errors = profile.Loaded.Values
            .Where(mod => mod.Error != null || mod.Status == ModStatus.Errored)
            .ToArray();
        if (errors.Length > 0 || profile.MissingDependencies.Count > 0)
        {
            string mods = errors.Length == 0
                ? ""
                : " Errored mods: " + string.Join(", ", errors.Select(mod => $"{mod.Id} ({mod.Error ?? ModError.Loading})")) + ".";
            string missing = profile.MissingDependencies.Count == 0
                ? ""
                : " Missing dependencies: " + string.Join(", ", profile.MissingDependencies) + ".";
            throw new PharosAssertException($"Expected no mod errors.{mods}{missing}".TrimEnd());
        }
    }

    private static SystemRun FindRun(ModLoadReport report, string selector, ModRunPhase phase)
    {
        SystemRun? run = report.RunLog.FirstOrDefault(candidate =>
            candidate.Phase == phase
            && (string.Equals(candidate.Key, selector, StringComparison.OrdinalIgnoreCase)
                || string.Equals(candidate.Type.FullName, selector, StringComparison.OrdinalIgnoreCase)
                || string.Equals(candidate.Type.Name, selector, StringComparison.OrdinalIgnoreCase)));
        if (run != null) return run;

        string available = string.Join(", ", report.RunLog
            .Where(candidate => candidate.Phase == phase)
            .Select(candidate => candidate.Key)
            .Distinct(StringComparer.OrdinalIgnoreCase));
        throw new PharosAssertException(
            $"No system matched '{selector}' in {phase}. Systems that ran: [{available}].");
    }

    private static SystemRun FindRun(ModLoadReport report, Type type, ModRunPhase phase)
    {
        SystemRun? run = report.RunLog.FirstOrDefault(candidate =>
            candidate.Phase == phase && candidate.Type == type);
        if (run != null) return run;

        string available = string.Join(", ", report.RunLog
            .Where(candidate => candidate.Phase == phase)
            .Select(candidate => candidate.Key)
            .Distinct(StringComparer.OrdinalIgnoreCase));
        throw new PharosAssertException(
            $"No system of type '{type.FullName}' ran in {phase}. Systems that ran: [{available}].");
    }

    private static SystemRun FindFirstRun(ModLoadReport report, string selector)
    {
        SystemRun? run = report.RunLog.FirstOrDefault(candidate =>
            string.Equals(candidate.Key, selector, StringComparison.OrdinalIgnoreCase)
            || string.Equals(candidate.Type.FullName, selector, StringComparison.OrdinalIgnoreCase)
            || string.Equals(candidate.Type.Name, selector, StringComparison.OrdinalIgnoreCase));
        if (run != null) return run;

        string available = string.Join(", ", report.RunLog
            .Select(candidate => candidate.Key)
            .Distinct(StringComparer.OrdinalIgnoreCase));
        throw new PharosAssertException(
            $"No system matched '{selector}'. Systems that ran: [{available}].");
    }

    private static SystemRun FindFirstRun(ModLoadReport report, Type type)
    {
        SystemRun? run = report.RunLog.FirstOrDefault(candidate => candidate.Type == type);
        if (run != null) return run;

        string available = string.Join(", ", report.RunLog
            .Select(candidate => candidate.Key)
            .Distinct(StringComparer.OrdinalIgnoreCase));
        throw new PharosAssertException(
            $"No system of type '{type.FullName}' ran. Systems that ran: [{available}].");
    }

    // -------------------------------------------------------------------------
    // Command assertions
    // -------------------------------------------------------------------------

    /// <summary>
    /// Asserts that every command a mod registered is complete and described: the game's own
    /// validation passes, no node misses a name, a privilege, a handler or a description, and every
    /// description resolves in the language the server runs in and in every language the mod ships.
    /// </summary>
    /// <param name="host">The server whose commands to check.</param>
    /// <param name="modId">The mod whose commands to check, such as <c>mymod</c>.</param>
    /// <exception cref="PharosAssertException">Thrown when a command is incomplete or a description does not resolve.</exception>
    public static void CommandsValid(EmbeddedServerHost host, string modId)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentException.ThrowIfNullOrWhiteSpace(modId);

        List<(string Name, Vintagestory.API.Common.IChatCommand Command)> commands = [.. CommandRegistry.CommandsOf(host, modId)];
        if (commands.Count == 0)
        {
            throw new PharosAssertException($"The mod '{modId}' registered no command, so there is nothing to check.");
        }

        List<string> problems = [];
        IReadOnlyList<string> languages = LanguagesOf(host, modId);

        foreach ((string name, Vintagestory.API.Common.IChatCommand command) in commands)
        {
            try
            {
                command.Validate();
            }
            catch (Exception ex)
            {
                problems.Add($"{name}: {ex.Message}");
                continue;
            }

            foreach (CommandNode node in NodesOf(command))
            {
                if (node.Incomplete)
                {
                    problems.Add($"{node.FullName}: the command is incomplete, a name, a privilege, a handler or a description is missing");
                }

                if (string.IsNullOrWhiteSpace(node.Description))
                {
                    problems.Add($"{node.FullName}: the command has no description");
                    continue;
                }

                if (!Resolves(node.Description))
                {
                    problems.Add($"{node.FullName}: the description '{node.Description}' does not resolve in {Lang.CurrentLocale}");
                }

                foreach (string language in languages)
                {
                    if (!Lang.AvailableLanguages.TryGetValue(language, out ITranslationService? service)) continue;
                    if (service.GetIfExists(node.Description) == null)
                    {
                        problems.Add($"{node.FullName}: the description '{node.Description}' does not resolve in {language}");
                    }
                }
            }
        }

        if (problems.Count == 0) return;

        string list = string.Join(Environment.NewLine, problems.Select(problem => "  " + problem));
        throw new PharosAssertException(
            $"{problems.Count} problem(s) in the commands of mod '{modId}':{Environment.NewLine}{list}");
    }

    private static IEnumerable<CommandNode> NodesOf(Vintagestory.API.Common.IChatCommand command)
    {
        yield return new CommandNode(
            command.Name ?? "",
            command.FullName ?? "",
            [],
            [],
            command.CallSyntax ?? "",
            "",
            command.Description ?? "",
            command.Incomplete,
            new Dictionary<string, CommandNode>());

        foreach (Vintagestory.API.Common.IChatCommand subcommand in command.Subcommands)
        {
            foreach (CommandNode node in NodesOf(subcommand)) yield return node;
        }
    }

    private static bool Resolves(string description) => Lang.GetIfExists(description) != null;

    /// <summary>The languages a mod ships translations for, by the files in its lang folder.</summary>
    private static IReadOnlyList<string> LanguagesOf(EmbeddedServerHost host, string modId)
    {
        Vintagestory.API.Common.Mod? mod = host.Api.ModLoader.Mods.FirstOrDefault(candidate =>
            string.Equals(candidate.Info?.ModID, modId, StringComparison.OrdinalIgnoreCase));
        if (mod == null || string.IsNullOrWhiteSpace(mod.SourcePath)) return [];

        string folder = Path.Combine(mod.SourcePath, "assets", modId, "lang");
        if (!Directory.Exists(folder)) return [];

        return [.. Directory.EnumerateFiles(folder, "*.json")
            .Select(path => Path.GetFileNameWithoutExtension(path))
            .Where(code => !string.IsNullOrWhiteSpace(code))
            .Order(StringComparer.OrdinalIgnoreCase)];
    }

    // -------------------------------------------------------------------------
    // Item assertions
    // -------------------------------------------------------------------------

    /// <summary>
    /// Asserts that a tool cannot mine a block: its tier is below the one the block needs, or it is
    /// not made for the block's material.
    /// </summary>
    /// <param name="host">The server whose items and blocks are read.</param>
    /// <param name="toolCode">The code of the tool, such as <c>game:pickaxe-copper</c>.</param>
    /// <param name="blockCode">The code of the block, such as <c>game:ore-rich-ilmenite-granite</c>.</param>
    /// <exception cref="PharosAssertException">Thrown when the tool can mine the block.</exception>
    public static void CannotMine(EmbeddedServerHost host, string toolCode, string blockCode)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentException.ThrowIfNullOrWhiteSpace(toolCode);
        ArgumentException.ThrowIfNullOrWhiteSpace(blockCode);

        (int toolTier, int requiredTier) = host.RunOnGameThread(() =>
        {
            Vintagestory.API.Common.Item? item = host.Api.World.GetItem(new Vintagestory.API.Common.AssetLocation(toolCode))
                ?? throw new ArgumentException($"The server has no item '{toolCode}'.", nameof(toolCode));
            Vintagestory.API.Common.AssetLocation blockAsset = new(blockCode);
            Vintagestory.API.Common.Block? block = host.Api.World.Blocks
                .FirstOrDefault(candidate => candidate.Code == blockAsset);
            if (block == null)
            {
                throw new ArgumentException($"The server has no block '{blockCode}'.", nameof(blockCode));
            }

            Vintagestory.API.Common.ItemStack tool = new(item, 1);
            return (tool.Collectible.GetToolTier(new Vintagestory.API.Common.DummySlot(tool)), block.RequiredMiningTier);
        });

        if (requiredTier <= 0) return;
        if (toolTier >= requiredTier)
        {
            throw new PharosAssertException(
                $"The tool '{toolCode}' has tier {toolTier} and can mine '{blockCode}', which needs tier {requiredTier}.");
        }
    }

    /// <summary>
    /// Asserts that the item in a hotbar slot lost the given amount of durability.
    /// </summary>
    /// <param name="host">The server whose player is read.</param>
    /// <param name="slot">The hotbar slot, counted from zero.</param>
    /// <param name="expectedLost">How much durability the item should have lost.</param>
    /// <param name="durabilityBefore">The durability the item had before the action.</param>
    /// <param name="player">The player whose slot is read. The first online player is used when none is given.</param>
    /// <exception cref="PharosAssertException">Thrown when the item did not lose that much durability.</exception>
    public static void DurabilityLost(EmbeddedServerHost host, int slot, int expectedLost, int durabilityBefore, Vintagestory.API.Common.IPlayer? player = null)
    {
        ArgumentNullException.ThrowIfNull(host);
        if (expectedLost < 0) throw new ArgumentOutOfRangeException(nameof(expectedLost), "The lost durability cannot be negative.");

        int remaining = host.Items.Durability(slot, player);
        int lost = durabilityBefore - remaining;
        if (lost != expectedLost)
        {
            throw new PharosAssertException(
                $"The item in the hotbar slot {slot} lost {lost} durability, not {expectedLost}: it had {durabilityBefore} and has {remaining} left.");
        }
    }

    // -------------------------------------------------------------------------
    // Inventory assertions
    // -------------------------------------------------------------------------

    /// <summary>
    /// Asserts that a region holds the same items as it did before: nothing appeared and nothing
    /// vanished.
    /// </summary>
    /// <param name="before">The totals before the action.</param>
    /// <param name="after">The totals after the action.</param>
    /// <exception cref="PharosAssertException">Thrown when an item appeared or vanished.</exception>
    public static void ItemsConserved(ItemTotals before, ItemTotals after)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);

        List<string> problems = [];
        foreach (string code in before.ByCode.Keys.Union(after.ByCode.Keys).Order(StringComparer.Ordinal))
        {
            int had = before.Count(code);
            int has = after.Count(code);
            if (had == has) continue;
            problems.Add(had > has
                ? $"{code}: {had - has} item(s) vanished"
                : $"{code}: {has - had} item(s) appeared");
        }

        if (problems.Count == 0) return;

        string list = string.Join(Environment.NewLine, problems.Select(problem => "  " + problem));
        throw new PharosAssertException(
            $"The items were not conserved: the region held {before.Total} and holds {after.Total}.{Environment.NewLine}{list}");
    }

    /// <summary>
    /// Asserts that the client's copy of an open container holds the same items as the server's.
    /// </summary>
    /// <param name="session">The session whose client and server are compared.</param>
    /// <param name="position">Where the container stands.</param>
    /// <exception cref="PharosAssertException">Thrown when the copies differ, or the client has no copy open.</exception>
    public static void InventorySynced(ClientServerLoopbackSession session, Vintagestory.API.MathTools.BlockPos position)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(position);

        (string ClassName, Dictionary<string, int> Items)? server = session.NativeServer?.RunOnGameThread<(string, Dictionary<string, int>)?>(() =>
        {
            if (session.NativeServer.Api.World.BlockAccessor.GetBlockEntity(position) is not Vintagestory.API.Common.IBlockEntityContainer container)
            {
                return null;
            }

            return (container.InventoryClassName, ItemsOf(container.Inventory));
        });
        if (server == null)
        {
            throw new PharosAssertException($"The block at {position} has no inventory to compare.");
        }

        Dictionary<string, int>? client = session.Client.RunOnClientThread(() =>
        {
            Vintagestory.API.Common.IInventory? open = session.Client.Client.player?.InventoryManager.OpenedInventories
                .FirstOrDefault(inventory => inventory.ClassName == server.Value.ClassName);
            return open == null ? null : ItemsOf(open);
        });
        if (client == null)
        {
            throw new PharosAssertException(
                $"The client has no open inventory of the class '{server.Value.ClassName}', so the copies cannot be compared.");
        }

        List<string> problems = [];
        foreach (string code in server.Value.Items.Keys.Union(client.Keys).Order(StringComparer.Ordinal))
        {
            int had = server.Value.Items.GetValueOrDefault(code);
            int has = client.GetValueOrDefault(code);
            if (had == has) continue;
            problems.Add($"{code}: the server has {had}, the client has {has}");
        }

        if (problems.Count == 0) return;

        string list = string.Join(Environment.NewLine, problems.Select(problem => "  " + problem));
        throw new PharosAssertException(
            $"The client's copy of the '{server.Value.ClassName}' at {position} differs from the server's:{Environment.NewLine}{list}");
    }

    private static Dictionary<string, int> ItemsOf(Vintagestory.API.Common.IInventory? inventory)
    {
        Dictionary<string, int> items = new(StringComparer.Ordinal);
        if (inventory == null) return items;

        foreach (Vintagestory.API.Common.ItemSlot slot in inventory)
        {
            Vintagestory.API.Common.ItemStack? stack = slot?.Itemstack;
            if (stack?.Collectible == null) continue;
            string code = stack.Collectible.Code.ToString();
            items[code] = items.GetValueOrDefault(code) + stack.StackSize;
        }

        return items;
    }

    // -------------------------------------------------------------------------
    // Bag assertions
    // -------------------------------------------------------------------------

    /// <summary>
    /// Asserts that the items a bag held are on the ground near where the player fell, in the item
    /// entities the game dropped them in.
    /// </summary>
    /// <param name="host">The server whose world is read.</param>
    /// <param name="before">How many items the bag held.</param>
    /// <param name="inItemEntityNear">Where the player fell.</param>
    /// <param name="code">The code of the items, such as <c>game:flint</c>.</param>
    /// <param name="radius">How many blocks around the position the items may lie.</param>
    /// <exception cref="PharosAssertException">Thrown when the items are not there.</exception>
    public static void BagContentsPreserved(
        EmbeddedServerHost host,
        int before,
        Vintagestory.API.MathTools.BlockPos inItemEntityNear,
        string code,
        int radius = 4)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentOutOfRangeException.ThrowIfNegative(before);

        int found = host.RunOnGameThread(() =>
        {
            Vintagestory.API.MathTools.BlockPos from = inItemEntityNear.AddCopy(-radius, -radius, -radius);
            Vintagestory.API.MathTools.BlockPos to = inItemEntityNear.AddCopy(radius, radius, radius);
            int total = 0;
            foreach (Vintagestory.API.Common.Entities.Entity entity in host.Api.World.GetEntitiesInsideCuboid(from, to))
            {
                if (entity is not Vintagestory.API.Common.EntityItem item || item.Itemstack == null) continue;

                if (item.Itemstack.Collectible?.Code.ToString() == code)
                {
                    total += item.Itemstack.StackSize;
                    continue;
                }

                // The items may lie inside a bag that fell with them.
                foreach (Vintagestory.API.Common.CollectibleBehavior behavior in item.Itemstack.Collectible.CollectibleBehaviors)
                {
                    if (behavior is not Vintagestory.API.Common.IHeldBag bag) continue;
                    foreach (Vintagestory.API.Common.ItemStack content in bag.GetContents(item.Itemstack, host.Api.World))
                    {
                        if (content?.Collectible?.Code.ToString() == code) total += content.StackSize;
                    }
                }
            }

            return total;
        });

        if (found >= before) return;

        throw new PharosAssertException(
            $"The bag held {before} x {code}, but only {found} lie in the item entities near {inItemEntityNear}.");
    }

    // -------------------------------------------------------------------------
    // Transition assertions
    // -------------------------------------------------------------------------

    /// <summary>
    /// Asserts that a stack spoils within the given days, the way the item itself says.
    /// </summary>
    /// <param name="host">The server whose items are read.</param>
    /// <param name="slot">The slot the stack sits in.</param>
    /// <param name="days">How many days the item should stay fresh.</param>
    /// <param name="tolerance">How many days the fresh time may differ.</param>
    /// <exception cref="PharosAssertException">Thrown when the item does not spoil, or spoils in another time.</exception>
    public static void SpoilsWithin(EmbeddedServerHost host, Vintagestory.API.Common.ItemSlot slot, double days, double tolerance = 1)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(slot);
        ArgumentOutOfRangeException.ThrowIfNegative(tolerance);

        Vintagestory.API.Common.TransitionState? perish = host.Items.Transition(slot, Vintagestory.API.Common.EnumTransitionType.Perish);
        if (perish == null)
        {
            throw new PharosAssertException("The item in the slot does not spoil, so it cannot spoil within a time.");
        }

        double fresh = perish.FreshHours / 24f;
        if (Math.Abs(fresh - days) > tolerance)
        {
            throw new PharosAssertException(
                $"The item stays fresh for {fresh:0.##} days, not {days:0.##} days (tolerance {tolerance:0.##}).");
        }
    }

    /// <summary>
    /// Asserts that a block spreads to a neighbour: the block at <paramref name="to"/> becomes the
    /// block at <paramref name="from"/> when the region is ticked.
    /// </summary>
    /// <param name="host">The server whose blocks are ticked.</param>
    /// <param name="from">The block that spreads.</param>
    /// <param name="to">The block that should take the spread.</param>
    /// <param name="maxRounds">The most rounds of ticks to give it.</param>
    /// <param name="seed">The seed of the random the blocks see.</param>
    /// <exception cref="PharosAssertException">Thrown when the spread did not happen.</exception>
    public static void SpreadsTo(EmbeddedServerHost host, Vintagestory.API.MathTools.BlockPos from, Vintagestory.API.MathTools.BlockPos to, int maxRounds = 50, int seed = 0)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(from);
        ArgumentNullException.ThrowIfNull(to);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxRounds);

        int id = host.GetBlock(from).Id;
        Vintagestory.API.MathTools.BlockPos min = new(
            Math.Min(from.X, to.X) - 1,
            Math.Min(from.Y, to.Y) - 1,
            Math.Min(from.Z, to.Z) - 1);
        Vintagestory.API.MathTools.BlockPos max = new(
            Math.Max(from.X, to.X) + 1,
            Math.Max(from.Y, to.Y) + 1,
            Math.Max(from.Z, to.Z) + 1);

        for (int round = 0; round < maxRounds; round++)
        {
            host.BlockTicks.TickRegionAsync(min, max, rounds: 1, seed: seed + round).GetAwaiter().GetResult();
            if (host.GetBlock(to).Id == id) return;
        }

        throw new PharosAssertException(
            $"The block at {to} did not become the block at {from} within {maxRounds} rounds of ticks.");
    }

    /// <summary>
    /// Asserts that a transition runs slower in one container than in another.
    /// </summary>
    /// <param name="host">The server whose items are read.</param>
    /// <param name="type">The kind of transition, such as <see cref="Vintagestory.API.Common.EnumTransitionType.Perish"/>.</param>
    /// <param name="slower">The slot whose container slows the transition down.</param>
    /// <param name="faster">The slot whose container lets it run faster.</param>
    /// <exception cref="PharosAssertException">Thrown when the transition is not slower in the first container.</exception>
    public static void SlowerIn(
        EmbeddedServerHost host,
        Vintagestory.API.Common.EnumTransitionType type,
        Vintagestory.API.Common.ItemSlot slower,
        Vintagestory.API.Common.ItemSlot faster)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(slower);
        ArgumentNullException.ThrowIfNull(faster);

        float slow = host.Items.TransitionRate(slower, type);
        float fast = host.Items.TransitionRate(faster, type);
        if (slow < fast) return;

        throw new PharosAssertException(
            $"The {type} transition runs at {slow:0.###} in the first container and {fast:0.###} in the second, so it is not slower in the first.");
    }

    // -------------------------------------------------------------------------
    // World, recipe and processor assertions
    // -------------------------------------------------------------------------

    /// <summary>
    /// Asserts that two captures of a world hold the same state: the same blocks, fluids, block-entity trees and entities, in the
    /// same places. Paths that start with an <paramref name="ignore"/> entry are left out.
    /// </summary>
    /// <param name="before">The state before the change, such as a save and reload.</param>
    /// <param name="after">The state after it.</param>
    /// <param name="ignore">Paths to leave out, such as <c>savegame mod data</c> for a counter that a save advances.</param>
    /// <exception cref="PharosAssertException">Thrown when the states differ, listing up to 50 differences, each with its path, before and after.</exception>
    public static void SameWorldState(WorldState before, WorldState after, params string[] ignore)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);

        IReadOnlyList<string> differences = WorldState.Differences(before, after, ignore);
        if (differences.Count == 0)
        {
            return;
        }

        const int Shown = 50;
        BlockPos c = after.Center;
        var message = new System.Text.StringBuilder();
        message.Append($"World state differs in {differences.Count} place(s) around {c.X},{c.InternalY},{c.Z} r={after.Radius}:");
        foreach (string line in differences.Take(Shown))
        {
            message.Append(Environment.NewLine).Append("  ").Append(line);
        }

        if (differences.Count > Shown)
        {
            message.Append(Environment.NewLine).Append($"  ... {differences.Count - Shown} more");
        }

        throw new PharosAssertException(message.ToString());
    }

    /// <summary>
    /// Asserts that every recipe of a domain loaded: each recipe the game found resolved, and every ingredient, output and wildcard
    /// in the domain's recipe files names an item or block. Pass the list from <c>Host.Recipes.Unresolved</c>.
    /// </summary>
    /// <param name="unresolved">The recipes that cannot be used, as the server reports them.</param>
    /// <param name="domain">The domain the list was taken for, for the message.</param>
    /// <exception cref="PharosAssertException">Thrown when any recipe of the domain cannot be used, naming each one and why.</exception>
    public static void RecipesLoaded(IEnumerable<UnresolvedRecipe> unresolved, string domain)
    {
        ArgumentNullException.ThrowIfNull(unresolved);
        ArgumentException.ThrowIfNullOrWhiteSpace(domain);
        UnresolvedRecipe[] problems = unresolved.ToArray();
        if (problems.Length == 0)
        {
            return;
        }

        string list = string.Join(Environment.NewLine, problems.Select(p => $"  {p.Recipe}: {p.Reason}"));
        throw new PharosAssertException($"{problems.Length} recipe(s) in the {domain} domain cannot be used:{Environment.NewLine}{list}");
    }

    /// <summary>
    /// Asserts that a processor's output holds the expected item or block and quantity, such as a firepit's output
    /// slot, a quern's output, or a barrel's liquid after its sealing time.
    /// </summary>
    /// <param name="output">The code and quantity the processor holds, or null when it holds nothing.</param>
    /// <param name="code">The expected code, such as <c>game:fish-cooked</c>.</param>
    /// <param name="quantity">The expected stack size.</param>
    /// <exception cref="PharosAssertException">Thrown when the output is empty, or differs in code or quantity.</exception>
    public static void Produces((string Code, int Quantity)? output, string code, int quantity)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        if (output is null)
        {
            throw new PharosAssertException($"Expected the processor to produce {quantity} x {code}, but its output is empty.");
        }

        if (!string.Equals(output.Value.Code, code, StringComparison.Ordinal) || output.Value.Quantity != quantity)
        {
            throw new PharosAssertException(
                $"Expected the processor to produce {quantity} x {code}, but it produced {output.Value.Quantity} x {output.Value.Code}.");
        }
    }

    /// <summary>
    /// Asserts that the server refused a player action and sent the given in-game error code.
    /// </summary>
    /// <param name="result">The bot result for the action.</param>
    /// <param name="code">The in-game error code the server should send, such as <c>noprivilege-buildbreak-landclaimed</c>.</param>
    /// <exception cref="PharosAssertException">Thrown when the action was accepted or the server sent a different code.</exception>
    public static void Denied(BotResult result, string code)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentException.ThrowIfNullOrWhiteSpace(code);

        if (result.Accepted)
        {
            throw new PharosAssertException($"Expected the action to be denied with '{code}', but the server accepted it.");
        }

        string? sent = result.Error?.Code;
        if (!string.Equals(sent, code, StringComparison.Ordinal))
        {
            throw new PharosAssertException(
                $"Expected the action to be denied with '{code}', but the server sent '{sent ?? result.Reason ?? "no error"}'.");
        }
    }

    // -------------------------------------------------------------------------
    // Golden image assertions
    // -------------------------------------------------------------------------

    /// <summary>Asserts a framebuffer snapshot against a named region-aware golden image.</summary>
    /// <param name="frame">Captured framebuffer pixels.</param>
    /// <param name="name">Golden name without a platform suffix; the final <c>.png</c> is optional.</param>
    /// <param name="options">Regions, masks, comparison tolerances, and metric.</param>
    /// <exception cref="PharosAssertException">The golden is missing or the comparison exceeds its tolerances.</exception>
    public static void MatchesGolden(FramebufferSnapshot frame, string name, GoldenImageOptions? options = null) =>
        GoldenImageMatcher.AssertMatches(frame, name, options);

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
        if (report.FramebufferLeaks > 0) leaks.Add($"Framebuffers: {report.FramebufferLeaks}");
        if (report.RenderbufferLeaks > 0) leaks.Add($"Renderbuffers: {report.RenderbufferLeaks}");

        throw new PharosAssertException(
            $"OpenGL resource leaks detected. {string.Join(", ", leaks)}. " +
            $"Total: {report.TotalResourceLeaks} leaked resources.");
    }

    /// <summary>
    /// Asserts that the client deleted every OpenGL object of <paramref name="kinds"/> it created
    /// while <paramref name="scope"/> was open. The message lists each one, with the code that
    /// created it when the scope captured stacks.
    /// </summary>
    /// <exception cref="PharosAssertException">Something leaked.</exception>
    public static void NoGlLeaks(GlResourceScope scope, GlResourceKind kinds = GlResourceKind.All)
    {
        ArgumentNullException.ThrowIfNull(scope);
        GlResourceReport report = scope.Report(kinds);
        if (report.HasLeaks) throw new PharosAssertException("OpenGL resource leaks detected: " + report);
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
    /// <remarks>Framebuffer and renderbuffer leaks are not checked; the overload that takes their limits checks them.</remarks>
    public static void GlLeaksBelow(GlLeakReport report, int maxBufferLeaks, int maxTextureLeaks, int maxVaoLeaks) =>
        GlLeaksBelow(report, maxBufferLeaks, maxTextureLeaks, maxVaoLeaks, int.MaxValue, int.MaxValue);

    /// <summary>
    /// Asserts that GL resource leaks, framebuffers and renderbuffers included, are within the
    /// specified thresholds.
    /// </summary>
    /// <param name="report">The GL leak report to validate.</param>
    /// <param name="maxBufferLeaks">Maximum allowed buffer leaks.</param>
    /// <param name="maxTextureLeaks">Maximum allowed texture leaks.</param>
    /// <param name="maxVaoLeaks">Maximum allowed VAO leaks.</param>
    /// <param name="maxFramebufferLeaks">Maximum allowed framebuffer leaks.</param>
    /// <param name="maxRenderbufferLeaks">Maximum allowed renderbuffer leaks.</param>
    /// <exception cref="PharosAssertException">Thrown when any leak count exceeds its threshold.</exception>
    public static void GlLeaksBelow(GlLeakReport report, int maxBufferLeaks, int maxTextureLeaks, int maxVaoLeaks, int maxFramebufferLeaks, int maxRenderbufferLeaks)
    {
        ArgumentNullException.ThrowIfNull(report);

        List<string> violations = [];

        if (report.BufferLeaks > maxBufferLeaks)
            violations.Add($"Buffers: {report.BufferLeaks} > {maxBufferLeaks}");
        if (report.TextureLeaks > maxTextureLeaks)
            violations.Add($"Textures: {report.TextureLeaks} > {maxTextureLeaks}");
        if (report.VAOLeaks > maxVaoLeaks)
            violations.Add($"VAOs: {report.VAOLeaks} > {maxVaoLeaks}");
        if (report.FramebufferLeaks > maxFramebufferLeaks)
            violations.Add($"Framebuffers: {report.FramebufferLeaks} > {maxFramebufferLeaks}");
        if (report.RenderbufferLeaks > maxRenderbufferLeaks)
            violations.Add($"Renderbuffers: {report.RenderbufferLeaks} > {maxRenderbufferLeaks}");

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

    /// <summary>
    /// Asserts that the dialog fits in the window: each of its composers and each of its elements
    /// lies inside the screen, to within <paramref name="tolerance"/> pixels. Elements in a
    /// clipped region, such as the rows of a scrolled list, show only where the region does and
    /// are not checked; the region itself is.
    /// </summary>
    /// <exception cref="PharosAssertException">Something lies off screen; the message says what and by how much.</exception>
    public static void DialogOnScreen(UI.DialogLayout layout, double tolerance = 1)
    {
        ArgumentNullException.ThrowIfNull(layout);
        List<string> off = [];
        foreach (UI.ComposerBounds composer in layout.Composers)
        {
            if (OffScreen(layout, composer.X, composer.Y, composer.Width, composer.Height, tolerance) is { } how)
            {
                off.Add($"composer '{composer.Composer}' {how}");
            }
        }

        foreach (UI.GuiElementInfo element in layout.Elements)
        {
            if (element.Width <= 0 || element.Height <= 0 || element.IsClipped) continue;
            if (OffScreen(layout, element.X, element.Y, element.Width, element.Height, tolerance) is { } how)
            {
                off.Add($"{element.Type} '{element.Key}' {how}");
            }
        }

        if (off.Count > 0)
        {
            throw new PharosAssertException($"{layout} does not fit on screen:\n  " + string.Join("\n  ", off.Take(30)) + (off.Count > 30 ? $"\n  ... and {off.Count - 30} more" : ""));
        }
    }

    /// <summary>Asserts that the open dialog <paramref name="nameOrType"/> fits in the window. See <see cref="DialogOnScreen(UI.DialogLayout, double)"/>.</summary>
    public static void DialogOnScreen(Core.HeadlessClient client, string nameOrType, double tolerance = 1)
    {
        ArgumentNullException.ThrowIfNull(client);
        UI.GuiDriver ui = client.Ui ?? throw new InvalidOperationException("The client has no GUI driver: it is not in engine mode.");
        DialogOnScreen(ui.Layout(nameOrType), tolerance);
    }

    /// <summary>
    /// Asserts that no two elements of the same composer overlap by more than
    /// <paramref name="tolerance"/> pixels each way. By default only the elements a player reads or
    /// uses are compared: buttons, inputs, switches, drop-downs, slot grids and text with text in
    /// it (see <see cref="UI.DialogLayout.ChecksOverlap"/>); containers and backgrounds hold
    /// their children and always overlap them.
    /// </summary>
    /// <exception cref="PharosAssertException">Some overlap; the message names each pair.</exception>
    public static void NoOverlappingElements(UI.DialogLayout layout, double tolerance = 1, Func<UI.GuiElementInfo, bool>? include = null)
    {
        ArgumentNullException.ThrowIfNull(layout);
        include ??= UI.DialogLayout.ChecksOverlap;
        List<string> overlaps = [];
        foreach (IGrouping<string, UI.GuiElementInfo> composer in layout.Elements.Where(e => e.Width > 0 && e.Height > 0 && include(e)).GroupBy(e => e.Composer))
        {
            UI.GuiElementInfo[] elements = [.. composer];
            for (int i = 0; i < elements.Length; i++)
            {
                for (int j = i + 1; j < elements.Length; j++)
                {
                    UI.GuiElementInfo a = elements[i], b = elements[j];
                    double width = Math.Min(a.X + a.Width, b.X + b.Width) - Math.Max(a.X, b.X);
                    double height = Math.Min(a.Y + a.Height, b.Y + b.Height) - Math.Max(a.Y, b.Y);
                    if (width > tolerance && height > tolerance)
                    {
                        overlaps.Add(Invariant($"'{a.Key}' ({a.Type}) and '{b.Key}' ({b.Type}) in '{composer.Key}' overlap by {width:0.#}x{height:0.#} px"));
                    }
                }
            }
        }

        if (overlaps.Count > 0)
        {
            throw new PharosAssertException($"{layout} has overlapping elements:\n  " + string.Join("\n  ", overlaps.Take(30)) + (overlaps.Count > 30 ? $"\n  ... and {overlaps.Count - 30} more" : ""));
        }
    }

    /// <summary>Asserts that no two elements of the open dialog overlap. See <see cref="NoOverlappingElements(UI.DialogLayout, double, Func{UI.GuiElementInfo, bool})"/>.</summary>
    public static void NoOverlappingElements(Core.HeadlessClient client, string nameOrType, double tolerance = 1, Func<UI.GuiElementInfo, bool>? include = null)
    {
        ArgumentNullException.ThrowIfNull(client);
        UI.GuiDriver ui = client.Ui ?? throw new InvalidOperationException("The client has no GUI driver: it is not in engine mode.");
        NoOverlappingElements(ui.Layout(nameOrType), tolerance, include);
    }

    private static string? OffScreen(UI.DialogLayout layout, double x, double y, double width, double height, double tolerance)
    {
        List<string> sides = [];
        if (x < -tolerance) sides.Add(Invariant($"{-x:0.#} px past the left edge"));
        if (y < -tolerance) sides.Add(Invariant($"{-y:0.#} px past the top edge"));
        if (x + width > layout.ScreenWidth + tolerance) sides.Add(Invariant($"{x + width - layout.ScreenWidth:0.#} px past the right edge"));
        if (y + height > layout.ScreenHeight + tolerance) sides.Add(Invariant($"{y + height - layout.ScreenHeight:0.#} px past the bottom edge"));
        return sides.Count == 0 ? null : Invariant($"at ({x:0.#}, {y:0.#}) {width:0.#}x{height:0.#} is ") + string.Join(" and ", sides);
    }

    /// <summary>
    /// Asserts that every item and block of <paramref name="domain"/> has a name in
    /// <paramref name="language"/>: the <c>item-</c> or <c>block-</c> key the game looks a
    /// collectible's name up by. Names a class builds in code, by overriding
    /// <c>GetHeldItemName</c>, are not checked.
    /// </summary>
    /// <param name="client">An engine-mode client that has joined a world.</param>
    /// <param name="domain">The domain, such as your mod's id.</param>
    /// <param name="language">The language code, or null for the language shown now.</param>
    /// <exception cref="ArgumentException">The game has no such language.</exception>
    /// <exception cref="PharosAssertException">Some have no name; the message lists them.</exception>
    public static void AllTranslated(Core.HeadlessClient client, string domain, string? language = null)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentException.ThrowIfNullOrWhiteSpace(domain);
        (int checkedCount, List<string> missing, string lang) = client.RunOnClientThread(() =>
        {
            string code = language ?? Vintagestory.API.Config.Lang.CurrentLocale;
            if (!Vintagestory.API.Config.Lang.AvailableLanguages.TryGetValue(code, out Vintagestory.API.Config.ITranslationService? service))
            {
                throw new ArgumentException($"The game has no language '{code}'.", nameof(language));
            }

            service.UseAssetManager(client.Client.Platform.AssetManager);
            List<string> keys = [];
            foreach (Vintagestory.API.Common.Block block in client.Client.World.Blocks)
            {
                if (block?.Code != null && block.Code.Domain == domain) keys.Add($"{domain}:block-{block.Code.Path}");
            }

            foreach (Vintagestory.API.Common.Item item in client.Client.World.Items)
            {
                if (item?.Code != null && item.Code.Domain == domain) keys.Add($"{domain}:item-{item.Code.Path}");
            }

            return (keys.Count, keys.Where(key => !service.HasTranslation(key, findWildcarded: true, logErrors: false)).ToList(), code);
        });

        if (checkedCount == 0)
        {
            throw new PharosAssertException($"The client has no item or block in the domain '{domain}'.");
        }

        if (missing.Count > 0)
        {
            throw new PharosAssertException(
                $"{missing.Count} of {checkedCount} items and blocks of '{domain}' have no name in '{lang}':\n  " +
                string.Join("\n  ", missing.Take(50)) + (missing.Count > 50 ? $"\n  ... and {missing.Count - 50} more" : ""));
        }
    }

    /// <summary>
    /// Asserts that the client looked up no translation key it has no entry for, since its boot or
    /// the start of the test; see <see cref="Translations.LanguageDriver.MissingKeys"/>.
    /// </summary>
    /// <param name="client">An engine-mode client.</param>
    /// <param name="domain">Only keys of this domain, such as your mod's id, or null for all.</param>
    /// <param name="includeFallbacks">Whether keys the game showed in English instead count too.</param>
    /// <exception cref="PharosAssertException">Some were missing; the message lists them.</exception>
    public static void NoMissingTranslations(Core.HeadlessClient client, string? domain = null, bool includeFallbacks = false)
    {
        ArgumentNullException.ThrowIfNull(client);
        IReadOnlyList<Translations.MissingTranslation> missing = domain == null
            ? (includeFallbacks ? client.Lang.MissingKeysIncludingFallbacks : client.Lang.MissingKeys)
            : client.Lang.MissingKeysOf(domain, includeFallbacks);
        if (missing.Count == 0) return;

        throw new PharosAssertException(
            $"The client looked up {missing.Count} translation key(s) it has no entry for:\n  " +
            string.Join("\n  ", missing.Take(50).Select(m => $"{m.Key} ({m.Language}{(m.FellBackToDefault ? ", shown in English" : "")}, {m.Count}x)")) +
            (missing.Count > 50 ? $"\n  ... and {missing.Count - 50} more" : ""));
    }

    // -------------------------------------------------------------------------
    // Server tick listener assertions
    // -------------------------------------------------------------------------

    /// <summary>
    /// Asserts that no game tick listener, and by default no delayed callback, is tied to
    /// <paramref name="pos"/> on the server: what a block entity leaves behind when it is removed
    /// without unregistering. See <see cref="Server.ServerTickListenerInspector.At"/>.
    /// </summary>
    /// <param name="host">The server.</param>
    /// <param name="pos">The block position.</param>
    /// <param name="includeCallbacks">
    /// Whether delayed callbacks count too. Blocks schedule some on their own, such as liquid
    /// flow; pass false when only listeners matter.
    /// </param>
    /// <exception cref="PharosAssertException">Some were left; the message lists them.</exception>
    public static void NoTickListenersAt(Server.EmbeddedServerHost host, Vintagestory.API.MathTools.BlockPos pos, bool includeCallbacks = true)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(pos);
        List<string> left = [.. host.TickListeners.At(pos).Select(l => $"listener #{l.Id} {l.Handler} ({l.Mod ?? "engine"}) every {l.IntervalMs} ms")];
        if (includeCallbacks)
            left.AddRange(host.TickListeners.CallbacksAt(pos).Select(c => $"callback #{c.Id} {c.Handler} ({c.Mod ?? "engine"}) due in {c.DueInMs} ms"));
        if (left.Count == 0) return;

        throw new PharosAssertException(
            $"{left.Count} tick listener(s) or callback(s) are still tied to {pos}:\n  " + string.Join("\n  ", left));
    }

    /// <summary>
    /// Asserts that <paramref name="log"/> holds records matching <paramref name="first"/> and then
    /// each of <paramref name="rest"/>, in that order. Other records may come between them. Each
    /// expectation takes the earliest matching record after the one before it.
    /// </summary>
    /// <param name="log">The log, read now.</param>
    /// <param name="first">The first expected record: a record type, or an <see cref="Events.EventMatch"/>.</param>
    /// <param name="rest">The records expected after it, in order.</param>
    /// <example><c>PharosAssert.EventOrder(log, typeof(EntitySpawned), EventMatch.Of&lt;EntityDied&gt;(d =&gt; d.EntityId == id));</c></example>
    /// <exception cref="PharosAssertException">One was missing or out of order; the message lists the records.</exception>
    public static void EventOrder(Events.GameEventLog log, Events.EventMatch first, params Events.EventMatch[] rest)
    {
        ArgumentNullException.ThrowIfNull(log);
        EventOrder(log.All, first, rest);
    }

    /// <summary>
    /// Asserts that <paramref name="records"/> hold records matching <paramref name="first"/> and
    /// then each of <paramref name="rest"/>, in that order. See
    /// <see cref="EventOrder(Events.GameEventLog, Events.EventMatch, Events.EventMatch[])"/>.
    /// </summary>
    /// <exception cref="PharosAssertException">One was missing or out of order; the message lists the records.</exception>
    public static void EventOrder(IEnumerable<Events.GameEventRecord> records, Events.EventMatch first, params Events.EventMatch[] rest)
    {
        ArgumentNullException.ThrowIfNull(records);
        ArgumentNullException.ThrowIfNull(first);
        ArgumentNullException.ThrowIfNull(rest);
        List<Events.GameEventRecord> list = [.. records];
        Events.EventMatch[] expected = [first, .. rest];
        int at = 0;
        Events.GameEventRecord? previous = null;
        for (int i = 0; i < expected.Length; i++)
        {
            int found = list.FindIndex(at, r => expected[i].Matches(r));
            if (found < 0)
            {
                bool anywhere = list.Any(expected[i].Matches);
                string where = previous == null ? "" : $" after {previous}";
                throw new PharosAssertException(
                    $"Expected {expected[i]} (#{i + 1} of {expected.Length}){where}, but {(anywhere ? "it came only before that" : "none was recorded")}. " +
                    Events.GameEventLog.Listing(list));
            }

            previous = list[found];
            at = found + 1;
        }
    }
}
