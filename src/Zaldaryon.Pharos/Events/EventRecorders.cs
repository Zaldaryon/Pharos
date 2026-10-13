using Vintagestory.Client.NoObf;
using Zaldaryon.Pharos.Core;
using Zaldaryon.Pharos.Server;

namespace Zaldaryon.Pharos.Events;

/// <summary>
/// Records the server's game events: <see cref="EmbeddedServerHost.Events"/>.
/// </summary>
/// <example>
/// <code>
/// using ServerEventLog log = Host!.Events.Record(ServerEvents.Blocks | ServerEvents.Entities);
/// // ... act ...
/// EntityDied died = await log.WaitForAsync&lt;EntityDied&gt;(e => e.Code?.Path == "chicken-hen");
/// </code>
/// </example>
public sealed class ServerEventRecorder
{
    private readonly EmbeddedServerHost _host;
    private readonly List<ServerEventLog> _open = [];

    internal ServerEventRecorder(EmbeddedServerHost host)
    {
        _host = host;
    }

    /// <summary>
    /// Starts recording <paramref name="events"/>. Dispose the log to stop; one a test leaves open is
    /// closed when the scenario ends.
    /// </summary>
    /// <exception cref="InvalidOperationException">The server is not running.</exception>
    public ServerEventLog Record(ServerEvents events = ServerEvents.All)
    {
        if (!_host.IsRunning) throw new InvalidOperationException("Cannot record the events of a stopped server.");
        ServerEventLog log = new(_host, this, events);
        lock (_open) _open.Add(log);
        try
        {
            _host.RunOnGameThread(log.Subscribe);
        }
        catch
        {
            Closed(log);
            throw;
        }

        return log;
    }

    /// <summary>How many logs are recording.</summary>
    internal int OpenCount
    {
        get
        {
            lock (_open) return _open.Count;
        }
    }

    /// <summary>Disposes every log still open, and returns how many there were.</summary>
    internal int CloseAll()
    {
        ServerEventLog[] open;
        lock (_open) open = [.. _open];
        foreach (ServerEventLog log in open)
        {
            try
            {
                log.Dispose();
            }
            catch (Exception)
            {
                // One log that cannot unsubscribe must not leave the others listening.
            }
        }

        return open.Length;
    }

    internal void Closed(ServerEventLog log)
    {
        lock (_open) _open.Remove(log);
    }
}

/// <summary>
/// Records the client's game events: <see cref="HeadlessClient.Events"/>. Engine mode, in a game.
/// </summary>
public sealed class ClientEventRecorder
{
    private readonly HeadlessClient _client;
    private readonly List<ClientEventLog> _open = [];

    internal ClientEventRecorder(HeadlessClient client)
    {
        _client = client;
    }

    /// <summary>
    /// Starts recording <paramref name="events"/>. Dispose the log to stop; one a test leaves open is
    /// closed when the scenario ends.
    /// </summary>
    /// <exception cref="InvalidOperationException">The client is not an engine-mode client in a game.</exception>
    public ClientEventLog Record(ClientEvents events = ClientEvents.All)
    {
        if (_client.IsDisposed) throw new InvalidOperationException("Cannot record the events of a disposed client.");
        if (!_client.IsEngineMode) throw new InvalidOperationException("Client events are recorded on an engine-mode client only.");
        ClientEventLog log = new(_client, this, events);
        _client.RunOnClientThread(() =>
        {
            ClientMain game = _client.Client;
            if (game?.eventManager == null || game.api?.Event == null)
            {
                throw new InvalidOperationException("The client is not in a game: it has no events to record yet.");
            }

            log.Subscribe(game);
        });
        lock (_open) _open.Add(log);
        return log;
    }

    /// <summary>How many logs are recording.</summary>
    internal int OpenCount
    {
        get
        {
            lock (_open) return _open.Count;
        }
    }

    /// <summary>Disposes every log still open, and returns how many there were.</summary>
    internal int CloseAll()
    {
        ClientEventLog[] open;
        lock (_open) open = [.. _open];
        foreach (ClientEventLog log in open)
        {
            try
            {
                log.Dispose();
            }
            catch (Exception)
            {
                // One log that cannot unsubscribe must not leave the others listening.
            }
        }

        return open.Length;
    }

    /// <summary>Moves every open log onto the client's new game session. Runs on the client thread.</summary>
    internal void MoveTo(ClientMain next)
    {
        ClientEventLog[] open;
        lock (_open) open = [.. _open];
        foreach (ClientEventLog log in open) log.MoveTo(next);
    }

    internal void Closed(ClientEventLog log)
    {
        lock (_open) _open.Remove(log);
    }
}
