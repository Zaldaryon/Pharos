using Vintagestory.Client;
using Vintagestory.Client.Network;
using Vintagestory.Common;

namespace Zaldaryon.Pharos.Network;

/// <summary>
/// The wire between an engine-mode client and an embedded server: every packet the client sends
/// or receives passes through it, where it is recorded and degraded.
/// </summary>
/// <remarks>
/// <para>
/// The client talks to the server over in-memory sockets. Pharos gives it sockets that hand each
/// packet to this link instead of straight to the other side. The link records the packet in
/// <see cref="PacketRecorder"/> when recording is on, and applies the active
/// <see cref="NetworkDegradationSimulator"/> profile.
/// </para>
/// <para>
/// Latency and jitter delay packets in both directions. TCP stays reliable and in order, as TCP
/// is: jitter never lets a packet overtake an earlier one, and nothing is dropped. Drops and
/// corruption hit UDP only, which is what a lossy network actually loses. A corrupted datagram
/// fails its checksum on a real network, so it is dropped too. Time on the link is the simulated
/// time of the session's frames, so the same seed and profile give the same delivery every run.
/// </para>
/// <para>
/// <see cref="Sever"/> cuts the wire: nothing passes any more, and the client is told its
/// connection broke, through the same handler a real socket error would reach.
/// </para>
/// </remarks>
internal sealed class LoopbackLink
{
    private readonly object _lock = new();
    private readonly PacketRecorder _recorder;
    private readonly NetworkDegradationSimulator _degradation;
    private readonly Queue<(double At, byte[] Data)> _toServer = new();
    private readonly Queue<(double At, NetIncomingMessage Message)> _toClient = new();
    private readonly List<(double At, Packet_UdpPacket Packet)> _udpToServer = [];
    private readonly List<(double At, Packet_UdpPacket Packet)> _udpToClient = [];
    private double _nowMs;
    private double _lastToServerAt;
    private double _lastToClientAt;
    private Action<Exception>? _onDisconnected;

    public LoopbackLink(PacketRecorder recorder, NetworkDegradationSimulator degradation)
    {
        _recorder = recorder;
        _degradation = degradation;
    }

    /// <summary>Whether the wire has been cut.</summary>
    public bool IsSevered { get; private set; }

    /// <summary>Advances the link's clock by one frame and delivers what is due.</summary>
    public void Advance(double dtMs, Action<byte[]> deliverToServer, Action<Packet_UdpPacket> deliverUdpToServer)
    {
        List<byte[]> tcp = [];
        List<Packet_UdpPacket> udp = [];

        lock (_lock)
        {
            _nowMs += dtMs;
            while (_toServer.Count > 0 && _toServer.Peek().At <= _nowMs) tcp.Add(_toServer.Dequeue().Data);
            TakeDue(_udpToServer, udp);
        }

        foreach (byte[] data in tcp) deliverToServer(data);
        foreach (Packet_UdpPacket packet in udp) deliverUdpToServer(packet);
    }

    /// <summary>A packet the client sends over TCP. Returns it when it may go now.</summary>
    public byte[]? ClientSends(byte[] data)
    {
        Record(data, PacketDirection.Outbound, server: false);

        lock (_lock)
        {
            if (IsSevered) return null;

            double at = Math.Max(_nowMs + Latency(), _lastToServerAt);
            _lastToServerAt = at;
            if (at <= _nowMs && _toServer.Count == 0) return data;

            _toServer.Enqueue((at, data));
            return null;
        }
    }

    /// <summary>A packet the server sent the client over TCP, as it is read off the socket.</summary>
    public void ServerSent(NetIncomingMessage message)
    {
        byte[] data = new byte[message.messageLength];
        Array.Copy(message.message, data, message.messageLength);
        Record(data, PacketDirection.Inbound, server: true);

        lock (_lock)
        {
            if (IsSevered) return;

            double at = Math.Max(_nowMs + Latency(), _lastToClientAt);
            _lastToClientAt = at;
            _toClient.Enqueue((at, message));
        }
    }

    /// <summary>The next server packet due for the client, or null.</summary>
    public NetIncomingMessage? NextForClient()
    {
        lock (_lock)
        {
            return _toClient.Count > 0 && _toClient.Peek().At <= _nowMs ? _toClient.Dequeue().Message : null;
        }
    }

    /// <summary>A datagram the client sends. Returns it when it may go now.</summary>
    public Packet_UdpPacket? ClientSendsUdp(Packet_UdpPacket packet)
    {
        lock (_lock)
        {
            if (IsSevered) return null;

            PacketDegradationResult fate = _degradation.ProcessPacket();
            if (fate.Dropped || fate.Corrupted) return null;
            if (fate.LatencyMs <= 0 && _udpToServer.Count == 0) return packet;

            _udpToServer.Add((_nowMs + fate.LatencyMs, packet));
            return null;
        }
    }

    /// <summary>Datagrams the server sent, as read off the socket; returns those due now.</summary>
    public List<Packet_UdpPacket> ServerSentUdp(IEnumerable<Packet_UdpPacket>? packets)
    {
        List<Packet_UdpPacket> due = [];

        lock (_lock)
        {
            if (!IsSevered && packets != null)
            {
                foreach (Packet_UdpPacket packet in packets)
                {
                    PacketDegradationResult fate = _degradation.ProcessPacket();
                    if (!fate.Dropped && !fate.Corrupted) _udpToClient.Add((_nowMs + fate.LatencyMs, packet));
                }
            }

            TakeDue(_udpToClient, due);
        }

        return due;
    }

    /// <summary>Remembers the handler the client wants called when its connection breaks.</summary>
    public void OnDisconnected(Action<Exception> handler)
    {
        lock (_lock)
        {
            _onDisconnected = handler;
        }
    }

    /// <summary>
    /// Cuts the wire and tells the client its connection broke.
    /// </summary>
    public void Sever(string reason)
    {
        Action<Exception>? handler;
        lock (_lock)
        {
            if (IsSevered) return;
            IsSevered = true;
            _toServer.Clear();
            _toClient.Clear();
            _udpToServer.Clear();
            _udpToClient.Clear();
            handler = _onDisconnected;
        }

        handler?.Invoke(new IOException(reason));
    }

    /// <summary>
    /// Takes the link out of use without telling the client: a reconnect leaves the old game's
    /// sockets behind, and nothing of the old connection may reach the new one.
    /// </summary>
    public void Detach()
    {
        lock (_lock)
        {
            IsSevered = true;
            _onDisconnected = null;
            _toServer.Clear();
            _toClient.Clear();
            _udpToServer.Clear();
            _udpToClient.Clear();
        }
    }

    private double Latency()
    {
        // TCP is reliable: it pays the latency and jitter of a lossy network, never the loss.
        if (!_degradation.IsActive) return 0;
        return Math.Max(0, _degradation.GetEffectiveLatency());
    }

    private void TakeDue(List<(double At, Packet_UdpPacket Packet)> queue, List<Packet_UdpPacket> into)
    {
        queue.Sort((a, b) => a.At.CompareTo(b.At));
        int due = queue.FindIndex(p => p.At > _nowMs);
        if (due < 0) due = queue.Count;
        into.AddRange(queue.Take(due).Select(p => p.Packet));
        queue.RemoveRange(0, due);
    }

    private void Record(byte[] data, PacketDirection direction, bool server)
    {
        if (!_recorder.IsRecording) return;

        int id;
        string type;
        try
        {
            if (server)
            {
                id = Packet_ServerSerializer.DeserializeBuffer(data, data.Length, new Packet_Server()).Id;
                type = "Packet_Server";
            }
            else
            {
                id = Packet_ClientSerializer.DeserializeBuffer(data, data.Length, new Packet_Client()).Id;
                type = "Packet_Client";
            }
        }
        catch
        {
            id = -1;
            type = server ? "Packet_Server" : "Packet_Client";
        }

        _recorder.Record(id, direction, data, type);
    }
}

/// <summary>The client's TCP socket on a <see cref="LoopbackLink"/>.</summary>
internal sealed class LinkedTcpNetClient : DummyTcpNetClient
{
    private readonly LoopbackLink _link;

    public LinkedTcpNetClient(LoopbackLink link)
    {
        _link = link;
    }

    public override void Connect(string ip, int port, Action<ConnectionResult> OnConnectionResult, Action<Exception> OnDisconnected)
    {
        _link.OnDisconnected(OnDisconnected);
        base.Connect(ip, port, OnConnectionResult, OnDisconnected);
    }

    public override void Send(byte[] data)
    {
        if (_link.ClientSends(data) is { } now) base.Send(now);
    }

    public override NetIncomingMessage ReadMessage()
    {
        while (base.ReadMessage() is { } message)
        {
            _link.ServerSent(message);
        }

        return _link.NextForClient()!;
    }

    /// <summary>Puts a delayed packet on the wire to the server.</summary>
    internal void Deliver(byte[] data) => base.Send(data);
}

/// <summary>The client's UDP socket on a <see cref="LoopbackLink"/>.</summary>
internal sealed class LinkedUdpNetClient : DummyUdpNetClient
{
    private readonly LoopbackLink _link;

    public LinkedUdpNetClient(LoopbackLink link)
    {
        _link = link;
    }

    public override void Send(Packet_UdpPacket packet)
    {
        // Sent as far as the client can tell, whatever the link then does with it.
        ModNetworkPatches.LinkedUdpSending(this, packet);
        if (_link.ClientSendsUdp(packet) is { } now) base.Send(now);
    }

    public override IEnumerable<Packet_UdpPacket> ReadMessage()
    {
        List<Packet_UdpPacket> due = _link.ServerSentUdp(base.ReadMessage());
        return due.Count == 0 ? null! : due;
    }

    /// <summary>Puts a delayed datagram on the wire to the server.</summary>
    internal void Deliver(Packet_UdpPacket packet) => base.Send(packet);
}
