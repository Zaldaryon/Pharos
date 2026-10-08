using Vintagestory.API.Common;
using Zaldaryon.Pharos.Core;
using Zaldaryon.Pharos.Server;

namespace Zaldaryon.Pharos.XUnit.Execution;

/// <summary>
/// What takes part in a rollback besides the world: the listeners a test left, and mods told
/// through <see cref="RollbackEvents"/>. Each side's work runs on its own game thread.
/// </summary>
internal static class RollbackParticipants
{
    /// <summary>
    /// The server's listeners now. Tick listeners and callbacks live in its core event manager,
    /// event-bus listeners in its mod event manager, which ticks listeners of its own too.
    /// </summary>
    public static ListenerWatermark Capture(EmbeddedServerHost host) =>
        host.RunOnGameThread(() => ListenerWatermark.Capture([host.Server.EventManager, host.Server.ModEventManager], host.Server.ModEventManager?.EventBusListeners));

    /// <summary>The client's listeners now.</summary>
    public static ListenerWatermark Capture(HeadlessClient client) =>
        client.RunOnClientThread(() => ListenerWatermark.Capture([client.Client.eventManager], client.Client.eventManager?.EventBusListeners));

    public static int Remove(EmbeddedServerHost host, ListenerWatermark? watermark, System.Func<Delegate, bool> ownedByTest) =>
        watermark == null ? 0 : host.RunOnGameThread(() => watermark.RemoveAddedSince(ownedByTest));

    public static int Remove(HeadlessClient client, ListenerWatermark? watermark, System.Func<Delegate, bool> ownedByTest) =>
        watermark == null ? 0 : client.RunOnClientThread(() => watermark.RemoveAddedSince(ownedByTest));

    /// <summary>Fires <paramref name="eventName"/> on the server's event bus.</summary>
    public static void Push(EmbeddedServerHost host, string eventName, string? test, int? chunks) =>
        host.RunOnGameThread(() => ((ICoreAPI)host.Server.Api).Event.PushEvent(eventName, IsolationLog.EventData(test, chunks)));

    /// <summary>Fires <paramref name="eventName"/> on the client's event bus.</summary>
    public static void Push(HeadlessClient client, string eventName, string? test, int? chunks) =>
        client.RunOnClientThread(() => client.Client.api.Event.PushEvent(eventName, IsolationLog.EventData(test, chunks)));

    /// <summary>Runs <paramref name="action"/>; what it throws becomes the reason a rollback failed.</summary>
    public static string? Try(string what, Action action)
    {
        try
        {
            action();
            return null;
        }
        catch (Exception ex)
        {
            return $"{what} threw {ex.GetType().Name}: {ex.Message}";
        }
    }
}
