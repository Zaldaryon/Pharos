using Vintagestory.API.Common;
using Vintagestory.API.Server;
using Vintagestory.Server;
using Zaldaryon.Pharos.Server;

namespace Zaldaryon.Pharos.XUnit;

/// <summary>
/// Abstract base class for server scenario tests.
/// Test classes inheriting from this should use <see cref="Xunit.IClassFixture{TFixture}"/>
/// to receive the shared or per-class server instance.
/// </summary>
/// <remarks>
/// <para>
/// Provides access to the embedded server, API, and command execution utilities.
/// </para>
/// <para>
/// Commands can be executed synchronously or asynchronously. Deferred commands
/// (those that return <see cref="EnumCommandStatus.Deferred"/>) are automatically
/// awaited using a callback-based resolution pattern.
/// </para>
/// </remarks>
public abstract class ServerScenarioBase
{
    private EmbeddedServerHost? _host;

    /// <summary>
    /// Gets the embedded server host managing the test server instance.
    /// Null until set by the fixture via <see cref="SetHost"/>.
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
    /// Gets the world isolation mode used for tests in this class.
    /// Override in derived classes to change isolation behavior.
    /// </summary>
    protected virtual WorldIsolation WorldIsolation => WorldIsolation.Rollback;

    /// <summary>
    /// Sets the embedded server host. Called by the fixture during initialization.
    /// </summary>
    /// <param name="host">The embedded server host to use for this scenario.</param>
    internal void SetHost(EmbeddedServerHost host)
    {
        _host = host;
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

        // Execute command with callback for async completion
        Api!.ChatCommands.ExecuteUnparsed(cmdName, args, result =>
        {
            CommandResult cmdResult = new(
                result.Status,
                result.StatusMessage,
                result.Data);

            tcs.TrySetResult(cmdResult);
        });

        return tcs.Task;
    }
}
