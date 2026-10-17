using Vintagestory.API.Common.Entities;
using Vintagestory.Common;
using Zaldaryon.Pharos.Network;
using Zaldaryon.Pharos.XUnit;

namespace Zaldaryon.Pharos.Server;

/// <summary>
/// Sends a recording's client-to-server traffic into a player's connection, frame by frame, with the game
/// ticking after each frame. Reach it as <see cref="EmbeddedServerHost.Replay"/>.
/// </summary>
/// <remarks>
/// Positions are the datagrams of packet id 2. The replay wraps each one the way the client does, and the
/// server drops a position whose version is below the player's current one, so the version is rewritten
/// unless the options say otherwise. A move of more than 128 blocks from the last replayed position is
/// skipped, because the server refuses it.
/// </remarks>
public sealed class ServerReplay
{
    // The packet ids a real client sends during its handshake and when it leaves. Replaying them would
    // join the player a second time.
    private static readonly IReadOnlySet<int> s_handshakeIds = new HashSet<int> { 33, 1, 2, 11, 14, 26, 29, 34 };

    private const int PositionPacketId = 2;
    private const int ClientUdpWrapperId = 35;
    private const double MaxMoveBlocks = 128;

    private readonly EmbeddedServerHost _host;

    internal ServerReplay(EmbeddedServerHost host) => _host = host;

    /// <summary>Replays the recording as the player named <paramref name="asPlayer"/>, joining that player first when it is not connected.</summary>
    public async Task<TrafficReplayResult> ClientTrafficAsync(RecordedSession recording, string asPlayer, ReplayOptions? options = null, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(recording);
        ArgumentException.ThrowIfNullOrEmpty(asPlayer);

        ServerTestPlayer player = _host.TestPlayers.FirstOrDefault(p => p.PlayerName == asPlayer && p.IsConnected)
            ?? await _host.JoinPlayerAsync(asPlayer, ct: ct).ConfigureAwait(false);
        return await ClientTrafficAsync(recording, player, options, ct).ConfigureAwait(false);
    }

    /// <summary>Replays the recording's client-to-server traffic through <paramref name="asPlayer"/>'s connection.</summary>
    /// <exception cref="InvalidOperationException">The player is not connected.</exception>
    public async Task<TrafficReplayResult> ClientTrafficAsync(RecordedSession recording, ServerTestPlayer asPlayer, ReplayOptions? options = null, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(recording);
        ArgumentNullException.ThrowIfNull(asPlayer);
        options ??= new ReplayOptions();
        if (options.TicksPerFrame < 0) throw new ArgumentOutOfRangeException(nameof(options), options.TicksPerFrame, "The ticks per frame cannot be negative.");

        IReadOnlySet<int> skip = options.SkipPacketIds ?? (options.SkipHandshake ? s_handshakeIds : new HashSet<int>());
        List<List<RecordedPacket>> frames = GroupByFrame(recording);
        List<RecordedPacket> skippedPackets = [];
        int delivered = 0;
        int skipped = 0;

        if (options.TeleportToFirstPosition && FirstPosition(frames) is { } first)
        {
            await asPlayer.TeleportTo(first.X, first.Y, first.Z).ConfigureAwait(false);
        }

        double lastX = double.NaN;
        double lastY = double.NaN;
        double lastZ = double.NaN;

        foreach (List<RecordedPacket> frame in frames)
        {
            ct.ThrowIfCancellationRequested();
            _host.RunOnGameThread(() =>
            {
                HeadlessPlayerConnection connection = asPlayer.Connection
                    ?? throw new InvalidOperationException($"The player {asPlayer.PlayerName ?? asPlayer.PlayerUID} is not connected.");

                foreach (RecordedPacket packet in frame)
                {
                    byte[] bytes = packet.GetPayloadBytes();
                    if (packet.Transport == PacketTransport.Udp)
                    {
                        Packet_UdpPacket udp = Packet_UdpPacketSerializer.DeserializeBuffer(bytes, bytes.Length, new Packet_UdpPacket());
                        if (udp.Id == PositionPacketId && udp.EntityPosition is { } position)
                        {
                            double x = CollectibleNet.DeserializeDoublePrecise(position.X);
                            double y = CollectibleNet.DeserializeDoublePrecise(position.Y);
                            double z = CollectibleNet.DeserializeDoublePrecise(position.Z);
                            if (!double.IsNaN(lastX) && Distance(x, y, z, lastX, lastY, lastZ) > MaxMoveBlocks)
                            {
                                skipped++;
                                skippedPackets.Add(packet);
                                continue;
                            }

                            if (options.RewritePositionVersion)
                            {
                                position.PositionVersion = (int)(asPlayer.Entity?.WatchedAttributes.GetInt("positionVersionNumber") ?? 0);
                            }

                            lastX = x;
                            lastY = y;
                            lastZ = z;
                        }

                        connection.SendClientPacket(new Packet_Client { Id = ClientUdpWrapperId, UdpPacket = udp });
                        delivered++;
                        continue;
                    }

                    Packet_Client client = Packet_ClientSerializer.DeserializeBuffer(bytes, bytes.Length, new Packet_Client());
                    if (skip.Contains(client.Id))
                    {
                        skipped++;
                        skippedPackets.Add(packet);
                        continue;
                    }

                    connection.SendClientPacket(client);
                    delivered++;
                }

                for (int tick = 0; tick < options.TicksPerFrame; tick++) _host.Tick();
            });
        }

        return new TrafficReplayResult(delivered, skipped, frames.Count, 0, skippedPackets);
    }

    private static List<List<RecordedPacket>> GroupByFrame(RecordedSession recording)
    {
        List<RecordedPacket> outbound = recording.Packets.Where(p => p.Direction == PacketDirection.Outbound).ToList();
        if (!recording.HasFrames) return outbound.Select(p => new List<RecordedPacket> { p }).ToList();

        return outbound.GroupBy(p => p.Frame).OrderBy(g => g.Key).Select(g => g.ToList()).ToList();
    }

    private static (double X, double Y, double Z)? FirstPosition(List<List<RecordedPacket>> frames)
    {
        foreach (RecordedPacket packet in frames.SelectMany(f => f))
        {
            if (packet.Transport != PacketTransport.Udp) continue;
            byte[] bytes = packet.GetPayloadBytes();
            Packet_UdpPacket udp = Packet_UdpPacketSerializer.DeserializeBuffer(bytes, bytes.Length, new Packet_UdpPacket());
            if (udp.Id == PositionPacketId && udp.EntityPosition is { } position)
            {
                return (CollectibleNet.DeserializeDoublePrecise(position.X), CollectibleNet.DeserializeDoublePrecise(position.Y), CollectibleNet.DeserializeDoublePrecise(position.Z));
            }
        }

        return null;
    }

    private static double Distance(double ax, double ay, double az, double bx, double by, double bz) =>
        Math.Sqrt((ax - bx) * (ax - bx) + (ay - by) * (ay - by) + (az - bz) * (az - bz));
}
