using System.Collections.Concurrent;
using Zaldaryon.Pharos.Assertions;

namespace Zaldaryon.Pharos.Network;

/// <summary>
/// Traces network packets across a loopback boundary for verification in tests.
/// </summary>
/// <remarks>
/// <para>
/// PacketTracer provides a lightweight packet capture mechanism for verifying that
/// specific packet types are sent during client-server interactions. Unlike
/// <see cref="PacketRecorder"/> which captures full payloads for replay,
/// PacketTracer focuses on packet ID verification with minimal overhead.
/// </para>
/// <para>
/// Typical usage:
/// <code>
/// var tracer = new PacketTracer();
/// tracer.StartTracing();
/// // ... perform actions that should generate packets ...
/// tracer.StopTracing();
/// tracer.AssertPacketSent(33); // LoginTokenQuery
/// tracer.AssertPacketSent(1);  // ClientIdentification
/// </code>
/// </para>
/// </remarks>
public sealed class PacketTracer
{
    private readonly object _lock = new();
    private readonly ConcurrentBag<CapturedPacket> _clientOutbound = new();
    private readonly ConcurrentBag<CapturedPacket> _serverOutbound = new();
    private bool _tracing;
    private DateTime _startTime;

    /// <summary>
    /// Gets whether packet tracing is currently active.
    /// </summary>
    public bool IsTracing
    {
        get { lock (_lock) return _tracing; }
    }

    /// <summary>
    /// Gets the total number of packets captured during the current or last tracing session.
    /// </summary>
    public int CapturedPacketCount => _clientOutbound.Count + _serverOutbound.Count;

    /// <summary>
    /// Gets the number of client-to-server packets captured.
    /// </summary>
    public int ClientOutboundCount => _clientOutbound.Count;

    /// <summary>
    /// Gets the number of server-to-client packets captured.
    /// </summary>
    public int ServerOutboundCount => _serverOutbound.Count;

    /// <summary>
    /// Starts tracing packets. Clears any previously captured packets.
    /// </summary>
    public void StartTracing()
    {
        lock (_lock)
        {
            _clientOutbound.Clear();
            _serverOutbound.Clear();
            _startTime = DateTime.UtcNow;
            _tracing = true;
        }
    }

    /// <summary>
    /// Stops tracing packets.
    /// </summary>
    public void StopTracing()
    {
        lock (_lock)
        {
            _tracing = false;
        }
    }

    /// <summary>
    /// Records a client-to-server packet if tracing is active.
    /// </summary>
    /// <param name="packetId">The packet ID.</param>
    /// <param name="payloadSize">Optional payload size for diagnostics.</param>
    public void RecordClientOutbound(int packetId, int payloadSize = 0)
    {
        if (!_tracing) return;

        _clientOutbound.Add(new CapturedPacket(
            PacketId: packetId,
            Direction: PacketDirection.Outbound,
            Timestamp: DateTime.UtcNow,
            PayloadSize: payloadSize));
    }

    /// <summary>
    /// Records a server-to-client packet if tracing is active.
    /// </summary>
    /// <param name="packetId">The packet ID.</param>
    /// <param name="payloadSize">Optional payload size for diagnostics.</param>
    public void RecordServerOutbound(int packetId, int payloadSize = 0)
    {
        if (!_tracing) return;

        _serverOutbound.Add(new CapturedPacket(
            PacketId: packetId,
            Direction: PacketDirection.Inbound,
            Timestamp: DateTime.UtcNow,
            PayloadSize: payloadSize));
    }

    /// <summary>
    /// Records a packet in the specified direction if tracing is active.
    /// </summary>
    /// <param name="packetId">The packet ID.</param>
    /// <param name="direction">The packet direction.</param>
    /// <param name="payloadSize">Optional payload size for diagnostics.</param>
    public void RecordPacket(int packetId, PacketDirection direction, int payloadSize = 0)
    {
        if (!_tracing) return;

        var packet = new CapturedPacket(
            PacketId: packetId,
            Direction: direction,
            Timestamp: DateTime.UtcNow,
            PayloadSize: payloadSize);

        if (direction == PacketDirection.Outbound)
        {
            _clientOutbound.Add(packet);
        }
        else
        {
            _serverOutbound.Add(packet);
        }
    }

    /// <summary>
    /// Asserts that a packet with the specified ID was sent (in either direction).
    /// </summary>
    /// <param name="packetId">The packet ID to verify was sent.</param>
    /// <exception cref="PharosAssertException">No packet with the specified ID was captured.</exception>
    public void AssertPacketSent(int packetId)
    {
        bool found = _clientOutbound.Any(p => p.PacketId == packetId) ||
                     _serverOutbound.Any(p => p.PacketId == packetId);

        if (!found)
        {
            throw new PharosAssertException(
                $"Expected packet ID {packetId} to be sent, but it was not captured. " +
                $"Captured {CapturedPacketCount} packets total.");
        }
    }

    /// <summary>
    /// Asserts that a packet with the specified ID was sent from client to server.
    /// </summary>
    /// <param name="packetId">The packet ID to verify was sent.</param>
    /// <exception cref="PharosAssertException">No client-to-server packet with the specified ID was captured.</exception>
    public void AssertClientSentPacket(int packetId)
    {
        bool found = _clientOutbound.Any(p => p.PacketId == packetId);

        if (!found)
        {
            throw new PharosAssertException(
                $"Expected client to send packet ID {packetId}, but it was not captured. " +
                $"Captured {ClientOutboundCount} client outbound packets.");
        }
    }

    /// <summary>
    /// Asserts that a packet with the specified ID was sent from server to client.
    /// </summary>
    /// <param name="packetId">The packet ID to verify was sent.</param>
    /// <exception cref="PharosAssertException">No server-to-client packet with the specified ID was captured.</exception>
    public void AssertServerSentPacket(int packetId)
    {
        bool found = _serverOutbound.Any(p => p.PacketId == packetId);

        if (!found)
        {
            throw new PharosAssertException(
                $"Expected server to send packet ID {packetId}, but it was not captured. " +
                $"Captured {ServerOutboundCount} server outbound packets.");
        }
    }

    /// <summary>
    /// Asserts that a packet with the specified ID was NOT sent.
    /// </summary>
    /// <param name="packetId">The packet ID that should not have been sent.</param>
    /// <exception cref="PharosAssertException">A packet with the specified ID was captured.</exception>
    public void AssertPacketNotSent(int packetId)
    {
        bool found = _clientOutbound.Any(p => p.PacketId == packetId) ||
                     _serverOutbound.Any(p => p.PacketId == packetId);

        if (found)
        {
            throw new PharosAssertException(
                $"Expected packet ID {packetId} to NOT be sent, but it was captured.");
        }
    }

    /// <summary>
    /// Asserts that at least the specified number of packets were captured.
    /// </summary>
    /// <param name="minPackets">Minimum expected packet count.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="minPackets"/> is negative.</exception>
    /// <exception cref="PharosAssertException">Fewer packets were captured than expected.</exception>
    public void AssertMinPacketCount(int minPackets)
    {
        if (minPackets < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(minPackets), minPackets, "Minimum packet count cannot be negative.");
        }

        int captured = CapturedPacketCount;
        if (captured < minPackets)
        {
            throw new PharosAssertException(
                $"Expected at least {minPackets} packets to be captured, but only {captured} were captured.");
        }
    }

    /// <summary>
    /// Gets all captured packets from the current or last tracing session.
    /// </summary>
    /// <returns>A read-only list of captured packets, ordered by timestamp.</returns>
    public IReadOnlyList<CapturedPacket> GetCapturedPackets()
    {
        var all = _clientOutbound.Concat(_serverOutbound)
            .OrderBy(p => p.Timestamp)
            .ToList();

        return all.AsReadOnly();
    }

    /// <summary>
    /// Gets all captured packets with the specified ID.
    /// </summary>
    /// <param name="packetId">The packet ID to filter by.</param>
    /// <returns>A read-only list of captured packets with the specified ID.</returns>
    public IReadOnlyList<CapturedPacket> GetPacketsById(int packetId)
    {
        var matching = _clientOutbound.Concat(_serverOutbound)
            .Where(p => p.PacketId == packetId)
            .OrderBy(p => p.Timestamp)
            .ToList();

        return matching.AsReadOnly();
    }

    /// <summary>
    /// Gets all unique packet IDs that were captured.
    /// </summary>
    /// <returns>A set of unique packet IDs.</returns>
    public IReadOnlySet<int> GetUniquePacketIds()
    {
        var ids = _clientOutbound.Concat(_serverOutbound)
            .Select(p => p.PacketId)
            .ToHashSet();

        return ids;
    }

    /// <summary>
    /// Clears all captured packets without stopping tracing.
    /// </summary>
    public void Clear()
    {
        _clientOutbound.Clear();
        _serverOutbound.Clear();
    }

    /// <summary>
    /// Gets a summary of captured packet counts by ID.
    /// </summary>
    /// <returns>A dictionary mapping packet ID to count.</returns>
    public IReadOnlyDictionary<int, int> GetPacketCountsByType()
    {
        var counts = _clientOutbound.Concat(_serverOutbound)
            .GroupBy(p => p.PacketId)
            .ToDictionary(g => g.Key, g => g.Count());

        return counts;
    }
}

/// <summary>
/// Represents a packet captured by <see cref="PacketTracer"/>.
/// </summary>
/// <param name="PacketId">The packet ID.</param>
/// <param name="Direction">The packet direction.</param>
/// <param name="Timestamp">When the packet was captured.</param>
/// <param name="PayloadSize">Size of the packet payload in bytes.</param>
public sealed record CapturedPacket(
    int PacketId,
    PacketDirection Direction,
    DateTime Timestamp,
    int PayloadSize);
