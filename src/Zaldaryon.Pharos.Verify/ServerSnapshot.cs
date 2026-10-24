using Vintagestory.API.MathTools;
using Zaldaryon.Pharos.Server;

namespace Zaldaryon.Pharos.Verify;

/// <summary>Compatibility entry point. Prefer <c>host.Snapshot.Region(min, max)</c>.</summary>
public static class ServerSnapshot
{
    /// <summary>Captures the same stable region text as <see cref="RegionSnapshotDriver.Region"/>.</summary>
    public static string Region(EmbeddedServerHost host, BlockPos min, BlockPos max)
    {
        ArgumentNullException.ThrowIfNull(host);
        return host.Snapshot.Region(min, max);
    }
}
