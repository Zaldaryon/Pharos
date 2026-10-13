using System.Collections.Concurrent;
using System.Net;
using Vintagestory.API.Server;
using Vintagestory.Common;
using Vintagestory.Common.Network.Packets;

namespace Zaldaryon.Pharos.Server;

/// <summary>
/// In-memory ingress for UDP packets forwarded by headless clients over TCP. The game still
/// handles each packet in <c>ServerUdpNetwork</c> and associates it with its original client.
/// </summary>
internal sealed class HeadlessUdpNetServer : UNetServer
{
    private readonly ConcurrentQueue<UdpPacket> _pending = new();
    private readonly Dictionary<IPEndPoint, int> _endPoints = [];
    private readonly object _endPointsLock = new();

    public override Dictionary<IPEndPoint, int> EndPoints => _endPoints;

    public override void SetIpAndPort(string ip, int port)
    {
    }

    public override void Start()
    {
    }

    public override UdpPacket[] ReadMessage()
    {
        if (_pending.IsEmpty) return [];

        List<UdpPacket> packets = [];
        while (_pending.TryDequeue(out UdpPacket packet)) packets.Add(packet);
        return packets.ToArray();
    }

    public override void Dispose()
    {
        while (_pending.TryDequeue(out _))
        {
        }
    }

    public override int SendToClient(int clientId, Packet_UdpPacket packet) => 0;

    public override void Add(IPEndPoint endPoint, int clientId)
    {
        lock (_endPointsLock) _endPoints[endPoint] = clientId;
    }

    public override void Remove(IServerPlayer player)
    {
        lock (_endPointsLock)
        {
            foreach (IPEndPoint endpoint in _endPoints.Where(pair => pair.Value == player.ClientId).Select(pair => pair.Key).ToArray())
            {
                _endPoints.Remove(endpoint);
            }
        }
    }

    public override void EnqueuePacket(UdpPacket udpPacket) => _pending.Enqueue(udpPacket);
}
