using System;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.Client.NoObf;

namespace Zaldaryon.Pharos.Player;

/// <summary>
/// Client-side test player providing programmatic control over player position, orientation, inventory, and GUIs.
/// </summary>
public sealed class ClientTestPlayer : IClientTestPlayer
{
    private readonly ClientMain _client;
    private readonly PlayerCameraController _camera;
    private readonly PlayerInventoryAccessor _inventory;
    private readonly PlayerGuiController _gui;

    private readonly Vec3d _fallbackMotion = new();

    public ClientTestPlayer(ClientMain client)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _camera = new PlayerCameraController(client);
        _inventory = new PlayerInventoryAccessor(client);
        _gui = new PlayerGuiController(client);
    }

    public bool IsAvailable => _client.player != null && _client.EntityPlayer != null;

    public IClientPlayer? RawPlayer => _client.player;

    public EntityPlayer? RawEntity => _client.EntityPlayer;

    public Vec3d Position
    {
        get
        {
            if (RawEntity?.Pos != null)
            {
                return RawEntity.Pos.XYZ.Clone();
            }

            return _camera.Position.Clone();
        }
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            Teleport(value.X, value.Y, value.Z);
        }
    }

    public Vec3d Motion
    {
        get => RawEntity?.Pos?.Motion != null ? RawEntity.Pos.Motion.Clone() : _fallbackMotion.Clone();
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            _fallbackMotion.Set(value);
            if (RawEntity?.Pos?.Motion != null)
            {
                RawEntity.Pos.Motion.Set(value);
            }
        }
    }

    public Vec3d EyePosition
    {
        get
        {
            if (RawEntity?.Pos != null)
            {
                Vec3d eye = RawEntity.Pos.XYZ.Clone();
                if (RawEntity.LocalEyePos != null)
                {
                    eye.Add(RawEntity.LocalEyePos);
                }
                return eye;
            }

            return _camera.Position.Clone();
        }
    }

    public void Teleport(double x, double y, double z)
    {
        if (RawEntity?.Pos != null)
        {
            RawEntity.Pos.SetPos(x, y, z);

            double eyeY = RawEntity.LocalEyePos != null ? RawEntity.LocalEyePos.Y : 1.7;
            RawEntity.CameraPos?.Set(x, y + eyeY, z);
            _camera.Position = new Vec3d(x, y + eyeY, z);
        }
        else
        {
            _camera.Position = new Vec3d(x, y, z);
        }

        _camera.UpdateFrustum();
    }

    public void Teleport(Vec3d pos)
    {
        ArgumentNullException.ThrowIfNull(pos);
        Teleport(pos.X, pos.Y, pos.Z);
    }

    public IPlayerCameraController Camera => _camera;

    public IPlayerInventoryAccessor Inventory => _inventory;

    public IPlayerGuiController Gui => _gui;
}
