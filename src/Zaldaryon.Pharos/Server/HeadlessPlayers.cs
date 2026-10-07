using System.Net;
using Vintagestory.API.Common;
using Vintagestory.API.Server;
using Vintagestory.Client.Network;
using Vintagestory.Server;
using Vintagestory.Server.Network;
using Zaldaryon.Pharos.XUnit;

namespace Zaldaryon.Pharos.Server;

/// <summary>
/// Joins headless players into an embedded server and keeps their connections drained.
/// </summary>
/// <remarks>
/// <para>
/// A headless player goes through the same join sequence as a real multiplayer client, packet by
/// packet: login token query and identification, then (once the server has spawned the player
/// entity) the join request, which sets up the inventories, then loaded and ready, which make it a
/// playing player. Everything the server does for a real player runs for it: join events, mods'
/// player join handlers, chunk sending, entity tracking, the playing-player count.
/// </para>
/// <para>
/// The server keeps sending to a playing client, so every pass the packets queued for each
/// player are taken off its socket and handed to the player, and the UDP queue the players share
/// is emptied unless a real client is attached to read it.
/// </para>
/// </remarks>
internal sealed class HeadlessPlayers
{
    private readonly EmbeddedServerHost _host;
    private readonly List<ServerTestPlayer> _players = [];
    private readonly object _lock = new();
    private DummyUdpNetClient? _udpDrain;

    public HeadlessPlayers(EmbeddedServerHost host)
    {
        _host = host;
    }

    /// <summary>Whether a real client reads the shared UDP queue, so it must not be emptied.</summary>
    public bool LoopbackClientAttached { get; set; }

    /// <summary>The players joined so far that are still connected.</summary>
    public IReadOnlyList<ServerTestPlayer> Joined
    {
        get
        {
            lock (_lock)
            {
                return [.. _players];
            }
        }
    }

    public async Task<ServerTestPlayer> JoinAsync(string playerName, int maxTicks, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(playerName);
        ServerMain server = _host.Server;
        string playerUid = "pharos-" + playerName.ToLowerInvariant();

        HeadlessPlayerConnection connection = _host.RunOnGameThread(() =>
        {
            if (server.GetClientByPlayername(playerName) != null)
            {
                throw new InvalidOperationException($"A player named '{playerName}' is already connected.");
            }

            return HeadlessPlayerConnection.Open(server, playerName, playerUid);
        });

        ServerTestPlayer player = new(playerUid);
        try
        {
            // The server spawns the entity once the identification is processed and the spawn
            // chunk is loaded.
            ConnectedClient client = await WaitForAsync(
                () => Find(server, playerName) is { Entityplayer: not null } c ? c : null,
                maxTicks, ct, $"spawn player '{playerName}'").ConfigureAwait(false);

            _host.RunOnGameThread(() => connection.RequestJoin());
            await WaitForAsync(
                () => client.Player?.InventoryManager?.Inventories?.Count > 0 ? client : null,
                maxTicks, ct, $"set up the inventories of '{playerName}'").ConfigureAwait(false);

            _host.RunOnGameThread(() =>
            {
                connection.ReportLoadedAndReady();

                // The engine expects every client it disconnects to have a UDP endpoint; a
                // headless player never sends UDP, so register one under its client id.
                if (server.UdpSockets[0] is DummyUdpNetServer udp && !udp.EndPoints.ContainsValue(client.Id))
                {
                    udp.Add(new IPEndPoint(IPAddress.Loopback, client.Id), client.Id);
                }
            });

            await WaitForAsync(
                () => client.State == EnumClientState.Playing ? client : null,
                maxTicks, ct, $"move '{playerName}' to the playing state").ConfigureAwait(false);

            player.Attach(client.Player, server, _host, connection);
            lock (_lock)
            {
                _players.Add(player);
            }

            return player;
        }
        catch
        {
            _host.RunOnGameThread(() => connection.Close(server));
            throw;
        }
    }

    /// <summary>Called on the game thread after every server pass.</summary>
    public void AfterTick()
    {
        ServerTestPlayer[] players;
        lock (_lock)
        {
            _players.RemoveAll(p => !p.IsConnected);
            players = [.. _players];
        }

        foreach (ServerTestPlayer player in players)
        {
            player.ReceivePending();
        }

        if (!LoopbackClientAttached && players.Length > 0)
        {
            _udpDrain ??= CreateUdpDrain();
            _udpDrain.ReadMessage();
        }
    }

    private DummyUdpNetClient CreateUdpDrain()
    {
        DummyUdpNetClient drain = new();
        drain.SetNetwork(_host.UdpNetwork);
        return drain;
    }

    private static ConnectedClient? Find(ServerMain server, string playerName) => server.GetClientByPlayername(playerName);

    private async Task<T> WaitForAsync<T>(Func<T?> probe, int maxTicks, CancellationToken ct, string what) where T : class
    {
        for (int tick = 0; tick <= maxTicks; tick++)
        {
            ct.ThrowIfCancellationRequested();

            T? result = _host.RunOnGameThread(probe);
            if (result != null) return result;

            _host.Tick();

            // Give the engine's off-thread work (chunk generation, packet parsing) a moment.
            await Task.Delay(1, ct).ConfigureAwait(false);
        }

        throw new TimeoutException($"The server did not {what} within {maxTicks} ticks.");
    }
}
