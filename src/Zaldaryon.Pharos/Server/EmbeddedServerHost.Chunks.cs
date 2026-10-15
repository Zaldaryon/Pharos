using System.Reflection;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.Server;
using Zaldaryon.Pharos.Timing;

namespace Zaldaryon.Pharos.Server;

public sealed partial class EmbeddedServerHost
{
    // The unloading system is internal to the game, so it is found by its name in the server's assembly.
    private static readonly Type? s_unloadSystemType = typeof(ServerMain).Assembly.GetType("Vintagestory.Server.ServerSystemUnloadChunks");
    private static readonly FieldInfo? s_systems = typeof(ServerMain).GetField("Systems", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
    private static readonly FieldInfo? s_unloadCandidates = s_unloadSystemType?.GetField("mapChunkUnloadCandidates", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
    private static readonly MethodInfo? s_unloadColumns = s_unloadSystemType?.GetMethod("UnloadChunkColumns", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

    /// <summary>
    /// Unloads the chunk columns under a box, as the server does once no player is near them: each changed chunk is written to the
    /// save, and the server forgets the chunks. Afterwards, <see cref="ReloadChunksAsync"/> loads them from the save again.
    /// </summary>
    /// <param name="min">One corner of the box, inclusive.</param>
    /// <param name="max">The opposite corner, inclusive.</param>
    /// <exception cref="ArgumentOutOfRangeException">The box leaves the world.</exception>
    /// <exception cref="InvalidOperationException">The server is not running, a player stands in one of the columns, or the game has no unloading system.</exception>
    internal void UnloadChunks(BlockPos min, BlockPos max)
    {
        if (!IsRunning) throw new InvalidOperationException("The server is not running, so its chunks cannot be unloaded.");
        IReadOnlyList<ChunkPos> chunks = CheckedRegion(min, max);
        RunOnGameThread(() => UnloadOnGameThread(chunks));
    }

    private void UnloadOnGameThread(IReadOnlyList<ChunkPos> chunks)
    {
        ServerMain server = Server;
        var columns = chunks.Select(c => (c.X, c.Z)).Distinct().ToList();

        // A player's column is kept with the player. Unloading it would leave the player in a chunk the server has forgotten.
        foreach (ConnectedClient client in server.Clients.Values)
        {
            if (client.Position is not { } pos) continue;
            int cx = (int)pos.X / GlobalConstants.ChunkSize;
            int cz = (int)pos.Z / GlobalConstants.ChunkSize;
            if (columns.Contains((cx, cz)))
            {
                throw new InvalidOperationException($"A player stands in the chunk column {cx},{cz}: unloading it would leave the player without its chunk.");
            }
        }

        object unload = UnloadSystem(server) ?? throw new InvalidOperationException("The game has no chunk unloading system.");
        var indices = new HashSet<long>();
        foreach ((int cx, int cz) in columns)
        {
            long index = server.WorldMap.MapChunkIndex2D(cx, cz);
            indices.Add(index);

            // The columns that EnsureChunksLoadedAsync kept are released, or the server would skip them.
            server.RemoveChunkColumnFromForceLoadedList(index);
        }

        var candidates = s_unloadCandidates?.GetValue(unload) as HashSet<long> ?? throw new InvalidOperationException("The game's unloading system has no candidate list.");
        candidates.UnionWith(indices);
        s_unloadColumns?.Invoke(unload, null);

        // The unloaded chunks are written by the chunk thread, which is paused here as the save does it.
        if (!server.Suspend(true, 10000)) throw new InvalidOperationException("The server could not be paused to write the unloaded chunks.");
        try
        {
            ((ServerSystem)unload).OnSeparateThreadTick();
        }
        finally
        {
            server.Suspend(false);
        }

        // The map chunks the server had cached may be the ones it just released.
        WorldSnapshot.ForgetCachedChunk(server);
    }

    private object? UnloadSystem(ServerMain server) =>
        s_systems?.GetValue(server) is System.Collections.IEnumerable systems
            ? systems.Cast<object?>().FirstOrDefault(system => system is not null && system.GetType() == s_unloadSystemType)
            : null;
}
