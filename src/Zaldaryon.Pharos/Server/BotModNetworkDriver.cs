using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using ProtoBuf;
using Vintagestory.Common;
using Vintagestory.Common.Network.Packets;
using Vintagestory.Server;
using Zaldaryon.Pharos.Network;
using Zaldaryon.Pharos.XUnit;

namespace Zaldaryon.Pharos.Server;

/// <summary>Sends protobuf look-alikes through a headless player's own client connection and the real server handlers.</summary>
public sealed class BotModNetworkDriver
{
    private readonly ServerTestPlayer _player;
    // 0 is idle, a positive value counts QueueSend calls in progress, and -1 is a SendAsync in progress.
    private int _state;
    internal BotModNetworkDriver(ServerTestPlayer player) => _player = player;

    /// <summary>Queues one message without ticking. Use inside SameTick for a TCP race or tick the host yourself.</summary>
    /// <exception cref="InvalidOperationException">A SendAsync of this bot is still in progress.</exception>
    public void QueueSend<T>(string channel, T message, string? messageType = null, bool? udp = null)
    {
        for (int state = Volatile.Read(ref _state); ; state = Volatile.Read(ref _state))
        {
            if (state < 0) throw InProgress();
            if (Interlocked.CompareExchange(ref _state, state + 1, state) == state) break;
        }
        try
        {
            Submit(Prepare(channel, message, messageType, udp));
        }
        finally
        {
            Interlocked.Decrement(ref _state);
        }
    }

    /// <summary>
    /// Sends a message and advances up to maxTicks until its server channel observes it.
    /// This preserves the bot's latency, filtering and UDP-loss settings. A dropped message times out.
    /// The bot's earlier packets are handled or dropped first, so an identical earlier message cannot complete this send.
    /// One bot runs one SendAsync at a time and not during its own QueueSend; overlapping calls throw.
    /// </summary>
    public async Task SendAsync<T>(string channel, T message, string? messageType = null, bool? udp = null,
        int maxTicks = 2000, CancellationToken ct = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxTicks);
        ct.ThrowIfCancellationRequested();
        var host = _player.RequireBotHost();
        host.RequireOutsideBatch();
        if (Interlocked.CompareExchange(ref _state, -1, 0) != 0) throw InProgress();
        try
        {
            // The payload is serialized now: changing the message after the call does not change what is sent or awaited.
            var prepared = Prepare(channel, message, messageType, udp);
            int tick = 0;
            for (; !IsIdle(host); tick++)
            {
                if (tick == maxTicks) throw Timeout<T>(channel, maxTicks);
                ct.ThrowIfCancellationRequested();
                host.Tick();
                await Task.Delay(1, ct).ConfigureAwait(false);
            }

            using var log = host.ModNetwork.Record(channel);
            Submit(prepared);
            for (; tick < maxTicks; tick++)
            {
                ct.ThrowIfCancellationRequested();
                host.Tick();
                if (log.Received<T>(channel, messageType).Any(m => Matches(m.Envelope, _player.PlayerUID, prepared))) return;
                await Task.Delay(1, ct).ConfigureAwait(false);
            }
            throw Timeout<T>(channel, maxTicks);
        }
        finally
        {
            Volatile.Write(ref _state, 0);
        }
    }

    internal sealed record Prepared(bool Udp, int ChannelId, int MessageId, byte[] Data);

    internal static bool Matches(ServerModMessage message, string playerUid, Prepared sent) =>
        message.Direction == ModMessageDirection.Received && message.Udp == sent.Udp && message.MessageId == sent.MessageId &&
        message.PlayerUID == playerUid && message.Data.AsSpan().SequenceEqual(sent.Data);

    private Prepared Prepare<T>(string channel, T message, string? messageType, bool? udp)
    {
        ArgumentNullException.ThrowIfNull(message);
        // Serializing on the game thread lets a message's getters read server state.
        return _player.RunBotOnGameThread(() =>
        {
            var target = _player.RequireBotHost().ModNetwork.Channel(channel, udp);
            int id = ModNetworkDriver.MessageId(target, typeof(T), messageType);
            using MemoryStream stream = new();
            Serializer.Serialize(stream, message);
            return new Prepared(target is UdpNetworkChannel, ModChannels.Id(target), id, stream.ToArray());
        });
    }

    private void Submit(Prepared prepared) => _player.RunBotOnGameThread(() =>
    {
        _player.RequireBotHost();
        if (!_player.IsConnected) throw new InvalidOperationException("The bot is disconnected.");
        var custom = new Packet_CustomPacket { ChannelId = prepared.ChannelId, MessageId = prepared.MessageId };
        custom.SetData(prepared.Data.ToArray());
        var packet = prepared.Udp
            ? new Packet_Client { Id = 35, UdpPacket = new Packet_UdpPacket { Id = 6, ChannelPacket = custom } }
            : new Packet_Client { Id = 23, CustomPacket = custom };
        _player.RequireBotConnection().SendClientPacket(packet);
    });

    // Nothing of this bot is in the link, the parser, the engine's queue of parsed client packets or its UDP ingress.
    private bool IsIdle(EmbeddedServerHost host) => _player.RunBotOnGameThread(() =>
    {
        int clientId = _player.Player?.ClientId ?? -1;
        return _player.RequireBotConnection().IsBatchReady && !ClientPackets(host.Server).Any(p => ClientOf(p)?.Id == clientId) &&
            !PendingUdp(host.Server.UdpSockets[1]).Any(p => p.Client?.Id == clientId);
    });

    // UDP-over-TCP waits in slot 1 until the engine's UDP tick listener reads it.
    private static IEnumerable<UdpPacket> PendingUdp(UNetServer? ingress) => ingress switch
    {
        null => [],
        HeadlessUdpNetServer headless => headless.Pending,
        Vintagestory.Server.Network.UdpNetServer listener => ServerPacketQueue(listener),
        _ => throw new NotSupportedException($"SendAsync cannot inspect the UDP server {ingress.GetType().Name}."),
    };

    private InvalidOperationException InProgress() =>
        new($"A SendAsync of {_player.PlayerUID} is in progress. Await it before sending another mod message from this bot.");

    private TimeoutException Timeout<T>(string channel, int maxTicks) =>
        new($"The server did not receive {typeof(T).Name} from {_player.PlayerUID} on '{channel}' within {maxTicks} ticks.");

    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "ClientPackets")]
    private static extern ref ConcurrentQueue<ReceivedClientPacket> ClientPackets(ServerMain server);

    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "serverPacketQueue")]
    private static extern ref ConcurrentQueue<UdpPacket> ServerPacketQueue(Vintagestory.Server.Network.UdpNetServer server);

    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "client")]
    private static extern ref ConnectedClient ClientOf(ReceivedClientPacket packet);
}
