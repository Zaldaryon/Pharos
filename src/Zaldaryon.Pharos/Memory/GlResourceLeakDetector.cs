using Zaldaryon.Pharos.Graphics;

namespace Zaldaryon.Pharos.Memory;

/// <summary>
/// Detects OpenGL resource leaks by tracking resource lifecycle via GlCommandProxy counters.
/// Records a baseline snapshot at test start, then computes leaks as the difference between
/// allocations and deletions at test end.
/// </summary>
public sealed class GlResourceLeakDetector
{
    private GlCommandRecord _baseline = GlCommandRecord.Empty;
    private readonly UnmanagedMemoryTracker _memoryTracker = new();
    private bool _isTracking;

    /// <summary>
    /// True if leak detection has been started with a valid baseline.
    /// </summary>
    public bool IsTracking => _isTracking;

    /// <summary>
    /// The baseline GL command record captured when tracking started.
    /// </summary>
    public GlCommandRecord Baseline => _baseline;

    /// <summary>
    /// Records the current GL resource counts and memory as the baseline.
    /// Call this at the start of a test scenario before exercising code that may leak.
    /// </summary>
    /// <param name="proxy">The GlCommandProxy to capture baseline from.</param>
    /// <param name="forceFullGC">If true, forces a full garbage collection before taking memory baseline.</param>
    public void StartBaseline(GlCommandProxy proxy, bool forceFullGC = false)
    {
        ArgumentNullException.ThrowIfNull(proxy);

        _baseline = proxy.Snapshot();
        _memoryTracker.StartTracking(forceFullGC);
        _isTracking = true;
    }

    /// <summary>
    /// Records the current GL resource counts and memory as the baseline using synthetic data.
    /// Useful for testing the leak detector logic without a real GlCommandProxy.
    /// </summary>
    /// <param name="syntheticBaseline">Synthetic baseline record for testing.</param>
    /// <param name="baselineMemoryBytes">Synthetic baseline memory value for testing.</param>
    public void StartBaseline(GlCommandRecord syntheticBaseline, long baselineMemoryBytes = 0)
    {
        ArgumentNullException.ThrowIfNull(syntheticBaseline);

        _baseline = syntheticBaseline;
        _memoryTracker.StartTracking(false);
        // Override the memory tracker's baseline for synthetic testing
        SetSyntheticMemoryBaseline(baselineMemoryBytes);
        _isTracking = true;
    }

    /// <summary>
    /// Computes a leak report by comparing the current GL resource counts against the baseline.
    /// Leaks are computed as: (current allocations - current deletions) - (baseline allocations - baseline deletions).
    /// </summary>
    /// <param name="proxy">The GlCommandProxy to capture current state from.</param>
    /// <param name="forceFullGC">If true, forces a full garbage collection before measuring memory growth.</param>
    /// <returns>A leak report with resource leak counts and memory growth.</returns>
    public GlLeakReport GetLeakReport(GlCommandProxy proxy, bool forceFullGC = false)
    {
        ArgumentNullException.ThrowIfNull(proxy);

        if (!_isTracking)
        {
            return GlLeakReport.Empty;
        }

        GlCommandRecord current = proxy.Snapshot();
        return ComputeLeakReport(current, forceFullGC);
    }

    /// <summary>
    /// Computes a leak report using synthetic current data.
    /// Useful for testing the leak detector logic without a real GlCommandProxy.
    /// </summary>
    /// <param name="syntheticCurrent">Synthetic current state record for testing.</param>
    /// <param name="currentMemoryBytes">Synthetic current memory value for testing.</param>
    /// <returns>A leak report with resource leak counts and memory growth.</returns>
    public GlLeakReport GetLeakReport(GlCommandRecord syntheticCurrent, long currentMemoryBytes = 0)
    {
        ArgumentNullException.ThrowIfNull(syntheticCurrent);

        if (!_isTracking)
        {
            return GlLeakReport.Empty;
        }

        return ComputeLeakReport(syntheticCurrent, currentMemoryBytes);
    }

    /// <summary>
    /// Resets the detector to its initial state, clearing the baseline.
    /// </summary>
    public void Reset()
    {
        _baseline = GlCommandRecord.Empty;
        _memoryTracker.Reset();
        _isTracking = false;
    }

    private GlLeakReport ComputeLeakReport(GlCommandRecord current, bool forceFullGC)
    {
        // Compute net resource delta for the recording window.
        // Leaks = (current allocations - current deletions) - (baseline allocations - baseline deletions)
        // Simplified: Leaks = (current allocations - baseline allocations) - (current deletions - baseline deletions)
        
        int bufferAllocDelta = current.BufferAllocations - _baseline.BufferAllocations;
        int bufferDeleteDelta = current.BufferDeletions - _baseline.BufferDeletions;
        int bufferLeaks = bufferAllocDelta - bufferDeleteDelta;

        // VAO: only allocations tracked, deletions assumed zero in current implementation
        int vaoAllocDelta = current.VertexArrayAllocations - _baseline.VertexArrayAllocations;
        // TODO: Add VertexArrayDeletions to GlCommandRecord when available
        int vaoDeleteDelta = 0;
        int vaoLeaks = vaoAllocDelta - vaoDeleteDelta;

        // Textures: not currently tracked in GlCommandProxy
        // TODO: Add texture tracking to GlCommandProxy
        int textureLeaks = 0;

        long memoryGrowth = _memoryTracker.GetGrowthBytes(forceFullGC);

        return new GlLeakReport
        {
            BufferLeaks = Math.Max(0, bufferLeaks),  // Negative means more deletions than allocations - not a leak
            TextureLeaks = Math.Max(0, textureLeaks),
            VAOLeaks = Math.Max(0, vaoLeaks),
            UnmanagedGrowthBytes = memoryGrowth,
        };
    }

    private GlLeakReport ComputeLeakReport(GlCommandRecord current, long currentMemoryBytes)
    {
        int bufferAllocDelta = current.BufferAllocations - _baseline.BufferAllocations;
        int bufferDeleteDelta = current.BufferDeletions - _baseline.BufferDeletions;
        int bufferLeaks = bufferAllocDelta - bufferDeleteDelta;

        int vaoAllocDelta = current.VertexArrayAllocations - _baseline.VertexArrayAllocations;
        int vaoDeleteDelta = 0;
        int vaoLeaks = vaoAllocDelta - vaoDeleteDelta;

        int textureLeaks = 0;

        long memoryGrowth = currentMemoryBytes - _memoryTracker.BaselineBytes;

        return new GlLeakReport
        {
            BufferLeaks = Math.Max(0, bufferLeaks),
            TextureLeaks = Math.Max(0, textureLeaks),
            VAOLeaks = Math.Max(0, vaoLeaks),
            UnmanagedGrowthBytes = memoryGrowth,
        };
    }

    private void SetSyntheticMemoryBaseline(long bytes)
    {
        // Use reflection to set the private baseline for testing purposes
        var field = typeof(UnmanagedMemoryTracker).GetField("_baselineBytes",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        field?.SetValue(_memoryTracker, bytes);
    }
}
