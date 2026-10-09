using System.Runtime.CompilerServices;
using Vintagestory.Client.NoObf;

namespace Zaldaryon.Pharos.Network;

/// <summary>Whether a mod message left the client or reached it.</summary>
public enum ModMessageDirection
{
    /// <summary>The client sent it to the server.</summary>
    Sent,

    /// <summary>It reached one of the client's channel handlers.</summary>
    Received,
}

/// <summary>A message on one of a mod's network channels, as the client sent or received it.</summary>
/// <param name="Sequence">Its place among this client's messages, in both directions; it only grows.</param>
/// <param name="Frame">The client frame it was sent or handled in.</param>
/// <param name="Direction">Sent or received.</param>
/// <param name="Channel">The channel's name.</param>
/// <param name="Udp">Whether the channel is a UDP one.</param>
/// <param name="MessageId">The message's id on the channel: the order its type was registered in.</param>
/// <param name="TypeName">The full name of the type registered for that id on the client, or null.</param>
/// <param name="Data">The message as protobuf bytes; empty for a message whose every field is a default.</param>
/// <param name="Injected">Delivered by <see cref="ModNetworkDriver.DeliverAsync{T}"/>, not by the server.</param>
public sealed record ModMessage(long Sequence, long Frame, ModMessageDirection Direction, string Channel, bool Udp, int MessageId, string? TypeName, byte[] Data, bool Injected);

/// <summary>The mod messages one client sent and received, the newest ones.</summary>
internal sealed class ModNetworkLog
{
    public const int Capacity = 10_000;

    private static readonly ConditionalWeakTable<ClientMain, ModNetworkLog> s_logs = new();

    private readonly object _lock = new();
    private readonly LinkedList<ModMessage> _messages = new();
    private readonly System.Func<long> _frame;
    private long _sequence;

    private ModNetworkLog(System.Func<long> frame)
    {
        _frame = frame;
    }

    /// <summary>Starts recording <paramref name="game"/>'s mod messages, stamped with <paramref name="frame"/>.</summary>
    public static void Register(ClientMain game, System.Func<long> frame) => s_logs.AddOrUpdate(game, new ModNetworkLog(frame));

    /// <summary>
    /// Records <paramref name="next"/>'s mod messages in <paramref name="previous"/>'s log, so the
    /// sequence goes on across a reconnect and marks taken before it stay valid.
    /// </summary>
    public static void Carry(ClientMain previous, ClientMain next)
    {
        if (!s_logs.TryGetValue(previous, out ModNetworkLog? log)) return;
        s_logs.Remove(previous);
        s_logs.AddOrUpdate(next, log);
    }

    /// <summary>The log of <paramref name="game"/>, or null when it is not recorded.</summary>
    public static ModNetworkLog? Of(ClientMain? game) => game != null && s_logs.TryGetValue(game, out ModNetworkLog? log) ? log : null;

    /// <summary>The recorded clients, for hooks that know a client only by its socket.</summary>
    public static IEnumerable<ClientMain> Clients() => s_logs.Select(pair => pair.Key);

    /// <summary>The last sequence number given, or 0.</summary>
    public long Mark()
    {
        lock (_lock) return _sequence;
    }

    public ModMessage Add(ModMessageDirection direction, string channel, bool udp, int messageId, string? typeName, byte[]? data, bool injected)
    {
        long frame;
        try
        {
            frame = _frame();
        }
        catch (Exception)
        {
            frame = -1;
        }

        lock (_lock)
        {
            ModMessage message = new(++_sequence, frame, direction, channel, udp, messageId, typeName, data?.ToArray() ?? [], injected);
            _messages.AddLast(message);
            if (_messages.Count > Capacity) _messages.RemoveFirst();
            return message;
        }
    }

    public IReadOnlyList<ModMessage> Snapshot()
    {
        lock (_lock) return [.. _messages];
    }

    public void Clear()
    {
        lock (_lock) _messages.Clear();
    }
}
