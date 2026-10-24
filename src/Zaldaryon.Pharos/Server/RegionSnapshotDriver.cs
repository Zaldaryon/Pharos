using System.Globalization;
using System.Text;
using Newtonsoft.Json;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Zaldaryon.Pharos.World;

namespace Zaldaryon.Pharos.Server;

/// <summary>Captures bounded, loaded regions on the server game thread.</summary>
public sealed class RegionSnapshotDriver
{
    private readonly EmbeddedServerHost _host;
    internal RegionSnapshotDriver(EmbeddedServerHost host) => _host = host;

    /// <summary>
    /// Inclusive region as block-code grids and block-entity trees. As in <see cref="Scene"/>, columns
    /// increase X, rows increase Z and layers increase Y. Both solid and fluid layers are included.
    /// </summary>
    /// <remarks>Requires loaded chunks in the main dimension. At most 65,536 cells, like Scene.</remarks>
    public string Region(BlockPos min, BlockPos max)
    {
        ArgumentNullException.ThrowIfNull(min);
        ArgumentNullException.ThrowIfNull(max);
        BlockPos start = min.Copy(), end = max.Copy();
        long width = (long)end.X - start.X + 1, height = (long)end.Y - start.Y + 1, depth = (long)end.Z - start.Z + 1;
        if (start.dimension != 0 || end.dimension != 0 || width <= 0 || height <= 0 || depth <= 0 ||
            width > Scene.MaxCells || height > Scene.MaxCells || depth > Scene.MaxCells || width * height * depth > Scene.MaxCells)
            throw new ArgumentOutOfRangeException(nameof(max), "Snapshot bounds must be ordered, in the main dimension, and contain at most 65,536 cells.");
        return _host.RunOnGameThread(() =>
        {
            Scene.EnsureLoaded(_host.Api, start, end);
            var blocks = _host.Api.World.BlockAccessor;
            var text = new StringBuilder();
            text.Append(CultureInfo.InvariantCulture, $"Region ({start.X},{start.Y},{start.Z})..({end.X},{end.Y},{end.Z})\n");
            foreach (var (name, layer) in new[] { ("Solid", BlockLayersAccess.Solid), ("Fluid", BlockLayersAccess.Fluid) })
            {
                for (int dy = 0; dy < height; dy++)
                {
                    text.Append(CultureInfo.InvariantCulture, $"{name} Y+{dy}\n");
                    for (int dz = 0; dz < depth; dz++)
                    {
                        for (int dx = 0; dx < width; dx++)
                        {
                            if (dx > 0) text.Append(" | ");
                            text.Append(blocks.GetBlock(start.AddCopy(dx, dy, dz), layer).Code.ToString());
                        }
                        text.Append('\n');
                    }
                }
            }
            text.Append("BlockEntities\n");
            for (int dy = 0; dy < height; dy++)
                for (int dz = 0; dz < depth; dz++)
                    for (int dx = 0; dx < width; dx++)
                    {
                        var entity = blocks.GetBlockEntity(start.AddCopy(dx, dy, dz));
                        if (entity is null) continue;
                        TreeAttribute tree = new();
                        entity.ToTreeAttributes(tree);
                        text.Append(CultureInfo.InvariantCulture, $"({dx},{dy},{dz}) {entity.GetType().Name}\n");
                        text.Append(JsonConvert.SerializeObject(AttributeSnapshot.Capture(tree), Formatting.Indented)).Append('\n');
                    }
            return text.ToString().Replace("\r\n", "\n", StringComparison.Ordinal);
        });
    }
}
