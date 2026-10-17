using System.Collections.Concurrent;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.Server;
using Zaldaryon.Pharos.Core;

namespace Zaldaryon.Pharos.Server;

/// <summary>
/// The state hash parts, computed on the game thread. Every value is folded with <see cref="StableHash"/>
/// in a fixed order, so two runs of the same state give the same hashes.
/// </summary>
internal static class StateHashing
{
    /// <summary>The hashes of the chosen parts at <paramref name="tick"/>. Runs on the game thread.</summary>
    public static StateHashTick Sample(EmbeddedServerHost host, Cuboidi? region, StateHashParts parts, long tick)
    {
        ServerMain server = host.Server;
        ulong blocks = parts.HasFlag(StateHashParts.Blocks) ? Blocks(server, region!) : 0;
        ulong entities = parts.HasFlag(StateHashParts.Entities) ? Entities(server) : 0;
        ulong players = parts.HasFlag(StateHashParts.Players) ? Players(server) : 0;
        return new StateHashTick(tick, blocks, entities, players);
    }

    // The blocks read in (y, z, x) order. A block that cannot be read counts as air here, so the hash is only
    // meaningful for a region whose chunks are loaded for the whole recording.
    private static ulong Blocks(ServerMain server, Cuboidi region)
    {
        IBlockAccessor accessor = ((ICoreServerAPI)server.Api).World.BlockAccessor;
        ulong hash = StableHash.Seed;
        for (int y = region.Y1; y <= region.Y2; y++)
        {
            for (int z = region.Z1; z <= region.Z2; z++)
            {
                for (int x = region.X1; x <= region.X2; x++)
                {
                    hash = StableHash.AddLong(hash, accessor.GetBlockId(new BlockPos(x, y, z, 0)));
                }
            }
        }

        return StableHash.Finish(hash);
    }

    // The loaded entities that are not players, by entity id.
    private static ulong Entities(ServerMain server)
    {
        ulong hash = StableHash.Seed;
        foreach (Entity entity in LoadedEntities(server).Values.Where(e => e is not EntityPlayer).OrderBy(e => e.EntityId))
        {
            hash = StableHash.AddLong(hash, entity.EntityId);
            hash = StableHash.AddString(hash, entity.Code.ToString());
            hash = Position(hash, entity.Pos);
            hash = StableHash.Add(hash, entity.Alive ? 1UL : 0UL);
        }

        return StableHash.Finish(hash);
    }

    // The players by player id, ordinally, so the order does not depend on the connection order.
    private static ulong Players(ServerMain server)
    {
        ulong hash = StableHash.Seed;
        foreach (EntityPlayer player in LoadedEntities(server).Values.OfType<EntityPlayer>()
            .OrderBy(p => p.Player?.PlayerUID ?? string.Empty, StringComparer.Ordinal))
        {
            hash = StableHash.AddString(hash, player.Player?.PlayerUID ?? string.Empty);
            hash = Position(hash, player.Pos);
            hash = StableHash.AddDouble(hash, player.Pos.Yaw);
        }

        return StableHash.Finish(hash);
    }

    private static ulong Position(ulong hash, EntityPos pos)
    {
        hash = StableHash.AddDouble(hash, pos.X);
        hash = StableHash.AddDouble(hash, pos.Y);
        return StableHash.AddDouble(hash, pos.Z);
    }

    private static ConcurrentDictionary<long, Entity> LoadedEntities(ServerMain server) => ((IServerWorldAccessor)server).LoadedEntities;
}
