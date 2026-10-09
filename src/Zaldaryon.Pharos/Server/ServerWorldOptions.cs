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
    /// A save to start from (a <c>.vcdbs</c> file), relative to the working folder or to the test
    /// assembly's folder, or null for a new world. The server boots into a copy of it, so the file
    /// itself never changes. The save keeps its own seed, play style, world type and world
    /// configuration: those options do not apply to it. Cannot be combined with
    /// <see cref="SaveFileLocation"/>.
    /// </summary>
    public string? SaveFile { get; init; }

    /// <summary>
    /// JSON string containing world configuration overrides. Defaults to an empty JSON object.
    /// </summary>
    public string WorldConfigurationJson { get; init; } = "{}";

    /// <summary>
    /// A TCP and UDP port the server also listens on, for clients that connect over a real
    /// network, or null to accept only in-memory connections. 0 picks a free port; read it from
    /// <see cref="EmbeddedServerHost.Port"/>.
    /// </summary>
    public int? ListenPort { get; init; }

    /// <summary>The address <see cref="ListenPort"/> binds to. Loopback by default.</summary>
    public string ListenAddress { get; init; } = "127.0.0.1";

    /// <summary>
    /// Whether the server has the Vintage Story auth server validate each player who connects over
    /// the network, as a public server does. Off by default, so offline clients can join.
    /// In-memory connections are never verified.
    /// </summary>
    public bool VerifyPlayerAuth { get; init; }
}
