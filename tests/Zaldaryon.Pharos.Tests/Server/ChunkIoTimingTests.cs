using System.Globalization;
using Xunit;
using Zaldaryon.Pharos.Assertions;
using Zaldaryon.Pharos.Server;
using Zaldaryon.Pharos.Timing;

namespace Zaldaryon.Pharos.Tests.Server;

/// <summary>
/// Tests for chunk IO pool timing observability.
/// All tests use synthetic timing data and are headless-safe.
/// </summary>
public sealed class ChunkIoTimingTests
{
    // -------------------------------------------------------------------------
    // ChunkIoTimingReport record tests
    // -------------------------------------------------------------------------

    [Fact]
    public void ChunkIoTimingReport_Empty_HasExpectedDefaults()
    {
        ChunkIoTimingReport report = ChunkIoTimingReport.Empty;

        Assert.Equal(0, report.ChunksLoaded);
        Assert.Equal(0, report.TotalTimeMs);
        Assert.Equal(0.0, report.AverageTimePerChunkMs);
        Assert.Equal(1.0, report.SpeedupFactor);
        Assert.False(report.HasData);
        Assert.False(report.HasBaseline);
    }

    [Fact]
    public void ChunkIoTimingReport_Create_SetsValues()
    {
        ChunkIoTimingReport report = ChunkIoTimingReport.Create(10, 500);

        Assert.Equal(10, report.ChunksLoaded);
        Assert.Equal(500, report.TotalTimeMs);
        Assert.Equal(50.0, report.AverageTimePerChunkMs);
        Assert.True(report.HasData);
        Assert.False(report.HasBaseline);
    }

    [Fact]
    public void ChunkIoTimingReport_WithBaseline_CalculatesSpeedup()
    {
        // Parallel: 10 chunks in 100ms (baseline serial: 400ms)
        ChunkIoTimingReport report = ChunkIoTimingReport.WithBaseline(10, 100, 400);

        Assert.Equal(4.0, report.SpeedupFactor);
        Assert.True(report.HasBaseline);
        Assert.Equal(300, report.TimeSavedMs);
        Assert.Equal(75.0, report.ImprovementPercent, precision: 1);
    }

    [Fact]
    public void ChunkIoTimingReport_AddBaseline_CreatesNewReport()
    {
        ChunkIoTimingReport original = ChunkIoTimingReport.Create(10, 100);
        ChunkIoTimingReport withBaseline = original.AddBaseline(400);

        Assert.False(original.HasBaseline);
        Assert.True(withBaseline.HasBaseline);
        Assert.Equal(4.0, withBaseline.SpeedupFactor);
    }

    [Fact]
    public void ChunkIoTimingReport_CompareToBaseline_UsesOtherReportTime()
    {
        ChunkIoTimingReport serial = ChunkIoTimingReport.Create(10, 400);
        ChunkIoTimingReport parallel = ChunkIoTimingReport.Create(10, 100);

        ChunkIoTimingReport compared = parallel.CompareToBaseline(serial);

        Assert.Equal(4.0, compared.SpeedupFactor);
        Assert.Equal(300, compared.TimeSavedMs);
    }

    [Fact]
    public void ChunkIoTimingReport_AverageTime_ZeroWhenNoChunks()
    {
        ChunkIoTimingReport report = new(0, 100);

        Assert.Equal(0.0, report.AverageTimePerChunkMs);
    }

    [Fact]
    public void ChunkIoTimingReport_SpeedupFactor_OneWhenNoBaseline()
    {
        ChunkIoTimingReport report = ChunkIoTimingReport.Create(10, 100);

        Assert.Equal(1.0, report.SpeedupFactor);
    }

    [Fact]
    public void ChunkIoTimingReport_SpeedupFactor_OneWhenZeroTime()
    {
        ChunkIoTimingReport report = ChunkIoTimingReport.WithBaseline(10, 0, 400);

        Assert.Equal(1.0, report.SpeedupFactor);
    }

    [Fact]
    public void ChunkIoTimingReport_ImprovementPercent_ZeroWhenNoBaseline()
    {
        ChunkIoTimingReport report = ChunkIoTimingReport.Create(10, 100);

        Assert.Equal(0.0, report.ImprovementPercent);
    }

    [Fact]
    public void ChunkIoTimingReport_NegativeImprovement_WhenSlower()
    {
        // Slower than baseline (100ms baseline, 200ms actual)
        ChunkIoTimingReport report = ChunkIoTimingReport.WithBaseline(10, 200, 100);

        Assert.True(report.ImprovementPercent < 0);
        Assert.True(report.TimeSavedMs < 0);
        Assert.True(report.SpeedupFactor < 1.0);
    }

    [Fact]
    public void ChunkIoTimingReport_ToString_FormatsCorrectly()
    {
        ChunkIoTimingReport report = ChunkIoTimingReport.WithBaseline(10, 100, 400);
        CultureInfo previousCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("pt-BR");
            string str = report.ToString();

            Assert.Contains("Chunks=10", str);
            Assert.Contains("Total=100ms", str);
            Assert.Contains("Avg=10.00ms/chunk", str);
            Assert.Contains("Speedup=4.00x", str);
        }
        finally
        {
            CultureInfo.CurrentCulture = previousCulture;
        }
    }

    // -------------------------------------------------------------------------
    // ChunkIoTimingTracker basic tests
    // -------------------------------------------------------------------------

    [Fact]
    public void ChunkIoTimingTracker_InitialState_NotTracking()
    {
        ChunkIoTimingTracker tracker = new();

        Assert.False(tracker.IsTracking);
        Assert.Equal(0, tracker.ChunksLoaded);
        Assert.Equal(0, tracker.TotalElapsedMs);
    }

    [Fact]
    public void ChunkIoTimingTracker_StartTracking_SetsState()
    {
        ChunkIoTimingTracker tracker = new();

        tracker.StartTracking();

        Assert.True(tracker.IsTracking);
    }

    [Fact]
    public void ChunkIoTimingTracker_StopTracking_ClearsState()
    {
        ChunkIoTimingTracker tracker = new();
        tracker.StartTracking();

        tracker.StopTracking();

        Assert.False(tracker.IsTracking);
    }

    [Fact]
    public void ChunkIoTimingTracker_RecordChunkLoaded_IgnoredWhenNotTracking()
    {
        ChunkIoTimingTracker tracker = new();

        tracker.RecordChunkLoaded(new ChunkPos(0, 0, 0), 100);

        Assert.Equal(0, tracker.ChunksLoaded);
    }

    [Fact]
    public void ChunkIoTimingTracker_RecordChunkLoaded_RecordsWhenTracking()
    {
        ChunkIoTimingTracker tracker = new();
        tracker.StartTracking();

        tracker.RecordChunkLoaded(new ChunkPos(0, 0, 0), 100);
        tracker.RecordChunkLoaded(new ChunkPos(1, 0, 0), 150);

        Assert.Equal(2, tracker.ChunksLoaded);
    }

    [Fact]
    public void ChunkIoTimingTracker_GetChunkTiming_ReturnsRecordedTime()
    {
        ChunkIoTimingTracker tracker = new();
        tracker.StartTracking();
        ChunkPos pos = new(0, 0, 0);

        tracker.RecordChunkLoaded(pos, 100);

        Assert.Equal(100, tracker.GetChunkTiming(pos));
    }

    [Fact]
    public void ChunkIoTimingTracker_GetChunkTiming_ReturnsNullForUnknown()
    {
        ChunkIoTimingTracker tracker = new();
        tracker.StartTracking();

        Assert.Null(tracker.GetChunkTiming(new ChunkPos(99, 99, 99)));
    }

    [Fact]
    public void ChunkIoTimingTracker_Reset_ClearsAllData()
    {
        ChunkIoTimingTracker tracker = new();
        tracker.StartTracking();
        tracker.RecordChunkLoaded(new ChunkPos(0, 0, 0), 100);
        tracker.StopTracking();

        tracker.Reset();

        Assert.False(tracker.IsTracking);
        Assert.Equal(0, tracker.ChunksLoaded);
        Assert.Equal(0, tracker.TotalElapsedMs);
    }

    [Fact]
    public void ChunkIoTimingTracker_GetReport_ReturnsCurrentState()
    {
        ChunkIoTimingTracker tracker = new();
        tracker.StartTracking();
        tracker.RecordChunkLoaded(new ChunkPos(0, 0, 0), 100);
        tracker.StopTracking();

        ChunkIoTimingReport report = tracker.GetReport();

        Assert.Equal(1, report.ChunksLoaded);
        Assert.True(report.HasData);
    }

    [Fact]
    public void ChunkIoTimingTracker_GetReportWithBaseline_AddsComparison()
    {
        ChunkIoTimingTracker tracker = new();
        tracker.StartTracking();
        tracker.RecordChunkLoaded(new ChunkPos(0, 0, 0), 100);
        tracker.StopTracking();

        ChunkIoTimingReport baseline = ChunkIoTimingReport.Create(1, 400);
        ChunkIoTimingReport report = tracker.GetReport(baseline);

        Assert.True(report.HasBaseline);
        Assert.Equal(400, report.BaselineTotalTimeMs);
    }

    [Fact]
    public void ChunkIoTimingTracker_GetChunkTimings_ReturnsAllRecorded()
    {
        ChunkIoTimingTracker tracker = new();
        tracker.StartTracking();
        tracker.RecordChunkLoaded(new ChunkPos(0, 0, 0), 100);
        tracker.RecordChunkLoaded(new ChunkPos(1, 0, 0), 150);

        var timings = tracker.GetChunkTimings();

        Assert.Equal(2, timings.Count);
        Assert.Equal(100, timings[new ChunkPos(0, 0, 0)]);
        Assert.Equal(150, timings[new ChunkPos(1, 0, 0)]);
    }

    [Fact]
    public void ChunkIoTimingTracker_SumOfChunkTimings_CalculatesCorrectly()
    {
        ChunkIoTimingTracker tracker = new();
        tracker.StartTracking();
        tracker.RecordChunkLoaded(new ChunkPos(0, 0, 0), 100);
        tracker.RecordChunkLoaded(new ChunkPos(1, 0, 0), 150);

        Assert.Equal(250, tracker.SumOfChunkTimings);
    }

    // -------------------------------------------------------------------------
    // CreateSynthetic tests
    // -------------------------------------------------------------------------

    [Fact]
    public void CreateSynthetic_CreatesPrePopulatedTracker()
    {
        var tracker = ChunkIoTimingTracker.CreateSynthetic([
            (new ChunkPos(0, 0, 0), 100),
            (new ChunkPos(1, 0, 0), 150)
        ], totalTimeMs: 200);

        Assert.Equal(2, tracker.ChunksLoaded);
        Assert.Equal(200, tracker.TotalElapsedMs);
        Assert.Equal(250, tracker.SumOfChunkTimings);
    }

    [Fact]
    public void CreateSyntheticSerial_SimulatesSequentialLoading()
    {
        var tracker = ChunkIoTimingTracker.CreateSyntheticSerial(10, avgTimePerChunkMs: 50);

        Assert.Equal(10, tracker.ChunksLoaded);
        Assert.Equal(500, tracker.TotalElapsedMs); // 10 * 50 = 500 (serial)
        Assert.Equal(500, tracker.SumOfChunkTimings);
        Assert.Equal(1.0, tracker.ParallelismFactor, precision: 2);
    }

    [Fact]
    public void CreateSyntheticParallel_SimulatesParallelLoading()
    {
        var tracker = ChunkIoTimingTracker.CreateSyntheticParallel(10, avgTimePerChunkMs: 50, parallelism: 4);

        Assert.Equal(10, tracker.ChunksLoaded);
        Assert.Equal(125, tracker.TotalElapsedMs); // 500 / 4 = 125 (parallel)
        Assert.Equal(500, tracker.SumOfChunkTimings);
        Assert.Equal(4.0, tracker.ParallelismFactor, precision: 2);
    }

    // -------------------------------------------------------------------------
    // PharosAssert.ChunkIoSpeedupAtLeast tests
    // -------------------------------------------------------------------------

    [Fact]
    public void ChunkIoSpeedupAtLeast_Passes_WhenSpeedupSufficient()
    {
        ChunkIoTimingReport serial = ChunkIoTimingReport.Create(10, 500);
        ChunkIoTimingReport parallel = ChunkIoTimingReport.Create(10, 250);

        // Should not throw - 2.0x speedup >= 1.4x requirement
        PharosAssert.ChunkIoSpeedupAtLeast(parallel, serial, minSpeedup: 1.4);
    }

    [Fact]
    public void ChunkIoSpeedupAtLeast_Throws_WhenSpeedupInsufficient()
    {
        ChunkIoTimingReport serial = ChunkIoTimingReport.Create(10, 500);
        ChunkIoTimingReport parallel = ChunkIoTimingReport.Create(10, 400); // Only 1.25x speedup

        var ex = Assert.Throws<PharosAssertException>(() =>
            PharosAssert.ChunkIoSpeedupAtLeast(parallel, serial, minSpeedup: 1.4));

        Assert.Contains("1.25", ex.Message);
        Assert.Contains("1.40", ex.Message);
    }

    [Fact]
    public void ChunkIoSpeedupAtLeast_Throws_WhenPoolReportEmpty()
    {
        ChunkIoTimingReport serial = ChunkIoTimingReport.Create(10, 500);
        ChunkIoTimingReport parallel = ChunkIoTimingReport.Empty;

        var ex = Assert.Throws<PharosAssertException>(() =>
            PharosAssert.ChunkIoSpeedupAtLeast(parallel, serial));

        Assert.Contains("Pool report", ex.Message);
    }

    [Fact]
    public void ChunkIoSpeedupAtLeast_Throws_WhenSerialReportEmpty()
    {
        ChunkIoTimingReport serial = ChunkIoTimingReport.Empty;
        ChunkIoTimingReport parallel = ChunkIoTimingReport.Create(10, 250);

        var ex = Assert.Throws<PharosAssertException>(() =>
            PharosAssert.ChunkIoSpeedupAtLeast(parallel, serial));

        Assert.Contains("Serial report", ex.Message);
    }

    [Fact]
    public void ChunkIoSpeedupAtLeast_Throws_WhenSerialTimeZero()
    {
        ChunkIoTimingReport serial = ChunkIoTimingReport.Create(10, 0);
        ChunkIoTimingReport parallel = ChunkIoTimingReport.Create(10, 250);

        var ex = Assert.Throws<PharosAssertException>(() =>
            PharosAssert.ChunkIoSpeedupAtLeast(parallel, serial));

        Assert.Contains("zero total time", ex.Message);
    }

    // -------------------------------------------------------------------------
    // PharosAssert.ChunkIoWithinBudget tests
    // -------------------------------------------------------------------------

    [Fact]
    public void ChunkIoWithinBudget_Passes_WhenWithinBudget()
    {
        ChunkIoTimingReport report = ChunkIoTimingReport.Create(10, 400);

        // Should not throw
        PharosAssert.ChunkIoWithinBudget(report, maxTotalTimeMs: 500);
    }

    [Fact]
    public void ChunkIoWithinBudget_Throws_WhenOverBudget()
    {
        ChunkIoTimingReport report = ChunkIoTimingReport.Create(10, 600);

        var ex = Assert.Throws<PharosAssertException>(() =>
            PharosAssert.ChunkIoWithinBudget(report, maxTotalTimeMs: 500));

        Assert.Contains("exceeded", ex.Message.ToLowerInvariant());
        Assert.Contains("600", ex.Message);
        Assert.Contains("500", ex.Message);
    }

    // -------------------------------------------------------------------------
    // PharosAssert.ChunkIoLoadedAtLeast tests
    // -------------------------------------------------------------------------

    [Fact]
    public void ChunkIoLoadedAtLeast_Passes_WhenAboveMinimum()
    {
        ChunkIoTimingReport report = ChunkIoTimingReport.Create(10, 500);

        // Should not throw
        PharosAssert.ChunkIoLoadedAtLeast(report, minChunks: 5);
    }

    [Fact]
    public void ChunkIoLoadedAtLeast_Throws_WhenBelowMinimum()
    {
        ChunkIoTimingReport report = ChunkIoTimingReport.Create(3, 500);

        var ex = Assert.Throws<PharosAssertException>(() =>
            PharosAssert.ChunkIoLoadedAtLeast(report, minChunks: 5));

        Assert.Contains("3 chunks", ex.Message);
        Assert.Contains("5", ex.Message);
    }

    // -------------------------------------------------------------------------
    // PharosAssert.ChunkIoAverageTimeBelow tests
    // -------------------------------------------------------------------------

    [Fact]
    public void ChunkIoAverageTimeBelow_Passes_WhenBelowThreshold()
    {
        ChunkIoTimingReport report = ChunkIoTimingReport.Create(10, 400); // 40ms/chunk

        // Should not throw
        PharosAssert.ChunkIoAverageTimeBelow(report, maxAvgTimeMs: 50.0);
    }

    [Fact]
    public void ChunkIoAverageTimeBelow_Throws_WhenAboveThreshold()
    {
        ChunkIoTimingReport report = ChunkIoTimingReport.Create(10, 600); // 60ms/chunk

        var ex = Assert.Throws<PharosAssertException>(() =>
            PharosAssert.ChunkIoAverageTimeBelow(report, maxAvgTimeMs: 50.0));

        Assert.Contains("60.00", ex.Message);
        Assert.Contains("50.00", ex.Message);
    }

    [Fact]
    public void ChunkIoAverageTimeBelow_Passes_WhenNoData()
    {
        ChunkIoTimingReport report = ChunkIoTimingReport.Empty;

        // Should not throw - no data to validate
        PharosAssert.ChunkIoAverageTimeBelow(report, maxAvgTimeMs: 50.0);
    }

    // -------------------------------------------------------------------------
    // Thread safety tests
    // -------------------------------------------------------------------------

    [Fact]
    public void ChunkIoTimingTracker_ThreadSafe_ConcurrentRecording()
    {
        ChunkIoTimingTracker tracker = new();
        tracker.StartTracking();
        const int threads = 4;
        const int chunksPerThread = 100;

        Parallel.For(0, threads, threadId =>
        {
            for (int i = 0; i < chunksPerThread; i++)
            {
                ChunkPos pos = new(threadId * 1000 + i, 0, 0);
                tracker.RecordChunkLoaded(pos, 10);
            }
        });

        tracker.StopTracking();

        Assert.Equal(threads * chunksPerThread, tracker.ChunksLoaded);
    }

    // -------------------------------------------------------------------------
    // Integration/scenario tests
    // -------------------------------------------------------------------------

    [Fact]
    public void Scenario_SerialVsParallel_SpeedupDetected()
    {
        // Simulate serial loading
        var serialTracker = ChunkIoTimingTracker.CreateSyntheticSerial(100, avgTimePerChunkMs: 10);

        // Simulate parallel loading with 4x parallelism
        var parallelTracker = ChunkIoTimingTracker.CreateSyntheticParallel(100, avgTimePerChunkMs: 10, parallelism: 4);

        ChunkIoTimingReport serialReport = serialTracker.GetReport();
        ChunkIoTimingReport parallelReport = parallelTracker.GetReport();

        // Verify speedup
        PharosAssert.ChunkIoSpeedupAtLeast(parallelReport, serialReport, minSpeedup: 3.5);
    }

    [Fact]
    public void Scenario_ParallelismFactor_IndicatesParallelExecution()
    {
        var parallelTracker = ChunkIoTimingTracker.CreateSyntheticParallel(10, avgTimePerChunkMs: 100, parallelism: 4);

        // Sum of individual chunk times should be ~4x wall clock time
        Assert.True(parallelTracker.ParallelismFactor >= 3.5);
    }

    // -------------------------------------------------------------------------
    // BeginChunkLoad tests
    // -------------------------------------------------------------------------

    [Fact]
    public void BeginChunkLoad_ReturnsNoOpWhenNotTracking()
    {
        ChunkIoTimingTracker tracker = new();
        ChunkPos pos = new(0, 0, 0);

        Action complete = tracker.BeginChunkLoad(pos);
        complete();

        Assert.Equal(0, tracker.ChunksLoaded);
    }

    [Fact]
    public void BeginChunkLoad_RecordsTimingWhenComplete()
    {
        ChunkIoTimingTracker tracker = new();
        tracker.StartTracking();
        ChunkPos pos = new(0, 0, 0);

        Action complete = tracker.BeginChunkLoad(pos);
        Thread.Sleep(10); // Brief delay for timing
        complete();

        Assert.Equal(1, tracker.ChunksLoaded);
        Assert.NotNull(tracker.GetChunkTiming(pos));
    }

    // -------------------------------------------------------------------------
    // Edge case tests
    // -------------------------------------------------------------------------

    [Fact]
    public void ChunkIoTimingTracker_StartTracking_ResetsExistingData()
    {
        ChunkIoTimingTracker tracker = new();
        tracker.StartTracking();
        tracker.RecordChunkLoaded(new ChunkPos(0, 0, 0), 100);
        tracker.StopTracking();

        tracker.StartTracking(); // Should reset

        Assert.Equal(0, tracker.ChunksLoaded);
    }

    [Fact]
    public void ChunkIoTimingTracker_MultipleStops_SafelyIgnored()
    {
        ChunkIoTimingTracker tracker = new();
        tracker.StartTracking();

        tracker.StopTracking();
        tracker.StopTracking(); // Should not throw
        tracker.StopTracking();

        Assert.False(tracker.IsTracking);
    }
}
