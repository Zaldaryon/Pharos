namespace Zaldaryon.Pharos.XUnit;

/// <summary>
/// Marks a test method as a client-server scenario (Fact-based).
/// The test class should inherit from <see cref="ClientServerScenarioBase"/> for lifecycle management.
/// </summary>
/// <remarks>
/// <para>
/// Client-server scenarios coordinate a headless client and an embedded server in lockstep.
/// The server boots in a sandbox, the client connects over loopback, and teardown is clean.
/// </para>
/// <para>
/// Use <see cref="ServerWorldAttribute"/> to configure world parameters.
/// Use <see cref="ServerModsAttribute"/> to stage mods before server bootstrap.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
public sealed class ClientServerScenarioAttribute : Xunit.FactAttribute
{
    /// <summary>
    /// The default timeout in milliseconds for client-server scenario tests (180 seconds).
    /// </summary>
    public const int DefaultTimeoutMs = 180_000;

    /// <summary>
    /// Gets or sets the world isolation mode for this test.
    /// When not set, uses <see cref="WorldIsolation.Rollback"/>.
    /// </summary>
    public WorldIsolation Isolation { get; set; } = WorldIsolation.Rollback;

    /// <summary>
    /// Gets or sets the watchdog timeout in milliseconds.
    /// When not set, uses <see cref="DefaultTimeoutMs"/> (180 seconds).
    /// </summary>
    public int TimeoutMs { get; set; } = DefaultTimeoutMs;

    /// <summary>
    /// Gets or sets whether to wait for the player to fully join before the test starts.
    /// When true (default), InitializeAsync blocks until the player has spawned into the world.
    /// </summary>
    public bool WaitForPlayerJoin { get; set; } = true;

    /// <summary>
    /// Initializes a new instance of the <see cref="ClientServerScenarioAttribute"/> class.
    /// </summary>
    public ClientServerScenarioAttribute()
    {
    }
}
