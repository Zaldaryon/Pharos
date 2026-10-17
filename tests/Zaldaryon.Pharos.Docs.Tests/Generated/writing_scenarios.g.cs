// Generated from docs/writing-scenarios.md
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



namespace Zaldaryon.Pharos.Docs.Generated.writing_scenarios.Snippet_1
{
    using Zaldaryon.Pharos.XUnit;
using Xunit;
#line 21 "docs/writing-scenarios.md"
public class ChunkLoadingTests : ClientScenarioBase
{
    [DocExample]
    public async Task InjectedChunkIsMeshed()
    {
        // The client is booted before the test runs. By default it is a fixture-mode client:
        // no server, chunks are injected and every frame is deterministic.
        ChunkPos target = new(10, 2, 10);
        ChunkFixture fixture = new ChunkFixtureBuilder()
            .At(target)
            .WithDefaultSunlight(31)
            .Fill(0, 0, 0, 31, 1, 31, 1) // a layer of block id 1
            .Build();

        Client!.InjectChunk(fixture, triggerTesselation: true);

        Assert.True(await Client.WaitForChunkMeshed(target, maxFrames: 10));
    }
}
#line default
}


namespace Zaldaryon.Pharos.Docs.Generated.writing_scenarios.Snippet_2
{
    using Zaldaryon.Pharos.Bootstrap;
#line 48 "docs/writing-scenarios.md"
public class MyTests : ClientScenarioBase
{
    protected override HeadlessClientOptions ClientOptions => new() { BootMode = ClientBootMode.Engine };
}
#line default
}


namespace Zaldaryon.Pharos.Docs.Generated.writing_scenarios.Snippet_3
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
#line 88 "docs/writing-scenarios.md"
// Single frame with fixed timestep
FrameController!.Frame(0.016f);

// Multiple frames
FrameController.Frames(60);

// Advance until a condition is true
FrameController.FramesUntil(() => Client!.WorldMap.LoadedChunks > 10, maxFrames: 300);
#line default
        }
    }
}


namespace Zaldaryon.Pharos.Docs.Generated.writing_scenarios.Snippet_4
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
#line 103 "docs/writing-scenarios.md"
// Teleport to position
Player!.SetPosition(100, 80, 200);

// Point camera at a target
Player.LookAt(100, 64, 300);

// Get current position
var pos = Player.Position;
#line default
        }
    }
}


namespace Zaldaryon.Pharos.Docs.Generated.writing_scenarios.Snippet_5
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

#line 120 "docs/writing-scenarios.md"
[DocExample]
public void MyTest()
{
    // Test code here
}
#line default
    }
}


namespace Zaldaryon.Pharos.Docs.Generated.writing_scenarios.Snippet_6
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

#line 132 "docs/writing-scenarios.md"
[DocExample]
[DocExample]
[DocExample]
[DocExample]
public void ChunksLoadAtPosition(int x, int y, int z)
{
    Player!.SetPosition(x, y, z);
    FrameController!.Frames(60);
    
    Assert.True(Client!.WorldMap.LoadedChunks > 0);
}
#line default
    }
}


namespace Zaldaryon.Pharos.Docs.Generated.writing_scenarios.Snippet_7
{
    using Zaldaryon.Pharos.Bootstrap;
#line 150 "docs/writing-scenarios.md"
[PharosMods("mods/MyMod.zip", "mods/DependencyMod")]
public class ModCompatibilityTests : ClientScenarioBase
{
    protected override HeadlessClientOptions ClientOptions => new() { BootMode = ClientBootMode.Engine };
}
#line default
}


namespace Zaldaryon.Pharos.Docs.Generated.writing_scenarios.Snippet_8
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

#line 171 "docs/writing-scenarios.md"
[DocExample]
[RequireGameVersion(">=1.22.0", Reason = "Uses the 1.22 calendar API.")]
public void SeasonsFollowTheNewCalendar() { }
#line default
    }
}


namespace Zaldaryon.Pharos.Docs.Generated.writing_scenarios.Snippet_9
{
    
#line 196 "docs/writing-scenarios.md"
public class FastTests : ClientScenarioBase
{
    // Uses SharedClient by default
}
#line default
}


namespace Zaldaryon.Pharos.Docs.Generated.writing_scenarios.Snippet_10
{
    
#line 207 "docs/writing-scenarios.md"
public class IsolatedStateTests : ClientScenarioBase
{
    protected override IsolationMode IsolationMode => IsolationMode.RollbackState;
    
    [DocExample]
    public void FirstTest()
    {
        Player!.SetPosition(100, 100, 100);
        // State rolls back after this test
    }
    
    [DocExample]
    public void SecondTest()
    {
        // Player is back at the baseline position
    }
}
#line default
}


namespace Zaldaryon.Pharos.Docs.Generated.writing_scenarios.Snippet_11
{
    
#line 231 "docs/writing-scenarios.md"
public class FullyIsolatedTests : ClientScenarioBase
{
    protected override IsolationMode IsolationMode => IsolationMode.FreshClient;
    
    [DocExample]
    public void TestA()
    {
        // Fresh client, no state from other tests
    }
    
    [DocExample]
    public void TestB()
    {
        // Another fresh client
    }
}
#line default
}


namespace Zaldaryon.Pharos.Docs.Generated.writing_scenarios.Snippet_12
{
    using Zaldaryon.Pharos.Graphics;
using Zaldaryon.Pharos.Culling;
using Zaldaryon.Pharos.Memory;
#line 254 "docs/writing-scenarios.md"
public class InspectionTests : ClientScenarioBase
{
    [DocExample]
    public void MeasureDrawCalls()
    {
        var proxy = new GlCommandProxy();
        proxy.Enable();
        
        FrameController!.Frames(60);
        proxy.Reset();
        FrameController.Frames(10);
        
        var record = proxy.Snapshot();
        proxy.Disable();
        
        Assert.True(record.TotalDrawCalls > 0);
    }
    
    [DocExample]
    public void CheckCulling()
    {
        var inspector = new CullingInspector(Client!.ClientMain);
        
        FrameController!.Frames(30);
        var snapshot = inspector.Snapshot();
        
        Assert.True(snapshot.VisibleChunks.Count > 0);
    }
}
#line default
}


namespace Zaldaryon.Pharos.Docs.Generated.writing_scenarios.Snippet_13
{
    
#line 294 "docs/writing-scenarios.md"
public class VisualTests : ClientScenarioBase
{
    [DocExample]
    public void SceneMatchesBaseline()
    {
        // Set up scene
        Player!.SetPosition(0, 100, 0);
        Player.LookAt(50, 64, 50);
        FrameController!.Frames(90);
        
        // Capture and compare
        var snapshot = Client!.Framebuffer.Capture();
        var baseline = FramebufferSnapshot.FromFile("baselines/scene.png");
        var result = snapshot.Compare(baseline, tolerance: 0.02f);
        
        // Save actual on failure for debugging
        if (result.DiffPixelCount > 50)
        {
            snapshot.SaveToPng("artifacts/scene-actual.png");
        }
        
        Assert.True(result.DiffPixelCount < 50,
            $"Visual diff: {result.DiffPixelCount} pixels exceed tolerance");
    }
}
#line default
}


namespace Zaldaryon.Pharos.Docs.Generated.writing_scenarios.Snippet_14
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
#line 336 "docs/writing-scenarios.md"
FrameController!.FramesUntil(
    () => Client!.WorldMap.LoadedChunks >= 50,
    maxFrames: 600);
#line default
        }
    }
}


namespace Zaldaryon.Pharos.Docs.Generated.writing_scenarios.Snippet_15
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
#line 346 "docs/writing-scenarios.md"
var proxy = new GlCommandProxy();
proxy.Enable();
try
{
    // Test code
}
finally
{
    proxy.Disable();
}
#line default
        }
    }
}


namespace Zaldaryon.Pharos.Docs.Generated.writing_scenarios.Snippet_16
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

#line 363 "docs/writing-scenarios.md"
// Good
public void ChunksOutsideFrustum_AreCulled() { }

// Bad
public void Test1() { }
#line default
    }
}
