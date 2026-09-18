using Zaldaryon.Pharos.XUnit;

namespace Atlas.Api;

/// <summary>
/// Compatibility alias for Atlas.Api.ITestPlayer.
/// This is a type-forwarding interface to <see cref="IServerTestPlayer"/> in Pharos.
/// </summary>
/// <remarks>
/// <para>
/// For source compatibility, this interface mirrors the essential IServerTestPlayer members.
/// For new code, use <see cref="IServerTestPlayer"/> directly.
/// </para>
/// <para>
/// The underlying Vintagestory types (IServerPlayer, EntityPlayer) are accessed through
/// the <see cref="Inner"/> property which returns the native Pharos player.
/// </para>
/// </remarks>
[Obsolete("Use IServerTestPlayer from Zaldaryon.Pharos.XUnit instead. This shim exists only for migration.")]
public interface ITestPlayer
{
    /// <summary>
    /// Gets the underlying Pharos server test player.
    /// Use this to access IServerPlayer, EntityPlayer, and other Vintagestory types.
    /// </summary>
    IServerTestPlayer Inner { get; }

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
    Task TeleportTo(double x, double y, double z);

    /// <summary>
    /// Gives the player an item with the specified code.
    /// </summary>
    void GiveItem(string itemCode, int quantity = 1);

    /// <summary>
    /// Checks whether the player has at least the specified quantity of an item.
    /// </summary>
    bool HasItem(string itemCode, int quantity = 1);
}
