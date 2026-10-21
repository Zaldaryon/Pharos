using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Xunit;
using Zaldaryon.Pharos.Assertions;
using Zaldaryon.Pharos.Core;
using Zaldaryon.Pharos.Server;
using Zaldaryon.Pharos.Tests.Support;
using Zaldaryon.Pharos.XUnit;

namespace Zaldaryon.Pharos.Tests.Server;

/// <summary>The server's command tree, its parsers, its callers and its chat handlers.</summary>
[Collection("Sequential")]
[Trait(PharosTraits.Category, PharosTraits.Live)]
[ServerMods("TestMods/pharoscommandmod")]
public class LiveCommandTests : ServerScenarioBase
{
    [ServerScenario]
    public void TheTree_ShowsTheSubcommandsAndTheirPrivileges()
    {
        CommandNode root = Server.Commands.Tree("pharoscmd")["pharoscmd"];

        Assert.Equal("buildblocks", root.Privilege);
        Assert.Equal("pharoscommandmod:pharos-command-root", root.Description);
        Assert.False(root.Incomplete);
        Assert.Contains("reset", root.Subcommands);
        Assert.Equal("controlserver", root.Subcommands["reset"].Privilege);
        Assert.Contains("pos", root.Subcommands["tp"].Syntax);
    }

    [ServerScenario]
    public void TheModsCommands_AreFound()
    {
        IReadOnlyDictionary<string, CommandNode> tree = Server.Commands.Tree("pharoscommandmod");

        Assert.Contains("pharoscmd", tree);
        Assert.Contains("pharosalias", tree);
        Assert.Contains("pharosalias2", tree["pharosalias"].Aliases);
    }

    [ServerScenario]
    public void AnUnknownName_IsReported()
    {
        ArgumentException failure = Assert.Throws<ArgumentException>(() => Server.Commands.Tree("nosuchcommand"));

        Assert.Contains("nosuchcommand", failure.Message);
    }

    [ServerScenario]
    public void TheCommands_AreValid()
    {
        PharosAssert.CommandsValid(Server, "pharoscommandmod");
    }

    [ServerScenario]
    public async Task TheParsers_RunWithoutTheHandler()
    {
        ParsedCommand parsed = await Server.Commands.ParseAsync("pharoscmd count 3");

        Assert.True(parsed.Ok, string.Join(", ", parsed.Errors));
        Assert.Equal(["pharoscmd", "count"], parsed.Path);
        Assert.Equal(3, parsed.Arg<int>("count"));
    }

    [ServerScenario]
    public async Task ABadArgument_IsReported()
    {
        ParsedCommand parsed = await Server.Commands.ParseAsync("pharoscmd count 99");

        Assert.False(parsed.Ok);
        Assert.NotEmpty(parsed.Errors);
    }

    [ServerScenario]
    public async Task ThePositionParser_ReadsARelativePosition()
    {
        ServerTestPlayer player = await CreateTestPlayerAsync(PlayerNames.Next());
        player.GrantPrivilege("buildblocks");
        Vec3d position = player.Entity!.Pos.XYZ;

        ParsedCommand parsed = await Server.Commands.ParseAsync("pharoscmd tp ~1 ~ ~-2", CommandDriver.PlayerCaller(player.Player!));

        Assert.True(parsed.Ok, string.Join(", ", parsed.Errors));
        Vec3d parsedPosition = parsed.Arg<Vec3d>("pos")!;
        Assert.Equal(position.X + 1, parsedPosition.X, 3);
        Assert.Equal(position.Y, parsedPosition.Y, 3);
        Assert.Equal(position.Z - 2, parsedPosition.Z, 3);
    }

    [ServerScenario]
    public async Task TheCommand_RunsAsItsCaller()
    {
        ServerTestPlayer player = await CreateTestPlayerAsync(PlayerNames.Next());
        player.GrantPrivilege("buildblocks");

        CommandResult result = await Server.Commands.ExecuteAsync("pharoscmd whoami", CommandDriver.PlayerCaller(player.Player!));

        Assert.True(result.Status == EnumCommandStatus.Success, $"{result.Status}: {result.StatusMessage}");
        Assert.Contains(player.PlayerName!, result.StatusMessage);
    }

    [ServerTheory]
    [CommandPrivilegeMatrix("/pharoscmd reset", "suvisitor", "suplayer", "admin", Expected = "denied,denied,allowed")]
    public async Task ResetNeedsTheRightPrivilege(CommandPrivilegeCase privilege)
    {
        bool allowed = await privilege.AssertAsync(Server);

        Assert.Equal(privilege.Expected == CommandPrivilegeCase.Allowed, allowed);
    }

    [ServerScenario]
    public async Task TheChatHandler_RewritesTheMessage()
    {
        using ChatLog chat = Server.Chat.Record();
        ServerTestPlayer player = await CreateTestPlayerAsync(PlayerNames.Next());

        await player.SayAsync("hello pharoschat");

        ChatMessage message = Assert.Single(chat.Messages);
        Assert.Equal(player.PlayerName, message.PlayerName);
        Assert.Contains("hello pharoschat", message.OriginalText, StringComparison.Ordinal);
        Assert.Contains("hello pharos-chat", message.FinalText, StringComparison.Ordinal);
        Assert.DoesNotContain("pharoschat", message.FinalPlainText, StringComparison.Ordinal);
        Assert.False(message.Consumed);
    }

    [ServerScenario]
    public async Task TheChatHandler_CanConsumeAMessage()
    {
        using ChatLog chat = Server.Chat.Record();
        ServerTestPlayer player = await CreateTestPlayerAsync(PlayerNames.Next());

        await player.SayAsync("pharosconsume");

        ChatMessage message = Assert.Single(chat.Messages);
        Assert.True(message.Consumed);
    }
}
