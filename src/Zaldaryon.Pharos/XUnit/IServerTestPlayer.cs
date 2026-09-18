using Vintagestory.API.Common;
using Vintagestory.API.Server;

namespace Zaldaryon.Pharos.XUnit;

/// <summary>
/// Represents a test player connected to an embedded server for scenario testing.
/// </summary>
/// <remarks>
/// This interface provides a controlled abstraction over <see cref="IServerPlayer"/>
/// for test scenarios, including privilege management, teleportation, and inventory operations.
/// </remarks>
public interface IServerTestPlayer
{
    /// <summary>
    /// Gets the underlying server player instance.
    /// May be null if the player is not connected to an active server.
    /// </summary>
    IServerPlayer? Player { get; }

    /// <summary>
    /// Gets the player's entity in the world.
    /// May be null if the player is not spawned or not connected.
    /// </summary>
    EntityPlayer? Entity { get; }

    /// <summary>
    /// Gets the unique player identifier.
    /// </summary>
    string PlayerUID { get; }

    /// <summary>
    /// Gets or sets the player's role code (e.g., "admin", "suplayer").
    /// </summary>
    string RoleCode { get; set; }

    /// <summary>
    /// Grants the specified privileges to this player.
    /// </summary>
    /// <param name="privileges">One or more privilege codes to grant.</param>
    void GrantPrivilege(params string[] privileges);

    /// <summary>
    /// Revokes the specified privileges from this player.
    /// </summary>
    /// <param name="privileges">One or more privilege codes to revoke.</param>
    void RevokePrivilege(params string[] privileges);

    /// <summary>
    /// Teleports the player to the specified world coordinates.
    /// </summary>
    /// <param name="x">The X coordinate.</param>
    /// <param name="y">The Y coordinate.</param>
    /// <param name="z">The Z coordinate.</param>
    /// <returns>A task that completes when the teleport is finished.</returns>
    Task TeleportTo(double x, double y, double z);

    /// <summary>
    /// Gives the player an item with the specified code.
    /// </summary>
    /// <param name="itemCode">The item code (e.g., "game:sword-iron").</param>
    /// <param name="quantity">The quantity to give. Default is 1.</param>
    void GiveItem(string itemCode, int quantity = 1);

    /// <summary>
    /// Checks whether the player has at least the specified quantity of an item.
    /// </summary>
    /// <param name="itemCode">The item code to check for.</param>
    /// <param name="quantity">The minimum quantity required. Default is 1.</param>
    /// <returns>True if the player has the item in sufficient quantity.</returns>
    bool HasItem(string itemCode, int quantity = 1);
}
