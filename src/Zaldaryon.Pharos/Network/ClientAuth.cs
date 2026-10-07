namespace Zaldaryon.Pharos.Network;

/// <summary>
/// Who an engine-mode client says it is when it joins a server, and whether it proves it to the
/// Vintage Story auth server.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Offline"/> needs no account and no network: the client answers the server's login
/// token itself. A server accepts it when it runs with <c>VerifyPlayerAuth</c> off, as an
/// embedded server does by default. This is what CI and cloud runs use.
/// </para>
/// <para>
/// <see cref="Online"/> joins as a real account. The client asks the auth server for a
/// single-use multiplayer token, and a server with <c>VerifyPlayerAuth</c> on has the auth server
/// validate it, as a public server does. It needs the account's session, as the launcher stores it
/// after a login: the player name, the player UID, the session key and its signature. The session
/// key is a credential: keep it in a secret, never in the repository.
/// </para>
/// </remarks>
public sealed record ClientAuth
{
    private ClientAuth(string playerName, string playerUid, string? sessionKey, string? sessionSignature)
    {
        PlayerName = playerName;
        PlayerUid = playerUid;
        SessionKey = sessionKey;
        SessionSignature = sessionSignature;
    }

    /// <summary>The player name the client joins with.</summary>
    public string PlayerName { get; }

    /// <summary>The player UID the client joins with.</summary>
    public string PlayerUid { get; }

    /// <summary>Whether the client proves its identity to the auth server.</summary>
    public bool IsOnline => SessionKey != null;

    internal string? SessionKey { get; }

    internal string? SessionSignature { get; }

    /// <summary>An offline identity, with a UID derived from the name.</summary>
    public static ClientAuth Offline(string playerName) =>
        new(playerName, "pharos-" + playerName.ToLowerInvariant(), null, null);

    /// <summary>A real account's session.</summary>
    public static ClientAuth Online(string playerName, string playerUid, string sessionKey, string sessionSignature)
    {
        ArgumentException.ThrowIfNullOrEmpty(playerName);
        ArgumentException.ThrowIfNullOrEmpty(playerUid);
        ArgumentException.ThrowIfNullOrEmpty(sessionKey);
        ArgumentException.ThrowIfNullOrEmpty(sessionSignature);
        return new ClientAuth(playerName, playerUid, sessionKey, sessionSignature);
    }

    /// <summary>
    /// A real account's session from the environment variables <c>{prefix}_PLAYERNAME</c>,
    /// <c>{prefix}_PLAYERUID</c>, <c>{prefix}_SESSIONKEY</c> and <c>{prefix}_SESSIONSIGNATURE</c>,
    /// or null when any of them is missing.
    /// </summary>
    public static ClientAuth? FromEnvironment(string prefix = "PHAROS_VS")
    {
        string? name = Environment.GetEnvironmentVariable(prefix + "_PLAYERNAME");
        string? uid = Environment.GetEnvironmentVariable(prefix + "_PLAYERUID");
        string? key = Environment.GetEnvironmentVariable(prefix + "_SESSIONKEY");
        string? signature = Environment.GetEnvironmentVariable(prefix + "_SESSIONSIGNATURE");

        return string.IsNullOrEmpty(name) || string.IsNullOrEmpty(uid) || string.IsNullOrEmpty(key) || string.IsNullOrEmpty(signature)
            ? null
            : new ClientAuth(name, uid, key, signature);
    }

    /// <inheritdoc />
    // The session key and signature are credentials; they never appear in test output.
    public override string ToString() => $"{(IsOnline ? "Online" : "Offline")}({PlayerName})";
}
