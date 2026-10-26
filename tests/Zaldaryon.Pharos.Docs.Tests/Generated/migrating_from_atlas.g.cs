// Generated from docs/migrating-from-atlas.md
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



namespace Zaldaryon.Pharos.Docs.Generated.migrating_from_atlas.Snippet_1
{
using Atlas.Api;
using Atlas.XUnit;
using WorldOptions = global::Atlas.Api.WorldOptions;
using global::Atlas.Api;
using global::Atlas.XUnit;
#line 71 "docs/migrating-from-atlas.md"
[AtlasWorld(seed: 12345, playStyle: "creativebuilding")]
public class InventoryTests : AtlasScenarioBase
{
    private readonly IWorldSession _session;

    public InventoryTests()
    {
        _session = WorldSession.Create(new WorldOptions { Seed = "12345" });
    }

    [DocExample]
    public async Task Should_GiveItem_ToPlayer()
    {
        var player = await _session.CreateTestPlayer();
        player.GiveItem("game:sword-iron");
        Assert.True(player.HasItem("game:sword-iron"));
    }

    [DocExample]
    [InlineData("game:sword-iron")]
    [InlineData("game:axe-copper")]
    public async Task Should_GiveItem_MultipleTypes(string itemCode)
    {
        var player = await _session.CreateTestPlayer();
        player.GiveItem(itemCode);
        Assert.True(player.HasItem(itemCode));
    }
}
#line default
}


namespace Zaldaryon.Pharos.Docs.Generated.migrating_from_atlas.Snippet_2
{
using Zaldaryon.Pharos.Server;
using Zaldaryon.Pharos.XUnit;
using WorldOptions = global::Atlas.Api.WorldOptions;
using global::Atlas.Api;
using global::Atlas.XUnit;
#line 107 "docs/migrating-from-atlas.md"
[ServerWorld(seed: 12345, playStyle: "creativebuilding")]
public class InventoryTests : ServerScenarioBase
{
    private readonly EmbeddedServerHost _host;

    public InventoryTests()
    {
        _host = EmbeddedServerHost.Boot(new ServerWorldOptions { Seed = "12345" });
    }

    [DocExample]
    public async Task Should_GiveItem_ToPlayer()
    {
        var player = await _host.CreateTestPlayer();
        player.GiveItem("game:sword-iron");
        Assert.True(player.HasItem("game:sword-iron"));
    }

    [DocExample]
    [InlineData("game:sword-iron")]
    [InlineData("game:axe-copper")]
    public async Task Should_GiveItem_MultipleTypes(string itemCode)
    {
        var player = await _host.CreateTestPlayer();
        player.GiveItem(itemCode);
        Assert.True(player.HasItem(itemCode));
    }
}
#line default
}


namespace Zaldaryon.Pharos.Docs.Generated.migrating_from_atlas.Snippet_3_Outer
{

using WorldOptions = global::Atlas.Api.WorldOptions;
using global::Atlas.Api;
using global::Atlas.XUnit;
#line 213 "docs/migrating-from-atlas.md"
namespace Atlas.Api
{
    // Wraps EmbeddedServerHost
    public interface IWorldSession { }
    public class WorldSession : IWorldSession { }

    // Alias for ServerWorldOptions
    public record WorldOptions { }

    // Wraps IServerTestPlayer  
    public interface ITestPlayer { }
    public class TestPlayerAdapter : ITestPlayer { }

    // Wraps Pharos CommandResult
    public class CommandResult { }
}
#line default
}


namespace Zaldaryon.Pharos.Docs.Generated.migrating_from_atlas.Snippet_4_Outer
{

using WorldOptions = global::Atlas.Api.WorldOptions;
using global::Atlas.Api;
using global::Atlas.XUnit;
#line 234 "docs/migrating-from-atlas.md"
namespace Atlas.XUnit
{
    // Equivalent to ServerScenarioAttribute
    [AttributeUsage(AttributeTargets.Method)]
    public class AtlasScenarioAttribute : FactAttribute { }

    // Equivalent to ServerTheoryAttribute
    [AttributeUsage(AttributeTargets.Method)]
    public class AtlasTheoryAttribute : TheoryAttribute { }

    // Equivalent to ServerScenarioBase
    public abstract class AtlasScenarioBase : ServerScenarioBase { }

    // Equivalent to ServerWorldAttribute
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
    public class AtlasWorldAttribute : Attribute { }

    // Equivalent to ServerModsAttribute
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Assembly)]
    public class AtlasModsAttribute : Attribute { }
}
#line default
}


namespace Zaldaryon.Pharos.Docs.Generated.migrating_from_atlas.Snippet_5
{
using Atlas.Api;
using Zaldaryon.Pharos.Server;
using Zaldaryon.Pharos.XUnit;
using WorldOptions = global::Atlas.Api.WorldOptions;
using global::Atlas.Api;
using global::Atlas.XUnit;
    public class ScenarioContainer
    {


        public async Task ExecuteSnippetAsync()
        {
#line 262 "docs/migrating-from-atlas.md"
// Atlas to Pharos
ITestPlayer atlasPlayer = null!;
IWorldSession worldSession = null!;
WorldOptions worldOptions = null!;
IServerTestPlayer pharosPlayer = atlasPlayer.AsPharosPlayer();
EmbeddedServerHost host = worldSession.Host;
ServerWorldOptions options = worldOptions.ToServerWorldOptions();

// Pharos to Atlas
ServerWorldOptions serverOptions = null!;
ITestPlayer atlasPlayerFromPharos = pharosPlayer.AsAtlasPlayer();
WorldOptions convertedOptions = WorldOptions.FromServerWorldOptions(serverOptions);
#line default
        }
    }
}
