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
    // Read on each use, so the class follows the client into a new game session after a reconnect.
    private readonly Func<ClientMain> _game;

    private ClientMain _client => _game();
    private readonly PlayerCameraController _camera;
    private readonly PlayerInventoryAccessor _inventory;
    private readonly PlayerGuiController _gui;
    private readonly ClientVitals _vitals;
    private readonly ClientDeath _death;

    private readonly Vec3d _fallbackMotion = new();

    public ClientTestPlayer(ClientMain client)
        : this(Fixed(client ?? throw new ArgumentNullException(nameof(client))))
    {
    }

    internal ClientTestPlayer(Func<ClientMain> game)
    {
        _game = game ?? throw new ArgumentNullException(nameof(game));
        _camera = new PlayerCameraController(game);
        _inventory = new PlayerInventoryAccessor(game);
        _gui = new PlayerGuiController(game);
        _vitals = new ClientVitals(game);
        _death = new ClientDeath(game);
    }

    private static Func<ClientMain> Fixed(ClientMain client) => () => client;

    public bool IsAvailable => _client.player != null && _client.EntityPlayer != null;

    public IClientPlayer? RawPlayer => _client.player;

    public EntityPlayer? RawEntity => _client.EntityPlayer;

    /// <inheritdoc />
    public IMountableSeat? MountedOn => RawEntity?.MountedOn;

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

    /// <summary>
    /// Switches the camera to the engine's own view angles. See
    /// <see cref="PlayerCameraController.UsesEngineCamera"/>.
    /// </summary>
    internal void UseEngineCamera() => _camera.UsesEngineCamera = true;

    public IPlayerInventoryAccessor Inventory => _inventory;

    public IPlayerGuiController Gui => _gui;

    /// <summary>
    /// The client's copy of the player's survival state, as the server last sent it: health,
    /// oxygen and body temperature. The client does not keep satiety or the nutrient levels, so
    /// those read as null.
    /// </summary>
    public ClientVitals Vitals => _vitals;

    /// <summary>
    /// The client's side of dying and coming back: whether the death screen is up, and the Respawn
    /// button on it. See <see cref="ClientDeath"/>.
    /// </summary>
    public ClientDeath Death => _death;

    /// <summary>
    /// Points the death screen at the client's GUI driver, once the client has built one.
    /// </summary>
    internal ClientTestPlayer UseGuiDriver(System.Func<UI.GuiDriver?> ui)
    {
        _death.UseGuiDriver(ui);
        return this;
    }
}
