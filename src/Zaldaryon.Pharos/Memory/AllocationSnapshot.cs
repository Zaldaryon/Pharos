namespace Zaldaryon.Pharos.Memory;

/// <summary>
/// Immutable snapshot of per-type managed allocation counts recorded since
/// <see cref="MemoryInspector.Enable"/> or the last <see cref="MemoryInspector.Reset"/>.
/// Call <see cref="MemoryInspector.AllocationSnapshot"/> to capture one.
/// </summary>
public sealed record AllocationSnapshot
{
    /// <summary>
    /// Number of <c>ItemRenderInfo</c> objects constructed while recording was enabled.
    /// Each GUI frame that renders item stacks without the Optimum reuse patch allocates one
    /// per visible slot.
    /// </summary>
    public long ItemRenderInfoAllocations { get; init; }

    /// <summary>
    /// Number of <c>MeshData</c> objects constructed directly (bypassing the recycler) while
    /// recording was enabled. Does not count recycler-managed mesh reuse.
    /// </summary>
    public long MeshDataAllocations { get; init; }

    public static readonly AllocationSnapshot Empty = new();
}
