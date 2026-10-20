# Server Profiling

A scenario can ask the server where its tick went. `Server.Profile.MeasureAsync(ticks)` runs a
stretch of ticks with stopwatches around the work the game does, then reports what each system, mod,
block entity, entity, behavior and AI task cost, and what every player sent and received.

```csharp
[ServerScenario]
public async Task TheQuernDoesNotCostMoreThanHalfAMillisecond()
{
    // Build the room and place the querns first; the profile covers only the ticks it runs.
    ServerProfile profile = await Server.Profile.MeasureAsync(ticks: 600);

    Assert.True(profile.BlockEntities["BlockEntityQuern"].MeanMsPerTick < 0.5);
    profile.Publish();
}
```

`Measure` is the synchronous form, for a test that cannot await. Both run the ticks themselves, so
set the world up first; the profile covers the ticks it ran and nothing before them. Only one
measurement can be open at a time, and cancelling the token stops it between ticks.

## What it reports

| Property | What it holds |
| --- | --- |
| `Ticks` | How many ticks actually ran and were measured |
| `TickTimes` | The whole tick's duration, one sample per tick: mean, P95, P99 and every sample |
| `Systems` | Each `ServerSystem.OnServerTick`, by system type name |
| `Mods` | The tick listeners and callbacks of each mod, by mod id |
| `BlockEntities` | The tick listeners attached to a block entity, by block entity type |
| `Entities` | Each entity type's `OnGameTick`, by entity code such as `game:chicken-hen` |
| `EntityBehaviors` | The behavior part of that time, by `entity code/behavior type` |
| `Listeners` | Every tick listener and callback, by the type and method it calls |
| `AiTasks` | Every `ShouldExecute` and `ContinueExecute`, most expensive first |
| `Bandwidth` | Packets and bytes sent and received per player, packet id and mod channel |

A `ServerProfileEntry` has `Calls`, `TotalMs`, `MeanMsPerTick`, `P95MsPerTick`, `Ticks` and
`Instances`. Times are per-tick sums, so P95 counts a tick in which the entry did not run as zero.
`PerTickMs` holds the sample behind those numbers, one value per measured tick. `Instances` is the
number of block positions, entity ids or listeners seen, depending on the collection; it is zero
where the profile does not track instances.

An `AiTaskProfile` has `Code`, `Calls`, `TotalMs`, `PerTickMs`, `MeanMsPerTick` and `P95MsPerTick`. The code is
the game's registry code, such as `wander` or `seekentity`, so it is stable across runs.

The levels nest. A block entity's listener time is in `BlockEntities` and in the mod that registered
it. An AI task runs inside its entity's behaviors, so its time is also in `EntityBehaviors` and
`Entities`. Add the levels only when you mean to.

## How work is attributed

- **Systems.** Every `ServerSystem.OnServerTick` call is timed at the call site the game's own tick
  profiler uses.
- **Mods, listeners and block entities.** A tick listener or delayed callback is attributed to the
  mod whose assembly registered it, using the same owner map as the tick listener inspector. A
  listener tied to a block entity is also attributed to that block entity's type, and the number of
  distinct positions it ran for is its `Instances`.
- **Entities and behaviors.** `ServerSystemEntitySimulation` ticks each entity; that call is timed
  by entity code. Inside the entity's `OnGameTick`, each `EntityBehavior.OnGameTick` is timed
  separately.
- **AI tasks.** The two methods the task manager calls on every task, `ShouldExecute` and
  `ContinueExecute`, are timed on the task's own implementation and named by the game's task
  registry.
- **Traffic.** Bytes and packets are counted where the server writes to and reads from a client.
  Packet ids are counted where the game serializes and dispatches a packet, and mod channels where
  `NetworkChannel` carries a message, including the channel's message id. Received events are
  queued by the packet parser thread and applied on the game thread at the end of the tick.

Work in the engine itself has no mod owner and appears only under systems, entities and behavior
types. A mod's `ModSystem` methods that run at boot, such as `StartServerSide`, are not part of a
tick measurement.

## Limits

- Times are CPU time from `Stopwatch`, not wall time, and a measured tick also pays for the
  stopwatches. They are noisy: compare two runs instead of reading one as an absolute, and let
  `pharos compare` decide whether a difference is real.
- The behavior split covers the behavior loop in `Entity.OnGameTick`. An entity class that overrides
  `OnGameTick` and does not call the base implementation still appears under `Entities`, but its
  behaviors are not split out.
- A mod channel's bytes are its payloads, what the mod serialized. The framed packet on the wire also
  has headers and may be compressed above 5 KB. Per-packet-id traffic is counted in packets, not
  bytes, because the id and the framed size are known at different points in the send path.
- Received bytes are the TCP messages the game reads per client. The game does not count received
  UDP per client, so neither does the profile.
- The hooks for entity, behavior, AI-task and network work are installed when a measurement opens and
  removed when it closes. The listener and system hooks live as long as the server does, because the
  automatic tick collector uses them too; when no profile is open they call the game directly after
  one null check.

## Metrics and comparison

`profile.Publish()`, or `Metrics.Publish(profile)`, records every number into the scenario's metrics,
so `result.json` carries them and `pharos compare` can check a change before and after. Each time
metric keeps one sample per measured tick, so the comparison sees the same distribution the profile
does. Outside a scenario it does nothing.

| Metric | Unit | Clock |
| --- | --- | --- |
| `server.tick.ms` | milliseconds per tick | tick |
| `server.tick.system.<name>.ms` | milliseconds per tick | tick |
| `server.tick.mod.<id>.ms` | milliseconds per tick | tick |
| `server.tick.be.<type>.ms` | milliseconds per tick | tick |
| `server.tick.entity.<code>.ms` | milliseconds per tick | tick |
| `server.tick.behavior.<entity code>.<type>.ms` | milliseconds per tick | tick |
| `server.tick.listener.<handler>.ms` | milliseconds per tick | tick |
| `server.ai.<task>.ms` | milliseconds per tick | tick |
| `net.sent.<player>.bytes`, `net.received.<player>.bytes` | bytes | exact |
| `net.sent.<player>.packets`, `net.received.<player>.packets` | packets | exact |
| `net.sent.<player>.packet.<id>.packets` | packets | exact |
| `net.received.<player>.packet.<id>.packets` | packets | exact |
| `net.sent.<player>.<channel>.bytes` and `.packets` | channel payload bytes, packets | exact |
| `net.received.<player>.<channel>.bytes` and `.packets` | channel payload bytes, packets | exact |
| `net.sent.bytes`, `net.sent.packets`, `net.received.bytes`, `net.received.packets` | totals | exact |

The time metrics also record `<name>.calls`, the number of calls in the run. A behavior's metric
carries both the entity code and the behavior type, so two entity types that share a behavior, or two
instances of the same type, stay one comparable series per entity type.

## A budget in a test

```csharp
ServerProfile profile = await Server.Profile.MeasureAsync(ticks: 600);
PharosAssert.TickCostBelow(profile, "mymod", 0.5);
```

The assertion fails when the mod has no measured tick work, as well as when its mean cost is over the
budget: a budget on work that never ran proves nothing.

## Reading the traffic

```csharp
ServerProfile profile = await Server.Profile.MeasureAsync(ticks: 600);
BandwidthProfile traffic = profile.Bandwidth;
PlayerTraffic playerTraffic = traffic.Players["Alice"];
long bytesToAlice = playerTraffic.BytesSent;
long busiestCount = playerTraffic.PacketsSentById.Values.Max(); // keyed by packet id
ChannelTraffic mod = playerTraffic.Channel("mymod");             // empty when unused
long largestMessage = mod.MessagesSent.Keys.Max();               // the channel's message ids
```
