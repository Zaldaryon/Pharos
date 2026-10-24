using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Zaldaryon.Pharos.Assertions;
using Zaldaryon.Pharos.Server;
using Zaldaryon.Pharos.Timing;

namespace Zaldaryon.Pharos.World;

/// <summary>Builds and checks small, readable block scenes in an embedded server world.</summary>
public static class Scene
{
    private const int MaxReportedDifferences = 10;
    internal const int MaxCells = 65_536;

    /// <summary>Places one horizontal layer of blocks. Diagram columns map to X and rows to Z.</summary>
    /// <param name="host">The running server.</param>
    /// <param name="origin">The block at the diagram's top-left corner.</param>
    /// <param name="legend">Characters and their block codes. <c>.</c> always means air.</param>
    /// <param name="rows">Equal-width rows, from low to high Z.</param>
    /// <remarks>The covered chunks must already be loaded. This synchronous method fails without making changes when any are missing.</remarks>
    public static void Build(EmbeddedServerHost host, BlockPos origin, IReadOnlyDictionary<char, string> legend, params string[] rows) =>
        Build(host, origin, legend, [rows]);

    /// <summary>Places stacked horizontal layers. Layer zero is at <paramref name="origin"/> Y.</summary>
    /// <remarks>Within each layer, columns map to X and rows map to increasing Z.</remarks>
    public static void Build(EmbeddedServerHost host, BlockPos origin, IReadOnlyDictionary<char, string> legend, params string[][] layers) =>
        Apply(host, origin, SceneLayout.Parse(legend, layers));

    /// <summary>Checks one horizontal layer against the world. Diagram columns map to X and rows to Z.</summary>
    /// <remarks>The covered chunks must already be loaded.</remarks>
    public static void Assert(EmbeddedServerHost host, BlockPos origin, IReadOnlyDictionary<char, string> legend, params string[] rows) =>
        Assert(host, origin, legend, [rows]);

    /// <summary>Checks stacked horizontal layers against the world, with layer zero at origin Y.</summary>
    /// <remarks>The covered chunks must already be loaded.</remarks>
    public static void Assert(EmbeddedServerHost host, BlockPos origin, IReadOnlyDictionary<char, string> legend, params string[][] layers)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(origin);
        SceneLayout layout = SceneLayout.Parse(legend, layers);
        host.RunOnGameThread(() =>
        {
            ICoreServerAPI api = (ICoreServerAPI)host.Server.Api;
            (BlockPos min, BlockPos max) = layout.Bounds(origin);
            EnsureLoaded(api, min, max);
            _ = ResolveBlocks(api.World, layout.BlockCodes);
            List<string> differences = [];
            int total = 0;
            foreach (SceneCell cell in layout.Cells)
            {
                BlockPos pos = origin.AddCopy(cell.X, cell.Y, cell.Z);
                string solidExpected = cell.BlockCode is { } expectedCode && !IsLiquid(api.World.GetBlock(new AssetLocation(expectedCode)))
                    ? expectedCode : "game:air";
                string fluidExpected = cell.BlockCode is { } fluidCode && IsLiquid(api.World.GetBlock(new AssetLocation(fluidCode)))
                    ? fluidCode : "game:air";
                string solidActual = api.World.BlockAccessor.GetBlock(pos, BlockLayersAccess.Solid).Code.ToString();
                string fluidActual = api.World.BlockAccessor.GetBlock(pos, BlockLayersAccess.Fluid).Code.ToString();
                if (!string.Equals(solidActual, solidExpected, StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(fluidActual, fluidExpected, StringComparison.OrdinalIgnoreCase))
                {
                    total++;
                    if (differences.Count < MaxReportedDifferences)
                        differences.Add($"({pos.X}, {pos.Y}, {pos.Z}): expected solid {solidExpected} / fluid {fluidExpected}, found solid {solidActual} / fluid {fluidActual}");
                }
            }

            if (differences.Count > 0)
            {
                throw new PharosAssertException($"Scene differs at {total} block(s): {string.Join("; ", differences)}{(total > differences.Count ? "; ..." : "")}");
            }
        });
    }

    private static void Apply(EmbeddedServerHost host, BlockPos origin, SceneLayout layout)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(origin);
        host.RunOnGameThread(() =>
        {
            ICoreServerAPI api = (ICoreServerAPI)host.Server.Api;
            (BlockPos min, BlockPos max) = layout.Bounds(origin);
            EnsureLoaded(api, min, max);

            // Resolve every block before the first write so invalid codes cannot leave a partial scene.
            Dictionary<string, int> blockIds = ResolveBlocks(api.World, layout.BlockCodes);
            foreach (SceneCell cell in layout.Cells)
            {
                BlockPos pos = origin.AddCopy(cell.X, cell.Y, cell.Z);
                if (cell.BlockCode is not { } code)
                {
                    api.World.BlockAccessor.SetBlock(0, pos);
                    api.World.BlockAccessor.SetBlock(0, pos, BlockLayersAccess.Fluid);
                }
                else if (IsLiquid(api.World.GetBlock(new AssetLocation(code))))
                {
                    api.World.BlockAccessor.SetBlock(0, pos);
                    api.World.BlockAccessor.SetBlock(blockIds[code], pos, BlockLayersAccess.Fluid);
                }
                else
                {
                    api.World.BlockAccessor.SetBlock(0, pos, BlockLayersAccess.Fluid);
                    api.World.BlockAccessor.SetBlock(blockIds[code], pos);
                }
            }
        });
    }

    private static Dictionary<string, int> ResolveBlocks(IWorldAccessor world, IReadOnlySet<string> codes)
    {
        Dictionary<string, int> result = new(StringComparer.OrdinalIgnoreCase);
        foreach (string code in codes)
        {
            Block block = world.GetBlock(new AssetLocation(code))
                ?? throw new InvalidOperationException($"The game does not define scene block '{code}'.");
            result.Add(code, block.BlockId);
        }

        return result;
    }

    private static bool IsLiquid(Block? block) => block?.ForFluidsLayer == true;

    internal static void EnsureLoaded(ICoreServerAPI api, BlockPos min, BlockPos max)
    {
        ChunkRegion.CheckInWorld(api, min, max);
        IReadOnlyList<ChunkPos> chunks = ChunkRegion.Covering(min, max);
        List<ChunkPos> missing = ChunkRegion.Missing(api, chunks);
        if (missing.Count > 0)
        {
            throw new InvalidOperationException($"Scene region has {missing.Count} unloaded chunk(s): {string.Join(", ", missing.Take(10))}{(missing.Count > 10 ? ", ..." : "")}. Load the chunks before building or asserting a scene.");
        }
    }
}

internal sealed record SceneCell(int X, int Y, int Z, string? BlockCode);

internal sealed class SceneLayout
{
    internal const int MaxCells = Scene.MaxCells;

    private SceneLayout(int width, int depth, int height, IReadOnlyList<SceneCell> cells, IReadOnlySet<string> blockCodes)
    {
        Width = width;
        Depth = depth;
        Height = height;
        Cells = cells;
        BlockCodes = blockCodes;
    }

    public int Width { get; }
    public int Depth { get; }
    public int Height { get; }
    public IReadOnlyList<SceneCell> Cells { get; }
    public IReadOnlySet<string> BlockCodes { get; }

    public static SceneLayout Parse(IReadOnlyDictionary<char, string> legend, IReadOnlyList<string[]> layers)
    {
        ArgumentNullException.ThrowIfNull(legend);
        ArgumentNullException.ThrowIfNull(layers);
        if (layers.Count == 0) throw new ArgumentException("A scene needs at least one layer.", nameof(layers));
        if (legend.ContainsKey('.')) throw new ArgumentException("The '.' character is reserved for air.", nameof(legend));

        List<SceneCell> cells = [];
        HashSet<string> codes = new(StringComparer.OrdinalIgnoreCase);
        long cellCount = 0;
        int? width = null;
        int? depth = null;
        for (int y = 0; y < layers.Count; y++)
        {
            string[] rows = layers[y] ?? throw new ArgumentException($"Scene layer {y} is null.", nameof(layers));
            if (rows.Length == 0) throw new ArgumentException($"Scene layer {y} has no rows.", nameof(layers));
            depth ??= rows.Length;
            if (rows.Length != depth) throw new ArgumentException($"Scene layer {y} has {rows.Length} rows; expected {depth}.", nameof(layers));

            for (int z = 0; z < rows.Length; z++)
            {
                string row = rows[z] ?? throw new ArgumentException($"Scene row {z} in layer {y} is null.", nameof(layers));
                if (row.Length == 0) throw new ArgumentException($"Scene row {z} in layer {y} is empty.", nameof(layers));
                cellCount += row.Length;
                if (cellCount > MaxCells) throw new ArgumentException($"A scene can contain at most {MaxCells} cells.", nameof(layers));
                width ??= row.Length;
                if (row.Length != width) throw new ArgumentException($"Scene row {z} in layer {y} has width {row.Length}; expected {width}.", nameof(layers));
                for (int x = 0; x < row.Length; x++)
                {
                    char symbol = row[x];
                    if (symbol == '.')
                    {
                        cells.Add(new SceneCell(x, y, z, null));
                        continue;
                    }

                    if (!legend.TryGetValue(symbol, out string? code) || string.IsNullOrWhiteSpace(code))
                        throw new ArgumentException($"Scene symbol '{symbol}' at column {x}, row {z}, layer {y} has no block code in the legend.", nameof(legend));
                    string normalized = new AssetLocation(code).ToString();
                    codes.Add(normalized);
                    cells.Add(new SceneCell(x, y, z, normalized));
                }
            }
        }

        return new SceneLayout(width ?? 0, depth ?? 0, layers.Count, cells, codes);
    }

    public (BlockPos Min, BlockPos Max) Bounds(BlockPos origin) =>
        (origin.Copy(), origin.AddCopy(Width - 1, Height - 1, Depth - 1));
}
