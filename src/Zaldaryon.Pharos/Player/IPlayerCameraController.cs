using Vintagestory.API.Client;
using Vintagestory.API.MathTools;

namespace Zaldaryon.Pharos.Player;

/// <summary>
/// Controls the client camera orientation, position, view-projection matrices, and frustum culling equations.
/// </summary>
public interface IPlayerCameraController
{
    /// <summary>
    /// Gets or sets the camera yaw angle in radians.
    /// </summary>
    double Yaw { get; set; }

    /// <summary>
    /// Gets or sets the camera pitch angle in radians.
    /// </summary>
    double Pitch { get; set; }

    /// <summary>
    /// Gets or sets the camera roll angle in radians.
    /// </summary>
    double Roll { get; set; }

    /// <summary>
    /// Gets or sets the absolute position of the camera in world coordinates.
    /// </summary>
    Vec3d Position { get; set; }

    /// <summary>
    /// Orients the camera to look directly toward the target world coordinates.
    /// </summary>
    void LookAt(double x, double y, double z);

    /// <summary>
    /// Orients the camera to look directly toward the target vector.
    /// </summary>
    void LookAt(Vec3d target);

    /// <summary>
    /// Sets yaw, pitch, and roll angles simultaneously.
    /// </summary>
    void SetOrientation(double yaw, double pitch, double roll = 0.0);

    /// <summary>
    /// Gets the current 4x4 camera view matrix.
    /// </summary>
    double[] ViewMatrix { get; }

    /// <summary>
    /// Gets the current 4x4 projection matrix.
    /// </summary>
    double[] ProjectionMatrix { get; }

    /// <summary>
    /// Gets the active frustum culler instance.
    /// </summary>
    FrustumCulling Frustum { get; }

    /// <summary>
    /// Determines whether a sphere at the given coordinates lies within the current camera frustum.
    /// </summary>
    bool IsInFrustum(double x, double y, double z, double radius = 0.5);

    /// <summary>
    /// Determines whether a block at the given position lies within the current camera frustum.
    /// </summary>
    bool IsInFrustum(BlockPos pos);

    /// <summary>
    /// Determines whether a sphere centered at the vector lies within the current camera frustum.
    /// </summary>
    bool IsInFrustum(Vec3d pos, double radius = 0.5);

    /// <summary>
    /// Immediately updates the camera view-projection matrices and frustum culling plane equations.
    /// </summary>
    void UpdateFrustum();
}
