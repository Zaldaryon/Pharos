using Xunit;
using Zaldaryon.Pharos.Bootstrap;
using Zaldaryon.Pharos.Core;
using Zaldaryon.Pharos.Player;
using Zaldaryon.Pharos.Timing;

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
public abstract class ClientScenarioBase : IAsyncLifetime
{
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
            }
            else if (IsolationMode == IsolationMode.RollbackState && _pooled.Baseline != null)
            {
                GetIsolationManager().RestoreState(_pooled.Client, _pooled.Baseline);
            }

            Client = _pooled.Client;
            GetIsolationManager().PrepareForTest(Client);
        }
        catch
        {
            _gate.Dispose();
            _gate = null;
            throw;
        }
    }

    /// <summary>
    /// Disposes the client, or pools it for the next test of this class.
    /// </summary>
    public virtual Task DisposeAsync()
    {
        try
        {
            if (_pooled != null)
            {
                if (IsolationMode != IsolationMode.FreshClient && !_pooled.Client.IsDisposed)
                {
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
