using Vintagestory.API.Server;
using Vintagestory.Server;
using Zaldaryon.Pharos.XUnit;

namespace Zaldaryon.Pharos.Server;

/// <summary>What a ban says.</summary>
/// <param name="Name">The name of the banned player.</param>
/// <param name="Reason">Why the player was banned.</param>
/// <param name="Until">When the ban ends, or null when it does not end.</param>
public sealed record BanInfo(string Name, string Reason, DateTime? Until);

/// <summary>What became of a join.</summary>
/// <param name="Accepted">Whether the player got in.</param>
/// <param name="DisconnectReason">Why the player was refused, when it was refused.</param>
/// <param name="Player">The player, when the join was accepted.</param>
public sealed record JoinOutcome(bool Accepted, string DisconnectReason, ServerTestPlayer? Player);

/// <summary>
/// The administration of the server: who may join, who may not, and the data the server keeps of
/// its players.
/// </summary>
public sealed class AdminDriver
{
    private readonly EmbeddedServerHost _host;

    internal AdminDriver(EmbeddedServerHost host) => _host = host;

    /// <summary>
    /// Lets a player join, whatever the whitelist says.
    /// </summary>
    /// <param name="name">The name of the player.</param>
    /// <param name="reason">Why the player was let in.</param>
    public void Whitelist(string name, string reason = "")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        _host.RunOnGameThread(() => _host.Server.PlayerDataManager.WhitelistPlayer(name, "", "test", reason, null));
    }

    /// <summary>
    /// Takes a player off the whitelist.
    /// </summary>
    /// <param name="name">The name of the player.</param>
    public void Unwhitelist(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        _host.RunOnGameThread(() => _host.Server.PlayerDataManager.UnWhitelistPlayer(name, ""));
    }

    /// <summary>
    /// Whether a player may join, whatever the whitelist says.
    /// </summary>
    /// <param name="name">The name of the player.</param>
    public bool IsWhitelisted(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        return _host.RunOnGameThread(() => _host.Server.PlayerDataManager.GetPlayerWhitelist(name, "") != null);
    }

    /// <summary>
    /// Bans a player, so the server refuses the join.
    /// </summary>
    /// <param name="name">The name of the player.</param>
    /// <param name="until">When the ban ends, or null when it does not end.</param>
    /// <param name="reason">Why the player was banned.</param>
    public void Ban(string name, DateTime? until = null, string reason = "")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        // A ban without a uid is looked up by name, which is how the game decides the join.
        _host.RunOnGameThread(() => GameMembers.Call(
            _host.Server.PlayerDataManager,
            "BanPlayer",
            name,
            null,
            "test",
            reason,
            until ?? DateTime.MaxValue));
    }

    /// <summary>
    /// Takes a ban off a player.
    /// </summary>
    /// <param name="name">The name of the player.</param>
    public void Unban(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        _host.RunOnGameThread(() => GameMembers.Call(
            _host.Server.PlayerDataManager,
            "UnbanPlayer",
            name,
            null,
            "test"));
    }

    /// <summary>
    /// The ban of a player, or null when the player is not banned.
    /// </summary>
    /// <param name="name">The name of the player.</param>
    public BanInfo? BanOf(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        return _host.RunOnGameThread(() =>
        {
            PlayerEntry? entry = _host.Server.PlayerDataManager.GetPlayerBan(name, null!);
            if (entry == null) return null;
            return new BanInfo(entry.PlayerName, entry.Reason ?? "", entry.UntilDate);
        });
    }

    /// <summary>
    /// Whether the server lets only the whitelisted players in.
    /// </summary>
    public EnumWhitelistMode WhitelistMode
    {
        get => _host.RunOnGameThread(() => _host.Config.WhitelistMode);
        set => _host.RunOnGameThread(() => _host.Config.WhitelistMode = value);
    }

    /// <summary>
    /// Kicks a player off the server, with the reason the client is sent.
    /// </summary>
    /// <param name="player">The player to kick.</param>
    /// <param name="reason">Why the player was kicked.</param>
    public void Kick(ServerTestPlayer player, string reason = "")
    {
        ArgumentNullException.ThrowIfNull(player);

        _host.RunOnGameThread(() => player.Player?.Disconnect(reason));
    }

    /// <summary>
    /// Tries to let a player in, and says what became of it.
    /// </summary>
    /// <param name="name">The name of the player.</param>
    /// <param name="maxTicks">The most ticks to wait for the answer.</param>
    /// <param name="ct">Cancels the wait.</param>
    /// <returns>What became of the join.</returns>
    public async Task<JoinOutcome> TryJoinAsync(string name, int maxTicks = 2000, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        try
        {
            ServerTestPlayer player = await _host.JoinPlayerAsync(name, maxTicks, ct).ConfigureAwait(false);
            return new JoinOutcome(true, "", player);
        }
        catch (Exception)
        {
            BanInfo? ban = BanOf(name);
            return new JoinOutcome(false, ban?.Reason ?? "", null);
        }
    }
}
