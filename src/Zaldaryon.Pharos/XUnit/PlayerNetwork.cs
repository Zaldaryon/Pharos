using Zaldaryon.Pharos.Network;
using Zaldaryon.Pharos.Server;

namespace Zaldaryon.Pharos.XUnit;

/// <summary>
/// A headless player's network: the link between the player and the server, which a test degrades, scripts or cuts. Its
/// changes apply from the next server tick.
/// </summary>
public sealed class PlayerNetwork
{
    private readonly HeadlessPlayerConnection _connection;
    private readonly ServerTestPlayer _player;

    internal PlayerNetwork(HeadlessPlayerConnection connection, ServerTestPlayer player)
    {
        _connection = connection;
        _player = player;
    }

    /// <summary>The simulator that degrades the player's link.</summary>
    public NetworkDegradationSimulator Simulator => _connection.Degradation;

    /// <summary>Degrades the player's link with <paramref name="profile"/>, from the next tick.</summary>
    /// <exception cref="ArgumentException"><paramref name="profile"/> is out of range.</exception>
    public void Configure(DegradedNetworkProfile profile) => Simulator.Configure(profile);

    /// <summary>Runs a timeline of profiles on the player's link. See <see cref="NetworkScript"/>.</summary>
    public void Run(NetworkScript script) => Simulator.Run(script);

    /// <summary>Cuts the connection without the leave packet. See <see cref="ServerTestPlayer.Sever"/>.</summary>
    public void Sever() => _connection.Sever();

    /// <summary>
    /// Sends a chat line from the player without running a server tick, so a test can choose the
    /// tick that handles it. A leading slash runs it as a command with this player as the caller.
    /// </summary>
    /// <exception cref="ArgumentException"><paramref name="message"/> is empty.</exception>
    /// <exception cref="InvalidOperationException">The player is not joined.</exception>
    public void SendChat(string message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message);
        _player.RunBotOnGameThread(() => _connection.Chat(message));
    }

    /// <summary>
    /// Freezes the player: it stops reading and answering the server's pings, so the server times it out after the game's ping timeout
    /// of simulated time. See <see cref="ServerTestPlayer.Freeze"/>.
    /// </summary>
    public void Freeze() => _player.FreezeNetwork();
}
