using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Vintagestory.API.Server;
using Vintagestory.Common;
using Vintagestory.Server;
using Zaldaryon.Pharos.Network;

namespace Zaldaryon.Pharos.Server;

/// <summary>Records all registered server mod channels, for real clients and headless bots alike.</summary>
public sealed class ServerModNetworkDriver
{
    private static readonly ConditionalWeakTable<ServerMain, ServerModNetworkDriver> s_drivers = new();
    private readonly EmbeddedServerHost _host;
    private readonly ConcurrentQueue<Pending> _pending = new();
    private readonly List<ServerModMessageLog> _logs = [];
    private readonly Queue<ServerModMessage> _history = new();
    private const int HistoryCapacity = 10_000;
    private long _sequence;
    private bool _historyOverflow;
    private volatile Exception? _failure;
    private int _pendingCount;

    internal ServerModNetworkDriver(EmbeddedServerHost host)
    {
        _host = host;
        s_drivers.Add(host.Server, this);
    }

    internal static ServerModNetworkDriver? Of(ServerMain server) =>
        s_drivers.TryGetValue(server, out var driver) ? driver : null;

    internal void Detach()
    {
        Flush();
        s_drivers.Remove(_host.Server);
        ChannelTypes[] registry;
        try { registry = Registry(); }
        catch (Exception ex) { _failure ??= ex; registry = []; }
        foreach (var log in _logs) log.Stop(registry, _failure);
        _logs.Clear();
    }

    /// <summary>Starts an independent recording of one channel or all channels.</summary>
    public ServerModMessageLog Record(string? channel = null, int capacity = HistoryCapacity)
    {
        if (channel != null) ArgumentException.ThrowIfNullOrWhiteSpace(channel);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);
        return Read(() =>
        {
            if (channel != null && !ChannelsCore().Any(c => c.ChannelName == channel))
                throw new ArgumentException($"No server channel '{channel}' is registered.", nameof(channel));
            var log = new ServerModMessageLog(this, channel, capacity);
            _logs.Add(log);
            return log;
        });
    }

    /// <summary>The newest server messages since Clear. Overflow is reported explicitly; scopes have their own capacity.</summary>
    public IReadOnlyList<ServerModMessage> Messages() => Read(() =>
    {
        if (_historyOverflow) throw new InvalidOperationException("Server mod history overflowed. Clear it or use a recording scope.");
        return (IReadOnlyList<ServerModMessage>)_history.Select(m => m.Copy()).ToArray();
    });

    /// <summary>Clears host history, not active scopes or the monotonic sequence.</summary>
    public void Clear() => _host.RunOnGameThread(() =>
    {
        Flush();
        _history.Clear();
        _historyOverflow = false;
        _failure = null;
    });

    /// <summary>Registered server channels and their message ids.</summary>
    public IReadOnlyList<ModChannelInfo> Channels() => Read(() => (IReadOnlyList<ModChannelInfo>)ChannelsCore()
        .Select(c => new ModChannelInfo(c.ChannelName, c is UdpNetworkChannel, true, ModChannels.Id(c),
            ModChannels.MessageTypes(c).ToDictionary(p => p.Value, p => p.Key.FullName ?? p.Key.Name))).ToArray());

    internal T Read<T>(Func<T> read) => _host.RunOnGameThread(() =>
    {
        Flush();
        if (_failure != null) throw new InvalidOperationException("Server mod recording failed.", _failure);
        return read();
    });

    internal void Remove(ServerModMessageLog log) => _host.RunOnGameThread(() =>
    {
        Flush();
        _logs.Remove(log);
        log.Stop(Registry(), _failure);
    });

    internal IEnumerable<NetworkChannel> ChannelsCore() =>
        TcpChannels((NetworkAPI)_host.Api.Network).Values.Concat(UdpChannels((NetworkAPI)_host.Api.Network).Values);

    internal NetworkChannel Channel(string name, bool? udp = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        var matches = ChannelsCore().Where(c => c.ChannelName == name && (udp == null || (c is UdpNetworkChannel) == udp)).ToArray();
        return matches.Length switch
        {
            1 => matches[0],
            0 => throw new ArgumentException($"No server channel '{name}' is registered.", nameof(name)),
            _ => throw new ArgumentException($"Both TCP and UDP channels are named '{name}'. Specify udp.", nameof(name)),
        };
    }

    internal HashSet<(string, bool, int)> MatchingTypes(Type type, string? channel, string? messageType) =>
        Read(() => Match(Registry(), type, channel, messageType));

    // A stopped scope decodes against the registrations captured when it stopped, not the live host.
    internal static HashSet<(string, bool, int)> Match(IEnumerable<ChannelTypes> registry, Type type, string? channel, string? messageType)
    {
        var result = new HashSet<(string, bool, int)>();
        foreach (var c in registry.Where(c => channel == null || c.Name == channel))
        {
            if (!c.Types.Keys.Any(t => t == type || t.FullName == (messageType ?? type.FullName) ||
                t.Name == (messageType ?? type.Name))) continue;
            result.Add((c.Name, c.Udp, ModNetworkDriver.MessageId(c.Name, c.Types, type, messageType)));
        }
        if (result.Count == 0) throw new ArgumentException($"No registered server message matches {messageType ?? type.Name}.");
        return result;
    }

    private ChannelTypes[] Registry() => ChannelsCore()
        .Select(c => new ChannelTypes(c.ChannelName, c is UdpNetworkChannel, new Dictionary<Type, int>(ModChannels.MessageTypes(c)))).ToArray();

    internal void Observe(int clientId, IServerPlayer? player, Packet_CustomPacket packet, bool udp, ModMessageDirection direction)
    {
        try
        {
            if (Interlocked.Increment(ref _pendingCount) > HistoryCapacity)
            {
                Interlocked.Decrement(ref _pendingCount);
                throw new InvalidOperationException("Pending server mod observations overflowed before the game thread could collect them.");
            }
            _pending.Enqueue(new Pending(clientId, player, packet.ChannelId, packet.MessageId, udp, direction, packet.Data?.ToArray() ?? []));
            // Worker hooks never read live positions. Their events are collected on the next game-thread boundary.
            if (_host.IsOnGameThread) Flush();
        }
        catch (Exception ex) { _failure = ex; }
    }

    internal void Flush()
    {
        while (_pending.TryDequeue(out var pending))
        {
            Interlocked.Decrement(ref _pendingCount);
            try
            {
                var channel = ChannelsCore().FirstOrDefault(c => (c is UdpNetworkChannel) == pending.Udp && ModChannels.Id(c) == pending.ChannelId)
                    ?? throw new InvalidOperationException($"Unknown server channel id {pending.ChannelId}.");
                // Retain the observed player reference: disconnect before collection must not lose its identity.
                var player = pending.Player;
                var message = new ServerModMessage(++_sequence, _host.TickCount, pending.Direction,
                    player?.PlayerUID, player?.PlayerName, player?.Entity?.Pos.XYZ.Clone(), channel.ChannelName,
                    pending.Udp, pending.MessageId, ModChannels.TypeOf(channel, pending.MessageId)?.FullName, pending.Data, pending.ClientId);
                if (_history.Count == HistoryCapacity) { _history.Dequeue(); _historyOverflow = true; }
                _history.Enqueue(message);
                foreach (var log in _logs) log.Add(message);
            }
            catch (Exception ex) { _failure = ex; }
        }
    }

    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "channels")]
    private static extern ref Vintagestory.API.Datastructures.OrderedDictionary<int, NetworkChannel> TcpChannels(NetworkAPI api);

    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "channelsUdp")]
    private static extern ref Vintagestory.API.Datastructures.OrderedDictionary<int, UdpNetworkChannel> UdpChannels(NetworkAPI api);

    internal static ServerMain ServerOf(NetworkChannel channel) => ServerField(ApiField(channel));

    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "api")]
    private static extern ref NetworkAPI ApiField(NetworkChannel channel);

    [UnsafeAccessor(UnsafeAccessorKind.Field, Name = "server")]
    private static extern ref ServerMain ServerField(NetworkAPI api);

    internal sealed record ChannelTypes(string Name, bool Udp, Dictionary<Type, int> Types);

    private sealed record Pending(int ClientId, IServerPlayer? Player, int ChannelId, int MessageId, bool Udp, ModMessageDirection Direction, byte[] Data);
}
