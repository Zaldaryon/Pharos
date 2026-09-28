using System;
using System.Collections.Generic;
using Vintagestory.Client;
using Vintagestory.Common;

namespace Zaldaryon.Pharos.Server;

/// <summary>A real TCP/UDP Vintage Story protocol participant for embedded-server integration tests.</summary>
public sealed class TcpProtocolTestPlayer : IDisposable
{
    private readonly TcpNetClient _tcp = new();
    private readonly Queue<Packet_Server> _received = new();
    private UdpNetClient? _udp;
    private Exception? _disconnectError;
    private string? _connectionResult;
    private string? _connectFailure;
    private bool _disposed;

    public TcpProtocolTestPlayer(string playerName, string playerUid)
    {
        if (string.IsNullOrWhiteSpace(playerName)) throw new ArgumentException("A player name is required.", nameof(playerName));
        if (string.IsNullOrWhiteSpace(playerUid)) throw new ArgumentException("A player UID is required.", nameof(playerUid));
        PlayerName = playerName;
        PlayerUid = playerUid;
    }

    public string PlayerName { get; }
    public string PlayerUid { get; }
    public string? ConnectionResult => _connectionResult;
    public string? ConnectFailure => _connectFailure;
    public bool ConnectionCompleted => _connectionResult is not null || _disconnectError is not null;
    public Exception? DisconnectError => _disconnectError;

    public void Connect(string host, int port)
    {
        ThrowIfDisposed();
        _tcp.Connect(host, port, result =>
        {
            _connectionResult = result.ToString();
            if (!result.connected) _connectFailure = result.errorMessage ?? result.exception?.Message ?? "TCP connection failed.";
        }, error => _disconnectError = error);
    }

    public IReadOnlyList<Packet_Server> DrainTcpPackets()
    {
        ThrowIfDisposed();
        while (_tcp.ReadMessage() is { } message)
        {
            Packet_Server packet = Packet_ServerSerializer.DeserializeBuffer(
                message.message, message.messageLength, new Packet_Server());
            _received.Enqueue(packet);
        }

        Packet_Server[] packets = _received.ToArray();
        _received.Clear();
        return packets;
    }

    public void SendLoginTokenQuery() => Send(new Packet_Client
    {
        Id = 33
    });

    public void SendIdentification() => Send(new Packet_Client
    {
        Id = 1,
        Identification = new Packet_ClientIdentification
        {
            Playername = PlayerName,
            PlayerUID = PlayerUid,
            MpToken = null,
            MdProtocolVersion = "1.22.7",
            NetworkVersion = "1.22.6",
            ShortGameVersion = "1.22.7",
            ViewDistance = 128,
            RenderMetaBlocks = 1
        }
    });

    public void RequestTcpPositionFallback() => Send(new Packet_Client { Id = 34 });

    public void RequestJoin() => Send(new Packet_Client
    {
        Id = 11,
        RequestJoin = new Packet_ClientRequestJoin { Language = "en" }
    });

    public void SignalClientLoaded() => Send(new Packet_Client { Id = 26 });

    public void SignalClientPlaying() => Send(new Packet_Client { Id = 29, ClientPlaying = new Packet_ClientPlaying() });

    public void SetForward(bool down) => Send(new Packet_Client
    {
        Id = 21,
        MoveKeyChange = new Packet_MoveKeyChange { Key = Packet_MoveKeyEnum.Forward, Down = down ? 1 : 0 }
    });

    public void Leave() => Send(new Packet_Client { Id = 14, Leave = new Packet_ClientLeave() });

    public void ConnectUdp(string host, int port, string loginToken)
    {
        ThrowIfDisposed();
        if (_udp is not null) throw new InvalidOperationException("UDP is already connected.");
        UdpNetClient udp = new();
        udp.Connect(host, port);
        _udp = udp;
        udp.Send(new Packet_UdpPacket
        {
            Id = 1,
            ConnectionPacket = new Packet_ConnectionPacket { LoginToken = loginToken }
        });
    }

    public void SendPositionTcp(Packet_EntityPosition position) => Send(new Packet_Client
    {
        Id = 35,
        UdpPacket = new Packet_UdpPacket { Id = 2, EntityPosition = position }
    });

    public void SendPositionUdp(Packet_EntityPosition position)
    {
        ThrowIfDisposed();
        (_udp ?? throw new InvalidOperationException("UDP is not connected.")).Send(
            new Packet_UdpPacket { Id = 2, EntityPosition = position });
    }

    public IReadOnlyList<Packet_UdpPacket> DrainUdpPackets()
    {
        ThrowIfDisposed();
        List<Packet_UdpPacket> packets = new();
        if (_udp is null) return packets;
        IEnumerable<Packet_UdpPacket>? received = _udp.ReadMessage();
        if (received is null) return packets;
        foreach (Packet_UdpPacket packet in received) packets.Add(packet);
        return packets;
    }

    public static Packet_EntityPosition Position(long entityId, double x, double y, double z, int tick, int controls)
    {
        return new Packet_EntityPosition
        {
            EntityId = entityId,
            X = CollectibleNet.SerializeDoublePrecise(x),
            Y = CollectibleNet.SerializeDoublePrecise(y),
            Z = CollectibleNet.SerializeDoublePrecise(z),
            Controls = controls,
            Tick = tick,
            PositionVersion = 0,
            MotionX = 0,
            MotionY = 0,
            MotionZ = 0
        };
    }

    private void Send(Packet_Client packet)
    {
        ThrowIfDisposed();
        CitoMemoryStream stream = new();
        packet.SerializeTo(stream);
        byte[] exactPacket = new byte[stream.Position()];
        Array.Copy(stream.GetBuffer(), exactPacket, exactPacket.Length);
        _tcp.Send(exactPacket);
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try
        {
            _tcp.Send(Packet_ClientSerializer.SerializeToBytes(new Packet_Client { Id = 14, Leave = new Packet_ClientLeave() }));
        }
        catch
        {
            // The peer may already have closed during test failure cleanup.
        }
        _udp?.Dispose();
        _tcp.Dispose();
    }
}
