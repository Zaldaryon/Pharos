namespace Zaldaryon.Pharos.Graphics;

/// <summary>
/// Represents a shared edge between two adjacent quads in a mesh.
/// Used for detecting UV seam discontinuities along merged quad boundaries.
/// </summary>
/// <param name="QuadAIndex">Index of the first quad sharing this edge.</param>
/// <param name="QuadBIndex">Index of the second quad sharing this edge.</param>
/// <param name="SharedAxis">The axis along which the edge is shared (0=X, 1=Y, 2=Z).</param>
/// <param name="SharedValue">The coordinate value along the shared axis.</param>
public readonly record struct QuadEdge(int QuadAIndex, int QuadBIndex, int SharedAxis, float SharedValue)
{
    /// <summary>
    /// Returns true if this edge represents a valid adjacency (both indices non-negative).
    /// </summary>
    public bool IsValid => QuadAIndex >= 0 && QuadBIndex >= 0;

    /// <summary>
    /// Returns a descriptive string of the shared axis.
    /// </summary>
    public string AxisName => SharedAxis switch
    {
        0 => "X",
        1 => "Y",
        2 => "Z",
        _ => "Unknown"
    };

    /// <inheritdoc />
    public override string ToString() =>
        $"QuadEdge(A={QuadAIndex}, B={QuadBIndex}, Axis={AxisName}, Value={SharedValue:F3})";
}

/// <summary>
/// Result of UV continuity check across a quad edge.
/// </summary>
/// <param name="Edge">The quad edge being checked.</param>
/// <param name="IsContinuous">True if UVs are continuous across this edge within tolerance.</param>
/// <param name="MaxDiscontinuity">Maximum UV delta found across the edge.</param>
public readonly record struct UvContinuityResult(QuadEdge Edge, bool IsContinuous, float MaxDiscontinuity)
{
    /// <summary>
    /// Empty result for edges with no UV data.
    /// </summary>
    public static readonly UvContinuityResult Empty = new(default, true, 0f);
}
