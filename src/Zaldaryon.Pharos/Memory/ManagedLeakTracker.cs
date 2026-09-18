using System.Collections.Concurrent;

namespace Zaldaryon.Pharos.Memory;

/// <summary>
/// Tracks managed object allocations and releases to detect leaks in mesh parts and MeshData.
/// Thread-safe for use in multi-threaded scenarios.
/// </summary>
public sealed class ManagedLeakTracker : IDisposable
{
    private readonly ConcurrentDictionary<object, TrackedInstance> _instances = new();
    private int _baselineMeshParts;
    private int _baselineMeshData;
    private bool _isTracking;
    private bool _disposed;

    /// <summary>
    /// True if tracking has been started with a valid baseline.
    /// </summary>
    public bool IsTracking => _isTracking;

    /// <summary>
    /// Current count of tracked instances (since baseline or creation).
    /// </summary>
    public int TrackedCount => _instances.Count;

    /// <summary>
    /// Tracks an instance allocation. Call when an object is created or acquired from a pool.
    /// </summary>
    /// <param name="key">The object instance to track (used as reference key).</param>
    /// <param name="label">Label identifying the type: "MeshPart" or "MeshData".</param>
    public void TrackInstance(object key, string label)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentException.ThrowIfNullOrWhiteSpace(label);

        _instances[key] = new TrackedInstance(label, Environment.TickCount64);
    }

    /// <summary>
    /// Releases a tracked instance. Call when an object is disposed or returned to a pool.
    /// </summary>
    /// <param name="key">The object instance to release.</param>
    /// <returns>True if the instance was tracked and removed, false if not found.</returns>
    public bool ReleaseInstance(object key)
    {
        ArgumentNullException.ThrowIfNull(key);
        return _instances.TryRemove(key, out _);
    }

    /// <summary>
    /// Records the current tracked counts as the baseline for leak detection.
    /// Call this at the start of a test scenario before exercising code that may leak.
    /// </summary>
    public void StartBaseline()
    {
        _baselineMeshParts = CountByLabel("MeshPart");
        _baselineMeshData = CountByLabel("MeshData");
        _isTracking = true;
    }

    /// <summary>
    /// Computes a leak report by comparing current tracked counts against the baseline.
    /// Leaks are computed as: current count - baseline count for each category.
    /// </summary>
    /// <returns>A leak report with counts of unreturned objects.</returns>
    public ManagedLeakReport GetLeakReport()
    {
        if (!_isTracking)
        {
            return ManagedLeakReport.Empty;
        }

        int currentMeshParts = CountByLabel("MeshPart");
        int currentMeshData = CountByLabel("MeshData");

        int meshPartLeaks = Math.Max(0, currentMeshParts - _baselineMeshParts);
        int meshDataLeaks = Math.Max(0, currentMeshData - _baselineMeshData);

        return new ManagedLeakReport(meshPartLeaks, meshDataLeaks);
    }

    /// <summary>
    /// Resets the tracker to its initial state, clearing all tracked instances and baseline.
    /// </summary>
    public void Reset()
    {
        _instances.Clear();
        _baselineMeshParts = 0;
        _baselineMeshData = 0;
        _isTracking = false;
    }

    /// <summary>
    /// Gets all currently tracked instances with their labels and tracking times.
    /// Useful for debugging which specific objects were not released.
    /// </summary>
    /// <returns>Dictionary of tracked objects to their metadata.</returns>
    public IReadOnlyDictionary<object, TrackedInstance> GetTrackedInstances()
    {
        return _instances.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
    }

    /// <summary>
    /// Gets tracked instances filtered by label.
    /// </summary>
    /// <param name="label">Label to filter by (e.g., "MeshPart", "MeshData").</param>
    /// <returns>List of objects matching the label.</returns>
    public IReadOnlyList<object> GetTrackedByLabel(string label)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        return _instances
            .Where(kvp => kvp.Value.Label.Equals(label, StringComparison.OrdinalIgnoreCase))
            .Select(kvp => kvp.Key)
            .ToList();
    }

    private int CountByLabel(string label)
    {
        return _instances.Count(kvp => kvp.Value.Label.Equals(label, StringComparison.OrdinalIgnoreCase));
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _instances.Clear();
    }
}

/// <summary>
/// Metadata for a tracked managed object instance.
/// </summary>
/// <param name="Label">Type label (e.g., "MeshPart", "MeshData").</param>
/// <param name="TrackedAtTick">Tick count when the instance was first tracked.</param>
public readonly record struct TrackedInstance(string Label, long TrackedAtTick);
