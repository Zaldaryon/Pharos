using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.Client.NoObf;
using Vintagestory.Common;

namespace Zaldaryon.Pharos.Core;

/// <summary>A chat line the client showed.</summary>
/// <param name="GroupId">The chat group it was shown in.</param>
/// <param name="Message">The text, as the chat dialog shows it.</param>
/// <param name="Type">
/// The kind of line. Every line the client shows itself, a client command's result included, is a
/// <see cref="EnumChatType.Notification"/>; only lines from the server carry other kinds.
/// </param>
public sealed record ClientChatLine(int GroupId, string Message, EnumChatType Type);

/// <summary>What a client command returned, and the chat lines the player saw while it ran.</summary>
/// <param name="Command">The command, as given.</param>
/// <param name="Status">The status the command finished with.</param>
/// <param name="Message">
/// The command's message as the player saw it in chat, or null when it had none. An unknown command
/// has none: the "No such command exists" line the player sees is in <paramref name="ChatLines"/>.
/// </param>
/// <param name="ErrorCode">The command's error code, or null.</param>
/// <param name="Data">What the command's handler returned besides its message, or null.</param>
/// <param name="ChatLines">
/// The chat lines the client showed while the command ran, its result line included. Empty lines
/// are left out.
/// </param>
public sealed record ClientCommandResult(
    string Command, EnumCommandStatus Status, string? Message, string? ErrorCode, object? Data, IReadOnlyList<ClientChatLine> ChatLines)
{
    /// <summary>Whether the command succeeded.</summary>
    public bool Succeeded => Status == EnumCommandStatus.Success;

    /// <summary>
    /// Whether the command succeeded or was deferred: not an error, not an unknown command. The
    /// same rule as <see cref="XUnit.CommandResult.Ok"/> for server commands.
    /// </summary>
    public bool Ok => Status is EnumCommandStatus.Success or EnumCommandStatus.Deferred;

    /// <inheritdoc />
    public override string ToString() =>
        $"{Command}: {Status}{(ErrorCode != null ? $" ({ErrorCode})" : "")}{(Message != null ? $": {Message}" : "")}";
}

/// <summary>A client command that was not <see cref="ClientCommandResult.Ok"/> when it had to be.</summary>
public sealed class ClientCommandException : Exception
{
    internal ClientCommandException(ClientCommandResult result)
        : base($"The client command '{result.Command}' did not succeed: {result.Status}{(result.ErrorCode != null ? $" ({result.ErrorCode})" : "")}{(result.Message != null ? $": {result.Message}" : "")}")
    {
        Result = result;
    }

    /// <summary>What the command returned.</summary>
    public ClientCommandResult Result { get; }
}

/// <summary>
/// Runs a client's chat commands, the ones that start with a dot, and returns their result.
/// </summary>
/// <remarks>
/// <para>
/// A command runs on the client's main thread with the same command lookup, the same privileges
/// and the same result line in chat as when the player types it in the chat dialog. Mods' hooks on
/// chat being sent (<c>OnSendChatMessage</c>) do not run: type the command through
/// <see cref="HeadlessClient.Input"/> to cover them. The chat lines the client shows while it runs
/// are recorded with the result. Lines that arrive after the command has finished, such as the
/// server's answer to a packet the command sent, are not. An engine-mode client that has joined a
/// world is needed.
/// </para>
/// <para>
/// A command whose arguments are looked up later reports <see cref="EnumCommandStatus.Deferred"/>
/// first and its real result once the lookup is done. <see cref="ExecuteAsync"/> waits for it by
/// stepping frames, the server's included when the client is in a
/// <see cref="Server.ClientServerLoopbackSession"/>. A handler that returns
/// <see cref="TextCommandResult.Deferred"/> itself never reports again, so the wait runs to
/// <c>maxFrames</c>, the server ticking all along, and the result is Deferred; pass
/// <c>maxFrames: 0</c> to take the first result as it comes. A real result that comes after the
/// wait is still shown in chat, during whatever steps next.
/// </para>
/// <para>
/// Call it from the test, not from the client thread or a game event, and one command at a time.
/// The typed path stays open: <see cref="HeadlessClient.Ui"/> and <see cref="HeadlessClient.Input"/>
/// can open the chat dialog and type the command for a test that wants to cover it.
/// </para>
/// </remarks>
public sealed class ClientCommandDriver
{
    private readonly HeadlessClient _client;

    internal ClientCommandDriver(HeadlessClient client)
    {
        _client = client;
    }

    /// <summary>
    /// Runs <paramref name="command"/>, such as <c>.clientconfig viewDistance 64</c>, and returns
    /// its result. The leading dot may be left out.
    /// </summary>
    /// <param name="command">The command and its arguments.</param>
    /// <param name="maxFrames">
    /// How many frames a deferred command may take to report its real result. 0 returns the first
    /// result, deferred or not.
    /// </param>
    /// <param name="ct">Cancels the wait.</param>
    /// <exception cref="ArgumentException">The command is empty or is a server command.</exception>
    /// <exception cref="InvalidOperationException">The client has no player: it has not joined a world.</exception>
    public async Task<ClientCommandResult> ExecuteAsync(string command, int maxFrames = 600, CancellationToken ct = default)
    {
        (string name, string args) = Parse(command);
        ArgumentOutOfRangeException.ThrowIfNegative(maxFrames);

        Run run = new(command);
        try
        {
            _client.RunOnClientThread(() => run.Start(Game, name, args));
            for (int frame = 0; !run.IsFinal && frame < maxFrames; frame++)
            {
                ct.ThrowIfCancellationRequested();
                await _client.StepAsync(ct).ConfigureAwait(false);
            }
        }
        finally
        {
            // Never hides the command's own exception: a client that is gone has nothing to clean up.
            try
            {
                _client.RunOnClientThread(() => run.Stop(Game));
            }
            catch (Exception ex) when (_client.IsDisposed || ex is OperationCanceledException)
            {
            }
        }

        // The game always reports at least once, Deferred at worst, before Execute returns.
        return run.Result ?? throw new InvalidOperationException($"The client command '{command}' never reported back.");
    }

    /// <summary>
    /// Runs <paramref name="command"/> and throws unless it succeeded or was deferred. See
    /// <see cref="ExecuteAsync"/>.
    /// </summary>
    /// <exception cref="ClientCommandException">The command failed or does not exist.</exception>
    public async Task<ClientCommandResult> ExecuteSuccessAsync(string command, int maxFrames = 600, CancellationToken ct = default)
    {
        ClientCommandResult result = await ExecuteAsync(command, maxFrames, ct).ConfigureAwait(false);
        return result.Ok ? result : throw new ClientCommandException(result);
    }

    /// <summary>Whether the client has a command or alias called <paramref name="name"/>, given without the dot.</summary>
    public bool Exists(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        string key = name.TrimStart('.').ToLowerInvariant();
        return _client.RunOnClientThread(() => Game.api?.ChatCommands?.Get(key) != null);
    }

    /// <summary>The names of the client's commands and their aliases, without the dot.</summary>
    public IReadOnlyList<string> Names() => _client.RunOnClientThread(() =>
        (IReadOnlyList<string>)(Game.api?.ChatCommands?.Select(c => c.Key).Distinct().Order(StringComparer.Ordinal).ToList() ?? []));

    private ClientMain Game => _client.Client;

    /// <summary>Splits a command into its name and its arguments, as the chat dialog does.</summary>
    internal static (string Name, string Args) Parse(string command)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(command);
        string text = command.Trim();
        if (text.StartsWith('/'))
        {
            throw new ArgumentException($"'{command}' is a server command: run it on the server, with ServerScenarioBase.ExecuteCommand in a server scenario.", nameof(command));
        }

        if (text.StartsWith('.')) text = text[1..];
        if (text.Length == 0) throw new ArgumentException("The command has no name.", nameof(command));

        int space = text.IndexOf(' ');
        return space > 0 ? (text[..space], text[(space + 1)..]) : (text, "");
    }

    /// <summary>One command's run: its result as it comes in, and the chat lines shown meanwhile.</summary>
    private sealed class Run(string command)
    {
        private readonly List<ClientChatLine> _lines = [];
        private ClientCommandResult? _result;
        private ChatLineDelegate? _recorder;
        private bool _isFinal;
        private int _linesBeforeStatus;

        public bool IsFinal
        {
            get
            {
                lock (_lines) return _isFinal;
            }
        }

        public ClientCommandResult? Result
        {
            get
            {
                lock (_lines) return _result;
            }
        }

        public void Start(ClientMain game, string name, string args)
        {
            if (game.player == null || game.api?.ChatCommands is not ChatCommandApi commands || game.eventManager == null)
            {
                throw new InvalidOperationException("Client commands need a client that has joined a world.");
            }

            // Every line the client shows goes through here: a mod's ShowChatMessage, the command's
            // result line and the server's chat lines. They are shown in the current group.
            _recorder = (groupId, message, type, _) =>
            {
                if (string.IsNullOrEmpty(message)) return;
                int group = groupId == GlobalConstants.CurrentChatGroup ? game.currentGroupid : groupId;
                lock (_lines) _lines.Add(new ClientChatLine(group, message, type));
            };
            game.eventManager.OnNewServerToClientChatLine.Add(_recorder);

            commands.Execute(name, game.player, game.currentGroupid, args, result =>
            {
                // The game looked the status line up as a translation key just now: that is not a
                // translation the test's code is missing.
                Translations.TranslationCapture.Forget(result.StatusMessage);
                if (result.Status == EnumCommandStatus.NoSuchCommand) Translations.TranslationCapture.Forget("No such command exists");
                lock (_lines)
                {
                    // The game shows the status message just before it reports back: that line is
                    // the message as the player saw it, translated once.
                    string? message = string.IsNullOrEmpty(result.StatusMessage) ? null
                        : _lines.Count > _linesBeforeStatus ? _lines[^1].Message : null;
                    _linesBeforeStatus = _lines.Count;
                    _result = new ClientCommandResult(command, result.Status, message, result.ErrorCode, result.Data, [.. _lines]);
                    _isFinal = result.Status != EnumCommandStatus.Deferred;
                }
            });
        }

        public void Stop(ClientMain game)
        {
            if (_recorder == null || game.eventManager == null) return;
            game.eventManager.OnNewServerToClientChatLine.Remove(_recorder);
            _recorder = null;
        }
    }
}
