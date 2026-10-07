using System.Reflection;
using Vintagestory.Client;
using Vintagestory.Common;

namespace Zaldaryon.Pharos.Network;

/// <summary>
/// The game's TCP client, with the packets sent before the connection is up held back and sent
/// in order once it is.
/// </summary>
/// <remarks>
/// <see cref="ClientMain.Connect"/> starts connecting and sends its first packet, the login token
/// request, straight away. The game's connection queues packets sent before it connects and sends
/// them from the thread that connects, but it checks and fills that queue without a lock: a packet
/// queued just after the connecting thread emptied it is never sent, and the client waits forever
/// for the server's answer. A fast machine rarely loses that race; a busy one does. This client
/// keeps its own locked queue and empties it from the network thread once the connection is up.
/// </remarks>
internal sealed class OrderedTcpNetClient : TcpNetClient
{
    private static readonly FieldInfo? s_connection =
        typeof(TcpNetClient).GetField("tcpConnection", BindingFlags.Instance | BindingFlags.NonPublic);

    private readonly object _lock = new();
    private readonly Queue<byte[]> _pending = new();

    private bool IsConnected => s_connection?.GetValue(this) is TCPNetworkConnection { Connected: true };

    /// <inheritdoc />
    public override void Send(byte[] data)
    {
        lock (_lock)
        {
            if (_pending.Count == 0 && IsConnected)
            {
                base.Send(data);
                return;
            }

            _pending.Enqueue(data);
        }

        Flush();
    }

    /// <inheritdoc />
    public override NetIncomingMessage ReadMessage()
    {
        Flush();
        return base.ReadMessage();
    }

    private void Flush()
    {
        lock (_lock)
        {
            while (_pending.Count > 0 && IsConnected)
            {
                base.Send(_pending.Dequeue());
            }
        }
    }
}
