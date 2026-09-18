namespace Zaldaryon.Pharos.XUnit;

/// <summary>
/// Specifies world configuration parameters for server scenario tests.
/// Apply to a test class or individual test methods to configure the server world.
/// </summary>
/// <remarks>
/// <para>
/// Method-level attributes override class-level attributes.
/// </para>
/// <para>
/// When not specified, the default values are:
/// <list type="bullet">
/// <item><see cref="Seed"/>: 0 (deterministic)</item>
/// <item><see cref="PlayStyle"/>: "creativebuilding"</item>
/// <item><see cref="WorldType"/>: "superflat"</item>
/// </list>
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public sealed class ServerWorldAttribute : Attribute
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
    public ServerWorldAttribute(int seed = 0, string playStyle = "creativebuilding", string worldType = "superflat")
    {
        Seed = seed;
        PlayStyle = playStyle ?? "creativebuilding";
        WorldType = worldType ?? "superflat";
    }
}
