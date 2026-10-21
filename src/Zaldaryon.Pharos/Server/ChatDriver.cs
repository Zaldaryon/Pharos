using System.Reflection;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.Server;
using Vintagestory.Server;

namespace Zaldaryon.Pharos.Server;

/// <summary>What the server's chat handlers did with a player's message.</summary>
/// <param name="PlayerName">Who wrote the message, or an empty name when there was no player.</param>
/// <param name="GroupId">The chat group the message went to.</param>
/// <param name="OriginalText">The text as the player wrote it.</param>
/// <param name="FinalText">The text after the handlers ran.</param>
/// <param name="OriginalData">The message's data as the player sent it.</param>
/// <param name="FinalData">The message's data after the handlers ran.</param>
/// <param name="Consumed">Whether a handler consumed the message, so it was not sent.</param>
public sealed record ChatMessage(
    string PlayerName,
    int GroupId,
    string OriginalText,
    string FinalText,
    string OriginalData,
    string FinalData,
    bool Consumed)
{
    /// <summary>The final text without the chat's markup.</summary>
    public string FinalPlainText => ChatText.Plain(FinalText);

    /// <summary>The original text without the chat's markup.</summary>
    public string OriginalPlainText => ChatText.Plain(OriginalText);
}

/// <summary>What the server's chat handlers did, while a recording was open.</summary>
public sealed class ChatLog : IDisposable
{
    private readonly ServerEventManager _events;
    private readonly PlayerChatDelegate? _original;
    private readonly List<ChatMessage> _messages = [];
    private bool _disposed;

    internal ChatLog(ServerEventManager events, PlayerChatDelegate? original)
    {
        _events = events;
        _original = original;
        ChatEventInternals.Set(events, Wrapper);
    }

    /// <summary>The messages the handlers saw, in order.</summary>
    public IReadOnlyList<ChatMessage> Messages
    {
        get
        {
            lock (_messages) return [.. _messages];
        }
    }

    /// <summary>How many messages the handlers saw.</summary>
    public int Count
    {
        get
        {
            lock (_messages) return _messages.Count;
        }
    }

    /// <summary>Stops recording and puts the server's own chat handler back.</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        ChatEventInternals.Set(_events, _original);
    }

    private void Wrapper(IServerPlayer byPlayer, int channelId, ref string message, ref string data, BoolRef consumed)
    {
        string originalText = message;
        string originalData = data;
        _original?.Invoke(byPlayer, channelId, ref message, ref data, consumed);
        lock (_messages)
        {
            _messages.Add(new ChatMessage(
                byPlayer?.PlayerName ?? "",
                channelId,
                originalText,
                message,
                originalData,
                data,
                consumed?.value ?? false));
        }
    }
}

/// <summary>The server's chat handlers, and what they did with the messages.</summary>
public sealed class ChatDriver(EmbeddedServerHost host)
{
    /// <summary>Records what the chat handlers do with the messages, until the record is disposed.</summary>
    public ChatLog Record()
    {
        ServerEventManager events = host.ModEventManager;
        return new ChatLog(events, ChatEventInternals.Get(events));
    }
}

/// <summary>The chat event's handlers, which the game keeps in a field of its own.</summary>
internal static class ChatEventInternals
{
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<Type, FieldInfo?> s_fields = new();

    public static PlayerChatDelegate? Get(ServerEventManager events) =>
        FieldOf(events)?.GetValue(events) as PlayerChatDelegate;

    public static void Set(ServerEventManager events, PlayerChatDelegate? handlers) =>
        FieldOf(events)?.SetValue(events, handlers);

    private static FieldInfo? FieldOf(ServerEventManager events) =>
        s_fields.GetOrAdd(events.GetType(), type => type.GetField("OnPlayerChat", BindingFlags.Instance | BindingFlags.NonPublic));
}

/// <summary>The chat's markup, and the plain text under it.</summary>
internal static class ChatText
{
    /// <summary>The text without the chat's tags, with the entities the chat uses decoded.</summary>
    public static string Plain(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;

        System.Text.StringBuilder plain = new(text.Length);
        bool inTag = false;
        foreach (char character in text)
        {
            if (character == '<')
            {
                inTag = true;
                continue;
            }

            if (character == '>')
            {
                inTag = false;
                continue;
            }

            if (!inTag) plain.Append(character);
        }

        return plain.ToString()
            .Replace("&lt;", "<", StringComparison.Ordinal)
            .Replace("&gt;", ">", StringComparison.Ordinal)
            .Replace("&quot;", "\"", StringComparison.Ordinal)
            .Replace("&apos;", "'", StringComparison.Ordinal)
            .Replace("&nbsp;", " ", StringComparison.Ordinal)
            .Replace("&amp;", "&", StringComparison.Ordinal);
    }
}
