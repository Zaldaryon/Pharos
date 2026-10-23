# Multiplayer scenarios

A headless test player completes the normal multiplayer handshake and owns a client connection. Use `CreateTestPlayerAsync` for individual players, or join a crowd through either scenario base or `EmbeddedServerHost`:

```csharp
using Zaldaryon.Pharos.Server;

var center = Server.DefaultSpawnPosition.XYZ;
ServerTestPlayer[] crowd = await CreateTestPlayersAsync(12, PlayerLayout.Ring(center, radius: 64));
// Or: PlayerLayout.Grid(center, spacing: 8, columns: 4)
```

Layouts use absolute world coordinates at the supplied Y, without flattening terrain. Ring order starts on positive X. Grid order is row-major; an incomplete final row remains left-aligned. Crowds use unique host-local names, normal join/chunk/entity tracking, and disconnect partially joined players if setup fails or is cancelled. Scenario teardown owns successful crowds.

## Server mod channels

`Server.ModNetwork` observes the server's registered TCP and UDP channels, including traffic from rendering clients and headless players. A sent message is one queue submission per recipient, recorded after the engine send returns, not a transport acknowledgement. UDP-over-TCP fallback remains one UDP-channel message, not a second send.

```csharp
using ProtoBuf;

[ProtoContract] public class ClaimRequest { [ProtoMember(1)] public int X { get; set; } [ProtoMember(2)] public int Z { get; set; } }
[ProtoContract] public class ClaimReply { [ProtoMember(1)] public int Result { get; set; } }

var crowd = await CreateTestPlayersAsync(2, PlayerLayout.Ring(Server.DefaultSpawnPosition.XYZ, 8));
using ServerModMessageLog messages = Server.ModNetwork.Record("mymod");
await crowd[0].ModNetwork.SendAsync("mymod", new ClaimRequest { X = 5, Z = 5 });

var request = messages.Received<ClaimRequest>().Single();
Assert.Equal(crowd[0].PlayerUID, request.Envelope.PlayerUID);
Assert.Equal(5, request.Message.X);

foreach (var reply in messages.Sent<ClaimReply>())
    Assert.True(reply.Envelope.Sequence > request.Envelope.Sequence);
```

The mod's message type need not be referenced by the test assembly. As with the client mod-network driver, a protobuf look-alike matches the registered type by exact identity, full name, then unique short name. Use `messageType: "MyMod.ClaimRequest"` for ambiguous short names. Typed queries filter by the registered message id before decoding: two protobuf-compatible types are not interchangeable.

Each envelope includes sender or recipient UID/name, copied position, channel, transport, registered type name/id, payload size, tick and monotonic host sequence. Tick/order are assigned on the server game thread. An off-thread send is collected at the next game-thread observation boundary; its position is captured then, not from a worker thread. No wall-clock sleeps define message order.

`SendAsync` sends through the player's existing link and first ticks until the bot's earlier packets have been handled or dropped, then ticks until the server observes that player's packet with the same payload on the same transport. The payload is serialized when you call it. One bot runs one `SendAsync` at a time and never beside its own `QueueSend`: an overlapping call on the same bot throws. Latency, scripts, filters and loss still apply; a dropped message times out. UDP messages tunnel through the engine's normal UDP-over-TCP client ingress, as other headless bot UDP actions do. `QueueSend` sends without ticking.

`Record()` includes every channel; `Record("mymod", capacity: 20000)` filters one. Scopes are independent. Dispose stops collection and keeps the recorded data. Scopes also stop when the host stops or crash-restarts. A stopped scope's messages, including typed reads, stay readable without the host; a recording failure before it stopped stays an error. Default capacity is 10,000 messages per scope. Overflow fails a read instead of passing an assertion on a truncated log. Host-wide `Messages()` has a separate 10,000-message history; `Clear()` clears only that history, not active scopes or sequence numbers. Payloads and positions returned by queries are copies. A send observed on a worker thread keeps the recipient it was addressed to, even if that player disconnects before the next game-thread boundary; its position is read at that boundary.

## Per-player visibility

```csharp
var crowd = await CreateTestPlayersAsync(2, PlayerLayout.Ring(Server.DefaultSpawnPosition.XYZ, 8));
var chunks = Server.Visibility.ChunksSentTo(crowd[0]);
var entities = Server.Visibility.EntitiesKnownTo(crowd[0]);
Assert.Contains(entities, entity => entity.EntityId == crowd[1].Entity!.EntityId);
```

These are game-thread snapshots of the engine connection's current chunk-sent set and tracked entities. Chunk positions are chunk coordinates, not block positions. Entity objects remain live. Tick until the relevant world work has completed before asserting. Rejoined players resolve to their current connection; disconnected or foreign-host players are rejected.

For a mod's nearby broadcast:

```csharp
using ProtoBuf;

[ProtoContract] public class BellRung { [ProtoMember(1)] public int Value { get; set; } }

var crowd = await CreateTestPlayersAsync(2, PlayerLayout.Ring(Server.DefaultSpawnPosition.XYZ, 8));
var bellPos = Server.DefaultSpawnPosition.AsBlockPos;
using var messages = Server.ModNetwork.Record("mymod");
await crowd[0].Bot.UseBlockAsync(bellPos);
PharosAssert.OnlyNearbyReceived(messages.Sent<BellRung>(), center: bellPos, radius: 32);
```

The assertion uses recipient positions captured at the game-thread observation boundary and an inclusive Euclidean 3D radius. It rejects empty evidence, incoming messages and missing/nonfinite positions. It checks that nobody outside the radius received a send, not that every nearby player did. Built-in chunk-based broadcasts can have different boundaries from a spherical radius. On Vintage Story 1.22.7, the entity and block-entity broadcast APIs send globally; sound uses a radius and particles use chunk visibility. To inspect actual built-in entity/block packets, sounds and particles, use each player's `Received` lists and `CountsById`; those remain separate from registered mod-channel recording.

## Same-tick inventory contention

Open the container through each bot's normal `UseBlockAsync` before queuing slot actions:

```csharp
var before = Server.Inventories.Totals(chestPos, radius: 16);
await Server.SameTick(() =>
{
    alice.Bot.QueueTakeSlot(chestPos, 0);
    bob.Bot.QueueTakeSlot(chestPos, 0);
});
PharosAssert.ItemsConserved(before, Server.Inventories.Totals(chestPos, radius: 16));
```

`QueueTakeSlot` sends the game's shift-click packet. `QueueMoveSlot(sourceInventoryId, sourceSlot, targetInventoryId, targetSlot, quantity)` sends its normal move-stack packet. Both validate open inventories and slots without advancing time. `ModNetwork.QueueSend` and `Network.SendChat` can also participate.

`SameTick` collects serialized TCP packets from its synchronous callback, validates all participating players, calls the engine's receive parser in submission order, then advances exactly one server tick. Handlers still perform normal permissions, inventory and client-state checks. This intentionally starts at the parser boundary rather than a transport socket: it controls dispatch order, not transport throughput or receive-thread timing. Existing external client packets are not reordered.

A callback/preflight failure submits no collected packets. Handler errors after commit use normal engine behavior and are not rolled back. Do not await, nest batches, tick the host, or call ordinary ticking bot methods in the callback. Use the queue methods. A batch rejects foreign/disconnected players, players whose earlier packets a network link still holds, UDP packets, and degraded, filtered or future-scripted links rather than silently bypassing their network settings. Bytes a bot already sent over a clean link are not rejected: the batch waits up to 5 seconds (scaled by `PHAROS_TIMEOUT_SCALE`) for the engine's parser thread to queue them first, without ticking. Use ordinary sends for transport-condition tests.
