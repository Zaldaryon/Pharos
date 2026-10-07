# Writing Server Scenarios

This guide explains how to write server-side scenario tests using Pharos. A server scenario runs against a real Vintage Story server embedded in your test process, without any live game client.

## Basic Setup

### 1. Install the Package

```bash
dotnet add package Zaldaryon.Pharos.XUnit
```

### 2. Create a Server Scenario Class

Inherit from `ServerScenarioBase` and mark test methods with `[ServerScenario]`:

```csharp
using Zaldaryon.Pharos.XUnit;
using Xunit;

[ServerWorld(seed: 12345, playStyle: "creativebuilding", worldType: "superflat")]
public class BlockPlacementTests : ServerScenarioBase
{
    [ServerScenario]
    public void SetBlock_PlacesBlockAtPosition()
    {
        var pos = new BlockPos(10, 64, 10);
        this.SetBlock(pos, "game:soil-low-none");

        var block = this.GetBlock(pos);
        Assert.Contains("soil", block.Code.Path);
    }
}
```

## ServerScenarioBase

The base class provides access to the embedded server and its APIs.

### Properties

| Property | Type | Description |
|----------|------|-------------|
| `Host` | `EmbeddedServerHost` | The embedded server host with tick control |
| `Server` | `ServerMain` | Direct access to the vanilla server instance |
| `Api` | `ICoreServerAPI` | Vintage Story server API |

### World Manipulation

```csharp
// Block access
void SetBlock(BlockPos pos, string blockCode);
void SetBlock(BlockPos pos, int blockId);
Block GetBlock(BlockPos pos);
BlockEntity? GetBlockEntity(BlockPos pos);

// Entity spawning
Entity SpawnEntity(string entityCode, Vec3d pos);
```

### Command Execution

```csharp
// Console caller
Task<CommandResult> ExecuteCommand(string command);
Task ExecuteSuccess(string command);  // asserts Ok == true

// Player caller
Task<CommandResult> ExecuteCommandAsPlayer(IServerTestPlayer player, string command);
Task ExecuteSuccessAsPlayer(IServerTestPlayer player, string command);
```

### Event Waiters

All waiters advance simulation ticks deterministically, not real-time delays:

```csharp
Task WaitForChunkLoadedAsync(int cx, int cy, int cz, int maxTicks = 300);
Task<Entity> WaitForEntitySpawnAsync(string entityCode, double radius, Vec3d around, int maxTicks = 300);
Task WaitForConditionAsync(Func<bool> condition, int maxTicks = 300);
```

## Lifecycle

`ServerScenarioBase` implements `IAsyncLifetime`. Before each test it boots an embedded server in a fresh sandbox from `WorldOptions`, which defaults to the class-level `[ServerWorld]`. It stages every `[ServerMods]` path from the class and the assembly into the sandbox's `Mods` folder first. After the test it stops the server, or keeps it for the next test (see below).

The server runs on its own game thread, as the real server does: boot, ticks and teardown all happen there. Test code that touches live server state should go through `Host.RunOnGameThread(...)`. The world helpers (`this.SetBlock`, `this.SpawnEntity`) and `ExecuteCommand` already do.

Scenarios run one at a time, whatever the test collection layout. The game keeps process-wide static state, so two servers or clients cannot boot side by side in one process.

## Isolation Modes

Control how world state is managed between test methods with `[ServerWorld(Isolation = ...)]` or by overriding `WorldIsolation`:

| Mode | Behavior |
|------|----------|
| `Rollback` (default) | Every test gets a freshly booted world. There is no faster in-place restore yet, so this currently isolates by restarting. |
| `Restart` | Every test gets a freshly booted world. |
| `Recycle` | The server keeps running for the next test of the same class. Tests share state, which suits read-only tests. |

```csharp
[ServerWorld(seed: 42, Isolation = WorldIsolation.Recycle)]
public class ReadOnlyTests : ServerScenarioBase { }
```

## Test Players

Join headless players into the server. Each one is a real multiplayer connection as far as the server can tell, with no rendering client behind it:

```csharp
[ServerScenario]
public async Task Player_CanReceiveItems()
{
    ServerTestPlayer player = await CreateTestPlayerAsync("TestPlayer");

    player.GiveItem("game:stick", 5);
    Assert.True(player.HasItem("game:stick", 5));

    player.GrantPrivilege("worldedit");
    await player.TeleportTo(100.0, 64.0, 100.0);
    await player.SayAsync("/time set day"); // runs as a command with the player as caller
}
```

A test player goes through the same join sequence as a real client, packet by packet:

1. Over an in-memory socket of its own, the player sends the login token query and its identification.
2. Once the server has spawned the player entity, the player sends the join request. The server answers it by setting up the inventories and streaming the world.
3. The player reports itself loaded and ready, which makes it a playing player.

So everything the server does for a real player also runs for a test player: the `PlayerJoin` and `PlayerNowPlaying` events, mods' join handlers, chunk sending, entity tracking and the playing-player count. A connection over an in-memory socket counts as local, so the server skips player verification and no auth server is involved.

Join as many players as a scenario needs. Each gets its own socket:

```csharp
var alice = await CreateTestPlayerAsync("Alice");
var bob = await CreateTestPlayerAsync("Bob");
Assert.Equal(2, Host.TestPlayers.Count);
```

`player.Disconnect()` sends the leave packet a quitting client sends. Players created in a test leave when the test ends, and the same name can join again. Outside a scenario, use `EmbeddedServerHost.JoinPlayerAsync`.

In a `ClientServerScenarioBase`, `CreateTestPlayerAsync` joins headless players next to the rendering client, which sees them as other players in the world.

A player counts as joined once it is playing and the server has sent it the chunk it stands in. The server only sends a client changes (blocks, particles, sounds) for chunks that client already has.

### What a player received

`player.Received` records everything the server has sent that player, which is what a real client would have received and acted on. Every packet is counted by id, and these kinds are decoded:

```csharp
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
```

| Member | Contents |
|--------|----------|
| `Chat`, `ChatMessages` | Chat lines, with group and chat type |
| `EntityArrivals`, `EntityDepartures`, `KnowsEntity`, `HasReceivedEntity` | Entities the server started or stopped tracking for the player |
| `PlayerData`, `HasReceivedPlayerData` | Player data about the player itself and about other players |
| `Particles`, `Sounds` | Particle spawns and sounds to play |
| `Highlights`, `HighlightedBlocks(slot)` | Block highlights per slot |
| `BlockChanges` | Block changes, single or batched |
| `ModPackets`, `ModPacketsOf<T>` | Mod channel packets |
| `IngameErrors`, `IngameDiscoveries` | In-game error and discovery messages |
| `CountsById`, `Total` | Every packet, counted by id |

`player.Received.Clear()` starts a fresh record.

## Server Tick Control

`EmbeddedServerHost` exposes deterministic tick stepping:

```csharp
[ServerScenario]
public async Task EntityMovesToTarget()
{
    var entity = SpawnEntity("game:wolf-male", new Vec3d(0, 64, 0));

    await Host.TickUntilAsync(
        () => entity.Pos.DistanceTo(new Vec3d(10, 64, 10)) < 1.0,
        maxTicks: 600
    );

    Assert.True(entity.Pos.DistanceTo(new Vec3d(10, 64, 10)) < 1.0);
}
```

## Staging Server Mods

Use `[ServerMods]` to load mods before server boot:

```csharp
[ServerWorld(seed: 42)]
[ServerMods("path/to/mymod.zip")]
public class ModIntegrationTests : ServerScenarioBase
{
    [ServerScenario]
    public void ModRegistersItsBlocks()
    {
        var block = Server.Api.World.GetBlock(new AssetLocation("mymod:myblock"));
        Assert.NotNull(block);
    }
}
```

## Watchdog

Tests that wedge or run too long fail automatically. The default watchdog is 120 seconds. Override per class:

```csharp
[ServerWorld(seed: 1)]
[ServerScenario(TimeoutMs = 30_000)]
public class QuickTests : ServerScenarioBase { }
```
