# Metrics

Pharos records named metrics for every scenario, so a test can say what a change cost instead of
only whether it passed. Metrics are written to `result.json` for every test, pass or fail, and
summarised in `run.json` and `pharos-run.json` for tools and before/after comparisons.

## Recording your own metrics

The static `Metrics` class records into the scenario running on the current flow:

```csharp
using Zaldaryon.Pharos.Metrics;

[ClientServerScenario]
public void Forge_smelts_a_stack()
{
    using (Metrics.Time("mymod.forge.fill.ms"))
    {
        // Work the test wants to time.
    }

    Metrics.Record("mymod.forge.smeltTicks", 2140,
        MetricUnit.Count, MetricDirection.LowerIsBetter);
    Metrics.Count("mymod.forge.items", 64, MetricDirection.Neutral);
    Metrics.Bytes("mymod.forge.savedBytes", 18432);
}
```

`Metrics.Record` takes a name, a value, a unit (`Count`, `Milliseconds`, `Bytes`, `Ratio`), a
direction (`LowerIsBetter`, `HigherIsBetter`, `Neutral`) and an optional clock and tags. A
millisecond reading with no clock is marked as a stopwatch reading; every other unit is exact.
`Metrics.Count`, `Metrics.Bytes` and `Metrics.Time` are shorthands. Repeated calls with the same
name form one sample set.

The API is a no-op outside a scenario, so mod and library code can call it without checking
whether Pharos is running. `Metrics.Time` still measures when no scenario is collecting; it just
records nothing.

## Automatic metrics

Put `[CollectMetrics]` on a test method, a class or the assembly. The flags from all three are
ORed, so a class can ask for frame and memory metrics while one method adds tick metrics:

```csharp
using Zaldaryon.Pharos.Metrics;

[ClientServerScenario]
[CollectMetrics(Metric.Frame | Metric.Tick | Metric.Network | Metric.Gl)]
public void Forge_smelts_a_stack()
{
    // ...
}
```

| Flag | Metrics |
|------|---------|
| `Metric.Frame` | `frame.count`, `frame.time.ms` |
| `Metric.Tick` | `server.ticks`, `server.tick.ms`, `tick.system.<ServerSystem>.ms`, `tick.listener.<target type>.ms` |
| `Metric.Memory` | `memory.allocated.bytes`, `memory.heap.bytes`, `memory.gc0`, `memory.gc1`, `memory.gc2` |
| `Metric.Gl` | `gl.drawcalls`, `gl.drawcalls.direct`, `gl.drawcalls.multi`, `gl.drawcalls.indirect`, `gl.buffers.created`, `gl.buffers.deleted`, `gl.textures.created`, `gl.textures.deleted`, `gl.framebuffers.created`, `gl.errors` |
| `Metric.Network` | `net.messages`, `net.bytes`, and `net.channel.<channel>.<direction>.packets` / `.bytes`, where direction is `sent` or `received` |
| `Metric.World` | `world.chunks`, `world.mapchunks`, `world.regions`, `world.entities`, `world.blockentities`, `world.listeners`, and `world.entity.<code>` per entity code |
| `Metric.Boot` | `boot.isolation.ms`, when the host was prepared or rolled back |

A method without `[CollectMetrics]` still records custom metrics and still writes `result.json`.
Automatic collection observes the scenario body; it does not change what the game does.

## Clocks

Each metric records which clock produced it, so a comparison knows how much to trust it:

| Clock | Meaning |
|-------|---------|
| `exact` | Counts and byte totals. Comparable between runs. |
| `frame` | Simulated frame time. Comparable within one testbed. |
| `tick` | Simulated server tick time. Comparable within one testbed. |
| `stopwatch` | Wall-clock time. Noisy; use repeated samples and percentiles. |

`Metrics.Time` and bridge timings use `stopwatch`. The automatic frame and tick collectors use
the `frame` and `tick` clocks.

## From mod code

A mod can record through the bridge without referencing the Pharos engine:

```csharp
using Zaldaryon.Pharos.Bridge;

PharosMetrics.Count("mymod.forge.calls", 3);
using (PharosMetrics.Time("mymod.forge.tick.ms"))
{
    // ...
}
```

The bridge forwards readings to the scenario running in the same process. Outside Pharos no one
is listening, so every call is a no-op. Units are `count`, `ms`, `bytes` or `ratio`; directions
are `lowerIsBetter`, `higherIsBetter` or `neutral`. A bridge timing is recorded on the stopwatch
clock.

## `result.json`

Each test gets `<root>/<TestClass>/<TestMethod>/result.json`. A theory row adds a short hash of its
arguments to the method name. The default root is `TestResults/pharos` under the test assembly;
failure artifacts use the same folder.

```json
{
  "schemaVersion": 1,
  "test": "MyTests.Forge_smelts_a_stack",
  "class": "MyTests",
  "method": "Forge_smelts_a_stack",
  "runId": "20261019-120000",
  "startedUtc": "2026-10-19T12:00:00.0000000Z",
  "durationMs": 812.4,
  "metrics": {
    "mymod.forge.smeltTicks": {
      "unit": "count",
      "direction": "lowerIsBetter",
      "clock": "exact",
      "samples": [2140],
      "count": 1,
      "sum": 2140,
      "min": 2140,
      "max": 2140,
      "mean": 2140,
      "p50": 2140,
      "p95": 2140,
      "p99": 2140,
      "value": 2140
    },
    "frame.time.ms": {
      "unit": "ms",
      "direction": "lowerIsBetter",
      "clock": "frame",
      "samples": [16.1, 15.8, 16.4],
      "count": 3,
      "sum": 48.3,
      "min": 15.8,
      "max": 16.4,
      "mean": 16.1,
      "p50": 16.1,
      "p95": 16.4,
      "p99": 16.4,
      "value": 16.1
    }
  }
}
```

`value` is the sum for counts and bytes and the mean for times and ratios, so a single number
answers "how much" without losing the samples. Percentiles use nearest rank: P50 is the lower of
the two middle samples when the count is even.

`test.duration.ms`, a stopwatch measurement of the scenario body, is recorded for every test.

## `run.json` and `pharos-run.json`

A failed test's `run.json` includes the same metrics plus the path to `result.json` under
`metricsResult`. Every test record in `pharos-run.json` includes a `metrics` object with the
compact summaries, so `pharos run`, `pharos diff` and `pharos compare` read one schema.

## Limitations

- The automatic frame collector records frame duration and frame count. Render-stage and
  per-renderer breakdowns are available from `Session.MeasureFramesAsync`, which remains the tool
  for a dedicated measured window.
- Network metrics read the client's mod-channel log, on an engine-mode client. Fixture-mode
  clients have no mod network log and record no network metrics. Vanilla packet ids and
  server-side mod messages are not broken out by the automatic collector.
- The GL collector reports command and resource counts; VRAM and texture-atlas pages are not part
  of this schema yet.
- Per-system and per-listener tick times cover calls that happen while the scenario's tick
  collector is open. A scenario that also opens `MeasureFramesAsync` yields its tick collector to
  that measurement and records only custom metrics for that window.
- Metrics never fail the test they measure. A session whose collectors cannot start, or a
  collector that fails while completing, is skipped and the test still reports its own outcome.
