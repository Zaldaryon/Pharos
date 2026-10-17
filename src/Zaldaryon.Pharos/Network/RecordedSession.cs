namespace Zaldaryon.Pharos.Network;

/// <summary>
/// A recording of the packets of a session, loaded for a replay. It reads the format
/// <see cref="PacketRecorder"/> writes, including recordings made before frames and transports were kept.
/// </summary>
public sealed class RecordedSession
{
    private readonly IReadOnlyList<RecordedPacket> _packets;

    private RecordedSession(IReadOnlyList<RecordedPacket> packets)
    {
        _packets = packets;
        Validate(packets);
    }

    /// <summary>The recorded packets, in the order they were captured.</summary>
    public IReadOnlyList<RecordedPacket> Packets => _packets;

    /// <summary>Whether the packets carry frame numbers, which replay needs to keep their order.</summary>
    public bool HasFrames => _packets.Any(p => p.Frame >= 0);

    /// <summary>Loads a recording from a JSON file.</summary>
    /// <exception cref="InvalidOperationException">The frames of the recording go backwards.</exception>
    public static RecordedSession Load(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        return FromJson(File.ReadAllText(path));
    }

    /// <summary>Reads a recording from JSON text.</summary>
    /// <exception cref="InvalidOperationException">The frames of the recording go backwards.</exception>
    public static RecordedSession FromJson(string json)
    {
        ArgumentException.ThrowIfNullOrEmpty(json);
        return new RecordedSession(PacketRecorder.FromJsonString(json).GetRecordedPackets().ToArray());
    }

    /// <summary>Takes the packets a recorder holds.</summary>
    public static RecordedSession From(PacketRecorder recorder)
    {
        ArgumentNullException.ThrowIfNull(recorder);
        return new RecordedSession(recorder.GetRecordedPackets().ToArray());
    }

    private static void Validate(IReadOnlyList<RecordedPacket> packets)
    {
        long last = -1;
        foreach (RecordedPacket packet in packets)
        {
            if (packet.Frame < 0) continue;
            if (packet.Frame < last)
            {
                throw new InvalidOperationException($"The recording's frames go backwards at frame {packet.Frame}, after frame {last}.");
            }

            last = packet.Frame;
        }
    }
}
