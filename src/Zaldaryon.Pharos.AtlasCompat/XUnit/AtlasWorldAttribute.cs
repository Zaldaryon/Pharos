using Zaldaryon.Pharos.XUnit;

namespace Atlas.XUnit;

/// <summary>
/// Compatibility shim for Atlas.XUnit.AtlasWorldAttribute.
/// Provides the same world configuration semantics as <see cref="ServerWorldAttribute"/> in Pharos.
/// </summary>
/// <remarks>
/// <para>
/// This attribute provides source compatibility for existing Atlas test suites.
/// For new code, use <see cref="ServerWorldAttribute"/> directly.
/// </para>
/// <para>
/// Atlas naming conventions are preserved: [AtlasWorld] maps to [ServerWorld] semantically.
/// </para>
/// </remarks>
[Obsolete("Use [ServerWorld] from Zaldaryon.Pharos.XUnit instead. This shim exists only for migration.")]
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public sealed class AtlasWorldAttribute : Attribute
{
    /// <summary>
    /// Gets the world seed for deterministic world generation.
    /// </summary>
    public int Seed { get; }

    /// <summary>
    /// Gets the play style identifier (e.g., "creativebuilding", "surviveandbuild").
    /// </summary>
    public string PlayStyle { get; }

    /// <summary>
    /// Gets the world type identifier (e.g., "superflat", "standard").
    /// </summary>
    public string WorldType { get; }

    /// <summary>
    /// Gets or sets optional JSON configuration to merge with world settings.
    /// </summary>
    public string? WorldConfigurationJson { get; set; }

    /// <summary>
    /// Gets or sets the world isolation mode for tests using this world configuration.
    /// When not set, uses <see cref="WorldIsolation.Rollback"/>.
    /// </summary>
    public WorldIsolation Isolation { get; set; } = WorldIsolation.Rollback;

    /// <summary>
    /// Initializes a new instance with the specified world parameters.
    /// </summary>
    /// <param name="seed">The world seed for deterministic world generation.</param>
    /// <param name="playStyle">The play style identifier (e.g., "creativebuilding").</param>
    /// <param name="worldType">The world type identifier (e.g., "superflat").</param>
    public AtlasWorldAttribute(int seed = 0, string playStyle = "creativebuilding", string worldType = "superflat")
    {
        Seed = seed;
        PlayStyle = playStyle ?? "creativebuilding";
        WorldType = worldType ?? "superflat";
    }

    /// <summary>
    /// Converts this Atlas attribute to a Pharos ServerWorldAttribute with equivalent settings.
    /// </summary>
    /// <returns>A ServerWorldAttribute with the same configuration.</returns>
    public ServerWorldAttribute ToPharosAttribute()
    {
        return new ServerWorldAttribute(Seed, PlayStyle, WorldType)
        {
            WorldConfigurationJson = WorldConfigurationJson,
            Isolation = Isolation
        };
    }
}
