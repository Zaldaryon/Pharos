using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Zaldaryon.Pharos.Server;
using Zaldaryon.Pharos.Timing;

namespace Zaldaryon.Pharos.World;

/// <summary>How <c>PlaceSchematicAsync</c> stamps a schematic into the world.</summary>
public sealed record SchematicOptions
{
    /// <summary>
    /// Which blocks already in the world the schematic replaces. <see cref="EnumReplaceMode.ReplaceAll"/>
    /// by default, as the WorldEdit import tool does: the box ends up exactly as exported, air included.
    /// </summary>
    public EnumReplaceMode ReplaceMode { get; init; } = EnumReplaceMode.ReplaceAll;

    /// <summary>
    /// Where the given position sits on the schematic's box. <see cref="EnumOrigin.StartPos"/> by
    /// default: the position is the box's lowest corner.
    /// </summary>
    public EnumOrigin Origin { get; init; } = EnumOrigin.StartPos;

    /// <summary>
    /// A rotation around the vertical axis, in degrees: 0, 90, 180 or 270. Blocks are swapped for
    /// their rotated variants. The origin applies to the rotated box.
    /// </summary>
    public int Angle { get; init; }

    /// <summary>
    /// Whether blocks the game does not know (from a mod that is not loaded, say) are left out,
    /// as the game does, instead of failing the placement. They are listed in
    /// <see cref="SchematicPlacement.MissingBlockCodes"/> either way.
    /// </summary>
    public bool AllowMissingBlocks { get; init; }

    /// <summary>How many server ticks the chunks under the box get to load. 3000 by default.</summary>
    public int MaxTicks { get; init; } = 3000;
}

/// <summary>What a schematic placement did.</summary>
/// <param name="Start">The lowest corner of the box the schematic filled.</param>
/// <param name="Size">The size of that box, after the rotation.</param>
/// <param name="BlocksPlaced">How many blocks the game placed.</param>
/// <param name="Chunks">Every chunk the box touches.</param>
/// <param name="MissingBlockCodes">Block codes in the schematic the game does not know, which were left out.</param>
public sealed record SchematicPlacement(
    BlockPos Start,
    Vec3i Size,
    int BlocksPlaced,
    IReadOnlyList<ChunkPos> Chunks,
    IReadOnlyList<AssetLocation> MissingBlockCodes)
{
    /// <summary>The highest corner of the box.</summary>
    public BlockPos End => Start.AddCopy(Size.X - 1, Size.Y - 1, Size.Z - 1);
}

/// <summary>A box of chunks to load and keep loaded on the server.</summary>
internal static class ChunkRegion
{
    /// <summary>The most chunk columns one request loads and keeps loaded.</summary>
    public const int MaxColumns = 1024;

    /// <summary>Every chunk the box from <paramref name="min"/> to <paramref name="max"/> touches.</summary>
    public static IReadOnlyList<ChunkPos> Covering(BlockPos min, BlockPos max)
    {
        const int Shift = 5;
        System.Diagnostics.Debug.Assert(1 << Shift == GlobalConstants.ChunkSize);
        int x0 = Math.Min(min.X, max.X) >> Shift, x1 = Math.Max(min.X, max.X) >> Shift;
        int y0 = Math.Min(min.Y, max.Y) >> Shift, y1 = Math.Max(min.Y, max.Y) >> Shift;
        int z0 = Math.Min(min.Z, max.Z) >> Shift, z1 = Math.Max(min.Z, max.Z) >> Shift;
        long columns = (long)(x1 - x0 + 1) * (z1 - z0 + 1);
        if (columns > MaxColumns)
        {
            throw new ArgumentOutOfRangeException(nameof(max), $"The box from {min} to {max} covers {columns} chunk columns; at most {MaxColumns} are loaded at once.");
        }

        List<ChunkPos> chunks = [];
        for (int cx = x0; cx <= x1; cx++)
        {
            for (int cy = y0; cy <= y1; cy++)
            {
                for (int cz = z0; cz <= z1; cz++)
                {
                    chunks.Add(new ChunkPos(cx, cy, cz));
                }
            }
        }

        return chunks;
    }

    /// <summary>Throws when the box leaves the world, where no chunk ever loads. Game thread.</summary>
    public static void CheckInWorld(ICoreServerAPI api, BlockPos min, BlockPos max)
    {
        if (!InWorld(api.World.BlockAccessor, min) || !InWorld(api.World.BlockAccessor, max))
        {
            throw new ArgumentOutOfRangeException(nameof(max), $"The box from {min} to {max} leaves the world.");
        }
    }

    /// <summary>
    /// Asks the server to load each chunk column under <paramref name="chunks"/> and to keep it
    /// loaded. Kept loaded, a column is not unloaded and saved mid-test, which would take it out of
    /// the reach of the world rollback. Columns stay kept for the life of the server.
    /// </summary>
    public static void Request(ICoreServerAPI api, IReadOnlyList<ChunkPos> chunks)
    {
        HashSet<(int, int)> columns = [];
        foreach (ChunkPos chunk in chunks)
        {
            if (columns.Add((chunk.X, chunk.Z)))
            {
                api.WorldManager.LoadChunkColumnPriority(chunk.X, chunk.Z, new ChunkLoadOptions { KeepLoaded = true });
            }
        }
    }

    // Inside the map, and in the main dimension: a Y beyond the map's height is another dimension.
    public static bool InWorld(IBlockAccessor blocks, BlockPos pos) =>
        pos.Y >= 0 && pos.Y < blocks.MapSizeY && blocks.IsValidPos(pos);

    /// <summary>The chunks of <paramref name="chunks"/> the server has not loaded yet.</summary>
    public static List<ChunkPos> Missing(ICoreServerAPI api, IReadOnlyList<ChunkPos> chunks) =>
        [.. chunks.Where(c => api.World.BlockAccessor.GetChunk(c.X, c.Y, c.Z) == null)];

    public static TimeoutException Timeout(IReadOnlyList<ChunkPos> missing, int maxTicks) =>
        new($"The server did not load {missing.Count} chunk(s) within {maxTicks} ticks: " +
            $"{string.Join(", ", missing.Take(10))}{(missing.Count > 10 ? ", ..." : "")}.");
}

/// <summary>
/// Stamps a WorldEdit schematic into a server's world the way the WorldEdit import does, in three
/// steps: <see cref="Prepare"/> and <see cref="Place"/> on the game thread, with the chunks under the
/// box loaded between them by whoever ticks the server.
/// </summary>
internal sealed class SchematicPlacer
{
    private const int MaxSize = 1024;

    private readonly BlockSchematic _original;
    private readonly SchematicOptions _options;
    private BlockSchematic? _rotated;
    private BlockPos? _start;
    private List<AssetLocation> _missing = [];

    private SchematicPlacer(BlockSchematic schematic, SchematicOptions options)
    {
        _original = schematic;
        _options = options;
    }

    public IReadOnlyList<ChunkPos> Chunks { get; private set; } = [];

    /// <summary>
    /// The block id and the fluid id of every position the schematic set, read back from the server
    /// after the placement when asked for. A client has the placement once it reads the same.
    /// </summary>
    public (BlockPos Pos, int BlockId, int FluidId)[] Expected { get; private set; } = [];

    /// <summary>Where the placement put block entities.</summary>
    public IReadOnlyList<BlockPos> BlockEntities { get; private set; } = [];

    /// <summary>Reads a schematic file. Safe on any thread.</summary>
    /// <exception cref="FileNotFoundException">The file does not exist.</exception>
    /// <exception cref="InvalidDataException">The file is not a schematic, or is too large.</exception>
    public static SchematicPlacer Load(string path, SchematicOptions? options)
    {
        options ??= new SchematicOptions();
        if (options.Angle is not (0 or 90 or 180 or 270))
        {
            throw new ArgumentException($"A schematic turns by 0, 90, 180 or 270 degrees, not {options.Angle}.", nameof(options));
        }

        if (options.MaxTicks <= 0) throw new ArgumentOutOfRangeException(nameof(options), options.MaxTicks, "MaxTicks must be positive.");

        string file = DataFileSet.ResolveFixture(path, "schematic");
        string error = "";
        BlockSchematic? schematic = BlockSchematic.LoadFromFile(file, ref error);
        if (schematic == null) throw new InvalidDataException($"Could not read the schematic {file}: {error}");
        if (schematic.SizeX >= MaxSize || schematic.SizeY >= MaxSize || schematic.SizeZ >= MaxSize)
        {
            throw new InvalidDataException($"The schematic {file} is {schematic.SizeX}x{schematic.SizeY}x{schematic.SizeZ}; the game places at most {MaxSize - 1} blocks on a side.");
        }

        return new SchematicPlacer(schematic, options);
    }

    /// <summary>Works out the box and the chunks under it. Game thread.</summary>
    public void Prepare(ICoreServerAPI api, BlockPos origin)
    {
        IWorldAccessor world = api.World;

        // Renamed blocks first, by the codes the file was saved with: a rotation drops what it cannot resolve.
        if (BlockSchematic.BlockRemaps != null && BlockSchematic.ItemRemaps != null) _original.Remap();
        _missing = [.. _original.BlockCodes.Values.Where(code => world.GetBlock(code) == null).Distinct()];
        if (_missing.Count > 0 && !_options.AllowMissingBlocks)
        {
            throw new InvalidOperationException(
                $"The schematic uses {_missing.Count} block(s) the game does not know: {string.Join(", ", _missing)}. " +
                "Load the mods that add them, or set SchematicOptions.AllowMissingBlocks to leave them out.");
        }

        _rotated = _original.ClonePacked();
        if (_options.Angle != 0) _rotated.TransformWhilePacked(world, _options.Origin, _options.Angle);
        _start = _rotated.GetStartPos(origin, _options.Origin);

        BlockPos end = _start.AddCopy(_rotated.SizeX - 1, _rotated.SizeY - 1, _rotated.SizeZ - 1);
        if (!ChunkRegion.InWorld(world.BlockAccessor, _start) || !ChunkRegion.InWorld(world.BlockAccessor, end))
        {
            throw new ArgumentOutOfRangeException(nameof(origin), $"The schematic's box from {_start} to {end} leaves the world.");
        }

        Chunks = ChunkRegion.Covering(_start, end);
    }

    /// <summary>Places the schematic, once the chunks under it are loaded. Game thread.</summary>
    /// <param name="api">The server's API.</param>
    /// <param name="readBack">Whether to record what the server now has under the box, for a client to compare with.</param>
    public SchematicPlacement Place(ICoreServerAPI api, bool readBack)
    {
        if (_rotated == null || _start == null) throw new InvalidOperationException("Prepare the placement first.");
        IWorldAccessor world = api.World;

        // As WorldEdit pastes: a revertable accessor holds the blocks back until the commit, so the
        // block entities are made afterwards, on the blocks that are really there.
        IBlockAccessorRevertable accessor = world.GetBlockAccessorRevertable(true, true);
        int placed = _rotated.Place(accessor, world, _start, _options.ReplaceMode, replaceMetaBlocks: true);
        _rotated.PlaceDecors(accessor, _start);
        accessor.Commit();

        // A rotation repacks the blocks under their ids in this world, into the same maps the file
        // keyed by its own ids. Block entity contents and entities still carry the file's ids: they
        // are resolved through the file's maps.
        _rotated.BlockCodes = new Dictionary<int, AssetLocation>(_original.BlockCodes);
        _rotated.ItemCodes = new Dictionary<int, AssetLocation>(_original.ItemCodes);
        _rotated.PlaceEntitiesAndBlockEntities(accessor, world, _start, _rotated.BlockCodes, _rotated.ItemCodes, false, null, 0, null, true);
        accessor.CommitBlockEntityData();

        if (readBack) ReadBack(world.BlockAccessor);
        return new SchematicPlacement(_start.Copy(), new Vec3i(_rotated.SizeX, _rotated.SizeY, _rotated.SizeZ), placed, Chunks, _missing);
    }

    private void ReadBack(IBlockAccessor blocks)
    {
        // Every position of the box for ReplaceAll, which clears it first; else what the schematic holds.
        IEnumerable<BlockPos> positions = _options.ReplaceMode == EnumReplaceMode.ReplaceAll
            ? Box()
            : _rotated!.Indices.Select(index => _start!.AddCopy((int)(index & 0x3FF), (int)((index >> 20) & 0x3FF), (int)((index >> 10) & 0x3FF)));

        List<(BlockPos Pos, int BlockId, int FluidId)> expected = [];
        List<BlockPos> blockEntities = [];
        foreach (BlockPos pos in positions)
        {
            expected.Add(Read(blocks, pos));
            if (blocks.GetBlockEntity(pos) != null) blockEntities.Add(pos);
        }

        Expected = [.. expected];
        BlockEntities = blockEntities;
    }

    /// <summary>The block and the fluid at <paramref name="pos"/>.</summary>
    public static (BlockPos Pos, int BlockId, int FluidId) Read(IBlockAccessor blocks, BlockPos pos) =>
        (pos, blocks.GetBlock(pos, BlockLayersAccess.Solid).BlockId, blocks.GetBlock(pos, BlockLayersAccess.Fluid).BlockId);

    private IEnumerable<BlockPos> Box()
    {
        for (int x = 0; x < _rotated!.SizeX; x++)
        {
            for (int y = 0; y < _rotated.SizeY; y++)
            {
                for (int z = 0; z < _rotated.SizeZ; z++)
                {
                    yield return _start!.AddCopy(x, y, z);
                }
            }
        }
    }
}
