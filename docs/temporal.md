# Stability, storms and rifts

The world of survival mode is not always steady. Places lose their stability, storms run over the
land and rifts open. The game keeps that state to itself, so Pharos gives a test a way in.

## Stability

`Server.Temporal.StabilityAt` reads how stable a place is, the way the game says it: one is
steady, zero is not, and two when the world has the temporal systems switched off.

```csharp
float stability = Server.Temporal.StabilityAt(pos);

Assert.InRange(stability, 0f, 1f);
```

`player.Vitals.TemporalStability` reads the stability a player carries, from zero to one, and
`TemporalStabilityVelocity` how fast it changes now. Both are null without the survival mod's
temporal stability behaviour.

## Storms

`Server.Temporal.Storm` reads the storm of the world: whether one runs now, the day the next one
starts, its strength, how much the world comes apart during it and the pattern it spawns with.

```csharp
TemporalStorm storm = Server.Temporal.Storm;

Assert.True(storm.Active);
```

`Server.Temporal.StartStormAsync` starts a storm now, with a strength, the way the world's own
storm tick does, and `Server.Temporal.EndStormAsync` ends it.

```csharp
await Server.Temporal.StartStormAsync(TemporalStormStrength.Heavy);

Assert.True(Server.Temporal.Storm.Active);
```

## Rifts

`Server.Temporal.Rifts` lists the rifts that are open now, with where they are and when they
disappear.

```csharp
IReadOnlyList<TemporalRift> rifts = Server.Temporal.Rifts;

Assert.NotNull(rifts);
```

`Server.Temporal.SpawnRift` opens a rift at a place, with the hours it stays, and adds it to the
game's own list. `Server.Temporal.ClearRifts` closes every rift that is open now.

```csharp
TemporalRift rift = Server.Temporal.SpawnRift(pos, hours: 2);

Assert.Contains(Server.Temporal.Rifts, r => r.RiftId == rift.RiftId);
```

## Rift wards

`Server.Temporal.Ward` reads what a rift ward does, and `Server.Temporal.SetWard` switches it on
or off, the way the ward does.

```csharp
Server.Temporal.SetWard(pos, true);

Assert.True(Server.Temporal.Ward(pos).On);
```

## Temporal gears

`Server.Temporal.UseGearAsync` holds a temporal gear on a block until it sets the player's spawn
point, the way a player does.

```csharp
player.GiveItem("game:gear-temporal");
await Server.Temporal.UseGearAsync(player, ticks: 90);

Assert.NotNull(player.Player!.GetSpawnPosition(false));
```

## Limits

- The stability, the storm and the rifts are the game's own, so the world, the season and the
  random numbers of the world decide them.
- The rifts open where the world decides, so a test that needs a rift at a certain place waits
  for the world to open one there.
- A rift ward only switches on while it has fuel, and a freshly placed one has none, so
  `SetWard` fills the ward when it switches it on.
- `EndStormAsync` moves the next storm into the future, so the world's own storm tick does not
  start another one at once.
