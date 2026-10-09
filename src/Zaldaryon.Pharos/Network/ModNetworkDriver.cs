using ProtoBuf;
using Vintagestory.Client.NoObf;
using Zaldaryon.Pharos.Core;

namespace Zaldaryon.Pharos.Network;

/// <summary>A mod network channel the client registered.</summary>
/// <param name="Name">The channel's name.</param>
/// <param name="Udp">Whether it is a UDP channel.</param>
/// <param name="Connected">Whether the server knows it too; a channel can only send once it does.</param>
/// <param name="Id">The id it sends with: the server's for it once connected, 0 otherwise.</param>
/// <param name="MessageTypes">The full names of its message types, by message id.</param>
public sealed record ModChannelInfo(string Name, bool Udp, bool Connected, int Id, IReadOnlyDictionary<int, string> MessageTypes);

/// <summary>
/// The messages an engine-mode client's mods send and receive on their network channels, decoded
/// into your own types, and messages delivered to the client's handlers as if the server had sent
/// them.
/// </summary>
/// <remarks>
/// <para>
/// A test cannot reference a mod's message types when the game compiles the mod from source. So a
/// type <c>T</c> is matched to the channel's registered type by itself, then by its full name, then
/// by its name; or name the registered type with <c>messageType</c>. Decoding and encoding go by
/// protobuf member numbers, so a look-alike class with the same <c>[ProtoMember]</c> numbers works.
/// </para>
/// <para>
/// Every message is recorded from the moment the client boots, the newest
/// <see cref="ModNetworkLog.Capacity"/> of them; a client-server scenario that reuses its pair
/// clears them between tests. A UDP message counts as sent when the client hands
/// it to its socket; a degraded network may still drop it. Messages that reached the client before
/// the server listed its channels are recorded when the game hands them over, after the list.
/// </para>
/// </remarks>
public sealed class ModNetworkDriver
{
    private readonly HeadlessClient _client;

    internal ModNetworkDriver(HeadlessClient client)
    {
        _client = client;
    }

    /// <summary>Every recorded message, oldest first, on <paramref name="channel"/> or all channels.</summary>
    public IReadOnlyList<ModMessage> Messages(string? channel = null) =>
        Log().Snapshot().Where(m => channel == null || m.Channel == channel).ToList();

    /// <summary>The sequence number of the newest message, to pass as <c>since</c>.</summary>
    public long Mark() => Log().Mark();

    /// <summary>Forgets the recorded messages.</summary>
    public void Clear() => Log().Clear();

    /// <summary>The channels the client registered, with their message types.</summary>
    public IReadOnlyList<ModChannelInfo> Channels() => _client.RunOnClientThread(() =>
        ModChannels.All(Api()).Select(c => new ModChannelInfo(
            ModChannels.Name(c), c is UdpNetworkChannel, c.Connected, c.Connected ? ModChannels.Id(c) : 0,
            ModChannels.MessageTypes(c).ToDictionary(pair => pair.Value, pair => pair.Key.FullName ?? pair.Key.Name))).ToList());

    /// <summary>The messages of type <typeparamref name="T"/> the client sent on <paramref name="channel"/>, decoded.</summary>
    /// <exception cref="ArgumentException">The channel does not exist, or has no message type matching <typeparamref name="T"/>.</exception>
    /// <remarks>Messages that do not decode as <typeparamref name="T"/>, such as malformed ones a test delivered, are left out.</remarks>
    public IReadOnlyList<T> Sent<T>(string channel, string? messageType = null) => Decoded<T>(Of<T>(channel, ModMessageDirection.Sent, messageType));

    /// <summary>The messages of type <typeparamref name="T"/> the client's handlers got on <paramref name="channel"/>, decoded.</summary>
    /// <exception cref="ArgumentException">The channel does not exist, or has no message type matching <typeparamref name="T"/>.</exception>
    /// <remarks>Messages that do not decode as <typeparamref name="T"/>, such as malformed ones a test delivered, are left out.</remarks>
    public IReadOnlyList<T> Received<T>(string channel, string? messageType = null) => Decoded<T>(Of<T>(channel, ModMessageDirection.Received, messageType));

    /// <summary>The recorded messages of type <typeparamref name="T"/> on <paramref name="channel"/> in one direction, with their details.</summary>
    public IReadOnlyList<ModMessage> Of<T>(string channel, ModMessageDirection direction, string? messageType = null)
    {
        int id = _client.RunOnClientThread(() => MessageId(Channel(channel), typeof(T), messageType));
        return Messages(channel).Where(m => m.Direction == direction && m.MessageId == id).ToList();
    }

    private static List<T> Decoded<T>(IEnumerable<ModMessage> messages)
    {
        List<T> values = [];
        foreach (ModMessage message in messages)
        {
            if (TryDecode(message, out T value)) values.Add(value);
        }

        return values;
    }

    private static bool TryDecode<T>(ModMessage message, out T value)
    {
        try
        {
            value = Decode<T>(message);
            return true;
        }
        catch (Exception ex) when (ex is ProtoException or InvalidOperationException or EndOfStreamException or OverflowException)
        {
            value = default!;
            return false;
        }
    }

    /// <summary>
    /// <paramref name="message"/>'s data decoded as <typeparamref name="T"/>. A message whose every
    /// field is a default has no data and decodes to a new <typeparamref name="T"/>.
    /// </summary>
    public static T Decode<T>(ModMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        using MemoryStream stream = new(message.Data);
        return Serializer.Deserialize<T>(stream);
    }

    /// <summary>
    /// Hands <paramref name="message"/> to the handler of <paramref name="channel"/> on the client
    /// thread, exactly as a message from the server reaches it, then steps a frame. A handler that
    /// throws throws out of this. It works whether or not the server knows the channel.
    /// </summary>
    /// <returns>The message as recorded, with <see cref="ModMessage.Injected"/> set; pass its sequence as <c>since</c> to a wait.</returns>
    /// <exception cref="ArgumentException">The channel does not exist, or has no message type matching <typeparamref name="T"/>.</exception>
    /// <exception cref="InvalidOperationException">The client set no handler for that message type.</exception>
    public async Task<ModMessage> DeliverAsync<T>(string channel, T message, string? messageType = null, CancellationToken ct = default)
    {
        byte[] data;
        using (MemoryStream stream = new())
        {
            Serializer.Serialize(stream, message);
            data = stream.ToArray();
        }

        int id = _client.RunOnClientThread(() => MessageId(Channel(channel), typeof(T), messageType));
        return await DeliverAsync(channel, id, data, ct).ConfigureAwait(false);
    }

    /// <summary>Hands raw protobuf <paramref name="data"/> for <paramref name="messageId"/> to <paramref name="channel"/>'s handler. See <see cref="DeliverAsync{T}"/>.</summary>
    public async Task<ModMessage> DeliverAsync(string channel, int messageId, byte[] data, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(data);
        ModNetworkLog log = Log();
        long before = log.Mark();
        _client.RunOnClientThread(() =>
        {
            NetworkChannel target = Channel(channel);
            if (!ModChannels.HasHandler(target, messageId))
            {
                throw new InvalidOperationException(
                    $"The client set no handler for message {messageId} ({ModChannels.TypeOf(target, messageId)?.Name ?? "unregistered"}) on '{channel}'.");
            }

            Packet_CustomPacket packet = new() { ChannelId = ModChannels.Id(target), MessageId = messageId };
            packet.SetData(data);
            ModNetworkPatches.Injecting(() =>
            {
                // The UDP channel's dispatch hides the TCP one rather than overriding it.
                if (target is UdpNetworkChannel udp) udp.OnPacket(packet);
                else target.OnPacket(packet);
            });
        });

        ModMessage delivered = log.Snapshot().LastOrDefault(m => m.Sequence > before && m.Injected && m.Channel == channel && m.MessageId == messageId)
            ?? throw new InvalidOperationException("The delivered message was not recorded: the client's network hooks are not installed.");
        await _client.StepAsync(ct).ConfigureAwait(false);
        return delivered;
    }

    /// <summary>
    /// Steps frames until the client sends a <typeparamref name="T"/> on <paramref name="channel"/>
    /// that matches <paramref name="predicate"/>, after <paramref name="since"/> (by default, now),
    /// and returns it decoded.
    /// </summary>
    /// <exception cref="TimeoutException">None came within <paramref name="maxFrames"/> frames.</exception>
    public Task<T> WaitForSentAsync<T>(string channel, System.Func<T, bool>? predicate = null, long? since = null, int maxFrames = 600, string? messageType = null, CancellationToken ct = default) =>
        WaitAsync(channel, ModMessageDirection.Sent, predicate, since, maxFrames, messageType, ct);

    /// <summary>
    /// Steps frames until the client's handlers get a <typeparamref name="T"/> on
    /// <paramref name="channel"/> that matches <paramref name="predicate"/>, after
    /// <paramref name="since"/> (by default, now), and returns it decoded.
    /// </summary>
    /// <exception cref="TimeoutException">None came within <paramref name="maxFrames"/> frames.</exception>
    public Task<T> WaitForReceivedAsync<T>(string channel, System.Func<T, bool>? predicate = null, long? since = null, int maxFrames = 600, string? messageType = null, CancellationToken ct = default) =>
        WaitAsync(channel, ModMessageDirection.Received, predicate, since, maxFrames, messageType, ct);

    private async Task<T> WaitAsync<T>(string channel, ModMessageDirection direction, System.Func<T, bool>? predicate, long? since, int maxFrames, string? messageType, CancellationToken ct)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maxFrames);
        long from = since ?? Mark();
        int id = _client.RunOnClientThread(() => MessageId(Channel(channel), typeof(T), messageType));
        for (int frame = 0; ; frame++)
        {
            foreach (ModMessage message in Messages(channel))
            {
                if (message.Sequence <= from || message.Direction != direction || message.MessageId != id) continue;
                if (TryDecode(message, out T value) && (predicate?.Invoke(value) ?? true)) return value;
            }

            if (frame >= maxFrames) break;
            await _client.StepAsync(ct).ConfigureAwait(false);
        }

        throw new TimeoutException($"No {typeof(T).Name} was {direction.ToString().ToLowerInvariant()} on '{channel}' within {maxFrames} frames.");
    }

    private ModNetworkLog Log()
    {
        ClientMain game = Game();
        return ModNetworkLog.Of(game) ?? throw new InvalidOperationException("This client's mod messages are not recorded.");
    }

    private ClientMain Game() =>
        _client.IsEngineMode
            ? _client.Client
            : throw new InvalidOperationException("Mod network messages are recorded on an engine-mode client only.");

    private NetworkAPI Api() => ModChannels.Api(Game()) ?? throw new InvalidOperationException("The client has no network API yet: it has not started a game.");

    private NetworkChannel Channel(string name)
    {
        List<NetworkChannel> matches = ModChannels.All(Api()).Where(c => ModChannels.Name(c) == name).ToList();
        return matches.Count switch
        {
            1 => matches[0],
            0 => throw new ArgumentException(
                $"The client has no network channel '{name}'. It has: {string.Join(", ", ModChannels.All(Api()).Select(ModChannels.Name))}.", nameof(name)),
            _ => throw new ArgumentException($"The client has a TCP and a UDP channel named '{name}'.", nameof(name)),
        };
    }

    // The channel's id for T: T itself, else the registered type with T's full name, then its name.
    private static int MessageId(NetworkChannel channel, Type type, string? messageType)
    {
        Dictionary<Type, int> registered = ModChannels.MessageTypes(channel);
        if (messageType == null && registered.TryGetValue(type, out int exact)) return exact;

        List<KeyValuePair<Type, int>> matches = messageType != null
            ? registered.Where(p => p.Key.FullName == messageType || p.Key.Name == messageType).ToList()
            : registered.Where(p => p.Key.FullName == type.FullName).ToList();
        if (matches.Count == 0 && messageType == null) matches = registered.Where(p => p.Key.Name == type.Name).ToList();

        return matches.Count switch
        {
            1 => matches[0].Value,
            0 => throw new ArgumentException(
                $"The channel '{ModChannels.Name(channel)}' has no message type matching {messageType ?? type.Name}. It has: {string.Join(", ", registered.Keys.Select(k => k.FullName))}."),
            _ => throw new ArgumentException(
                $"Several message types on '{ModChannels.Name(channel)}' match {messageType ?? type.Name}: {string.Join(", ", matches.Select(m => m.Key.FullName))}. Pass messageType with a full name."),
        };
    }
}
