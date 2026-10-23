using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Vintagestory.Client.NoObf;
using Vintagestory.Common;
using Vintagestory.Server;

namespace Zaldaryon.Pharos.Server;

/// <summary>
/// The server-side socket of a headless player. It reads the player's messages like any in-memory socket, and can be severed: the
/// server then reads one disconnect for the player's connection, as it would when a real connection is lost, and nothing more from it.
/// </summary>
internal sealed class HeadlessTcpNetServer : DummyTcpNetServer
{
    private volatile bool _severed;
    private NetConnection? _connection;
    private bool _disconnectSent;
    private readonly object _readLock = new();
    private bool _parsing;

    internal bool Owns(ConnectedClient client) => ReferenceEquals(client.Socket, _connection);

    internal bool IsDrained(DummyNetwork network)
    {
        lock (_readLock)
        lock (ReceiveLock(network)) return !_parsing && ReceiveBuffer(network).Count == 0;
    }

    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "ServerReceiveBuffer")]
    private static extern ref Queue<object> ReceiveBuffer(DummyNetwork network);

    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "ServerReceiveBufferLock")]
    private static extern ref MonitorObject ReceiveLock(DummyNetwork network);

    /// <summary>Cuts the connection: the server reads a disconnect for the player on its next pass. Safe from any thread.</summary>
    internal void Sever() => _severed = true;

    // Runs on the packet parser thread. The connection is the one the player's first message came on.
    public override NetIncomingMessage ReadMessage()
    {
        lock (_readLock)
        {
            // The parser calls ReadMessage again only after enqueueing the preceding packet.
            _parsing = false;
            if (_severed)
            {
                if (_connection is null || _disconnectSent) return null!;

                _disconnectSent = true;
                return new NetIncomingMessage { Type = NetworkMessageType.Disconnect, SenderConnection = _connection };
            }

            NetIncomingMessage message = base.ReadMessage();
            if (message is not null) { _connection ??= message.SenderConnection; _parsing = true; }
            return message!;
        }
    }
}
