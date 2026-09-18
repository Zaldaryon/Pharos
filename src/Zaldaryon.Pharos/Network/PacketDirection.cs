namespace Zaldaryon.Pharos.Network;

/// <summary>
/// Direction of a recorded network packet.
/// </summary>
public enum PacketDirection
{
    /// <summary>Packet received from server.</summary>
    Inbound,

    /// <summary>Packet sent to server.</summary>
    Outbound
}
