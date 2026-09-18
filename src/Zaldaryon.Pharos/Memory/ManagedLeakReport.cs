namespace Zaldaryon.Pharos.Memory;

/// <summary>
/// Immutable report of managed object leaks detected between a baseline and current snapshot.
/// Tracks unreturned mesh parts and unrecycled MeshData objects.
/// </summary>
/// <param name="UnreturnedMeshParts">Number of mesh parts tracked but not released (allocation delta).</param>
/// <param name="UnrecycledMeshData">Number of MeshData objects tracked but not released (allocation delta).</param>
public sealed record ManagedLeakReport(int UnreturnedMeshParts, int UnrecycledMeshData)
{
    /// <summary>
    /// True if any managed leaks were detected (UnreturnedMeshParts > 0 || UnrecycledMeshData > 0).
    /// </summary>
    public bool HasLeaks => UnreturnedMeshParts > 0 || UnrecycledMeshData > 0;

    /// <summary>
    /// Total number of leaked managed objects across all categories.
    /// </summary>
    public int TotalLeaks => UnreturnedMeshParts + UnrecycledMeshData;

    /// <summary>
    /// Empty report with no leaks.
    /// </summary>
    public static readonly ManagedLeakReport Empty = new(0, 0);
}
