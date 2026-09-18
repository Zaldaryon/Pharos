namespace Zaldaryon.Pharos.Server;

/// <summary>
/// Configuration options for creating a new server world with <see cref="EmbeddedServerHost"/>.
/// </summary>
/// <remarks>
/// This record provides a native alternative to Atlas.Api.WorldOptions, enabling standalone server hosting
/// without depending on the Pixnop.Atlas package.
/// </remarks>
public sealed record ServerWorldOptions
{
    /// <summary>
    /// The world seed string used for terrain generation.
    /// </summary>
    public string Seed { get; init; } = "424242";

    /// <summary>
    /// The display name for the world.
    /// </summary>
    public string WorldName { get; init; } = "PharosWorld";

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
}
