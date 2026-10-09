using System.Net;
using System.Reflection;
using Vintagestory.Common;
using Vintagestory.Server;
using Vintagestory.Server.Network;
using Zaldaryon.Pharos.Core;

namespace Zaldaryon.Pharos.Server;

public sealed partial class EmbeddedServerHost
{
    private static readonly FieldInfo? s_serverLockField = typeof(DummyNetwork).GetField("ServerReceiveBufferLock", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
    private static readonly FieldInfo? s_clientLockField = typeof(DummyNetwork).GetField("ClientReceiveBufferLock", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
    private static readonly FieldInfo? s_endPointsReverseField = typeof(DummyUdpNetServer).GetField("endPointsReverse", BindingFlags.Instance | BindingFlags.NonPublic);

    /// <summary>
    /// Readies the server for an engine-mode client joining again as <paramref name="playerName"/>:
    /// drops the player if it is still on the server, as after a lost connection, and gives the
    /// next connection an in-memory socket of its own. The host's own socket keeps whatever the
    /// old connection left queued, and nothing reads it again.
    /// </summary>
    /// <param name="playerName">The player that is coming back.</param>
    /// <param name="previousSlot">The socket an earlier reconnect installed, closed here, or -1.</param>
    /// <returns>The network the client's new TCP socket talks over, and the slot of the server's end.</returns>
    internal (DummyNetwork Network, int Slot) PrepareReconnect(string playerName, int previousSlot) => RunOnGameThread(() =>
    {
        if (Server.GetClientByPlayername(playerName) is { } stale)
        {
            Disconnect(stale);
        }

        if (previousSlot >= 0 && previousSlot < Server.MainSockets.Length)
        {
            // A reconnect that timed out may have left a connection that never identified.
            if (Server.MainSockets[previousSlot] is DummyTcpNetServer previous
                && s_connectedClientField?.GetValue(previous) is NetConnection { client: { } orphan }
                && Server.Clients.ContainsKey(orphan.Id))
            {
                Disconnect(orphan);
            }

            Server.MainSockets[previousSlot] = null!;
        }

        ResetLoopbackUdp();

        // The new connection identifies over the socket, as the first one did.
        HeadlessClient.ForgetLocalAssetPush(this);

        DummyNetwork network = new();
        network.Start();
        DummyTcpNetServer socket = new();
        socket.SetNetwork(network);
        return (network, HeadlessPlayerConnection.InstallSocket(Server, socket));
    });

    private static readonly FieldInfo? s_connectedClientField = typeof(DummyTcpNetServer).GetField("connectedClient", BindingFlags.Instance | BindingFlags.NonPublic);

    // The engine removes every client it disconnects from the UDP server, which throws for one
    // that never finished its UDP handshake: such a client gets an endpoint first.
    private void Disconnect(ConnectedClient client)
    {
        if (Server.UdpSockets[0] is DummyUdpNetServer udp && !udp.EndPoints.ContainsValue(client.Id))
        {
            udp.Add(new IPEndPoint(IPAddress.Loopback, 60000 + client.Id), client.Id);
        }

        Server.DisconnectPlayer(client, null, null);
    }

    // Every connection over an in-memory socket shares the host's UDP server, which reads all of
    // its datagrams as from one connection and one endpoint. The old player must not keep that
    // connection, nor the endpoint, or the new client's UDP handshake is ignored.
    private void ResetLoopbackUdp()
    {
        if (Server.UdpSockets[0] is not DummyUdpNetServer udp) return;

        udp.Client.Player = null;
        IPEndPoint loopback = new(0L, 0);
        if (udp.EndPoints.TryGetValue(loopback, out int clientId))
        {
            udp.EndPoints.Remove(loopback);
            (s_endPointsReverseField?.GetValue(udp) as Dictionary<int, IPEndPoint>)?.Remove(clientId);
        }

        // Datagrams still queued either way belong to the old connection.
        object serverLock = s_serverLockField?.GetValue(UdpNetwork) ?? throw new MissingFieldException(nameof(DummyNetwork), "ServerReceiveBufferLock");
        object clientLock = s_clientLockField?.GetValue(UdpNetwork) ?? throw new MissingFieldException(nameof(DummyNetwork), "ClientReceiveBufferLock");
        lock (serverLock)
        {
            lock (clientLock)
            {
                UdpNetwork.Clear();
            }
        }
    }
}
