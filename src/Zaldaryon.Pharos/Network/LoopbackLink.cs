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
    private readonly Func<int, bool, string?> _channelName;
    private readonly Queue<(double At, byte[] Data)> _toServer = new();
    private readonly Queue<(double At, NetIncomingMessage Message)> _toClient = new();
    private readonly List<(double At, Packet_UdpPacket Packet)> _udpToServer = [];
    private readonly List<(double At, Packet_UdpPacket Packet)> _udpToClient = [];
    private double _nowMs;
    private double _lastToServerAt;
    private double _lastToClientAt;
    private Action<Exception>? _onDisconnected;

    /// <param name="frame">The frame the recording stamps each packet with; -1 when there is none.</param>
    public LoopbackLink(PacketRecorder recorder, NetworkDegradationSimulator degradation, Func<int, bool, string?>? channelName = null, Func<long>? frame = null)
    {
        _recorder = recorder;
        _degradation = degradation;
        _channelName = channelName ?? ((_, _) => null);
        _frame = frame;
    }

    private readonly Func<long>? _frame;
    private bool _holdServerTraffic;
    private int _liveDiscarded;

    private long Frame => _frame?.Invoke() ?? -1;

    /// <summary>
    /// While set, the server's live packets are discarded and counted, so a replay can deliver its own in their place.
    /// </summary>
    public bool HoldServerTraffic
    {
        get { lock (_lock) return _holdServerTraffic; }
        set { lock (_lock) _holdServerTraffic = value; }
    }

    /// <summary>The live packets discarded while <see cref="HoldServerTraffic"/> was set.</summary>
    public int LiveDiscarded
    {
        get { lock (_lock) return _liveDiscarded; }
    }

    /// <summary>Whether the wire has been cut.</summary>
    public bool IsSevered { get; private set; }

    /// <summary>Advances the link's clock by one frame and delivers what is due.</summary>
    public void Advance(double dtMs, Action<byte[]> deliverToServer, Action<Packet_UdpPacket> deliverUdpToServer)
    {
        List<byte[]> tcp = [];
        List<Packet_UdpPacket> udp = [];

        _degradation.Elapse(dtMs);
        lock (_lock)
        {
            _nowMs += dtMs;
            ReleaseHeldIfRecovered();
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
        if (Filtered(() => PacketDescriber.Tcp(PacketDirection.Outbound, data, _channelName))) return null;

        lock (_lock)
        {
            if (IsSevered) return null;

            double leave = _degradation.Departure(PacketDirection.Outbound, data.Length, _nowMs);
            double at = Math.Max(leave + Latency(PacketDirection.Outbound), _lastToServerAt);
            if (_degradation.IsOutage) at = Math.Max(at, _degradation.OutageEndMs);
            _lastToServerAt = at;
            if (at <= _nowMs && _toServer.Count == 0) return data;

            _toServer.Enqueue((at, data));
            return null;
        }
    }

    /// <summary>A packet the server sent the client over TCP, as it is read off the socket.</summary>
    public void ServerSent(NetIncomingMessage message)
    {
        lock (_lock)
        {
            if (_holdServerTraffic)
            {
                _liveDiscarded++;
                return;
            }
        }

        byte[] data = new byte[message.messageLength];
        Array.Copy(message.message, data, message.messageLength);
        Record(data, PacketDirection.Inbound, server: true);
        if (Filtered(() => PacketDescriber.Tcp(PacketDirection.Inbound, data, _channelName))) return;

        lock (_lock)
        {
            if (IsSevered) return;

            double leave = _degradation.Departure(PacketDirection.Inbound, message.messageLength, _nowMs);
            double at = Math.Max(leave + Latency(PacketDirection.Inbound), _lastToClientAt);
            if (_degradation.IsOutage) at = Math.Max(at, _degradation.OutageEndMs);
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
        RecordUdp(packet, PacketDirection.Outbound);
        if (Filtered(() => PacketDescriber.Udp(PacketDirection.Outbound, packet, _channelName))) return null;

        lock (_lock)
        {
            if (IsSevered) return null;

            PacketDegradationResult fate = _degradation.ProcessPacket(PacketDirection.Outbound);
            if (fate.Dropped || fate.Corrupted) return null;

            double at = _degradation.Departure(PacketDirection.Outbound, Packet_UdpPacketSerializer.GetSize(packet), _nowMs) + fate.LatencyMs;
            if (at <= _nowMs && _udpToServer.Count == 0) return packet;

            _udpToServer.Add((at, packet));
            return null;
        }
    }

    /// <summary>Datagrams the server sent, as read off the socket; returns those due now.</summary>
    public List<Packet_UdpPacket> ServerSentUdp(IEnumerable<Packet_UdpPacket>? packets)
    {
        List<Packet_UdpPacket> due = [];

        lock (_lock)
        {
            if (_holdServerTraffic)
            {
                _liveDiscarded += packets?.Count() ?? 0;
                return due;
            }

            if (!IsSevered && packets != null)
            {
                foreach (Packet_UdpPacket packet in packets)
                {
                    RecordUdp(packet, PacketDirection.Inbound);
                    if (Filtered(() => PacketDescriber.Udp(PacketDirection.Inbound, packet, _channelName))) continue;

                    PacketDegradationResult fate = _degradation.ProcessPacket(PacketDirection.Inbound);
                    if (fate.Dropped || fate.Corrupted) continue;

                    double at = _degradation.Departure(PacketDirection.Inbound, Packet_UdpPacketSerializer.GetSize(packet), _nowMs) + fate.LatencyMs;
                    _udpToClient.Add((at, packet));
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

    // TCP packets held by an outage that has ended are due now. The last-send times follow them, or no new packet would ever leave.
    private void ReleaseHeldIfRecovered()
    {
        if (_degradation.IsOutage) return;

        ReleaseHeld(_toServer, _nowMs);
        ReleaseHeld(_toClient, _nowMs);
        if (double.IsPositiveInfinity(_lastToServerAt)) _lastToServerAt = _nowMs;
        if (double.IsPositiveInfinity(_lastToClientAt)) _lastToClientAt = _nowMs;
    }

    private static void ReleaseHeld<T>(Queue<(double At, T Item)> queue, double nowMs)
    {
        if (!queue.Any(p => double.IsPositiveInfinity(p.At))) return;

        List<(double At, T Item)> items = [.. queue];
        queue.Clear();
        foreach ((double at, T item) in items)
        {
            queue.Enqueue((double.IsPositiveInfinity(at) ? nowMs : at, item));
        }
    }

    // Whether a filter drops the packet. The packet is described only while a filter is in place.
    private bool Filtered(Func<NetworkPacketInfo> describe) => _degradation.HasFilters && _degradation.FilterDrops(describe());

    private double Latency(PacketDirection direction)
    {
        // TCP is reliable: it pays the latency and jitter of a lossy network, never the loss.
        if (!_degradation.IsActive) return 0;
        return Math.Max(0, _degradation.GetEffectiveLatency(direction));
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

        _recorder.Record(id, direction, data, Frame, PacketTransport.Tcp, type);
    }

    // A datagram is recorded as the bytes the engine serializes it to, so a replay can read it back.
    private void RecordUdp(Packet_UdpPacket packet, PacketDirection direction)
    {
        if (!_recorder.IsRecording) return;
        _recorder.Record(packet.Id, direction, Packet_UdpPacketSerializer.SerializeToBytes(packet), Frame, PacketTransport.Udp, "Packet_UdpPacket");
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
        if (_link.IsSevered || _link.HoldServerTraffic)
        {
            _overTcp.Clear();
            return due.Count == 0 ? null! : due;
        }

        while (_overTcp.TryDequeue(out Packet_UdpPacket? packet)) due.Add(packet);
        return due.Count == 0 ? null! : due;
    }

    // Datagrams the server sent inside TCP packets once the client fell back to TCP: they came
    // over the link already, as TCP. The game's dummy socket does not take them.
    private readonly System.Collections.Concurrent.ConcurrentQueue<Packet_UdpPacket> _overTcp = new();

    public override void EnqueuePacket(Packet_UdpPacket udpPacket)
    {
        if (_link.IsSevered || _link.HoldServerTraffic) return;
        _overTcp.Enqueue(udpPacket);
    }

    /// <summary>Puts a delayed datagram on the wire to the server.</summary>
    internal void Deliver(Packet_UdpPacket packet) => base.Send(packet);
}
