// Generated from docs/writing-server-scenarios.md
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

[assembly: Seeded]                       // every scenario test in the assembly

namespace Zaldaryon.Pharos.Docs.Generated.writing_server_scenarios.Snippet_1
{
    using Zaldaryon.Pharos.XUnit;
using Xunit;
#line 17 "docs/writing-server-scenarios.md"
[ServerWorld(seed: 12345, playStyle: "creativebuilding", worldType: "superflat")]
public class BlockPlacementTests : ServerScenarioBase
{
    [DocExample]
    public void SetBlock_PlacesBlockAtPosition()
    {
        var pos = new BlockPos(10, 64, 10);
        this.SetBlock(pos, "game:soil-low-none");

        var block = this.GetBlock(pos);
        Assert.Contains("soil", block.Code.Path);
    }
}
#line default
}


namespace Zaldaryon.Pharos.Docs.Generated.writing_server_scenarios.Snippet_2
{
    using Vintagestory.API.MathTools;
using Zaldaryon.Pharos.World;
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

#line 44 "docs/writing-server-scenarios.md"
[DocExample]
public async Task BuildAndCheckAWall()
{
    BlockPos origin = new(10, 80, 10);
    await Host!.EnsureChunksLoadedAsync(origin, origin.AddCopy(2, 0, 1));

    Dictionary<char, string> blocks = new()
    {
        ['S'] = "game:stone",
        ['W'] = "game:water-still-7",
    };
    string[] rows = ["SS.", ".W."];

    Scene.Build(Host, origin, blocks, rows);
    Scene.Assert(Host, origin, blocks, rows);
}
#line default
    }
}


namespace Zaldaryon.Pharos.Docs.Generated.writing_server_scenarios.Snippet_3
{
    
    public interface IApiReference
    {
#line 78 "docs/writing-server-scenarios.md"
// Block access
void SetBlock(BlockPos pos, string blockCode);
void SetBlock(BlockPos pos, int blockId);
Block GetBlock(BlockPos pos);
BlockEntity? GetBlockEntity(BlockPos pos);

// Entity spawning
Entity SpawnEntity(string entityCode, Vec3d pos);
#line default
    }
}


namespace Zaldaryon.Pharos.Docs.Generated.writing_server_scenarios.Snippet_4
{
    
    public interface IApiReference
    {
#line 91 "docs/writing-server-scenarios.md"
// Console caller
Task<CommandResult> ExecuteCommand(string command);
Task ExecuteSuccess(string command);  // asserts Ok == true

// Player caller
Task<CommandResult> ExecuteCommandAsPlayer(IServerTestPlayer player, string command);
Task ExecuteSuccessAsPlayer(IServerTestPlayer player, string command);
#line default
    }
}


namespace Zaldaryon.Pharos.Docs.Generated.writing_server_scenarios.Snippet_5
{
    
    public interface IApiReference
    {
#line 105 "docs/writing-server-scenarios.md"
Task WaitForChunkLoadedAsync(int cx, int cy, int cz, int maxTicks = 300);
Task<Entity> WaitForEntitySpawnAsync(string entityCode, double radius, Vec3d around, int maxTicks = 300);
Task WaitForConditionAsync(Func<bool> condition, int maxTicks = 300);
#line default
    }
}


namespace Zaldaryon.Pharos.Docs.Generated.writing_server_scenarios.Snippet_6
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

        
        public async Task ExecuteSnippetAsync()
        {
#line 121 "docs/writing-server-scenarios.md"
await SucceedWhenAsync(() => Assert.Equal("lit", GetBlock(lamp).Variant["state"]), maxTicks: 200);

await Sequence()
    .Then(() => SetBlock(lever, "game:lever-on"))
    .Idle(10)
    .WaitUntil(() => Assert.True(Door(doorPos).Open))
    .FailIf(() => Assert.True(Player.Vitals.Health < 10))
    .RunAsync(maxTicks: 400);
#line default
        }
    }
}


namespace Zaldaryon.Pharos.Docs.Generated.writing_server_scenarios.Snippet_7
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

        
        public async Task ExecuteSnippetAsync()
        {
#line 151 "docs/writing-server-scenarios.md"
Entity wolf = await Host.Entities.SpawnAsync("game:wolf-male", pos, entity =>
    entity.WatchedAttributes.SetString("test-owner", "scenario"));

IReadOnlyList<string> active = Host.Entities.ActiveTasks(wolf);
Host.Entities.StartTask(wolf, "seekentity");
Host.Entities.StopTask(wolf, "seekentity");

await Host.Entities.WaitUntilAtAsync(wolf, Player.Entity!.Pos.XYZ, radius: 2, maxTicks: 600);
EntityDied death = await Host.Entities.WaitForDeathAsync(wolf, maxTicks: 600);
EntityDespawned despawn = await Host.Entities.WaitForDespawnAsync(wolf, maxTicks: 600);
#line default
        }
    }
}


namespace Zaldaryon.Pharos.Docs.Generated.writing_server_scenarios.Snippet_8
{
    
#line 217 "docs/writing-server-scenarios.md"
[ServerWorld(seed: 42, Isolation = Isolation.Recycle)]
public class ReadOnlyTests : ServerScenarioBase { }
#line default
}


namespace Zaldaryon.Pharos.Docs.Generated.writing_server_scenarios.Snippet_9
{
    
#line 258 "docs/writing-server-scenarios.md"
[ServerWorld(Isolation = Isolation.Rollback)]
public class EconomyTests : ClientServerScenarioBase
{
    // Save-game, map region and player mod data keys.
    protected override bool RestoreModData(string key) => key != "mymod:lifetimeStats";

    // Top-level keys of the joined player's WatchedAttributes and Attributes.
    protected override bool RestorePlayerAttribute(string key) => key != "mymod:reputation";
}
#line default
}


namespace Zaldaryon.Pharos.Docs.Generated.writing_server_scenarios.Snippet_10
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

        
        public async Task ExecuteSnippetAsync()
        {
#line 292 "docs/writing-server-scenarios.md"
api.Event.RegisterEventBusListener((string name, ref EnumHandling handling, IAttribute data) =>
{
    if (name == "pharos:rollback:restored") myCache.Clear();
});
#line default
        }
    }
}


namespace Zaldaryon.Pharos.Docs.Generated.writing_server_scenarios.Snippet_11
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

        
        public async Task ExecuteSnippetAsync()
        {
#line 326 "docs/writing-server-scenarios.md"
WorldSnapshot snapshot = host.TakeSnapshot();
// ... change the world ...
int restoredChunks = host.RestoreSnapshot(snapshot);
#line default
        }
    }
}


namespace Zaldaryon.Pharos.Docs.Generated.writing_server_scenarios.Snippet_12
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

#line 336 "docs/writing-server-scenarios.md"
[DocExample]
public async Task Player_CanReceiveItems()
{
    ServerTestPlayer player = await CreateTestPlayerAsync("TestPlayer");

    player.GiveItem("game:stick", 5);
    Assert.True(player.HasItem("game:stick", 5));

    player.GrantPrivilege("worldedit");
    await player.TeleportTo(100.0, 64.0, 100.0);
    await player.SayAsync("/time set day"); // runs as a command with the player as caller
}
#line default
    }
}


namespace Zaldaryon.Pharos.Docs.Generated.writing_server_scenarios.Snippet_13
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

        
        public async Task ExecuteSnippetAsync()
        {
#line 361 "docs/writing-server-scenarios.md"
var alice = await CreateTestPlayerAsync("Alice");
var bob = await CreateTestPlayerAsync("Bob");
Assert.Equal(2, Host.TestPlayers.Count);
#line default
        }
    }
}


namespace Zaldaryon.Pharos.Docs.Generated.writing_server_scenarios.Snippet_14
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

        
        public async Task ExecuteSnippetAsync()
        {
#line 377 "docs/writing-server-scenarios.md"
await alice.SayAsync("hello");
Assert.Contains(bob.Received.ChatMessages, m => m.Contains("hello"));

Entity hen = this.SpawnEntity("game:chicken-hen", nearAlice);
await Host.TickUntilAsync(() => alice.Received.KnowsEntity(hen.EntityId));

Assert.True(alice.Received.HasReceivedPlayerData(bob.PlayerUID));
Assert.NotEmpty(alice.Received.Particles);
Assert.Contains(alice.Received.Sounds, s => s.Name.Contains("planks"));
Assert.Equal(marked, alice.Received.HighlightedBlocks(slotId: 7));
Assert.Contains(alice.Received.BlockChanges, c => c.Position.Equals(pos));

// A mod's own network channel, deserialized by message type
var pings = Host.ModPackets<MyPing>(alice, "mymod");
#line default
        }
    }
}


namespace Zaldaryon.Pharos.Docs.Generated.writing_server_scenarios.Snippet_15
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

        
        public async Task ExecuteSnippetAsync()
        {
#line 415 "docs/writing-server-scenarios.md"
using VirtualClock clock = Host!.UseVirtualClock(tickMs: 1000);
bob.Network.Freeze();
clock.Advance(TimeSpan.FromSeconds(160), TimeSpan.FromSeconds(1));
Assert.False(bob.IsConnected);
#line default
        }
    }
}


namespace Zaldaryon.Pharos.Docs.Generated.writing_server_scenarios.Snippet_16
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

        
        public async Task ExecuteSnippetAsync()
        {
#line 433 "docs/writing-server-scenarios.md"
ServerTestPlayer bob = await CreateTestPlayerAsync("Bob");
BotResult walk = await bob.Bot.WalkToAsync(new Vec3d(12, 4, 10), maxTicks: 600);
Assert.True(walk.Accepted, walk.Error?.Code ?? walk.Reason);

BotResult selected = await bob.Bot.SelectSlotAsync(0);
BotResult broken = await bob.Bot.BreakBlockAsync(pos);
Assert.True(selected.Accepted, selected.Error?.Code ?? selected.Reason);
Assert.True(broken.Accepted, broken.Error?.Code ?? broken.Reason);

BotResult used = await bob.Bot.UseBlockAsync(chestPos);
IInventory chest = ((IBlockEntityContainer)GetBlockEntity(chestPos)!).Inventory;
IInventory hotbar = bob.Player!.InventoryManager.GetHotbarInventory();
BotResult moved = await bob.Bot.MoveSlotAsync(hotbar.InventoryID, 0, chest.InventoryID, 3, quantity: 1);
Assert.True(used.Accepted && moved.Accepted);
#line default
        }
    }
}


namespace Zaldaryon.Pharos.Docs.Generated.writing_server_scenarios.Snippet_17
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

        
        public async Task ExecuteSnippetAsync()
        {
#line 467 "docs/writing-server-scenarios.md"
Entity boat = this.SpawnEntity("game:boat-raft-aged", nearClient);
Assert.True(await Session!.Entities.UseAsync(boat));
Assert.True(await Session.StepUntilAsync(
    () => Session.Player.MountedOn?.Entity?.EntityId == boat.EntityId
        && ServerHost!.RunOnGameThread(() => ServerHost.Server.GetClientByPlayername(PlayerName)?.Entityplayer?.MountedOn?.Entity?.EntityId == boat.EntityId)));
#line default
        }
    }
}


namespace Zaldaryon.Pharos.Docs.Generated.writing_server_scenarios.Snippet_18
{
    
#line 479 "docs/writing-server-scenarios.md"
[ServerWorld(seed: 4242, playStyle: "surviveandbuild")]
public class HungerTests : ServerScenarioBase
{
    [DocExample]
    public async Task Eating_RaisesSatietyAndTheProteinLevel()
    {
        ServerTestPlayer player = await CreateTestPlayerAsync("Alice");
        PlayerVitals vitals = player.Vitals;

        Assert.Equal(15, vitals.MaxHealth);
        Assert.Equal(1500, vitals.Saturation);

        // Only what is named changes, through the behaviour, so what the game derives updates.
        vitals.Set(health: 5, saturation: 300);
        Assert.Equal(5, vitals.Health);

        // Food feeds one nutrient level. The multiplier is the one a mod's food passes.
        vitals.ReceiveSaturation(250, EnumFoodCategory.Protein, nutritionGainMultiplier: 2f);
        Assert.Equal(200, vitals.Nutrition!.Protein);

        // Spending it, the way an attack or a health regeneration does, without waiting ten seconds.
        vitals.ConsumeSaturation(100);
    }
}
#line default
}


namespace Zaldaryon.Pharos.Docs.Generated.writing_server_scenarios.Snippet_19
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

        
        public async Task ExecuteSnippetAsync()
        {
#line 521 "docs/writing-server-scenarios.md"
VitalsReading before = vitals.Read();
Assert.Equal(15, before.MaxHealth);
Assert.Equal(1500, before.Saturation);
#line default
        }
    }
}


namespace Zaldaryon.Pharos.Docs.Generated.writing_server_scenarios.Snippet_20
{
    
#line 541 "docs/writing-server-scenarios.md"
[ServerWorld(seed: 4242, playStyle: "surviveandbuild")]
public class ArmorTests : ServerScenarioBase
{
    [DocExample]
    public async Task AHit_GoesThroughTheModsArmor()
    {
        ServerTestPlayer player = await CreateTestPlayerAsync("Alice");
        Entity wolf = await Host!.SpawnEntityAsync("wolf-male", player.Position);

        await player.DamageAsync(10, DamageInfo.FromEntity(wolf, EnumDamageType.PiercingAttack));

        // The mod's armour took 5 off, and the health behaviour did the rest.
        Assert.Equal(10, player.Vitals.Health);
    }
}
#line default
}


namespace Zaldaryon.Pharos.Docs.Generated.writing_server_scenarios.Snippet_21
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

#line 584 "docs/writing-server-scenarios.md"
[DocExample]
public async Task EntityMovesToTarget()
{
    Entity entity = await Host.Entities.SpawnAsync("game:wolf-male", new Vec3d(0, 64, 0));
    Vec3d target = new(10, 64, 10);

    await Host.Entities.WaitUntilAtAsync(entity, target, radius: 1, maxTicks: 600);
}
#line default
    }
}


namespace Zaldaryon.Pharos.Docs.Generated.writing_server_scenarios.Snippet_22
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

        
        public async Task ExecuteSnippetAsync()
        {
#line 599 "docs/writing-server-scenarios.md"
using VirtualClock clock = Host!.UseVirtualClock(tickMs: 50);
Host.Ticks(600);                              // exactly 30 simulated seconds
clock.Advance(TimeSpan.FromMinutes(2));       // ticks in steps of 250 ms or less
await Host.WaitForBackgroundWorkAsync();      // relight, random ticks, spawner, chunks, pathfinding idle
#line default
        }
    }
}


namespace Zaldaryon.Pharos.Docs.Generated.writing_server_scenarios.Snippet_23
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

#line 643 "docs/writing-server-scenarios.md"
[DocExample, Soak]
public async Task ThreeDays_NoLeaks()
{
    await CreateTestPlayerAsync(PlayerNames.Next());
    SoakReport report = await Host!.Simulate.DaysAsync(3, speed: 120, monitor: m => m
        .EntitiesBelow(500)
        .ManagedMemoryGrowthBelow(64_000_000)
        .TickListenersStable()
        .TickMs.P99Below(50));

    report.SaveCsv(SoakReport.ArtifactPath("ThreeDays_NoLeaks.csv"));
}
#line default
    }
}


namespace Zaldaryon.Pharos.Docs.Generated.writing_server_scenarios.Snippet_24
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

        
        public async Task ExecuteSnippetAsync()
        {
#line 690 "docs/writing-server-scenarios.md"
using (Host!.Faults.SlowTicks(every: 4, delayMs: 500))
{
    // Every fourth tick takes 500 ms longer on the virtual clock.
}

using (Host.Faults.ThrowIn<MyModSystem>(nameof(MyModSystem.OnTick), times: 1))
{
    // The next call to OnTick throws InjectedFaultException; the server logs it and runs the next tick.
}

using (Host.Faults.FailSaves())
{
    // Each database write of a save on the game thread throws. The game logs some of these and the save completes; the chunk writes
    // escape the save, which then throws. The game's off-thread saves, which run on the chunk thread after a save, are not failed.
}

using (Host.Faults.DelayChunkLoads(ticks: 100))
{
    // A column requested now stays missing for 100 ticks.
}
#line default
        }
    }
}


namespace Zaldaryon.Pharos.Docs.Generated.writing_server_scenarios.Snippet_25
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

        
        public async Task ExecuteSnippetAsync()
        {
#line 729 "docs/writing-server-scenarios.md"
Host!.Calendar.Freeze();
Host.Calendar.SetTime(hourOfDay: 22);           // the next 22:00
Host.Calendar.SetDate(year: 1, month: 7, day: 4); // keeps the time of day
Host.Calendar.Advance(TimeSpan.FromHours(30));  // at once, as /time add does
EnumSeason season = Host.Calendar.Season;          // at the spawn point
#line default
        }
    }
}


namespace Zaldaryon.Pharos.Docs.Generated.writing_server_scenarios.Snippet_26
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

        
        public async Task ExecuteSnippetAsync()
        {
#line 744 "docs/writing-server-scenarios.md"
Host!.Weather.SetPattern("overcast");               // every loaded region
Host.Weather.SetWind("still", at: player.Entity!.Pos.AsBlockPos);
Host.Weather.SetPrecipitation(1);                  // world-wide, -1 to 1; null hands it back
Host.Weather.StopChanges();                        // no new patterns, winds or events
#line default
        }
    }
}


namespace Zaldaryon.Pharos.Docs.Generated.writing_server_scenarios.Snippet_27
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

        
        public async Task ExecuteSnippetAsync()
        {
#line 761 "docs/writing-server-scenarios.md"
BlockPos pos = new(512000, 4, 512000);
var listeners = Host!.TickListeners.At(pos);
Assert.Contains(listeners, l => l.Mod == "mymod" && l.IntervalMs == 100);

FastForwardResult run = await Host.TickListeners.FastForwardAsync(pos, TimeSpan.FromMinutes(2), step: TimeSpan.FromMilliseconds(100));

Host.RunOnGameThread(() => Api!.World.BlockAccessor.SetBlock(0, pos));
PharosAssert.NoTickListenersAt(Host, pos);
#line default
        }
    }
}


namespace Zaldaryon.Pharos.Docs.Generated.writing_server_scenarios.Snippet_28
{
    
#line 847 "docs/writing-server-scenarios.md"
[Seeded]                                 // a stable hash of each test's name
public class DropTests : ServerScenarioBase
{
    [DocExample, Seeded(1234)]       // a fixed seed
    public void Grass_DropsSeeds_Sometimes() { }
}
#line default
}


namespace Zaldaryon.Pharos.Docs.Generated.writing_server_scenarios.Snippet_29
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

        
        public async Task ExecuteSnippetAsync()
        {
#line 868 "docs/writing-server-scenarios.md"
Host!.Random.Seed(42);                   // re-seeds, undoing the earlier seeding
int? seed = Host.Random.CurrentSeed;     // null while the game's own generators run
Host.Random.Restore();                   // the game's own generators again
IReadOnlyList<SeededRandom> seeded = Host.Random.Seeded;
Assert.Empty(Host.Random.Unseeded.Where(u => u.Assembly == typeof(MyMod).Assembly));
#line default
        }
    }
}


namespace Zaldaryon.Pharos.Docs.Generated.writing_server_scenarios.Snippet_30
{
    
#line 909 "docs/writing-server-scenarios.md"
[ServerWorld(seed: 42)]
[ServerMods("path/to/mymod.zip")]
public class ModIntegrationTests : ServerScenarioBase
{
    [DocExample]
    public void ModRegistersItsBlocks()
    {
        var block = Server.Api.World.GetBlock(new AssetLocation("mymod:myblock"));
        Assert.NotNull(block);
    }
}
#line default
}


namespace Zaldaryon.Pharos.Docs.Generated.writing_server_scenarios.Snippet_31
{
    
#line 927 "docs/writing-server-scenarios.md"
[ServerMods("mods/mymod.zip")]
[DataFiles("Fixtures/mymod.json", To = "ModConfig/mymod.json", Side = EnumAppSide.Server)]
public class MyModConfigTests : ServerScenarioBase
{
    [DocExample]
    public void TheModListensOnItsPort()
    {
        using TcpClient tcp = new();
        tcp.Connect("127.0.0.1", DataFilePort("metrics"));
    }
}
#line default
}


namespace Zaldaryon.Pharos.Docs.Generated.writing_server_scenarios.Snippet_32
{
    
#line 947 "docs/writing-server-scenarios.md"
[ServerWorld(seed: 1)]
public class QuickTests : ServerScenarioBase
{
    [DocExample]
    public void FastTest() { }
}
#line default
}
