using Vintagestory.API.Common;
using Vintagestory.Common;

namespace Zaldaryon.Pharos.Network;

/// <summary>
/// What a packet filter sees of one packet: its direction and transport, its packet id, and, for a mod's message, the channel
/// and the message id on it.
/// </summary>
/// <param name="Direction">Inbound, from the server, or outbound, to it.</param>
/// <param name="Udp">Whether the packet travels as a datagram.</param>
/// <param name="PacketId">The packet's id, or -1 when it does not decode.</param>
/// <param name="Channel">The name of the mod channel the packet carries, or null when it is not a mod message.</param>
/// <param name="MessageId">The message's id on that channel, or -1.</param>
/// <param name="Length">The packet's size in bytes.</param>
public sealed record NetworkPacketInfo(PacketDirection Direction, bool Udp, int PacketId, string? Channel, int MessageId, int Length);

/// <summary>Reads a packet's id and mod channel from its bytes, as the game's packet types lay them out.</summary>
internal static class PacketDescriber
{
    // The ids of the packets that carry a mod's message, in the game's enums.
    private const int ClientCustomPacket = 23;
    private const int ServerCustomPacket = 55;
    private const int UdpCustomPacket = 6;

    /// <summary>Describes a TCP packet. <paramref name="channelName"/> maps a channel id to its name, or null.</summary>
    public static NetworkPacketInfo Tcp(PacketDirection direction, byte[] data, System.Func<int, bool, string?> channelName)
    {
        try
        {
            if (direction == PacketDirection.Outbound)
            {
                Packet_Client packet = Packet_ClientSerializer.DeserializeBuffer(data, data.Length, new Packet_Client());
                return Custom(direction, udp: false, packet.Id, packet.Id == ClientCustomPacket ? packet.CustomPacket : null, channelName, data.Length);
            }

            Packet_Server server = Packet_ServerSerializer.DeserializeBuffer(data, data.Length, new Packet_Server());
            return Custom(direction, udp: false, server.Id, server.Id == ServerCustomPacket ? server.CustomPacket : null, channelName, data.Length);
        }
        catch (Exception)
        {
            return new NetworkPacketInfo(direction, Udp: false, PacketId: -1, Channel: null, MessageId: -1, Length: data.Length);
        }
    }

    /// <summary>Describes a datagram.</summary>
    public static NetworkPacketInfo Udp(PacketDirection direction, Packet_UdpPacket packet, System.Func<int, bool, string?> channelName) =>
        Custom(direction, udp: true, packet.Id, packet.Id == UdpCustomPacket ? packet.ChannelPacket : null, channelName, Packet_UdpPacketSerializer.GetSize(packet));

    private static NetworkPacketInfo Custom(PacketDirection direction, bool udp, int id, Packet_CustomPacket? custom,
        System.Func<int, bool, string?> channelName, int length)
    {
        if (custom is null) return new NetworkPacketInfo(direction, udp, id, null, -1, length);
        return new NetworkPacketInfo(direction, udp, id, channelName(custom.ChannelId, udp), custom.MessageId, length);
    }
}
