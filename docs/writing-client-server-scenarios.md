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

## Data Files

`[DataFiles]` puts files, such as mod configs, into the server's data folder, the client's, or both, before they boot. `{{pharos:port:NAME}}` placeholders get the same free port on both sides, read back with `DataFilePort("NAME")`. Each side's mod API reads its own folder. See [Data files](data-files.md).

## Mod Network Messages

`Client.ModNetwork` records every message the client's mods send and receive on their network channels, TCP and UDP, from the moment the client boots. It decodes them into your types, and hands messages to the client's handlers as if the server had sent them. It works on an engine-mode client only.

```csharp
// A look-alike of the mod's message: same [ProtoMember] numbers, matched by type name.
[ProtoContract] public class SyncPacket { [ProtoMember(1)] public int Version { get; set; } }
[ProtoContract] public class SyncAck { [ProtoMember(1)] public int Version { get; set; } }

ModMessage delivered = await Client!.ModNetwork.DeliverAsync("mymod", new SyncPacket { Version = 1 });
SyncAck ack = await Client.ModNetwork.WaitForSentAsync<SyncAck>("mymod", since: delivered.Sequence);
Assert.Equal(1, ack.Version);
```

- **Reading.**
  - `Sent<T>(channel)` and `Received<T>(channel)` decode the recorded messages of a type.
  - `Of<T>(channel, direction)` and `Messages(channel)` give their details: sequence, frame, channel, UDP or not, message id, registered type name, raw bytes, and whether a test injected it.
- **Matching types.** A test cannot reference a mod's types when the game compiles the mod from source. So `T` is matched to the channel's registered type by itself, then by full name, then by name. Pass `messageType` to name it outright. Protobuf decodes by member number, so a look-alike class with the same `[ProtoMember]` numbers works.
- **Delivering.** `DeliverAsync<T>(channel, message)` calls the channel's handler on the client thread, the way a server message reaches it, then steps a frame. A handler that throws throws out of it, and it fails when the channel, the type or the handler is missing. `DeliverAsync(channel, messageId, bytes)` sends raw data: an old protocol version, or a malformed message.
- **Waiting.** `WaitForSentAsync<T>` and `WaitForReceivedAsync<T>` step frames until a matching message comes after `since` (by default, now). A reply a handler sends during `DeliverAsync` is already recorded when it returns, so pass the delivered message's `Sequence` as `since`.
- **Channels.** `Channels()` lists each channel with its message types and ids. `Mark()` and `Clear()` help bracket a step.

A UDP message counts as sent when the client hands it to its socket: a degraded network may still drop it. The newest 10,000 messages are kept. A class whose pair is reused between tests (the default rollback isolation) starts each test with none; sequence numbers keep growing. Messages that do not decode as `T`, such as a malformed one a test delivered, are left out of `Sent<T>`, `Received<T>` and the waits.

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
