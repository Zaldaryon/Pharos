using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace Zaldaryon.Pharos.Player;

/// <summary>
/// Exposes test player inspection and control on the client for scenario and integration tests.
/// </summary>
public interface IClientTestPlayer
{
    /// <summary>
    /// Gets whether a player entity is currently spawned and attached to the client.
    /// </summary>
    bool IsAvailable { get; }

    /// <summary>
    /// Gets the underlying Vintage Story IClientPlayer instance, or null if not yet connected.
    /// </summary>
    IClientPlayer? RawPlayer { get; }

    /// <summary>
    /// Gets the underlying Vintage Story EntityPlayer instance, or null if not yet spawned.
    /// </summary>
    EntityPlayer? RawEntity { get; }

    /// <summary>
    /// Gets or sets the player entity's absolute world coordinates.
    /// </summary>
    Vec3d Position { get; set; }

    /// <summary>
    /// Gets or sets the player entity's current motion velocity vector.
    /// </summary>
    Vec3d Motion { get; set; }

    /// <summary>
    /// Gets the player entity's eye position in absolute world coordinates.
    /// </summary>
    Vec3d EyePosition { get; }

    /// <summary>
    /// Teleports the player immediately to the target coordinates without physics interpolation.
    /// </summary>
    void Teleport(double x, double y, double z);

    /// <summary>
    /// Teleports the player immediately to the target position vector.
    /// </summary>
    void Teleport(Vec3d pos);

    /// <summary>
    /// Gets the camera controller for adjusting orientation and querying frustum visibility.
    /// </summary>
    IPlayerCameraController Camera { get; }

    /// <summary>
    /// Gets the inventory accessor for inspecting and modifying player items and slots.
    /// </summary>
    IPlayerInventoryAccessor Inventory { get; }

    /// <summary>
    /// Gets the GUI controller for programmatically opening and closing dialogs and interfaces.
    /// </summary>
    IPlayerGuiController Gui { get; }
}
