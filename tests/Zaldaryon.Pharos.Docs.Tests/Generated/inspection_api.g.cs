// Generated from docs/inspection-api.md
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



namespace Zaldaryon.Pharos.Docs.Generated.inspection_api.Snippet_1
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
#line 16 "docs/inspection-api.md"
RendererInfo mine = Assert.Single(Client.Renderers.Of("mymod"));
Assert.Equal(EnumRenderStage.Opaque, mine.Stage);
Assert.Equal(30, await Client.Renderers.CountCallsAsync(mine, frames: 30));
#line default
        }
    }
}


namespace Zaldaryon.Pharos.Docs.Generated.inspection_api.Snippet_2
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
#line 39 "docs/inspection-api.md"
TickListenerInfo listener = Assert.Single(Client.TickListeners.Of("mymod"));
Assert.Equal(50, listener.IntervalMs);
int calls = await Client.TickListeners.CountCallsAsync(listener, frames: 120);
#line default
        }
    }
}


namespace Zaldaryon.Pharos.Docs.Generated.inspection_api.Snippet_3
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
#line 59 "docs/inspection-api.md"
using (ParticleCapture capture = Client.Particles.Capture())
{
    await Client.Commands.ExecuteSuccessAsync(".mymod sparks");
    await Session.StepFramesAsync(2);
    SpawnedParticles sparks = Assert.Single(capture.Spawned, p => p.Color == SparkColor);
    Assert.Equal(EnumParticleModel.Quad, sparks.Model);
}
Assert.True(Client.Particles.Alive(EnumParticleModel.Quad) > 0);
#line default
        }
    }
}


namespace Zaldaryon.Pharos.Docs.Generated.inspection_api.Snippet_4
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
#line 84 "docs/inspection-api.md"
ClientHighlight marked = Client.Highlights(slot: 9)!;
Assert.Equal(expectedBlocks, marked.Positions);
#line default
        }
    }
}


namespace Zaldaryon.Pharos.Docs.Generated.inspection_api.Snippet_5
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
#line 102 "docs/inspection-api.md"
using ServerEventLog log = ServerHost!.Events.Record(ServerEvents.Blocks | ServerEvents.Entities);
await Session!.Blocks.PlaceAsync(ground, BlockFacing.UP, "mymod:lamp");
BlockPlaced placed = Assert.Single(log.BlockPlaced);
Assert.Equal("mymod:lamp", placed.Stack?.Collectible.Code.ToString());
EntityDied died = await log.WaitForAsync<EntityDied>(e => e.Code?.Path == "wolf-male");
PharosAssert.EventOrder(log, typeof(BlockPlaced), EventMatch.Of<EntityDied>(e => e.EntityId == wolfId));
#line default
        }
    }
}


namespace Zaldaryon.Pharos.Docs.Generated.inspection_api.Snippet_6
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
#line 210 "docs/inspection-api.md"
ItemStack sword = client.Items.Stack("mymod:sword-iron");
Assert.Contains("Durability: 500 / 500", client.Items.Tooltip(sword).Lines);
GoldenImageAssertion.Assert(client.Items.RenderIcon(sword, 64), "Goldens/sword-iron.png");
#line default
        }
    }
}


namespace Zaldaryon.Pharos.Docs.Generated.inspection_api.Snippet_7
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
#line 243 "docs/inspection-api.md"
await Session.StepFramesAsync(30);                       // warm up: the first frames compile code
FrameMeasurement before = await Session.MeasureFramesAsync(300);
await Client.Commands.ExecuteSuccessAsync(".mymod effects on");
FrameMeasurement with = await Session.MeasureFramesAsync(300);
Assert.True(with.Median - before.Median < TimeSpan.FromMilliseconds(2), with.ToString());
#line default
        }
    }
}


namespace Zaldaryon.Pharos.Docs.Generated.inspection_api.Snippet_8
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
#line 280 "docs/inspection-api.md"
FrameMeasurement measured = await Session.MeasureFramesAsync(300);
FrameBaseline.Assert(measured, "pharos-baselines.json", "mymod.village.alloc", FrameStat.AllocatedBytesPerFrame, tolerance: 0.10);
FrameBaseline.Assert(measured, "pharos-baselines.json", "mymod.village.median", FrameStat.Median, tolerance: 0.50);
#line default
        }
    }
}


namespace Zaldaryon.Pharos.Docs.Generated.inspection_api.Snippet_9
{
    
#line 333 "docs/inspection-api.md"
public class DrawCallTests : ClientScenarioBase
{
    [DocExample]
    public void SteadyStateHasNoBatching()
    {
        var proxy = new GlCommandProxy();
        proxy.Enable();
        
        // Let the client stabilize
        FrameController!.Frames(60);
        
        proxy.Reset();
        FrameController.Frames(10);
        var record = proxy.Snapshot();
        
        proxy.Disable();
        
        Assert.True(record.IndirectDrawCalls > 0, "Expected indirect draw calls");
        Assert.Equal(0, record.BufferAllocations);
    }
}
#line default
}


namespace Zaldaryon.Pharos.Docs.Generated.inspection_api.Snippet_10
{
    
#line 385 "docs/inspection-api.md"
public class CullingTests : ClientScenarioBase
{
    [DocExample]
    public void ChunksOutsideFrustumAreCulled()
    {
        var inspector = new CullingInspector(Client!.ClientMain);
        
        // Position camera looking at specific area
        Player!.SetPosition(0, 100, 0);
        Player.LookAt(32, 64, 32);
        FrameController!.Frames(30);
        
        var snapshot = inspector.Snapshot();
        
        Assert.True(snapshot.CulledChunks.Count > 0, "Expected some chunks to be culled");
        Assert.True(snapshot.VisibleChunks.Count > 0, "Expected some chunks to be visible");
    }
}
#line default
}


namespace Zaldaryon.Pharos.Docs.Generated.inspection_api.Snippet_11
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
#line 429 "docs/inspection-api.md"
using GlResourceScope scope = Client!.Memory.TrackGlResources(new() { CaptureStacks = true });
for (int i = 0; i < 10; i++)
{
    await Client.Hotkeys.TriggerAsync("mymod:openpanel");
    await Session!.StepFramesAsync(5);
    await Client.Hotkeys.TriggerAsync("mymod:openpanel");
}
PharosAssert.NoGlLeaks(scope, GlResourceKind.VertexArray | GlResourceKind.Texture);
#line default
        }
    }
}


namespace Zaldaryon.Pharos.Docs.Generated.inspection_api.Snippet_12
{
    
#line 470 "docs/inspection-api.md"
public class AllocationTests : ClientScenarioBase
{
    [DocExample]
    public void SteadyStateHasNoAllocations()
    {
        var inspector = new MemoryInspector();
        inspector.Enable();
        
        // Warm up
        FrameController!.Frames(120);
        
        inspector.Reset();
        long bytes = MemoryInspector.MeasureAllocations(() =>
        {
            FrameController.Frames(60);
        });
        
        var allocs = inspector.AllocationSnapshot();
        inspector.Disable();
        
        Assert.Equal(0, allocs.MeshDataAllocations);
        Assert.True(bytes < 1024, $"Allocated {bytes} bytes during steady state");
    }
}
#line default
}


namespace Zaldaryon.Pharos.Docs.Generated.inspection_api.Snippet_13
{
    
#line 524 "docs/inspection-api.md"
public class ShaderTests : ClientScenarioBase
{
    [DocExample]
    public void NoRedundantUniformUploads()
    {
        var inspector = new ShaderInspector();
        inspector.Enable();
        
        FrameController!.Frames(60);
        
        int redundant = inspector.GetRedundantUploadCount();
        inspector.Disable();
        
        Assert.True(redundant < 100, $"Found {redundant} redundant uniform uploads");
    }
}
#line default
}


namespace Zaldaryon.Pharos.Docs.Generated.inspection_api.Snippet_14
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
#line 577 "docs/inspection-api.md"
var snapshot = Client!.Framebuffer.Capture();
#line default
        }
    }
}


namespace Zaldaryon.Pharos.Docs.Generated.inspection_api.Snippet_15
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
#line 583 "docs/inspection-api.md"
var baseline = FramebufferSnapshot.FromFile("baseline.png");
#line default
        }
    }
}


namespace Zaldaryon.Pharos.Docs.Generated.inspection_api.Snippet_16
{
    
#line 615 "docs/inspection-api.md"
public class VisualRegressionTests : ClientScenarioBase
{
    [DocExample]
    public void RenderedFrameMatchesBaseline()
    {
        Player!.SetPosition(0, 100, 0);
        Player.LookAt(0, 64, 100);
        FrameController!.Frames(60);
        
        var snapshot = Client!.Framebuffer.Capture();
        var baseline = FramebufferSnapshot.FromFile("baselines/view-north.png");
        
        var result = snapshot.Compare(baseline, tolerance: 0.02f);
        
        if (result.DiffPixelCount > 0)
        {
            snapshot.SaveToPng("artifacts/view-north-actual.png");
        }
        
        Assert.True(result.DiffPixelCount < 100, 
            $"Visual regression: {result.DiffPixelCount} pixels differ");
    }
}
#line default
}
