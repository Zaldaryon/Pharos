using System;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.Server;

namespace PharosCommandMod;

/// <summary>
/// Registers the command tree and the chat handler the command tests use.
/// </summary>
public sealed class PharosCommandModSystem : ModSystem
{
    /// <summary>The privilege the root command needs.</summary>
    public const string UsePrivilege = "buildblocks";

    /// <summary>The privilege the reset subcommand needs.</summary>
    public const string ResetPrivilege = "controlserver";

    /// <summary>The text the chat handler replaces.</summary>
    public const string ChatMarker = "pharoschat";

    /// <summary>What the chat handler replaces the marker with.</summary>
    public const string ChatReplacement = "pharos-chat";

    /// <summary>The text the chat handler consumes.</summary>
    public const string ConsumedMarker = "pharosconsume";

    public override void StartServerSide(ICoreServerAPI api)
    {
        RegisterCommands(api);
        RegisterChat(api);
    }

    private static void RegisterCommands(ICoreServerAPI api)
    {
        IChatCommand root = api.ChatCommands.Create("pharoscmd")
            .WithDescription("pharoscommandmod:pharos-command-root")
            .RequiresPrivilege(UsePrivilege);

        root.BeginSubCommand("reset")
            .WithDescription("pharoscommandmod:pharos-command-reset")
            .RequiresPrivilege(ResetPrivilege)
            .HandleWith(_ => TextCommandResult.Success("reset"))
            .EndSubCommand();

        root.BeginSubCommand("tp")
            .WithDescription("pharoscommandmod:pharos-command-tp")
            .WithArgs(api.ChatCommands.Parsers.WorldPosition("pos"))
            .HandleWith(args => TextCommandResult.Success($"tp {args[0]}"))
            .EndSubCommand();

        root.BeginSubCommand("count")
            .WithDescription("pharoscommandmod:pharos-command-count")
            .WithArgs(api.ChatCommands.Parsers.IntRange("count", 1, 10))
            .HandleWith(args => TextCommandResult.Success($"count {args[0]}"))
            .EndSubCommand();

        root.BeginSubCommand("whoami")
            .WithDescription("pharoscommandmod:pharos-command-whoami")
            .HandleWith(args => TextCommandResult.Success($"whoami {args.Caller.GetName()}"))
            .EndSubCommand();

        api.ChatCommands.Create("pharosalias")
            .WithDescription("pharoscommandmod:pharos-command-alias")
            .RequiresPrivilege(UsePrivilege)
            .HandleWith(_ => TextCommandResult.Success("alias"))
            .WithAlias("pharosalias2");
    }

    private static void RegisterChat(ICoreServerAPI api)
    {
        void OnChat(IServerPlayer byPlayer, int channelId, ref string message, ref string data, BoolRef consumed)
        {
            if (message.Contains(ConsumedMarker, StringComparison.Ordinal))
            {
                consumed.value = true;
                return;
            }

            if (message.Contains(ChatMarker, StringComparison.Ordinal))
            {
                message = message.Replace(ChatMarker, ChatReplacement);
            }
        }

        api.Event.PlayerChat += OnChat;
    }
}
