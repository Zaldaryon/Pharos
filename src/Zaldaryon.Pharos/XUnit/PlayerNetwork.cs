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

    internal PlayerNetwork(HeadlessPlayerConnection connection)
    {
        _connection = connection;
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
}
