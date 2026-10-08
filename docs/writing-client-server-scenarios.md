# Writing Client-Server Scenarios

This guide explains how to write end-to-end scenarios that run a Vintage Story client and server together in the same test process, advancing them in lockstep.

## Basic Setup

### 1. Install the Package

```bash
dotnet add package Zaldaryon.Pharos.XUnit
```

### 2. Create a Client-Server Scenario Class

Inherit from `ClientServerScenarioBase` and use `[ClientServerScenario]`:

```csharp
using Zaldaryon.Pharos.XUnit;
using Xunit;

public class BlockSyncTests : ClientServerScenarioBase
{
    [ClientServerScenario]
    public async Task PlacingBlock_SyncsToClient()
    {
        var pos = new BlockPos(10, 64, 10);

        // Place a block on the server
        Server.Api.World.BlockAccessor.SetBlock(1, pos);

        // Step until client chunk reflects the change
        await Session.StepUntilAsync(() => ClientServerAssert.BlockSynced(Session, pos));

        // Assert final state
        ClientServerAssert.ClientServerBlockSynced(Session, pos);
    }
}
```

## ClientServerScenarioBase

### Properties

| Property | Type | Description |
|----------|------|-------------|
| `Client` | `HeadlessClient` | Headless client instance with OpenGL context |
| `ServerHost` | `EmbeddedServerHost` | Embedded server host |
| `Server` | `ServerMain` | Direct server access |
| `Session` | `ClientServerLoopbackSession` | Loopback bridge for synchronized stepping |
| `Player` | `IClientTestPlayer` | Client-side test player |

## Lockstep Stepping

`ClientServerLoopbackSession.Step()` advances both the server and client in a deterministic 6-stage pipeline:

1. Flush client outbound network packets
2. Server processes received packets
3. Server advances simulation ticks
4. Flush server outbound network packets
5. Client processes received packets
6. Client advances one render frame

```csharp
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
```

## State Assertions

`ClientServerAssert` provides assertions across both sides of the loopback:

```csharp
// Block at pos is the same on client and server
ClientServerAssert.ClientServerBlockSynced(Session, pos);

// Client player position matches server entity position
ClientServerAssert.PlayerPositionSynced(Session, maxDistance: 0.1);

// Client inventory slot matches server store
ClientServerAssert.InventorySynced(Session, "hotbar");
```

## Packet Tracing

`PacketTracer` captures which packets cross the loopback boundary:

```csharp
[ClientServerScenario]
public async Task ChatMessage_SendsCorrectPacket()
{
    var tracer = new PacketTracer();
    tracer.StartTracing();

    await ExecuteCommandAsPlayer(Player.ServerPlayer, "/say hello");
    Session.StepFrames(10);

    tracer.StopTracing();

    // Assert packet 48 (chat) was sent client-to-server
    tracer.AssertClientSentPacket(48);
}
```

## Network Degradation

Inject simulated adverse network conditions before stepping:

```csharp
[ClientServerScenario]
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
```

## Lifecycle

`ClientServerScenarioBase` handles the full lifecycle:

1. Boot embedded server in an isolated sandbox directory
2. Initialize headless client with offscreen GLFW context and null audio
3. Connect client to server over in-process loopback
4. Run the test method
5. Under `Rollback`, roll the world back and keep the pair for the next test of the class (see [Isolation](#isolation)); otherwise go on below
6. Disconnect client
7. Drain server background tasks
8. Stop and dispose server; delete sandbox directory

The default watchdog is 180 seconds. Override with `[ClientServerScenario(TimeoutMs = 60_000)]`.

## Isolation

Override `WorldIsolation`, or set `[ServerWorld(Isolation = ...)]`, to choose what the next test of the same class starts from. `[ServerWorld]` on a client-server class also sets its world: seed, play style and world type.

| Mode | Behavior |
|------|----------|
| `Rollback` (default) | The server, the client and its connection stay up. The world is put back in place as it was when the client joined. |
| `Restart` | Every test boots a new server and client. `Recycle` does the same here. |

Under `Rollback`, after each test:

- **Test players:** they leave.
- **World:** the snapshot taken after the join is restored on the server's game thread (see [Rollback](writing-server-scenarios.md#rollback)).
- **Joined player:** it goes back to where it stood, with the game mode and inventories it had.
- **Client:** it is sent the restored chunks. Its held controls are released and its dialogs closed. Its packet recorder, network degradation, sound recorder and logs are reset.

- **Listeners:** tick listeners, delayed callbacks and event-bus listeners the test registered, on the server and on the client, are removed, as are block-position callbacks the test's changes scheduled (see [Listeners a test leaves](writing-server-scenarios.md#listeners-a-test-leaves)).
- **Mods:** once the client has the world back, `pharos:rollback:restored` fires on the server's event bus, then on the client's, and the class's `OnRollbackRestored()` runs. `pharos:rollback:captured` fires on both sides once, when the world is captured after the join (see [Mods and rollbacks](writing-server-scenarios.md#mods-and-rollbacks)).

The next test of the class gets the same client, already joined, with no boot. A test that disconnects the client, cuts its link, or overrides `WaitForPlayerJoinOnInit` to false gets a freshly booted pair, as does the next test after a failed rollback.

```csharp
public class ExpensiveSetupTests : ClientServerScenarioBase
{
    // Every test boots its own pair.
    protected override WorldIsolation WorldIsolation => WorldIsolation.Restart;
}
```

A client counts the time from the server's calendar packets. `Session.WaitForCalendarSyncAsync()` steps until the client's calendar matches the server's: to the second when frozen, within 60 game seconds while time runs. It throws a `TimeoutException` naming both times when they do not meet within its frame budget:

```csharp
ServerHost!.Calendar.Freeze();               // first, or time runs on between the calls
ServerHost.Calendar.SetTime(hourOfDay: 22);
await Session!.WaitForCalendarSyncAsync();
Assert.Equal(22f, Client!.RunOnClientThread(() => Client.Client.Calendar.HourOfDay));
```

A rollback waits for the same before the next test. Of the weather, a headless client takes the precipitation override at once; cloud patterns and wind reach a client as it renders them, so assert those on the server (see [Calendar and Weather](writing-server-scenarios.md#calendar-and-weather)).

`Isolation` and `StrictIsolation` work as for server scenarios (see [Isolation report](writing-server-scenarios.md#isolation-report)). A test that disconnects the client on purpose cannot be rolled back, so under `StrictIsolation` it fails.

```csharp
[ServerWorld(Isolation = WorldIsolation.Rollback, StrictIsolation = true)]
public class MyTests : ClientServerScenarioBase
{
    protected override void OnRollbackRestored() => MyModSystem.Instance.ReloadFromWorld();
}
```
