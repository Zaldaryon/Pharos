# World Metrics

Worldgen, saving and the size of the loaded world are where a mod can hurt a server without
touching a tick. A scenario can measure all three: `Server.Worldgen` generates a fresh area and
attributes every generator, `Server.Saves` runs a save and splits its time and bytes by category,
and `Server.WorldStats` counts what is loaded.

```csharp
WorldgenProfile worldgen = await Server.Worldgen.MeasureAsync(64, 64, 65, 65);
Assert.True(worldgen.MeanMsPerColumn < 5);
Assert.True(worldgen.Mod("mymod").MeanMsPerColumn < 20);
```

## Worldgen

`Server.Worldgen.MeasureAsync(chunkX1, chunkZ1, chunkX2, chunkZ2)` asks the server to generate the
chunk columns in that rectangle through its normal chunk queue. Generation runs on the game's
worldgen threads, so the call ticks the server while it waits. The area must be unloaded; a
measurement of a loaded column throws `InvalidOperationException` instead of timing a cache hit. At
most 64 columns fit in one measurement.

```csharp
WorldgenProfile profile = await Server.Worldgen.MeasureAsync(64, 64, 65, 65, maxTicks: 8000);
WorldgenHandlerProfile expensive = profile.Handlers[0];
Console.WriteLine($"{expensive.Stage} {expensive.Owner} {expensive.Handler}: {expensive.MeanMsPerItem:F3} ms");
```

`WorldgenProfile` has:

| Property | What it holds |
| --- | --- |
| `RequestedColumns` | How many columns the measurement asked for |
| `Columns` | How many columns were generated, including neighbours the game needed |
| `WallMs` | Wall time from the request to the game's loaded callback |
| `Handlers` | One `WorldgenHandlerProfile` per handler that ran, most expensive first |
| `Passes` | The per-pass handlers |
| `MapRegions`, `MapChunks` | The map region and map chunk handlers |
| `TotalPassMs` | The sum of every per-pass handler's time |
| `MeanMsPerColumn` | `TotalPassMs` divided by `Columns` |
| `Mods` | One `WorldgenModProfile` per mod, most expensive first |
| `Mod(modId)` | A mod's aggregate, or `KeyNotFoundException` when it ran no pass handler |

A `WorldgenHandlerProfile` has `Stage` (`terrain`, `terrain_features`, `vegetation`,
`sunlight_flood`, `done`, `region` or `mapchunk`), `Handler` (its type and method), `Mod` (null for
the game's own generators), `Owner` (`Mod`, or `game`), `Calls`, `Items`, `PerItemMs`, `TotalMs`,
`MeanMsPerItem`, `P95MsPerItem` and `MaxMsPerItem`. For a pass handler one item is one chunk column;
for a map region or map chunk handler it is one region or map chunk. A `WorldgenModProfile` has
`Mod`, `Owner`, `Calls`, `Columns`, `Handlers`, `TotalMs`, `MeanMsPerColumn`, `P95MsPerColumn` and
`MaxMsPerColumn`; its per-column samples sum the mod's handlers across passes.

Handlers are attributed by the assembly of the delegate the mod registered, the same rule the server
profile uses. They are found by replacing the entries in the game's handler lists for the duration
of the measurement and restored when it closes. A handler a mod registers while a measurement is
open is not timed.

## Saves

`Server.Saves.MeasureAsync()` runs the game's full save path synchronously on the game thread: map
regions, map chunks, dirty loaded and generating chunks, player data and the savegame blob. It does
not run the off-thread autosave, so the profile is complete when the task returns.

```csharp
SaveProfile save = await Server.Saves.MeasureAsync();
Assert.True(save.TotalBytes < 100_000_000);
Assert.True(save.ModDataBytes("mymod") < 1024 * 1024);
save.Publish();
```

| Property | What it holds |
| --- | --- |
| `TotalMs` | Wall time of the whole save |
| `TotalBytes` | The sum of every category's bytes |
| `Categories` | `players`, `mapregions`, `mapchunks`, `chunks` and `savegame`, in write order |
| `Chunks`, `MapChunks`, `MapRegions`, `Players`, `SaveGame` | The category of that name |
| `ModData` | One `SaveModDataEntry` per mod data key, largest first |
| `TotalModDataBytes` | The sum of the mod data payloads |
| `ModDataBytes(modId)` | The payloads whose key has that mod's domain |
| `ModDataOf(modId)` | The entries whose key has that mod's domain |

A `SaveCategoryProfile` has `Name`, `Ms`, `Bytes` and `Count`. The bytes are the serialized arrays
the game hands to the database, and the time is time inside the database write calls; serializing
the chunks and marking them clean happens outside those calls, so `TotalMs` is larger than the sum
of the categories.

Only dirty chunks, map chunks and map regions are written, as in a normal save. Change a block or
set mod data first if a test needs a category to have work in it.

`SaveProfile.ModData` aggregates the same key across the savegame, map regions and loaded chunks, so
a key a mod stores in every chunk appears once with the sum of those payloads. A key such as
`mymod:notes` belongs to `mymod` when `mymod` is a loaded mod id; other keys are unattributed. The
payloads are the in-memory serialized values, not the database framing around them. `LiveModData`
is not included: the game serializes it into the chunk's mod data only when the chunk unloads.

## Loaded world counts

`Server.WorldStats` takes a snapshot and returns a `WorldStats`:

```csharp
WorldStats stats = Server.WorldStats;
Assert.True(stats.LoadedChunks > 0);
Assert.Equal(stats.BlockEntitiesByType.Values.Sum(), stats.BlockEntities);
```

| Property | What it holds |
| --- | --- |
| `LoadedChunks` | Loaded 3D chunks |
| `LoadedMapChunks`, `LoadedMapRegions` | Loaded map chunks and map regions |
| `GeneratingChunkColumns` | Chunk columns in the generating queue |
| `LoadedEntities` | Loaded entities, dead ones included |
| `EntitiesByCode` | Entities per code, such as `game:chicken-hen` |
| `BlockEntities` | Block entities in the loaded chunks |
| `BlockEntitiesByType` | Block entities per class name, such as `BlockEntityQuern` |

The game clones its loaded chunk, map chunk and map region dictionaries to answer, which locks the
loaded chunk dictionary briefly. Take the snapshot between ticks, not in a loop.

## Metric names

Every profile publishes its numbers for `result.json` and `pharos compare` through `Publish()`, or
through `Metrics.Publish(worldgen, save, stats)` for several at once:

```csharp
WorldgenProfile worldgen = await Server.Worldgen.MeasureAsync(64, 64, 65, 65);
SaveProfile save = await Server.Saves.MeasureAsync();
WorldStats stats = Server.WorldStats;
Zaldaryon.Pharos.Metrics.Metrics.Publish(worldgen, save, stats);
```

| Profile | Names |
| --- | --- |
| Worldgen | `worldgen.wall.ms`, `worldgen.requested.columns`, `worldgen.columns`, `worldgen.<stage>.<owner>.<handler>.ms`, `.calls`, `.items`, `worldgen.mod.<owner>.ms`, `.calls`, `.columns` |
| Save | `save.total.ms`, `save.total.bytes`, `save.<category>.ms`, `.bytes`, `.count`, `save.moddata.bytes`, `save.moddata.<owner>.bytes`, `.keys`, `save.moddata.key.<owner>.<key>.bytes` |
| World stats | `worldstats.chunks`, `worldstats.mapchunks`, `worldstats.mapregions`, `worldstats.generatingchunkcolumns`, `worldstats.entities`, `worldstats.blockentities`, `worldstats.entity.<code>`, `worldstats.blockentity.<type>` |

Handler times are one sample per column, region or map chunk with the stopwatch clock, so
`pharos compare` can test a distribution. Save times are one sample per save. Bytes and counts are
exact. A key or owner in a metric name has spaces replaced with `_` and `/` with `.`.

## Limits

- Worldgen measures the game's real generation on the game's threads. Neighbouring columns can be
  generated and are included, so `Columns` can exceed `RequestedColumns`. The area stays loaded
  afterwards, like any chunk a player visits.
- The save measurement calls the game's internal full-save method. If a game update removes it, the
  measurement throws `InvalidOperationException` rather than silently measuring nothing.
- Save files may contain database pages larger than the payloads reported here. `TotalBytes` is the
  serialized data handed to the database, not the file size on disk.
- Counts are a snapshot of loaded objects. A chunk the server unloads while the scenario runs changes
  the next snapshot, not the one already taken.
