// Generated from docs/engine-mode.md
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



namespace Zaldaryon.Pharos.Docs.Generated.engine_mode.Snippet_1
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
#line 7 "docs/engine-mode.md"
using var server = EmbeddedServerHost.Boot(new ServerWorldOptions { WorldType = "superflat" });
using var client = HeadlessClientBootstrap.Boot(new HeadlessClientOptions
{
    BootMode = ClientBootMode.Engine,
});
using var session = client.ConnectLoopback(server, "Pilot");

Assert.True(session.WaitForPlayerJoined(TimeSpan.FromSeconds(120)));
await session.WaitForChunkMeshedAsync(ChunkPos.FromBlockPos(client.Client.EntityPlayer.Pos.AsBlockPos), maxFrames: 3000);

FramebufferSnapshot frame = client.CaptureFrame();
#line default
        }
    }
}


namespace Zaldaryon.Pharos.Docs.Generated.engine_mode.Snippet_2
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
#line 66 "docs/engine-mode.md"
client.Input.PressKey(GlKeys.C);                    // hotkey: opens the character dialog
client.Input.InjectKey(VirtualKey.W, pressed: true); // hold W: the player walks, the server sees it
client.Input.InjectMouseDelta(40, 0);                // mouse look: turns the camera right
client.Input.InjectScroll(-1);                       // next hotbar slot
client.Input.PressKey(GlKeys.T);                     // open chat
client.Input.TypeText("hello");                      // text input into the focused field
client.Input.PressKey(GlKeys.Enter);
client.Input.Click(320, 180);                        // move the cursor and click
#line default
        }
    }
}


namespace Zaldaryon.Pharos.Docs.Generated.engine_mode.Snippet_3
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
#line 83 "docs/engine-mode.md"
await session.HoldAsync(PlayerAction.Forward, frames: 60); // walk
client.Controls.Press(PlayerAction.Jump);                   // hold until released
client.Controls.Release(PlayerAction.Jump);
#line default
        }
    }
}


namespace Zaldaryon.Pharos.Docs.Generated.engine_mode.Snippet_4
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
#line 91 "docs/engine-mode.md"
bool broken = await session.Blocks.BreakAsync(pos);
bool placed = await session.Blocks.PlaceAsync(against: ground, BlockFacing.UP, "game:rock-granite");
bool clicked = await session.Blocks.UseAsync(chestPos);  // opens the chest dialog
await session.Blocks.AimAtAsync(pos, BlockFacing.NORTH); // just aim
#line default
        }
    }
}


namespace Zaldaryon.Pharos.Docs.Generated.engine_mode.Snippet_5
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
#line 108 "docs/engine-mode.md"
client.Input.PressKey(GlKeys.Escape);
var back = client.Ui.Elements("GuiDialogEscapeMenu")
    .Single(e => e.Type == "GuiElementTextButton" && e.Text == "Back to Game");
client.Ui.Click("GuiDialogEscapeMenu", back.Key);

client.Ui.TypeInto("GuiDialogInventory", "searchbox", "granite");
client.Ui.ClickSlot("HudHotbar", "hotbargrid", slotIndex: 2);
bool open = client.Ui.IsOpen("GuiDialogCharacter");
#line default
        }
    }
}


namespace Zaldaryon.Pharos.Docs.Generated.engine_mode.Snippet_6
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
#line 123 "docs/engine-mode.md"
client.Inventory.SelectSlot(4);
client.Inventory.DragSlot(0, 3);
await session.StepUntilAsync(() => /* server sees the stack in slot 3 */ true);
#line default
        }
    }
}


namespace Zaldaryon.Pharos.Docs.Generated.engine_mode.Snippet_7
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
#line 136 "docs/engine-mode.md"
HandbookDriver handbook = client.Ui.Handbook;
await handbook.OpenPageAsync("mymod-guide");               // as a click on handbook://mymod-guide
Assert.Contains("Smelting", handbook.PageText);
var found = await handbook.SearchAsync("lighthouse");       // types into the search field
var mine = await handbook.PagesAsync("mymod");              // HandbookPageInfo(Code, Title, Category, Kind, Domain)

WorldMapDriver map = client.Ui.WorldMap;
await map.SetSizeAsync(900, 600);                           // .map worldmapsize 900 600
await map.OpenAsync();
await map.SetLayerGroupActiveAsync("waypoints", false);     // clicks the layer group's tab
WaypointInfo camp = await map.AddWaypointAsync(pos, "Camp", icon: "home", color: map.WaypointColors[3]);
var onServer = serverHost.RunOnGameThread(() => ServerWaypoints.Of(sapi, playerUid));
await map.RemoveWaypointAsync("Camp");

CharacterDriver character = client.Ui.Character;
await character.OpenAsync();
await character.SwitchTabAsync("Traits");
string walk = character.Stat("walkspeed");                  // "100%"
await character.EquipAsync(hotbarSlot: 2, EnumCharacterDressType.ArmorBody);
#line default
        }
    }
}


namespace Zaldaryon.Pharos.Docs.Generated.engine_mode.Snippet_8
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
#line 254 "docs/engine-mode.md"
client.Sounds.Clear();
await session.HoldAsync(PlayerAction.Forward, 120);
Assert.True(client.Sounds.WasPlayed("walk"));

RecordedSound planks = client.Sounds.Played("block/planks")[0];
Assert.NotNull(planks.Position);
#line default
        }
    }
}


namespace Zaldaryon.Pharos.Docs.Generated.engine_mode.Snippet_9
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
#line 269 "docs/engine-mode.md"
client.PacketRecorder.Start();
await session.HoldAsync(PlayerAction.Forward, 30);
client.PacketRecorder.Stop();
client.PacketRecorder.SaveToJson("walk.json");   // both directions, with packet ids

client.NetworkDegradation.Configure(new DegradedNetworkProfile(LatencyMs: 200, JitterMs: 50, PacketDropRate: 0.05f));
#line default
        }
    }
}


namespace Zaldaryon.Pharos.Docs.Generated.engine_mode.Snippet_10
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
#line 294 "docs/engine-mode.md"
Session.DisconnectSimulator.SimulateKick("bye");
await Session.ReconnectAsync();
Assert.True(Client!.IsJoined);
#line default
        }
    }
}


namespace Zaldaryon.Pharos.Docs.Generated.engine_mode.Snippet_11
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
#line 319 "docs/engine-mode.md"
BridgeChannel.Active!.DrainAll();
client.Input.PressKey(GlKeys.Escape);
await session.StepFramesAsync(3);
Assert.Contains(BridgeChannel.Active.DrainAll(), e => e.ScreenName == "GuiDialogEscapeMenu" && e.IsOpen);
#line default
        }
    }
}


namespace Zaldaryon.Pharos.Docs.Generated.engine_mode.Snippet_12
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
#line 332 "docs/engine-mode.md"
Assert.Empty(client.Logs.Errors);
Assert.Empty(serverHost.Logs.UnexpectedErrors(["known vanilla noise"]));
#line default
        }
    }
}


namespace Zaldaryon.Pharos.Docs.Generated.engine_mode.Snippet_13
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

#line 339 "docs/engine-mode.md"
protected override bool FailOnLoggedErrors => true;
protected override IEnumerable<string> AllowedLoggedErrors => ["Failed to load optional texture"];
#line default
    }
}


namespace Zaldaryon.Pharos.Docs.Generated.engine_mode.Snippet_14
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
#line 350 "docs/engine-mode.md"
using (client.Settings.Apply(ClientSettingsProfile.Of("far", ("viewDistance", 256))))
{
    await session.StepFramesAsync(30);
}
#line default
        }
    }
}


namespace Zaldaryon.Pharos.Docs.Generated.engine_mode.Snippet_15
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

#line 359 "docs/engine-mode.md"
[DocExample, DocExample]
public async Task WorldRenders(ClientSettingsProfile profile)
{
    using IDisposable _ = Client!.Settings.Apply(profile);
    await Session!.StepFramesAsync(10);
}
#line default
    }
}


namespace Zaldaryon.Pharos.Docs.Generated.engine_mode.Snippet_16
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
#line 374 "docs/engine-mode.md"
HotkeyInfo open = Client!.Hotkeys.Require("mymod:openpanel");
Assert.Equal(HotkeyBinding.Of(GlKeys.P, ctrl: true), open.Current);

HotkeyTriggerResult result = await Client.Hotkeys.TriggerAsync("mymod:openpanel");
Assert.True(result.Fired);
Assert.Null(Client.Hotkeys.ConflictWith("mymod:openpanel"));
#line default
        }
    }
}


namespace Zaldaryon.Pharos.Docs.Generated.engine_mode.Snippet_17
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
#line 398 "docs/engine-mode.md"
ClientCommandResult result = await Client!.Commands.ExecuteAsync(".mymod status");
Assert.Equal(EnumCommandStatus.Success, result.Status);
Assert.Equal("ready", result.Message);

await Client.Commands.ExecuteSuccessAsync(".clientconfig viewDistance 64");
Assert.True(Client.Commands.Exists("mymod"));
#line default
        }
    }
}


namespace Zaldaryon.Pharos.Docs.Generated.engine_mode.Snippet_18
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
#line 428 "docs/engine-mode.md"
await client.Window.ResizeAsync(1280, 720);   // runs the frame after the resize
Assert.Equal(1280, client.CaptureFrame().Width);
#line default
        }
    }
}


namespace Zaldaryon.Pharos.Docs.Generated.engine_mode.Snippet_19
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

#line 443 "docs/engine-mode.md"
[DocExample]
[WindowSizes("1280x720", "1920x1080", GuiScales = [1f, 1.5f])]
public async Task PanelFitsOnScreen(WindowLayout layout)
{
    await using IAsyncDisposable _ = await Client!.Window.UseAsync(layout);
    await Client.Hotkeys.TriggerAsync("mymod:openpanel");
    await Session!.StepFramesAsync(2);

    DialogLayout panel = Client.Ui!.Layout("GuiDialogMyPanel");
    PharosAssert.DialogOnScreen(panel);
    PharosAssert.NoOverlappingElements(panel);
}
#line default
    }
}


namespace Zaldaryon.Pharos.Docs.Generated.engine_mode.Snippet_20
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
#line 485 "docs/engine-mode.md"
await client.ReloadAsync(AssetCategory.shaders);
Assert.Equal("ok", (await client.Commands.ExecuteSuccessAsync(".mymod shaderstate")).Message);
#line default
        }
    }
}


namespace Zaldaryon.Pharos.Docs.Generated.engine_mode.Snippet_21
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
#line 505 "docs/engine-mode.md"
using (client.Lang.Use("pt-br"))
{
    PharosAssert.AllTranslated(client, domain: "mymod");
    // ... open the mod's dialogs, look at its items ...
}

Assert.Empty(client.Lang.MissingKeysOf("mymod"));
#line default
        }
    }
}


namespace Zaldaryon.Pharos.Docs.Generated.engine_mode.Snippet_22
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
#line 538 "docs/engine-mode.md"
var server = EmbeddedServerHost.Boot(new ServerWorldOptions { ListenPort = 0 });   // 0 picks a free port
var session = client.ConnectTcp(server, ClientAuth.Offline("Alice"));             // ticks the server in lockstep
#line default
        }
    }
}
