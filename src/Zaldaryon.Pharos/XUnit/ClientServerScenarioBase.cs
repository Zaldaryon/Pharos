using Vintagestory.Server;
using Xunit;
using Zaldaryon.Pharos.Bootstrap;
using Zaldaryon.Pharos.Core;
using Zaldaryon.Pharos.Player;
using Zaldaryon.Pharos.Server;

namespace Zaldaryon.Pharos.XUnit;

/// <summary>
/// Abstract base class for client-server scenario tests.
/// Provides cooperative lifecycle management for a headless client and embedded server.
/// </summary>
/// <remarks>
/// <para>
/// Test classes inheriting from this should be marked with <see cref="ClientServerScenarioAttribute"/>
/// and use <see cref="IAsyncLifetime"/> for setup and teardown.
/// </para>
/// <para>
/// The lifecycle sequence:
/// <list type="number">
/// <item>Server boots in a sandbox with loopback networking</item>
/// <item>Headless client initializes and connects over loopback</item>
/// <item>Session waits for player to join (configurable)</item>
/// <item>Test executes with full client-server synchronization</item>
/// <item>Clean teardown: client disconnects, server stops, sandbox cleaned</item>
/// </list>
/// </para>
/// <para>
/// Uses separate platform lock acquisition sequences to prevent deadlocks when both
/// client and server compete for shared resources.
/// </para>
/// </remarks>
public abstract class ClientServerScenarioBase : IAsyncLifetime
{
    private ServerSandbox? _sandbox;
    private EmbeddedServerHost? _serverHost;
    private HeadlessClient? _client;
    private ClientServerLoopbackSession? _session;
    private bool _disposed;

    /// <summary>
    /// Gets the headless client instance.
    /// Null until <see cref="InitializeAsync"/> completes.
    /// </summary>
    protected HeadlessClient? Client => _client;

    /// <summary>
    /// Gets the embedded server host managing the test server instance.
    /// Null until <see cref="InitializeAsync"/> completes.
    /// </summary>
    protected EmbeddedServerHost? ServerHost => _serverHost;

    /// <summary>
    /// Gets the underlying Vintage Story server instance.
    /// Null if the host is not initialized.
    /// </summary>
    protected ServerMain? Server => _serverHost?.Server;

    /// <summary>
    /// Gets the loopback session coordinating client and server.
    /// Null until <see cref="InitializeAsync"/> completes.
    /// </summary>
    protected ClientServerLoopbackSession? Session => _session;

    /// <summary>
    /// Gets the client's test player interface.
    /// Null until the client is connected.
    /// </summary>
    protected IClientTestPlayer? Player => _client?.TestPlayer;

    /// <summary>
    /// Gets whether the client has connected to the server.
    /// </summary>
    protected bool IsConnected => _session?.IsConnected ?? false;

    /// <summary>
    /// Gets the world isolation mode used for tests in this class.
    /// Override in derived classes to change isolation behavior.
    /// </summary>
    protected virtual WorldIsolation WorldIsolation => WorldIsolation.Rollback;

    /// <summary>
    /// Gets the world configuration options for this scenario.
    /// Override to customize world settings like seed, play style, or world type.
    /// </summary>
    protected virtual ServerWorldOptions WorldOptions => new();

    /// <summary>
    /// Gets the client configuration options for this scenario.
    /// Override to customize headless client settings.
    /// </summary>
    protected virtual HeadlessClientOptions ClientOptions => new();

    /// <summary>
    /// Gets the timeout for waiting for the player to join.
    /// Default is 60 seconds.
    /// </summary>
    protected virtual TimeSpan PlayerJoinTimeout => TimeSpan.FromSeconds(60);

    /// <summary>
    /// Gets whether to wait for the player to fully join during initialization.
    /// Default is true. Override to false for tests that need to control the join sequence.
    /// </summary>
    protected virtual bool WaitForPlayerJoinOnInit => true;

    /// <summary>
    /// Gets the player name used for the loopback connection.
    /// Default is "PharosTest".
    /// </summary>
    protected virtual string PlayerName => "PharosTest";

    /// <summary>
    /// Initializes the client-server scenario: boots server, connects client, waits for player join.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The initialization sequence prevents platform lock deadlocks by:
    /// <list type="bullet">
    /// <item>Booting the server first with its own sandbox</item>
    /// <item>Initializing the client separately</item>
    /// <item>Connecting via loopback after both are ready</item>
    /// </list>
    /// </para>
    /// </remarks>
    public virtual async Task InitializeAsync()
    {
        // Create sandbox for isolated server storage
        _sandbox = new ServerSandbox();

        // Boot embedded server in sandbox
        _serverHost = EmbeddedServerHost.Boot(_sandbox, WorldOptions);

        // Initialize headless client (separate platform init)
        _client = HeadlessClientBootstrap.Boot(ClientOptions);

        // Create loopback session connecting client to server
        _session = _client.ConnectLoopback(_serverHost, PlayerName);

        // Optionally wait for player to fully join
        if (WaitForPlayerJoinOnInit)
        {
            bool joined = await _session.WaitForPlayerJoinedAsync(PlayerJoinTimeout).ConfigureAwait(false);
            if (!joined)
            {
                throw new TimeoutException(
                    $"Player did not join within {PlayerJoinTimeout.TotalSeconds} seconds. " +
                    "Override WaitForPlayerJoinOnInit to false for manual join control.");
            }
        }
    }

    /// <summary>
    /// Tears down the client-server scenario: disconnects client, stops server, cleans sandbox.
    /// </summary>
    public virtual async Task DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        // Disconnect and dispose session
        try
        {
            _session?.Dispose();
        }
        catch
        {
            // Ignore session dispose errors during teardown
        }
        _session = null;

        // Dispose client
        try
        {
            _client?.Dispose();
        }
        catch
        {
            // Ignore client dispose errors during teardown
        }
        _client = null;

        // Dispose server host
        try
        {
            if (_serverHost is not null)
            {
                await _serverHost.DisposeAsync().ConfigureAwait(false);
            }
        }
        catch
        {
            // Ignore server dispose errors during teardown
        }
        _serverHost = null;

        // Cleanup sandbox
        try
        {
            _sandbox?.Dispose();
        }
        catch
        {
            // Ignore sandbox cleanup errors during teardown
        }
        _sandbox = null;
    }

    /// <summary>
    /// Advances the server by one tick and the client by one frame in lockstep.
    /// </summary>
    /// <param name="dt">Delta time for the frame. Default is 1/60 second.</param>
    protected void Step(float dt = 1f / 60f)
    {
        if (_session is null)
        {
            throw new InvalidOperationException("Session is not initialized. Call InitializeAsync first.");
        }

        _session.Step(dt);
    }

    /// <summary>
    /// Advances the server and client by N frames in lockstep.
    /// </summary>
    /// <param name="count">Number of frames to step.</param>
    /// <param name="dt">Delta time per frame. Default is 1/60 second.</param>
    protected void StepFrames(int count, float dt = 1f / 60f)
    {
        if (_session is null)
        {
            throw new InvalidOperationException("Session is not initialized. Call InitializeAsync first.");
        }

        _session.StepFrames(count, dt);
    }

    /// <summary>
    /// Asynchronously advances until a condition is met or max frames reached.
    /// </summary>
    /// <param name="condition">Condition to wait for.</param>
    /// <param name="maxFrames">Maximum frames to step. Default is 600 (10 seconds at 60 FPS).</param>
    /// <param name="dt">Delta time per frame. Default is 1/60 second.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>True if condition was met; false if maxFrames reached.</returns>
    protected async Task<bool> StepUntilAsync(
        Func<bool> condition,
        int maxFrames = 600,
        float dt = 1f / 60f,
        CancellationToken ct = default)
    {
        if (_session is null)
        {
            throw new InvalidOperationException("Session is not initialized. Call InitializeAsync first.");
        }

        for (int i = 0; i < maxFrames; i++)
        {
            ct.ThrowIfCancellationRequested();

            if (condition())
            {
                return true;
            }

            await _session.StepAsync(dt, ct).ConfigureAwait(false);
        }

        return condition();
    }

    /// <summary>
    /// Waits for the world to be ready with chunks loaded and meshed around the player.
    /// </summary>
    /// <param name="radius">Chunk radius around the player. Default is 1.</param>
    /// <param name="maxFrames">Maximum frames to wait. Default is 600.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>True if world became ready; throws TimeoutException otherwise.</returns>
    protected Task<bool> WaitForWorldReadyAsync(int radius = 1, int maxFrames = 600, CancellationToken ct = default)
    {
        if (_session is null)
        {
            throw new InvalidOperationException("Session is not initialized. Call InitializeAsync first.");
        }

        return _session.WaitForWorldReadyAsync(radius, maxFrames, ct: ct);
    }
}
