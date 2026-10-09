using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Zaldaryon.Pharos.Timing;
using Zaldaryon.Pharos.World;

namespace Zaldaryon.Pharos.Server;

public sealed partial class ClientServerLoopbackSession
{
    /// <summary>
    /// Stamps a WorldEdit schematic into the server's world (see
    /// <see cref="EmbeddedServerHost.PlaceSchematicAsync"/>), then steps the session until the
    /// client has every block and block entity of it and has meshed the chunks under it.
    /// </summary>
    /// <param name="path">The schematic, relative to the working folder or to the test assembly's folder.</param>
    /// <param name="origin">Where the schematic goes; see <see cref="SchematicOptions.Origin"/>.</param>
    /// <param name="options">How it is placed.</param>
    /// <param name="maxFrames">How many frames the client gets to receive and mesh the placement.</param>
    /// <param name="ct">Cancels the waits.</param>
    /// <remarks>
    /// The client is only sent chunks within its view distance: place schematics near the player.
    /// </remarks>
    /// <exception cref="InvalidOperationException">The session has no embedded server, or the schematic uses blocks the game does not know.</exception>
    /// <exception cref="TimeoutException">The chunks did not load, or the client did not get the placement in time.</exception>
    public async Task<SchematicPlacement> PlaceSchematicAsync(string path, BlockPos origin, SchematicOptions? options = null, int maxFrames = 600, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(origin);
        if (maxFrames <= 0) throw new ArgumentOutOfRangeException(nameof(maxFrames), maxFrames, "Maximum frame count must be positive.");
        EmbeddedServerHost server = NativeServer ?? throw new InvalidOperationException("Placing a schematic needs a session with an embedded server.");

        SchematicPlacer placer = SchematicPlacer.Load(path, options);
        SchematicPlacement placement = await server.PlaceSchematicCoreAsync(
            placer, origin, options?.MaxTicks ?? new SchematicOptions().MaxTicks, readBack: true, (done, max) => StepUntilAsync(done, max, ct: ct)).ConfigureAwait(false);

        string? mismatch = null;
        bool arrived = await StepUntilAsync(() => (mismatch = Arrived(server, placer)) == null, maxFrames, ct: ct).ConfigureAwait(false);
        if (!arrived)
        {
            throw new TimeoutException(
                $"The client did not get the schematic placed at {placement.Start} within {maxFrames} frames: {mismatch}. " +
                "The client is only sent chunks within its view distance; place the schematic near the player.");
        }

        List<ChunkPos> unmeshed = [];
        bool meshed = await StepUntilAsync(() => (unmeshed = [.. placement.Chunks.Where(c => !Client.FrameController.IsChunkMeshed(c))]).Count == 0, maxFrames, ct: ct).ConfigureAwait(false);
        if (!meshed)
        {
            throw new TimeoutException($"The client did not mesh {unmeshed.Count} chunk(s) under the schematic within {maxFrames} frames: {string.Join(", ", unmeshed.Take(10))}.");
        }

        return placement;
    }

    /// <summary>
    /// Loads every chunk between <paramref name="min"/> and <paramref name="max"/> on the server and
    /// keeps their columns loaded, stepping the session until they are there. See
    /// <see cref="EmbeddedServerHost.EnsureChunksLoadedAsync"/>.
    /// </summary>
    /// <exception cref="InvalidOperationException">The session has no embedded server.</exception>
    public async Task<IReadOnlyList<ChunkPos>> EnsureChunksLoadedAsync(BlockPos min, BlockPos max, int maxFrames = 3000, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(min);
        ArgumentNullException.ThrowIfNull(max);
        EmbeddedServerHost server = NativeServer ?? throw new InvalidOperationException("Loading server chunks needs a session with an embedded server.");
        IReadOnlyList<ChunkPos> chunks = server.CheckedRegion(min, max);
        await server.LoadChunksCoreAsync(chunks, maxFrames, (done, limit) => StepUntilAsync(done, limit, ct: ct)).ConfigureAwait(false);
        return chunks;
    }

    // What the client still lacks of the placement, or null once it has all of it. A block the
    // server changed on its own since (sand that fell, water that flowed) is compared as it is now.
    private string? Arrived(EmbeddedServerHost server, SchematicPlacer placer)
    {
        while (true)
        {
            (int index, string? why) = Client.RunOnClientThread(() => FirstMismatch(placer));
            if (index < 0) return why;

            (BlockPos Pos, int BlockId, int FluidId) expected = placer.Expected[index];
            (BlockPos Pos, int BlockId, int FluidId) live = server.RunOnGameThread(() => SchematicPlacer.Read(((Vintagestory.API.Server.ICoreServerAPI)server.Server.Api).World.BlockAccessor, expected.Pos));
            if (live == expected) return why;
            placer.Expected[index] = live;
        }
    }

    // The first position where the client does not have what the server placed, and why; index -1
    // when the reason is not a block, null when it has everything. Client thread.
    private (int Index, string? Why) FirstMismatch(SchematicPlacer placer)
    {
        IBlockAccessor? blocks = Client.Client.World?.BlockAccessor;
        if (blocks == null) return (-1, "the client has no world");

        for (int i = 0; i < placer.Expected.Length; i++)
        {
            (BlockPos pos, int blockId, int fluidId) = placer.Expected[i];
            if (blocks.GetChunkAtBlockPos(pos) == null) return (-1, $"the client does not have the chunk at {pos}");
            int clientBlock = blocks.GetBlock(pos, BlockLayersAccess.Solid).BlockId;
            int clientFluid = blocks.GetBlock(pos, BlockLayersAccess.Fluid).BlockId;
            if (clientBlock != blockId || clientFluid != fluidId)
            {
                return (i, $"at {pos} the server has block {blockId}/fluid {fluidId}, the client {clientBlock}/{clientFluid}");
            }
        }

        foreach (BlockPos pos in placer.BlockEntities)
        {
            if (blocks.GetBlockEntity(pos) == null) return (-1, $"the client has no block entity at {pos}");
        }

        return (-1, null);
    }
}
