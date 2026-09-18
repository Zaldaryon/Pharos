using Zaldaryon.Pharos.XUnit;

namespace Atlas.XUnit;

/// <summary>
/// Compatibility shim for Atlas.XUnit.AtlasModsAttribute.
/// Provides the same mod staging semantics as <see cref="ServerModsAttribute"/> in Pharos.
/// </summary>
/// <remarks>
/// <para>
/// This attribute provides source compatibility for existing Atlas test suites.
/// For new code, use <see cref="ServerModsAttribute"/> directly.
/// </para>
/// <para>
/// Atlas naming conventions are preserved: [AtlasMods] maps to [ServerMods] semantically.
/// </para>
/// </remarks>
[Obsolete("Use [ServerMods] from Zaldaryon.Pharos.XUnit instead. This shim exists only for migration.")]
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Assembly, AllowMultiple = true)]
public sealed class AtlasModsAttribute : Attribute
{
    /// <summary>
    /// Gets the list of mod paths declared by this attribute.
    /// Paths may be absolute or relative to the test assembly directory.
    /// Supports directories, zip files, and individual mod files.
    /// </summary>
    public IReadOnlyList<string> ModPaths { get; }

    /// <summary>
    /// Initializes a new instance declaring one or more mod paths to stage.
    /// </summary>
    /// <param name="modPaths">One or more paths to mods (directories, zips, or files).</param>
    public AtlasModsAttribute(params string[] modPaths)
    {
        ModPaths = modPaths ?? Array.Empty<string>();
    }

    /// <summary>
    /// Converts this Atlas attribute to a Pharos ServerModsAttribute with equivalent settings.
    /// </summary>
    /// <returns>A ServerModsAttribute with the same mod paths.</returns>
    public ServerModsAttribute ToPharosAttribute()
    {
        return new ServerModsAttribute(ModPaths.ToArray());
    }
}
