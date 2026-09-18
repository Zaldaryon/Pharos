using Xunit;
using Zaldaryon.Pharos.Server;
using Zaldaryon.Pharos.Timing;

namespace Zaldaryon.Pharos.Tests.Server;

/// <summary>
/// Tests for parallel worldgen parity harness.
/// All tests use synthetic chunk data and are headless-safe.
/// </summary>
public sealed class WorldgenParityTests
{
    // -------------------------------------------------------------------------
    // WorldgenParityResult record tests
    // -------------------------------------------------------------------------

    [Fact]
    public void WorldgenParityResult_Empty_HasNoMismatches()
    {
        WorldgenParityResult result = WorldgenParityResult.Empty;

        Assert.Equal(0, result.TotalChunksCompared);
        Assert.Empty(result.MismatchedChunks);
        Assert.False(result.HasMismatches);
        Assert.Equal(0, result.MatchedChunks);
        Assert.Equal(0.0, result.MismatchRatio);
    }

    [Fact]
    public void WorldgenParityResult_AllMatched_HasNoMismatches()
    {
        WorldgenParityResult result = WorldgenParityResult.AllMatched(100, 50.0);

        Assert.Equal(100, result.TotalChunksCompared);
        Assert.Empty(result.MismatchedChunks);
        Assert.False(result.HasMismatches);
        Assert.Equal(100, result.MatchedChunks);
        Assert.Equal(0.0, result.MismatchRatio);
        Assert.Equal(50.0, result.ComparisonTimeMs);
    }

    [Fact]
    public void WorldgenParityResult_WithMismatches_CalculatesCorrectly()
    {
        var mismatches = new List<ChunkPos> { new(0, 0, 0), new(1, 0, 0) };
        WorldgenParityResult result = WorldgenParityResult.WithMismatches(10, mismatches, 25.0);

        Assert.Equal(10, result.TotalChunksCompared);
        Assert.Equal(2, result.MismatchedChunks.Count);
        Assert.True(result.HasMismatches);
        Assert.Equal(8, result.MatchedChunks);
        Assert.Equal(0.2, result.MismatchRatio, precision: 5);
    }

    [Fact]
    public void WorldgenParityResult_MismatchRatio_ZeroWhenNoChunks()
    {
        WorldgenParityResult result = new(0, [new(0, 0, 0)], 0.0);

        Assert.Equal(0.0, result.MismatchRatio);
    }

    // -------------------------------------------------------------------------
    // WorldgenParityHarness basic tests
    // -------------------------------------------------------------------------

    [Fact]
    public void WorldgenParityHarness_InitialState_Empty()
    {
        WorldgenParityHarness harness = new();

        Assert.Equal(0, harness.SerialChunkCount);
        Assert.Equal(0, harness.ParallelChunkCount);
    }

    [Fact]
    public void WorldgenParityHarness_AddSerialChunk_IncreasesCount()
    {
        WorldgenParityHarness harness = new();
        harness.AddSerialChunk(new ChunkPos(0, 0, 0), new int[32768]);

        Assert.Equal(1, harness.SerialChunkCount);
        Assert.Equal(0, harness.ParallelChunkCount);
    }

    [Fact]
    public void WorldgenParityHarness_AddParallelChunk_IncreasesCount()
    {
        WorldgenParityHarness harness = new();
        harness.AddParallelChunk(new ChunkPos(0, 0, 0), new int[32768]);

        Assert.Equal(0, harness.SerialChunkCount);
        Assert.Equal(1, harness.ParallelChunkCount);
    }

    [Fact]
    public void WorldgenParityHarness_Clear_ResetsAll()
    {
        WorldgenParityHarness harness = new();
        harness.AddSerialChunk(new ChunkPos(0, 0, 0), new int[100]);
        harness.AddParallelChunk(new ChunkPos(0, 0, 0), new int[100]);

        harness.Clear();

        Assert.Equal(0, harness.SerialChunkCount);
        Assert.Equal(0, harness.ParallelChunkCount);
    }

    // -------------------------------------------------------------------------
    // CompareWorldgen tests - matching data
    // -------------------------------------------------------------------------

    [Fact]
    public void CompareWorldgen_IdenticalData_NoMismatches()
    {
        WorldgenParityHarness harness = new();
        int[] data = new int[] { 1, 2, 3, 4, 5 };
        ChunkPos pos = new(0, 0, 0);

        harness.AddSerialChunk(pos, data);
        harness.AddParallelChunk(pos, (int[])data.Clone());

        WorldgenParityResult result = harness.CompareWorldgen();

        Assert.Equal(1, result.TotalChunksCompared);
        Assert.False(result.HasMismatches);
        Assert.Equal(0, result.MismatchedChunks.Count);
    }

    [Fact]
    public void CompareWorldgen_MultipleIdenticalChunks_NoMismatches()
    {
        WorldgenParityHarness harness = new();

        for (int x = 0; x < 3; x++)
        {
            for (int z = 0; z < 3; z++)
            {
                ChunkPos pos = new(x, 0, z);
                int[] data = new int[100];
                Array.Fill(data, x * 10 + z);
                harness.AddSerialChunk(pos, data);
                harness.AddParallelChunk(pos, (int[])data.Clone());
            }
        }

        WorldgenParityResult result = harness.CompareWorldgen();

        Assert.Equal(9, result.TotalChunksCompared);
        Assert.False(result.HasMismatches);
    }

    // -------------------------------------------------------------------------
    // CompareWorldgen tests - mismatched data
    // -------------------------------------------------------------------------

    [Fact]
    public void CompareWorldgen_DifferentData_DetectsMismatch()
    {
        WorldgenParityHarness harness = new();
        ChunkPos pos = new(0, 0, 0);

        harness.AddSerialChunk(pos, new int[] { 1, 2, 3 });
        harness.AddParallelChunk(pos, new int[] { 1, 2, 99 }); // Different!

        WorldgenParityResult result = harness.CompareWorldgen();

        Assert.Equal(1, result.TotalChunksCompared);
        Assert.True(result.HasMismatches);
        Assert.Single(result.MismatchedChunks);
        Assert.Equal(pos, result.MismatchedChunks[0]);
    }

    [Fact]
    public void CompareWorldgen_DifferentLengths_DetectsMismatch()
    {
        WorldgenParityHarness harness = new();
        ChunkPos pos = new(0, 0, 0);

        harness.AddSerialChunk(pos, new int[] { 1, 2, 3 });
        harness.AddParallelChunk(pos, new int[] { 1, 2 }); // Different length

        WorldgenParityResult result = harness.CompareWorldgen();

        Assert.True(result.HasMismatches);
    }

    [Fact]
    public void CompareWorldgen_PartialMismatches_ReportsCorrectChunks()
    {
        WorldgenParityHarness harness = new();
        
        // Matching chunk
        ChunkPos match1 = new(0, 0, 0);
        harness.AddSerialChunk(match1, new int[] { 1, 2, 3 });
        harness.AddParallelChunk(match1, new int[] { 1, 2, 3 });

        // Mismatched chunk
        ChunkPos mismatch = new(1, 0, 0);
        harness.AddSerialChunk(mismatch, new int[] { 1, 2, 3 });
        harness.AddParallelChunk(mismatch, new int[] { 9, 9, 9 });

        // Another matching chunk
        ChunkPos match2 = new(2, 0, 0);
        harness.AddSerialChunk(match2, new int[] { 4, 5, 6 });
        harness.AddParallelChunk(match2, new int[] { 4, 5, 6 });

        WorldgenParityResult result = harness.CompareWorldgen();

        Assert.Equal(3, result.TotalChunksCompared);
        Assert.Equal(1, result.MismatchedChunks.Count);
        Assert.Contains(mismatch, result.MismatchedChunks);
        Assert.Equal(2, result.MatchedChunks);
    }

    // -------------------------------------------------------------------------
    // CompareWorldgen tests - edge cases
    // -------------------------------------------------------------------------

    [Fact]
    public void CompareWorldgen_NoCommonChunks_ReturnsEmptyResult()
    {
        WorldgenParityHarness harness = new();
        
        harness.AddSerialChunk(new ChunkPos(0, 0, 0), new int[] { 1, 2, 3 });
        harness.AddParallelChunk(new ChunkPos(99, 99, 99), new int[] { 1, 2, 3 });

        WorldgenParityResult result = harness.CompareWorldgen();

        Assert.Equal(0, result.TotalChunksCompared);
        Assert.False(result.HasMismatches);
    }

    [Fact]
    public void CompareWorldgen_EmptyHarness_ReturnsEmptyResult()
    {
        WorldgenParityHarness harness = new();

        WorldgenParityResult result = harness.CompareWorldgen();

        Assert.Equal(0, result.TotalChunksCompared);
        Assert.False(result.HasMismatches);
    }

    [Fact]
    public void CompareWorldgen_RecordsComparisonTime()
    {
        WorldgenParityHarness harness = new();
        harness.AddSerialChunk(new ChunkPos(0, 0, 0), new int[1000]);
        harness.AddParallelChunk(new ChunkPos(0, 0, 0), new int[1000]);

        WorldgenParityResult result = harness.CompareWorldgen();

        Assert.True(result.ComparisonTimeMs >= 0);
    }

    // -------------------------------------------------------------------------
    // Synthetic data generation tests
    // -------------------------------------------------------------------------

    [Fact]
    public void GenerateSyntheticIdenticalData_CreatesMatchingData()
    {
        WorldgenParityHarness harness = new();

        harness.GenerateSyntheticIdenticalData(chunkRadius: 1, seed: 42);

        WorldgenParityResult result = harness.CompareWorldgen();

        Assert.True(harness.SerialChunkCount > 0);
        Assert.Equal(harness.SerialChunkCount, harness.ParallelChunkCount);
        Assert.False(result.HasMismatches);
    }

    [Fact]
    public void GenerateSyntheticMismatchedData_CreatesMismatches()
    {
        WorldgenParityHarness harness = new();
        var mismatchPositions = new List<ChunkPos> { new(0, 0, 0), new(1, 0, 1) };

        harness.GenerateSyntheticMismatchedData(chunkRadius: 1, mismatchPositions, seed: 42);

        WorldgenParityResult result = harness.CompareWorldgen();

        Assert.True(result.HasMismatches);
        Assert.Equal(2, result.MismatchedChunks.Count);
        Assert.Contains(new ChunkPos(0, 0, 0), result.MismatchedChunks);
        Assert.Contains(new ChunkPos(1, 0, 1), result.MismatchedChunks);
    }

    [Fact]
    public void GenerateSyntheticIdenticalData_DeterministicWithSameSeed()
    {
        WorldgenParityHarness harness1 = new();
        WorldgenParityHarness harness2 = new();

        harness1.GenerateSyntheticIdenticalData(chunkRadius: 1, seed: 12345);
        harness2.GenerateSyntheticIdenticalData(chunkRadius: 1, seed: 12345);

        Assert.Equal(harness1.SerialChunkCount, harness2.SerialChunkCount);
    }

    // -------------------------------------------------------------------------
    // GetBlockDifferences tests
    // -------------------------------------------------------------------------

    [Fact]
    public void GetBlockDifferences_IdenticalChunks_ReturnsEmpty()
    {
        WorldgenParityHarness harness = new();
        ChunkPos pos = new(0, 0, 0);
        int[] data = new int[] { 1, 2, 3, 4, 5 };

        harness.AddSerialChunk(pos, data);
        harness.AddParallelChunk(pos, (int[])data.Clone());

        var diffs = harness.GetBlockDifferences(pos);

        Assert.Empty(diffs);
    }

    [Fact]
    public void GetBlockDifferences_DifferentBlocks_ReturnsDifferences()
    {
        WorldgenParityHarness harness = new();
        ChunkPos pos = new(0, 0, 0);

        harness.AddSerialChunk(pos, new int[] { 1, 2, 3, 4, 5 });
        harness.AddParallelChunk(pos, new int[] { 1, 99, 3, 4, 88 });

        var diffs = harness.GetBlockDifferences(pos);

        Assert.Equal(2, diffs.Count);
        Assert.Contains(diffs, d => d.Index == 1 && d.SerialValue == 2 && d.ParallelValue == 99);
        Assert.Contains(diffs, d => d.Index == 4 && d.SerialValue == 5 && d.ParallelValue == 88);
    }

    [Fact]
    public void GetBlockDifferences_NonExistentChunk_ReturnsEmpty()
    {
        WorldgenParityHarness harness = new();

        var diffs = harness.GetBlockDifferences(new ChunkPos(99, 99, 99));

        Assert.Empty(diffs);
    }

    // -------------------------------------------------------------------------
    // CompareWorldgen with parameters tests
    // -------------------------------------------------------------------------

    [Fact]
    public void CompareWorldgen_WithParameters_UsesPreRegisteredData()
    {
        WorldgenParityHarness harness = new();
        ChunkPos pos = new(0, 0, 0);
        
        harness.AddSerialChunk(pos, new int[] { 1, 2, 3 });
        harness.AddParallelChunk(pos, new int[] { 1, 2, 3 });

        // Parameters are recorded but comparison uses pre-registered data
        WorldgenParityResult result = harness.CompareWorldgen(
            seed: 12345,
            serialWorkerCount: 1,
            parallelWorkerCount: 4,
            chunkRadius: 2);

        Assert.Equal(1, result.TotalChunksCompared);
        Assert.False(result.HasMismatches);
    }
}
