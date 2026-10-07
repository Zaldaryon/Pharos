using Vintagestory.API.Common;
using Vintagestory.API.Server;
using Vintagestory.Server;
using Xunit;
using Zaldaryon.Pharos.Server;

using Zaldaryon.Pharos.Reporting;
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
/// class. <see cref="WorldIsolation.Restart"/> and <see cref="WorldIsolation.Rollback"/> give every
/// test a freshly booted world. Rollback has no faster in-place restore yet, so it currently
/// isolates by restarting.
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
public abstract class ServerScenarioBase : IAsyncLifetime
{
    private EmbeddedServerHost? _host;
    private IDisposable? _gate;
    private PooledServer? _pooled;
    private readonly List<ServerTestPlayer> _testPlayers = [];

    /// <summary>
    /// Gets the embedded server host managing the test server instance.
    /// Null until <see cref="InitializeAsync"/> completes.
    /// </summary>
    protected EmbeddedServerHost? Host => _host;

    /// <summary>
    /// Gets the underlying Vintage Story server instance.
    /// Null if the host is not initialized.
    /// </summary>
    protected ServerMain? Server => _host?.Server;

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
    /// Boots the server, or takes the pooled one when <see cref="WorldIsolation"/> allows it.
    /// </summary>
    public virtual async Task InitializeAsync()
    {
        _gate = await ScenarioHostPool.EnterAsync(HostWaitTimeout).ConfigureAwait(false);

        try
        {
            ServerWorldOptions options = WorldOptions;
            IReadOnlyList<string> mods = ScenarioAttributes.ServerMods(GetType());
            ServerPoolKey key = new(options, string.Join("|", mods));

            _pooled = WorldIsolation == WorldIsolation.Recycle
                ? ScenarioHostPool.Take<PooledServer>(GetType(), key)
                : null;

            if (_pooled == null)
            {
                ScenarioHostPool.Clear();

                ServerSandbox sandbox = new();
                try
                {
                    ScenarioAttributes.StageMods(mods, sandbox.ModsPath);
                    _pooled = new PooledServer(sandbox, EmbeddedServerHost.Boot(sandbox, options), key);
                }
                catch
                {
                    sandbox.Dispose();
                    throw;
                }
            }

            _host = _pooled.Host;
        }
        catch
        {
            _gate.Dispose();
            _gate = null;
            throw;
        }
    }

    /// <summary>
    /// Stops the server, or pools it for the next test of this class under
    /// <see cref="WorldIsolation.Recycle"/>.
    /// </summary>
    public virtual Task DisposeAsync()
    {
        IReadOnlyList<LogEntry> loggedErrors = LoggedErrorGate.Collect(FailOnLoggedErrors, AllowedLoggedErrors, _host?.Logs);

        try
        {
            foreach (ServerTestPlayer player in _testPlayers)
            {
                player.Dispose();
            }

            _testPlayers.Clear();

            if (_pooled != null)
            {
                if (WorldIsolation == WorldIsolation.Recycle && _pooled.Host.IsRunning)
                {
                    // The next test judges only what it logs itself.
                    _pooled.Host.Logs.Clear();
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
            _host = null;
            _gate?.Dispose();
            _gate = null;
        }

        LoggedErrorGate.ThrowIfAny(loggedErrors);
        return Task.CompletedTask;
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

    private sealed record ServerPoolKey(ServerWorldOptions Options, string Mods);

    private sealed class PooledServer(ServerSandbox sandbox, EmbeddedServerHost host, ServerPoolKey key) : IDisposable
    {
        public ServerSandbox Sandbox { get; } = sandbox;
        public EmbeddedServerHost Host { get; } = host;
        public ServerPoolKey Key { get; } = key;

        public void Dispose()
        {
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
            Caller = caller ?? new Caller { Type = EnumCallerType.Console }
        };

        // Execute command with callback for async completion, on the server's game thread like a
        // command typed into the server console
        void Execute() => Api!.ChatCommands.ExecuteUnparsed(cmdName, args, result =>
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
