using System.Reflection;
using Vintagestory.API.Client;
using Vintagestory.API.Config;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Zaldaryon.Pharos.Core;

namespace Zaldaryon.Pharos.Inspection;

/// <summary>A snapshot of the client's camera state.</summary>
public sealed record CameraSnapshot(
    float X, float Y, float Z,
    int FovDegrees,
    int Mode,
    double[]? ProjectionMatrix);

/// <summary>
/// Reads and drives the client's camera: mode, FOV, position and projection.
/// </summary>
public sealed class CameraDriver
{
    private readonly HeadlessClient _client;
    private static readonly FieldInfo? s_cameraMode = typeof(Vintagestory.Client.NoObf.Camera).GetField("CameraMode", BindingFlags.Instance | BindingFlags.NonPublic);

    internal CameraDriver(HeadlessClient client) => _client = client;

    private int ReadMode() => _client.RunOnClientThread(() =>
    {
        object? v = s_cameraMode?.GetValue(_client.Client.MainCamera);
        return v is EnumCameraMode m ? (int)m : 0;
    });

    /// <summary>The current camera mode as an int.</summary>
    public int Mode => ReadMode();

    /// <summary>The current FOV in degrees.</summary>
    public int FovDegrees => _client.RunOnClientThread(() => Vintagestory.Client.NoObf.ClientSettings.FieldOfView);

    /// <summary>Reads the current camera state.</summary>
    public CameraSnapshot Snapshot() => _client.RunOnClientThread(() =>
    {
        var pos = _client.Client.EntityPlayer.Pos.XYZ;
        double[]? proj = null;
        try { proj = _client.Client.PerspectiveProjectionMat; } catch { }
        object? v = s_cameraMode?.GetValue(_client.Client.MainCamera);
        int mode = v is EnumCameraMode m ? (int)m : 0;
        return new CameraSnapshot(
            (float)pos.X, (float)pos.Y, (float)pos.Z,
            Vintagestory.Client.NoObf.ClientSettings.FieldOfView,
            mode,
            proj);
    });

    /// <summary>Sets the camera mode.</summary>
    public void SetMode(int mode) => _client.RunOnClientThread(() =>
        s_cameraMode?.SetValue(_client.Client.MainCamera, (EnumCameraMode)mode));

    /// <summary>Sets the FOV.</summary>
    public void SetFov(int fov) => _client.RunOnClientThread(() =>
        Vintagestory.Client.NoObf.ClientSettings.FieldOfView = fov);
}