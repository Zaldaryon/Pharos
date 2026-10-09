# Test Worlds

A scene a test needs (a house, a machine, a farm) does not have to be built one `SetBlock` at a
time. Pharos builds test worlds three ways:

- from a WorldEdit schematic, stamped in by the test;
- from a saved world, which each test class boots a copy of;
- from code, with `pharos fixture`, which runs a builder test and saves the world it leaves.

## Schematics

A schematic is the `.json` file WorldEdit writes with `/we export`. Put it in the test project,
copy it to the output, and place it from a scenario:

```xml
<None Include="Fixtures\**" CopyToOutputDirectory="PreserveNewest" />
```

```csharp
BlockPos origin = new(512, 4, 512);
SchematicPlacement placed = await this.PlaceSchematicAsync("Fixtures/furnace-room.json", origin);
```

- **In a server scenario,** `PlaceSchematicAsync` (or `EmbeddedServerHost.PlaceSchematicAsync`):
  1. loads the chunks under the box, ticking the server;
  2. places the blocks, the decors, the block entities with their contents and the entities, as
     the WorldEdit import does.
- **In a client-server scenario,** `PlaceSchematicAsync` (or
  `ClientServerLoopbackSession.PlaceSchematicAsync`) also steps the session until the client has
  every block and block entity of the schematic and has meshed the chunks under it. The client is
  only sent chunks within its view distance, so place schematics near the player.

The path is relative to the working folder, or else to the test assembly's folder.

`SchematicOptions`:

| Option | Default | |
|---|---|---|
| `ReplaceMode` | `ReplaceAll` | What the schematic replaces. `ReplaceAll` leaves the box exactly as exported, air included; `ReplaceOnlyAir` keeps what is there. |
| `Origin` | `StartPos` | Where `origin` sits on the box: its lowest corner, or a center. |
| `Angle` | 0 | A turn around the vertical axis: 0, 90, 180 or 270. Blocks are swapped for their turned variants, and `Origin` applies to the turned box. |
| `AllowMissingBlocks` | false | Leave out blocks the game does not know instead of failing. |
| `MaxTicks` | 3000 | How long the chunks under the box get to load. |

`SchematicPlacement` gives the box (`Start`, `End`, `Size`), the blocks placed, the chunks it covers
and the block codes left out.

The chunk columns under a schematic stay loaded for the life of the server. A column the server
unloads mid-test would be saved with the schematic in it, out of the world rollback's reach.
`EnsureChunksLoadedAsync(min, max)` loads and keeps any other box the same way, up to 1024 chunk
columns at once.

## Saved worlds

```csharp
[ServerWorld(SaveFile = "Fixtures/village.vcdbs")]
public class VillageTests : ServerScenarioBase
{
    [ServerScenario]
    public async Task TheWellHasWater()
    {
        await Host!.EnsureChunksLoadedAsync(well, well.UpCopy(3));
        // ...
    }
}
```

Each class boots into a copy of the save in its own sandbox, so the fixture file never changes,
and the world is rolled back to it between tests as usual. `ServerWorldOptions.SaveFile` does the
same for a host booted by hand.

- **The save's own settings win.** The world keeps its seed, play style, world type and world
  configuration; the attribute's are not applied to it.
- **Only the spawn area loads at boot.** Load the chunks a test reads with
  `EnsureChunksLoadedAsync` first.
- **Players come back.** A player saved in the world gets back their position and inventory when a
  test player of the same name joins.
- **Use a closed save.** A save copied out of a running game, with a `-wal` file next to it, is
  refused, because it misses what the log holds. Save worlds with the game closed, or with
  `pharos fixture`.

## Saving a world

`EmbeddedServerHost.SaveWorldAsync(path)` saves the world as it is and writes a copy of the save to
`path`, ready to boot from. It does what the game's autosave does, made synchronous:

1. pauses the server;
2. tells mods and systems the world is being saved;
3. writes the chunks;
4. copies the save with SQLite's online backup.

The copy is written next to `path` and then moved over it. The save runs to the end before the
call returns. As with an autosave:

- the pause drops the queue of chunks still being generated, and something that asks for them
  queues them again;
- the server stops when the disk has less free space than its `DieBelowDiskSpaceMb` setting.

## `pharos fixture`

`pharos fixture` makes saved worlds from code, so they can be made again when the game, a mod or
the scene changes. Write a builder test:

```csharp
public class VillageBuilder : ServerScenarioBase
{
    [ServerScenario]
    public async Task Build()
    {
        await this.PlaceSchematicAsync("Fixtures/village.json", new BlockPos(512, 4, 512));
        // ...
    }
}
```

Then run it:

```bash
pharos fixture tests/MyMod.Tests --scenario VillageBuilder.Build --out tests/MyMod.Tests/Fixtures/village.vcdbs
```

How it works:

- `--scenario` names one test: its full name, or a dotted tail of it.
- The command builds the project, finds that test, and runs it alone in a worker, as `pharos run`
  does.
- Once the test passes, the world of its server is saved, before the class rolls it back.
- `--out` is replaced only when the run succeeds.

Exit codes:

- 0: the save was written;
- 1: the test failed, or its world could not be saved;
- 2: the scenario named no test or several, or the test saved no world.

The run's results and logs go to `pharos-fixture/` in the working folder (`--results-dir` moves
them); add it to `.gitignore`.

Any `[ServerScenario]` or `[ClientServerScenario]` test can build a fixture. A theory cannot,
because its rows would all write the same file.

Saves are a few megabytes even for a superflat world, since they hold every chunk generated around
the spawn. Commit them as binary files (`*.vcdbs binary` in `.gitattributes`), or make them in CI
with `pharos fixture` before the tests run.
