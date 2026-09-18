using System.Collections.Concurrent;
using System.Diagnostics;
using Zaldaryon.Pharos.Timing;

namespace Zaldaryon.Pharos.Server;

/// <summary>
/// Tracks chunk IO timing for observability of parallel vs serial load performance.
/// Thread-safe for concurrent chunk load recording.
/// </summary>
public sealed class ChunkIoTimingTracker
{
    private readonly ConcurrentDictionary<ChunkPos, long> _chunkTimings = new();
    private readonly Stopwatch _totalStopwatch = new();
    private volatile bool _isTracking;
    private long _totalElapsedMs;
    private int _chunksLoaded;

    /// <summary>
    /// Gets whether tracking is currently active.
    /// </summary>
    public bool IsTracking => _isTracking;

    /// <summary>
    /// Gets the number of chunks recorded while tracking is/was active.
    /// </summary>
    public int ChunksLoaded => _chunksLoaded;

    /// <summary>
    /// Gets the total elapsed time in milliseconds since tracking started.
    /// Returns 0 if tracking has not been started.
    /// </summary>
    public long TotalElapsedMs => _isTracking ? _totalStopwatch.ElapsedMilliseconds : _totalElapsedMs;

    /// <summary>
    /// Starts tracking chunk IO timing. Resets any previous data.
    /// </summary>
    public void StartTracking()
    {
        Reset();
        _isTracking = true;
        _totalStopwatch.Start();
    }

    /// <summary>
    /// Stops tracking and captures the final elapsed time.
    /// </summary>
    public void StopTracking()
    {
        if (!_isTracking) return;

        _totalStopwatch.Stop();
        _totalElapsedMs = _totalStopwatch.ElapsedMilliseconds;
        _isTracking = false;
    }

    /// <summary>
    /// Records a chunk load event with its elapsed time.
    /// </summary>
    /// <param name="pos">The chunk position that was loaded.</param>
    /// <param name="elapsedMs">Time taken to load the chunk in milliseconds.</param>
    public void RecordChunkLoaded(ChunkPos pos, long elapsedMs)
    {
        if (!_isTracking) return;

        _chunkTimings[pos] = elapsedMs;
        Interlocked.Increment(ref _chunksLoaded);
    }

    /// <summary>
    /// Records a chunk load event, timing it automatically using a Stopwatch.
    /// Call the returned action when the load completes.
    /// </summary>
    /// <param name="pos">The chunk position being loaded.</param>
    /// <returns>An action to call when the chunk load completes.</returns>
    public Action BeginChunkLoad(ChunkPos pos)
    {
        if (!_isTracking)
        {
            return static () => { };
        }

        var sw = Stopwatch.StartNew();
        return () =>
        {
            sw.Stop();
            RecordChunkLoaded(pos, sw.ElapsedMilliseconds);
        };
    }

    /// <summary>
    /// Gets the timing report for the current tracking session.
    /// </summary>
    public ChunkIoTimingReport GetReport()
    {
        return new ChunkIoTimingReport(
            _chunksLoaded,
            _isTracking ? _totalStopwatch.ElapsedMilliseconds : _totalElapsedMs);
    }

    /// <summary>
    /// Gets the timing report with a baseline comparison.
    /// </summary>
    /// <param name="baselineReport">The baseline report for comparison (e.g., serial load).</param>
    public ChunkIoTimingReport GetReport(ChunkIoTimingReport baselineReport)
    {
        ArgumentNullException.ThrowIfNull(baselineReport);

        return new ChunkIoTimingReport(
            _chunksLoaded,
            _isTracking ? _totalStopwatch.ElapsedMilliseconds : _totalElapsedMs,
            baselineReport.TotalTimeMs);
    }

    /// <summary>
    /// Gets individual timing data for all recorded chunks.
    /// </summary>
    public IReadOnlyDictionary<ChunkPos, long> GetChunkTimings()
    {
        return new Dictionary<ChunkPos, long>(_chunkTimings);
    }

    /// <summary>
    /// Gets the load time for a specific chunk, or null if not recorded.
    /// </summary>
    public long? GetChunkTiming(ChunkPos pos)
    {
        return _chunkTimings.TryGetValue(pos, out long timing) ? timing : null;
    }

    /// <summary>
    /// Gets the sum of individual chunk timings (may differ from total elapsed time
    /// due to parallel loading overlap).
    /// </summary>
    public long SumOfChunkTimings
    {
        get
        {
            long sum = 0;
            foreach (var kvp in _chunkTimings)
            {
                sum += kvp.Value;
            }
            return sum;
        }
    }

    /// <summary>
    /// Gets the parallelism factor (sum of individual times / wall clock time).
    /// Values > 1.0 indicate parallel execution.
    /// </summary>
    public double ParallelismFactor
    {
        get
        {
            long total = _isTracking ? _totalStopwatch.ElapsedMilliseconds : _totalElapsedMs;
            return total > 0 ? (double)SumOfChunkTimings / total : 1.0;
        }
    }

    /// <summary>
    /// Resets the tracker, clearing all data.
    /// </summary>
    public void Reset()
    {
        _isTracking = false;
        _chunkTimings.Clear();
        _totalStopwatch.Reset();
        _totalElapsedMs = 0;
        _chunksLoaded = 0;
    }

    /// <summary>
    /// Creates a synthetic tracker pre-populated with timing data for testing.
    /// </summary>
    /// <param name="chunkTimings">Chunk position to load time mappings.</param>
    /// <param name="totalTimeMs">Total wall clock time for the load operation.</param>
    public static ChunkIoTimingTracker CreateSynthetic(
        IEnumerable<(ChunkPos pos, long elapsedMs)> chunkTimings,
        long totalTimeMs)
    {
        ArgumentNullException.ThrowIfNull(chunkTimings);

        var tracker = new ChunkIoTimingTracker();
        tracker._totalElapsedMs = totalTimeMs;

        foreach (var (pos, elapsed) in chunkTimings)
        {
            tracker._chunkTimings[pos] = elapsed;
            tracker._chunksLoaded++;
        }

        return tracker;
    }

    /// <summary>
    /// Creates a synthetic tracker simulating serial chunk loading
    /// where each chunk is loaded sequentially.
    /// </summary>
    /// <param name="chunkCount">Number of chunks to simulate.</param>
    /// <param name="avgTimePerChunkMs">Average load time per chunk.</param>
    public static ChunkIoTimingTracker CreateSyntheticSerial(int chunkCount, long avgTimePerChunkMs)
    {
        var timings = new List<(ChunkPos, long)>();
        for (int i = 0; i < chunkCount; i++)
        {
            timings.Add((new ChunkPos(i, 0, 0), avgTimePerChunkMs));
        }

        // Serial: wall clock time = sum of individual times
        long totalTime = chunkCount * avgTimePerChunkMs;
        return CreateSynthetic(timings, totalTime);
    }

    /// <summary>
    /// Creates a synthetic tracker simulating parallel chunk loading
    /// with the specified parallelism level.
    /// </summary>
    /// <param name="chunkCount">Number of chunks to simulate.</param>
    /// <param name="avgTimePerChunkMs">Average load time per chunk.</param>
    /// <param name="parallelism">Number of parallel workers (speedup factor).</param>
    public static ChunkIoTimingTracker CreateSyntheticParallel(
        int chunkCount,
        long avgTimePerChunkMs,
        int parallelism)
    {
        if (parallelism < 1) parallelism = 1;

        var timings = new List<(ChunkPos, long)>();
        for (int i = 0; i < chunkCount; i++)
        {
            timings.Add((new ChunkPos(i, 0, 0), avgTimePerChunkMs));
        }

        // Parallel: wall clock time = sum / parallelism (idealized)
        long totalTime = (chunkCount * avgTimePerChunkMs) / parallelism;
        return CreateSynthetic(timings, Math.Max(1, totalTime));
    }
}
