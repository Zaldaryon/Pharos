using Zaldaryon.Pharos.Timing;

namespace Zaldaryon.Pharos.Server;

/// <summary>
/// Result of comparing world generation output between two configurations (e.g., serial vs parallel).
/// Contains mismatch details and timing information.
/// </summary>
/// <param name="TotalChunksCompared">Total number of chunks compared in both datasets.</param>
/// <param name="MismatchedChunks">Chunk positions where block data differed between the two datasets.</param>
/// <param name="ComparisonTimeMs">Time taken to perform the comparison in milliseconds.</param>
public sealed record WorldgenParityResult(
    int TotalChunksCompared,
    IReadOnlyList<ChunkPos> MismatchedChunks,
    double ComparisonTimeMs)
{
    /// <summary>
    /// True if any chunks had mismatched block data between the two configurations.
    /// </summary>
    public bool HasMismatches => MismatchedChunks.Count > 0;

    /// <summary>
    /// Fraction of compared chunks that had mismatches (0.0 to 1.0).
    /// </summary>
    public double MismatchRatio => TotalChunksCompared > 0
        ? (double)MismatchedChunks.Count / TotalChunksCompared
        : 0.0;

    /// <summary>
    /// Number of chunks with matching block data.
    /// </summary>
    public int MatchedChunks => TotalChunksCompared - MismatchedChunks.Count;

    /// <summary>
    /// Empty result with no chunks compared.
    /// </summary>
    public static readonly WorldgenParityResult Empty = new(0, [], 0.0);

    /// <summary>
    /// Creates a result indicating all chunks matched.
    /// </summary>
    public static WorldgenParityResult AllMatched(int chunkCount, double comparisonTimeMs) =>
        new(chunkCount, [], comparisonTimeMs);

    /// <summary>
    /// Creates a result with specific mismatched chunks.
    /// </summary>
    public static WorldgenParityResult WithMismatches(
        int totalChunks,
        IReadOnlyList<ChunkPos> mismatches,
        double comparisonTimeMs) =>
        new(totalChunks, mismatches, comparisonTimeMs);
}
