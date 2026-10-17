using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.Server;
using Xunit;
using Zaldaryon.Pharos.Reporting;
using Zaldaryon.Pharos.Server;
using Zaldaryon.Pharos.XUnit.Execution;

namespace Zaldaryon.Pharos.XUnit;

/// <summary>
/// Abstract base class for server scenario tests: boots an embedded server before each test and
/// tears it down, or pools it, afterwards.
/// </summary>
/// <remarks>
/// <para>
/// The world comes from <see cref="WorldOptions"/>, which defaults to the class-level
/// <see cref="ServerWorldAttribute"/>. Mods listed by <see cref="ServerModsAttribute"/> on the
/// class or the assembly are staged into the server's sandbox before it boots.
/// </para>
/// <para>
/// <see cref="WorldIsolation.Recycle"/> keeps the server running for the next test of the same
/// class as it is. <see cref="WorldIsolation.Rollback"/> keeps it running too, but puts the world
/// back in place as it was after boot, without a restart; see <see cref="WorldSnapshot"/>.
/// <see cref="WorldIsolation.Restart"/> boots a fresh server for every test. A rollback that fails
/// falls back to a fresh server for the next test.
/// </para>
/// <para>
/// Scenarios run one at a time, whatever the test collection layout: the game keeps process-wide
/// static state, so two servers or clients cannot boot side by side in one process.
/// </para>
/// <para>
/// Deferred commands (those that return <see cref="EnumCommandStatus.Deferred"/>) are awaited
/// through their completion callback.
/// </para>
/// </remarks>
public abstract class ServerScenarioBase : IAsyncLifetime, IScenarioLifecycle
{
    private readonly ScenarioRun _run = new();
    private EmbeddedServerHost? _host;
    private IDisposable? _gate;
    private PooledServer? _pooled;
    private ScenarioContext? _environmentContext;
    private readonly List<ServerTestPlayer> _testPlayers = [];

    /// <summary>
    /// <summary>
    /// Gets the embedded server host managing the test server instance.
    /// </summary>
    /// <exception cref="InvalidOperationException">The server is accessed before scenario initialization.</exception>
    protected EmbeddedServerHost Server => _host ?? _capturing
        ?? throw new InvalidOperationException("Server is not initialized. It is available after scenario boot.");

    /// <summary>
    /// Gets the embedded server host managing the test server instance.
    /// </summary>
    [Obsolete("Use Server instead. This property will be removed in 0.8.")]
    protected EmbeddedServerHost Host => Server;

    // The server while OnRollbackCaptured runs, before the test has it.
    private EmbeddedServerHost? _capturing;

    /// <summary>
    /// Gets the server-side API for game interactions.
    /// </summary>
    /// <exception cref="InvalidOperationException">The server API is accessed before scenario initialization.</exception>
    protected ICoreServerAPI Api => (ICoreServerAPI)Server.Server.Api;

    /// <summary>Sets a block at the specified position by block code.</summary>
    protected void SetBlock(BlockPos pos, string blockCode) => Server.SetBlock(pos, blockCode);

    /// <summary>Sets a block at the specified position by numeric ID.</summary>
    protected void SetBlock(BlockPos pos, int blockId) => Server.SetBlock(pos, blockId);

    /// <summary>Gets the block at the specified position.</summary>
    protected Block GetBlock(BlockPos pos) => Server.GetBlock(pos);

    /// <summary>Gets the block entity at the specified position, or null if none exists.</summary>
    protected BlockEntity? GetBlockEntity(BlockPos pos) => Server.GetBlockEntity(pos);

    /// <summary>Gets the block entity of type <typeparamref name="T"/> at the specified position.</summary>
    protected T? GetBlockEntity<T>(BlockPos pos) where T : BlockEntity => Server.GetBlockEntity<T>(pos);

    /// <summary>Spawns an entity at the specified position.</summary>
    protected Entity SpawnEntity(string entityCode, Vec3d pos) => Server.SpawnEntity(entityCode, pos);

    /// <summary>Polls an assertion once per server tick until it passes or the tick budget expires.</summary>
    protected Task SucceedWhenAsync(Action assertion, int maxTicks = 300, CancellationToken ct = default)
    {
        EmbeddedServerHost host = Server;
        return ScenarioTickWait.SucceedWhenAsync(assertion, maxTicks, (count, token) =>
        {
            token.ThrowIfCancellationRequested();
            host.Ticks(count);
            return Task.CompletedTask;
        }, ct);
    }

    /// <summary>Creates a sequence that advances this server by ticks.</summary>
    protected ScenarioSequence Sequence()
    {
        EmbeddedServerHost host = Server;
        return new ScenarioSequence((count, token) =>
        {
            token.ThrowIfCancellationRequested();
            host.Ticks(count);
            return Task.CompletedTask;
        });
    }

    /// <summary>
    /// Gets the world isolation mode used for tests in this class. Defaults to the
    /// <see cref="ServerWorldAttribute.Isolation"/> of the class, or
    /// <see cref="Isolation.Rollback"/>.
    /// </summary>
    protected virtual Isolation Isolation =>
        ScenarioAttributes.ServerWorld(GetType())?.Isolation ?? Isolation.Rollback;

    /// <summary>Legacy world isolation mode.</summary>
    [Obsolete("Use Isolation instead. This property will be removed in 0.8.")]
    protected virtual WorldIsolation WorldIsolation => (WorldIsolation)(int)Isolation;

    private Isolation EffectiveIsolation =>
        (WorldIsolation)(int)Isolation != WorldIsolation.Rollback && WorldIsolation != WorldIsolation.Rollback
            ? (Isolation)(int)WorldIsolation
            : Isolation;

    /// <summary>
    /// Gets the world to boot. Defaults to the class-level <see cref="ServerWorldAttribute"/>.
    /// </summary>
    protected virtual ServerWorldOptions WorldOptions => ScenarioAttributes.WorldOptions(GetType());

    /// <summary>
    /// Whether a test whose world cannot be rolled back fails, under <see cref="Isolation.Rollback"/>.
    /// Defaults to <see cref="ServerWorldAttribute.StrictIsolation"/>.
    /// </summary>
    protected virtual bool StrictIsolation => ScenarioAttributes.ServerWorld(GetType())?.StrictIsolation ?? false;

    /// <summary>
    /// How this test's server was made ready: booted, rolled back from the previous test, or
    /// recycled, with what it cost and why it was booted again when a rollback was expected.
    /// Null until <see cref="InitializeAsync"/> completes.
    /// </summary>
    protected IsolationReport? IsolationReport { get; private set; }

    /// <summary>
    /// Called once the server's world has been captured for rollbacks, after
    /// <see cref="RollbackEvents.Captured"/> has fired on the server.
    /// </summary>
    protected virtual void OnRollbackCaptured()
    {
    }

    /// <summary>
    /// Called once the world has been rolled back after this test, after
    /// <see cref="RollbackEvents.Restored"/> has fired on the server, so the class can reload
    /// state of its own before the next test. What it throws fails the rollback.
    /// </summary>
    protected virtual void OnRollbackRestored()
    {
    }

    /// <summary>
    /// Whether a tick listener, delayed callback or event-bus listener added during a test is the
    /// test's own, and so removed by a rollback. By default, a handler compiled into this class's
    /// assembly or a base class's, other than Pharos's.
    /// </summary>
    protected virtual bool IsTestListener(Delegate handler) => ListenerWatermark.DeclaredIn(handler, IsolationLog.TestAssemblies(GetType()));

    /// <summary>
    /// Whether a rollback puts back the mod data stored under <paramref name="key"/>: in the save
    /// game (<c>SaveGame.StoreData</c>), in a map region, or in a connected player's data
    /// (<c>IServerPlayer.SetModdata</c>). A refused key keeps its value from test to test. By
    /// default, every key. Runs on the server's game thread; what it throws fails the rollback.
    /// </summary>
    protected virtual bool RestoreModData(string key) => true;

    /// <summary>
    /// Gets how long a test waits for another scenario to release the host before it fails.
    /// </summary>
    protected virtual TimeSpan HostWaitTimeout => TimeSpan.FromMinutes(10);

    /// <summary>
    /// Whether the test fails when the server logged an error during it that
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
    /// and <c>docs/failure-artifacts.md</c>. A server scenario has no client, so no screenshot.
    /// </summary>
    protected virtual FailureArtifacts Artifacts => FailureArtifacts.Default;

    /// <summary>
    /// Called when the test fails, while the server is still up, to add files of the test's own
    /// to <paramref name="directory"/>, the folder of its failure artifacts.
    /// </summary>
    protected virtual void OnFailure(string directory)
    {
    }

    /// <summary>
    /// Boots the server, or takes the pooled one when <see cref="WorldIsolation"/> allows it.
    /// </summary>
    public virtual async Task InitializeAsync()
    {
        BootCheck.ThrowIfFailedBefore(GetType());
        _gate = await ScenarioHostPool.EnterAsync(HostWaitTimeout).ConfigureAwait(false);

        try
        {
            ServerWorldOptions options = WorldPresetCache.Resolve(WorldOptions);
            IReadOnlyList<string> mods = ScenarioAttributes.ServerMods(GetType());
            DataFileSet files = ScenarioAttributes.DataFiles(GetType(), EnumAppSide.Server);
            ServerPoolKey key = new(options, string.Join("|", mods), EffectiveIsolation, files.Key);

            _pooled = EffectiveIsolation is Isolation.Recycle or Isolation.Rollback
                ? ScenarioHostPool.Take<PooledServer>(GetType(), key)
                : null;

            bool reused = _pooled != null;
            long bootStart = System.Diagnostics.Stopwatch.GetTimestamp();
            _host = null;
            if (_pooled == null)
            {
                EmbeddedServerHost.CheckSaveFile(options);
                ScenarioHostPool.Clear();

                ServerSandbox sandbox = new();
                try
                {
                    ScenarioAttributes.StageMods(mods, sandbox.ModsPath);
                    files.WriteTo(sandbox.RootPath, EnumAppSide.Server);
                    _pooled = new PooledServer(sandbox, EmbeddedServerHost.Boot(sandbox, options), key, files);
                }
                catch (Exception ex)
                {
                    Exception failure = ScenarioRun.SetupFailed(ex, (error, test) => Describe(test, error, timedOut: false, server: null, sandbox));
                    sandbox.Dispose();
                    if (ReferenceEquals(failure, ex)) throw;
                    throw failure;
                }

                // Checked before the host is used or pooled: a host that fails it is disposed.
                if (BootCheck.Enforce(GetType(), _pooled.Host.BootDiagnostics))
                {
                    _pooled.Host.Logs.Clear();
                }

                if (EffectiveIsolation == Isolation.Rollback)
                {
                    _pooled.TakeBaseline();
                    RollbackParticipants.Push(_pooled.Host, RollbackEvents.Captured, ScenarioTestInfo.Current?.DisplayName, chunks: null);
                    _capturing = _pooled.Host;
                    OnRollbackCaptured();
                    _capturing = null;

                    // After the hook: what the class sets up there lives as long as the server.
                    _pooled.CaptureListeners();
                }
            }

            IsolationReport = IsolationLog.Prepared(GetType(), reused, EffectiveIsolation == Isolation.Recycle, System.Diagnostics.Stopwatch.GetElapsedTime(bootStart));

            // A client booted since, by another scenario or the test before, moved the game's data
            // folder to its own.
            if (reused) Vintagestory.API.Config.GamePaths.DataPath = _pooled.Sandbox.RootPath;
            _host = _pooled.Host;
        }
        catch (Exception ex)
        {
            Exception failure = ScenarioRun.SetupFailed(ex, (error, test) => Describe(test, error, timedOut: false, _pooled?.Host, _pooled?.Sandbox));
            // xUnit does not call DisposeAsync after a failed InitializeAsync: a server that did not
            // reach the test is disposed here, never pooled.
            _capturing = null;
            if (_pooled != null && _host == null)
            {
                _pooled.Dispose();
                _pooled = null;
                ScenarioHostPool.HostDisposed();
                IsolationLog.Left(GetType(), null, "the previous test failed to start");
            }

            _gate.Dispose();
            _gate = null;
            if (ReferenceEquals(failure, ex)) throw;
            throw failure;
        }
    }

    /// <summary>
    /// The server's boot diagnostics judged against the class's <see cref="AllowBootDiagnosticAttribute"/>s,
    /// whether or not the class is <see cref="StrictBootAttribute"/>. See <c>docs/boot-diagnostics.md</c>.
    /// </summary>
    protected BootDiagnosticsResult UnexpectedBootDiagnostics => BootCheck.Evaluate(GetType(), _host?.BootDiagnostics);

    void IScenarioLifecycle.BeforeBody()
    {
        _run.BodyStarting(frames: null, ticks: _host?.TickCount);
        ScenarioRun.SeedRandom(this, _host?.Random, client: null);
    }

    Task IScenarioLifecycle.ApplyEnvironmentsAsync(CancellationToken cancellationToken)
    {
        ScenarioTestInfo test = ScenarioTestInfo.Current ?? throw new InvalidOperationException("Scenario test information is not available.");
        _environmentContext = new ScenarioContext(_host, client: null, session: null);
        return ScenarioEnvironments.ApplyAsync(test.TestClass, test.Method, _environmentContext, cancellationToken);
    }

    Task IScenarioLifecycle.CleanupEnvironmentsAsync(CancellationToken cancellationToken) => CleanupEnvironmentsAsync(cancellationToken);

    private async Task CleanupEnvironmentsAsync(CancellationToken cancellationToken)
    {
        ScenarioContext? context = _environmentContext;
        if (context != null)
        {
            await context.DisposeAsync(cancellationToken);
            if (ReferenceEquals(_environmentContext, context)) _environmentContext = null;
        }
    }

    void IScenarioLifecycle.CheckLoggedErrors() =>
        LoggedErrorGate.ThrowIfAny(LoggedErrorGate.Collect(FailOnLoggedErrors, AllowedLoggedErrors, _host?.Logs));

    void IScenarioLifecycle.BodyTimedOut(bool stillRunning) => _run.BodyTimedOut(stillRunning);

    EmbeddedServerHost? IScenarioLifecycle.FixtureServer => _host;

    /// <summary>
    /// Stamps a WorldEdit schematic into the world; see <see cref="EmbeddedServerHost.PlaceSchematicAsync"/>.
    /// </summary>
    /// <exception cref="InvalidOperationException">The server is not running.</exception>
    protected Task<World.SchematicPlacement> PlaceSchematicAsync(string path, Vintagestory.API.MathTools.BlockPos origin, World.SchematicOptions? options = null, CancellationToken ct = default) =>
        (Host ?? throw new InvalidOperationException("The server is not running.")).PlaceSchematicAsync(path, origin, options, ct);

    string? IScenarioLifecycle.CaptureFailure(ScenarioTestInfo test, Exception exception, bool timedOut) =>
        FailureArtifactWriter.Write(Describe(test, exception, timedOut, _host, _pooled?.Sandbox)).Summary();

    private ScenarioFailure Describe(ScenarioTestInfo test, Exception exception, bool timedOut, EmbeddedServerHost? server, ServerSandbox? sandbox) => new()
    {
        DisplayName = test.DisplayName,
        TestClass = test.TestClass,
        MethodName = test.MethodName,
        TimedOut = timedOut,
        Exception = exception,
        Server = server,
        ServerDataPath = server?.DataPath ?? sandbox?.RootPath,
        Sandbox = sandbox,
        World = WorldOptions,
        Isolation = EffectiveIsolation.ToString(),
        IsolationReport = IsolationReport,
        RandomSeed = server?.Random.CurrentSeed,
        RandomSeedSource = server?.Random.CurrentSource,
        ServerTicks = ScenarioRun.Since(_run.TicksAtStart, server?.TickCount),
        Artifacts = Artifacts,
        AddFiles = OnFailure,
    };

    /// <summary>
    /// Stops the server, or pools it for the next test of this class under
    /// <see cref="WorldIsolation.Recycle"/>.
    /// </summary>
    public virtual async Task DisposeAsync()
    {
        IReadOnlyList<LogEntry> loggedErrors = _run.PipelineChecksLoggedErrors
            ? []
            : LoggedErrorGate.Collect(FailOnLoggedErrors, AllowedLoggedErrors, _host?.Logs);

        if (_run.Abandoned)
        {
            AbandonRunningScenario();
            return;
        }

        Exception? environmentFailure = null;
        if (_environmentContext is { } environmentContext)
        {
            try
            {
                await environmentContext.DisposeAsync(ScenarioTimeouts.Grace).ConfigureAwait(false);
                if (ReferenceEquals(_environmentContext, environmentContext)) _environmentContext = null;
            }
            catch (Exception ex) { environmentFailure = ex; }
        }
        if (environmentFailure is TimeoutException)
        {
            _run.BodyTimedOut(stillRunning: true);
            AbandonRunningScenario();
            throw new TimeoutException("Scenario environment cleanup did not stop; the server was abandoned to avoid reusing it in a dirty state.", environmentFailure);
        }

        IsolationException? isolationFailure = null;
        Events.LeftOpenLogs.Close(_host, null);
        try
        {
            // A virtual clock the test left in use ends before the world is rolled back.
            try
            {
                _host?.ResetVirtualClock();
            }
            catch
            {
                // Ignore clock teardown errors
            }

            foreach (ServerTestPlayer player in _testPlayers)
            {
                player.Dispose();
            }

            _testPlayers.Clear();

            // The recipe player leaves as the test players do, so rollback does not keep it.
            _host?.ReleaseRecipePlayer();

            // The generators go back before anything else: the server is pooled or disposed next,
            // and the static ones belong to the whole process.
            string? random = RollbackParticipants.Try("Restoring the random generators", () => World.RandomSeeding.EndScenario(_host is { IsRunning: true } host ? host.Random : null, client: null));
            if (random != null && EffectiveIsolation == Isolation.Restart) ServerMain.Logger?.Warning("Pharos: {0}", random);

            if (_pooled != null)
            {
                string? fallback = WhyNotReusable(_pooled) ?? random ?? (ScenarioHostPool.CanReuseAfterEnvironmentCleanup(environmentFailure) ? null : "a test environment cleanup failed");
                IsolationReport? rolledBack = null;
                if (fallback == null && EffectiveIsolation == Isolation.Rollback)
                {
                    (rolledBack, fallback) = _pooled.TryRollback(IsTestListener, new WorldRestoreFilter(ModData: RestoreModData), ScenarioTestInfo.Current?.DisplayName);
                    fallback ??= RollbackParticipants.Try(nameof(OnRollbackRestored), OnRollbackRestored);
                    if (fallback != null) rolledBack = null;
                }

                bool expected = EffectiveIsolation is Isolation.Recycle or Isolation.Rollback;
                if (fallback == null && expected)
                {
                    // The next test judges only what it logs itself. A rollback clears the logs
                    // before it tells the mods, so what their handlers log counts against it.
                    if (EffectiveIsolation == Isolation.Recycle) _pooled.Host.Logs.Clear();
                    ScenarioHostPool.Return(GetType(), _pooled.Key, _pooled);
                }
                else
                {
                    if (fallback != null && expected) ServerMain.Logger?.Warning("Pharos could not keep the server for the next test, which boots a fresh one: {0}", fallback);
                    _pooled.Dispose();
                    ScenarioHostPool.HostDisposed();
                }

                IsolationLog.Left(GetType(), rolledBack, expected ? fallback : null, kept: fallback == null && expected);
                if (fallback != null && EffectiveIsolation == Isolation.Rollback && StrictIsolation && !_run.TimedOut && !_pooled.Sandbox.IsRetained)
                {
                    isolationFailure = new IsolationException($"The world could not be rolled back after this test: {fallback}.");
                }
            }
        }
        finally
        {
            _pooled = null;
            _host = null;
            _gate?.Dispose();
            _gate = null;
        }

        Exception? teardownFailure = isolationFailure != null && environmentFailure != null
            ? new AggregateException(isolationFailure, environmentFailure)
            : isolationFailure ?? environmentFailure;
        ScenarioFailures.ThrowAll(teardownFailure, () => LoggedErrorGate.ThrowIfAny(loggedErrors));
    }

    private void AbandonRunningScenario()
    {
        World.RandomSeeding.Abandon();
        IsolationLog.Left(GetType(), null, "the previous test timed out and was left running");
        // A timed-out body or environment cleanup may still be using this host.
        _testPlayers.Clear();
        _environmentContext = null;
        _pooled = null;
        _host = null;
        _gate?.Dispose();
        _gate = null;
    }

    // Why the server cannot be kept for the next test, before any rollback is tried.
    private string? WhyNotReusable(PooledServer pooled) =>
        !pooled.Host.IsRunning ? "the server stopped"
        : pooled.Reloaded ? "the world was saved and booted again"
        : _run.TimedOut ? "the test timed out"
        : pooled.Sandbox.IsRetained ? "the sandbox was kept for inspection"
        : null;

    /// <summary>
    /// Saves the world, stops the server and boots it again on its save, so a test can compare what the save gives back. The
    /// players the test joined are gone afterwards: join them again. Errors that the server logged while it saved or stopped are
    /// reported once the new server is up.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <exception cref="InvalidOperationException">The server is not running, or it was not booted from this test's pool.</exception>
    protected Task ReloadWorldAsync(CancellationToken ct = default) => RestartWorldAsync(RestartMode.Graceful, ct);

    /// <summary>
    /// Stops the server in <paramref name="mode"/> and boots it again on its save, as <see cref="ReloadWorldAsync"/> does. A
    /// <see cref="RestartMode.Crash"/> keeps only what the game had saved before the crash.
    /// </summary>
    /// <param name="mode">How the server stops.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <exception cref="InvalidOperationException">The server is not running, or it was not booted from this test's pool.</exception>
    protected async Task RestartWorldAsync(RestartMode mode, CancellationToken ct = default)
    {
        EmbeddedServerHost old = _host ?? throw new InvalidOperationException("The server is not running, so its world cannot be restarted.");
        PooledServer pooled = _pooled ?? throw new InvalidOperationException("The server was not booted for this test, so it cannot be restarted.");

        // The game logs a save's serialization errors while the old server stops, so they are checked once it has. The new
        // server replaces the old one first, so the scenario disposes it whatever the check finds.
        EmbeddedServerHost next = await old.RestartAsync(mode, ct).ConfigureAwait(false);
        _testPlayers.Clear();
        pooled.ReplaceHost(next);
        _host = next;
        LoggedErrorGate.ThrowIfAny(LoggedErrorGate.Collect(FailOnLoggedErrors, AllowedLoggedErrors, old.Logs));
    }

    /// <summary>
    /// Joins a headless player into the server and returns it once it is playing. See
    /// <see cref="EmbeddedServerHost.JoinPlayerAsync"/>. The player leaves the server when the
    /// test ends.
    /// </summary>
    /// <param name="playerName">The player name. Must not belong to a connected player.</param>
    /// <exception cref="InvalidOperationException">The server is not running.</exception>
    public async Task<ServerTestPlayer> CreateTestPlayerAsync(string playerName)
    {
        EmbeddedServerHost host = _host ?? throw new InvalidOperationException("Server is not running. Ensure the host is initialized before joining players.");
        ServerTestPlayer player = await host.JoinPlayerAsync(playerName).ConfigureAwait(false);
        _testPlayers.Add(player);
        return player;
    }

    /// <summary>
    /// Sets the embedded server host directly, bypassing <see cref="InitializeAsync"/>.
    /// </summary>
    /// <param name="host">The embedded server host to use for this scenario.</param>
    internal void SetHost(EmbeddedServerHost host)
    {
        _host = host;
    }

    /// <summary>
    /// The port a <c>{{pharos:port:NAME}}</c> placeholder got in this test's data files. See
    /// <see cref="DataFilesAttribute"/>.
    /// </summary>
    /// <exception cref="InvalidOperationException">The server has not booted yet.</exception>
    /// <exception cref="ArgumentException">No data file of the test has that placeholder.</exception>
    protected int DataFilePort(string name) =>
        (_pooled ?? throw new InvalidOperationException("Data file ports are known once the server has booted.")).Files.Port(name);

    private sealed record ServerPoolKey(ServerWorldOptions Options, string Mods, Isolation Isolation, string DataFiles);

    private sealed class PooledServer(ServerSandbox sandbox, EmbeddedServerHost host, ServerPoolKey key, DataFileSet files) : IDisposable
    {
        private WorldSnapshot? _baseline;
        private IDisposable? _tracking;
        private ListenerWatermark? _listeners;
        private DataFileBaseline? _dataFiles;
        private LandClaimDriver.LandClaimBaseline? _landClaims;
        private PvpDriver.Baseline? _pvp;
        private ReinforcementDriver.Baseline? _reinforcement;
        private GroupDriver.Baseline? _groups;
        private ClimateDriver.Baseline? _climate;

        public DataFileSet Files { get; } = files;

        public ServerSandbox Sandbox { get; } = sandbox;
        public EmbeddedServerHost Host { get; private set; } = host;
        public ServerPoolKey Key { get; } = key;

        /// <summary>Whether the world was saved and booted again during the test, so it no longer matches the captured baseline.</summary>
        public bool Reloaded { get; private set; }

        /// <summary>
        /// Puts a server that was booted again on the same save in place of the one the test started with. The baseline of the old
        /// server does not describe the new one, so the server is not pooled for another test.
        /// </summary>
        public void ReplaceHost(EmbeddedServerHost next)
        {
            try
            {
                _tracking?.Dispose();
            }
            catch
            {
                // The old server is stopped already; its tracking has nothing left to undo.
            }

            _tracking = null;
            _baseline = null;
            Host = next;
            Reloaded = true;
        }

        /// <summary>Captures the world as booted, for <see cref="TryRollback"/>.</summary>
        public void TakeBaseline()
        {
            _baseline = Host.TakeSnapshot();
            _tracking = Host.RunOnGameThread(() => _baseline.TrackChunksLoadedLater(Host));
            _dataFiles = Host.RunOnGameThread(() => DataFileBaseline.Capture(Sandbox.RootPath, Files, EnumAppSide.Server));
            _landClaims = Host.LandClaims.Capture();
            _pvp = Host.Pvp.Capture();
            _reinforcement = Host.Reinforcement.Capture();
            _groups = Host.Groups.Capture();
            _climate = Host.Climate.Capture();
        }

        /// <summary>Notes the listeners that live as long as the server; later ones are the tests'.</summary>
        public void CaptureListeners() => _listeners = RollbackParticipants.Capture(Host);

        /// <summary>
        /// Removes the listeners the test left, puts the world back as booted and tells the
        /// server's mods. Returns what it did, or why it failed.
        /// </summary>
        public (IsolationReport? Report, string? Failure) TryRollback(System.Func<Delegate, bool> ownedByTest, WorldRestoreFilter filter, string? test)
        {
            if (_baseline == null) return (null, "the world was never captured");

            int listeners = 0, chunks = 0, files = 0;
            WorldRestoreResult? restored = null;
            string? failure = null;
            TimeSpan took = IsolationLog.Time(() =>
            {
                failure = RollbackParticipants.Try("Removing the test's listeners", () => listeners = RollbackParticipants.Remove(Host, _listeners, ownedByTest))
                    ?? RollbackParticipants.Try("The rollback", () => chunks = (restored = Host.RestoreSnapshot(_baseline, filter)).ChunksRestored)
                    ?? (Host.IsRunning ? null : "the server stopped during the rollback")
                    ?? RollbackParticipants.Try("Restoring the data files", () => files = Host.RunOnGameThread(() => _dataFiles?.Restore() ?? 0))
                    ?? RollbackParticipants.Try("Restoring the land claims", () => _landClaims?.Restore())
                    ?? RollbackParticipants.Try("Restoring the PvP setting", () => _pvp?.Restore())
                    ?? RollbackParticipants.Try("Clearing the test's reinforcements", () => _reinforcement?.Restore())
                    ?? RollbackParticipants.Try("Removing the test's groups", () => _groups?.Restore())
                    ?? RollbackParticipants.Try("Ending the test's climate overrides", () => _climate?.Restore())
                    ?? RollbackParticipants.Try("Clearing the logs", Host.Logs.Clear)
                    ?? RollbackParticipants.Try($"A {RollbackEvents.Restored} handler", () => RollbackParticipants.Push(Host, RollbackEvents.Restored, test, chunks));
            });

            return failure != null ? (null, failure) : (new IsolationReport(IsolationKind.RolledBack, null, chunks, listeners, took)
            {
                DataFilesRestored = files,
                PlayersRestored = restored?.PlayersRestored ?? 0,
                ModDataKeysRestored = restored?.ModDataKeysRestored ?? 0,
                MapRegionsRestored = restored?.MapRegionsRestored ?? 0,
            }, null);
        }

        public void Dispose()
        {
            _tracking?.Dispose();
            try
            {
                Host.Dispose();
            }
            finally
            {
                Sandbox.Dispose();
            }
        }
    }

    /// <summary>
    /// Executes a console command on the server.
    /// </summary>
    /// <param name="command">The full command string including arguments (e.g., "/time set day").</param>
    /// <returns>A task that completes with the command result.</returns>
    /// <exception cref="InvalidOperationException">The server is not running.</exception>
    /// <remarks>
    /// <para>
    /// Commands are executed as if typed in the server console with full privileges.
    /// </para>
    /// <para>
    /// If the command returns <see cref="EnumCommandStatus.Deferred"/>, this method
    /// awaits the async callback before returning the final result.
    /// </para>
    /// </remarks>
    public Task<CommandResult> ExecuteCommandAsync(string command)
    {
        return ExecuteCommandCore(command, caller: null);
    }

    /// <summary>
    /// Executes a console command on the server.
    /// </summary>
    [Obsolete("Use ExecuteCommandAsync instead. This method will be removed in 0.8.")]
    public Task<CommandResult> ExecuteCommand(string command) => ExecuteCommandAsync(command);

    /// <summary>
    /// Executes a command as if issued by the specified test player.
    /// </summary>
    /// <param name="player">The test player to execute the command as.</param>
    /// <param name="command">The full command string including arguments.</param>
    /// <returns>A task that completes with the command result.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="player"/> is null.</exception>
    /// <exception cref="InvalidOperationException">The server is not running or player is not connected.</exception>
    public Task<CommandResult> ExecuteCommandAsPlayerAsync(IServerTestPlayer player, string command)
    {
        ArgumentNullException.ThrowIfNull(player);

        if (player.Player is null)
        {
            throw new InvalidOperationException("Player is not connected to the server.");
        }

        Caller caller = new()
        {
            Type = EnumCallerType.Player,
            Player = player.Player
        };

        return ExecuteCommandCore(command, caller);
    }

    /// <summary>
    /// Executes a command as if issued by the specified test player.
    /// </summary>
    [Obsolete("Use ExecuteCommandAsPlayerAsync instead. This method will be removed in 0.8.")]
    public Task<CommandResult> ExecuteCommandAsPlayer(IServerTestPlayer player, string command) => ExecuteCommandAsPlayerAsync(player, command);

    /// <summary>
    /// Executes a command and asserts that it succeeds.
    /// </summary>
    /// <param name="command">The full command string including arguments.</param>
    /// <returns>A task that completes when the command has executed successfully.</returns>
    /// <exception cref="Xunit.Sdk.XunitException">The command did not succeed.</exception>
    public async Task ExecuteSuccessAsync(string command)
    {
        CommandResult result = await ExecuteCommandAsync(command).ConfigureAwait(false);

        if (!result.Ok)
        {
            throw new CommandExecutionException(
                $"Command '{command}' failed with status {result.Status}: {result.StatusMessage ?? "(no message)"}",
                result);
        }
    }

    /// <summary>
    /// Executes a command and asserts that it succeeds.
    /// </summary>
    [Obsolete("Use ExecuteSuccessAsync instead. This method will be removed in 0.8.")]
    public Task ExecuteSuccess(string command) => ExecuteSuccessAsync(command);

    /// <summary>
    /// Executes a command as a player and asserts that it succeeds.
    /// </summary>
    /// <param name="player">The test player to execute the command as.</param>
    /// <param name="command">The full command string including arguments.</param>
    /// <returns>A task that completes when the command has executed successfully.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="player"/> is null.</exception>
    /// <exception cref="Xunit.Sdk.XunitException">The command did not succeed.</exception>
    public async Task ExecuteSuccessAsPlayerAsync(IServerTestPlayer player, string command)
    {
        CommandResult result = await ExecuteCommandAsPlayerAsync(player, command).ConfigureAwait(false);

        if (!result.Ok)
        {
            throw new CommandExecutionException(
                $"Command '{command}' as player '{player.PlayerUID}' failed with status {result.Status}: {result.StatusMessage ?? "(no message)"}",
                result);
        }
    }

    /// <summary>
    /// Executes a command as a player and asserts that it succeeds.
    /// </summary>
    [Obsolete("Use ExecuteSuccessAsPlayerAsync instead. This method will be removed in 0.8.")]
    public Task ExecuteSuccessAsPlayer(IServerTestPlayer player, string command) => ExecuteSuccessAsPlayerAsync(player, command);

    /// <summary>The caller the game gives a command typed into the server console: an admin with every privilege.</summary>
    internal static Caller ConsoleCaller() => new()
    {
        Type = EnumCallerType.Console,
        CallerRole = "admin",
        CallerPrivileges = ["*"],
        FromChatGroupId = GlobalConstants.ConsoleGroup,
    };

    private Task<CommandResult> ExecuteCommandCore(string command, Caller? caller)
    {
        EmbeddedServerHost host = Server;

        TaskCompletionSource<CommandResult> tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);

        // Strip leading slash if present
        string normalizedCommand = command.TrimStart('/');

        // Parse command and arguments
        int spaceIndex = normalizedCommand.IndexOf(' ');
        string cmdName = spaceIndex >= 0 ? normalizedCommand[..spaceIndex] : normalizedCommand;
        string rawArgs = spaceIndex >= 0 ? normalizedCommand[(spaceIndex + 1)..] : string.Empty;

        // Build calling args
        TextCommandCallingArgs args = new()
        {
            RawArgs = new CmdArgs(rawArgs),
            Caller = caller ?? ConsoleCaller()
        };

        // Execute command with callback for async completion, on the server's game thread like a
        // command typed into the server console
        // ExecuteUnparsed takes the text as typed: it drops the leading slash itself.
        void Execute() => Api.ChatCommands.ExecuteUnparsed("/" + normalizedCommand, args, result =>
        {
            CommandResult cmdResult = new(
                result.Status,
                result.StatusMessage,
                result.Data);

            tcs.TrySetResult(cmdResult);
        });

        host.RunOnGameThread(Execute);

        return tcs.Task;
    }
}
