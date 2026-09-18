using Zaldaryon.Pharos.Server;

namespace Atlas.Api;

/// <summary>
/// Compatibility shim for Atlas.Api.WorldOptions.
/// Maps to <see cref="ServerWorldOptions"/> in Pharos.
/// </summary>
/// <remarks>
/// <para>
/// This record provides source compatibility for existing Atlas test suites.
/// For new code, use <see cref="ServerWorldOptions"/> directly.
/// </para>
/// </remarks>
[Obsolete("Use ServerWorldOptions from Zaldaryon.Pharos.Server instead. This shim exists only for migration.")]
public sealed record WorldOptions
{
    /// <summary>
    /// The world seed string used for terrain generation.
    /// </summary>
    public string Seed { get; init; } = "424242";

    /// <summary>
    /// The display name for the world.
    /// </summary>
    public string WorldName { get; init; } = "AtlasWorld";

    /// <summary>
    /// The play style identifier (e.g., "creativebuilding", "surviveandbuild").
    /// </summary>
    public string PlayStyle { get; init; } = "creativebuilding";

    /// <summary>
    /// The world type identifier (e.g., "superflat", "standard").
    /// </summary>
    public string WorldType { get; init; } = "superflat";

    /// <summary>
    /// Optional explicit save file location. When null, a default path is computed from the data directory.
    /// </summary>
    public string? SaveFileLocation { get; init; }

    /// <summary>
    /// JSON string containing world configuration overrides. Defaults to an empty JSON object.
    /// </summary>
    public string WorldConfigurationJson { get; init; } = "{}";

    /// <summary>
    /// Converts this Atlas WorldOptions to a Pharos ServerWorldOptions.
    /// </summary>
    /// <returns>A ServerWorldOptions record with equivalent settings.</returns>
    public ServerWorldOptions ToServerWorldOptions()
    {
        return new ServerWorldOptions
        {
            Seed = Seed,
            WorldName = WorldName,
            PlayStyle = PlayStyle,
            WorldType = WorldType,
            SaveFileLocation = SaveFileLocation,
            WorldConfigurationJson = WorldConfigurationJson
        };
    }

    /// <summary>
    /// Creates a WorldOptions from a Pharos ServerWorldOptions.
    /// </summary>
    /// <param name="options">The Pharos server world options to convert.</param>
    /// <returns>An Atlas-compatible WorldOptions record.</returns>
    public static WorldOptions FromServerWorldOptions(ServerWorldOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        return new WorldOptions
        {
            Seed = options.Seed,
            WorldName = options.WorldName,
            PlayStyle = options.PlayStyle,
            WorldType = options.WorldType,
            SaveFileLocation = options.SaveFileLocation,
            WorldConfigurationJson = options.WorldConfigurationJson
        };
    }
}
