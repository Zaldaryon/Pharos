using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.Common;
using Vintagestory.Server;
using Zaldaryon.Pharos.Inspection;
using Zaldaryon.Pharos.Performance;

namespace Zaldaryon.Pharos.Server;

/// <summary>
/// The measurement behind <see cref="ServerProfileDriver"/>: it owns the per-tick samples, resolves
/// what owns a listener, and is the switch the hooks check before they do anything.
/// </summary>
/// <remarks>
/// Everything here runs on the server's game thread. When <see cref="Active"/> is null the hooks
/// call the game's method directly, so a server that is not being profiled pays one null check.
/// </remarks>
internal sealed class ServerProfiler
{
    private static ServerProfiler? s_active;

    private static readonly object s_openLock = new();
    private static bool s_open;

    private readonly EmbeddedServerHost _host;
    private readonly int _gameThreadId;
    private readonly int _capacity;
    private readonly double[] _tickMs;
    private readonly Dictionary<string, Series> _systems = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Series> _mods = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Series> _blockEntities = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Series> _entities = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Series> _behaviors = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Series> _listeners = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Series> _aiTasks = new(StringComparer.Ordinal);
    private readonly Dictionary<Type, string> _aiTaskCodes = [];
    private readonly Dictionary<string, PlayerCounters> _players = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ChannelCounters> _channels = new(StringComparer.Ordinal);
    private readonly ConditionalWeakTable<object, ListenerOwner> _owners = new();
    private readonly System.Collections.Concurrent.ConcurrentQueue<NetworkEvent> _network = new();
    private readonly ModOwners _modOwners;
    private readonly IServerWorldAccessor _world;
    private readonly IReadOnlyDictionary<int, string> _channelNames;

    private int _measure = -1;
    private bool _tickOpen;
    private int _measuredTicks;
    private long _tickStart;

    private ServerProfiler(EmbeddedServerHost host, int ticks, ModOwners modOwners,
        IReadOnlyDictionary<int, string> channelNames)
    {
        _host = host;
        _gameThreadId = Environment.CurrentManagedThreadId;
        _capacity = ticks;
        _tickMs = new double[ticks];
        _modOwners = modOwners;
        _world = host.Api.World;
        _channelNames = channelNames;
    }

    /// <summary>
    /// The profiler of the measurement that is open, or null. The network hooks read it on the
    /// packet parser thread, so it is process-wide, not per thread.
    /// </summary>
    internal static ServerProfiler? Active => Volatile.Read(ref s_active);

    /// <summary>Whether the calling thread is the server's game thread, where the samples are kept.</summary>
    internal bool IsGameThread => Environment.CurrentManagedThreadId == _gameThreadId;

    /// <summary>Opens a measurement on the game thread; call from there.</summary>
    internal static ServerProfiler Open(EmbeddedServerHost host, int ticks)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(ticks);
        lock (s_openLock)
        {
            if (s_open) throw new InvalidOperationException("A server profile is already being measured.");
            ModOwners owners = ModOwners.Of(host.Api);
            ServerProfiler profiler = new(host, ticks, owners, ChannelNames(host.Api));
            ServerProfileHooks.Install(host);
            s_open = true;
            Volatile.Write(ref s_active, profiler);
            return profiler;
        }
    }

    /// <summary>Closes the measurement and builds the profile; call on the game thread.</summary>
    internal ServerProfile Close()
    {
        ServerProfileHooks.Remove();
        Volatile.Write(ref s_active, null);
        lock (s_openLock) s_open = false;
        DrainNetwork();

        double[] ticks = _tickMs[.._measuredTicks];
        TimingStats times = TimingStats.From(ticks.Select(ms => TimeSpan.FromMilliseconds(ms)));
        return new ServerProfile(_measuredTicks, times,
            Entries(_systems), Entries(_mods), Entries(_blockEntities), Entries(_entities),
            Entries(_behaviors), Entries(_listeners), AiTasks(), Bandwidth());
    }

    /// <summary>Closes a measurement the test abandoned, keeping the hooks off.</summary>
    internal void Abort()
    {
        ServerProfileHooks.Remove();
        Volatile.Write(ref s_active, null);
        lock (s_openLock) s_open = false;
        while (_network.TryDequeue(out _))
        {
        }
    }

    // ---- ticks -------------------------------------------------------------------------------

    internal void TickStarting(bool suspended)
    {
        if (suspended || _measuredTicks >= _capacity) return;
        _measure = _measuredTicks;
        _tickOpen = true;
        _tickStart = Stopwatch.GetTimestamp();
    }

    internal void TickFinished()
    {
        if (!_tickOpen) return;
        _tickOpen = false;
        _tickMs[_measure] = Stopwatch.GetElapsedTime(_tickStart).TotalMilliseconds;
        _measuredTicks++;
        DrainNetwork();
    }

    // ---- attribution -------------------------------------------------------------------------

    internal void AddSystem(string name, double ms) => Add(_systems, name, ms);

    internal void AddListener(object listener, double ms)
    {
        ListenerOwner owner = _owners.GetValue(listener, ResolveOwner);
        Add(_listeners, owner.Handler, ms);
        if (owner.Mod != null) Add(_mods, owner.Mod, ms);
        if (owner.BlockEntityType != null) Add(_blockEntities, owner.BlockEntityType, ms, owner.BlockEntityInstance);
    }

    internal void AddCallback(Delegate handler, BlockPos? pos, double ms)
    {
        ListenerOwner owner = ResolveOwner(handler, pos);
        Add(_listeners, owner.Handler, ms);
        if (owner.Mod != null) Add(_mods, owner.Mod, ms);
        if (owner.BlockEntityType != null) Add(_blockEntities, owner.BlockEntityType, ms, owner.BlockEntityInstance);
    }

    internal void AddEntity(Entity entity, double ms) =>
        Add(_entities, entity.Code?.ToString() ?? entity.GetType().Name, ms, entity.EntityId.ToString());

    internal void AddBehavior(EntityBehavior behavior, double ms)
    {
        string code = behavior.entity?.Code?.ToString() ?? behavior.entity?.GetType().Name ?? "unknown";
        Add(_behaviors, $"{code}/{behavior.GetType().Name}", ms);
    }

    internal void AddAiTask(object task, double ms) => Add(_aiTasks, AiTaskCodes.CodeOf(task, _aiTaskCodes), ms);

    internal void LearnAiTask(Type type) => _aiTaskCodes.TryAdd(type, AiTaskCodes.Resolve(type));

    private ListenerOwner ResolveOwner(object listener)
    {
        Delegate? handler = TickItems.HandlerOf(listener);
        BlockPos? pos = TickItems.PosOf(listener);
        return ResolveOwner(handler, pos);
    }

    private ListenerOwner ResolveOwner(Delegate? handler, BlockPos? pos)
    {
        string description = ModOwners.Describe(handler);
        string? mod = _modOwners.OfDelegate(handler);
        string? ownerType = TickItems.OwnerTypeOf(handler);
        string? blockEntityType = null;
        string? instance = null;
        if (handler?.Target is BlockEntity blockEntity)
        {
            blockEntityType = blockEntity.GetType().Name;
            instance = $"{blockEntity.Pos.X},{blockEntity.Pos.InternalY},{blockEntity.Pos.Z}";
        }
        else if (pos != null)
        {
            BlockEntity? atPosition = _world.BlockAccessor.GetBlockEntity(pos);
            if (atPosition != null)
            {
                blockEntityType = atPosition.GetType().Name;
                instance = $"{pos.X},{pos.InternalY},{pos.Z}";
            }
        }

        return new ListenerOwner(description, mod, blockEntityType, instance);
    }

    // ---- network ------------------------------------------------------------------------------

    internal void SentBytes(int clientId, int bytes) =>
        _network.Enqueue(new NetworkEvent(NetworkEventKind.SentBytes, clientId, null, bytes, 0, 0, 0));

    internal void SentPacket(ConnectedClient client, Packet_Server packet) =>
        _network.Enqueue(new NetworkEvent(NetworkEventKind.SentPacket, client.Id, null, packet.Id,
            packet.CustomPacket?.MessageId ?? 0, packet.CustomPacket?.ChannelId ?? 0,
            packet.CustomPacket?.Data?.Length ?? 0));

    internal void SentBoxed(ConnectedClient client, int bytes) =>
        _network.Enqueue(new NetworkEvent(NetworkEventKind.SentBytes, client.Id, null, bytes, 0, 0, 0));

    internal void ReceivedMessage(ConnectedClient client, int bytes) =>
        _network.Enqueue(new NetworkEvent(NetworkEventKind.ReceivedBytes, client.Id, null, bytes, 0, 0, 0));

    internal void ReceivedPacket(ConnectedClient client, int id) =>
        _network.Enqueue(new NetworkEvent(NetworkEventKind.ReceivedPacket, client.Id, null, id, 0, 0, 0));

    internal void ReceivedChannel(IServerPlayer player, Packet_CustomPacket packet) =>
        _network.Enqueue(new NetworkEvent(NetworkEventKind.ReceivedChannel, player.ClientId, player.PlayerName,
            packet.Data?.Length ?? 0, packet.MessageId, packet.ChannelId, packet.Data?.Length ?? 0));

    // The packet parser runs on its own thread, so every network hook queues what it saw and the
    // game thread applies it at the end of the tick. Numbers only: no game object crosses threads.
    private void DrainNetwork()
    {
        while (_network.TryDequeue(out NetworkEvent received))
        {
            PlayerCounters counters = Player(received.ClientId);
            switch (received.Kind)
            {
                case NetworkEventKind.SentBytes:
                case NetworkEventKind.ReceivedBytes:
                    counters.BytesSent += received.Kind == NetworkEventKind.SentBytes ? received.Value : 0;
                    counters.BytesReceived += received.Kind == NetworkEventKind.ReceivedBytes ? received.Value : 0;
                    counters.PacketsSent += received.Kind == NetworkEventKind.SentBytes ? 1 : 0;
                    counters.PacketsReceived += received.Kind == NetworkEventKind.ReceivedBytes ? 1 : 0;
                    break;
                case NetworkEventKind.SentPacket:
                    counters.SentById[received.Value] = counters.SentById.GetValueOrDefault(received.Value) + 1;
                    if (received.ChannelId != 0 || received.MessageId != 0 || received.Bytes != 0)
                    {
                        AddChannel(counters, received, sent: true);
                    }

                    break;
                case NetworkEventKind.ReceivedPacket:
                    counters.ReceivedById[received.Value] = counters.ReceivedById.GetValueOrDefault(received.Value) + 1;
                    break;
                case NetworkEventKind.ReceivedChannel:
                    AddChannel(counters, received, sent: false);
                    break;
            }
        }
    }

    private void AddChannel(PlayerCounters player, NetworkEvent received, bool sent)
    {
        string channel = ChannelName(received.ChannelId);
        ChannelCounters perPlayer = Channel(player, channel);
        ChannelCounters overall = Channel(_channels, channel);
        if (sent)
        {
            perPlayer.BytesSent += received.Bytes;
            perPlayer.PacketsSent++;
            perPlayer.MessagesSent[received.MessageId] =
                perPlayer.MessagesSent.GetValueOrDefault(received.MessageId) + 1;
            overall.BytesSent += received.Bytes;
            overall.PacketsSent++;
            overall.MessagesSent[received.MessageId] =
                overall.MessagesSent.GetValueOrDefault(received.MessageId) + 1;
        }
        else
        {
            perPlayer.BytesReceived += received.Bytes;
            perPlayer.PacketsReceived++;
            perPlayer.MessagesReceived[received.MessageId] =
                perPlayer.MessagesReceived.GetValueOrDefault(received.MessageId) + 1;
            overall.BytesReceived += received.Bytes;
            overall.PacketsReceived++;
            overall.MessagesReceived[received.MessageId] =
                overall.MessagesReceived.GetValueOrDefault(received.MessageId) + 1;
        }
    }

    private enum NetworkEventKind
    {
        SentBytes,
        SentPacket,
        ReceivedBytes,
        ReceivedPacket,
        ReceivedChannel,
    }

    private readonly record struct NetworkEvent(NetworkEventKind Kind, int ClientId, string? PlayerName,
        int Value, int MessageId, int ChannelId, int Bytes);

    private PlayerCounters Player(int clientId)
    {
        ConnectedClient? client = _host.Server.Clients.TryGetValue(clientId, out ConnectedClient? found) ? found : null;
        string name = client?.PlayerName is { Length: > 0 } playerName ? playerName : $"client-{clientId}";
        return GetPlayer(name);
    }

    private PlayerCounters GetPlayer(string name)
    {
        if (!_players.TryGetValue(name, out PlayerCounters? counters))
        {
            _players[name] = counters = new PlayerCounters(name);
        }

        return counters;
    }

    private static ChannelCounters Channel(Dictionary<string, ChannelCounters> channels, string name)
    {
        if (!channels.TryGetValue(name, out ChannelCounters? counters))
        {
            channels[name] = counters = new ChannelCounters(name);
        }

        return counters;
    }

    private static ChannelCounters Channel(PlayerCounters player, string name) => Channel(player.Channels, name);

    private string ChannelName(int id) => _channelNames.TryGetValue(id, out string? name) ? name : $"channel-{id}";

    // ---- results ------------------------------------------------------------------------------

    private void Add(Dictionary<string, Series> series, string name, double ms, string? instance = null)
    {
        if (_measure < 0) return;
        if (!series.TryGetValue(name, out Series? entry))
        {
            series[name] = entry = new Series(_capacity);
        }

        entry.Calls++;
        entry.PerTick[_measure] += ms;
        if (instance != null) entry.Instances.Add(instance);
    }

    private IReadOnlyDictionary<string, ServerProfileEntry> Entries(Dictionary<string, Series> series)
    {
        SortedDictionary<string, ServerProfileEntry> entries = new(StringComparer.Ordinal);
        foreach ((string name, Series entry) in series)
        {
            entries[name] = entry.Entry(name, _measuredTicks);
        }

        return entries;
    }

    private IReadOnlyList<AiTaskProfile> AiTasks()
    {
        List<AiTaskProfile> tasks = [];
        foreach ((string code, Series entry) in _aiTasks)
        {
            if (entry.Calls == 0) continue;
            tasks.Add(new AiTaskProfile(code, entry.Calls, entry.Slice(_measuredTicks), entry.P95(_measuredTicks)));
        }

        tasks.Sort((left, right) => right.TotalMs.CompareTo(left.TotalMs));
        return tasks;
    }

    private BandwidthProfile Bandwidth()
    {
        SortedDictionary<string, PlayerTraffic> players = new(StringComparer.Ordinal);
        long sent = 0, sentPackets = 0, received = 0, receivedPackets = 0;
        foreach ((string name, PlayerCounters counters) in _players)
        {
            SortedDictionary<string, ChannelTraffic> channels = new(StringComparer.Ordinal);
            foreach ((string channel, ChannelCounters traffic) in counters.Channels)
            {
                channels[channel] = traffic.ToTraffic();
            }

            players[name] = new PlayerTraffic(name, counters.BytesSent, counters.PacketsSent,
                counters.BytesReceived, counters.PacketsReceived,
                new Dictionary<int, long>(counters.SentById), new Dictionary<int, long>(counters.ReceivedById), channels);
            sent += counters.BytesSent;
            sentPackets += counters.PacketsSent;
            received += counters.BytesReceived;
            receivedPackets += counters.PacketsReceived;
        }

        Dictionary<string, SortedDictionary<string, long>> sentPerPlayer = new(StringComparer.Ordinal);
        Dictionary<string, SortedDictionary<string, long>> receivedPerPlayer = new(StringComparer.Ordinal);
        foreach ((string playerName, PlayerCounters counters) in _players)
        {
            foreach ((string channel, ChannelCounters traffic) in counters.Channels)
            {
                if (!sentPerPlayer.TryGetValue(channel, out SortedDictionary<string, long>? sentForPlayer))
                {
                    sentPerPlayer[channel] = sentForPlayer = new SortedDictionary<string, long>(StringComparer.Ordinal);
                }

                sentForPlayer[playerName] = traffic.BytesSent;
                if (!receivedPerPlayer.TryGetValue(channel, out SortedDictionary<string, long>? receivedFromPlayer))
                {
                    receivedPerPlayer[channel] =
                        receivedFromPlayer = new SortedDictionary<string, long>(StringComparer.Ordinal);
                }

                receivedFromPlayer[playerName] = traffic.BytesReceived;
            }
        }

        SortedDictionary<string, ChannelTraffic> allChannels = new(StringComparer.Ordinal);
        foreach ((string channel, ChannelCounters traffic) in _channels)
        {
            allChannels[channel] = traffic.ToTraffic(sentPerPlayer.GetValueOrDefault(channel),
                receivedPerPlayer.GetValueOrDefault(channel));
        }

        return new BandwidthProfile(players, allChannels, sent, sentPackets, received, receivedPackets);
    }

    private static IReadOnlyDictionary<int, string> ChannelNames(ICoreServerAPI api)
    {
        Dictionary<int, string> names = [];
        try
        {
            FieldInfo? field = api.Network?.GetType().GetField("channels", BindingFlags.Instance | BindingFlags.NonPublic);
            if (field?.GetValue(api.Network) is IDictionary<int, NetworkChannel> channels)
            {
                foreach ((int id, NetworkChannel channel) in channels)
                {
                    names[id] = channel.ChannelName;
                }
            }
        }
        catch (Exception ex) when (ex is TargetInvocationException or MissingFieldException or InvalidCastException)
        {
            // Without the names the channels keep their ids; the numbers are unaffected.
        }

        return names;
    }

    private sealed record ListenerOwner(string Handler, string? Mod, string? BlockEntityType, string? BlockEntityInstance);

    private sealed class Series(int capacity)
    {
        public readonly double[] PerTick = new double[capacity];
        public readonly HashSet<string> Instances = new(StringComparer.Ordinal);
        public long Calls;

        public double P95(int ticks) => Percentile.Rank(PerTick, ticks, 0.95);

        public ServerProfileEntry Entry(string name, int ticks) =>
            new(name, Calls, Slice(ticks), P95(ticks), Instances.Count);

        public double[] Slice(int ticks) => PerTick[..ticks];
    }

    private sealed class PlayerCounters(string name)
    {
        public string Name { get; } = name;
        public long BytesSent;
        public long PacketsSent;
        public long BytesReceived;
        public long PacketsReceived;
        public Dictionary<int, long> SentById { get; } = [];
        public Dictionary<int, long> ReceivedById { get; } = [];
        public Dictionary<string, ChannelCounters> Channels { get; } = new(StringComparer.Ordinal);
    }

    private sealed class ChannelCounters(string name)
    {
        public string Name { get; } = name;
        public long BytesSent;
        public long PacketsSent;
        public long BytesReceived;
        public long PacketsReceived;
        public Dictionary<int, long> MessagesSent { get; } = [];
        public Dictionary<int, long> MessagesReceived { get; } = [];

        public ChannelTraffic ToTraffic(IReadOnlyDictionary<string, long>? bytesSentPerPlayer = null,
            IReadOnlyDictionary<string, long>? bytesReceivedPerPlayer = null) =>
            new(Name, BytesSent, PacketsSent, BytesReceived, PacketsReceived,
                new Dictionary<int, long>(MessagesSent), new Dictionary<int, long>(MessagesReceived),
                bytesSentPerPlayer, bytesReceivedPerPlayer);
    }
}

/// <summary>Nearest-rank percentiles over a per-tick sample array.</summary>
internal static class Percentile
{
    /// <summary>The <paramref name="percentile"/> of the first <paramref name="count"/> values, counting zeros.</summary>
    public static double Rank(double[] samples, int count, double percentile)
    {
        if (count <= 0) return 0;
        double[] sorted = samples[..count];
        Array.Sort(sorted);
        int index = Math.Clamp((int)Math.Ceiling(percentile * count) - 1, 0, count - 1);
        return sorted[index];
    }
}
