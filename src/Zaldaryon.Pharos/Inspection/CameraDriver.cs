using System.Reflection;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.Client.NoObf;
using Zaldaryon.Pharos.Core;

namespace Zaldaryon.Pharos.Inspection;

/// <summary>A snapshot of the client's camera state.</summary>
public sealed record CameraSnapshot(
    double EyeX, double EyeY, double EyeZ,
    double PitchDegrees,
    double YawDegrees,
    int FovDegrees,
    int Mode,
    double[] CameraMatrix,
    double[] ProjectionMatrix);

/// <summary>
/// Reads and drives the client's camera: mode, FOV, position and projection.
/// </summary>
public sealed class CameraDriver
{
    private readonly HeadlessClient _client;

    // CameraMode is internal in the Camera class
    private static readonly FieldInfo? CameraModeField =
        typeof(Camera).GetField("CameraMode", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

    // Tppcameradistance for third person
    private static readonly FieldInfo? TppDistanceField =
        typeof(Camera).GetField("Tppcameradistance", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

    internal CameraDriver(HeadlessClient client) => _client = client;

    /// <summary>The actual camera eye position (not player position).</summary>
    public (double X, double Y, double Z) EyePosition => _client.RunOnClientThread(() =>
    {
        var eye = _client.Client.MainCamera?.CameraEyePos;
        if (eye == null) return (0, 0, 0);
        return (eye.X, eye.Y, eye.Z);
    });

    /// <summary>The current camera mode (0=FirstPerson, 1=ThirdPerson, 2=Overhead).</summary>
    public int Mode => _client.RunOnClientThread(() =>
    {
        var camera = _client.Client.MainCamera;
        if (camera == null) return 0;
        var mode = CameraModeField?.GetValue(camera);
        return mode is EnumCameraMode m ? (int)m : 0;
    });

    /// <summary>The current camera pitch in degrees (vertical rotation).</summary>
    public double PitchDegrees => _client.RunOnClientThread(() =>
    {
        var player = _client.Client.EntityPlayer;
        return player?.Pos.Pitch * GameMath.RAD2DEG ?? 0;
    });

    /// <summary>The current camera yaw in degrees (horizontal rotation).</summary>
    public double YawDegrees => _client.RunOnClientThread(() =>
    {
        var player = _client.Client.EntityPlayer;
        return player?.Pos.Yaw * GameMath.RAD2DEG ?? 0;
    });

    /// <summary>The current FOV in degrees.</summary>
    public int FovDegrees => _client.RunOnClientThread(() => ClientSettings.FieldOfView);

    /// <summary>The current third-person camera distance.</summary>
    public float ThirdPersonDistance => _client.RunOnClientThread(() =>
    {
        var camera = _client.Client.MainCamera;
        if (camera == null) return 0f;
        var dist = TppDistanceField?.GetValue(camera);
        return dist is float f ? f : 0f;
    });

    /// <summary>Whether the camera is in first-person mode.</summary>
    public bool IsFirstPerson => Mode == (int)EnumCameraMode.FirstPerson;

    /// <summary>Whether the camera is in third-person mode.</summary>
    public bool IsThirdPerson => Mode == (int)EnumCameraMode.ThirdPerson;

    /// <summary>Whether the camera is in overhead mode.</summary>
    public bool IsOverhead => Mode == (int)EnumCameraMode.Overhead;

    /// <summary>Reads the current camera state as a detached snapshot.</summary>
    public CameraSnapshot Snapshot() => _client.RunOnClientThread(() =>
    {
        var camera = _client.Client.MainCamera;
        var player = _client.Client.EntityPlayer;

        var eye = camera?.CameraEyePos ?? new Vec3d();
        var modeObj = CameraModeField?.GetValue(camera);
        int mode = modeObj is EnumCameraMode m ? (int)m : 0;

        // Detach camera matrix (copy to avoid mutation)
        double[] camMatrix = new double[16];
        var srcMatrix = camera?.CameraMatrix;
        if (srcMatrix != null && srcMatrix.Length >= 16)
            Array.Copy(srcMatrix, camMatrix, 16);

        // Detach projection matrix
        double[] projMatrix = new double[16];
        var srcProj = _client.Client.PerspectiveProjectionMat;
        if (srcProj != null && srcProj.Length >= 16)
            Array.Copy(srcProj, projMatrix, 16);

        return new CameraSnapshot(
            eye.X, eye.Y, eye.Z,
            (player?.Pos.Pitch ?? 0) * GameMath.RAD2DEG,
            (player?.Pos.Yaw ?? 0) * GameMath.RAD2DEG,
            ClientSettings.FieldOfView,
            mode,
            camMatrix,
            projMatrix);
    });

    /// <summary>Sets the camera mode.</summary>
    public void SetMode(EnumCameraMode mode) => _client.RunOnClientThread(() =>
    {
        var camera = _client.Client.MainCamera;
        if (camera != null)
        {
            // Use the internal SetMode method if available
            var setModeMethod = typeof(Camera).GetMethod("SetMode", BindingFlags.Instance | BindingFlags.NonPublic);
            if (setModeMethod != null)
            {
                setModeMethod.Invoke(camera, new object[] { mode });
            }
            else
            {
                CameraModeField?.SetValue(camera, mode);
            }
        }
    });

    /// <summary>Sets the FOV in degrees.</summary>
    public void SetFov(int fov) => _client.RunOnClientThread(() =>
    {
        ClientSettings.FieldOfView = Math.Clamp(fov, 10, 150);
    });

    /// <summary>Rotates the camera by the specified pitch and yaw deltas in radians.</summary>
    public void Rotate(float pitchDelta, float yawDelta) => _client.RunOnClientThread(() =>
    {
        var player = _client.Client.EntityPlayer;
        if (player != null)
        {
            player.Pos.Pitch = Math.Clamp(player.Pos.Pitch + pitchDelta, -GameMath.PIHALF + 0.01f, GameMath.PIHALF - 0.01f);
            player.Pos.Yaw = (player.Pos.Yaw + yawDelta) % GameMath.TWOPI;
        }
    });

    /// <summary>Looks at a specific world position.</summary>
    public void LookAt(double x, double y, double z) => _client.RunOnClientThread(() =>
    {
        var player = _client.Client.EntityPlayer;
        if (player == null) return;

        var eye = _client.Client.MainCamera?.CameraEyePos ?? player.Pos.XYZ;
        double dx = x - eye.X;
        double dy = y - eye.Y;
        double dz = z - eye.Z;

        double dist = Math.Sqrt(dx * dx + dz * dz);
        float yaw = (float)Math.Atan2(-dx, -dz);
        float pitch = (float)Math.Atan2(dy, dist);

        player.Pos.Yaw = yaw;
        player.Pos.Pitch = Math.Clamp(pitch, -GameMath.PIHALF + 0.01f, GameMath.PIHALF - 0.01f);
    });

    /// <summary>Toggles between first and third person camera modes.</summary>
    public void TogglePerspective() => _client.RunOnClientThread(() =>
    {
        var camera = _client.Client.MainCamera;
        if (camera == null) return;

        var modeObj = CameraModeField?.GetValue(camera);
        var currentMode = modeObj is EnumCameraMode m ? m : EnumCameraMode.FirstPerson;
        var newMode = currentMode == EnumCameraMode.FirstPerson
            ? EnumCameraMode.ThirdPerson
            : EnumCameraMode.FirstPerson;

        SetMode(newMode);
    });

    /// <summary>Sets the third-person camera distance.</summary>
    public void SetThirdPersonDistance(float distance) => _client.RunOnClientThread(() =>
    {
        var camera = _client.Client.MainCamera;
        if (camera != null)
        {
            TppDistanceField?.SetValue(camera, Math.Clamp(distance, 0.5f, 20f));
        }
    });
}
