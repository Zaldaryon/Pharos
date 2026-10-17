namespace Zaldaryon.Pharos.Network;

/// <summary>How a recorded packet travelled: the reliable stream, or the datagram socket.</summary>
public enum PacketTransport
{
    /// <summary>The reliable, ordered stream.</summary>
    Tcp,

    /// <summary>The datagram socket, which the engine reads once per game tick.</summary>
    Udp
}
