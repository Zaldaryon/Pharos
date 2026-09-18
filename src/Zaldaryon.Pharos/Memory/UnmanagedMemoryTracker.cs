namespace Zaldaryon.Pharos.Memory;

/// <summary>
/// Tracks unmanaged memory growth using GC.GetTotalMemory.
/// Call StartTracking to record a baseline, then GetGrowthBytes to measure growth since baseline.
/// </summary>
public sealed class UnmanagedMemoryTracker
{
    private long _baselineBytes;
    private bool _isTracking;

    /// <summary>
    /// True if tracking has been started with a valid baseline.
    /// </summary>
    public bool IsTracking => _isTracking;

    /// <summary>
    /// The baseline memory value recorded when tracking started.
    /// </summary>
    public long BaselineBytes => _baselineBytes;

    /// <summary>
    /// Records the current GC total memory as the baseline for growth measurement.
    /// </summary>
    /// <param name="forceFullGC">If true, forces a full garbage collection before taking baseline (slower but more accurate).</param>
    public void StartTracking(bool forceFullGC = false)
    {
        _baselineBytes = GC.GetTotalMemory(forceFullGC);
        _isTracking = true;
    }

    /// <summary>
    /// Returns the memory growth in bytes since StartTracking was called.
    /// Returns 0 if tracking has not been started.
    /// </summary>
    /// <param name="forceFullGC">If true, forces a full garbage collection before measurement (slower but more accurate).</param>
    /// <returns>Memory growth in bytes (positive = growth, negative = shrink, zero = no tracking).</returns>
    public long GetGrowthBytes(bool forceFullGC = false)
    {
        if (!_isTracking)
        {
            return 0;
        }

        long currentBytes = GC.GetTotalMemory(forceFullGC);
        return currentBytes - _baselineBytes;
    }

    /// <summary>
    /// Returns the current GC total memory snapshot in bytes.
    /// </summary>
    /// <param name="forceFullGC">If true, forces a full garbage collection before measurement.</param>
    /// <returns>Current total memory bytes.</returns>
    public long GetCurrentBytes(bool forceFullGC = false)
    {
        return GC.GetTotalMemory(forceFullGC);
    }

    /// <summary>
    /// Resets tracking state and clears the baseline.
    /// </summary>
    public void Reset()
    {
        _baselineBytes = 0;
        _isTracking = false;
    }

    /// <summary>
    /// Creates a new tracker and immediately starts tracking with the current memory as baseline.
    /// </summary>
    /// <param name="forceFullGC">If true, forces a full garbage collection before taking baseline.</param>
    /// <returns>A new tracker instance with tracking already started.</returns>
    public static UnmanagedMemoryTracker StartNew(bool forceFullGC = false)
    {
        UnmanagedMemoryTracker tracker = new();
        tracker.StartTracking(forceFullGC);
        return tracker;
    }
}
