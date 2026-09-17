namespace Zaldaryon.Pharos.Culling;

/// <summary>
/// Immutable copy of a single frustum plane equation (Ax + By + Cz + D = 0).
/// The normal vector (NormalX, NormalY, NormalZ) is unit-length.
/// A point is inside the frustum half-space when the signed distance is non-negative.
/// </summary>
public readonly record struct FrustumPlane(double NormalX, double NormalY, double NormalZ, double D)
{
    /// <summary>
    /// Returns the signed distance from the given point to this plane.
    /// Positive values are inside the frustum half-space.
    /// </summary>
    public double DistanceOf(double x, double y, double z) =>
        NormalX * x + NormalY * y + NormalZ * z + D;
}
