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

[ServerWorld(seed: "12345", playStyle: "creativebuilding", worldType: "superflat")]
public class BlockPlacementTests : ServerScenarioBase
{
    [ServerScenario]
    public void SetBlock_PlacesBlockAtPosition()
    {
        var pos = new BlockPos(10, 64, 10);
        SetBlock(pos, "game:soil-low-none");

        var block = GetBlock(pos);
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

## Isolation Modes

Control how world state is managed between test methods:

```csharp
[ServerWorld(seed: "42")]
public class MyTests : ServerScenarioBase
{
    // WorldIsolation.Rollback (default): fast in-memory snapshot restore between tests
    // WorldIsolation.Restart: full server reboot for tests requiring clean state
    // WorldIsolation.Recycle: no isolation, reuse running server (read-only tests)
}
```

With `Rollback` (default), Pharos takes an in-memory snapshot after server boot and restores it between test methods in about 50ms. The server keeps running; only chunks and savegame globals are reset.

## Test Players

Create simulated connected players:

```csharp
[ServerScenario]
public async Task Player_CanReceiveItems()
{
    var player = await CreateTestPlayerAsync("TestPlayer");

    player.GiveItem("game:sword-iron", 1);
    Assert.True(player.HasItem("game:sword-iron"));

    player.GrantPrivilege("worldedit");
    await player.TeleportTo(100.0, 64.0, 100.0);
}
```

`IServerTestPlayer` is backed by a genuine `ConnectedClient` and `ServerPlayer` registered in the server's internal registries. When the player is released, Pharos purges the name claim so subsequent tests can reuse the same player name.

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
[ServerWorld(seed: "42")]
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
[ServerWorld(seed: "1")]
[ServerScenario(TimeoutMs = 30_000)]
public class QuickTests : ServerScenarioBase { }
```
