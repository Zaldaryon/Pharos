# Block ticks and snow

Some blocks change when the server ticks them by chance: grass spreads, leaves fall, snow melts and
ice thaws. The server does that on a thread of its own, so a test cannot count on it. Pharos ticks
the blocks itself, with a random of the test's own.

## Ticking one block

`Server.BlockTicks.Tick` ticks one block the way the server does, with a seeded random, and says
whether the block wanted the tick.

```csharp
bool wanted = Server.BlockTicks.Tick(pos, seed: 7);

Assert.False(wanted);
```

A block that does not want ticks says so, and nothing happens. A block that wants one is ticked,
the way the server would tick it.

## Ticking a region

`Server.BlockTicks.TickRegionAsync` ticks every block of a region many times and counts the blocks
that changed.

```csharp
int changes = await Server.BlockTicks.TickRegionAsync(pos, pos, rounds: 50, seed: 7);

Assert.True(changes > 0);
```

The region is given by two corners, both included. The rounds are one after the other, and each
round gives the blocks a different random, so the run is the same every time.

## Stopping the background ticks

`Server.BlockTicks.Pause` stops the server's own random ticks, so the blocks of a test stay where
they are, and `Resume` lets them run again.

```csharp
Server.BlockTicks.Pause();

Assert.True(Server.BlockTicks.IsPaused);

Server.BlockTicks.Resume();
```

## The snow

`Server.Snow.LevelAt` reads how much snow lies on a block, the way the block itself says it, and
`Server.Snow.AllowsSnow` says whether the block takes snow at all.

```csharp
float snow = Server.Snow.LevelAt(pos);

Assert.True(snow >= 0);
```

`Server.Snow.UpdateAsync` asks the world's own weather system to look at the chunks of a region
now, and waits for the snow to settle. The snow falls when the climate is cold enough and the
weather brings it, so a test sets both first.

```csharp
using ClimateOverride cold = Server.Climate.Override(new Cuboidi(pos.X - 4, pos.Y - 2, pos.Z - 4, pos.X + 4, pos.Y + 4, pos.Z + 4), temperature: -10);
Server.Weather.SetPrecipitation(1f);

bool updated = await Server.Snow.UpdateAsync(pos, pos, maxTicks: 100);

Assert.True(updated);
```

## Limits

- The blocks are ticked the way the server ticks them, so the weather, the climate and the light
  decide what a block does with the tick.
- The snow belongs to the world's weather system. A world without that system has no snow, and the
  update says so.
- The snow of a world only falls where the world's weather has its regions loaded, so a world
  without a loaded weather region keeps no snow, even with a cold climate and a falling weather.
- The random is the test's own, so a run with the same seed gives the same result.
