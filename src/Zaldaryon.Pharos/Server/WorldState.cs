using System.Globalization;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.Server;

namespace Zaldaryon.Pharos.Server;

/// <summary>
/// The state of a cube of a server's world, as the game stores it: block and fluid codes, the trees of the block entities,
/// and the entities that are saved with their chunks. Each fact is one entry, keyed by its path, so two states can be
/// compared place by place.
/// </summary>
/// <remarks>
/// Captured on the game thread, and read only: capturing changes nothing in the world. Players are left out, because the game
/// does not save them with their chunks. Entity positions are compared to a thousandth of a block, and the entity's motion is not
/// captured at all, so a moving entity does not differ from itself between two captures.
/// </remarks>
/// <remarks>
/// The mod data is read as it is stored. A chunk or region keeps the mod data a mod changed in memory until the game saves it, so
/// capture after <see cref="EmbeddedServerHost.SaveWorldAsync()"/> to compare what the save holds.
/// </remarks>
public sealed class WorldState
{
    // Written by the game when an entity is serialized, not part of the entity's state.
    private const string ExtraInfoText = "extraInfoText";
    private const int MaxBlocks = 50_000;

    private readonly IReadOnlyDictionary<string, string> _entries;

    private WorldState(BlockPos center, int radius, IReadOnlyDictionary<string, string> entries)
    {
        Center = center;
        Radius = radius;
        _entries = entries;
    }

    /// <summary>The block the cube is centred on.</summary>
    public BlockPos Center { get; }

    /// <summary>How many blocks from the centre the cube reaches on each axis.</summary>
    public int Radius { get; }

    /// <summary>Every captured entry, keyed by its path, such as <c>block 513,4,512</c> or <c>block entity 513,4,512/inventory/0</c>.</summary>
    public IReadOnlyDictionary<string, string> Entries => _entries;

    /// <summary>Captures the cube of blocks of radius <paramref name="radius"/> around <paramref name="around"/>.</summary>
    /// <param name="host">The running server.</param>
    /// <param name="around">The block the cube is centred on.</param>
    /// <param name="radius">How many blocks from the centre the cube reaches, on each axis. Between 0 and 16.</param>
    /// <exception cref="ArgumentNullException"><paramref name="host"/> or <paramref name="around"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="radius"/> is outside 0 to 16.</exception>
    /// <exception cref="InvalidOperationException">The server is not running.</exception>
    public static WorldState Capture(EmbeddedServerHost host, BlockPos around, int radius = 2)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(around);
        ArgumentOutOfRangeException.ThrowIfNegative(radius);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(radius, 16);
        if (!host.IsRunning) throw new InvalidOperationException("The server is not running, so its world cannot be captured.");

        Dictionary<string, string> entries = host.RunOnGameThread(() => CaptureOnGameThread(host, around, radius));
        return new WorldState(around.Copy(), radius, entries);
    }

    // A state from entries that were captured or written by hand, for testing the comparison.
    internal static WorldState FromEntries(BlockPos center, int radius, IReadOnlyDictionary<string, string> entries) =>
        new(center.Copy(), radius, new Dictionary<string, string>(entries, StringComparer.Ordinal));

    /// <summary>The entries that differ between two states, as readable lines, sorted by path.</summary>
    /// <param name="before">The earlier state.</param>
    /// <param name="after">The later state.</param>
    /// <param name="ignore">Paths to leave out: an entry is ignored when its path starts with one of them.</param>
    /// <exception cref="ArgumentNullException"><paramref name="before"/> or <paramref name="after"/> is null.</exception>
    public static IReadOnlyList<string> Differences(WorldState before, WorldState after, params string[] ignore)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);

        var keys = new SortedSet<string>(StringComparer.Ordinal);
        keys.UnionWith(before._entries.Keys);
        keys.UnionWith(after._entries.Keys);

        var lines = new List<string>();
        foreach (string key in keys)
        {
            if (ignore.Any(prefix => key.StartsWith(prefix, StringComparison.Ordinal))) continue;

            string b = before._entries.TryGetValue(key, out string? bv) ? bv : "(missing)";
            string a = after._entries.TryGetValue(key, out string? av) ? av : "(missing)";
            if (b != a) lines.Add($"{key}: {b} -> {a}");
        }

        return lines;
    }

    private static Dictionary<string, string> CaptureOnGameThread(EmbeddedServerHost host, BlockPos around, int radius)
    {
        ICoreServerAPI api = (ICoreServerAPI)host.Server.Api;
        IBlockAccessor blocks = api.World.BlockAccessor;
        var entries = new Dictionary<string, string>(StringComparer.Ordinal);

        int side = 2 * radius + 1;
        if ((long)side * side * side > MaxBlocks) throw new InvalidOperationException($"A cube of radius {radius} holds more than {MaxBlocks} blocks.");

        for (int dx = -radius; dx <= radius; dx++)
        {
            for (int dy = -radius; dy <= radius; dy++)
            {
                for (int dz = -radius; dz <= radius; dz++)
                {
                    var pos = new BlockPos(around.X + dx, around.InternalY + dy, around.Z + dz);
                    string where = $"{pos.X},{pos.InternalY},{pos.Z}";

                    entries[$"block {where}"] = blocks.GetBlock(pos, BlockLayersAccess.Solid).Code.ToString();
                    string fluid = blocks.GetBlock(pos, BlockLayersAccess.Fluid).Code.ToString();
                    if (fluid != "game:air") entries[$"fluid {where}"] = fluid;

                    BlockEntity? be = blocks.GetBlockEntity(pos);
                    if (be == null) continue;

                    entries[$"block entity {where}"] = be.GetType().Name;
                    var tree = new TreeAttribute();
                    be.ToTreeAttributes(tree);
                    Flatten($"block entity {where}", tree, entries);
                }
            }
        }

        // The mod data of the save, of each chunk of the cube, of its map chunks and of its map regions.
        if (api.WorldManager.SaveGame is SaveGame save && save.ModData is { } saveData)
        {
            AddModData(entries, "savegame", saveData);
        }

        var chunks = new HashSet<long>();
        var columns = new HashSet<(int, int)>();
        var regions = new HashSet<(int, int)>();
        for (int dx = -radius; dx <= radius; dx++)
        {
            for (int dy = -radius; dy <= radius; dy++)
            {
                for (int dz = -radius; dz <= radius; dz++)
                {
                    var pos = new BlockPos(around.X + dx, around.InternalY + dy, around.Z + dz);
                    chunks.Add(host.Server.WorldMap.ChunkIndex3D(pos.X / GlobalConstants.ChunkSize, pos.InternalY / GlobalConstants.ChunkSize, pos.Z / GlobalConstants.ChunkSize));
                    columns.Add((pos.X / GlobalConstants.ChunkSize, pos.Z / GlobalConstants.ChunkSize));
                    regions.Add((pos.X / MagicNum.MapRegionSize, pos.Z / MagicNum.MapRegionSize));
                }
            }
        }

        foreach (long index in chunks)
        {
            if (host.Server.GetLoadedChunk(index) is { } chunk && chunk.ModData is { } chunkData)
            {
                Vintagestory.Common.Database.ChunkPos at = host.Server.WorldMap.ChunkPosFromChunkIndex3D(index);
                AddModData(entries, $"chunk {at.X},{at.Y},{at.Z}", chunkData);
            }
        }

        foreach ((int cx, int cz) in columns)
        {
            if (host.Server.WorldMap.GetMapChunk(cx, cz) is ServerMapChunk mapChunk && mapChunk.Moddata is { } mapData)
            {
                AddModData(entries, $"map chunk {cx},{cz}", mapData);
            }
        }

        foreach ((int rx, int rz) in regions)
        {
            if (host.Server.WorldMap.GetMapRegion(rx, rz) is ServerMapRegion region && region.ModData is { } regionData)
            {
                AddModData(entries, $"map region {rx},{rz}", regionData);
            }
        }

        foreach (Entity entity in api.World.LoadedEntities.Values)
        {
            if (entity is EntityPlayer || !entity.Alive || !entity.StoreWithChunk) continue;
            if (!InCube(entity.Pos, around, radius)) continue;

            string prefix = $"entity {entity.EntityId}";
            entries[$"{prefix} code"] = entity.Code.ToString();
            entries[$"{prefix} pos"] = $"{Format(entity.Pos.X)},{Format(entity.Pos.Y)},{Format(entity.Pos.Z)}";
            entries[$"{prefix} yaw"] = Format(entity.Pos.Yaw);
            entries[$"{prefix} dimension"] = entity.Pos.Dimension.ToString(CultureInfo.InvariantCulture);
            Flatten($"{prefix}/attributes", entity.Attributes, entries);
            Flatten($"{prefix}/watched", entity.WatchedAttributes, entries, skip: ExtraInfoText);
        }

        return entries;
    }

    private static bool InCube(EntityPos pos, BlockPos around, int radius) =>
        Math.Abs(pos.X - (around.X + 0.5)) <= radius + 0.5
        && Math.Abs(pos.Y - (around.InternalY + 0.5)) <= radius + 0.5
        && Math.Abs(pos.Z - (around.Z + 0.5)) <= radius + 0.5;

    // Every key of a tree, down to its leaves. A subtree is walked under its own path, so a changed leaf keeps its place.
    private static void Flatten(string path, TreeAttribute tree, IDictionary<string, string> entries, string? skip = null)
    {
        foreach (string key in tree.Keys)
        {
            if (key == skip) continue;

            string leaf = $"{path}/{key}";
            IAttribute attribute = tree[key];
            switch (attribute)
            {
                case TreeAttribute child:
                    Flatten(leaf, child, entries);
                    break;
                case ItemstackAttribute stack:
                    entries[leaf] = Describe(stack.value);
                    break;
                default:
                    entries[leaf] = Describe(attribute.GetValue());
                    break;
            }
        }
    }

    // The mod data of a save, a chunk or a region. An entry with no bytes is left out: the game writes an empty entry when it
    // unloads a map region at shutdown, as the cloth manager does for its cloth systems, so an empty entry holds nothing a save keeps.
    private static void AddModData(IDictionary<string, string> entries, string owner, IReadOnlyDictionary<string, byte[]> data)
    {
        foreach (KeyValuePair<string, byte[]> datum in data)
        {
            if (datum.Value.Length > 0) entries[$"{owner} mod data {datum.Key}"] = Describe(datum.Value);
        }
    }

    private static string Describe(object? value) => value switch
    {
        null => "null",
        ItemStack stack => stack.Collectible is null ? "unresolved" : $"{stack.StackSize}x {stack.Collectible.Code}",
        byte[] bytes => Convert.ToHexString(bytes),
        double d => Format(d),
        float f => Format(f),
        _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? "null",
    };

    // A thousandth of a block: the precision the comparison holds positions to.
    private static string Format(double value) => value.ToString("F3", CultureInfo.InvariantCulture);
}
