using System.Reflection;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.Common;
using Zaldaryon.Pharos.XUnit;

namespace Zaldaryon.Pharos.Server;

/// <summary>One command or subcommand of the server's command tree.</summary>
/// <param name="Name">The command's own name.</param>
/// <param name="FullName">Its name with the names of its parents.</param>
/// <param name="Aliases">The other names it answers to.</param>
/// <param name="RootAliases">The names it answers to as a root command.</param>
/// <param name="Syntax">How the command is called, with its parameters.</param>
/// <param name="Privilege">The privilege it needs, or the one of its closest parent that has one.</param>
/// <param name="Description">Its description, a translation key.</param>
/// <param name="Incomplete">Whether the command is missing a name, a privilege, a handler or a description.</param>
/// <param name="Subcommands">Its subcommands, by name.</param>
public sealed record CommandNode(
    string Name,
    string FullName,
    IReadOnlyList<string> Aliases,
    IReadOnlyList<string> RootAliases,
    string Syntax,
    string Privilege,
    string Description,
    bool Incomplete,
    IReadOnlyDictionary<string, CommandNode> Subcommands)
{
    /// <summary>The game's command this node stands for.</summary>
    public IChatCommand Command { get; init; } = null!;
}

/// <summary>One argument a command parsed.</summary>
/// <param name="Name">The argument's name, as its parser declares it.</param>
/// <param name="Raw">The text the argument was parsed from, when there was one.</param>
/// <param name="Value">The parsed value, or null when the argument was missing.</param>
/// <param name="Missing">Whether the argument had no value.</param>
/// <param name="Mandatory">Whether the argument has to be there.</param>
public sealed record ParsedCommandArgument(string Name, string? Raw, object? Value, bool Missing, bool Mandatory);

/// <summary>What a command's parsers made of a command line, without running the command.</summary>
/// <param name="Path">The command names the line names, from the root down.</param>
/// <param name="Command">The command the line names, or null when there is none.</param>
/// <param name="Arguments">The parsed arguments, in the order the parsers ran.</param>
/// <param name="Errors">What the parsers complained about.</param>
/// <param name="HasPrivilege">Whether the caller may run the command.</param>
/// <param name="Deferred">Whether a parser was still looking its value up.</param>
public sealed record ParsedCommand(
    IReadOnlyList<string> Path,
    CommandNode? Command,
    IReadOnlyList<ParsedCommandArgument> Arguments,
    IReadOnlyList<string> Errors,
    bool HasPrivilege,
    bool Deferred)
{
    /// <summary>Whether the line named a command and every parser accepted it.</summary>
    public bool Ok => Command != null && Errors.Count == 0;

    /// <summary>The value of the argument named <paramref name="name"/>, or null.</summary>
    public object? Arg(string name)
    {
        foreach (ParsedCommandArgument argument in Arguments)
        {
            if (string.Equals(argument.Name, name, StringComparison.OrdinalIgnoreCase)) return argument.Value;
        }

        return null;
    }

    /// <summary>The value of the argument named <paramref name="name"/>, or the default.</summary>
    public T? Arg<T>(string name) => Arg(name) is T value ? value : default;
}

/// <summary>The server's commands: their tree, their parsers, and the callers that may run them.</summary>
public sealed class CommandDriver
{
    private readonly EmbeddedServerHost _host;

    internal CommandDriver(EmbeddedServerHost host) => _host = host;

    /// <summary>The command tree of a command, or the trees of the commands a mod registered.</summary>
    /// <param name="rootOrModId">
    /// The name of a command, such as <c>mycmd</c>, or the id of a mod, such as <c>mymod</c>.
    /// </param>
    /// <exception cref="ArgumentException">Neither a command nor a mod goes by that name.</exception>
    public IReadOnlyDictionary<string, CommandNode> Tree(string rootOrModId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootOrModId);
        return _host.RunOnGameThread(() => TreeCore(rootOrModId));
    }

    /// <summary>One command's tree, by name.</summary>
    public CommandNode? Node(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return _host.RunOnGameThread(() =>
        {
            IChatCommand? command = _host.Api.ChatCommands.Get(name);
            return command == null ? null : NodeOf(command);
        });
    }

    /// <summary>Runs a command as a given caller and returns what it returned.</summary>
    /// <param name="command">The command line, with or without its leading slash.</param>
    /// <param name="caller">Who runs it: the console, a player, an entity or a position.</param>
    /// <param name="ct">Cancels the wait.</param>
    public Task<CommandResult> ExecuteAsync(string command, Caller? caller = null, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(command);
        TaskCompletionSource<CommandResult> tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
        _host.RunOnGameThread(() => ExecuteCore(command, caller ?? ConsoleCaller(), tcs));
        return tcs.Task.WaitAsync(ct);
    }

    /// <summary>
    /// Runs a command's parsers without running its handler, and returns what they made of the line.
    /// </summary>
    /// <param name="command">The command line, with or without its leading slash.</param>
    /// <param name="caller">Who runs it: the console, a player, an entity or a position.</param>
    /// <param name="maxTicks">How many ticks a parser may take to look its value up.</param>
    /// <param name="ct">Cancels the wait.</param>
    public Task<ParsedCommand> ParseAsync(string command, Caller? caller = null, int maxTicks = 600, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(command);
        Caller effective = caller ?? ConsoleCaller();
        ParseOutcome outcome = _host.RunOnGameThread(() => ParseCore(command, effective));

        // A parser that had to look its value up gets the server's ticks, the way the game's own
        // command execution waits for it.
        for (int tick = 0; outcome.HasPending && tick < maxTicks && !ct.IsCancellationRequested; tick++)
        {
            _host.Tick();
        }

        return Task.FromResult(_host.RunOnGameThread(outcome.Complete));
    }

    /// <summary>The caller the game gives a command typed into the server console: an admin with every privilege.</summary>
    public static Caller ConsoleCaller() => new()
    {
        Type = EnumCallerType.Console,
        CallerRole = "admin",
        CallerPrivileges = ["*"],
        FromChatGroupId = GlobalConstants.ConsoleGroup,
    };

    /// <summary>A caller that runs a command as a player.</summary>
    public static Caller PlayerCaller(IServerPlayer player)
    {
        ArgumentNullException.ThrowIfNull(player);
        return new()
        {
            Type = EnumCallerType.Player,
            Player = player,
        };
    }

    /// <summary>A caller that runs a command as an entity.</summary>
    public static Caller EntityCaller(Entity entity)
    {
        ArgumentNullException.ThrowIfNull(entity);
        return new()
        {
            Type = EnumCallerType.Entity,
            Entity = entity,
        };
    }

    /// <summary>A caller that runs a command at a position.</summary>
    public static Caller PositionCaller(Vec3d position) => new()
    {
        Type = EnumCallerType.Block,
        Pos = position,
    };

    private IReadOnlyDictionary<string, CommandNode> TreeCore(string rootOrModId)
    {
        IChatCommandApi commands = _host.Api.ChatCommands;
        if (commands.Get(rootOrModId) is { } command)
        {
            return new Dictionary<string, CommandNode>(StringComparer.OrdinalIgnoreCase)
            {
                [command.Name] = NodeOf(command),
            };
        }

        Dictionary<string, CommandNode> trees = new(StringComparer.OrdinalIgnoreCase);
        foreach ((string name, IChatCommand modCommand) in CommandRegistry.CommandsOf(_host, rootOrModId))
        {
            trees[name] = NodeOf(modCommand);
        }

        if (trees.Count == 0)
        {
            throw new ArgumentException(
                $"No command or mod is named '{rootOrModId}'. The server has {commands.Count()} command(s).",
                nameof(rootOrModId));
        }

        return trees;
    }

    private static CommandNode NodeOf(IChatCommand command)
    {
        Dictionary<string, CommandNode> subcommands = new(StringComparer.OrdinalIgnoreCase);
        foreach ((string name, IChatCommand subcommand) in command.AllSubcommands)
        {
            subcommands[name] = NodeOf(subcommand);
        }

        ChatCommandImpl? impl = command as ChatCommandImpl;
        return new CommandNode(
            command.Name ?? "",
            command.FullName ?? "",
            command.Aliases is null ? [] : [.. command.Aliases],
            command.RootAliases is null ? [] : [.. command.RootAliases],
            command.CallSyntax ?? "",
            impl?.GetPrivilege() ?? "",
            command.Description ?? "",
            command.Incomplete,
            subcommands)
        {
            Command = command,
        };
    }

    private void ExecuteCore(string command, Caller caller, TaskCompletionSource<CommandResult> tcs)
    {
        string normalized = command.TrimStart('/');
        TextCommandCallingArgs args = new()
        {
            RawArgs = new CmdArgs(Stripped(normalized)),
            Caller = caller,
        };

        _host.Api.ChatCommands.ExecuteUnparsed("/" + normalized, args, outcome =>
        {
            tcs.TrySetResult(new CommandResult(outcome.Status, outcome.StatusMessage, outcome.Data));
        });
    }

    private ParseOutcome ParseCore(string command, Caller caller)
    {
        string normalized = command.TrimStart('/');
        int space = normalized.IndexOf(' ');
        string name = space >= 0 ? normalized[..space] : normalized;
        string rawArgs = space >= 0 ? normalized[(space + 1)..] : "";

        IChatCommand? root = _host.Api.ChatCommands.Get(name);
        if (root == null)
        {
            return ParseOutcome.Failed([name], $"No such command: {name}");
        }

        List<string> path = [root.Name ?? name];
        List<ParsedCommandArgument> arguments = [];
        List<string> errors = [];
        List<(ICommandArgumentParser Parser, TaskCompletionSource<AsyncParseResults> Pending)> pending = [];
        bool dependsOnSubsequent = false;
        ICommandArgumentParser? dependsOnParser = null;

        TextCommandCallingArgs args = new()
        {
            RawArgs = new CmdArgs(rawArgs),
            Caller = caller,
            Command = root,
        };

        IChatCommand current = root;
        while (true)
        {
            ICommandArgumentParser[] parsers = CommandRegistry.ParsersOf(current);
            foreach (ICommandArgumentParser parser in parsers)
            {
                parser.PreProcess(args);
                if (parser.IsMissing)
                {
                    arguments.Add(new ParsedCommandArgument(parser.ArgumentName, null, null, true, parser.IsMandatoryArg));
                    if (parser.IsMandatoryArg)
                    {
                        errors.Add($"Missing argument: {parser.ArgumentName}");
                        break;
                    }

                    continue;
                }

                TaskCompletionSource<AsyncParseResults> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
                EnumParseResult outcome = parser.TryProcess(args, data => completion.TrySetResult(data));
                if (outcome == EnumParseResult.Good)
                {
                    arguments.Add(new ParsedCommandArgument(parser.ArgumentName, parser.GetValue()?.ToString(), parser.GetValue(), false, parser.IsMandatoryArg));
                    continue;
                }

                if (outcome == EnumParseResult.Deferred)
                {
                    pending.Add((parser, completion));
                    arguments.Add(new ParsedCommandArgument(parser.ArgumentName, null, null, false, parser.IsMandatoryArg));
                    continue;
                }

                if (outcome == EnumParseResult.Bad)
                {
                    errors.Add($"{parser.ArgumentName}: {parser.LastErrorMessage ?? "wrong argument"}");
                    arguments.Add(new ParsedCommandArgument(parser.ArgumentName, null, null, false, parser.IsMandatoryArg));
                    break;
                }

                dependsOnSubsequent = true;
                dependsOnParser = parser;
            }

            if (errors.Count > 0) break;

            string? next = args.RawArgs.PeekWord()?.ToLowerInvariant();
            if (next != null && current.AllSubcommands.TryGetValue(next, out IChatCommand? subcommand))
            {
                args.SubCmdCode = args.RawArgs.PopWord();
                current = subcommand;
                path.Add(current.Name ?? next);
                args.Command = current;
                continue;
            }

            break;
        }

        if (dependsOnSubsequent && dependsOnParser != null)
        {
            errors.Add($"{dependsOnParser.ArgumentName}: the argument depends on the ones after it");
        }

        CommandNode? node = NodeOf(current);
        bool hasPrivilege = current.IsAvailableTo(caller);
        if (!hasPrivilege) errors.Add($"The caller has no privilege for {current.FullName}");

        return new ParseOutcome(path, node, arguments, errors, hasPrivilege, pending);
    }

    /// <summary>What a command's parsers made of a line, and the lookups that are still running.</summary>
    private sealed class ParseOutcome(
        IReadOnlyList<string> path,
        CommandNode? node,
        List<ParsedCommandArgument> arguments,
        List<string> errors,
        bool hasPrivilege,
        List<(ICommandArgumentParser Parser, TaskCompletionSource<AsyncParseResults> Pending)> pending)
    {
        public static ParseOutcome Failed(IReadOnlyList<string> path, string error) =>
            new(path, null, [], [error], false, []);

        /// <summary>Whether a parser is still looking its value up.</summary>
        public bool HasPending => pending.Any(entry => !entry.Pending.Task.IsCompleted);

        /// <summary>The parsed command, with the values the deferred parsers found.</summary>
        public ParsedCommand Complete()
        {
            List<ParsedCommandArgument> values = [.. arguments];
            List<string> problems = [.. errors];

            foreach ((ICommandArgumentParser parser, TaskCompletionSource<AsyncParseResults> completion) in pending)
            {
                if (!completion.Task.IsCompleted)
                {
                    problems.Add($"{parser.ArgumentName}: the value was not found in time");
                    continue;
                }

                AsyncParseResults result = completion.Task.Result;
                if (result.Status == EnumParseResultStatus.Error)
                {
                    problems.Add($"{parser.ArgumentName}: {parser.LastErrorMessage ?? "the value was not found"}");
                    continue;
                }

                parser.SetValue(result.Data);
                int index = values.FindIndex(argument => string.Equals(argument.Name, parser.ArgumentName, StringComparison.OrdinalIgnoreCase));
                ParsedCommandArgument argument = new(parser.ArgumentName, parser.GetValue()?.ToString(), parser.GetValue(), false, parser.IsMandatoryArg);
                if (index >= 0) values[index] = argument;
                else values.Add(argument);
            }

            return new ParsedCommand(path, node, values, problems, hasPrivilege, pending.Count > 0);
        }
    }

    private static string Stripped(string command)
    {
        int space = command.IndexOf(' ');
        return space >= 0 ? command[(space + 1)..] : "";
    }
}
