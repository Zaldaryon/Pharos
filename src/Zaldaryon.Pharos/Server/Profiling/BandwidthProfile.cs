using System.Globalization;
using Zaldaryon.Pharos.Metrics;

namespace Zaldaryon.Pharos.Server;

/// <summary>Packets and payload bytes on one mod channel, for one player or for the whole server.</summary>
/// <remarks>
/// Bytes are the channel payloads the game's <c>NetworkChannel</c> carried, not the framed packets
/// on the wire, which also hold headers and are compressed above 5 KB. Message ids are the ids the
/// channel registered its message types under.
/// </remarks>
public sealed class ChannelTraffic
{
    internal ChannelTraffic(string name, long bytesSent, long packetsSent, long bytesReceived, long packetsReceived,
        IReadOnlyDictionary<int, long> messagesSent, IReadOnlyDictionary<int, long> messagesReceived,
        IReadOnlyDictionary<string, long>? bytesSentPerPlayer = null,
        IReadOnlyDictionary<string, long>? bytesReceivedPerPlayer = null)
    {
        Name = name;
        BytesSent = bytesSent;
        PacketsSent = packetsSent;
        BytesReceived = bytesReceived;
        PacketsReceived = packetsReceived;
        MessagesSent = messagesSent;
        MessagesReceived = messagesReceived;
        BytesSentPerPlayer = bytesSentPerPlayer ?? new Dictionary<string, long>(0);
        BytesReceivedPerPlayer = bytesReceivedPerPlayer ?? new Dictionary<string, long>(0);
    }

    /// <summary>The channel name the mod registered.</summary>
    public string Name { get; }

    /// <summary>The payload bytes sent on this channel.</summary>
    public long BytesSent { get; }

    /// <summary>The packets sent on this channel.</summary>
    public long PacketsSent { get; }

    /// <summary>The payload bytes received on this channel.</summary>
    public long BytesReceived { get; }

    /// <summary>The packets received on this channel.</summary>
    public long PacketsReceived { get; }

    /// <summary>Packets sent, by the message id the channel registered the type under.</summary>
    public IReadOnlyDictionary<int, long> MessagesSent { get; }

    /// <summary>Packets received, by message id.</summary>
    public IReadOnlyDictionary<int, long> MessagesReceived { get; }

    /// <summary>
    /// The bytes sent on this channel to each player, by player name, for a channel read from
    /// <see cref="BandwidthProfile.Channels"/> or <see cref="BandwidthProfile.Channel"/>. A
    /// per-player entry has only its own numbers and leaves this empty.
    /// </summary>
    public IReadOnlyDictionary<string, long> BytesSentPerPlayer { get; }

    /// <summary>The bytes received on this channel from each player, by player name.</summary>
    public IReadOnlyDictionary<string, long> BytesReceivedPerPlayer { get; }

    /// <inheritdoc/>
    public override string ToString() => string.Create(CultureInfo.InvariantCulture,
        $"{Name}: sent {PacketsSent} packets/{BytesSent} bytes, received {PacketsReceived} packets/{BytesReceived} bytes");
}

/// <summary>What the server sent to and received from one player in the measured run.</summary>
public sealed class PlayerTraffic
{
    internal PlayerTraffic(string name, long bytesSent, long packetsSent, long bytesReceived, long packetsReceived,
        IReadOnlyDictionary<int, long> packetsSentById, IReadOnlyDictionary<int, long> packetsReceivedById,
        IReadOnlyDictionary<string, ChannelTraffic> channels)
    {
        Name = name;
        BytesSent = bytesSent;
        PacketsSent = packetsSent;
        BytesReceived = bytesReceived;
        PacketsReceived = packetsReceived;
        PacketsSentById = packetsSentById;
        PacketsReceivedById = packetsReceivedById;
        Channels = channels;
    }

    /// <summary>The player's name, or <c>client-&lt;id&gt;</c> before it identifies.</summary>
    public string Name { get; }

    /// <summary>The bytes sent to this player, headers included.</summary>
    public long BytesSent { get; }

    /// <summary>The packets sent to this player.</summary>
    public long PacketsSent { get; }

    /// <summary>The bytes received from this player.</summary>
    public long BytesReceived { get; }

    /// <summary>The packets received from this player.</summary>
    public long PacketsReceived { get; }

    /// <summary>Packets sent, by the id of the game's packet.</summary>
    public IReadOnlyDictionary<int, long> PacketsSentById { get; }

    /// <summary>Packets received, by the id of the game's packet.</summary>
    public IReadOnlyDictionary<int, long> PacketsReceivedById { get; }

    /// <summary>The mod channels this player used.</summary>
    public IReadOnlyDictionary<string, ChannelTraffic> Channels { get; }

    /// <summary>The traffic on <paramref name="channel"/>, or an empty entry when it was not used.</summary>
    public ChannelTraffic Channel(string channel)
    {
        ArgumentNullException.ThrowIfNull(channel);
        return Channels.TryGetValue(channel, out ChannelTraffic? traffic) ? traffic : BandwidthProfile.Empty(channel);
    }

    /// <inheritdoc/>
    public override string ToString() => string.Create(CultureInfo.InvariantCulture,
        $"{Name}: sent {PacketsSent} packets/{BytesSent} bytes, received {PacketsReceived} packets/{BytesReceived} bytes");
}

/// <summary>The server's traffic per player, per packet id and per mod channel in a measured run.</summary>
/// <remarks>
/// Packet counts and byte totals come from the points where the server sends to and reads from a
/// client, so they cover every packet. The bytes of a mod channel are the channel payloads, which
/// is what a mod controls; the per-packet-id split is available for counts, not bytes, because the
/// id and the framed size are known at different points in the send path.
/// </remarks>
public sealed class BandwidthProfile
{
    private static readonly IReadOnlyDictionary<int, long> NoMessages = new Dictionary<int, long>(0);
    private static readonly IReadOnlyDictionary<string, ChannelTraffic> NoChannels = new Dictionary<string, ChannelTraffic>(0);

    internal BandwidthProfile(IReadOnlyDictionary<string, PlayerTraffic> players,
        IReadOnlyDictionary<string, ChannelTraffic> channels, long bytesSent, long packetsSent,
        long bytesReceived, long packetsReceived)
    {
        Players = players;
        Channels = channels;
        TotalBytesSent = bytesSent;
        TotalPacketsSent = packetsSent;
        TotalBytesReceived = bytesReceived;
        TotalPacketsReceived = packetsReceived;
    }

    /// <summary>Each player's traffic, by name.</summary>
    public IReadOnlyDictionary<string, PlayerTraffic> Players { get; }

    /// <summary>Each mod channel's traffic over every player, by channel name.</summary>
    public IReadOnlyDictionary<string, ChannelTraffic> Channels { get; }

    /// <summary>Every byte sent to every player.</summary>
    public long TotalBytesSent { get; }

    /// <summary>Every packet sent to every player.</summary>
    public long TotalPacketsSent { get; }

    /// <summary>Every byte received from every player, as the game read it.</summary>
    public long TotalBytesReceived { get; }

    /// <summary>Every packet received from every player.</summary>
    public long TotalPacketsReceived { get; }

    /// <summary>The traffic on <paramref name="channel"/> over every player, or an empty entry.</summary>
    public ChannelTraffic Channel(string channel)
    {
        ArgumentNullException.ThrowIfNull(channel);
        return Channels.TryGetValue(channel, out ChannelTraffic? traffic) ? traffic : Empty(channel);
    }

    internal static ChannelTraffic Empty(string name) =>
        new(name, 0, 0, 0, 0, NoMessages, NoMessages);

    internal void AddMetrics(List<MetricReading> readings)
    {
        readings.Add(new MetricReading("net.sent.bytes", TotalBytesSent, MetricUnit.Bytes,
            MetricDirection.LowerIsBetter, MetricClock.Exact));
        readings.Add(new MetricReading("net.sent.packets", TotalPacketsSent, MetricUnit.Count,
            MetricDirection.LowerIsBetter, MetricClock.Exact));
        readings.Add(new MetricReading("net.received.bytes", TotalBytesReceived, MetricUnit.Bytes,
            MetricDirection.LowerIsBetter, MetricClock.Exact));
        readings.Add(new MetricReading("net.received.packets", TotalPacketsReceived, MetricUnit.Count,
            MetricDirection.LowerIsBetter, MetricClock.Exact));

        foreach ((string player, PlayerTraffic traffic) in Players)
        {
            readings.Add(new MetricReading($"net.sent.{player}.bytes", traffic.BytesSent, MetricUnit.Bytes,
                MetricDirection.LowerIsBetter, MetricClock.Exact));
            readings.Add(new MetricReading($"net.sent.{player}.packets", traffic.PacketsSent, MetricUnit.Count,
                MetricDirection.LowerIsBetter, MetricClock.Exact));
            readings.Add(new MetricReading($"net.received.{player}.bytes", traffic.BytesReceived, MetricUnit.Bytes,
                MetricDirection.LowerIsBetter, MetricClock.Exact));
            readings.Add(new MetricReading($"net.received.{player}.packets", traffic.PacketsReceived, MetricUnit.Count,
                MetricDirection.LowerIsBetter, MetricClock.Exact));
            foreach ((int id, long count) in traffic.PacketsSentById)
            {
                readings.Add(new MetricReading($"net.sent.{player}.packet.{id}.packets", count, MetricUnit.Count,
                    MetricDirection.LowerIsBetter, MetricClock.Exact));
            }

            foreach ((int id, long count) in traffic.PacketsReceivedById)
            {
                readings.Add(new MetricReading($"net.received.{player}.packet.{id}.packets", count, MetricUnit.Count,
                    MetricDirection.LowerIsBetter, MetricClock.Exact));
            }

            foreach ((string channel, ChannelTraffic channelTraffic) in traffic.Channels)
            {
                readings.Add(new MetricReading($"net.sent.{player}.{channel}.bytes", channelTraffic.BytesSent,
                    MetricUnit.Bytes, MetricDirection.LowerIsBetter, MetricClock.Exact));
                readings.Add(new MetricReading($"net.sent.{player}.{channel}.packets", channelTraffic.PacketsSent,
                    MetricUnit.Count, MetricDirection.LowerIsBetter, MetricClock.Exact));
                readings.Add(new MetricReading($"net.received.{player}.{channel}.bytes", channelTraffic.BytesReceived,
                    MetricUnit.Bytes, MetricDirection.LowerIsBetter, MetricClock.Exact));
                readings.Add(new MetricReading($"net.received.{player}.{channel}.packets", channelTraffic.PacketsReceived,
                    MetricUnit.Count, MetricDirection.LowerIsBetter, MetricClock.Exact));
            }
        }
    }
}
