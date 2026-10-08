using Vintagestory.Server;
using Xunit;
using Zaldaryon.Pharos.Bootstrap;
using Zaldaryon.Pharos.Core;
using Zaldaryon.Pharos.Player;
using Zaldaryon.Pharos.Reporting;
using Zaldaryon.Pharos.Server;
using Zaldaryon.Pharos.XUnit.Execution;

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
/// Under <see cref="WorldIsolation.Rollback"/>, the default, the server, the client and its
/// connection stay up between the tests of a class. After each test the world is put back in
/// place as it was when the client joined (see <see cref="WorldSnapshot"/>), the client is sent the
/// restored chunks, its controls are released, its dialogs closed and its recorders cleared. A
/// test that disconnects the client, or a rollback that fails, leaves the next test a freshly
/// booted pair. <see cref="WorldIsolation.Restart"/> boots a new pair for every test.
/// </para>
/// <para>
/// Uses separate platform lock acquisition sequences to prevent deadlocks when both
/// client and server compete for shared resources.
/// </para>
/// </remarks>
public abstract class ClientServerScenarioBase : IAsyncLifetime, IScenarioLifecycle
{
    private readonly ScenarioRun _run = new();
    private ServerSandbox? _sandbox;
    private EmbeddedServerHost? _serverHost;
    private HeadlessClient? _client;
    private ClientServerLoopbackSession? _session;
    private IDisposable? _classSettings;
    private bool _disposed;
    private IDisposable? _gate;
    private string? _clientModsDirectory;
    private WorldSnapshot? _baseline;
    private IDisposable? _baselineTracking;
    private ClientServerPoolKey? _poolKey;
    private readonly List<ServerTestPlayer> _testPlayers = [];

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
    /// Gets the client configuration options for this scenario. An engine-mode client by default,
    /// which is the only kind that can join the server.
    /// Override to customize headless client settings.
    /// </summary>
    protected virtual HeadlessClientOptions ClientOptions => new() { BootMode = ClientBootMode.Engine };

    /// <summary>
    /// Gets the timeout for waiting for the player to join. Default is 60 seconds, scaled by
    /// <c>PHAROS_TIMEOUT_SCALE</c>: a client composing its texture atlases in software on a busy
    /// runner can take most of a minute.
    /// </summary>
    protected virtual TimeSpan PlayerJoinTimeout => TimeSpan.FromSeconds(60 * ScenarioTimeouts.Scale);

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
    /// Gets how long a test waits for another scenario to release the host before it fails.
    /// </summary>
    protected virtual TimeSpan HostWaitTimeout => TimeSpan.FromMinutes(10);

    /// <summary>
    /// Whether the test fails when the client or the server logged an error during it that
    /// <see cref="AllowedLoggedErrors"/> does not allow. Off by default. The errors are taken when
    /// the test body ends, so what the game logs while it shuts down does not count.
    /// </summary>
    protected virtual bool FailOnLoggedErrors => false;

    /// <summary>
    /// Fragments of logged errors that <see cref="FailOnLoggedErrors"/> lets pass, compared
    /// without regard to case.
    /// </summary>
    protected virtual IEnumerable<string> AllowedLoggedErrors => [];

    /// <summary>
    /// What the test saves when it fails or times out; see <see cref="Reporting.FailureArtifacts"/>
    /// and <c>docs/failure-artifacts.md</c>.
    /// </summary>
    protected virtual FailureArtifacts Artifacts => FailureArtifacts.Default;

    /// <summary>
    /// Called when the test fails, while the client and the server are still up, to add files of
    /// the test's own to <paramref name="directory"/>, the folder of its failure artifacts.
    /// </summary>
    protected virtual void OnFailure(string directory)
    {
    }

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
        BootCheck.ThrowIfFailedBefore(GetType());
        _gate = await ScenarioHostPool.EnterAsync(HostWaitTimeout).ConfigureAwait(false);

        try
        {
            bool rollback = WorldIsolation == WorldIsolation.Rollback && WaitForPlayerJoinOnInit;
            _poolKey = rollback ? PoolKey() : null;

            if (_poolKey != null && ScenarioHostPool.Take<PooledClientServer>(GetType(), _poolKey) is { } pooled)
            {
                Adopt(pooled);
                ApplyClassSettings();
                return;
            }

            ScenarioHostPool.Clear();
            await StartAsync().ConfigureAwait(false);

            // Checked before the pair is snapshotted or pooled: a pair that fails it is torn down.
            if (BootCheck.Enforce(GetType(), _serverHost!.BootDiagnostics, _client!.BootDiagnostics))
            {
                _serverHost.Logs.Clear();
                _client.Logs.Clear();
            }

            if (_poolKey != null)
            {
                _baseline = _serverHost!.TakeSnapshot();
                _baselineTracking = _serverHost.RunOnGameThread(() => _baseline.TrackChunksLoadedLater(_serverHost));
            }
        }
        catch (Exception ex)
        {
            // Saved before the teardown below takes the client and the server down.
            Exception failure = ScenarioRun.SetupFailed(ex, (error, test) => Describe(test, error, timedOut: false));

            // xUnit does not always reach DisposeAsync after a failed InitializeAsync, and the
            // gate must be released either way or every later scenario waits for it. Nothing it
            // throws may replace the setup's own failure.
            _run.SetupFailing();
            try
            {
                await DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception teardown)
            {
                ServerMain.Logger?.Warning("Pharos could not tear down a scenario that failed to start: {0}", teardown);
            }

            if (ReferenceEquals(failure, ex)) throw;
            throw failure;
        }
    }

    /// <summary>
    /// The server's and the client's boot diagnostics judged against the class's
    /// <see cref="AllowBootDiagnosticAttribute"/>s, whether or not the class is
    /// <see cref="StrictBootAttribute"/>. See <c>docs/boot-diagnostics.md</c>.
    /// </summary>
    protected BootDiagnosticsResult UnexpectedBootDiagnostics => BootCheck.Evaluate(GetType(), _serverHost?.BootDiagnostics, _client?.BootDiagnostics);

    void IScenarioLifecycle.BeforeBody()
    {
        _run.BodyStarting(_session?.FrameCount, _session?.ServerTickCount);
        if (Artifacts.HasFlag(FailureArtifacts.Packets) && _client is { PacketRecorder.IsRecording: false } client)
        {
            client.PacketRecorder.Start();
        }
    }

    void IScenarioLifecycle.CheckLoggedErrors() =>
        LoggedErrorGate.ThrowIfAny(LoggedErrorGate.Collect(FailOnLoggedErrors, AllowedLoggedErrors, _client?.Logs, _serverHost?.Logs));

    void IScenarioLifecycle.BodyTimedOut(bool stillRunning) => _run.BodyTimedOut(stillRunning);

    string? IScenarioLifecycle.CaptureFailure(ScenarioTestInfo test, Exception exception, bool timedOut) =>
        FailureArtifactWriter.Write(Describe(test, exception, timedOut)).Summary();

    private ScenarioFailure Describe(ScenarioTestInfo test, Exception exception, bool timedOut) => new()
    {
        DisplayName = test.DisplayName,
        TestClass = test.TestClass,
        MethodName = test.MethodName,
        TimedOut = timedOut,
        Exception = exception,
        Client = _client,
        ClientReachable = !_run.Abandoned,
        Server = _serverHost,
        ServerDataPath = _sandbox?.RootPath,
        Sandbox = _sandbox,
        World = WorldOptions,
        Isolation = WorldIsolation.ToString(),
        Frames = ScenarioRun.Since(_run.FramesAtStart, _session?.FrameCount),
        ServerTicks = ScenarioRun.Since(_run.TicksAtStart, _session?.ServerTickCount),
        Artifacts = Artifacts,
        AddFiles = OnFailure,
    };

    private async Task StartAsync()
    {

        // Create sandbox for isolated server storage
        _sandbox = new ServerSandbox();

        // The server's mods go to both sides: a real client needs every universal mod the server
        // runs, and an in-memory client has no mod download step to fetch them.
        IReadOnlyList<string> serverMods = ScenarioAttributes.ServerMods(GetType());
        ScenarioAttributes.StageMods(serverMods, _sandbox.ModsPath);
        _clientModsDirectory = Path.Combine(_sandbox.RootPath, "ClientMods");
        ScenarioAttributes.StageMods([.. serverMods, .. ScenarioAttributes.ClientMods(GetType())], _clientModsDirectory);

        // Boot embedded server in sandbox
        _serverHost = EmbeddedServerHost.Boot(_sandbox, WorldOptions);

        // Initialize headless client (separate platform init)
        HeadlessClientOptions clientOptions = ClientOptions;
        _client = HeadlessClientBootstrap.Boot(new HeadlessClientOptions
        {
            Width = clientOptions.Width,
            Height = clientOptions.Height,
            GameInstallPath = clientOptions.GameInstallPath,
            DataPath = clientOptions.DataPath,
            AssetsPath = clientOptions.AssetsPath,
            DisableAudio = clientOptions.DisableAudio,
            UseNullAudioDevice = clientOptions.UseNullAudioDevice,
            ConfigureMesaEnvironment = clientOptions.ConfigureMesaEnvironment,
            ForceSoftwareRendering = clientOptions.ForceSoftwareRendering,
            MesaGlVersionOverride = clientOptions.MesaGlVersionOverride,
            MesaGlslVersionOverride = clientOptions.MesaGlslVersionOverride,
            LinuxDisplay = clientOptions.LinuxDisplay,
            BootMode = clientOptions.BootMode,
            CompleteCharacterSelection = clientOptions.CompleteCharacterSelection,
            CharacterClass = clientOptions.CharacterClass,
            LoadBridge = clientOptions.LoadBridge,
            ModPaths = [.. clientOptions.ModPaths, _clientModsDirectory],
        });

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

        ApplyClassSettings();
    }

    private void ApplyClassSettings()
    {
        if (ScenarioAttributes.ClientSettings(GetType()) is { } settings)
        {
            _classSettings = _client!.Settings.Apply(settings);
        }
    }

    private ClientServerPoolKey PoolKey()
    {
        HeadlessClientOptions client = ClientOptions;
        return new ClientServerPoolKey(
            WorldOptions,
            string.Join("|", ScenarioAttributes.ServerMods(GetType())),
            string.Join("|", [.. ScenarioAttributes.ClientMods(GetType()), .. client.ModPaths]),
            PlayerName,
            client.BootMode,
            client.Width,
            client.Height);
    }

    /// <summary>
    /// Tears down the client-server scenario: disconnects client, stops server, cleans sandbox.
    /// </summary>
    public virtual async Task DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        IReadOnlyList<LogEntry> loggedErrors = _run.PipelineChecksLoggedErrors
            ? []
            : LoggedErrorGate.Collect(FailOnLoggedErrors, AllowedLoggedErrors, _client?.Logs, _serverHost?.Logs);

        if (_run.Abandoned)
        {
            // The timed-out body still drives the client and the server: they are left as they are.
            _classSettings = null;
            _testPlayers.Clear();
            _session = null;
            _client = null;
            _serverHost = null;
            _sandbox = null;
            _gate?.Dispose();
            _gate = null;
            return;
        }

        // Settings are process-wide: the next scenario's client must not inherit them.
        try
        {
            _classSettings?.Dispose();
        }
        catch
        {
            // Ignore settings restore errors during teardown
        }
        _classSettings = null;

        foreach (ServerTestPlayer player in _testPlayers)
        {
            try
            {
                player.Dispose();
            }
            catch
            {
                // Ignore player teardown errors
            }
        }

        _testPlayers.Clear();

        bool reusable = !_run.TimedOut && _sandbox is { IsRetained: false } && _client is { IsDataPathRetained: false };
        if (_poolKey != null && reusable && await TryRollbackAsync().ConfigureAwait(false))
        {
            ScenarioHostPool.Return(GetType(), _poolKey, Detach());
            _gate?.Dispose();
            _gate = null;
            LoggedErrorGate.ThrowIfAny(loggedErrors);
            return;
        }

        _baselineTracking?.Dispose();
        _baselineTracking = null;

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

        ScenarioHostPool.HostDisposed();
        _gate?.Dispose();
        _gate = null;

        LoggedErrorGate.ThrowIfAny(loggedErrors);
    }

    /// <summary>
    /// Puts the world back as it was when the client joined and readies the client for the next
    /// test. False when the pair cannot be reused.
    /// </summary>
    private async Task<bool> TryRollbackAsync()
    {
        EmbeddedServerHost? server = _serverHost;
        HeadlessClient? client = _client;
        ClientServerLoopbackSession? session = _session;
        if (_baseline == null || server == null || client == null || session == null) return false;
        if (!server.IsRunning || client.IsDisposed || !session.IsConnected || session.IsLinkSevered || client.DisconnectSimulator.IsDisconnected) return false;

        try
        {
            if (!client.IsJoined || server.RunOnGameThread(() => server.Server.GetClientByPlayername(PlayerName) == null)) return false;

            client.Controls.ReleaseAll();
            client.RunOnClientThread(() =>
            {
                foreach (Vintagestory.API.Client.GuiDialog dialog in client.Client.api.Gui.OpenedGuis.ToList())
                {
                    if (dialog.DialogType == Vintagestory.API.Client.EnumDialogType.Dialog) dialog.TryClose();
                }
            });
            client.NetworkDegradation.Reset();
            client.PacketRecorder.Stop();

            server.RestoreSnapshot(_baseline);

            // The client has the world again once the server has sent every restored chunk.
            bool sent = await session.StepUntilAsync(
                () => server.RunOnGameThread(() => server.Server.Clients.Values.All(c => c.forceSendChunks.Count == 0 && c.forceSendMapChunks.Count == 0)),
                maxFrames: 600).ConfigureAwait(false);
            await session.StepFramesAsync(10).ConfigureAwait(false);

            // The server put the player back where it stood. Until the client has moved there too,
            // the two disagree on where the player is, and the server refuses interactions it
            // judges out of range.
            bool home = await PlayerIsHomeAsync(server, client, session).ConfigureAwait(false);

            client.PacketRecorder.Clear();
            client.Sounds.Clear();
            client.Logs.Clear();
            server.Logs.Clear();

            return sent && home && session.IsConnected && client.IsJoined;
        }
        catch (Exception ex)
        {
            ServerMain.Logger?.Warning("Pharos could not roll the world back, the next test boots a fresh server: {0}", ex);
            return false;
        }
    }

    /// <summary>
    /// Steps until the client and the server both have the joined player within half a block of
    /// where the baseline put it.
    /// </summary>
    private async Task<bool> PlayerIsHomeAsync(EmbeddedServerHost server, HeadlessClient client, ClientServerLoopbackSession session)
    {
        string? uid = server.RunOnGameThread(() => server.Server.GetClientByPlayername(PlayerName)?.Player?.PlayerUID);
        if (uid == null || _baseline!.PositionOf(uid) is not { } home) return true;

        return await session.StepUntilAsync(
            () => client.RunOnClientThread(() => client.Client.EntityPlayer.Pos.XYZ.SquareDistanceTo(home) < 0.25)
                && server.RunOnGameThread(() => server.Server.GetClientByPlayername(PlayerName)?.Entityplayer?.Pos.XYZ.SquareDistanceTo(home) < 0.25),
            maxFrames: 600).ConfigureAwait(false);
    }

    private void Adopt(PooledClientServer pooled)
    {
        _sandbox = pooled.Sandbox;
        _serverHost = pooled.ServerHost;
        _client = pooled.Client;
        _session = pooled.Session;
        _clientModsDirectory = pooled.ClientModsDirectory;
        _baseline = pooled.Baseline;
        _baselineTracking = pooled.BaselineTracking;
    }

    private PooledClientServer Detach()
    {
        PooledClientServer pooled = new(_sandbox!, _serverHost!, _client!, _session!, _clientModsDirectory, _baseline!, _baselineTracking);
        _sandbox = null;
        _serverHost = null;
        _client = null;
        _session = null;
        _baseline = null;
        _baselineTracking = null;
        return pooled;
    }

    private sealed record ClientServerPoolKey(
        ServerWorldOptions World, string ServerMods, string ClientMods, string PlayerName,
        ClientBootMode BootMode, int Width, int Height);

    /// <summary>A joined client and its server, kept for the next test of the same class.</summary>
    private sealed record PooledClientServer(
        ServerSandbox Sandbox,
        EmbeddedServerHost ServerHost,
        HeadlessClient Client,
        ClientServerLoopbackSession Session,
        string? ClientModsDirectory,
        WorldSnapshot Baseline,
        IDisposable? BaselineTracking) : IDisposable
    {
        public void Dispose()
        {
            BaselineTracking?.Dispose();
            try
            {
                Session.Dispose();
            }
            catch
            {
                // Ignore session dispose errors during teardown
            }

            try
            {
                Client.Dispose();
            }
            catch
            {
                // Ignore client dispose errors during teardown
            }

            try
            {
                ServerHost.Dispose();
            }
            catch
            {
                // Ignore server dispose errors during teardown
            }

            try
            {
                Sandbox.Dispose();
            }
            catch
            {
                // Ignore sandbox cleanup errors during teardown
            }
        }
    }

    /// <summary>
    /// Joins a headless player into the same server as the rendering client, for multiplayer
    /// scenarios. The player is a real multiplayer connection as far as the server can tell; the
    /// client sees it as another player. It leaves the server when the test ends.
    /// </summary>
    /// <param name="playerName">The player name. Must differ from <see cref="PlayerName"/>.</param>
    /// <exception cref="InvalidOperationException">The scenario is not initialized.</exception>
    protected async Task<ServerTestPlayer> CreateTestPlayerAsync(string playerName)
    {
        EmbeddedServerHost host = _serverHost ?? throw new InvalidOperationException("Session is not initialized. Call InitializeAsync first.");
        ServerTestPlayer player = await host.JoinPlayerAsync(playerName).ConfigureAwait(false);
        _testPlayers.Add(player);
        return player;
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

            await _session.StepAsync(dt, 1, ct).ConfigureAwait(false);
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
