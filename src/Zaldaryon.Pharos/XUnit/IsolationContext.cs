using Vintagestory.API.MathTools;
using Zaldaryon.Pharos.Player;
using Zaldaryon.Pharos.Timing;

namespace Zaldaryon.Pharos.XUnit;

/// <summary>
/// Holds captured client state for rollback isolation.
/// </summary>
public sealed class IsolationContext
{
    /// <summary>
    /// The camera position at the time of capture.
    /// </summary>
    public Vec3d SavedCameraPosition { get; init; } = new();

    /// <summary>
    /// The player entity position at the time of capture.
    /// </summary>
    public Vec3d SavedPlayerPosition { get; init; } = new();

    /// <summary>
    /// The camera yaw angle in radians at the time of capture.
    /// </summary>
    public float SavedCameraYaw { get; init; }

    /// <summary>
    /// The camera pitch angle in radians at the time of capture.
    /// </summary>
    public float SavedCameraPitch { get; init; }

    /// <summary>
    /// Captures the current player and camera state into an IsolationContext.
    /// </summary>
    /// <param name="player">The test player abstraction. If null or unavailable, returns null.</param>
    /// <param name="controller">The frame controller. Currently unused but reserved for future state.</param>
    /// <returns>A new IsolationContext with captured state, or null if the player is not available.</returns>
    public static IsolationContext? Capture(IClientTestPlayer? player, DeterministicFrameController? controller)
    {
        if (player is null || !player.IsAvailable)
            return null;

        return new IsolationContext
        {
            SavedCameraPosition = player.Camera.Position.Clone(),
            SavedPlayerPosition = player.Position.Clone(),
            SavedCameraYaw = (float)player.Camera.Yaw,
            SavedCameraPitch = (float)player.Camera.Pitch
        };
    }
}
