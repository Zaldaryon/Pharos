using Zaldaryon.Pharos.Timing;

namespace Zaldaryon.Pharos.Culling;

/// <summary>
/// Pure-math oracle for comparing SIMD frustum culling decisions against a naive
/// reference plane-box intersection algorithm. Used to assert zero false negatives.
/// </summary>
public static class FrustumOracleComparator
{
    /// <summary>
    /// Chunk size in blocks. Chunks are 32x32x32 blocks.
    /// </summary>
    public const int ChunkSize = 32;

    /// <summary>
    /// Tests whether an AABB is inside or intersects all frustum planes using
    /// the P-vertex/N-vertex algorithm. Returns true if the box is at least
    /// partially inside the frustum.
    /// </summary>
    /// <param name="planes">Frustum planes (typically 6).</param>
    /// <param name="minX">AABB minimum X coordinate.</param>
    /// <param name="minY">AABB minimum Y coordinate.</param>
    /// <param name="minZ">AABB minimum Z coordinate.</param>
    /// <param name="maxX">AABB maximum X coordinate.</param>
    /// <param name="maxY">AABB maximum Y coordinate.</param>
    /// <param name="maxZ">AABB maximum Z coordinate.</param>
    /// <returns>True if the AABB is inside or intersects the frustum.</returns>
    public static bool IsBoxInFrustum(
        IReadOnlyList<FrustumPlane> planes,
        double minX, double minY, double minZ,
        double maxX, double maxY, double maxZ)
    {
        ArgumentNullException.ThrowIfNull(planes);

        foreach (FrustumPlane plane in planes)
        {
            // Compute the P-vertex: the corner of the AABB that is furthest
            // along the plane normal direction
            double px = plane.NormalX >= 0 ? maxX : minX;
            double py = plane.NormalY >= 0 ? maxY : minY;
            double pz = plane.NormalZ >= 0 ? maxZ : minZ;

            // If the P-vertex is behind the plane, the entire box is outside
            double dist = plane.DistanceOf(px, py, pz);
            if (dist < 0)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Tests whether a chunk is inside or intersects all frustum planes.
    /// Converts chunk coordinates to block AABB (ChunkPos * 32 for min, +32 for max).
    /// </summary>
    public static bool IsChunkInFrustum(IReadOnlyList<FrustumPlane> planes, ChunkPos chunk)
    {
        double minX = chunk.X * ChunkSize;
        double minY = chunk.Y * ChunkSize;
        double minZ = chunk.Z * ChunkSize;
        double maxX = minX + ChunkSize;
        double maxY = minY + ChunkSize;
        double maxZ = minZ + ChunkSize;

        return IsBoxInFrustum(planes, minX, minY, minZ, maxX, maxY, maxZ);
    }

    /// <summary>
    /// Compares the oracle frustum test against a CullingSnapshot for a set of chunks.
    /// Returns a list of mismatches where the SIMD culler marked a chunk as culled
    /// but the oracle determined it should be visible (false negatives).
    /// </summary>
    /// <param name="snapshot">The culling snapshot from SIMD/runtime culling.</param>
    /// <param name="allChunks">All chunks to test (typically VisibleChunks + CulledChunks).</param>
    /// <returns>List of oracle mismatches (false negatives).</returns>
    public static IReadOnlyList<OracleMismatch> Compare(
        CullingSnapshot snapshot,
        IEnumerable<ChunkPos> allChunks)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(allChunks);

        if (snapshot.FrustumPlanes.Count == 0)
        {
            // No frustum planes available, cannot compare
            return [];
        }

        HashSet<ChunkPos> visibleSet = [.. snapshot.VisibleChunks];
        List<OracleMismatch> mismatches = [];

        foreach (ChunkPos chunk in allChunks)
        {
            bool oracleVisible = IsChunkInFrustum(snapshot.FrustumPlanes, chunk);
            bool simdVisible = visibleSet.Contains(chunk);

            // False negative: oracle says visible, SIMD says culled
            // This is a bug in the SIMD implementation (missed a visible chunk)
            if (oracleVisible && !simdVisible)
            {
                mismatches.Add(new OracleMismatch(chunk, OracleMismatchType.FalseNegative));
            }
            // False positive: oracle says culled, SIMD says visible
            // This is acceptable (conservative, wastes GPU but not incorrect)
            // We track it for informational purposes but it is not a failure
            else if (!oracleVisible && simdVisible)
            {
                mismatches.Add(new OracleMismatch(chunk, OracleMismatchType.FalsePositive));
            }
        }

        return mismatches;
    }

    /// <summary>
    /// Filters mismatches to only false negatives (visible chunks incorrectly culled).
    /// These are the critical bugs that cause missing geometry.
    /// </summary>
    public static IReadOnlyList<OracleMismatch> FalseNegatives(IReadOnlyList<OracleMismatch> mismatches)
    {
        ArgumentNullException.ThrowIfNull(mismatches);
        return [.. mismatches.Where(m => m.Type == OracleMismatchType.FalseNegative)];
    }
}

/// <summary>
/// Represents a mismatch between oracle and SIMD frustum culling decisions.
/// </summary>
public readonly record struct OracleMismatch(ChunkPos Chunk, OracleMismatchType Type);

/// <summary>
/// Type of mismatch between oracle and SIMD culling.
/// </summary>
public enum OracleMismatchType
{
    /// <summary>
    /// Oracle says visible, SIMD says culled. This is a bug (missing geometry).
    /// </summary>
    FalseNegative,

    /// <summary>
    /// Oracle says culled, SIMD says visible. This is acceptable but wastes GPU.
    /// </summary>
    FalsePositive
}
