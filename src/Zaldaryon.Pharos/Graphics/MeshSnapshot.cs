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
}

/// <summary>
/// A single UV coordinate sample from a mesh vertex.
/// </summary>
public readonly record struct UvSample(float U, float V);
