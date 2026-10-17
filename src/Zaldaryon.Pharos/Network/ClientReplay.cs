using Vintagestory.Common;
using Zaldaryon.Pharos.Core;

namespace Zaldaryon.Pharos.Network;

/// <summary>
/// Feeds a recording's server-to-client traffic into the engine-mode client's network processor, frame by
/// frame, as the server's packets would arrive. Reach it as <see cref="HeadlessClient.Replay"/>.
/// </summary>
/// <remarks>
/// While the replay runs the link holds the server's live packets, so they cannot race the recorded ones,
/// and it counts them in <see cref="TrafficReplayResult.LiveDiscarded"/>. A client that joined the server
/// has already received the asset packets and the world data, which the default skips leave out.
/// The client does not match the server after a replay, because the server kept running without it.
/// </remarks>
public sealed class ClientReplay
{
    // The server packets a joined client must not receive again: the asset push, the join and world-data
    // handshake, and the disconnect. Replaying them would reload the client.
    private static readonly IReadOnlySet<int> s_joinedSkipIds = new HashSet<int> { 77, 1, 2, 4, 5, 6, 19, 21, 30, 56, 73, 82 };

    private readonly HeadlessClient _client;

    internal ClientReplay(HeadlessClient client) => _client = client;

    /// <summary>Replays the recording's server-to-client packets into the client.</summary>
    /// <exception cref="NotSupportedException">The client runs in fixture mode, which does no network processing.</exception>
    public async Task<TrafficReplayResult> ServerTrafficAsync(RecordedSession recording, ReplayOptions? options = null, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(recording);
        options ??= new ReplayOptions();
        if (options.TicksPerFrame < 0) throw new ArgumentOutOfRangeException(nameof(options), options.TicksPerFrame, "The ticks per frame cannot be negative.");

        LoopbackLink link = _client.EngineLink ?? throw new NotSupportedException("The client runs in fixture mode, which does no network processing, so it cannot take replayed traffic.");
        IReadOnlySet<int> skip = options.SkipPacketIds ?? (options.SkipHandshake ? s_joinedSkipIds : new HashSet<int>());
        List<List<RecordedPacket>> frames = GroupByFrame(recording);
        List<RecordedPacket> skippedPackets = [];
        int delivered = 0;
        int skipped = 0;

        _client.RunOnClientThread(() => link.HoldServerTraffic = true);
        try
        {
            await _client.Frame(ct: ct).ConfigureAwait(false);
            foreach (List<RecordedPacket> frame in frames)
            {
                ct.ThrowIfCancellationRequested();
                _client.RunOnClientThread(() =>
                {
                    foreach (RecordedPacket packet in frame)
                    {
                        byte[] bytes = packet.GetPayloadBytes();
                        if (packet.Transport == PacketTransport.Udp)
                        {
                            _client.EngineUdp!.EnqueuePacket(Packet_UdpPacketSerializer.DeserializeBuffer(bytes, bytes.Length, new Packet_UdpPacket()));
                            delivered++;
                            continue;
                        }

                        if (skip.Contains(Packet_ServerSerializer.DeserializeBuffer(bytes, bytes.Length, new Packet_Server()).Id))
                        {
                            skipped++;
                            skippedPackets.Add(packet);
                            continue;
                        }

                        _client.Client.networkProc.TryReadPacket(bytes, bytes.Length);
                        delivered++;
                    }
                });

                for (int tick = 0; tick < Math.Max(1, options.TicksPerFrame); tick++)
                {
                    await _client.Frame(ct: ct).ConfigureAwait(false);
                }
            }
        }
        finally
        {
            _client.RunOnClientThread(() => link.HoldServerTraffic = false);
        }

        int liveDiscarded = _client.RunOnClientThread(() => link.LiveDiscarded);
        return new TrafficReplayResult(delivered, skipped, frames.Count, liveDiscarded, skippedPackets);
    }

    private static List<List<RecordedPacket>> GroupByFrame(RecordedSession recording)
    {
        List<RecordedPacket> inbound = recording.Packets.Where(p => p.Direction == PacketDirection.Inbound).ToList();
        if (!recording.HasFrames) return inbound.Select(p => new List<RecordedPacket> { p }).ToList();

        return inbound.GroupBy(p => p.Frame).OrderBy(g => g.Key).Select(g => g.ToList()).ToList();
    }
}
