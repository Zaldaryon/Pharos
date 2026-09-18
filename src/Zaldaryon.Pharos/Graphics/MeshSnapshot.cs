namespace Zaldaryon.Pharos.Graphics;

/// <summary>
/// Immutable snapshot of mesh geometry captured from a chunk or MeshData.
/// </summary>
public sealed record MeshSnapshot
{
    /// <summary>Number of vertices in the mesh.</summary>
    public int VertexCount { get; init; }

    /// <summary>Number of indices in the index buffer (0 if non-indexed).</summary>
    public int IndexCount { get; init; }

    /// <summary>Number of triangular faces (IndexCount / 3 for indexed, VertexCount / 3 otherwise).</summary>
    public int FaceCount { get; init; }

    /// <summary>Number of quads (FaceCount / 2, assuming quad-based meshing).</summary>
    public int QuadCount { get; init; }

    /// <summary>Sample of UV coordinates from the mesh (first N vertices).</summary>
    public IReadOnlyList<UvSample> UvSamples { get; init; } = [];

    /// <summary>Vertex positions for UV continuity analysis (optional, populated for adjacency checks).</summary>
    public IReadOnlyList<VertexPosition> VertexPositions { get; init; } = [];

    /// <summary>Quad edges representing adjacencies in the mesh (populated by MeshInspector.ExtractQuadAdjacency).</summary>
    public IReadOnlyList<QuadEdge> QuadEdges { get; init; } = [];

    /// <summary>True if mesh data was successfully extracted, false if reflection failed.</summary>
    public bool IsValid { get; init; }

    /// <summary>Error message if extraction failed, otherwise null.</summary>
    public string? ErrorMessage { get; init; }

    /// <summary>Empty snapshot sentinel for invalid or unavailable mesh data.</summary>
    public static readonly MeshSnapshot Empty = new() { IsValid = false };

    /// <summary>
    /// Creates a synthetic snapshot for testing without GPU context.
    /// </summary>
    /// <param name="vertexCount">Number of vertices.</param>
    /// <param name="indexCount">Number of indices (used to compute face count).</param>
    /// <param name="uvSamples">Optional UV samples.</param>
    /// <returns>A valid MeshSnapshot with computed face and quad counts.</returns>
    public static MeshSnapshot CreateSynthetic(int vertexCount, int indexCount, IReadOnlyList<UvSample>? uvSamples = null)
    {
        int faceCount = indexCount > 0 ? indexCount / 3 : vertexCount / 3;
        return new MeshSnapshot
        {
            VertexCount = vertexCount,
            IndexCount = indexCount,
            FaceCount = faceCount,
            QuadCount = faceCount / 2,
            UvSamples = uvSamples ?? [],
            IsValid = true,
        };
    }

    /// <summary>
    /// Creates a synthetic snapshot with UV samples, vertex positions, and quad edges for UV continuity testing.
    /// </summary>
    /// <param name="vertexCount">Number of vertices.</param>
    /// <param name="indexCount">Number of indices.</param>
    /// <param name="uvSamples">UV coordinates for each vertex.</param>
    /// <param name="vertexPositions">3D positions for each vertex.</param>
    /// <param name="quadEdges">Pre-computed quad adjacencies.</param>
    /// <returns>A valid MeshSnapshot for UV continuity testing.</returns>
    public static MeshSnapshot CreateSyntheticWithEdges(
        int vertexCount,
        int indexCount,
        IReadOnlyList<UvSample> uvSamples,
        IReadOnlyList<VertexPosition> vertexPositions,
        IReadOnlyList<QuadEdge> quadEdges)
    {
        int faceCount = indexCount > 0 ? indexCount / 3 : vertexCount / 3;
        return new MeshSnapshot
        {
            VertexCount = vertexCount,
            IndexCount = indexCount,
            FaceCount = faceCount,
            QuadCount = faceCount / 2,
            UvSamples = uvSamples,
            VertexPositions = vertexPositions,
            QuadEdges = quadEdges,
            IsValid = true,
        };
    }
}

/// <summary>
/// A single UV coordinate sample from a mesh vertex.
/// </summary>
public readonly record struct UvSample(float U, float V);

/// <summary>
/// A vertex position in 3D space for adjacency analysis.
/// </summary>
public readonly record struct VertexPosition(float X, float Y, float Z)
{
    /// <summary>
    /// Computes squared distance to another position.
    /// </summary>
    public float DistanceSquaredTo(VertexPosition other)
    {
        float dx = X - other.X;
        float dy = Y - other.Y;
        float dz = Z - other.Z;
        return dx * dx + dy * dy + dz * dz;
    }
}
