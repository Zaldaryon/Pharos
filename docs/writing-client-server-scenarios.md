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
5. Disconnect client
6. Drain server background tasks
7. Stop and dispose server; delete sandbox directory

The default watchdog is 180 seconds. Override with `[ClientServerScenario(TimeoutMs = 60_000)]`.

## Isolation

Each `[ClientServerScenario]` test method gets its own server and client boot cycle. If you want to share a server across methods in a class (faster), use `ClientServerScenarioBase.IsolationMode`:

```csharp
// Override in your base class to share server/client across methods
// Default: IsolationMode.FreshClient (new client per test)
```
