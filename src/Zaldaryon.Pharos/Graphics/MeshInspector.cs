using System.Reflection;
using Vintagestory.API.Client;
using Vintagestory.Client.NoObf;

namespace Zaldaryon.Pharos.Graphics;

/// <summary>
/// Extracts mesh geometry data from ClientChunk render data or MeshData objects
/// via reflection. Gracefully returns empty snapshot when GPU context unavailable.
/// </summary>
public sealed class MeshInspector
{
    // Reflected fields for ClientChunk mesh access
    private static readonly FieldInfo? s_meshRefField =
        typeof(ClientChunk).GetField("chunkMeshRef", BindingFlags.Instance | BindingFlags.NonPublic);

    // MeshRef fields
    private static readonly FieldInfo? s_vertexCountField =
        typeof(MeshRef).GetField("vertexCount", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
    private static readonly FieldInfo? s_indexCountField =
        typeof(MeshRef).GetField("indexCount", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

    // MeshData direct access for upload-time inspection
    private static readonly FieldInfo? s_meshDataUv =
        typeof(MeshData).GetField("Uv");

    private const int MaxUvSamples = 64;

    /// <summary>
    /// Attempts to extract mesh data from a ClientChunk via reflection.
    /// Returns MeshSnapshot.Empty with ErrorMessage if extraction fails.
    /// </summary>
    /// <param name="chunk">The chunk to inspect (may be null).</param>
    /// <returns>A MeshSnapshot containing mesh geometry data or an invalid snapshot with error details.</returns>
    public MeshSnapshot Snapshot(ClientChunk? chunk)
    {
        if (chunk == null)
            return new MeshSnapshot { IsValid = false, ErrorMessage = "Chunk is null" };

        try
        {
            object? meshRef = s_meshRefField?.GetValue(chunk);
            if (meshRef == null)
                return new MeshSnapshot { IsValid = false, ErrorMessage = "No MeshRef in chunk (not meshed or GPU unavailable)" };

            int vertexCount = (int)(s_vertexCountField?.GetValue(meshRef) ?? 0);
            int indexCount = (int)(s_indexCountField?.GetValue(meshRef) ?? 0);
            int faceCount = indexCount > 0 ? indexCount / 3 : vertexCount / 3;

            return new MeshSnapshot
            {
                VertexCount = vertexCount,
                IndexCount = indexCount,
                FaceCount = faceCount,
                QuadCount = faceCount / 2,
                UvSamples = [], // UV data not retained in MeshRef; requires MeshData
                IsValid = true,
            };
        }
        catch (Exception ex)
        {
            return new MeshSnapshot { IsValid = false, ErrorMessage = ex.Message };
        }
    }

    /// <summary>
    /// Extracts mesh data directly from a MeshData object (pre-upload).
    /// Includes UV samples for validation.
    /// </summary>
    /// <param name="meshData">The MeshData to inspect (may be null).</param>
    /// <returns>A MeshSnapshot containing mesh geometry data with UV samples.</returns>
    public MeshSnapshot Snapshot(MeshData? meshData)
    {
        if (meshData == null)
            return new MeshSnapshot { IsValid = false, ErrorMessage = "MeshData is null" };

        try
        {
            int vertexCount = meshData.VerticesCount;
            int indexCount = meshData.IndicesCount;
            int faceCount = indexCount > 0 ? indexCount / 3 : vertexCount / 3;

            List<UvSample> uvSamples = ExtractUvSamples(meshData, MaxUvSamples);

            return new MeshSnapshot
            {
                VertexCount = vertexCount,
                IndexCount = indexCount,
                FaceCount = faceCount,
                QuadCount = faceCount / 2,
                UvSamples = uvSamples,
                IsValid = true,
            };
        }
        catch (Exception ex)
        {
            return new MeshSnapshot { IsValid = false, ErrorMessage = ex.Message };
        }
    }

    private static List<UvSample> ExtractUvSamples(MeshData meshData, int maxSamples)
    {
        float[]? uv = s_meshDataUv?.GetValue(meshData) as float[] ?? meshData.Uv;
        if (uv == null || uv.Length == 0) return [];

        int count = Math.Min(uv.Length / 2, maxSamples);
        List<UvSample> samples = new(count);
        for (int i = 0; i < count; i++)
        {
            samples.Add(new UvSample(uv[i * 2], uv[i * 2 + 1]));
        }
        return samples;
    }

    /// <summary>
    /// Extracts quad adjacency information from a mesh snapshot for UV continuity analysis.
    /// Identifies pairs of quads that share an edge by analyzing vertex positions.
    /// </summary>
    /// <param name="snapshot">The mesh snapshot to analyze.</param>
    /// <param name="positionTolerance">Tolerance for matching vertex positions (default: 0.001f).</param>
    /// <returns>List of quad edges representing adjacencies found in the mesh.</returns>
    public static IReadOnlyList<QuadEdge> ExtractQuadAdjacency(MeshSnapshot snapshot, float positionTolerance = 0.001f)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        if (!snapshot.IsValid || snapshot.QuadCount < 2 || snapshot.VertexPositions.Count == 0)
        {
            return [];
        }

        List<QuadEdge> edges = [];
        int verticesPerQuad = 4;
        float toleranceSq = positionTolerance * positionTolerance;

        // For each pair of quads, check if they share an edge
        for (int quadA = 0; quadA < snapshot.QuadCount; quadA++)
        {
            for (int quadB = quadA + 1; quadB < snapshot.QuadCount; quadB++)
            {
                QuadEdge? edge = FindSharedEdge(snapshot, quadA, quadB, verticesPerQuad, toleranceSq);
                if (edge.HasValue)
                {
                    edges.Add(edge.Value);
                }
            }
        }

        return edges;
    }

    private static QuadEdge? FindSharedEdge(MeshSnapshot snapshot, int quadA, int quadB, int verticesPerQuad, float toleranceSq)
    {
        int baseA = quadA * verticesPerQuad;
        int baseB = quadB * verticesPerQuad;

        // Check if any two vertices from quadA match two vertices from quadB
        // A shared edge means 2 coincident vertices
        List<(int, int)> matchingPairs = [];

        for (int i = 0; i < verticesPerQuad && matchingPairs.Count < 2; i++)
        {
            if (baseA + i >= snapshot.VertexPositions.Count) continue;
            VertexPosition posA = snapshot.VertexPositions[baseA + i];

            for (int j = 0; j < verticesPerQuad; j++)
            {
                if (baseB + j >= snapshot.VertexPositions.Count) continue;
                VertexPosition posB = snapshot.VertexPositions[baseB + j];

                if (posA.DistanceSquaredTo(posB) < toleranceSq)
                {
                    matchingPairs.Add((baseA + i, baseB + j));
                    break;
                }
            }
        }

        if (matchingPairs.Count < 2) return null;

        // Determine shared axis from the two matching vertices
        VertexPosition v1 = snapshot.VertexPositions[matchingPairs[0].Item1];
        VertexPosition v2 = snapshot.VertexPositions[matchingPairs[1].Item1];

        // Find which axis has the same value (shared edge axis)
        int sharedAxis;
        float sharedValue;

        float dx = Math.Abs(v1.X - v2.X);
        float dy = Math.Abs(v1.Y - v2.Y);
        float dz = Math.Abs(v1.Z - v2.Z);

        if (dx < toleranceSq && dy >= toleranceSq && dz >= toleranceSq)
        {
            sharedAxis = 0; // X is shared
            sharedValue = v1.X;
        }
        else if (dy < toleranceSq && dx >= toleranceSq && dz >= toleranceSq)
        {
            sharedAxis = 1; // Y is shared
            sharedValue = v1.Y;
        }
        else if (dz < toleranceSq && dx >= toleranceSq && dy >= toleranceSq)
        {
            sharedAxis = 2; // Z is shared
            sharedValue = v1.Z;
        }
        else
        {
            // Edge is along one axis, determine which
            if (dx < dy && dx < dz)
            {
                sharedAxis = 0;
                sharedValue = v1.X;
            }
            else if (dy < dz)
            {
                sharedAxis = 1;
                sharedValue = v1.Y;
            }
            else
            {
                sharedAxis = 2;
                sharedValue = v1.Z;
            }
        }

        return new QuadEdge(quadA, quadB, sharedAxis, sharedValue);
    }
}
