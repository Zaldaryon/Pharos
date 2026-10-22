using Vintagestory.API.Server;
using Zaldaryon.Pharos.XUnit;

namespace Zaldaryon.Pharos.Server;

/// <summary>
/// The data the server keeps of its players: the role, the privileges, the groups and whatever a
/// mod stored with the player.
/// </summary>
public sealed class PlayerDataDriver
{
    private readonly EmbeddedServerHost _host;

    internal PlayerDataDriver(EmbeddedServerHost host) => _host = host;

    /// <summary>
    /// The data of a player, created when the player has none yet.
    /// </summary>
    /// <param name="uid">The player's uid.</param>
    public IServerPlayerData Of(string uid)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(uid);

        return _host.RunOnGameThread(() => _host.Server.PlayerDataManager.GetOrCreateServerPlayerData(uid));
    }

    /// <summary>
    /// The data of a player, created when the player has none yet.
    /// </summary>
    /// <param name="player">The player.</param>
    public IServerPlayerData Of(ServerTestPlayer player)
    {
        ArgumentNullException.ThrowIfNull(player);
        return Of(player.PlayerUID);
    }
}
