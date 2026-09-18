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
}
