using Xunit;
using Zaldaryon.Pharos.Bootstrap;
using Zaldaryon.Pharos.Core;
using Zaldaryon.Pharos.Player;
using Zaldaryon.Pharos.Reporting;
using Zaldaryon.Pharos.Server;
using Zaldaryon.Pharos.Timing;
using Zaldaryon.Pharos.XUnit.Execution;

namespace Zaldaryon.Pharos.XUnit;

/// <summary>
/// Abstract base class for client scenario tests: boots a headless client before each test and
/// tears it down, or pools it, afterwards.
/// </summary>
/// <remarks>
/// <para>
/// The client is booted from <see cref="ClientOptions"/>. Mods listed by
/// <see cref="PharosModsAttribute"/> on the class or the assembly are staged into a mod folder the
/// client loads from. Only an engine-mode client loads mods; a fixture-mode client never starts
/// the mod loader.
/// </para>
/// <para>
/// <see cref="IsolationMode.SharedClient"/> keeps the client for the next test of the same class
/// as it is. <see cref="IsolationMode.RollbackState"/> keeps it too, but puts the player position
/// and camera back where they were when the client was first booted.
/// <see cref="IsolationMode.FreshClient"/> boots a new client for every test.
/// </para>
/// <para>
/// Scenarios run one at a time, whatever the test collection layout: the game keeps process-wide
/// static state, so two clients or servers cannot boot side by side in one process.
/// </para>
/// </remarks>
public abstract class ClientScenarioBase : IAsyncLifetime, IScenarioLifecycle
{
    private readonly ScenarioRun _run = new();
    private bool _recordingForArtifacts;
    private IDisposable? _classSettings;
    private ClientIsolationManager? _isolationManager;
    private IDisposable? _gate;
    private PooledClient? _pooled;

    /// <summary>
    /// The headless client instance. Null until <see cref="InitializeAsync"/> completes.
    /// </summary>
    protected HeadlessClient? Client { get; private set; }

    /// <summary>
    /// Test player abstraction for controlling and querying the player entity.
    /// </summary>
    protected IClientTestPlayer? Player => Client?.TestPlayer;

    /// <summary>
    /// Frame controller for deterministic frame stepping.
    /// </summary>
    protected DeterministicFrameController? FrameController => Client?.FrameController;

    /// <summary>
    /// The isolation mode used for tests in this class.
    /// Override in derived classes to change isolation behavior.
    /// </summary>
    protected virtual IsolationMode IsolationMode => IsolationMode.SharedClient;

    /// <summary>
    /// The options the client is booted with. Fixture mode by default.
    /// </summary>
    protected virtual HeadlessClientOptions ClientOptions => new();

    /// <summary>
    /// Gets how long a test waits for another scenario to release the host before it fails.
    /// </summary>
    protected virtual TimeSpan HostWaitTimeout => TimeSpan.FromMinutes(10);

    /// <summary>
    /// Whether the test fails when the client logged an error during it that
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
    /// Called when the test fails, while the client is still up, to add files of the test's own
    /// to <paramref name="directory"/>, the folder of its failure artifacts.
    /// </summary>
    protected virtual void OnFailure(string directory)
    {
    }

    /// <summary>
    /// Gets the isolation manager, creating it lazily with the current IsolationMode.
    /// </summary>
    /// <returns>The ClientIsolationManager for this scenario.</returns>
    protected ClientIsolationManager GetIsolationManager()
    {
        return _isolationManager ??= new ClientIsolationManager(IsolationMode);
    }

    /// <summary>
    /// Boots the client, or takes the pooled one when <see cref="IsolationMode"/> allows it.
    /// </summary>
    public virtual async Task InitializeAsync()
    {
        BootCheck.ThrowIfFailedBefore(GetType());
        if (GetType().IsDefined(typeof(DataFilesAttribute), inherit: true)
            || Execution.ScenarioTestInfo.Current?.Method?.IsDefined(typeof(DataFilesAttribute), inherit: true) == true)
        {
            throw new NotSupportedException("[DataFiles] works in ServerScenarioBase and ClientServerScenarioBase classes, not in client-only scenarios.");
        }

        _gate = await ScenarioHostPool.EnterAsync(HostWaitTimeout).ConfigureAwait(false);

        try
        {
            HeadlessClientOptions options = ClientOptions;
            IReadOnlyList<string> mods = ScenarioAttributes.ClientMods(GetType());
            ClientPoolKey key = new(options.BootMode, options.Width, options.Height, options.CharacterClass, string.Join("|", mods));

            _pooled = IsolationMode == IsolationMode.FreshClient
                ? null
                : ScenarioHostPool.Take<PooledClient>(GetType(), key);

            if (_pooled == null)
            {
                ScenarioHostPool.Clear();
                _pooled = PooledClient.Boot(options, mods, key);

                // An engine-mode client here never joins a server, so its mods never start: its
                // boot ends here, and what its tests log is not part of it.
                _pooled.Client.Logs.CompleteBoot();
                if (BootCheck.Enforce(GetType(), _pooled.Client.BootDiagnostics))
                {
                    _pooled.Client.Logs.Clear();
                }
            }
            else if (IsolationMode == IsolationMode.RollbackState && _pooled.Baseline != null)
            {
                GetIsolationManager().RestoreState(_pooled.Client, _pooled.Baseline);
            }

            Client = _pooled.Client;
            GetIsolationManager().PrepareForTest(Client);

            if (ScenarioAttributes.ClientSettings(GetType()) is { } settings)
            {
                _classSettings = Client.Settings.Apply(settings);
            }
        }
        catch (Exception ex)
        {
            Exception failure = ScenarioRun.SetupFailed(ex, (error, test) => Describe(test, error, timedOut: false, _pooled?.Client));

            // xUnit does not call DisposeAsync after a failed InitializeAsync: the client goes
            // now, or the next scenario boots a second one beside it.
            try
            {
                _classSettings?.Dispose();
            }
            catch
            {
                // The client is disposed below anyway.
            }

            _classSettings = null;
            if (_pooled != null)
            {
                try
                {
                    _pooled.Dispose();
                }
                catch
                {
                    // A client that fails to shut down must not hide why it failed to start.
                }

                _pooled = null;
                ScenarioHostPool.HostDisposed();
            }

            Client = null;

            _gate.Dispose();
            _gate = null;
            if (ReferenceEquals(failure, ex)) throw;
            throw failure;
        }
    }

    /// <summary>
    /// The client's boot diagnostics judged against the class's <see cref="AllowBootDiagnosticAttribute"/>s,
    /// whether or not the class is <see cref="StrictBootAttribute"/>. See <c>docs/boot-diagnostics.md</c>.
    /// </summary>
    protected BootDiagnosticsResult UnexpectedBootDiagnostics => BootCheck.Evaluate(GetType(), Client?.BootDiagnostics);

    void IScenarioLifecycle.BeforeBody()
    {
        // Each test sees its own missing translations, in the language the client booted with.
        if (Client is { IsEngineMode: true } langClient)
        {
            langClient.Lang.RestoreBootLanguage();
            langClient.Lang.Reset();
        }

        _run.BodyStarting(Client?.FrameController.TotalFrames, ticks: null);
        if (Artifacts.HasFlag(FailureArtifacts.Packets) && Client is { PacketRecorder.IsRecording: false } client)
        {
            client.PacketRecorder.Start();
            _recordingForArtifacts = true;
        }
    }

    void IScenarioLifecycle.CheckLoggedErrors() =>
        LoggedErrorGate.ThrowIfAny(LoggedErrorGate.Collect(FailOnLoggedErrors, AllowedLoggedErrors, Client?.Logs));

    void IScenarioLifecycle.BodyTimedOut(bool stillRunning) => _run.BodyTimedOut(stillRunning);

    EmbeddedServerHost? IScenarioLifecycle.FixtureServer => null;

    string? IScenarioLifecycle.CaptureFailure(ScenarioTestInfo test, Exception exception, bool timedOut) =>
        FailureArtifactWriter.Write(Describe(test, exception, timedOut, Client)).Summary();

    private ScenarioFailure Describe(ScenarioTestInfo test, Exception exception, bool timedOut, HeadlessClient? client) => new()
    {
        DisplayName = test.DisplayName,
        TestClass = test.TestClass,
        MethodName = test.MethodName,
        TimedOut = timedOut,
        Exception = exception,
        Client = client,
        // A fixture-mode client renders on the test's own thread: after a timeout the body may
        // still hold its GL context. An engine-mode client's capture queues on its own thread.
        ClientReachable = !_run.Abandoned && !(timedOut && client?.BootMode == ClientBootMode.Fixture),
        Isolation = IsolationMode.ToString(),
        Frames = ScenarioRun.Since(_run.FramesAtStart, client?.FrameController.TotalFrames),
        Artifacts = Artifacts,
        AddFiles = OnFailure,
    };

    /// <summary>
    /// Disposes the client, or pools it for the next test of this class.
    /// </summary>
    public virtual Task DisposeAsync()
    {
        if (!_run.Abandoned && Client is { IsEngineMode: true } langClient) langClient.Lang.RestoreBootLanguage();

        IReadOnlyList<LogEntry> loggedErrors = _run.PipelineChecksLoggedErrors
            ? []
            : LoggedErrorGate.Collect(FailOnLoggedErrors, AllowedLoggedErrors, Client?.Logs);

        if (_run.Abandoned)
        {
            // The timed-out body still drives the client: it is left as it is.
            _classSettings = null;
            _pooled = null;
            Client = null;
            _gate?.Dispose();
            _gate = null;
            return Task.CompletedTask;
        }

        // The recording started for the failure artifacts must not run on into the next test.
        if (_recordingForArtifacts && Client is { IsDisposed: false } recorded)
        {
            recorded.PacketRecorder.Stop();
            recorded.PacketRecorder.Clear();
        }

        // Settings are process-wide, and a pooled client serves the next test as it is left.
        try
        {
            _classSettings?.Dispose();
        }
        finally
        {
            _classSettings = null;
        }

        try
        {
            if (_pooled != null)
            {
                if (IsolationMode != IsolationMode.FreshClient && !_pooled.Client.IsDisposed && !_run.TimedOut && !_pooled.Client.IsDataPathRetained)
                {
                    // The next test judges only what it logs itself.
                    _pooled.Client.Logs.Clear();
                    ScenarioHostPool.Return(GetType(), _pooled.Key, _pooled);
                }
                else
                {
                    _pooled.Dispose();
                    ScenarioHostPool.HostDisposed();
                }
            }
        }
        finally
        {
            _pooled = null;
            Client = null;
            _gate?.Dispose();
            _gate = null;
        }

        LoggedErrorGate.ThrowIfAny(loggedErrors);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Sets the client instance directly, bypassing <see cref="InitializeAsync"/>.
    /// </summary>
    internal void SetClient(HeadlessClient client)
    {
        Client = client;
    }

    private sealed record ClientPoolKey(ClientBootMode BootMode, int Width, int Height, string CharacterClass, string Mods);

    private sealed class PooledClient : IDisposable
    {
        private readonly string? _modsDirectory;

        private PooledClient(HeadlessClient client, ClientPoolKey key, string? modsDirectory)
        {
            Client = client;
            Key = key;
            _modsDirectory = modsDirectory;
            Baseline = IsolationContext.Capture(client.TestPlayer, client.FrameController);
        }

        public HeadlessClient Client { get; }
        public ClientPoolKey Key { get; }
        public IsolationContext? Baseline { get; }

        public static PooledClient Boot(HeadlessClientOptions options, IReadOnlyList<string> mods, ClientPoolKey key)
        {
            string? modsDirectory = null;
            if (mods.Count > 0)
            {
                modsDirectory = Path.Combine(Path.GetTempPath(), "pharos-client-mods-" + Guid.NewGuid().ToString("N")[..8]);
                ScenarioAttributes.StageMods(mods, modsDirectory);
                options = WithModPath(options, modsDirectory);
            }

            try
            {
                return new PooledClient(HeadlessClientBootstrap.Boot(options), key, modsDirectory);
            }
            catch
            {
                DeleteDirectory(modsDirectory);
                throw;
            }
        }

        public void Dispose()
        {
            try
            {
                Client.Dispose();
            }
            finally
            {
                DeleteDirectory(_modsDirectory);
            }
        }

        private static HeadlessClientOptions WithModPath(HeadlessClientOptions options, string modsDirectory) => new()
        {
            Width = options.Width,
            Height = options.Height,
            GameInstallPath = options.GameInstallPath,
            DataPath = options.DataPath,
            AssetsPath = options.AssetsPath,
            DisableAudio = options.DisableAudio,
            UseNullAudioDevice = options.UseNullAudioDevice,
            ConfigureMesaEnvironment = options.ConfigureMesaEnvironment,
            ForceSoftwareRendering = options.ForceSoftwareRendering,
            MesaGlVersionOverride = options.MesaGlVersionOverride,
            MesaGlslVersionOverride = options.MesaGlslVersionOverride,
            LinuxDisplay = options.LinuxDisplay,
            BootMode = options.BootMode,
            CompleteCharacterSelection = options.CompleteCharacterSelection,
            CharacterClass = options.CharacterClass,
            LoadBridge = options.LoadBridge,
            ModPaths = [.. options.ModPaths, modsDirectory],
        };

        private static void DeleteDirectory(string? path)
        {
            if (path == null || !Directory.Exists(path)) return;

            try
            {
                Directory.Delete(path, recursive: true);
            }
            catch
            {
                // Best effort temporary cleanup
            }
        }
    }
}
