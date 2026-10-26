// Generated from docs/writing-client-server-scenarios.md
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



namespace Zaldaryon.Pharos.Docs.Generated.writing_client_server_scenarios.Snippet_1
{
using Zaldaryon.Pharos.XUnit;
using Xunit;
#line 17 "docs/writing-client-server-scenarios.md"
public class BlockSyncTests : ClientServerScenarioBase
{
    [DocExample]
    public async Task PlacingBlock_SyncsToClient()
    {
        var pos = new BlockPos(10, 64, 10);

        // Place a block on the server on its game thread
        Server.RunOnGameThread(() => Server.Api.World.BlockAccessor.SetBlock(1, pos));

        // Step until client chunk reflects the change
        await Session.StepUntilAsync(() => ClientServerAssert.BlockSynced(Session, pos));

        // Assert final state
        ClientServerAssert.ClientServerBlockSynced(Session, pos);
    }
}
#line default
}


namespace Zaldaryon.Pharos.Docs.Generated.writing_client_server_scenarios.Snippet_2
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
#line 63 "docs/writing-client-server-scenarios.md"
// Single lockstep frame
Session.Step();

// Multiple frames
Session.StepFrames(60);

// Advance until condition holds
bool settled = await Session.StepUntilAsync(
    () => Client.Culling.Snapshot().TotalChunks > 0,
    maxFrames: 600
);
Assert.True(settled);
#line default
        }
    }
}


namespace Zaldaryon.Pharos.Docs.Generated.writing_client_server_scenarios.Snippet_3
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
#line 82 "docs/writing-client-server-scenarios.md"
// Block at pos is the same on client and server
ClientServerAssert.ClientServerBlockSynced(Session, pos);

// Client player position matches server entity
ClientServerAssert.PlayerPositionSynced(Session, maxDistance: 0.1);

// Client inventory slot matches server store
ClientServerAssert.InventorySynced(Session, "hotbar");

// The client's copy of the player's survival state matches the server's
ClientServerAssert.VitalsSynced(Session, PlayerName);

// A tracked entity and one of its watched attributes match the server
ClientServerAssert.EntitySynced(Session, entity.EntityId, tolerance: 0.1);
ClientServerAssert.WatchedAttributeSynced(Session, entity.EntityId, "health");
#line default
        }
    }
}


namespace Zaldaryon.Pharos.Docs.Generated.writing_client_server_scenarios.Snippet_4
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

#line 108 "docs/writing-client-server-scenarios.md"
[DocExample]
public async Task Wetting_ThePlayer_ReachesTheClient()
{
    ServerPlayer.Vitals.Set(wetness: 0.6f);

    await StepUntilAsync(() => WetnessReachedTheClient(), maxFrames: 600);
    Assert.Equal(0.6f, Player!.Vitals.Wetness!.Value, 2);

    bool WetnessReachedTheClient()
    {
        try
        {
            ClientServerAssert.VitalsSynced(Session!, PlayerName, values: VitalsValues.Wetness);
            return true;
        }
        catch (PharosAssertException ex)
        {
            _lastFailure = ex.Message;
            return false;
        }
    }
}
#line default
    }
}


namespace Zaldaryon.Pharos.Docs.Generated.writing_client_server_scenarios.Snippet_5
{

#line 141 "docs/writing-client-server-scenarios.md"
[ServerWorld(seed: 4242, playStyle: "surviveandbuild")]   // a creative player takes no damage
public class DeathScreenTests : ClientServerScenarioBase
{
    [DocExample]
    public async Task TheDeathScreen_OpensAndRespawnClosesIt()
    {
        await ServerPlayer.DamageAsync(100, DamageInfo.Blunt);

        Assert.True(await StepUntilAsync(() => Player.Death.IsDialogOpen, maxFrames: 600));

        Assert.True(Player.Death.Respawn());     // a real click on the dialog's own button
        Assert.True(await StepUntilAsync(() => ServerPlayer.Entity!.Alive, maxFrames: 600));
        Assert.True(await StepUntilAsync(() => !Player.Death.IsDialogOpen, maxFrames: 600));
    }
}
#line default
}


namespace Zaldaryon.Pharos.Docs.Generated.writing_client_server_scenarios.Snippet_6
{

#line 167 "docs/writing-client-server-scenarios.md"
public class PacketTracingTests : ClientServerScenarioBase
{
    [DocExample]
    public async Task ChatMessage_SendsCorrectPacket()
    {
        var tracer = new PacketTracer();
        tracer.StartTracing();

        await ExecuteCommandAsPlayerAsync(ServerPlayer, "/say hello");
        Session.StepFrames(10);

        tracer.StopTracing();

        // Assert packet 48 (chat) was sent client-to-server
        tracer.AssertClientSentPacket(48);
    }
}
#line default
}


namespace Zaldaryon.Pharos.Docs.Generated.writing_client_server_scenarios.Snippet_7
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

#line 191 "docs/writing-client-server-scenarios.md"
[DocExample]
public async Task ClientReconnects_AfterPacketLoss()
{
    var profile = new DegradedNetworkProfile
    {
        LatencyMs = 100,
        PacketDropRate = 0.3f,
        JitterMs = 20
    };

    Client.NetworkDegradation.Configure(profile);
    Session.StepFrames(120);
    Client.NetworkDegradation.Reset();

    ClientServerAssert.PlayerPositionSynced(Session);
}
#line default
    }
}


namespace Zaldaryon.Pharos.Docs.Generated.writing_client_server_scenarios.Snippet_8
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
#line 214 "docs/writing-client-server-scenarios.md"
Client.NetworkDegradation.Run(NetworkScript.Begin(DegradedNetworkProfile.None)
    .Then(1000, new DegradedNetworkProfile(Outage: true))
    .Then(3000, DegradedNetworkProfile.None));
#line default
        }
    }
}


namespace Zaldaryon.Pharos.Docs.Generated.writing_client_server_scenarios.Snippet_9
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
#line 223 "docs/writing-client-server-scenarios.md"
using (Client.NetworkDegradation.Filter(p => p.Channel == "mymod" && p.MessageId == 2))
{
    await Session.HoldAsync(PlayerAction.Forward, 60);
}
#line default
        }
    }
}


namespace Zaldaryon.Pharos.Docs.Generated.writing_client_server_scenarios.Snippet_10
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

        [ProtoContract] public class SyncPacket { [ProtoMember(1)] public int Version { get; set; } }
        [ProtoContract] public class SyncAck { [ProtoMember(1)] public int Version { get; set; } }


        public async Task ExecuteSnippetAsync()
        {
#line 240 "docs/writing-client-server-scenarios.md"
// A look-alike of the mod's message: same [ProtoMember] numbers, matched by type name.

            ModMessage delivered = await Client!.ModNetwork.DeliverAsync("mymod", new SyncPacket { Version = 1 });
            SyncAck ack = await Client.ModNetwork.WaitForSentAsync<SyncAck>("mymod", since: delivered.Sequence);
            Assert.Equal(1, ack.Version);
#line default
        }
    }
}


namespace Zaldaryon.Pharos.Docs.Generated.writing_client_server_scenarios.Snippet_11
{

#line 308 "docs/writing-client-server-scenarios.md"
public class ExpensiveSetupTests : ClientServerScenarioBase
{
    // Every test boots its own pair.
    protected override WorldIsolation WorldIsolation => WorldIsolation.Restart;
}
#line default
}


namespace Zaldaryon.Pharos.Docs.Generated.writing_client_server_scenarios.Snippet_12
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
#line 318 "docs/writing-client-server-scenarios.md"
ServerHost!.Calendar.Freeze();               // first, or time runs on between the calls
ServerHost.Calendar.SetTime(hourOfDay: 22);
await Session!.WaitForCalendarSyncAsync();
Assert.Equal(22f, Client!.RunOnClientThread(() => Client.Client.Calendar.HourOfDay));
#line default
        }
    }
}


namespace Zaldaryon.Pharos.Docs.Generated.writing_client_server_scenarios.Snippet_13
{

#line 329 "docs/writing-client-server-scenarios.md"
[ServerWorld(Isolation = Isolation.Rollback, StrictIsolation = true)]
public class MyTests : ClientServerScenarioBase
{
    protected override void OnRollbackRestored() => MyModSystem.Instance.ReloadFromWorld();
}
#line default
}
