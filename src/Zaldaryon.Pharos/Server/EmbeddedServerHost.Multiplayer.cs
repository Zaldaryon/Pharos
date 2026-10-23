using System.Runtime.CompilerServices;
using Vintagestory.Common;
using Vintagestory.Server;
using Zaldaryon.Pharos.Scenarios;
using Zaldaryon.Pharos.XUnit;

namespace Zaldaryon.Pharos.Server;

public sealed partial class EmbeddedServerHost
{
    private List<BatchPacket>? _batch;
    [ThreadStatic] private static EmbeddedServerHost? t_batchHost;
    private long _crowdSequence;

    /// <summary>Server-side mod channel recording, including real client and bot traffic.</summary>
    public ServerModNetworkDriver ModNetwork { get; private set; } = null!;

    /// <summary>Actual per-connection engine chunk/entity tracking.</summary>
    public ServerVisibility Visibility { get; private set; } = null!;

    /// <summary>
    /// Collects synchronous TCP bot actions, submits their immutable serialized bytes to the engine receive parser in order,
    /// and runs exactly one server tick. All handlers execute through the normal ClientPackets queue.
    /// </summary>
    /// <remarks>
    /// Transport timing is intentionally excluded: degraded, filtered or scheduled links and UDP actions are rejected.
    /// No packets are submitted if the callback or preflight fails. Once committed, ordinary engine errors/disconnects
    /// are not rolled back. Other clients' already queued packets are not reordered. Do not tick, nest or await in the callback.
    /// </remarks>
    public Task SameTick(Action actions)
    {
        RequireOutsideBatch();
        ArgumentNullException.ThrowIfNull(actions);
        if (actions.Method.IsDefined(typeof(AsyncStateMachineAttribute), false))
            throw new ArgumentException("SameTick requires a synchronous callback.", nameof(actions));
        try
        {
            RunOnGameThread(() =>
            {
                RequireOutsideBatch();
                var packets = new List<BatchPacket>();
                _batch = packets;
                t_batchHost = this;
                try
                {
                    actions();
                    foreach (var packet in packets) ValidateBatchPlayer(packet.Player);
                }
                finally { _batch = null; t_batchHost = null; }

                foreach (var packet in packets)
                {
                    var client = Server.Clients[packet.Player.Player!.ClientId];
                    ParsePacket(Server, client, packet.Bytes, packet.Bytes.Length);
                }
                Tick();
            });
            return Task.CompletedTask;
        }
        catch (Exception ex) { return Task.FromException(ex); }
    }

    internal void RequireOutsideBatch()
    {
        if (t_batchHost != null) throw new InvalidOperationException("Cannot tick, nest SameTick or await a bot action inside a SameTick callback.");
    }

    internal bool CaptureBatchPacket(ServerTestPlayer player, Packet_Client packet)
    {
        if (t_batchHost == null) return false;
        if (!ReferenceEquals(t_batchHost, this) || _batch == null)
            throw new InvalidOperationException("SameTick bots must belong to its host.");
        ValidateBatchPlayer(player);
        if (packet.Id == 35) throw new InvalidOperationException("SameTick only supports TCP bot actions.");
        CitoMemoryStream stream = new();
        Packet_ClientSerializer.Serialize(stream, packet);
        var bytes = new byte[stream.Position()];
        Array.Copy(stream.ToArray(), bytes, bytes.Length);
        _batch.Add(new BatchPacket(player, bytes));
        return true;
    }

    private void ValidateBatchPlayer(ServerTestPlayer player)
    {
        if (!ReferenceEquals(player.RequireBotHost(), this) || !player.IsConnected)
            throw new InvalidOperationException("SameTick requires connected bots on this host.");
        if (!player.RequireBotConnection().WaitUntilBatchReady(TimeSpan.FromSeconds(5 * ScenarioTimeouts.Scale)))
            throw new InvalidOperationException("SameTick requires earlier bot packets to reach the engine queue first; tick the host before batching.");
        if (!player.RequireBotConnection().Degradation.IsPristine)
            throw new InvalidOperationException("SameTick cannot bypass degraded, filtered or scripted network links.");
    }

    /// <summary>
    /// Joins count normal headless players and places them in a deterministic layout. Names are unique within this host.
    /// Partially joined crowds are disconnected on cancellation or failure; existing players are untouched.
    /// </summary>
    public async Task<ServerTestPlayer[]> CreateTestPlayersAsync(int count, PlayerLayout layout,
        int maxTicks = 5000, CancellationToken ct = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(count);
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxTicks);
        RequireOutsideBatch();
        ct.ThrowIfCancellationRequested();
        long group = RunOnGameThread(() =>
        {
            long candidate;
            do { candidate = ++_crowdSequence; }
            while (TestPlayers.Any(p => p.PlayerName?.StartsWith($"Crowd{candidate}-", StringComparison.OrdinalIgnoreCase) == true));
            return candidate;
        });
        List<ServerTestPlayer> players = new(count);
        try
        {
            for (int index = 0; index < count; index++)
            {
                ct.ThrowIfCancellationRequested();
                var position = layout.Position(index, count);
                var player = await CreateTestPlayerAsync($"Crowd{group}-{index}", maxTicks, ct).ConfigureAwait(false);
                players.Add(player);
                await player.TeleportToAsync(position.X, position.Y, position.Z).ConfigureAwait(false);
                if (!await TryTickUntilAsync(() => RunOnGameThread(() => player.Entity!.Pos.XYZ.DistanceTo(position) < 0.15), maxTicks, ct).ConfigureAwait(false))
                    throw new TimeoutException($"Crowd player {index} did not reach its layout position within {maxTicks} ticks.");
            }
            return players.ToArray();
        }
        catch
        {
            foreach (var player in players)
            {
                try { player.Disconnect(); }
                catch (Exception cleanup) { Api.Logger.Warning("Crowd cleanup failed: " + cleanup.Message); }
            }
            throw;
        }
    }

    [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "ParseClientPacket_offthread")]
    private static extern void ParsePacket(ServerMain server, ConnectedClient client, byte[] data, int dataLength);

    private sealed record BatchPacket(ServerTestPlayer Player, byte[] Bytes);
}
