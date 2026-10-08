using Vintagestory.API.Common;
using Vintagestory.API.Config;
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
    private readonly List<ServerTestPlayer> _testPlayers = [];

    /// <summary>
    /// Gets the embedded server host managing the test server instance.
    /// Null until <see cref="InitializeAsync"/> completes.
    /// </summary>
    protected EmbeddedServerHost? Host => _host ?? _capturing;

    // The server while OnRollbackCaptured runs, before the test has it.
    private EmbeddedServerHost? _capturing;

    /// <summary>
    /// Gets the underlying Vintage Story server instance.
    /// Null if the host is not initialized.
    /// </summary>
    protected ServerMain? Server => Host?.Server;

    /// <summary>
    /// Gets the server-side API for game interactions.
    /// Null if the server is not running.
    /// </summary>
    protected ICoreServerAPI? Api => Server?.Api as ICoreServerAPI;

    /// <summary>
    /// Gets the world isolation mode used for tests in this class. Defaults to the
    /// <see cref="ServerWorldAttribute.Isolation"/> of the class, or
    /// <see cref="WorldIsolation.Rollback"/>.
    /// </summary>
    protected virtual WorldIsolation WorldIsolation =>
        ScenarioAttributes.ServerWorld(GetType())?.Isolation ?? WorldIsolation.Rollback;

    /// <summary>
    /// Gets the world to boot. Defaults to the class-level <see cref="ServerWorldAttribute"/>.
    /// </summary>
    protected virtual ServerWorldOptions WorldOptions => ScenarioAttributes.WorldOptions(GetType());

    /// <summary>
    /// Whether a test whose world cannot be rolled back fails, under <see cref="WorldIsolation.Rollback"/>.
    /// Defaults to <see cref="ServerWorldAttribute.StrictIsolation"/>.
    /// </summary>
    protected virtual bool StrictIsolation => ScenarioAttributes.ServerWorld(GetType())?.StrictIsolation ?? false;

    /// <summary>
    /// How this test's server was made ready: booted, rolled back from the previous test, or
    /// recycled, with what it cost and why it was booted again when a rollback was expected.
    /// Null until <see cref="InitializeAsync"/> completes.
    /// </summary>
    protected IsolationReport? Isolation { get; private set; }

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
            ServerWorldOptions options = WorldOptions;
            IReadOnlyList<string> mods = ScenarioAttributes.ServerMods(GetType());
            ServerPoolKey key = new(options, string.Join("|", mods), WorldIsolation);

            _pooled = WorldIsolation is WorldIsolation.Recycle or WorldIsolation.Rollback
                ? ScenarioHostPool.Take<PooledServer>(GetType(), key)
                : null;

            bool reused = _pooled != null;
            long bootStart = System.Diagnostics.Stopwatch.GetTimestamp();
            _host = null;
            if (_pooled == null)
            {
                ScenarioHostPool.Clear();

                ServerSandbox sandbox = new();
                try
                {
                    ScenarioAttributes.StageMods(mods, sandbox.ModsPath);
                    _pooled = new PooledServer(sandbox, EmbeddedServerHost.Boot(sandbox, options), key);
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

                if (WorldIsolation == WorldIsolation.Rollback)
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

            Isolation = IsolationLog.Prepared(GetType(), reused, WorldIsolation == WorldIsolation.Recycle, System.Diagnostics.Stopwatch.GetElapsedTime(bootStart));
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

    void IScenarioLifecycle.BeforeBody() => _run.BodyStarting(frames: null, ticks: _host?.TickCount);

    void IScenarioLifecycle.CheckLoggedErrors() =>
        LoggedErrorGate.ThrowIfAny(LoggedErrorGate.Collect(FailOnLoggedErrors, AllowedLoggedErrors, _host?.Logs));

    void IScenarioLifecycle.BodyTimedOut(bool stillRunning) => _run.BodyTimedOut(stillRunning);

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
        Isolation = WorldIsolation.ToString(),
        IsolationReport = Isolation,
        ServerTicks = ScenarioRun.Since(_run.TicksAtStart, server?.TickCount),
        Artifacts = Artifacts,
        AddFiles = OnFailure,
    };

    /// <summary>
    /// Stops the server, or pools it for the next test of this class under
    /// <see cref="WorldIsolation.Recycle"/>.
    /// </summary>
    public virtual Task DisposeAsync()
    {
        IReadOnlyList<LogEntry> loggedErrors = _run.PipelineChecksLoggedErrors
            ? []
            : LoggedErrorGate.Collect(FailOnLoggedErrors, AllowedLoggedErrors, _host?.Logs);

        if (_run.Abandoned)
        {
            IsolationLog.Left(GetType(), null, "the previous test timed out and was left running");
            // The timed-out body still runs on the game thread: the host is left as it is.
            _testPlayers.Clear();
            _pooled = null;
            _host = null;
            _gate?.Dispose();
            _gate = null;
            return Task.CompletedTask;
        }

        IsolationException? isolationFailure = null;
        try
        {
            foreach (ServerTestPlayer player in _testPlayers)
            {
                player.Dispose();
            }

            _testPlayers.Clear();

            if (_pooled != null)
            {
                string? fallback = WhyNotReusable(_pooled);
                IsolationReport? rolledBack = null;
                if (fallback == null && WorldIsolation == WorldIsolation.Rollback)
                {
                    (rolledBack, fallback) = _pooled.TryRollback(IsTestListener, ScenarioTestInfo.Current?.DisplayName);
                    fallback ??= RollbackParticipants.Try(nameof(OnRollbackRestored), OnRollbackRestored);
                    if (fallback != null) rolledBack = null;
                }

                bool expected = WorldIsolation is WorldIsolation.Recycle or WorldIsolation.Rollback;
                if (fallback == null && expected)
                {
                    // The next test judges only what it logs itself. A rollback clears the logs
                    // before it tells the mods, so what their handlers log counts against it.
                    if (WorldIsolation == WorldIsolation.Recycle) _pooled.Host.Logs.Clear();
                    ScenarioHostPool.Return(GetType(), _pooled.Key, _pooled);
                }
                else
                {
                    if (fallback != null && expected) ServerMain.Logger?.Warning("Pharos could not keep the server for the next test, which boots a fresh one: {0}", fallback);
                    _pooled.Dispose();
                    ScenarioHostPool.HostDisposed();
                }

                IsolationLog.Left(GetType(), rolledBack, expected ? fallback : null, kept: fallback == null && expected);
                if (fallback != null && WorldIsolation == WorldIsolation.Rollback && StrictIsolation && !_run.TimedOut && !_pooled.Sandbox.IsRetained)
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

        ScenarioFailures.ThrowAll(isolationFailure, () => LoggedErrorGate.ThrowIfAny(loggedErrors));
        return Task.CompletedTask;
    }

    // Why the server cannot be kept for the next test, before any rollback is tried.
    private string? WhyNotReusable(PooledServer pooled) =>
        !pooled.Host.IsRunning ? "the server stopped"
        : _run.TimedOut ? "the test timed out"
        : pooled.Sandbox.IsRetained ? "the sandbox was kept for inspection"
        : null;

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

    private sealed record ServerPoolKey(ServerWorldOptions Options, string Mods, WorldIsolation Isolation);

    private sealed class PooledServer(ServerSandbox sandbox, EmbeddedServerHost host, ServerPoolKey key) : IDisposable
    {
        private WorldSnapshot? _baseline;
        private IDisposable? _tracking;
        private ListenerWatermark? _listeners;

        public ServerSandbox Sandbox { get; } = sandbox;
        public EmbeddedServerHost Host { get; } = host;
        public ServerPoolKey Key { get; } = key;

        /// <summary>Captures the world as booted, for <see cref="TryRollback"/>.</summary>
        public void TakeBaseline()
        {
            _baseline = Host.TakeSnapshot();
            _tracking = Host.RunOnGameThread(() => _baseline.TrackChunksLoadedLater(Host));
        }

        /// <summary>Notes the listeners that live as long as the server; later ones are the tests'.</summary>
        public void CaptureListeners() => _listeners = RollbackParticipants.Capture(Host);

        /// <summary>
        /// Removes the listeners the test left, puts the world back as booted and tells the
        /// server's mods. Returns what it did, or why it failed.
        /// </summary>
        public (IsolationReport? Report, string? Failure) TryRollback(System.Func<Delegate, bool> ownedByTest, string? test)
        {
            if (_baseline == null) return (null, "the world was never captured");

            int listeners = 0, chunks = 0;
            string? failure = null;
            TimeSpan took = IsolationLog.Time(() =>
            {
                failure = RollbackParticipants.Try("Removing the test's listeners", () => listeners = RollbackParticipants.Remove(Host, _listeners, ownedByTest))
                    ?? RollbackParticipants.Try("The rollback", () => chunks = Host.RestoreSnapshot(_baseline))
                    ?? (Host.IsRunning ? null : "the server stopped during the rollback")
                    ?? RollbackParticipants.Try("Clearing the logs", Host.Logs.Clear)
                    ?? RollbackParticipants.Try($"A {RollbackEvents.Restored} handler", () => RollbackParticipants.Push(Host, RollbackEvents.Restored, test, chunks));
            });

            return failure != null ? (null, failure) : (new IsolationReport(IsolationKind.RolledBack, null, chunks, listeners, took), null);
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
    public Task<CommandResult> ExecuteCommand(string command)
    {
        if (Server is null || Api is null)
        {
            throw new InvalidOperationException("Server is not running. Ensure the host is initialized before executing commands.");
        }

        return ExecuteCommandCore(command, caller: null);
    }

    /// <summary>
    /// Executes a command as if issued by the specified test player.
    /// </summary>
    /// <param name="player">The test player to execute the command as.</param>
    /// <param name="command">The full command string including arguments.</param>
    /// <returns>A task that completes with the command result.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="player"/> is null.</exception>
    /// <exception cref="InvalidOperationException">The server is not running or player is not connected.</exception>
    public Task<CommandResult> ExecuteCommandAsPlayer(IServerTestPlayer player, string command)
    {
        ArgumentNullException.ThrowIfNull(player);

        if (Server is null || Api is null)
        {
            throw new InvalidOperationException("Server is not running. Ensure the host is initialized before executing commands.");
        }

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
    /// Executes a command and asserts that it succeeds.
    /// </summary>
    /// <param name="command">The full command string including arguments.</param>
    /// <returns>A task that completes when the command has executed successfully.</returns>
    /// <exception cref="Xunit.Sdk.XunitException">The command did not succeed.</exception>
    public async Task ExecuteSuccess(string command)
    {
        CommandResult result = await ExecuteCommand(command).ConfigureAwait(false);

        if (!result.Ok)
        {
            throw new CommandExecutionException(
                $"Command '{command}' failed with status {result.Status}: {result.StatusMessage ?? "(no message)"}",
                result);
        }
    }

    /// <summary>
    /// Executes a command as a player and asserts that it succeeds.
    /// </summary>
    /// <param name="player">The test player to execute the command as.</param>
    /// <param name="command">The full command string including arguments.</param>
    /// <returns>A task that completes when the command has executed successfully.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="player"/> is null.</exception>
    /// <exception cref="Xunit.Sdk.XunitException">The command did not succeed.</exception>
    public async Task ExecuteSuccessAsPlayer(IServerTestPlayer player, string command)
    {
        CommandResult result = await ExecuteCommandAsPlayer(player, command).ConfigureAwait(false);

        if (!result.Ok)
        {
            throw new CommandExecutionException(
                $"Command '{command}' as player '{player.PlayerUID}' failed with status {result.Status}: {result.StatusMessage ?? "(no message)"}",
                result);
        }
    }

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
        void Execute() => Api!.ChatCommands.ExecuteUnparsed("/" + normalizedCommand, args, result =>
        {
            CommandResult cmdResult = new(
                result.Status,
                result.StatusMessage,
                result.Data);

            tcs.TrySetResult(cmdResult);
        });

        if (_host is null) Execute();
        else _host.RunOnGameThread(Execute);

        return tcs.Task;
    }
}
