// Generated from docs/commands.md
#nullable enable
#pragma warning disable CS0105, CS0168, CS0219, CS8600, CS8602, CS8604, CS8618, CS8669, CS0414, CS1998, CS0067, CS0649, CS0169, CS0618, CS4014, xUnit2029, xUnit1008

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.Client;
using Vintagestory.Client.NoObf;
using Vintagestory.Common;
using Vintagestory.Server;
using Xunit;
using ProtoBuf;
using Zaldaryon.Pharos;
using Zaldaryon.Pharos.Assertions;
using Zaldaryon.Pharos.Bridge;
using Zaldaryon.Pharos.Core;
using Zaldaryon.Pharos.Events;
using Zaldaryon.Pharos.Fixtures;
using Zaldaryon.Pharos.Graphics;
using Zaldaryon.Pharos.Network;
using Zaldaryon.Pharos.Player;
using Zaldaryon.Pharos.Reporting;
using Zaldaryon.Pharos.Server;
using Zaldaryon.Pharos.Translations;
using Zaldaryon.Pharos.UI;
using Zaldaryon.Pharos.Visual;
using Zaldaryon.Pharos.World;
using Zaldaryon.Pharos.Inspection;
using Zaldaryon.Pharos.Timing;
using Zaldaryon.Pharos.Audio;
using Zaldaryon.Pharos.Culling;
using Zaldaryon.Pharos.Input;
using Zaldaryon.Pharos.Memory;
using Zaldaryon.Pharos.Performance;
using Zaldaryon.Pharos.XUnit;
using Zaldaryon.Pharos.Bootstrap;
using Zaldaryon.Pharos.Docs.Tests;
using WorldOptions = global::Atlas.Api.WorldOptions;



namespace Zaldaryon.Pharos.Docs.Generated.commands.Snippet_1
{

#line 7 "docs/commands.md"
[ServerMods("mods/mymod")]
public class MyModCommandTests : ServerScenarioBase
{
    [DocExample]
    public void TheCommandTreeIsComplete()
    {
        CommandNode root = Server.Commands.Tree("mycmd")["mycmd"];

        Assert.Equal("mymod:admin", root.Privilege);
        Assert.Equal("mymod:mycmd-desc", root.Description);
        Assert.Contains("reset", root.Subcommands);
    }
}
#line default
}


namespace Zaldaryon.Pharos.Docs.Generated.commands.Snippet_2
{

    public class ScenarioContainer : ClientServerScenarioBase
    {

        protected BlockPos nearClient => new(10, 64, 10);
        protected BlockPos nearAlice => new(10, 64, 10);
        protected BlockPos chestPos => new(10, 64, 10);
        protected BlockPos pos => new(10, 64, 10);
        protected BlockPos lamp => new(10, 64, 10);
        protected BlockPos lever => new(10, 64, 10);
        protected BlockPos doorPos => new(10, 64, 10);
        protected new string PlayerName => "TestPlayer";
        protected PlayerVitals vitals => null!;
        protected ClientVitals clientVitals => Player.Vitals;
        protected ServerTestPlayer player => null!;
        protected ClientServerLoopbackSession session => Session;
        protected EmbeddedServerHost serverHost => Server;
        protected BlockPos ground => new(10, 64, 10);
        protected long wolfId => 1;
        protected Entity wolf => null!;
        protected int SparkColor => 0;
        protected List<BlockPos> expectedBlocks => [];
        protected ICoreServerAPI sapi => Api;
        protected string playerUid => "playerUid";
        protected ServerTestPlayer alice => null!;
        protected ServerTestPlayer bob => null!;
        protected Entity entity => null!;
        protected BotResult used => new(true);
        protected List<string> myCache => [];
        protected DoorMock Door(BlockPos pos) => new();
        protected string DumpInventory() => string.Empty;
        protected string _lastFailure = string.Empty;
        protected List<BlockPos> marked => [];
        protected new int DataFilePort(string name) => 8080;
        protected EmbeddedServerHost host => ServerHost;
        protected EmbeddedServerHost Host => ServerHost;
        protected ICoreServerAPI api => Api;
        protected HeadlessClient client => Client;

#line 32 "docs/commands.md"
[DocExample]
public void MyModsCommandsAreValid()
{
    PharosAssert.CommandsValid(Server, "mymod");
}
#line default
    }
}


namespace Zaldaryon.Pharos.Docs.Generated.commands.Snippet_3
{

    public class ScenarioContainer : ClientServerScenarioBase
    {

        protected BlockPos nearClient => new(10, 64, 10);
        protected BlockPos nearAlice => new(10, 64, 10);
        protected BlockPos chestPos => new(10, 64, 10);
        protected BlockPos pos => new(10, 64, 10);
        protected BlockPos lamp => new(10, 64, 10);
        protected BlockPos lever => new(10, 64, 10);
        protected BlockPos doorPos => new(10, 64, 10);
        protected new string PlayerName => "TestPlayer";
        protected PlayerVitals vitals => null!;
        protected ClientVitals clientVitals => Player.Vitals;
        protected ServerTestPlayer player => null!;
        protected ClientServerLoopbackSession session => Session;
        protected EmbeddedServerHost serverHost => Server;
        protected BlockPos ground => new(10, 64, 10);
        protected long wolfId => 1;
        protected Entity wolf => null!;
        protected int SparkColor => 0;
        protected List<BlockPos> expectedBlocks => [];
        protected ICoreServerAPI sapi => Api;
        protected string playerUid => "playerUid";
        protected ServerTestPlayer alice => null!;
        protected ServerTestPlayer bob => null!;
        protected Entity entity => null!;
        protected BotResult used => new(true);
        protected List<string> myCache => [];
        protected DoorMock Door(BlockPos pos) => new();
        protected string DumpInventory() => string.Empty;
        protected string _lastFailure = string.Empty;
        protected List<BlockPos> marked => [];
        protected new int DataFilePort(string name) => 8080;
        protected EmbeddedServerHost host => ServerHost;
        protected EmbeddedServerHost Host => ServerHost;
        protected ICoreServerAPI api => Api;
        protected HeadlessClient client => Client;

#line 45 "docs/commands.md"
[DocExample]
public async Task ThePositionIsRelativeToTheCaller()
{
    ServerTestPlayer alice = await CreateTestPlayerAsync("alice");
    alice.GrantPrivilege("buildblocks");

    ParsedCommand parsed = await Server.Commands.ParseAsync("/mycmd tp ~1 ~ ~-2", CommandDriver.PlayerCaller(alice.Player!));

    Assert.True(parsed.Ok);
    Assert.Equal(alice.Entity!.Pos.XYZ.AddCopy(1, 0, -2), parsed.Arg<Vec3d>("pos"));
}
#line default
    }
}


namespace Zaldaryon.Pharos.Docs.Generated.commands.Snippet_4
{

    public class ScenarioContainer : ClientServerScenarioBase
    {

        protected BlockPos nearClient => new(10, 64, 10);
        protected BlockPos nearAlice => new(10, 64, 10);
        protected BlockPos chestPos => new(10, 64, 10);
        protected BlockPos pos => new(10, 64, 10);
        protected BlockPos lamp => new(10, 64, 10);
        protected BlockPos lever => new(10, 64, 10);
        protected BlockPos doorPos => new(10, 64, 10);
        protected new string PlayerName => "TestPlayer";
        protected PlayerVitals vitals => null!;
        protected ClientVitals clientVitals => Player.Vitals;
        protected ServerTestPlayer player => null!;
        protected ClientServerLoopbackSession session => Session;
        protected EmbeddedServerHost serverHost => Server;
        protected BlockPos ground => new(10, 64, 10);
        protected long wolfId => 1;
        protected Entity wolf => null!;
        protected int SparkColor => 0;
        protected List<BlockPos> expectedBlocks => [];
        protected ICoreServerAPI sapi => Api;
        protected string playerUid => "playerUid";
        protected ServerTestPlayer alice => null!;
        protected ServerTestPlayer bob => null!;
        protected Entity entity => null!;
        protected BotResult used => new(true);
        protected List<string> myCache => [];
        protected DoorMock Door(BlockPos pos) => new();
        protected string DumpInventory() => string.Empty;
        protected string _lastFailure = string.Empty;
        protected List<BlockPos> marked => [];
        protected new int DataFilePort(string name) => 8080;
        protected EmbeddedServerHost host => ServerHost;
        protected EmbeddedServerHost Host => ServerHost;
        protected ICoreServerAPI api => Api;
        protected HeadlessClient client => Client;

#line 68 "docs/commands.md"
[DocExample]
public async Task TheCommandRunsAsThePlayer()
{
    ServerTestPlayer alice = await CreateTestPlayerAsync("alice");
    alice.GrantPrivilege("buildblocks");

    CommandResult result = await Server.Commands.ExecuteAsync("/mycmd whoami", CommandDriver.PlayerCaller(alice.Player!));

    Assert.Equal(EnumCommandStatus.Success, result.Status);
}
#line default
    }
}


namespace Zaldaryon.Pharos.Docs.Generated.commands.Snippet_5
{

    public class ScenarioContainer : ClientServerScenarioBase
    {

        protected BlockPos nearClient => new(10, 64, 10);
        protected BlockPos nearAlice => new(10, 64, 10);
        protected BlockPos chestPos => new(10, 64, 10);
        protected BlockPos pos => new(10, 64, 10);
        protected BlockPos lamp => new(10, 64, 10);
        protected BlockPos lever => new(10, 64, 10);
        protected BlockPos doorPos => new(10, 64, 10);
        protected new string PlayerName => "TestPlayer";
        protected PlayerVitals vitals => null!;
        protected ClientVitals clientVitals => Player.Vitals;
        protected ServerTestPlayer player => null!;
        protected ClientServerLoopbackSession session => Session;
        protected EmbeddedServerHost serverHost => Server;
        protected BlockPos ground => new(10, 64, 10);
        protected long wolfId => 1;
        protected Entity wolf => null!;
        protected int SparkColor => 0;
        protected List<BlockPos> expectedBlocks => [];
        protected ICoreServerAPI sapi => Api;
        protected string playerUid => "playerUid";
        protected ServerTestPlayer alice => null!;
        protected ServerTestPlayer bob => null!;
        protected Entity entity => null!;
        protected BotResult used => new(true);
        protected List<string> myCache => [];
        protected DoorMock Door(BlockPos pos) => new();
        protected string DumpInventory() => string.Empty;
        protected string _lastFailure = string.Empty;
        protected List<BlockPos> marked => [];
        protected new int DataFilePort(string name) => 8080;
        protected EmbeddedServerHost host => ServerHost;
        protected EmbeddedServerHost Host => ServerHost;
        protected ICoreServerAPI api => Api;
        protected HeadlessClient client => Client;

#line 89 "docs/commands.md"
[DocExample]
[CommandPrivilegeMatrix("/mycmd reset", "suvisitor", "suplayer", "admin", Expected = "denied,denied,allowed")]
public async Task ResetNeedsAdmin(CommandPrivilegeCase privilege)
{
    await privilege.AssertAsync(Server);
}
#line default
    }
}


namespace Zaldaryon.Pharos.Docs.Generated.commands.Snippet_6
{

    public class ScenarioContainer : ClientServerScenarioBase
    {

        protected BlockPos nearClient => new(10, 64, 10);
        protected BlockPos nearAlice => new(10, 64, 10);
        protected BlockPos chestPos => new(10, 64, 10);
        protected BlockPos pos => new(10, 64, 10);
        protected BlockPos lamp => new(10, 64, 10);
        protected BlockPos lever => new(10, 64, 10);
        protected BlockPos doorPos => new(10, 64, 10);
        protected new string PlayerName => "TestPlayer";
        protected PlayerVitals vitals => null!;
        protected ClientVitals clientVitals => Player.Vitals;
        protected ServerTestPlayer player => null!;
        protected ClientServerLoopbackSession session => Session;
        protected EmbeddedServerHost serverHost => Server;
        protected BlockPos ground => new(10, 64, 10);
        protected long wolfId => 1;
        protected Entity wolf => null!;
        protected int SparkColor => 0;
        protected List<BlockPos> expectedBlocks => [];
        protected ICoreServerAPI sapi => Api;
        protected string playerUid => "playerUid";
        protected ServerTestPlayer alice => null!;
        protected ServerTestPlayer bob => null!;
        protected Entity entity => null!;
        protected BotResult used => new(true);
        protected List<string> myCache => [];
        protected DoorMock Door(BlockPos pos) => new();
        protected string DumpInventory() => string.Empty;
        protected string _lastFailure = string.Empty;
        protected List<BlockPos> marked => [];
        protected new int DataFilePort(string name) => 8080;
        protected EmbeddedServerHost host => ServerHost;
        protected EmbeddedServerHost Host => ServerHost;
        protected ICoreServerAPI api => Api;
        protected HeadlessClient client => Client;

#line 107 "docs/commands.md"
[DocExample]
public async Task TheChatHandlerRewritesTheMessage()
{
    using ChatLog chat = Server.Chat.Record();
    ServerTestPlayer alice = await CreateTestPlayerAsync("alice");

    await alice.SayAsync("hello world");

    ChatMessage message = Assert.Single(chat.Messages);
    Assert.Contains("hello world", message.OriginalText, StringComparison.Ordinal);
    Assert.Equal("hello world", message.FinalPlainText);
}
#line default
    }
}
