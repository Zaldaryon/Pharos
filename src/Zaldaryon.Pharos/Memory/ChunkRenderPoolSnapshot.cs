using System.Collections.Generic;

namespace Zaldaryon.Pharos.Memory;

/// <summary>
/// Immutable snapshot of the chunk render mesh pool state.
/// This captures the pools used by <c>ChunkRenderer</c> to store terrain mesh data.
/// Call <see cref="MemoryInspector.ChunkRenderPoolSnapshot"/> to capture one.
/// </summary>
public sealed record ChunkRenderPoolSnapshot
{
    /// <summary>Total number of <c>MeshDataPool</c> instances managed by the master pool.</summary>
    public int TotalPoolCount { get; init; }

    /// <summary>Total vertices allocated across all chunk render pools.</summary>
    public long TotalVerticesAllocated { get; init; }

    /// <summary>Total vertices currently used across all chunk render pools.</summary>
    public long TotalVerticesUsed { get; init; }

    /// <summary>Total indices allocated across all chunk render pools.</summary>
    public long TotalIndicesAllocated { get; init; }

    /// <summary>Total triangles rendered in the last frame.</summary>
    public long TotalRenderedTriangles { get; init; }

    /// <summary>Total triangles allocated (may differ from rendered due to culling).</summary>
    public long TotalAllocatedTriangles { get; init; }

    /// <summary>Estimated video memory used by chunk render pools in bytes.</summary>
    public long EstimatedVideoMemoryBytes { get; init; }

    /// <summary>Average fragmentation across all pools (0.0 = no fragmentation, 1.0 = fully fragmented).</summary>
    public double AverageFragmentation { get; init; }

    /// <summary>
    /// Per-render-pass pool statistics.
    /// Keys are render pass names (Opaque, OpaqueNoCull, BlendNoCull, Liquid, TopSoil, Meta, Transparent).
    /// </summary>
    public IReadOnlyDictionary<string, RenderPassPoolStats> ByRenderPass { get; init; } =
        new Dictionary<string, RenderPassPoolStats>();

    /// <summary>
    /// Locations pending delayed removal (chunks being unloaded but held for a few frames).
    /// </summary>
    public int PendingRemovalCount { get; init; }

    /// <summary>
    /// Whether the snapshot contains valid data.
    /// False if the ChunkRenderer could not be accessed.
    /// </summary>
    public bool IsValid { get; init; }

    /// <summary>
    /// Diagnostic message if <see cref="IsValid"/> is false.
    /// </summary>
    public string? DiagnosticMessage { get; init; }

    /// <summary>Empty/invalid snapshot.</summary>
    public static readonly ChunkRenderPoolSnapshot Empty = new()
    {
        IsValid = false,
        DiagnosticMessage = "ChunkRenderer not accessible"
    };
}

/// <summary>
/// Statistics for a single render pass's mesh pools.
/// </summary>
public sealed record RenderPassPoolStats
{
    /// <summary>The render pass name.</summary>
    public string RenderPass { get; init; } = "";

    /// <summary>Number of pools for this render pass.</summary>
    public int PoolCount { get; init; }

    /// <summary>Total vertices allocated in this render pass.</summary>
    public long VerticesAllocated { get; init; }

    /// <summary>Total vertices used in this render pass.</summary>
    public long VerticesUsed { get; init; }

    /// <summary>Total indices allocated in this render pass.</summary>
    public long IndicesAllocated { get; init; }

    /// <summary>Triangles rendered in the last frame for this pass.</summary>
    public long RenderedTriangles { get; init; }

    /// <summary>Triangles allocated for this pass.</summary>
    public long AllocatedTriangles { get; init; }

    /// <summary>Number of chunk parts (locations) in this render pass.</summary>
    public int ChunkPartCount { get; init; }
}
