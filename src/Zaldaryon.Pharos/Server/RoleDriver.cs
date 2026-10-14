using Vintagestory.API.Common;
using Zaldaryon.Pharos.XUnit;

namespace Zaldaryon.Pharos.Server;

/// <summary>Assigns the server's player roles, such as <c>suplayer</c> or <c>suvisitor</c>, to test players.</summary>
public sealed class RoleDriver
{
    private readonly EmbeddedServerHost _host;

    internal RoleDriver(EmbeddedServerHost host) => _host = host;

    /// <summary>The role codes the server's configuration defines.</summary>
    public IReadOnlyList<string> Available => _host.RunOnGameThread(() =>
        _host.Server.Config.Roles.Select(role => role.Code).ToArray());

    /// <summary>Gives a player a role and checks that the player now has it.</summary>
    /// <param name="player">The joined test player.</param>
    /// <param name="roleCode">A role code from <see cref="Available"/>.</param>
    /// <returns>The role code the player has after the assignment.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="player"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="roleCode"/> is not a role the server defines.</exception>
    /// <exception cref="InvalidOperationException">The player has not joined, or the server did not apply the role.</exception>
    public string Assign(ServerTestPlayer player, string roleCode)
    {
        ArgumentNullException.ThrowIfNull(player);
        ArgumentException.ThrowIfNullOrWhiteSpace(roleCode);
        if (!Available.Contains(roleCode, StringComparer.OrdinalIgnoreCase))
        {
            throw new ArgumentException($"The server defines no role '{roleCode}'. Available roles: {string.Join(", ", Available)}.", nameof(roleCode));
        }

        player.RoleCode = roleCode;
        for (int tick = 0; tick <= 20; tick++)
        {
            string? current = _host.RunOnGameThread(() => player.Player?.Role?.Code);
            if (string.Equals(current, roleCode, StringComparison.OrdinalIgnoreCase)) return current;
            _host.Tick();
        }

        throw new InvalidOperationException($"The server did not apply role '{roleCode}' to {player.PlayerUID} within 20 ticks.");
    }
}
