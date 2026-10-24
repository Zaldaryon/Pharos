using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Zaldaryon.Pharos.Server;

namespace Zaldaryon.Pharos.Verify;

public static class ServerSnapshot
{
    public static string Region(EmbeddedServerHost host, BlockPos min, BlockPos max)
    {
        var ba = host.Api.World.BlockAccessor;
        var sb = new System.Text.StringBuilder();
        for (int y = min.Y; y <= max.Y; y++)
        {
            sb.AppendLine($"Y={y}");
            for (int z = min.Z; z <= max.Z; z++)
            {
                for (int x = min.X; x <= max.X; x++)
                {
                    var block = ba.GetBlock(new BlockPos(x, y, z));
                    sb.Append((block?.Code?.ToString() ?? "?") + " ");
                }
                sb.AppendLine();
            }
        }
        return sb.ToString();
    }
}
