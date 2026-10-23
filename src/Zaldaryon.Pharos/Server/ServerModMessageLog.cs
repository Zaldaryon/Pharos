using ProtoBuf;
using Vintagestory.API.MathTools;
using Zaldaryon.Pharos.Network;

namespace Zaldaryon.Pharos.Server;

/// <summary>One server channel message, in server observation order. Sent means queued for a recipient, not acknowledged.</summary>
/// <param name="Sequence">Monotonic order within this host.</param>
/// <param name="Tick">Host tick when observed on the game thread (the first tick is zero).</param>
/// <param name="Direction">Relative to the server, not the client.</param>
/// <param name="PlayerUID">Recipient for sent messages, sender for received ones; null before player identification.</param>
/// <param name="PlayerName">Player name at observation.</param>
/// <param name="Position">Player position at observation, copied from the server entity.</param>
/// <param name="Channel">Registered server channel name.</param>
/// <param name="Udp">Whether the channel uses UDP, including UDP over TCP.</param>
/// <param name="MessageId">Registered message id.</param>
/// <param name="TypeName">Registered server type name, not inferred from protobuf decoding.</param>
/// <param name="Data">Copied protobuf payload.</param>
/// <param name="ClientId">Engine connection id, including sends before player identification.</param>
public sealed record ServerModMessage(long Sequence, long Tick, ModMessageDirection Direction,
    string? PlayerUID, string? PlayerName, Vec3d? Position, string Channel, bool Udp,
    int MessageId, string? TypeName, byte[] Data, int ClientId = 0)
{
    /// <summary>Payload size in bytes, excluding transport headers.</summary>
    public int Size => Data.Length;

    /// <summary>Decodes the payload into a protobuf look-alike type.</summary>
    public T Decode<T>()
    {
        using MemoryStream stream = new(Data);
        return Serializer.Deserialize<T>(stream);
    }

    internal ServerModMessage Copy() => this with { Position = Position?.Clone(), Data = Data.ToArray() };
}

/// <summary>A decoded message with its original server metadata.</summary>
public sealed record ServerModMessage<T>(ServerModMessage Envelope, T Message);

/// <summary>
/// A bounded recording scope. Dispose stops collection but retains its snapshot. Overflow is an error on read, never silent truncation.
/// </summary>
public sealed class ServerModMessageLog : IDisposable
{
    private readonly ServerModNetworkDriver _driver;
    private readonly string? _channel;
    private readonly int _capacity;
    private readonly List<ServerModMessage> _messages = [];
    private bool _overflow;
    private Exception? _failure;
    private ServerModNetworkDriver.ChannelTypes[] _registry = [];
    private volatile bool _disposed;

    internal ServerModMessageLog(ServerModNetworkDriver driver, string? channel, int capacity)
    {
        _driver = driver;
        _channel = channel;
        _capacity = capacity;
    }

    internal void Add(ServerModMessage message)
    {
        if (_disposed || (_channel != null && message.Channel != _channel)) return;
        if (_messages.Count == _capacity) { _overflow = true; return; }
        _messages.Add(message);
    }

    /// <summary>All messages in this scope, in server order. Returned payloads and positions are copies.</summary>
    public IReadOnlyList<ServerModMessage> Messages() => _disposed ? Snapshot() : _driver.Read(Snapshot);

    private IReadOnlyList<ServerModMessage> Snapshot()
    {
        if (_failure != null) throw new InvalidOperationException("Server mod recording failed.", _failure);
        if (_overflow) throw new InvalidOperationException("Server mod recording overflowed. Increase capacity or shorten the recording.");
        return _messages.Select(m => m.Copy()).ToArray();
    }

    // A failure before the scope stopped, such as a pending-queue overflow, stays an error after it stopped.
    internal void Stop(ServerModNetworkDriver.ChannelTypes[] registry, Exception? failure)
    {
        _registry = registry;
        _failure ??= failure;
        _disposed = true;
    }

    /// <summary>Sent messages of the registered type, decoded into a look-alike.</summary>
    public IReadOnlyList<ServerModMessage<T>> Sent<T>(string? channel = null, string? messageType = null) =>
        Decoded<T>(ModMessageDirection.Sent, channel, messageType);

    /// <summary>Received messages of the registered type, decoded into a look-alike.</summary>
    public IReadOnlyList<ServerModMessage<T>> Received<T>(string? channel = null, string? messageType = null) =>
        Decoded<T>(ModMessageDirection.Received, channel, messageType);

    private IReadOnlyList<ServerModMessage<T>> Decoded<T>(ModMessageDirection direction, string? channel, string? messageType)
    {
        var ids = _disposed
            ? ServerModNetworkDriver.Match(_registry, typeof(T), channel ?? _channel, messageType)
            : _driver.MatchingTypes(typeof(T), channel ?? _channel, messageType);
        return Messages().Where(m => m.Direction == direction && ids.Contains((m.Channel, m.Udp, m.MessageId)))
            .Select(m => new ServerModMessage<T>(m, m.Decode<T>())).ToArray();
    }

    /// <summary>Stops this scope. Previously recorded messages remain readable.</summary>
    public void Dispose()
    {
        if (!_disposed) _driver.Remove(this);
    }
}
