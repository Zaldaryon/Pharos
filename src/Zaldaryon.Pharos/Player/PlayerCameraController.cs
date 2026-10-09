using System;
using Vintagestory.API.Client;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.Client.NoObf;

namespace Zaldaryon.Pharos.Player;

/// <summary>
/// Concrete camera controller manipulating ClientMain camera state and calculating view frustum equations.
/// </summary>
public sealed class PlayerCameraController : IPlayerCameraController
{
    // Read on each use, so the class follows the client into a new game session after a reconnect.
    private readonly Func<ClientMain> _game;

    private ClientMain _client => _game();

    private readonly Vec3d _fallbackPosition = new();

    public PlayerCameraController(ClientMain client)
        : this(Fixed(client ?? throw new ArgumentNullException(nameof(client))))
    {
    }

    internal PlayerCameraController(Func<ClientMain> game)
    {
        _game = game ?? throw new ArgumentNullException(nameof(game));
    }

    private static Func<ClientMain> Fixed(ClientMain client) => () => client;

    /// <summary>
    /// Whether the client runs the engine's own camera, as an engine-mode client does.
    /// </summary>
    /// <remarks>
    /// The engine stores the player's view as the entity's yaw and pitch, in its own convention:
    /// a horizontal view is a pitch of π, straight up π/2 and straight down 3π/2, clamped just
    /// inside that range, and the yaw is offset by π from the convention used here (a pitch of 0
    /// looks ahead, positive looks up). It rebuilds the camera from those angles every frame, so
    /// for an engine-mode client the angles are converted and written there, and positions are
    /// measured from the eyes, where the engine casts its selection ray from.
    /// </remarks>
    internal bool UsesEngineCamera { get; set; }

    public double Yaw
    {
        get => UsesEngineCamera && _client.EntityPlayer?.Pos != null
            ? GameMath.Mod(_client.EntityPlayer.Pos.Yaw - Math.PI, Math.PI * 2)
            : _client.MainCamera != null ? _client.MainCamera.Yaw : _client.mouseYaw;
        set
        {
            if (UsesEngineCamera)
            {
                SetOrientation(value, Pitch, Roll);
                return;
            }

            _client.mouseYaw = (float)value;
            if (_client.MainCamera != null)
            {
                _client.MainCamera.Yaw = value;
            }
            if (_client.EntityPlayer?.Pos != null)
            {
                _client.EntityPlayer.Pos.Yaw = (float)value;
            }
            UpdateFrustum();
        }
    }

    public double Pitch
    {
        get => UsesEngineCamera && _client.EntityPlayer?.Pos != null
            ? Math.PI - _client.EntityPlayer.Pos.Pitch
            : _client.MainCamera != null ? _client.MainCamera.Pitch : _client.mousePitch;
        set
        {
            if (UsesEngineCamera)
            {
                SetOrientation(Yaw, value, Roll);
                return;
            }

            _client.mousePitch = (float)value;
            if (_client.MainCamera != null)
            {
                _client.MainCamera.Pitch = value;
            }
            if (_client.EntityPlayer?.Pos != null)
            {
                _client.EntityPlayer.Pos.Pitch = (float)value;
            }
            UpdateFrustum();
        }
    }

    public double Roll
    {
        get => _client.MainCamera != null ? _client.MainCamera.Roll : 0.0;
        set
        {
            if (_client.MainCamera != null)
            {
                _client.MainCamera.Roll = value;
            }
            UpdateFrustum();
        }
    }

    public Vec3d Position
    {
        get
        {
            if (UsesEngineCamera && _client.EntityPlayer?.Pos != null)
            {
                return _client.EntityPlayer.Pos.XYZ.Add(_client.EntityPlayer.LocalEyePos);
            }

            if (_client.MainCamera?.CamSourcePosition != null)
            {
                return _client.MainCamera.CamSourcePosition.Clone();
            }

            if (_client.EntityPlayer?.CameraPos != null)
            {
                return _client.EntityPlayer.CameraPos.Clone();
            }

            return _fallbackPosition.Clone();
        }
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            _fallbackPosition.Set(value);
            _client.MainCamera?.CamSourcePosition?.Set(value);
            _client.EntityPlayer?.CameraPos?.Set(value);
            UpdateFrustum();
        }
    }

    public void LookAt(double x, double y, double z)
    {
        Vec3d camPos = Position;
        double dx = x - camPos.X;
        double dy = y - camPos.Y;
        double dz = z - camPos.Z;

        double horizDist = Math.Sqrt(dx * dx + dz * dz);
        double pitch = Math.Atan2(dy, horizDist);
        double yaw = Math.Atan2(-dx, -dz);

        SetOrientation(yaw, pitch, Roll);
    }

    public void LookAt(Vec3d target)
    {
        ArgumentNullException.ThrowIfNull(target);
        LookAt(target.X, target.Y, target.Z);
    }

    public void SetOrientation(double yaw, double pitch, double roll = 0.0)
    {
        if (UsesEngineCamera && _client.EntityPlayer?.Pos != null)
        {
            float engineYaw = (float)GameMath.Mod(yaw + Math.PI, Math.PI * 2);
            float enginePitch = GameMath.Clamp((float)(Math.PI - pitch), 1.5857964f, 4.697389f);
            _client.mouseYaw = engineYaw;
            _client.mousePitch = enginePitch;
            _client.EntityPlayer.Pos.Yaw = engineYaw;
            _client.EntityPlayer.Pos.Pitch = enginePitch;
            return;
        }

        _client.mouseYaw = (float)yaw;
        _client.mousePitch = (float)pitch;

        if (_client.MainCamera != null)
        {
            _client.MainCamera.Yaw = yaw;
            _client.MainCamera.Pitch = pitch;
            _client.MainCamera.Roll = roll;
        }

        if (_client.EntityPlayer?.Pos != null)
        {
            _client.EntityPlayer.Pos.Yaw = (float)yaw;
            _client.EntityPlayer.Pos.Pitch = (float)pitch;
        }

        UpdateFrustum();
    }

    private FrustumCulling? _fallbackFrustum;

    public double[] ViewMatrix
    {
        get
        {
            if (_client.MainCamera?.CameraMatrixOrigin != null)
            {
                double[] origin = _client.MainCamera.CameraMatrixOrigin;
                if (origin[0] != 0.0 || origin[5] != 0.0 || origin[15] != 0.0)
                {
                    return origin;
                }
            }

            if (_client.MvMatrix != null && _client.MvMatrix.Count > 0)
            {
                double[] top = _client.MvMatrix.Top;
                if (top[0] != 0.0 || top[5] != 0.0 || top[15] != 0.0)
                {
                    return top;
                }
            }

            Vec3d eye = Position;
            Vec3f forward = EntityPos.GetViewVector((float)Pitch, (float)Yaw);
            Vec3d target = new(eye.X + forward.X, eye.Y + forward.Y, eye.Z + forward.Z);
            double[] view = Mat4d.Create();
            Mat4d.LookAt(view, eye.ToDoubleArray(), target.ToDoubleArray(), new double[] { 0.0, 1.0, 0.0 });
            return view;
        }
    }

    public double[] ProjectionMatrix
    {
        get
        {
            if (_client.PMatrix != null && _client.PMatrix.Count > 0)
            {
                double[] top = _client.PMatrix.Top;
                if (top[11] == -1.0)
                {
                    return top;
                }
            }

            double[] proj = Mat4d.Create();
            Mat4d.Perspective(proj, 70.0 * Math.PI / 180.0, 1280.0 / 720.0, 0.1, 1000.0);
            return proj;
        }
    }

    public FrustumCulling Frustum => _client.frustumCuller ?? (_fallbackFrustum ??= new FrustumCulling());

    public bool IsInFrustum(double x, double y, double z, double radius = 0.5)
    {
        return Frustum.SphereInFrustum(x, y, z, radius);
    }

    public bool IsInFrustum(BlockPos pos)
    {
        ArgumentNullException.ThrowIfNull(pos);
        return IsInFrustum(pos.X + 0.5, pos.Y + 0.5, pos.Z + 0.5, 0.866);
    }

    public bool IsInFrustum(Vec3d pos, double radius = 0.5)
    {
        ArgumentNullException.ThrowIfNull(pos);
        return IsInFrustum(pos.X, pos.Y, pos.Z, radius);
    }

    public void UpdateFrustum()
    {
        FrustumCulling frustum = Frustum;
        BlockPos playerPos = _client.EntityPlayer?.Pos?.AsBlockPos ?? new BlockPos(0);
        double[] projection = ProjectionMatrix;

        Vec3d eye = Position;
        Vec3f forward = EntityPos.GetViewVector((float)Pitch, (float)Yaw);
        Vec3d target = new(eye.X + forward.X, eye.Y + forward.Y, eye.Z + forward.Z);
        double[] up = new double[] { 0.0, 1.0, 0.0 };

        double[] view = Mat4d.Create();
        Mat4d.LookAt(view, eye.ToDoubleArray(), target.ToDoubleArray(), up);

        if (Roll != 0.0)
        {
            Mat4d.Rotate(view, view, Roll, new double[] { 1.0, 0.0, 0.0 });
        }

        if (_client.MainCamera != null)
        {
            _client.MainCamera.CameraMatrixOrigin = view;
            _client.MainCamera.CameraMatrix = view;
        }

        frustum.CalcFrustumEquations(playerPos, projection, view);
    }
}
