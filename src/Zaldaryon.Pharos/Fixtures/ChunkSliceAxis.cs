namespace Zaldaryon.Pharos.Fixtures;

/// <summary>
/// Defines the axis along which a chunk slice is taken for preview rendering.
/// </summary>
public enum ChunkSliceAxis
{
    /// <summary>
    /// Horizontal slice at a fixed Y level, showing X-Z plane (top-down view).
    /// </summary>
    XZ,

    /// <summary>
    /// Vertical slice at a fixed Z level, showing X-Y plane (front view).
    /// </summary>
    XY,

    /// <summary>
    /// Vertical slice at a fixed X level, showing Y-Z plane (side view).
    /// </summary>
    YZ
}
