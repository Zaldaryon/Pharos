using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.Server;
using Zaldaryon.Pharos.XUnit;

namespace Zaldaryon.Pharos.Server;

/// <summary>Snapshots the engine's actual per-connection chunk and entity tracking, not geometric estimates or packet history.</summary>
public sealed class ServerVisibility
{
    private readonly EmbeddedServerHost _host;
    internal ServerVisibility(EmbeddedServerHost host) => _host = host;

    /// <summary>Chunk coordinates the server currently considers sent to this player.</summary>
    public IReadOnlyList<Vec3i> ChunksSentTo(ServerTestPlayer player) => _host.RunOnGameThread(() =>
        (IReadOnlyList<Vec3i>)Client(player).ChunkSent.Order().Select(index =>
        {
            var pos = _host.Server.WorldMap.ChunkPosFromChunkIndex3D(index);
            return new Vec3i(pos.X, pos.Y, pos.Z);
        }).ToArray());

    /// <summary>Live server entities currently tracked for this player. The collection is a snapshot; entities remain live.</summary>
    public IReadOnlyList<Entity> EntitiesKnownTo(ServerTestPlayer player) => _host.RunOnGameThread(() =>
        (IReadOnlyList<Entity>)Client(player).TrackedEntities.Order()
            .Select(id => _host.Api.World.GetEntityById(id)).OfType<Entity>().ToArray());

    private ConnectedClient Client(ServerTestPlayer player)
    {
        ArgumentNullException.ThrowIfNull(player);
        if (!ReferenceEquals(player.Server, _host.Server) || !player.IsConnected ||
            player.Player is not { } live || !_host.Server.Clients.TryGetValue(live.ClientId, out var client))
            throw new InvalidOperationException("Visibility requires a connected player on this host.");
        return client;
    }
}
