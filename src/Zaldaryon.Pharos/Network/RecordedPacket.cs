namespace Zaldaryon.Pharos.Network;

/// <summary>
/// Represents a single recorded network packet with metadata for replay.
/// </summary>
/// <param name="PacketId">The numeric packet identifier.</param>
/// <param name="Direction">Whether the packet was inbound (server to client) or outbound (client to server).</param>
/// <param name="TimestampMs">Milliseconds since recording started when this packet was captured.</param>
/// <param name="PayloadBase64">Base64-encoded raw packet payload bytes.</param>
/// <param name="PacketTypeName">Optional packet type name for debugging.</param>
public sealed record RecordedPacket(
    int PacketId,
    PacketDirection Direction,
    long TimestampMs,
    string PayloadBase64,
    string? PacketTypeName = null)
{
    /// <summary>
    /// Decodes the payload from Base64 to raw bytes.
    /// </summary>
    public byte[] GetPayloadBytes() => Convert.FromBase64String(PayloadBase64);

    /// <summary>
    /// Creates a RecordedPacket from raw payload bytes.
    /// </summary>
    public static RecordedPacket FromBytes(
        int packetId,
        PacketDirection direction,
        long timestampMs,
        byte[] payload,
        string? packetTypeName = null)
    {
        ArgumentNullException.ThrowIfNull(payload);
        return new RecordedPacket(
            packetId,
            direction,
            timestampMs,
            Convert.ToBase64String(payload),
            packetTypeName);
    }
}
